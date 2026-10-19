// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.Diagnostics;
using System.Xml;
using System.Xml.Schema;
using Xsl3.Qil;
using Xsl3.Runtime;
using Xsl3.XPath31;
using Xsl3.XPath31.Functions;
using Xsl3.Xdm;
using QName = Xsl3.Xdm.QName;
using T = Xsl3.XmlQueryTypeFactory;

namespace Xsl3.Xslt
{
    // Xsl3: XSLT 2.0 compilation (SPEC §22, P2; docs/plan/p2-xslt20.md). When the principal stylesheet is 2.0 or later
    // (Compiler.Version == 2) every expression compiles with the XPath 3.1 front end, values are boxed item*
    // sequences, and the 2.0 rules for sequence constructors apply. 1.0 stylesheets never reach this code.
    internal sealed partial class QilGenerator : IXPath31Environment
    {
        private bool IsV2 => _compiler.Version == 2;

        // True while compiling an xsl:function body: the focus is absent there (XPDY0002).
        private bool _noFocus;

        // The innermost xsl:analyze-string segment, for regex-group().
        private QilNode? _currentMatch;

        private DecimalFormatTable? _decimalFormats31;

        private const string FnNs = "http://www.w3.org/2005/xpath-functions";

        // Every template and mode gets the full focus (current, position, last) as implicit arguments.
        private void SetFullFocusV2(StylesheetLevel level)
        {
            foreach (ProtoTemplate t in _compiler.AllTemplates)
            {
                t.Flags |= XslFlags.FullFocus;
            }
            void Visit(StylesheetLevel sheet)
            {
                foreach (QilName mode in new List<QilName>(sheet.ModeFlags.Keys))
                {
                    sheet.ModeFlags[mode] |= XslFlags.FullFocus;
                }
                if (sheet.Imports != null)
                {
                    foreach (Stylesheet import in sheet.Imports)
                    {
                        Visit(import);
                    }
                }
            }
            Visit(level);
        }

        // ------------------------------------------------------------ the XPath 3.1 environment

        // In a function body the focus is absent, except inside instructions that set one (xsl:for-each, …).
        private bool FocusAbsent => _noFocus && !_curLoop.IsFocusSet && _nodeContext31 == null;

        QilNode? IXPath31Environment.ContextItem() => FocusAbsent ? null : GetCurrentNode();

        QilNode IXPath31Environment.ContextPosition() =>
            FocusAbsent ? X31(nameof(XsltLibrary.X31RaiseDouble), T.DoubleX, _f.String("XPDY0002"), _f.String("The focus is absent in a function body")) : GetCurrentPosition();

        QilNode IXPath31Environment.ContextLast() =>
            FocusAbsent ? X31(nameof(XsltLibrary.X31RaiseDouble), T.DoubleX, _f.String("XPDY0002"), _f.String("The focus is absent in a function body")) : GetLastPosition();

        QilNode? IXPath31Environment.ResolveVariable(QName name) => _scope.LookupVariable(name.LocalName, name.NamespaceUri);

        QilNode? IXPath31Environment.ResolveFunction(QName name, IReadOnlyList<XPath31.Expr> args, System.Func<XPath31.Expr, QilNode> compile)
        {
            // Stylesheet functions (xsl:function) first: they may not override F&O functions, but have their own namespaces.
            if (_compiler.Functions.TryGetValue((_f.QName(name.LocalName, name.NamespaceUri), args.Count), out var entry))
            {
                QilList actual = _f.ActualParameterList();
                for (int i = 0; i < args.Count; i++)
                {
                    actual.Add(compile(args[i]));
                }
                return _f.Invoke(entry.Function.Function!, actual);
            }
            if (name.NamespaceUri != FnNs)
            {
                return null;
            }
            switch (name.LocalName, args.Count)
            {
                case ("current", 0):
                    return _patternNode31 ?? (FocusAbsent ? null : GetCurrentNode());
                case ("current-group", 0) when _patternNode31 != null:
                    return ReportXPath31Error("XTSE1060", "current-group() can't be used in a pattern");
                case ("current-grouping-key", 0) when _patternNode31 != null:
                    return ReportXPath31Error("XTSE1070", "current-grouping-key() can't be used in a pattern");
                case ("unparsed-entity-uri" or "unparsed-entity-public-id", 1) when FocusAbsent:
                    return X31(nameof(XsltLibrary.X31Raise), T.ItemS, _f.String("XTDE1380"), _f.String($"{name.LocalName}(): there is no context node"));
                case ("current-group", 0):
                    return _currentGroupFactory?.Invoke() ?? X31(nameof(XsltLibrary.X31Raise), T.ItemS, _f.String("XTDE1061"), _f.String("current-group() is absent"));
                case ("current-grouping-key", 0):
                    return _currentGroupingKeyFactory?.Invoke() ?? X31(nameof(XsltLibrary.X31Raise), T.ItemS, _f.String("XTDE1071"), _f.String("current-grouping-key() is absent"));
                case ("regex-group", 1):
                    return _currentMatch == null
                        ? X31(nameof(XsltLibrary.X31StringItem), T.Item, _f.String(""))
                        : X31(nameof(XsltLibrary.X31SegmentGroup), T.Item, _currentMatch, compile(args[0]));
                case ("key", 2):
                case ("key", 3):
                    // Without a third argument, the context item of the expression (not of the instruction).
                    return CompileKey31(args[0] is XPath31.StringLiteral s ? _f.String(s.Value) : String31(compile(args[0])),
                        compile(args[1]), compile(args.Count == 3 ? args[2] : new XPath31.ContextItemExpr()), subtree: args.Count == 3);
                case ("document", 1):
                case ("document", 2):
                    return CompileFnDocument(compile(args[0]), args.Count == 2 ? X31(nameof(XsltLibrary.X31Nodes), T.NodeNotRtfS, compile(args[1]), _f.String("XPTY0004")) : null);
                case ("unparsed-entity-uri", 1):
                case ("unparsed-entity-public-id", 1):
                {
                    // The context node must be in a tree rooted at a document node (XTDE1380).
                    QilNode value = name.LocalName == "unparsed-entity-uri" ? CompileUnparsedEntityUri(String31(compile(args[0]))) : _f.String("");
                    return X31(nameof(XsltLibrary.X31StringItem), T.Item, _f.Conditional(
                        X31(nameof(XsltLibrary.X31EntityContext), T.BooleanX, compile(new XPath31.ContextItemExpr())), value, _f.String("")));
                }
                case ("system-property", 1):
                    return CompileSystemProperty31(args[0], compile);
                case ("function-available", 1):
                case ("function-available", 2):
                case ("element-available", 1):
                case ("type-available", 1):
                    return CompileAvailable31(name.LocalName, args, compile);
            }
            return null;
        }

        private QilNode X31(string method, XmlQueryType type, params QilNode[] args) => XPath31Compiler.Invoke(_f, method, type, args);

        // A boxed value's string value, as a native string (the xs:string? argument rules, empty for ()).
        private QilNode String31(QilNode items) => X31(nameof(XsltLibrary.X31String), T.StringX, Box(items));

        private QilNode Box(QilNode n) => XPath31Compiler.Box(n, _f);

        private QilNode Ebv31(QilNode items) => X31(nameof(XsltLibrary.X31Ebv), T.BooleanX, Box(items));

        private QilNode Nodes31(QilNode items, string errorCode) =>
            items.XmlType!.IsSubtypeOf(T.NodeNotRtfS) ? items : X31(nameof(XsltLibrary.X31Nodes), T.NodeNotRtfS, Box(items), _f.String(errorCode));

        private QilNode Node31(QilNode item, string errorCode) =>
            item.XmlType!.IsSubtypeOf(T.NodeNotRtf) ? item
            : item.XmlType.IsSingleton ? X31(nameof(XsltLibrary.X31NodeItem), T.NodeNotRtf, item, _f.String(errorCode))
            : X31(nameof(XsltLibrary.X31SingleNode), T.NodeNotRtf, Box(item), _f.String(errorCode));

        private XPathStaticContext StaticContext31()
        {
            var sc = new XPathStaticContext
            {
                BaseUri = StaticBaseUri31(),
                DefaultElementNamespace = _scope.LookupNamespace(XsltInput.XPathDefaultNamespacePrefix) ?? "",
                DefaultCollation = _scope.LookupNamespace(XsltInput.DefaultCollationPrefix) ?? Collation.CodepointUri,
            };
            // In XSLT the statically known namespaces are the stylesheet's namespaces in scope, and xml (XSLT 3.0 §5.4.1).
            sc.Namespaces.Clear();
            sc.Namespaces["xml"] = XmlReservedNs.NsXml;
            foreach (CompilerScopeManager<QilIterator>.ScopeRecord rec in _scope)
            {
                if (rec.ncName != null && rec.nsUri != null && rec.ncName.Length > 0 && rec.ncName[0] != '#')
                {
                    sc.Namespaces.TryAdd(rec.ncName, rec.nsUri);
                }
            }
            _decimalFormats31 ??= DecimalFormats31();
            sc.DefaultDecimalFormat = _decimalFormats31.Unnamed;
            foreach (DecimalFormatDecl d in _compiler.DecimalFormats)
            {
                if (!d.Name.IsEmpty)
                {
                    sc.DecimalFormats[new QName(d.Name.Namespace, d.Name.Name)] = ToXPathDecimalFormat(d);
                }
            }
            return sc;
        }


        // The static base URI: the module's URI, or the effective xml:base (XsltInput.XmlBasePrefix).
        private string? StaticBaseUri31() => _scope.LookupNamespace(XsltInput.XmlBasePrefix) ?? _lastScope?.SourceLine?.Uri;

        // A temporary tree (XSLT 3.0 §9.4): a document node whose base URI is the static base URI.
        private QilNode TemporaryTree31(QilNode content) =>
            _f.XsltConvert(_f.RtfCtor(ContentNodes(content), _f.String(StaticBaseUri31() ?? "")), T.NodeNotRtf);

        private DecimalFormatTable DecimalFormats31()
        {
            XPathDecimalFormat unnamed = XPathDecimalFormat.Default;
            foreach (DecimalFormatDecl d in _compiler.DecimalFormats)
            {
                if (d.Name.IsEmpty)
                {
                    unnamed = ToXPathDecimalFormat(d);
                }
            }
            return new DecimalFormatTable(unnamed, new Dictionary<QName, XPathDecimalFormat>(), null);
        }

        private static XPathDecimalFormat ToXPathDecimalFormat(DecimalFormatDecl d) => new()
        {
            DecimalSeparator = d.Characters[0],
            GroupingSeparator = d.Characters[1],
            Percent = d.Characters[2],
            PerMille = d.Characters[3],
            ZeroDigit = d.Characters[4],
            Digit = d.Characters[5],
            PatternSeparator = d.Characters[6],
            MinusSign = d.Characters[7],
            Infinity = d.InfinitySymbol,
            NaN = d.NanSymbol,
        };

        // ------------------------------------------------------------ compiling expressions

        /// <summary>An XPath 3.1 expression of a 2.0 instruction, boxed (item*).</summary>
        private QilNode CompileXPath31(string? expr)
        {
            if (expr == null)
            {
                return PhantomXPathExpression();
            }
            try
            {
                return XPath31Compiler.CompileEmbedded(Parser.ParseXPath(expr), StaticContext31(), _f, this);
            }
            catch (XPathStaticException e)
            {
                return ReportXPath31Error(e.Code, e.Message);
            }
            catch (XdmException e)
            {
                return ReportXPath31Error(e.Code, e.Message);
            }
        }

        private QilNode ReportXPath31Error(string code, string message)
        {
            if (_xslVersion != XslVersion.ForwardsCompatible)
            {
                ReportError(SR.Xml_UserException, $"{code}: {message}");
            }
            return X31(nameof(XsltLibrary.X31Raise), T.ItemS, _f.String(code), _f.String(message));
        }

        /// <summary>
        /// The expression of an attribute value template (from just after its '{'), as a native string: the items
        /// atomised and joined with single spaces (XSLT 3.0 §5.6.1).
        /// </summary>
        private QilNode CompileAvtExpression31(string source, ref int pos)
        {
            try
            {
                XPath31.Expr? ast = Parser.ParseEnclosedExpr(source, pos, out int end);
                pos = end + 1;
                if (ast == null)
                {
                    return _f.String("");
                }
                return X31(nameof(XsltLibrary.X31AvtString), T.StringX, XPath31Compiler.CompileEmbedded(ast, StaticContext31(), _f, this));
            }
            catch (XPathStaticException e)
            {
                pos = source.Length;
                ReportXPath31Error(e.Code, e.Message);
                return _f.String("");
            }
        }

        // ------------------------------------------------------------ sequence constructors

        /// <summary>
        /// The 2.0 rules for the content of a constructed element or document (XSLT 3.0 §5.7.1): adjacent atomic
        /// values become one text node with single spaces between them, document nodes are replaced by their
        /// children, nodes are copied. Content that is statically nodes only passes through.
        /// </summary>
        private QilNode ContentNodes(QilNode content)
        {
            if (content.XmlType!.IsSubtypeOf(T.NodeNotRtfS) || content.XmlType.IsEmpty)
            {
                return content;
            }
            QilIterator i;
            return _f.Loop(
                i = _f.For(X31(nameof(XsltLibrary.X31ContentItems), T.ItemS, Box(content))),
                _f.Conditional(_f.IsType(i, T.Node),
                    _f.TypeAssert(i, T.NodeNotRtf),
                    _f.TextCtor(_f.XsltConvert(i, T.StringX))
                )
            );
        }

        /// <summary>The string value of simple content (attribute, comment, PI, value-of): atomised items joined by <paramref name="separator"/>.</summary>
        private QilNode SimpleContent31(QilNode items, QilNode separator) =>
            X31(nameof(XsltLibrary.X31SimpleContent), T.StringX, Box(items), separator);

        // The value of xsl:sequence, xsl:value-of and friends: select, else the sequence constructor.
        private QilNode SelectOrContent31(XslNode node, string? select)
        {
            if (select != null)
            {
                return CompileXPath31(select);
            }
            return CompileInstructions(node.Content);
        }

        // ------------------------------------------------------------ instructions

        /// <summary>The 2.0 compilation of an instruction; null if the 1.0 code applies unchanged.</summary>
        private QilNode? CompileInstructionV2(XslNode node)
        {
            switch (node.NodeType)
            {
                case XslNodeType.Sequence:
                    return SelectOrContent31(node, node.Select);
                case XslNodeType.ValueOf:
                case XslNodeType.ValueOfDoe:
                {
                    string? separator = node.V2?.Separator;
                    QilNode sep = separator == null ? _f.String(node.Select != null ? " " : "") : CompileStringAvt(separator)!;
                    QilNode value = SimpleContent31(SelectOrContent31(node, node.Select), sep);
                    return node.NodeType == XslNodeType.ValueOf ? _f.TextCtor(value) : _f.RawTextCtor(value);
                }
                case XslNodeType.CopyOf:
                    return CompileCopyOf31(node);
                case XslNodeType.If:
                    return _f.Conditional(Ebv31(CompileXPath31(node.Select)), CompileInstructions(node.Content), InstructionList());
                case XslNodeType.Choose:
                    return CompileChoose31(node);
                case XslNodeType.ForEach:
                    return WithoutTemplateRule(() => CompileForEach31((XslNodeEx)node));
                case XslNodeType.ApplyTemplates:
                    return CompileApplyTemplates31((XslNodeEx)node);
                case XslNodeType.Attribute:
                    return CompileAttribute31((NodeCtor)node);
                case XslNodeType.Comment:
                    return _f.CommentCtor(_f.TextCtor(SimpleContent31(SelectOrContent31(node, node.V2?.Select), _f.String(node.V2?.Select != null ? " " : ""))));
                case XslNodeType.PI:
                {
                    QilNode qilName = CompileStringAvt(node.Select!);
                    if (qilName.NodeType == QilNodeType.LiteralString)
                    {
                        Compiler.ValidatePiName((string)(QilLiteral)qilName, (IErrorHelper)this);
                    }
                    return _f.PICtor(qilName, _f.TextCtor(SimpleContent31(SelectOrContent31(node, node.V2?.Select), _f.String(node.V2?.Select != null ? " " : ""))));
                }
                case XslNodeType.Message:
                    return CompileMessage31(node);
                case XslNodeType.Copy:
                    return CompileCopy31(node);
                case XslNodeType.Document:
                    return _f.DocumentCtor(ContentNodes(CompileInstructions(node.Content)));
                case XslNodeType.Namespace:
                    return CompileNamespace31(node);
                case XslNodeType.ForEachGroup:
                    return WithoutTemplateRule(() => CompileForEachGroup31((XslNodeEx)node));
                case XslNodeType.AnalyzeString:
                    return WithoutTemplateRule(() => CompileAnalyzeString31(node));
                case XslNodeType.PerformSort:
                    return CompilePerformSort31(node);
                case XslNodeType.NextMatch:
                    return CompileNextMatch31(node);
                case XslNodeType.Number:
                    return CompileNumber31((Number)node);
                case XslNodeType.ResultDocument:
                {
                    // The secondary result is built as a document and handed to the result-document handler; with
                    // no href, its content is part of the principal result.
                    XslNodeV2 v2 = node.V2!;
                    if (_temporaryOutput31 > 0)
                    {
                        return X31(nameof(XsltLibrary.X31Raise), T.ItemS, _f.String("XTDE1480"), _f.String("xsl:result-document can't be evaluated while a temporary tree is being constructed"));
                    }
                    QilNode href = v2.Href != null ? CompileStringAvt(v2.Href)! : _f.String("");
                    // The output definition: format="…" names one (at run time if it is computed); else the unnamed one.
                    Output baseOutput = _compiler.Output;
                    QilNode formatName = _f.String("");
                    if (v2.Format != null)
                    {
                        QilNode format = CompileStringAvt(v2.Format)!;
                        if (format is QilLiteral { NodeType: QilNodeType.LiteralString } literalFormat)
                        {
                            Output? named = ResolveOutputName((string)literalFormat);
                            if (named == null)
                            {
                                return X31(nameof(XsltLibrary.X31Raise), T.ItemS, _f.String("XTDE1460"), _f.String($"There is no output definition named '{(string)literalFormat}'"));
                            }
                            baseOutput = named;
                        }
                        else
                        {
                            formatName = format;
                        }
                    }
                    ResultFormat baseFormat = ResultFormat31(baseOutput);
                    if (v2.UseCharacterMaps != null)
                    {
                        var map = new Dictionary<int, string>(baseFormat.Extras?.CharacterMap ?? new Dictionary<int, string>());
                        foreach (string qname in XmlConvert.SplitString(v2.UseCharacterMaps))
                        {
                            AddCharacterMap(CharacterMapName(qname), map, new HashSet<QilName>());
                        }
                        OutputExtras extras = baseFormat.Extras?.Clone() ?? new OutputExtras();
                        extras.CharacterMap = map;
                        baseFormat = baseFormat with { Extras = extras };
                    }
                    // The serialization attributes, evaluated: name U+0001 value U+0002 …
                    QilList parameters = _f.BaseFactory.Sequence();
                    foreach (var (name, value) in v2.Serialization!)
                    {
                        parameters.Add(_f.String(name + SerializationParameters.NameEnd));
                        parameters.Add(CompileStringAvt(value)!);
                        parameters.Add(_f.String(SerializationParameters.ValueEnd.ToString()));
                    }
                    QilNode doc = _f.DocumentCtor(ContentNodes(WithoutTemplateRule(() => CompileInstructions(node.Content))));
                    return X31(nameof(XsltLibrary.X31ResultDocument), T.ItemS, href, doc, _f.String(StaticBaseUri31() ?? ""),
                        _f.Double(ConstantPool.Add(baseFormat)), formatName, _f.Double(ResultFormats31()),
                        parameters.Count == 0 ? _f.String("") : _f.StrConcat(parameters), _f.Double(NameContext31()));
                }
            }
            return null;
        }

        // ------------------------------------------------------------ parameters

        private static readonly QilName s_nameTunnel = new QilFactory().LiteralQName("tunnel", XmlReservedNs.NsXslDebug, "");

        private QilName _nameTunnel => s_nameTunnel;

        // The tunnel argument of the template being compiled (null: none, at the top level or in a function).
        private QilNode? _currentTunnel;

        // The xsl:param declaration of each template parameter, for 2.0 type conversion.
        private readonly Dictionary<QilParameter, VarPar> _paramDecls = new();

        /// <summary>
        /// The value of the tunnel argument for a call: the current tunnel parameters, plus the call's own
        /// tunnel="yes" with-params, which are removed from <paramref name="args"/> (from <paramref name="from"/>).
        /// </summary>
        private QilNode TunnelFrame31(IList<XslNode> args, int from)
        {
            QilNode frame = _currentTunnel != null ? _currentTunnel : _f.TypeAssert(_f.Sequence(), T.ItemS);
            for (int i = from; i < args.Count;)
            {
                if (args[i] is VarPar { V2.Tunnel: true } p && p.NodeType == XslNodeType.WithParam && p.Value != null)
                {
                    frame = X31(nameof(XsltLibrary.X31TunnelAdd), T.ItemS, frame, _f.String(p.Name!.NamespaceUri), _f.String(p.Name.LocalName), Box(p.Value));
                    args.RemoveAt(i);
                }
                else
                {
                    i++;
                }
            }
            return frame;
        }

        /// <summary>Binds a 2.0 template's tunnel parameters and converts its parameters to their declared types.</summary>
        private List<QilIterator> BindTemplateParams31(ProtoTemplate tmpl)
        {
            var lets = new List<QilIterator>();
            _currentTunnel = null;
            foreach (QilParameter arg in tmpl.Function!.Arguments)
            {
                if (arg.Name!.Equals(_nameTunnel))
                {
                    _currentTunnel = arg;
                }
                else if (_paramDecls.TryGetValue(arg, out VarPar? decl) && decl.V2?.As is string asType)
                {
                    QilIterator let = _f.Let(Coerce31(arg, asType, "XTTE0590"));
                    lets.Add(let);
                    _scope.AddVariable(decl.Name!, let);
                }
            }
            foreach (XslNode node in tmpl.Content)
            {
                if (node is VarPar { V2.Tunnel: true } p && p.NodeType == XslNodeType.Param)
                {
                    // The value from the tunnel argument if it has one by this name, else the default.
                    EnterScope(p);
                    QilNode defaultValue = p.V2!.Required
                        ? X31(nameof(XsltLibrary.X31Raise), T.ItemS, _f.String("XTDE0700"), _f.String($"The required tunnel parameter ${p.Name!.QualifiedName} was not supplied"))
                        : CompileVarParValue(p);
                    ExitScope();
                    QilNode frame = _currentTunnel ?? (QilNode)_f.TypeAssert(_f.Sequence(), T.ItemS);
                    QilNode value = _f.Conditional(
                        X31(nameof(XsltLibrary.X31TunnelHas), T.BooleanX, frame, _f.String(p.Name!.NamespaceUri), _f.String(p.Name.LocalName)),
                        X31(nameof(XsltLibrary.X31TunnelGet), T.ItemS, _currentTunnel ?? (QilNode)_f.TypeAssert(_f.Sequence(), T.ItemS), _f.String(p.Name.NamespaceUri), _f.String(p.Name.LocalName)),
                        Box(defaultValue));
                    if (p.V2.As is string asType)
                    {
                        value = Coerce31(value, asType, "XTTE0590");
                    }
                    QilIterator let = _f.Let(value);
                    lets.Add(let);
                    _scope.AddVariable(p.Name, let);
                }
            }
            return lets;
        }

        private QilNode FinishTemplate31(ProtoTemplate tmpl, QilNode body, List<QilIterator> prologue)
        {
            if (tmpl.V2?.As is string asType)
            {
                body = _f.TypeAssert(Box(Coerce31(body, asType, "XTTE0505")), T.ItemS);
            }
            for (int i = prologue.Count - 1; i >= 0; i--)
            {
                body = _f.Loop(prologue[i], body);
            }
            return body;
        }

        // While compiling xsl:number's counting: the context node, which 1.0's PlaceMarker code expects typed as a node.
        private QilIterator? _nodeContext31;

        private QilNode CompileNumber31(Number num)
        {
            string? value = num.Value ?? num.V2?.Select;
            QilNode format = CompileStringAvt(num.Format)!;
            QilNode groupingSeparator = num.GroupingSeparator != null ? CompileStringAvt(num.GroupingSeparator)! : _f.String("");
            QilNode groupingSize = num.GroupingSize != null ? CompileStringAvt(num.GroupingSize)! : _f.String("");
            QilNode ordinal = num.V2?.Ordinal != null ? CompileStringAvt(num.V2.Ordinal)! : _f.String("");
            QilNode lang = num.Lang != null ? CompileStringAvt(num.Lang)! : _f.String("");
            if (num.Value != null)
            {
                // value="…": the numbers are formatted with the format tokens (XSLT 3.0 §12.3).
                return _f.TextCtor(X31(nameof(XsltLibrary.X31NumberFormat), T.StringX,
                    Box(CompileXPath31(value)), format, groupingSeparator, groupingSize, ordinal, lang));
            }

            // Counting nodes: the 1.0 machinery, on a node-typed context (select="…" in 2.0, else the context item).
            QilNode context = num.V2?.Select != null ? CompileXPath31(num.V2.Select) : GetCurrentNode();
            QilIterator node = _f.Let(Node31(context, "XTTE0990"));
            _nodeContext31 = node;
            QilNode? countPattern = num.Count != null ? CompileNumberPattern(num.Count) : null;
            QilNode? fromPattern = num.From != null ? CompileNumberPattern(num.From) : null;
            QilNode counted = num.Level switch
            {
                NumberLevel.Single => PlaceMarker31(countPattern, fromPattern, false),
                NumberLevel.Multiple => PlaceMarker31(countPattern, fromPattern, true),
                _ => PlaceMarkerAny31(countPattern, fromPattern),
            };
            _nodeContext31 = null;
            QilIterator n;
            QilNode numbers = _f.Loop(n = _f.For(counted), X31(nameof(XsltLibrary.X31IntegerItem), T.Item, _f.XsltConvert(n, T.DoubleX)));
            return _f.Loop(node, _f.TextCtor(X31(nameof(XsltLibrary.X31NumberFormat), T.StringX,
                numbers, format, groupingSeparator, groupingSize, ordinal, lang)));
        }

        /// <summary>
        /// xsl:number level="single" and "multiple" (XSLT 3.0 §12.3): $A is ancestor-or-self::node()[matches-count(.)]
        /// (its first only, for single), $F is ancestor-or-self::node()[matches-from(.)][1], where the root always
        /// matches from, and the numbers are 1 + count($a/preceding-sibling::node()[matches-count(.)]) for each $a of
        /// $A that is $F or a descendant of it.
        /// </summary>
        private QilNode PlaceMarker31(QilNode? countPattern, QilNode? fromPattern, bool multiple)
        {
            QilIterator i, j;
            QilNode? countPattern2 = countPattern?.DeepClone(_f.BaseFactory);
            QilNode countMatches = _f.Filter(i = _f.For(_f.AncestorOrSelf(GetCurrentNode())), MatchCountPattern(countPattern, i));
            QilNode A = multiple ? _f.DocOrderDistinct(countMatches) : _f.Filter(i = _f.For(countMatches), _f.Eq(_f.PositionOf(i), _f.Int32(1)));
            QilNode AF;
            if (fromPattern == null)
            {
                AF = A;
            }
            else
            {
                QilNode fromMatches = _f.Filter(i = _f.For(_f.AncestorOrSelf(GetCurrentNode())),
                    _f.Or(_f.Is(i, _f.Root(GetCurrentNode())), MatchPattern(fromPattern, i)));
                QilNode F = _f.Filter(i = _f.For(fromMatches), _f.Eq(_f.PositionOf(i), _f.Int32(1)));
                AF = _f.Loop(i = _f.For(F), _f.Filter(j = _f.For(A), _f.Or(_f.Is(i, j), _f.Before(i, j))));
            }
            return _f.Loop(j = _f.For(AF),
                _f.Add(_f.Int32(1), _f.Length(_f.Filter(i = _f.For(_f.PrecedingSibling(j)), MatchCountPattern(countPattern2, i)))));
        }

        /// <summary>
        /// xsl:number level="any" (XSLT 3.0 §12.3): with $A the nodes up to the context node (in document order)
        /// that match count, and $F the last node up to the context node that matches from or is the root, the
        /// number is count($A[. is $F or . >> $F]), or () if that is zero.
        /// </summary>
        private QilNode PlaceMarkerAny31(QilNode? countPattern, QilNode? fromPattern)
        {
            QilIterator i, k, m, f;
            QilNode AF;
            if (fromPattern == null)
            {
                AF = _f.Filter(i = _f.For(_f.NodeRange(_f.Root(GetCurrentNode()), GetCurrentNode())), MatchCountPattern(countPattern, i));
            }
            else
            {
                QilIterator froms = _f.Let(_f.Filter(i = _f.For(_f.NodeRange(_f.Root(GetCurrentNode()), GetCurrentNode())),
                    _f.Or(_f.Is(i, _f.Root(GetCurrentNode())), MatchPattern(fromPattern, i))));
                QilNode F = _f.Filter(f = _f.For(froms), _f.Eq(_f.PositionOf(f), _f.Length(froms)));
                AF = _f.Loop(froms, _f.Loop(m = _f.For(F),
                    _f.Filter(k = _f.For(_f.NodeRange(m, GetCurrentNode())), MatchCountPattern(countPattern, k))));
            }
            return _f.Loop(k = _f.Let(_f.Length(AF)), _f.Conditional(_f.Eq(k, _f.Int32(0)), _f.Sequence(), k));
        }

        // The template rule whose body is being compiled, for mode="#current" (unlike _currentTemplateRule, it stays
        // set inside xsl:for-each).
        private Template? _modeRule31;

        private QilName CurrentMode31() =>
            _modeRule31 is not Template rule ? XsltLoader.nullMode
            : rule.V2?.Modes is { Count: > 0 } modes ? modes[0]
            : rule.V2?.AllModes == true ? XsltLoader.nullMode
            : rule.Mode;

        // The template rule whose body is being compiled, for xsl:next-match; null where it is absent.
        private Template? _currentTemplateRule;

        private readonly Dictionary<Template, List<QilFunction>> _nextMatchFunctions = new();

        // Inside xsl:for-each, xsl:for-each-group and xsl:analyze-string the current template rule is absent.
        private QilNode WithoutTemplateRule(System.Func<QilNode> compile)
        {
            Template? rule = _currentTemplateRule;
            _currentTemplateRule = null;
            QilNode result = compile();
            _currentTemplateRule = rule;
            return result;
        }

        private QilNode CompileNextMatch31(XslNode node)
        {
            if (_currentTemplateRule == null)
            {
                return X31(nameof(XsltLibrary.X31Raise), T.ItemS, _f.String("XTDE0560"), _f.String("xsl:next-match is used where there is no current template rule"));
            }
            int varScope = _varHelper.StartVariables();
            var args = new List<XslNode>();
            foreach (XslNode n in node.Content)
            {
                if (n is VarPar withParam)
                {
                    CompileWithParam(withParam);
                    QilNode? val = withParam.Value;
                    if (!(val is QilIterator || val is QilLiteral))
                    {
                        QilIterator let = _f.Let(val!);
                        _varHelper.AddVariable(let);
                        withParam.Value = let;
                    }
                    args.Add(withParam);
                }
            }
            QilNode result = InvokeApplyFunction(_compiler.Root!, _currentTemplateRule.Mode, args, nextMatchBelow: _currentTemplateRule);
            return _varHelper.FinishVariables(result, varScope);
        }

        private QilNode CompileChoose31(XslNode node)
        {
            IList<XslNode> cases = node.Content;
            QilNode? result = null;
            for (int i = cases.Count - 1; 0 <= i; i--)
            {
                XslNode when = cases[i];
                QilList? nsList = EnterScope(when);
                result = when.NodeType == XslNodeType.Otherwise
                    ? CompileInstructions(when.Content)
                    : _f.Conditional(Ebv31(CompileXPath31(when.Select)), CompileInstructions(when.Content), result ?? InstructionList());
                ExitScope();
                SetLineInfoCheck(result, when.SourceLine);
                result = SetDebugNs(result, nsList);
            }
            return result ?? _f.Sequence();
        }

        // xsl:copy-of: nodes are deep-copied, atomic values pass through.
        private QilNode CompileCopyOf31(XslNode node)
        {
            QilNode select = CompileXPath31(node.Select);
            _outputScope.InvalidateAllPrefixes();
            QilIterator it;
            if (select.XmlType!.IsSubtypeOf(T.NodeS))
            {
                return _f.Loop(it = _f.For(select), _f.XsltCopyOf(it));
            }
            return _f.Loop(
                it = _f.For(select),
                _f.Conditional(_f.IsType(it, T.Node),
                    _f.XsltCopyOf(_f.TypeAssert(it, T.Node)),
                    _f.TypeAssert(it, T.AnyAtomicType)
                )
            );
        }

        private QilNode CompileForEach31(XslNodeEx node)
        {
            IList<XslNode> content = node.Content;
            LoopFocus curLoopSaved = _curLoop;
            QilIterator it = _f.For(CompileXPath31(node.Select));
            _curLoop.SetFocus(it);
            int varScope = _varHelper.StartVariables();
            _curLoop.Sort(CompileSorts(content, ref curLoopSaved));
            QilNode result = CompileInstructions(content);
            result = WrapLoopBody(node.ElemNameLi, result, node.EndTagLi);
            result = AddCurrentPositionLast(result);
            result = _curLoop.ConstructLoop(result);
            result = _varHelper.FinishVariables(result, varScope);
            _curLoop = curLoopSaved;
            return result;
        }

        private QilNode CompileApplyTemplates31(XslNodeEx node)
        {
            IList<XslNode> content = node.Content;
            int varScope = _varHelper.StartVariables();
            QilIterator select = _f.Let(Nodes31(CompileXPath31(node.Select), "XTTE0520"));
            _varHelper.AddVariable(select);
            for (int i = 0; i < content.Count; i++)
            {
                if (content[i] is VarPar withParam)
                {
                    CompileWithParam(withParam);
                    QilNode? val = withParam.Value;
                    if (IsDebug || !(val is QilIterator || val is QilLiteral))
                    {
                        QilIterator let = _f.Let(val!);
                        _varHelper.AddVariable(let);
                        withParam.Value = let;
                    }
                }
            }
            LoopFocus curLoopSaved = _curLoop;
            QilIterator it = _f.For(select);
            _curLoop.SetFocus(it);
            _curLoop.Sort(CompileSorts(content, ref curLoopSaved));
            QilNode result = GenerateApply(_compiler.Root!, node);
            result = WrapLoopBody(node.ElemNameLi, result, node.EndTagLi);
            result = AddCurrentPositionLast(result);
            result = _curLoop.ConstructLoop(result);
            _curLoop = curLoopSaved;
            return _varHelper.FinishVariables(result, varScope);
        }

        private QilNode CompileAttribute31(NodeCtor node)
        {
            QilNode? qilNs = CompileStringAvt(node.NsAvt);
            QilNode qilName = CompileStringAvt(node.NameAvt);
            QilNode qname;
            if (qilName.NodeType == QilNodeType.LiteralString && (qilNs == null || qilNs.NodeType == QilNodeType.LiteralString))
            {
                string name = (string)(QilLiteral)qilName;
                bool isValid = _compiler.ParseQName(name, out string prefix, out string local, (IErrorHelper)this);
                string nsUri = qilNs == null ? (isValid ? ResolvePrefix(/*ignoreDefaultNs:*/true, prefix) : _compiler.CreatePhantomNamespace()) : (string)(QilLiteral)qilNs;
                if (name == "xmlns" || local == "xmlns" && nsUri.Length == 0)
                {
                    ReportError(/*[XTDE0850]*/SR.Xslt_XmlnsAttr, "name", name);
                }
                qname = _f.QName(local, nsUri, prefix);
            }
            else
            {
                qname = qilNs != null ? _f.StrParseQName(qilName, qilNs) : ResolveQNameDynamic(/*ignoreDefaultNs:*/true, qilName);
            }
            _outputScope.InvalidateAllPrefixes();
            string? select = node.V2?.Select;
            string? separator = node.V2?.Separator;
            QilNode sep = separator != null ? CompileStringAvt(separator)! : _f.String(select != null ? " " : "");
            return _f.AttributeCtor(qname, _f.TextCtor(SimpleContent31(SelectOrContent31(node, select), sep)));
        }

        private QilNode CompileMessage31(XslNode node)
        {
            // The message is the content (select is inserted as a copy-of by the loader), serialized.
            string? baseUri = _lastScope!.SourceLine!.Uri;
            QilNode content = _f.RtfCtor(ContentNodes(CompileInstructions(node.Content)), _f.String(baseUri));
            content = _f.InvokeOuterXml(content);
            QilIterator i;
            if (node.V2?.NameAvt is string terminateAvt)
            {
                // terminate="{…}": decided when the message is output.
                QilIterator t = _f.Let(X31(nameof(XsltLibrary.X31YesNo), T.BooleanX, CompileStringAvt(terminateAvt)!, _f.String("terminate")));
                return _f.Loop(t, _f.Loop(i = _f.Let(content), _f.Conditional(t, _f.Sequence(_f.Warning(i), _f.Error(i)), _f.Warning(i))));
            }
            if (!(bool)node.Arg!)
            {
                return _f.Warning(content);
            }
            return _f.Loop(i = _f.Let(content), _f.Sequence(_f.Warning(i), _f.Error(i)));
        }

        private QilNode CompileCopy31(XslNode copy)
        {
            QilNode current = GetCurrentNode();
            _outputScope.InvalidateAllPrefixes();
            if (current.XmlType!.IsSubtypeOf(T.NodeNotRtf))
            {
                return _f.XsltCopy(current, ContentNodes(CompileInstructions(copy.Content)));
            }
            QilIterator c;
            // An atomic context item is copied as itself; a node with XsltCopy.
            return _f.Loop(c = _f.Let(current),
                _f.Conditional(_f.IsType(c, T.Node),
                    _f.XsltCopy(_f.TypeAssert(c, T.NodeNotRtf), ContentNodes(CompileInstructions(copy.Content))),
                    _f.TypeAssert(c, T.AnyAtomicType)));
        }

        private QilNode CompileNamespace31(XslNode node)
        {
            QilNode prefix = CompileStringAvt(node.V2!.NameAvt!)!;
            QilNode uri = SimpleContent31(SelectOrContent31(node, node.Select), _f.String(node.Select != null ? " " : ""));
            _outputScope.InvalidateAllPrefixes();
            return _f.NamespaceDecl(prefix, uri);
        }

        // ------------------------------------------------------------ variables

        /// <summary>
        /// The value of a 2.0 variable or parameter (XSLT 3.0 §9.3): select, else content; with no <c>as</c>, content
        /// makes a temporary tree (a document node); with <c>as</c>, the value is the sequence converted to the type.
        /// </summary>
        private QilNode CompileVarParValue31(XslNode node)
        {
            IList<XslNode> content = node.Content;
            string? select = node.Select;
            string? asType = node.V2?.As;
            QilNode value;
            if (select != null)
            {
                value = CompileXPath31(select);
            }
            else if (content.Count != 0)
            {
                _outputScope.PushScope();
                _outputScope.InvalidateAllPrefixes();
                _temporaryOutput31++;
                QilNode body = CompileInstructions(content);
                _temporaryOutput31--;
                value = asType == null ? TemporaryTree31(body) : Box(body);
                _outputScope.PopScope();
            }
            else
            {
                value = asType == null
                    ? X31(nameof(XsltLibrary.X31StringItem), T.Item, _f.String(""))
                    : (QilNode)_f.TypeAssert(_f.Sequence(), T.ItemS);
            }
            if (asType != null)
            {
                value = Coerce31(value, asType, node.NodeType == XslNodeType.Variable ? "XTTE0570" : "XTTE0590");
            }
            return _f.TypeAssert(Box(value), T.ItemS);
        }

        /// <summary>The function conversion rules to the sequence type <paramref name="asType"/> (an <c>as</c> attribute).</summary>
        private QilNode Coerce31(QilNode value, string asType, string errorCode)
        {
            try
            {
                SequenceType type = Parser.ParseSequenceType(asType);
                XPathStaticContext sc = StaticContext31();
                var resolver = new StaticTypeResolver(p => sc.Namespaces.TryGetValue(p, out string? uri) ? uri : null, sc.DefaultElementNamespace);
                SequenceTypeTest test = resolver.Resolve(type);
                return X31(nameof(XsltLibrary.X31Coerce), T.ItemS, Box(value), _f.Double(ConstantPool.Add(test)), _f.String(errorCode));
            }
            catch (XPathStaticException e)
            {
                return ReportXPath31Error(e.Code, e.Message);
            }
        }

        // ------------------------------------------------------------ XSLT functions

        // While compiling a variable's content: a temporary tree, where xsl:result-document is an error (XTDE1480).
        private int _temporaryOutput31;

        private Output? ResolveOutputName(string lexical)
        {
            lexical = lexical.Trim();
            int colon = lexical.IndexOf(':');
            string? ns = colon < 0 ? "" : _scope.LookupNamespace(lexical[..colon]);
            if (ns == null)
            {
                return null;
            }
            QilName name = _f.QName(colon < 0 ? lexical : lexical[(colon + 1)..], ns);
            return _compiler.NamedOutputs.TryGetValue(name, out Output? output) ? output : null;
        }

        // xsl:call-template: a non-tunnel xsl:with-param must name a parameter of the template (XTSE0680).
        private void CheckWithParams31(IList<XslNode> withParams, Template tmpl)
        {
            foreach (XslNode withParam in withParams)
            {
                if (withParam.NodeType != XslNodeType.WithParam || withParam.V2?.Tunnel == true)
                {
                    continue;
                }
                bool declared = false;
                foreach (XslNode n in tmpl.Content)
                {
                    if (n.NodeType == XslNodeType.Param && n.V2?.Tunnel != true && n.Name!.Equals(withParam.Name))
                    {
                        declared = true;
                        break;
                    }
                }
                if (!declared)
                {
                    ReportError(SR.Xml_UserException, $"XTSE0680: the template {tmpl.Name!.QualifiedName} has no parameter {withParam.Name!.QualifiedName}");
                }
            }
        }

        // The character maps refer only to maps that exist (XTSE1590) and not to themselves (XTSE1600).
        private void ValidateCharacterMaps31()
        {
            foreach (CharacterMap map in _compiler.CharacterMaps.Values)
            {
                CheckCharacterMap(map, new HashSet<QilName>());
            }

            void CheckCharacterMap(CharacterMap map, HashSet<QilName> active)
            {
                if (!active.Add(map.Name!))
                {
                    ReportError(SR.Xml_UserException, $"XTSE1600: character map {map.Name!.QualifiedName} uses itself");
                    return;
                }
                foreach (QilName used in map.UseCharacterMaps)
                {
                    if (_compiler.CharacterMaps.TryGetValue(used, out CharacterMap? next))
                    {
                        if (active.Count < 64)
                        {
                            CheckCharacterMap(next, active);
                        }
                    }
                    else
                    {
                        ReportError(SR.Xml_UserException, $"XTSE1590: there is no character map named {used.QualifiedName}");
                    }
                }
                active.Remove(map.Name!);
            }
        }

        private QilName CharacterMapName(string lexical)
        {
            int colon = lexical.IndexOf(':');
            string ns = colon < 0 ? "" : _scope.LookupNamespace(lexical[..colon]) ?? "";
            return _f.QName(colon < 0 ? lexical : lexical[(colon + 1)..], ns);
        }

        private ResultFormat ResultFormat31(Output output) =>
            new(output.Settings, OutputExtras31(output), output.Settings.OutputMethod == XmlOutputMethod.Text);

        private int? _resultFormats31;

        // The named output definitions, for a computed format="…".
        private int ResultFormats31()
        {
            if (_resultFormats31 == null)
            {
                var formats = new Dictionary<(string, string), ResultFormat>();
                foreach (var (name, output) in _compiler.NamedOutputs)
                {
                    formats[(name.NamespaceUri, name.LocalName)] = ResultFormat31(output);
                }
                _resultFormats31 = ConstantPool.Add(formats);
            }
            return _resultFormats31.Value;
        }

        // ------------------------------------------------------------ serialization

        /// <summary>The serialization parameters of an output definition that the result writer must apply.</summary>
        private OutputExtras? OutputExtras31(Output output)
        {
            var extras = new OutputExtras
            {
                Xhtml = output.Xhtml,
                IncludeContentType = output.IncludeContentType ?? true,
                Encoding = output.Encoding ?? "UTF-8",
                MediaType = output.MediaTypePrec != Output.NeverDeclaredPrec && output.Settings.MediaType != null ? output.Settings.MediaType : "text/html",
            };
            if (output.NormalizationForm != null && OutputExtras.TryParseNormalizationForm(output.NormalizationForm, out System.Text.NormalizationForm? form))
            {
                extras.Normalization = form;
            }
            if (output.UseCharacterMaps != null)
            {
                var map = new Dictionary<int, string>();
                foreach (QilName name in output.UseCharacterMaps)
                {
                    AddCharacterMap(name, map, new HashSet<QilName>());
                }
                extras.CharacterMap = map;
            }
            return extras.IsEmpty && output.Encoding == null ? null : extras;
        }

        // A character map and the maps it uses (those first: its own characters take precedence).
        private void AddCharacterMap(QilName name, Dictionary<int, string> map, HashSet<QilName> active)
        {
            if (!_compiler.CharacterMaps.TryGetValue(name, out CharacterMap? characterMap))
            {
                ReportError(SR.Xml_UserException, $"XTSE1590: there is no character map named {name.QualifiedName}");
                return;
            }
            if (!active.Add(name))
            {
                return; // reported by ValidateCharacterMaps31
            }
            foreach (QilName used in characterMap.UseCharacterMaps)
            {
                if (_compiler.CharacterMaps.ContainsKey(used))
                {
                    AddCharacterMap(used, map, active);
                }
            }
            active.Remove(name);
            foreach (var (c, s) in characterMap.Characters)
            {
                map[c] = s;
            }
        }

        // ------------------------------------------------------------ patterns

        // The node a pattern is matched against, for current() in the pattern (XSLT 3.0 §5.5.3).
        private QilIterator? _patternNode31;

        /// <summary>
        /// A pattern as the 1.0 pattern builder delivers it: a Filter over the fixup node per alternative (a
        /// Sequence of them if there are several), each annotated with its default priority.
        /// </summary>
        private QilNode CompilePattern31(string pattern)
        {
            QilIterator? saved = _patternNode31;
            try
            {
                List<PatternAlternative> alternatives = XPath31Compiler.CompilePattern(Parser.ParseXPath(pattern), StaticContext31(), _f, this,
                    () => _patternNode31 = _f.For(_ptrnBuilder.FixupNode));
                QilList list = _f.BaseFactory.Sequence();
                foreach (PatternAlternative a in alternatives)
                {
                    QilNode filter = _f.BaseFactory.Filter(a.Node, a.Condition);
                    XPathPatternBuilder.SetPriority(filter, a.Priority);
                    list.Add(filter);
                }
                return list.Count == 1 ? list[0] : list;
            }
            catch (XPathStaticException e)
            {
                return PatternError(e.Code == "XPST0003" ? "XTSE0340" : e.Code, e.Message);
            }
            catch (XdmException e)
            {
                return PatternError(e.Code, e.Message);
            }
            finally
            {
                _patternNode31 = saved;
            }
        }

        private QilNode PatternError(string code, string message)
        {
            ReportXPath31Error(code, message);
            QilNode result = _f.Loop(_f.For(_ptrnBuilder.FixupNode), _f.Error(_f.String($"{code}: {message}")));
            XPathPatternBuilder.SetPriority(result, 0.5);
            return result;
        }

        /// <summary>
        /// The key functions (context node, key value) => nodes, in the form the optimizer indexes (AnnotateIndex1).
        /// In 2.0 a key's match and use may call key() for other keys, so every function exists before any body is
        /// compiled.
        /// </summary>
        private readonly List<(Key Key, QilParameter Context, QilParameter Value)> _pendingKeys31 = new();

        // The collation of each key, by name: key values are compared with it.
        private readonly Dictionary<QilName, string> _keyCollations31 = new();

        private string KeyCollation31(QilNode name)
        {
            if (name.NodeType == QilNodeType.LiteralString)
            {
                string lexical = (string)(QilLiteral)name;
                int colon = lexical.IndexOf(':');
                string? ns = colon < 0 ? "" : _scope.LookupNamespace(lexical[..colon]);
                if (ns != null && _keyCollations31.TryGetValue(_f.QName(colon < 0 ? lexical : lexical[(colon + 1)..], ns), out string? c))
                {
                    return c;
                }
            }
            return Collation.CodepointUri;
        }

        private void CreateKeyFunctions31()
        {
            List<(Key Key, QilParameter Context, QilParameter Value)> pending = _pendingKeys31;
            for (int idx = 0; idx < _compiler.Keys.Count; idx++)
            {
                // The definitions of a key must agree on the collation (XTSE1220), which must be supported (XTSE1210).
                // A definition without collation="…" uses the default collation in scope.
                string? first = null;
                foreach (Key key in _compiler.Keys[idx])
                {
                    EnterScope(key);
                    string collation = key.V2?.Collation ?? _scope.LookupNamespace(XsltInput.DefaultCollationPrefix) ?? Collation.CodepointUri;
                    ExitScope();
                    _keyCollations31[key.Name!] = collation;
                    if (collation.Length > 0)
                    {
                        try
                        {
                            Collation.Resolve(collation);
                        }
                        catch (XdmException)
                        {
                            EnterScope(key);
                            ReportError(SR.Xml_UserException, $"XTSE1210: the collation '{collation}' of xsl:key isn't supported");
                            ExitScope();
                        }
                    }
                    if (first == null)
                    {
                        first = collation;
                    }
                    else if (first != collation)
                    {
                        EnterScope(key);
                        ReportError(SR.Xml_UserException, $"XTSE1220: the definitions of key {key.Name!.QualifiedName} have different collations");
                        ExitScope();
                    }
                }
                foreach (Key key in _compiler.Keys[idx])
                {
                    QilParameter context = _f.Parameter(T.NodeNotRtf);
                    QilParameter value = _f.Parameter(T.StringX);
                    QilFunction func = _f.Function(_f.FormalParameterList(context, value), _f.False(), T.NodeNotRtfS);
                    func.DebugName = key.GetDebugName();
                    SetLineInfo(func, key.SourceLine);
                    key.Function = func;
                    _functions.Add(func);
                    pending.Add((key, context, value));
                }
            }
        }

        private void CompileKeyBodies31()
        {
            CheckSingletonFocus();
            foreach (var (key, context, value) in _pendingKeys31)
            {
                EnterScope(key);
                _singlFocus.SetFocus(context);
                QilIterator values = _f.For(_f.OptimizeBarrier(CompileKeyMatch31(key.Match, context)));
                _singlFocus.SetFocus(values);
                QilIterator keys = _f.For(CompileKeyUse31(key));
                keys = _f.For(_f.OptimizeBarrier(_f.Loop(keys, _f.ConvertToString(keys))));
                key.Function!.Definition = _f.Filter(values, _f.Not(_f.IsEmpty(_f.Filter(keys, _f.Eq(keys, value)))));
                ExitScope();
            }
            _singlFocus.SetFocus(null);
        }

        // xsl:key's match: the nodes of the context node's tree that match the pattern, in document order.
        private QilNode CompileKeyMatch31(string? pattern, QilNode context)
        {
            if (pattern == null)
            {
                return PhantomKeyMatch();
            }
            QilNode patterns = CompilePattern31(pattern);
            QilIterator e = _f.For(_f.DescendantOrSelf(_f.Root(context)));
            QilNode candidates = e;
            if (pattern.Contains('@') || pattern.Contains('$') || pattern.Contains('(') || pattern.Contains("attribute") || pattern.Contains("node"))
            {
                // A pattern that can match attributes: they follow their element in document order.
                QilIterator a = _f.For(_f.Content(e));
                candidates = _f.Loop(e, _f.Sequence(e, _f.Filter(a, _f.IsType(a, T.Attribute))));
            }
            else
            {
                candidates = _f.Loop(e, e);
            }
            QilIterator n = _f.For(candidates);
            return _f.Filter(n, MatchPattern(patterns, n));
        }

        // xsl:key's use (or content): the key values of the node in focus, as the strings key() compares.
        private QilNode CompileKeyUse31(Key key)
        {
            QilNode values = key.Use != null ? CompileXPath31(key.Use) : CompileInstructions(key.Content);
            return X31(nameof(XsltLibrary.X31KeyValues), T.ItemS, Box(values), _f.String(_keyCollations31.TryGetValue(key.Name!, out string? c) ? c : Collation.CodepointUri));
        }

        private QilNode CompileKey31(QilNode name, QilNode values, QilNode top, bool subtree)
        {
            // key() may use its context node and name once per key definition: they are shared through Lets.
            QilIterator context = _f.Let(Node31(top, "XTDE1270"));
            QilIterator? nameLet = name.NodeType == QilNodeType.LiteralString ? null : _f.Let(name);
            QilIterator v = _f.For(X31(nameof(XsltLibrary.X31KeyValues), T.ItemS, Box(values), _f.String(KeyCollation31(name))));
            QilNode single;
            try
            {
                single = CompileSingleKey(nameLet ?? name, _f.XsltConvert(v, T.StringX), new FixedFocus(context));
            }
            catch (XslLoadException e)
            {
                return ReportXPath31Error("XTDE1260", e.Message);
            }
            QilNode result = _f.DocOrderDistinct(_f.Loop(v, single));
            if (subtree)
            {
                // key(name, values, $top): the nodes that have $top as an ancestor-or-self.
                QilIterator n = _f.For(result), a = _f.For(_f.AncestorOrSelf(n));
                result = _f.Filter(n, _f.Not(_f.IsEmpty(_f.Filter(a, _f.Is(a, context)))));
            }
            // The tree searched must be rooted at a document node (XTDE1270).
            result = _f.Conditional(_f.IsType(_f.Root(context), T.Document), result,
                _f.TypeAssert(X31(nameof(XsltLibrary.X31Raise), T.ItemS, _f.String("XTDE1270"), _f.String("key(): the tree is not rooted at a document node")), T.NodeNotRtfS));
            return _f.Loop(context, nameLet != null ? _f.Loop(nameLet, result) : result);
        }

        // An IFocus with a fixed current node (key()'s context).
        private sealed class FixedFocus(QilNode current) : XPath.IFocus
        {
            public QilNode GetCurrent() => current;

            public QilNode GetPosition() => throw new System.InvalidOperationException();

            public QilNode GetLast() => throw new System.InvalidOperationException();
        }

        // system-property, function-available, element-available, type-available: evaluated at run time against
        // the namespaces in scope (a constant name folds to a constant call).
        private QilNode CompileSystemProperty31(XPath31.Expr arg, System.Func<XPath31.Expr, QilNode> compile) =>
            X31(nameof(XsltLibrary.X31SystemProperty), T.Item, String31(compile(arg)), _f.Double(NameContext31()));

        private QilNode CompileAvailable31(string function, IReadOnlyList<XPath31.Expr> args, System.Func<XPath31.Expr, QilNode> compile)
        {
            QilNode name = String31(compile(args[0]));
            QilNode context = _f.Double(NameContext31());
            return function switch
            {
                "element-available" => X31(nameof(XsltLibrary.X31ElementAvailable), T.Item, name, context),
                "type-available" => X31(nameof(XsltLibrary.X31TypeAvailable), T.Item, name, context),
                _ => X31(nameof(XsltLibrary.X31FunctionAvailable), T.Item, name,
                    args.Count > 1 ? X31(nameof(XsltLibrary.X31Integer), T.DoubleX, Box(compile(args[1]))) : _f.Double(-1), context),
            };
        }

        private HashSet<(string, string, int)>? _stylesheetFunctions31;

        private int NameContext31()
        {
            var namespaces = new Dictionary<string, string>(System.StringComparer.Ordinal) { ["xml"] = XmlReservedNs.NsXml };
            foreach (CompilerScopeManager<QilIterator>.ScopeRecord rec in _scope)
            {
                if (rec.ncName != null && rec.nsUri != null && rec.ncName.Length > 0 && rec.ncName[0] != '#')
                {
                    namespaces.TryAdd(rec.ncName, rec.nsUri);
                }
            }
            if (_stylesheetFunctions31 == null)
            {
                _stylesheetFunctions31 = new HashSet<(string, string, int)>();
                foreach (var (name, arity) in _compiler.Functions.Keys)
                {
                    _stylesheetFunctions31.Add((name.NamespaceUri, name.LocalName, arity));
                }
            }
            return ConstantPool.Add(new XsltNameContext(namespaces, _stylesheetFunctions31));
        }

        // ------------------------------------------------------------ grouping, regex, sorting

        // The context item inside xsl:for-each-group and xsl:analyze-string isn't the loop's own item (a group or a
        // segment) but derived from it: GetCurrentNode() consults these, keyed by the loop iterator.
        private readonly Dictionary<QilIterator, System.Func<QilIterator, QilNode>> _focusOverrides = new();

        private QilNode? FocusOverride()
        {
            if (_curLoop.IsFocusSet && _curLoop.GetCurrent() is QilIterator it && _focusOverrides.TryGetValue(it, out var make))
            {
                return make(it);
            }
            return null;
        }

        /// <summary>The xsl:sort children of an instruction as QIL sort keys (removed from the content), or null.</summary>
        private static int _sortSites31;

        private QilList? CompileSorts31(IList<XslNode> content, ref LoopFocus parentLoop)
        {
            QilList keyList = _f.BaseFactory.SortKeyList();
            int i = 0;
            while (i < content.Count)
            {
                if (content[i] is Sort sort)
                {
                    CompileSort31(sort, keyList, ref parentLoop);
                    content.RemoveAt(i);
                }
                else
                {
                    i++;
                }
            }
            return keyList.Count == 0 ? null : keyList;
        }

        // A 2.0 sort key becomes an order-preserving string (X31SortKey) compared in code point order, so the QIL sort
        // (stable, with descending order) does the rest.
        private void CompileSort31(Sort sort, QilList keyList, ref LoopFocus parentLoop)
        {
            EnterScope(sort);
            QilNode select = sort.Select != null ? CompileXPath31(sort.Select) : CompileInstructions(sort.Content);
            // The keys of one sort must be comparable (XTDE1030): checked from the first item's (position 1) on.
            QilNode position = GetCurrentPosition();

            // The attributes are evaluated in the context of the instruction, not of each item.
            LoopFocus curLoopSaved = _curLoop;
            _curLoop = parentLoop;
            QilNode dataType = sort.DataType != null ? CompileStringAvt(sort.DataType)! : _f.String("");
            QilNode collation = sort.V2?.Collation != null ? CompileStringAvt(sort.V2.Collation)! : _f.String("");
            QilNode lang = sort.Lang != null ? CompileStringAvt(sort.Lang)! : _f.String("");
            QilNode? order = sort.Order != null ? CompileStringAvt(sort.Order) : null;
            _curLoop = curLoopSaved;

            QilNode key = X31(nameof(XsltLibrary.X31SortKey), T.StringX, Box(select), dataType, collation, lang, _f.Double(System.Threading.Interlocked.Increment(ref _sortSites31)), position);
            // The keys are [0-9A-F] strings: any culture orders them as code points do. (The code point collation itself
            // can't make sort keys in the upstream sorter.)
            const string KeyCollation = XmlReservedNs.NsCollationBase + "/en-US";
            QilNode collationUri;
            if (order == null)
            {
                collationUri = _f.String(KeyCollation);
            }
            else if (order.NodeType == QilNodeType.LiteralString)
            {
                string o = (string)(QilLiteral)order;
                if (o != "ascending" && o != "descending" && !sort.ForwardsCompatible)
                {
                    ReportError(/*[XTDE0030]*/SR.Xslt_InvalidAttrValue, "order", o);
                }
                collationUri = _f.String(KeyCollation + (o == "descending" ? "?descendingOrder=1" : ""));
            }
            else
            {
                collationUri = _f.StrConcat(_f.String(KeyCollation),
                    _f.Conditional(_f.Eq(order, _f.String("descending")), _f.String("?descendingOrder=1"), _f.String("")));
            }
            keyList.Add(_f.SortKey(key, collationUri));
            ExitScope();
        }

        private QilNode CompilePerformSort31(XslNode node)
        {
            var content = new List<XslNode>(node.Content);
            QilList sorts = _f.BaseFactory.SortKeyList();
            var body = new List<XslNode>();
            foreach (XslNode n in content)
            {
                if (n.NodeType != XslNodeType.Sort) body.Add(n);
            }
            QilNode input = node.Select != null ? CompileXPath31(node.Select) : CompileInstructions(body);
            LoopFocus curLoopSaved = _curLoop;
            QilIterator it = _f.For(Box(input));
            _curLoop.SetFocus(it);
            _curLoop.Sort(CompileSorts31(content, ref curLoopSaved));
            QilNode result = _curLoop.ConstructLoop(_curLoop.GetCurrent()!);
            _curLoop = curLoopSaved;
            return result;
        }

        private QilNode CompileForEachGroup31(XslNodeEx node)
        {
            XslNodeV2 v2 = node.V2!;
            IList<XslNode> content = node.Content;
            int varScope = _varHelper.StartVariables();

            // 1. The population, each item with its grouping key, evaluated with the item as the focus.
            QilNode population = CompileXPath31(node.Select);
            LoopFocus curLoopSaved = _curLoop;
            QilIterator item = _f.For(Box(population));
            _curLoop.SetFocus(item);
            int kind;
            QilNode key;
            if (v2.GroupBy != null) { kind = 0; key = CompileXPath31(v2.GroupBy); }
            else if (v2.GroupAdjacent != null) { kind = 1; key = CompileXPath31(v2.GroupAdjacent); }
            else
            {
                kind = v2.GroupStartingWith != null ? 2 : 3;
                // Patterns match nodes only; an atomic item never starts or ends a group.
                QilIterator n;
                key = X31(nameof(XsltLibrary.X31BooleanItem), T.Item,
                    _f.Conditional(_f.IsType(item, T.NodeNotRtf),
                        _f.Loop(n = _f.Let(_f.TypeAssert(item, T.NodeNotRtf)), MatchPattern(CompileMatchPattern(v2.GroupStartingWith ?? v2.GroupEndingWith!), n)),
                        _f.False()));
            }
            QilNode tuples = _curLoop.ConstructLoop(X31(nameof(XsltLibrary.X31GroupTuple), T.Item, _curLoop.GetCurrent()!, Box(key)));
            _curLoop = curLoopSaved;

            QilNode collation = v2.Collation != null ? CompileStringAvt(v2.Collation)! : _f.String("");
            QilIterator groups = _f.Let(X31(nameof(XsltLibrary.X31Groups), T.ItemS, tuples, _f.Double(kind), collation, v2.Composite ? _f.True() : _f.False()));
            _varHelper.AddVariable(groups);

            // 2. The groups, each with its first item as the context item and the group as current-group().
            var savedGroup = _currentGroupFactory;
            var savedKey = _currentGroupingKeyFactory;
            QilIterator g = _f.For(groups);
            _curLoop.SetFocus(g);
            _focusOverrides[g] = it => X31(nameof(XsltLibrary.X31GroupFirst), T.Item, it);
            // Sort keys see the group being sorted as current-group().
            _currentGroupFactory = () => X31(nameof(XsltLibrary.X31GroupMembers), T.ItemS, g);
            _currentGroupingKeyFactory = () => X31(nameof(XsltLibrary.X31GroupKey), T.ItemS, g);
            _curLoop.Sort(CompileSorts31(content, ref curLoopSaved));
            var current = (QilIterator)_curLoop.GetCurrent()!;
            if (current != g)
            {
                _focusOverrides[current] = it => X31(nameof(XsltLibrary.X31GroupFirst), T.Item, it);
            }
            _currentGroupFactory = () => X31(nameof(XsltLibrary.X31GroupMembers), T.ItemS, current);
            _currentGroupingKeyFactory = () => X31(nameof(XsltLibrary.X31GroupKey), T.ItemS, current);

            QilNode result = CompileInstructions(content);
            result = WrapLoopBody(node.ElemNameLi, result, node.EndTagLi);
            result = AddCurrentPositionLast(result);
            result = _curLoop.ConstructLoop(result);
            _curLoop = curLoopSaved;
            _currentGroupFactory = savedGroup;
            _currentGroupingKeyFactory = savedKey;
            return _varHelper.FinishVariables(result, varScope);
        }

        // current-group() and current-grouping-key() may be used many times; each use needs its own QIL node.
        private System.Func<QilNode>? _currentGroupFactory, _currentGroupingKeyFactory;

        private QilNode CompileAnalyzeString31(XslNode node)
        {
            XslNodeV2 v2 = node.V2!;
            QilNode input = CompileXPath31(node.Select);
            QilNode regex = CompileStringAvt(v2.Regex ?? "")!;
            QilNode flags = CompileStringAvt(v2.Flags ?? "")!;
            int varScope = _varHelper.StartVariables();
            QilIterator segments = _f.Let(X31(nameof(XsltLibrary.X31AnalyzeString), T.ItemS, Box(input), regex, flags));
            if (regex.NodeType == QilNodeType.LiteralString && flags.NodeType == QilNodeType.LiteralString)
            {
                // A static regex: errors in it are reported now (XTDE1140, XTDE1145), as the regex is fixed.
                try
                {
                    XPath31.Functions.XPathRegex.Get((string)(QilLiteral)regex, (string)(QilLiteral)flags);
                }
                catch (XdmException e)
                {
                    ReportXPath31Error(e.Code == "FORX0001" ? "XTDE1145" : "XTDE1140", e.Message);
                }
            }
            _varHelper.AddVariable(segments);

            XslNode? matching = null, nonMatching = null;
            foreach (XslNode child in node.Content)
            {
                if (child.NodeType == XslNodeType.MatchingSubstring) matching = child;
                else if (child.NodeType == XslNodeType.NonMatchingSubstring) nonMatching = child;
            }

            LoopFocus curLoopSaved = _curLoop;
            QilIterator seg = _f.For(segments);
            _curLoop.SetFocus(seg);
            _focusOverrides[seg] = it => X31(nameof(XsltLibrary.X31SegmentString), T.Item, it);
            QilNode? savedMatch = _currentMatch;

            _currentMatch = seg;
            QilNode onMatch = matching != null ? CompileBranch31(matching) : InstructionList();
            _currentMatch = null;
            QilNode onNonMatch = nonMatching != null ? CompileBranch31(nonMatching) : InstructionList();
            _currentMatch = savedMatch;

            QilNode result = _f.Conditional(X31(nameof(XsltLibrary.X31SegmentIsMatch), T.BooleanX, seg), onMatch, onNonMatch);
            result = AddCurrentPositionLast(result);
            result = _curLoop.ConstructLoop(result);
            _curLoop = curLoopSaved;
            return _varHelper.FinishVariables(result, varScope);
        }

        private QilNode CompileBranch31(XslNode branch)
        {
            QilList? nsList = EnterScope(branch);
            QilNode result = CompileInstructions(branch.Content);
            ExitScope();
            return SetDebugNs(result, nsList);
        }

        // ------------------------------------------------------------ stylesheet functions

        /// <summary>Creates the QIL functions for xsl:function declarations (bodies are compiled later).</summary>
        private void PrecompileFunctions31()
        {
            foreach (var ((name, arity), (function, _)) in _compiler.Functions)
            {
                QilList args = _f.FormalParameterList();
                foreach (XslNode n in function.Content)
                {
                    if (n.NodeType == XslNodeType.Param)
                    {
                        QilParameter p = CreateXslParam(n.Name!, T.ItemS);
                        args.Add(p);
                    }
                }
                function.Function = _f.Function(args, _f.True(), T.ItemS);
                function.Function.DebugName = $"<xsl:function name=\"{name.QualifiedName}\">";
                SetLineInfo(function.Function, function.SourceLine ?? SourceLineInfo.NoSource);
                _functions.Add(function.Function);
            }
        }

        private void CompileFunctions31()
        {
            foreach (var (_, (function, _)) in _compiler.Functions)
            {
                EnterScope(function);
                bool savedNoFocus = _noFocus;
                _noFocus = true;
                _temporaryOutput31++; // a function's result is a temporary tree (XTDE1480 for xsl:result-document)
                _currentTunnel = null;
                CheckSingletonFocus();
                int index = 0;
                var body = new List<XslNode>();
                QilNode? prologue = null;
                var coerced = new List<(QilIterator, QilNode)>();
                foreach (XslNode n in function.Content)
                {
                    if (n.NodeType == XslNodeType.Param)
                    {
                        var p = (QilParameter)function.Function!.Arguments[index++];
                        QilNode value = p;
                        if (n.V2?.As is string asType)
                        {
                            QilIterator let = _f.Let(Coerce31(p, asType, "XTTE0790"));
                            coerced.Add((let, value));
                            _scope.AddVariable(n.Name!, let);
                        }
                        else
                        {
                            _scope.AddVariable(n.Name!, p);
                        }
                    }
                    else
                    {
                        body.Add(n);
                    }
                }
                QilNode result = CompileInstructions(body);
                if (function.V2?.As is string resultType)
                {
                    result = Coerce31(result, resultType, "XTTE0780");
                }
                result = _f.TypeAssert(Box(result), T.ItemS);
                for (int i = coerced.Count - 1; i >= 0; i--)
                {
                    result = _f.Loop(coerced[i].Item1, result);
                }
                function.Function!.Definition = prologue ?? result;
                _noFocus = savedNoFocus;
                _temporaryOutput31--;
                ExitScope();
            }
        }
    }
}
