// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.Linq;
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

        QilNode? IXPath31Environment.ResolveVariable(QName name)
        {
            if (_scope.IsLocalVariable(name.LocalName, name.NamespaceUri) || _compiler.PackageInfos.Count <= 1)
            {
                return _scope.LookupVariable(name.LocalName, name.NamespaceUri);
            }
            // Xsl3: a global variable as the package whose code this is sees it (Packages.cs).
            QilName? global = ResolveComponent31('V', _f.QName(name.LocalName, name.NamespaceUri), -1);
            return global == null ? null : _scope.LookupVariable(global.LocalName, global.NamespaceUri);
        }

        // ------------------------------------------------------------ packages

        // The package whose code is being compiled, and the component (for xsl:original).
        private int _package31;
        private XslNode? _component31;

        private void EnterComponent31(XslNode component)
        {
            _package31 = component.PackageId;
            _component31 = component;
        }

        /// <summary>
        /// The registered name of the component a name in the current package's code refers to (null if it is
        /// hidden there); xsl:original, in an overriding component, is the component it overrides (XTSE3075
        /// elsewhere).
        /// </summary>
        private QilName? ResolveComponent31(char kind, QilName name, int arity)
        {
            if (name.NamespaceUri == XmlReservedNs.NsXslt && name.LocalName == "original")
            {
                if (_component31 != null && _compiler.PackageOriginals.TryGetValue(_component31, out QilName? original))
                {
                    return original;
                }
                ReportXPath31Error("XTSE3075", "xsl:original is only available in a component that overrides one in xsl:override");
                return null;
            }
            return PackageComponents.Resolve(_compiler, _package31, kind, name, arity);
        }

        // The lifted functions of the stylesheet's function items, and their dispatcher (completed by Compile).
        private FunctionItemRegistry? _functionItems31;

        FunctionItemRegistry? IXPath31Environment.FunctionItems => _functionItems31 ??= new FunctionItemRegistry(_f, _functions.Add);

        QilNode? IXPath31Environment.ResolveFunction(QName name, IReadOnlyList<XPath31.Expr> args, System.Func<XPath31.Expr, QilNode> compile)
        {
            // Stylesheet functions (xsl:function) first: they may not override F&O functions, but have their own namespaces.
            if ((IsV2 ? ResolveComponent31('F', _f.QName(name.LocalName, name.NamespaceUri), args.Count) : _f.QName(name.LocalName, name.NamespaceUri)) is QilName functionName &&
                _compiler.Functions.TryGetValue((functionName, args.Count), out var entry))
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
                case ("current-merge-group", 0):
                case ("current-merge-group", 1):
                case ("current-merge-key", 0):
                    if (_currentMerge31 is not var (group, key, sources, names))
                    {
                        return X31(nameof(XsltLibrary.X31Raise), T.ItemS, _f.String("XTDE3480"), _f.String($"{name.LocalName}() is absent outside xsl:merge-action"));
                    }
                    if (name.LocalName == "current-merge-key")
                    {
                        return key;
                    }
                    if (args.Count == 0)
                    {
                        return group;
                    }
                    if (args[0] is XPath31.StringLiteral sourceName)
                    {
                        int index = names.IndexOf(sourceName.Value);
                        return index < 0
                            ? ReportXPath31Error("XTDE3490", $"There is no merge source named {sourceName.Value}")
                            : X31(nameof(XsltLibrary.X31MergeSourceGroup), T.ItemS, sources, _f.Double(index));
                    }
                    return X31(nameof(XsltLibrary.X31MergeSourceGroupNamed), T.ItemS, sources, String31(compile(args[0])),
                        _f.Double(ConstantPool.Add(names.ToArray())));
                case ("accumulator-before" or "accumulator-after", 1):
                    return X31(nameof(XsltLibrary.X31AccumulatorValue), T.ItemS, AccumulatorItems31(), _f.Double(_accumulatorTable31),
                        String31(compile(args[0])), _f.Double(NameContext31()), Box(compile(new XPath31.ContextItemExpr())),
                        _f.Boolean(name.LocalName == "accumulator-after"));
                case ("copy-of", 0):
                case ("copy-of", 1):
                    return Box(CopyItems31(compile(args.Count == 0 ? new XPath31.ContextItemExpr() : args[0])));
                case ("snapshot", 0):
                case ("snapshot", 1):
                    return X31(nameof(XsltLibrary.X31Snapshot), T.ItemS, compile(args.Count == 0 ? new XPath31.ContextItemExpr() : args[0]));
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
                case XslNodeType.WherePopulated:
                    return CompileWherePopulated31(node);
                case XslNodeType.Try:
                    return CompileTry31(node);
                case XslNodeType.Iterate:
                    return CompileIterate31(node);
                case XslNodeType.Merge:
                    return CompileMerge31(node);
                case XslNodeType.Map:
                    // XSLT 3.0 §21.2: the maps of the content, merged (XTDE3365 for a duplicate key).
                    return X31(nameof(XsltLibrary.X31MapMerge), T.Item, Box(CompileInstructions(node.Content)));
                case XslNodeType.MapEntry:
                    return X31(nameof(XsltLibrary.X31MapSingleton), T.Item, Box(CompileXPath31(node.V2!.NameAvt!)), Box(SelectOrContent31(node, node.Select)));
                case XslNodeType.ContextItem:
                    // The context item a template rule or named template requires (XSLT 3.0 §10.1.2).
                    return node.V2?.As is string contextType && !FocusAbsent
                        ? X31(nameof(XsltLibrary.X31Discard), T.ItemS, InstanceCheck31(GetCurrentNode(), contextType, "XTTE0590"))
                        : _f.Sequence();
                case XslNodeType.NextIteration:
                    return CompileNextIteration31(node);
                case XslNodeType.Break:
                    return CompileBreak31(node);
                case XslNodeType.OnCompletion:
                    return ReportXPath31Error("XTSE0010", "xsl:on-completion is only allowed in xsl:iterate");
                case XslNodeType.Catch:
                    return ReportXPath31Error("XTSE0010", "xsl:catch is only allowed in xsl:try");
                case XslNodeType.OnEmpty or XslNodeType.OnNonEmpty:
                    // Outside a sequence constructor with other instructions (handled by CompileWithOnEmpty31).
                    return node.NodeType == XslNodeType.OnEmpty ? SelectOrContent31(node, node.Select) : _f.Sequence();
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
            return CopyItems31(select);
        }

        private QilNode CopyItems31(QilNode select)
        {
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
            // XSLT 3.0 applies templates to any items (§6.8): those that aren't nodes go to the item matcher.
            QilNode selected = CompileXPath31(node.Select);
            QilIterator select = _f.Let(selected.XmlType!.IsSubtypeOf(T.NodeNotRtfS) ? selected : Box(selected));
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
            if (node.V2?.StaticValue is int staticValue)
            {
                return _f.TypeAssert(X31(nameof(XsltLibrary.X31StaticValue), T.ItemS, _f.Double(staticValue)), T.ItemS);
            }
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
        // The value, if it is an instance of the type (no conversion), else the error.
        private QilNode InstanceCheck31(QilNode value, string asType, string errorCode)
        {
            try
            {
                XPathStaticContext sc = StaticContext31();
                var resolver = new StaticTypeResolver(p => sc.Namespaces.TryGetValue(p, out string? uri) ? uri : null, sc.DefaultElementNamespace);
                SequenceTypeTest test = resolver.Resolve(Parser.ParseSequenceType(asType));
                return X31(nameof(XsltLibrary.X31InstanceCheck), T.ItemS, Box(value), _f.Double(ConstantPool.Add(test)), _f.String(errorCode));
            }
            catch (XPathStaticException e)
            {
                return ReportXPath31Error(e.Code, e.Message);
            }
        }

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

        // ------------------------------------------------------------ xsl:merge

        // In xsl:merge-action: the current merge group, the merge key, the group's items of each source (an array)
        // and the sources' names.
        private (QilNode Group, QilNode Key, QilNode Sources, List<string?> Names)? _currentMerge31;

        /// <summary>
        /// xsl:merge (XSLT 3.0 §15), unstreamed: each source's inputs are computed here (as arrays, one per input
        /// sequence), its merge keys and the action are lifted into functions, and the run-time library merges the
        /// inputs by key and calls the action for each group (X31Merge).
        /// </summary>
        private QilNode CompileMerge31(XslNode node)
        {
            var sources = new List<XslNode>();
            XslNode? action = null;
            foreach (XslNode n in node.Content)
            {
                if (n.NodeType == XslNodeType.MergeSource)
                {
                    sources.Add(n);
                }
                else if (n.NodeType == XslNodeType.MergeAction)
                {
                    action = n;
                }
            }
            if (action == null || sources.Count == 0)
            {
                return _f.Sequence();
            }
            var names = new List<string?>();
            var spec = new MergeSpec();
            QilList inputs = _f.BaseFactory.Sequence();
            QilList keyFunctions = _f.BaseFactory.Sequence();
            QilList orders = _f.BaseFactory.Sequence(); // the order attribute value templates of the first source's keys
            foreach (XslNode source in sources)
            {
                EnterScope(source);
                string? sourceName = source.V2?.NameAvt;
                if (sourceName != null && names.Contains(sourceName))
                {
                    ReportXPath31Error("XTSE3190", $"Two merge sources are named {sourceName}");
                }
                names.Add(sourceName);
                if (source.Content.Count != sources[0].Content.Count)
                {
                    ReportXPath31Error("XTSE2200", "The merge sources have different numbers of merge keys");
                }
                spec.SortBeforeMerge.Add(source.V2!.SortBeforeMerge);
                string? forEach = source.V2.ForEachItem ?? (source.V2.ForEachSource is string uris ? $"({uris}) ! doc(.)" : null);
                QilNode sourceInputs;
                if (forEach == null)
                {
                    sourceInputs = X31(nameof(XsltLibrary.X31Wrap), T.Item, Box(CompileXPath31(source.Select!)));
                }
                else
                {
                    QilIterator each = _f.For(Box(CompileXPath31(forEach)));
                    sourceInputs = _f.Loop(each, WithFixedFocus31(each, () => X31(nameof(XsltLibrary.X31Wrap), T.Item, Box(CompileXPath31(source.Select!)))));
                }
                inputs.Add(X31(nameof(XsltLibrary.X31Wrap), T.Item, Box(sourceInputs)));
                keyFunctions.Add(Lift31(own => WithFixedFocus31(_f.TypeAssert(own[0], T.Item), () =>
                {
                    QilList keys = _f.BaseFactory.Sequence();
                    foreach (XslNode key in source.Content)
                    {
                        EnterScope(key);
                        keys.Add(X31(nameof(XsltLibrary.X31Wrap), T.Item, Box(SelectOrContent31(key, key.Select))));
                        ExitScope();
                    }
                    return Box(keys);
                }), 1));
                if (spec.Descending.Count == 0)
                {
                    foreach (XslNode key in source.Content)
                    {
                        EnterScope(key);
                        spec.Descending.Add(key.V2?.Order == "descending");
                        orders.Add(X31(nameof(XsltLibrary.X31StringItem), T.Item, CompileStringAvt(key.V2?.Order ?? "ascending")));
                        string? collation = key.V2?.Collation;
                        spec.Collations.Add(collation != null && !collation.Contains('{')
                            ? XPath31.Functions.UriReference.IsAbsolute(collation) || StaticBaseUri31() is not string b ? collation : XPath31.Functions.UriReference.Resolve(collation, b)
                            : StaticContext31().DefaultCollation);
                        ExitScope();
                    }
                }
                ExitScope();
            }

            var savedMerge = _currentMerge31;
            QilNode actionItem = Lift31(own => WithFixedFocus31(_f.TypeAssert(X31(nameof(XsltLibrary.X31FirstItem), T.ItemS, own[0]), T.Item), () =>
            {
                var savedFixed = _fixedFocus31;
                _fixedFocus31 = (savedFixed!.Value.Current, X31(nameof(XsltLibrary.X31CapturedNumber), T.DoubleX, own[3]),
                    X31(nameof(XsltLibrary.X31CapturedNumber), T.DoubleX, own[4]));
                _currentMerge31 = (own[0], own[1], own[2], names);
                try
                {
                    EnterScope(action);
                    QilNode result = CompileInstructions(action.Content);
                    ExitScope();
                    return result;
                }
                finally
                {
                    _currentMerge31 = savedMerge;
                    _fixedFocus31 = savedFixed;
                }
            }), 5);

            QilIterator inputsLet = _f.Let(Box(inputs)), keysLet = _f.Let(Box(keyFunctions)), actionLet = _f.Let(actionItem), ordersLet = _f.Let(Box(orders));
            return _f.Loop(inputsLet, _f.Loop(keysLet, _f.Loop(actionLet, _f.Loop(ordersLet,
                X31(nameof(XsltLibrary.X31Merge), T.ItemS, inputsLet, keysLet, actionLet, ordersLet, _f.Double(ConstantPool.Add(spec)))))));
        }

        // ------------------------------------------------------------ accumulators

        // The function items of every accumulator (a global variable, compiled by CompileAccumulators31), and the
        // table (ConstantPool) of where each accumulator's items are.
        private QilIterator? _accumulatorItems31;
        private int _accumulatorTable31;

        private QilIterator AccumulatorItems31()
        {
            if (_accumulatorItems31 == null)
            {
                _accumulatorItems31 = _f.Let(_f.TypeAssert(_f.Sequence(), T.ItemS));
                _accumulatorItems31.DebugName = "<xsl:accumulator>";
                _gloVars.Add(_accumulatorItems31);
                _accumulatorTable31 = ConstantPool.Add(new Dictionary<string, AccumulatorInfo>());
            }
            return _accumulatorItems31;
        }

        /// <summary>
        /// The accumulators (XSLT 3.0 §18.2), if accumulator-before or accumulator-after is called: for each, a
        /// function of the root for the initial value, and per rule a function of a node for the priority of the
        /// rule's pattern if the node matches (else nothing) and one of the node and $value for the new value.
        /// The run-time library walks each tree once, in document order, with them (X31AccumulatorValue).
        /// </summary>
        private void CompileAccumulators31()
        {
            if (_accumulatorItems31 == null)
            {
                return;
            }
            var table = (Dictionary<string, AccumulatorInfo>)ConstantPool.Get(_accumulatorTable31);
            QilList items = _f.BaseFactory.Sequence();
            foreach (var (name, (decl, _)) in _compiler.Accumulators)
            {
                EnterScope(decl);
                EnterComponent31(decl);
                var phases = new List<bool>();
                int offset = items.Count;
                items.Add(Lift31(own => WithFixedFocus31(_f.TypeAssert(own[0], T.Item), () => CompileXPath31(decl.Select!)), 1));
                // The rules, in decreasing order of preference among equal priorities: the last one wins.
                for (int i = decl.Content.Count - 1; i >= 0; i--)
                {
                    XslNode rule = decl.Content[i];
                    EnterScope(rule);
                    phases.Add(rule.V2!.Phase == "end");
                    items.Add(Lift31(own => AccumulatorMatch31(rule.V2!.Match!, own[0]), 1));
                    items.Add(Lift31(own => WithFixedFocus31(_f.TypeAssert(own[0], T.Item), () =>
                    {
                        _scope.EnterScope();
                        try
                        {
                            _scope.AddVariable(_f.QName("value"), own[1]);
                            return SelectOrContent31(rule, rule.Select);
                        }
                        finally
                        {
                            _scope.ExitScope();
                        }
                    }), 2));
                    ExitScope();
                }
                SequenceTypeTest? type = null;
                if (decl.V2?.As is string asType)
                {
                    try
                    {
                        XPathStaticContext sc = StaticContext31();
                        type = new StaticTypeResolver(p => sc.Namespaces.TryGetValue(p, out string? uri) ? uri : null, sc.DefaultElementNamespace)
                            .Resolve(Parser.ParseSequenceType(asType));
                    }
                    catch (XPathStaticException e)
                    {
                        ReportXPath31Error(e.Code, e.Message);
                    }
                }
                table[new QName(name.NamespaceUri, name.LocalName).EQName] = new AccumulatorInfo(offset, phases.ToArray(), type);
                ExitScope();
            }
            _accumulatorItems31.Binding = Box(items);
        }

        // The pattern of an accumulator rule: the priority of the best alternative the node matches, else nothing.
        private QilNode AccumulatorMatch31(string pattern, QilParameter node)
        {
            QilNode compiled = CompilePattern31(pattern);
            var alternatives = new List<QilLoop>();
            if (compiled.NodeType == QilNodeType.Sequence)
            {
                foreach (QilNode n in (QilList)compiled)
                {
                    alternatives.Add((QilLoop)n);
                }
            }
            else
            {
                alternatives.Add((QilLoop)compiled);
            }
            alternatives.Sort((a, b) => XPathPatternBuilder.GetPriority(b).CompareTo(XPathPatternBuilder.GetPriority(a)));
            QilIterator subject = _f.Let(_f.TypeAssert(node, T.NodeNotRtf));
            QilNode result = _f.TypeAssert(_f.Sequence(), T.ItemS);
            for (int i = alternatives.Count - 1; i >= 0; i--)
            {
                QilLoop alternative = alternatives[i];
                QilNode condition = _refReplacer.Replace(alternative.Body, alternative.Variable, subject);
                result = _f.Conditional(condition, Box(_f.Double(XPathPatternBuilder.GetPriority(alternative))), result);
            }
            return _f.Loop(subject, result);
        }

        // Compiles with the item as the focus (position and size 1), as the body of xsl:iterate does.
        private QilNode WithFixedFocus31(QilNode item, System.Func<QilNode> compile)
        {
            LoopFocus savedLoop = _curLoop;
            var savedFixed = _fixedFocus31;
            QilIterator? savedNodeContext = _nodeContext31;
            bool savedNoFocus = _noFocus;
            QilIterator current = _f.Let(item);
            try
            {
                _curLoop = new LoopFocus(_f);
                _fixedFocus31 = (current, _f.Double(1), _f.Double(1));
                _nodeContext31 = null;
                _noFocus = false;
                return _f.Loop(current, compile());
            }
            finally
            {
                _curLoop = savedLoop;
                _fixedFocus31 = savedFixed;
                _nodeContext31 = savedNodeContext;
                _noFocus = savedNoFocus;
            }
        }

        // ------------------------------------------------------------ xsl:iterate

        // The focus of the body of xsl:iterate: the item, its position and the size of the sequence.
        private (QilNode Current, QilNode Position, QilNode Last)? _fixedFocus31;

        // The xsl:iterate instructions being compiled: their parameters and the variables holding their values.
        private readonly Stack<(List<VarPar> Params, List<QilIterator> Values)> _iterate31 = new();

        /// <summary>
        /// xsl:iterate (XSLT 3.0 §7.2): the body and xsl:on-completion are lifted into functions and the run-time
        /// library loops over the items, passing the parameters' values on. The body's value ends with a control
        /// item when it ends in xsl:next-iteration (the new values) or xsl:break.
        /// </summary>
        private QilNode CompileIterate31(XslNode node)
        {
            var parameters = new List<VarPar>();
            XslNode? onCompletion = null;
            var body = new List<XslNode>();
            foreach (XslNode n in node.Content)
            {
                if (n is VarPar { NodeType: XslNodeType.Param } p)
                {
                    parameters.Add(p);
                }
                else if (n.NodeType == XslNodeType.OnCompletion)
                {
                    onCompletion = n;
                }
                else
                {
                    body.Add(n);
                }
            }
            for (int i = 0; i < parameters.Count; i++)
            {
                if (parameters.FindIndex(p => p.Name!.Equals(parameters[i].Name)) != i)
                {
                    return ReportXPath31Error("XTSE0580", $"Two parameters of xsl:iterate are named ${parameters[i].Name!.QualifiedName}");
                }
            }
            if (!InTailPosition31(body, tail: true))
            {
                return ReportXPath31Error("XTSE3120", "xsl:next-iteration and xsl:break must be in a tail position of the body of xsl:iterate");
            }
            var lets = new List<QilIterator>();
            QilIterator Bind(QilNode value)
            {
                QilIterator let = _f.Let(value);
                lets.Add(let);
                return let;
            }

            QilIterator select = Bind(Box(CompileXPath31(node.Select)));
            QilNode initial = X31(nameof(XsltLibrary.X31IterationNew), T.Item, _f.False());
            foreach (VarPar p in parameters)
            {
                EnterScope(p);
                QilNode value = Box(CompileVarParValue(p));
                ExitScope();
                if (p.V2?.As is string asType)
                {
                    value = Coerce31(value, asType, "XTTE0590");
                }
                initial = X31(nameof(XsltLibrary.X31IterationAdd), T.Item, initial, Bind(value));
            }
            QilIterator initialLet = Bind(initial);

            QilNode bodyItem = Lift31(own =>
            {
                LoopFocus savedLoop = _curLoop;
                var savedFixed = _fixedFocus31;
                QilIterator? savedNodeContext = _nodeContext31;
                bool savedNoFocus = _noFocus;
                _scope.EnterScope();
                try
                {
                    var focus = new List<QilIterator>
                    {
                        _f.Let(_f.TypeAssert(own[0], T.Item)),
                        _f.Let(X31(nameof(XsltLibrary.X31CapturedNumber), T.DoubleX, own[1])),
                        _f.Let(X31(nameof(XsltLibrary.X31CapturedNumber), T.DoubleX, own[2])),
                    };
                    _curLoop = new LoopFocus(_f);
                    _fixedFocus31 = (focus[0], focus[1], focus[2]);
                    _nodeContext31 = null;
                    _noFocus = false;
                    List<QilIterator> values = BindIterateParams31(parameters, own, 3, focus);
                    _iterate31.Push((parameters, values));
                    QilNode result = CompileInstructions(body);
                    _iterate31.Pop();
                    for (int i = focus.Count - 1; i >= 0; i--)
                    {
                        result = _f.Loop(focus[i], result);
                    }
                    return result;
                }
                finally
                {
                    _scope.ExitScope();
                    _curLoop = savedLoop;
                    _fixedFocus31 = savedFixed;
                    _nodeContext31 = savedNodeContext;
                    _noFocus = savedNoFocus;
                }
            }, 3 + parameters.Count);
            QilIterator bodyLet = Bind(bodyItem);

            QilNode completion = _f.TypeAssert(_f.Sequence(), T.ItemS);
            if (onCompletion != null)
            {
                completion = Lift31(own =>
                {
                    // The focus is absent in xsl:on-completion (XPDY0002).
                    LoopFocus savedLoop = _curLoop;
                    var savedFixed = _fixedFocus31;
                    QilIterator? savedNodeContext = _nodeContext31;
                    bool savedNoFocus = _noFocus;
                    _curLoop = new LoopFocus(_f);
                    _fixedFocus31 = null;
                    _nodeContext31 = null;
                    _noFocus = true;
                    _scope.EnterScope();
                    try
                    {
                        var lets2 = new List<QilIterator>();
                        BindIterateParams31(parameters, own, 0, lets2);
                        EnterScope(onCompletion);
                        QilNode result = SelectOrContent31(onCompletion, onCompletion.Select);
                        ExitScope();
                        for (int i = lets2.Count - 1; i >= 0; i--)
                        {
                            result = _f.Loop(lets2[i], result);
                        }
                        return result;
                    }
                    finally
                    {
                        _scope.ExitScope();
                        _curLoop = savedLoop;
                        _fixedFocus31 = savedFixed;
                        _nodeContext31 = savedNodeContext;
                        _noFocus = savedNoFocus;
                    }
                }, parameters.Count);
            }
            QilIterator completionLet = Bind(completion);

            QilNode loop = X31(nameof(XsltLibrary.X31Iterate), T.ItemS, select, bodyLet, completionLet, initialLet);
            for (int i = lets.Count - 1; i >= 0; i--)
            {
                loop = _f.Loop(lets[i], loop);
            }
            return loop;
        }

        // Whether every xsl:next-iteration and xsl:break of a sequence constructor is in a tail position (XSLT 3.0
        // §7.2): last, or last in an xsl:if, xsl:choose branch or xsl:try/xsl:catch that is itself in one.
        private static bool InTailPosition31(IList<XslNode> content, bool tail)
        {
            int last = content.Count - 1;
            while (last >= 0 && content[last] is { NodeType: XslNodeType.Text } t && t.Select != null && t.Select.Trim().Length == 0)
            {
                last--;
            }
            for (int i = 0; i < content.Count; i++)
            {
                XslNode n = content[i];
                bool isTail = tail && i == last;
                switch (n.NodeType)
                {
                    case XslNodeType.NextIteration:
                    case XslNodeType.Break:
                        if (!isTail)
                        {
                            return false;
                        }
                        break;
                    case XslNodeType.Iterate:
                        break; // its own body is checked when it is compiled
                    case XslNodeType.Choose:
                        foreach (XslNode branch in n.Content)
                        {
                            if (!InTailPosition31(branch.Content, isTail))
                            {
                                return false;
                            }
                        }
                        break;
                    case XslNodeType.Try:
                        if (!InTailPosition31(n.Content.TakeWhile(c => c.NodeType != XslNodeType.Catch).ToList(), isTail) ||
                            !n.Content.Where(c => c.NodeType == XslNodeType.Catch).All(c => InTailPosition31(c.Content, isTail)))
                        {
                            return false;
                        }
                        break;
                    case XslNodeType.If:
                    case XslNodeType.Otherwise:
                        if (!InTailPosition31(n.Content, isTail))
                        {
                            return false;
                        }
                        break;
                    default:
                        if (n.Content != null && n.Content.Count != 0 && !InTailPosition31(n.Content, tail: false))
                        {
                            return false;
                        }
                        break;
                }
            }
            return true;
        }

        // The parameters of xsl:iterate as variables, from the function's parameters (from `first` on).
        private List<QilIterator> BindIterateParams31(List<VarPar> parameters, IReadOnlyList<QilParameter> own, int first, List<QilIterator> lets)
        {
            var values = new List<QilIterator>();
            for (int i = 0; i < parameters.Count; i++)
            {
                QilIterator let = _f.Let(own[first + i]);
                lets.Add(let);
                values.Add(let);
                _scope.AddVariable(parameters[i].Name!, let);
            }
            return values;
        }

        // xsl:next-iteration: a control item with the parameters' new values (the current ones where not given).
        private QilNode CompileNextIteration31(XslNode node)
        {
            if (_iterate31.Count == 0)
            {
                return ReportXPath31Error("XTSE3120", "xsl:next-iteration is only allowed in the body of xsl:iterate");
            }
            var (parameters, values) = _iterate31.Peek();
            var given = new Dictionary<QilName, VarPar>();
            foreach (XslNode n in node.Content)
            {
                if (n is VarPar { NodeType: XslNodeType.WithParam } wp)
                {
                    if (!parameters.Exists(p => p.Name!.Equals(wp.Name)))
                    {
                        return ReportXPath31Error("XTSE3130", $"xsl:iterate has no parameter ${wp.Name!.QualifiedName}");
                    }
                    if (!given.TryAdd(wp.Name!, wp))
                    {
                        return ReportXPath31Error("XTSE0670", $"Duplicate xsl:with-param ${wp.Name!.QualifiedName}");
                    }
                }
            }
            var lets = new List<QilIterator>();
            QilNode control = X31(nameof(XsltLibrary.X31IterationNew), T.Item, _f.False());
            for (int i = 0; i < parameters.Count; i++)
            {
                QilNode value;
                if (given.TryGetValue(parameters[i].Name!, out VarPar? wp))
                {
                    CompileWithParam(wp);
                    value = Box(wp.Value!);
                    if (parameters[i].V2?.As is string asType)
                    {
                        value = Coerce31(value, asType, "XTTE0590");
                    }
                    QilIterator let = _f.Let(value);
                    lets.Add(let);
                    value = let;
                }
                else
                {
                    value = values[i];
                }
                control = X31(nameof(XsltLibrary.X31IterationAdd), T.Item, control, value);
            }
            for (int i = lets.Count - 1; i >= 0; i--)
            {
                control = _f.Loop(lets[i], control);
            }
            return control;
        }

        // xsl:break: its value, then a control item that ends the iteration.
        private QilNode CompileBreak31(XslNode node)
        {
            if (_iterate31.Count == 0)
            {
                return ReportXPath31Error("XTSE3120", "xsl:break is only allowed in the body of xsl:iterate");
            }
            return _f.Sequence(Box(SelectOrContent31(node, node.Select)), X31(nameof(XsltLibrary.X31IterationNew), T.Item, _f.True()));
        }

        // ------------------------------------------------------------ xsl:try, xsl:catch

        // The variables of xsl:catch, in the err namespace (XSLT 3.0 §8.3.1), in the order X31Try passes them.
        private static readonly string[] s_errVariables31 = { "code", "description", "value", "module", "line-number", "column-number", "additional" };

        /// <summary>
        /// xsl:try (XSLT 3.0 §8.3): the sequence constructor and each xsl:catch are lifted into functions of the
        /// variables they use, and the run-time library calls them through the dispatcher inside a C# try/catch;
        /// the try's value is complete (materialized) before it is returned, so no partial result escapes.
        /// </summary>
        private QilNode CompileTry31(XslNode node)
        {
            var body = new List<XslNode>();
            var catches = new List<XslNode>();
            foreach (XslNode n in node.Content)
            {
                if (n.NodeType == XslNodeType.Catch)
                {
                    catches.Add(n);
                }
                else if (catches.Count == 0)
                {
                    body.Add(n);
                }
            }
            QilNode tryItem = Lift31(_ =>
            {
                // The try's variables aren't in scope in the catches.
                _scope.EnterScope();
                try
                {
                    return node.Select != null ? CompileXPath31(node.Select) : CompileInstructions(body);
                }
                finally
                {
                    _scope.ExitScope();
                }
            }, 0);
            QilList catchItems = _f.BaseFactory.Sequence();
            var errors = new List<List<string>>();
            foreach (XslNode c in catches)
            {
                errors.Add(c.V2?.Errors ?? new List<string> { "*" });
                catchItems.Add(Lift31(parameters =>
                {
                    EnterScope(c);
                    try
                    {
                        for (int i = 0; i < s_errVariables31.Length; i++)
                        {
                            _scope.AddVariable(_f.QName(s_errVariables31[i], XdmException.ErrorNamespace, "err"), parameters[i]);
                        }
                        return SelectOrContent31(c, c.Select);
                    }
                    finally
                    {
                        ExitScope();
                    }
                }, s_errVariables31.Length));
            }
            // Bound first: arguments passed as item()* must be cached.
            QilIterator tryLet = _f.Let(tryItem), catchLet = _f.Let(catchItems);
            return _f.Loop(tryLet, _f.Loop(catchLet, X31(nameof(XsltLibrary.X31Try), T.ItemS, tryLet, catchLet, _f.Double(ConstantPool.Add(errors)), _f.String(node.SourceLine?.Uri ?? ""))));
        }

        /// <summary>
        /// A function item for code compiled here: <paramref name="compile"/> gets the function's own parameters
        /// (item()*) and returns the body, whose references to local variables, parameters and focus iterators
        /// become captured values (globals stay references).
        /// </summary>
        private QilNode Lift31(System.Func<IReadOnlyList<QilParameter>, QilNode> compile, int arity)
        {
            var own = new List<QilParameter>();
            for (int i = 0; i < arity; i++)
            {
                own.Add(_f.Parameter(T.ItemS));
            }
            int varScope = _varHelper.StartVariables();
            QilNode body = _varHelper.FinishVariables(Box(compile(own)), varScope);

            // Global variables are captured too, so that their errors aren't caught by xsl:try (XSLT 3.0 §8.3);
            // not the accumulators' items, which are made of lifted functions.
            var exclude = new HashSet<QilNode>(own);
            if (_accumulatorItems31 != null)
            {
                exclude.Add(_accumulatorItems31);
            }
            foreach (QilList globals in new[] { _extPars, _nsVars })
            {
                foreach (QilNode g in globals)
                {
                    exclude.Add(g);
                }
            }
            var finder = new FreeReferences31(exclude);
            finder.Find(body);

            QilList formal = _f.FormalParameterList();
            var map = new Dictionary<QilNode, QilNode>();
            var positions = new Dictionary<QilNode, QilNode>();
            var lets = new List<QilIterator>();
            foreach (QilNode free in finder.Free)
            {
                QilParameter p = _f.Parameter(T.ItemS);
                formal.Add(p);
                QilIterator let = _f.Let(Unbox31(p, free.XmlType!));
                lets.Add(let);
                map[free] = let;
            }
            foreach (QilNode iterator in finder.Positions)
            {
                // The position of a captured focus iterator is captured too (a Let's position is 1).
                QilParameter p = _f.Parameter(T.ItemS);
                formal.Add(p);
                QilIterator let = _f.Let(_f.XsltConvert(X31(nameof(XsltLibrary.X31CapturedNumber), T.DoubleX, p), T.IntX));
                lets.Add(let);
                positions[iterator] = let;
            }
            foreach (QilParameter p in own)
            {
                formal.Add(p);
            }
            body = new ReplaceReferences31(_f.BaseFactory, map, positions).Replace(body);
            for (int i = lets.Count - 1; i >= 0; i--)
            {
                body = _f.Loop(lets[i], body);
            }
            QilFunction function = _f.Function(formal, _f.True(), T.ItemS);
            function.Definition = _f.TypeAssert(body, T.ItemS);
            function.DebugName = arity == 0 ? "<xsl:try>" : "<xsl:catch>";
            FunctionItemRegistry registry = ((IXPath31Environment)this).FunctionItems!;
            _ = registry.Dispatcher; // the run-time library calls it through the dispatcher
            int id = registry.Lift(function, finder.Free.Count + finder.Positions.Count, arity);
            var any = new SequenceTypeTest[arity];
            System.Array.Fill(any, SequenceTypeTest.AnySequence);
            QilNode item = X31(nameof(XsltLibrary.X31FunctionNew), T.Item, _f.Double(id),
                _f.Double(ConstantPool.Add(new FunctionSignature(null, any, SequenceTypeTest.AnySequence))));
            foreach (QilNode free in finder.Free)
            {
                item = X31(nameof(XsltLibrary.X31FunctionCapture), T.Item, item, Box(free));
            }
            foreach (QilNode iterator in finder.Positions)
            {
                item = X31(nameof(XsltLibrary.X31FunctionCapture), T.Item, item, Box(_f.XsltConvert(_f.PositionOf((QilIterator)iterator), T.DoubleX)));
            }
            return item;
        }

        // A captured value (item()*) back as the type of the reference it replaces.
        private QilNode Unbox31(QilNode boxed, XmlQueryType type)
        {
            if (type == T.ItemS)
            {
                return boxed;
            }
            if (type.IsAtomicValue && type.IsStrict && type.TypeCode is XmlTypeCode.Double or XmlTypeCode.Int)
            {
                // Native positions and sizes: boxed as xs:integer.
                QilNode number = X31(nameof(XsltLibrary.X31CapturedNumber), T.DoubleX, boxed);
                return type.TypeCode == XmlTypeCode.Double ? number : _f.XsltConvert(number, type);
            }
            return type.IsAtomicValue && type.IsStrict ? _f.XsltConvert(boxed, type) : _f.TypeAssert(boxed, type);
        }

        // The references of a QIL expression to iterators and parameters it doesn't bind.
        private sealed class FreeReferences31(HashSet<QilNode> exclude) : QilScopedVisitor
        {
            private readonly HashSet<QilNode> _bound = new();
            private readonly HashSet<QilNode> _seen = new();
            private readonly HashSet<QilNode> _seenPositions = new();
            public readonly List<QilNode> Free = new();
            public readonly List<QilNode> Positions = new(); // free iterators whose position is used

            public void Find(QilNode root) => VisitAssumeReference(root);

            protected override QilNode VisitPositionOf(QilUnary n)
            {
                if (!_bound.Contains(n.Child) && !exclude.Contains(n.Child) && _seenPositions.Add(n.Child))
                {
                    Positions.Add(n.Child);
                }
                return n;
            }

            protected override void BeginScope(QilNode node) => _bound.Add(node);

            protected override void EndScope(QilNode node) => _bound.Remove(node);

            protected override QilNode VisitReference(QilNode node)
            {
                if (node is QilIterator && !_bound.Contains(node) && !exclude.Contains(node) && _seen.Add(node))
                {
                    Free.Add(node);
                }
                return node;
            }
        }

        private sealed class ReplaceReferences31(QilFactory f, Dictionary<QilNode, QilNode> map, Dictionary<QilNode, QilNode> positions) : QilReplaceVisitor(f)
        {
            public QilNode Replace(QilNode node) => VisitAssumeReference(node);

            protected override QilNode VisitPositionOf(QilUnary n) => positions.TryGetValue(n.Child, out QilNode? p) ? p : base.VisitPositionOf(n);

            protected override QilNode VisitReference(QilNode node) => map.TryGetValue(node, out QilNode? r) ? r : node;
        }

        // ------------------------------------------------------------ xsl:where-populated, xsl:on-empty, xsl:on-non-empty

        private static bool HasOnEmpty31(IList<XslNode> instructions)
        {
            foreach (XslNode n in instructions)
            {
                if (n.NodeType is XslNodeType.OnEmpty or XslNodeType.OnNonEmpty)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// A sequence constructor with xsl:on-empty or xsl:on-non-empty (XSLT 3.0 §8.4.2): the other instructions are
        /// evaluated first; if their result is empty (or only vacuous items), the xsl:on-empty instructions give the
        /// result; otherwise it is that result with the xsl:on-non-empty ones, those before the other instructions
        /// first and the rest after.
        /// </summary>
        private QilNode CompileWithOnEmpty31(IList<XslNode> instructions)
        {
            var others = new List<XslNode>();
            var attributes = new List<XslNode>();
            var before = new List<XslNode>();
            var after = new List<XslNode>();
            var onEmpty = new List<XslNode>();
            for (int k = 0; k < instructions.Count; k++)
            {
                XslNode n = instructions[k];
                switch (n.NodeType)
                {
                    case XslNodeType.OnEmpty:
                        if (k != instructions.Count - 1)
                        {
                            return ReportXPath31Error("XTSE0010", "xsl:on-empty must be the last instruction of its sequence constructor");
                        }
                        onEmpty.Add(n);
                        break;
                    case XslNodeType.OnNonEmpty:
                        (others.Count == 0 ? before : after).Add(n);
                        break;
                    case XslNodeType.LiteralAttribute or XslNodeType.UseAttributeSet:
                        // A literal result element's attributes aren't part of its sequence constructor.
                        attributes.Add(n);
                        break;
                    default:
                        others.Add(n);
                        break;
                }
            }
            QilIterator result = _f.Let(Box(CompileInstructions(others)));
            QilNode CompileAll(List<XslNode> nodes)
            {
                QilList list = InstructionList();
                foreach (XslNode n in nodes)
                {
                    EnterScope(n);
                    list.Add(SelectOrContent31(n, n.Select));
                    ExitScope();
                }
                return list;
            }
            QilNode whenEmpty = CompileAll(onEmpty);
            QilNode whenNonEmpty = _f.Sequence(CompileAll(before), result, CompileAll(after));
            QilNode chosen = _f.Loop(result, _f.Conditional(X31(nameof(XsltLibrary.X31Vacuous), T.BooleanX, result), whenEmpty, whenNonEmpty));
            return attributes.Count == 0 ? chosen : _f.Sequence(CompileInstructions(attributes), chosen);
        }

        // xsl:where-populated: the content without the items deemed empty (XSLT 3.0 §8.4.1).
        private QilNode CompileWherePopulated31(XslNode node) =>
            X31(nameof(XsltLibrary.X31Populated), T.ItemS, Box(CompileInstructions(node.Content)));

        // ------------------------------------------------------------ modes

        /// <summary>
        /// The built-in template rules of a mode (XSLT 3.0 §6.7): shallow-copy and shallow-skip apply templates to
        /// the attributes and children of documents and elements; deep-copy copies the node; deep-skip does nothing;
        /// fail is XTDE0555.
        /// </summary>
        private QilNode BuiltinRules31(string onNoMatch, QilIterator current, QilName mode, QilNode applyToChildren)
        {
            switch (onNoMatch)
            {
                case "deep-skip":
                    return _f.Sequence();
                case "fail":
                    return _f.TypeAssert(X31(nameof(XsltLibrary.X31Raise), T.ItemS, _f.String("XTDE0555"),
                        _f.String($"No template rule matches the node in mode {(mode.LocalName.Length == 0 ? "#unnamed" : mode.QualifiedName)}")), ModeType31(mode));
                case "deep-copy":
                    return _f.XsltCopyOf(current);
            }
            QilNode attributesThenChildren = _f.Sequence(BuiltinApply31(current, mode, attributes: true), applyToChildren);
            if (onNoMatch == "shallow-skip")
            {
                return _f.Conditional(_f.IsType(current, T.DocumentOrElement), attributesThenChildren, _f.Sequence());
            }
            // shallow-copy
            return _f.Conditional(_f.IsType(current, T.Element), _f.XsltCopy(current, ContentNodes(attributesThenChildren)),
                _f.Conditional(_f.IsType(current, T.Document), _f.XsltCopy(current, ContentNodes(BuiltinApply31(current, mode, attributes: false))),
                    _f.XsltCopy(current, _f.Sequence())));
        }

        // Functions applying the template rules of a mode to items that aren't nodes, by mode.
        private readonly Dictionary<(StylesheetLevel, QilName, Template?), List<QilFunction>> _itemApplyFunctions31 = new();

        /// <summary>
        /// xsl:apply-templates to an item that isn't a node (XSLT 3.0 §6.8): a function per mode (and set of
        /// parameters) that tries the template rules whose patterns are predicate patterns, then the built-in rules.
        /// </summary>
        private QilNode InvokeItemApplyFunction31(StylesheetLevel sheet, QilName mode, IList<XslNode> actualArgs, XslFlags flags, Template? nextMatchBelow)
        {
            QilList invokeArgs = _f.ActualParameterList();
            if (!_itemApplyFunctions31.TryGetValue((sheet, mode, nextMatchBelow), out List<QilFunction>? functions))
            {
                functions = _itemApplyFunctions31[(sheet, mode, nextMatchBelow)] = new List<QilFunction>();
            }
            foreach (QilFunction func in functions)
            {
                if (FillupInvokeArgs(func.Arguments, actualArgs, invokeArgs))
                {
                    return _f.Invoke(func, invokeArgs);
                }
            }
            invokeArgs.Clear();
            QilList formalArgs = _f.FormalParameterList();
            for (int i = 0; i < actualArgs.Count; i++)
            {
                VarPar withParam = (VarPar)actualArgs[i];
                invokeArgs.Add(withParam.Value!);
                QilParameter formalArg = _f.Parameter(i == 0 ? T.Item : withParam.Value!.XmlType!);
                formalArg.Name = CloneName(withParam.Name!);
                formalArgs.Add(formalArg);
                withParam.Value = formalArg;
            }
            XmlQueryType type = ModeType31(mode);
            QilFunction function = _f.Function(formalArgs, _f.Boolean((flags & XslFlags.SideEffects) != 0), type);
            function.DebugName = $"<xsl:apply-templates{(mode.LocalName.Length == 0 ? "" : $" mode=\"{mode.QualifiedName}\"")}> (items)";
            functions.Add(function);
            _functions.Add(function);

            QilIterator current = (QilIterator)formalArgs[0];
            QilNode? savedTunnel = _currentTunnel;
            foreach (QilNode formal in formalArgs)
            {
                if (formal is QilParameter fp && fp.Name!.Equals(_nameTunnel))
                {
                    _currentTunnel = fp;
                }
            }
            string onNoMatch = _compiler.ModeDecls.TryGetValue(mode, out ModeDecl? decl) ? decl.OnNoMatch : "text-only-copy";
            QilNode text = _f.TextCtor(X31(nameof(XsltLibrary.X31ItemText), T.StringX, current));
            QilNode builtin = onNoMatch switch
            {
                "shallow-skip" or "deep-skip" => _f.Sequence(),
                "fail" => _f.TypeAssert(X31(nameof(XsltLibrary.X31Raise), T.ItemS, _f.String("XTDE0555"),
                    _f.String($"No template rule matches the item in mode {(mode.LocalName.Length == 0 ? "#unnamed" : mode.QualifiedName)}")), type),
                "shallow-copy" or "deep-copy" when type == T.ItemS => current,
                _ => text,
            };
            _matcherBuilder.CollectPatterns(sheet, mode, nextMatchBelow);
            function.Definition = _matcherBuilder.BuildItemMatcher(current, actualArgs, builtin);
            _currentTunnel = savedTunnel;
            return _f.Invoke(function, invokeArgs);
        }

        // apply-templates to the attributes (or the children) of the current node, in the same mode.
        private QilNode BuiltinApply31(QilIterator current, QilName mode, bool attributes)
        {
            QilIterator child = _f.For(_f.Content(current));
            QilNode filter = _f.Filter(child, _f.IsType(child, attributes ? T.Attribute : T.Content));
            filter.XmlType = attributes ? T.AttributeS : T.ContentS;
            LoopFocus saved = _curLoop;
            _curLoop.SetFocus(_f.For(filter));
            QilNode body = InvokeApplyFunction(_compiler.Root!, mode, null);
            QilNode loop = _curLoop.ConstructLoop(body);
            _curLoop = saved;
            return loop;
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
            if (output.Method is { Namespace.Length: 0, Name: "json" or "adaptive" } raw)
            {
                // Serialization 3.1 §10, §11: the raw result is serialized, not a tree.
                extras.RawMethod = raw.Name;
                extras.ItemSeparator = output.ItemSeparator;
                extras.JsonNodeOutputMethod = output.JsonNodeOutputMethod;
                extras.AllowDuplicateNames = output.AllowDuplicateNames;
                extras.Indent = output.Settings.Indent;
            }
            // HTML5: html-version="5", or version="5.0" with the html method (Serialization 3.1 §7.1).
            bool html = output.Settings.OutputMethod == XmlOutputMethod.Html;
            string? htmlVersion = output.HtmlVersion ?? (html ? output.Version : null);
            if ((html || output.Xhtml) && htmlVersion != null && htmlVersion.StartsWith('5'))
            {
                extras.Html5 = true;
                extras.HtmlMethod = html;
                extras.DefaultDoctype = output.Settings.DocTypeSystem == null;
            }
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

        // What the iterator of a predicate pattern ranges over (it is replaced by the item matched).
        private QilNode? _fixupItem31;

        // Compiling the pattern of a template rule (whose predicate patterns may match any item).
        private bool _templatePattern31;

        /// <summary>
        /// A pattern as the 1.0 pattern builder delivers it: a Filter over the fixup node per alternative (a
        /// Sequence of them if there are several), each annotated with its default priority.
        /// </summary>
        private QilNode CompilePattern31(string pattern)
        {
            QilIterator? saved = _patternNode31;
            try
            {
                XPath31.Expr ast = Parser.ParseXPath(pattern);
                if (XPath31Compiler.IsPredicatePattern(ast) && WithoutComments31(pattern).TrimStart().StartsWith('('))
                {
                    return PatternError("XTSE0340", "A predicate pattern can't be parenthesized");
                }
                List<PatternAlternative> alternatives = XPath31Compiler.CompilePattern(ast, StaticContext31(), _f, this,
                    items => _patternNode31 = _f.For(items && _templatePattern31 ? _fixupItem31 ??= _f.Unknown(T.ItemS) : _ptrnBuilder.FixupNode));
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

        private static string WithoutComments31(string xpath)
        {
            string previous;
            do
            {
                previous = xpath;
                xpath = System.Text.RegularExpressions.Regex.Replace(xpath, @"\(:(?:(?!\(:|:\)).)*:\)", " ", System.Text.RegularExpressions.RegexOptions.Singleline);
            }
            while (xpath != previous);
            return xpath;
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
                EnterComponent31(key);
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
            foreach (var ((functionName, functionArity), (function, _)) in _compiler.Functions)
            {
                EnterScope(function);
                EnterComponent31(function);
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
                if (PackageComponents.IsAbstractWithoutOverride(_compiler, 'F', function, functionName, functionArity))
                {
                    // An abstract function that no package overrides (XSLT 3.0 §3.5.3.1).
                    result = X31(nameof(XsltLibrary.X31Raise), T.ItemS, _f.String("XTDE3052"), _f.String($"The abstract function {functionName.LocalName} isn't overridden"));
                    prologue = null;
                }
                function.Function!.Definition = prologue ?? result;
                _noFocus = savedNoFocus;
                _temporaryOutput31--;
                ExitScope();
            }
        }
    }
}
