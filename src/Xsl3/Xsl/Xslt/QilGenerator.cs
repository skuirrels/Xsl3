// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// <spec>http://www.w3.org/TR/xslt.html</spec>
// <spec>http://www.w3.org/TR/xslt20/</spec>
//------------------------------------------------------------------------------

using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Text;
using Xsl3.Qil;
using Xsl3.Runtime;
using Xsl3.XPath;
using ScopeRecord = Xsl3.Xslt.CompilerScopeManager<Xsl3.Qil.QilIterator>.ScopeRecord;
using T = Xsl3.XmlQueryTypeFactory;

namespace Xsl3.Xslt
{
    // Everywhere in this code in case of error in the stylesheet we should call ReportError or ReportWarning

    internal sealed class ReferenceReplacer : QilReplaceVisitor
    {
        private QilReference? _lookFor, _replaceBy;

        public ReferenceReplacer(QilFactory f) : base(f)
        {
        }

        public QilNode Replace(QilNode expr, QilReference lookFor, QilReference replaceBy)
        {
            QilDepthChecker.Check(expr);
            _lookFor = lookFor;
            _replaceBy = replaceBy;
            return VisitAssumeReference(expr);
        }

        protected override QilNode VisitReference(QilNode n)
        {
            return (n == _lookFor) ? _replaceBy! : n;
        }
    }

    internal sealed partial class QilGenerator : IErrorHelper
    {
        private readonly CompilerScopeManager<QilIterator> _scope;
        private readonly OutputScopeManager _outputScope;
        private readonly HybridDictionary _prefixesInUse;

        private readonly XsltQilFactory _f;
        private readonly XPathBuilder _xpathBuilder;
        private readonly XPathParser<QilNode> _xpathParser;
        private readonly XPathPatternBuilder _ptrnBuilder;
        private readonly XPathPatternParser _ptrnParser;
        private readonly ReferenceReplacer _refReplacer;
        private KeyMatchBuilder? _keyMatchBuilder;
        private readonly InvokeGenerator _invkGen;
        private readonly MatcherBuilder _matcherBuilder;
        private readonly QilStrConcatenator _strConcat;
        private readonly VariableHelper _varHelper;

        private Compiler _compiler = null!;
        private QilList _functions = null!;
        private QilFunction? _generalKey;
        private bool _formatNumberDynamicUsed;
        private QilList _extPars = null!;
        private QilList _gloVars = null!;
        private QilList _nsVars = null!;

        private readonly XmlQueryType _elementOrDocumentType;
        private readonly XmlQueryType _textOrAttributeType;
        private XslNode? _lastScope;
        private XslVersion _xslVersion;

        private readonly QilName _nameCurrent;
        private readonly QilName _namePosition;
        private readonly QilName _nameLast;
        private readonly QilName _nameNamespaces;
        private readonly QilName _nameInit;

        private SingletonFocus _singlFocus;
        private FunctionFocus _funcFocus;
        private LoopFocus _curLoop;

        private int _formatterCnt;

        public static QilExpression CompileStylesheet(Compiler compiler)
        {
            return new QilGenerator(compiler.IsDebug).Compile(compiler);
        }

        private QilGenerator(bool debug)
        {
            _scope = new CompilerScopeManager<QilIterator>();
            _outputScope = new OutputScopeManager();
            _prefixesInUse = new HybridDictionary();
            _f = new XsltQilFactory(new QilFactory(), debug);
            _xpathBuilder = new XPathBuilder((IXPathEnvironment)this);
            _xpathParser = new XPathParser<QilNode>();
            _ptrnBuilder = new XPathPatternBuilder((IXPathEnvironment)this);
            _ptrnParser = new XPathPatternParser();
            _refReplacer = new ReferenceReplacer(_f.BaseFactory);
            _invkGen = new InvokeGenerator(_f, debug);
            _matcherBuilder = new MatcherBuilder(_f, _refReplacer, _invkGen);
            _singlFocus = new SingletonFocus(_f);
            _funcFocus = default;
            _curLoop = new LoopFocus(_f);
            _strConcat = new QilStrConcatenator(_f);
            _varHelper = new VariableHelper(_f);

            _elementOrDocumentType = T.DocumentOrElement;
            _textOrAttributeType = T.NodeChoice(XmlNodeKindFlags.Text | XmlNodeKindFlags.Attribute);

            _nameCurrent = _f.QName("current", XmlReservedNs.NsXslDebug);
            _namePosition = _f.QName("position", XmlReservedNs.NsXslDebug);
            _nameLast = _f.QName("last", XmlReservedNs.NsXslDebug);
            _nameNamespaces = _f.QName("namespaces", XmlReservedNs.NsXslDebug);
            _nameInit = _f.QName("init", XmlReservedNs.NsXslDebug);

            _formatterCnt = 0;
        }

        private bool IsDebug
        {
            get { return _compiler.IsDebug; }
        }

        private bool EvaluateFuncCalls { get { return !IsDebug; } }
        private bool InferXPathTypes { get { return !IsDebug; } }

        private QilExpression Compile(Compiler compiler)
        {
            Debug.Assert(compiler != null);
            _compiler = compiler;
            if (IsV2 && !IsDebug)
            {
                _scope.Referenced = new HashSet<QilIterator>(ReferenceEqualityComparer.Instance);
            }
            _functions = _f.FunctionList();
            _extPars = _f.GlobalParameterList();
            _gloVars = _f.GlobalVariableList();
            _nsVars = _f.GlobalVariableList();

            // Refactor huge templates into smaller ones (more JIT friendly)
            (new XslAstRewriter()).Rewrite(compiler);

            if (!IsDebug)
            {
                (new XslAstAnalyzer()).Analyze(compiler);
            }
            if (IsV2)
            {
                // Xsl3: the analyzer reads expressions with the 1.0 parser, so 2.0 templates get the full focus.
                SetFullFocusV2(compiler.Root!);
            }

            // Global variables and external params are visible from everywhere, so we have
            // to prepopulate the scope with all of them before starting compilation
            CreateGlobalVarPars();

            try
            {
                if (IsV2)
                {
                    // Patterns may call key() and stylesheet functions; key definitions may apply templates.
                    _dynamicCurrentMode31 = compiler.AllTemplates.Any(t => !HasStaticMode31(t) && UsesCurrentMode31(t.Content, t is Template { Match: not null }));
                    _rules31 = compiler.AllTemplates.OfType<Template>().Where(t => t.Match != null).ToList();
                    _callerRule31 = compiler.AllTemplates.Any(t => (t is Template { Match: null } || t is AttributeSet) && UsesNextMatch31(t.Content));
                    CreateKeyFunctions31();
                    PrecompileFunctions31();
                    CompileAndSortMatches(compiler.Root!.Imports![0]);
                    PrecompileProtoTemplatesHeaders();
                    CompileKeyBodies31();
                }
                else
                {
                    CompileKeys();
                    CompileAndSortMatches(compiler.Root!.Imports![0]);
                    PrecompileProtoTemplatesHeaders();
                }
                CompileGlobalVariables();

                foreach (ProtoTemplate tmpl in compiler.AllTemplates)
                {
                    CompileProtoTemplate(tmpl);
                }
                if (IsV2)
                {
                    CompileFunctions31();
                    CompileAccumulators31();
                    _functionItems31?.Finish();
                }
                _varHelper.CheckEmpty();
            }
            catch (XslLoadException e)
            {
                e.SetSourceLineInfo(_lastScope!.SourceLine);
                throw;
            }
            catch (Exception e)
            {
                if (!XmlException.IsCatchableException(e))
                {
                    throw;
                }
                throw new XslLoadException(e, _lastScope!.SourceLine);
            }

            CompileInitializationCode();
            QilNode root = CompileRootExpression(compiler.StartApplyTemplates!);

            // Clean default values which we calculate in caller context
            foreach (ProtoTemplate tmpl in compiler.AllTemplates)
            {
                foreach (QilParameter par in tmpl.Function!.Arguments)
                {
                    if (!IsDebug || par.Name!.Equals(_nameNamespaces))
                    {
                        par.DefaultValue = null;
                    }
                }
            }

            // Create list of all early bound objects
#pragma warning disable SYSLIB0062 // Scripts field is obsolete
            Scripts.TrimSafeDictionary scriptClasses = compiler.Scripts.ScriptClasses;
#pragma warning restore SYSLIB0062
            List<EarlyBoundInfo> ebTypes = new List<EarlyBoundInfo>(scriptClasses.Count);
            foreach (string key in scriptClasses.Keys)
            {
                Type? value = scriptClasses[key];
                if (value != null)
                {
                    ebTypes.Add(new EarlyBoundInfo(key, value));
                }
            }

            QilExpression qil = _f.QilExpression(root, _f.BaseFactory);
            {
                qil.EarlyBoundTypes = ebTypes;
                qil.FunctionList = _functions;
                qil.GlobalParameterList = _extPars;
                qil.GlobalVariableList = _gloVars;
                qil.WhitespaceRules = compiler.WhitespaceRules;
                qil.IsDebug = IsDebug;
                qil.MaxRecursionDepth = compiler.Settings.MaxRecursionDepth;
                qil.DefaultWriterSettings = compiler.Output.Settings;
                if (IsV2)
                {
                    ValidateCharacterMaps31();
                    (qil.DefaultWriterSettings, qil.OutputExtras) = WithParameterDocument31(compiler.Output);
                    // The principal result writer is needed for xsl:result-document, and to choose the xhtml method when
                    // the result starts with XHTML's html element and no method is declared (XSLT 3.0 §26.1).
                    qil.ResultDocuments = _resultDocuments31 || compiler.XhtmlHtmlLiteral && compiler.Output.Method == null;
                    qil.Xslt2 = true;
                    qil.Dispatcher = _functionItems31?.DispatcherIfUsed;
                    qil.Frames = _frames31;
                }
            }

            QilDepthChecker.Check(qil);

            return qil;
        }

        private QilNode InvokeOnCurrentNodeChanged()
        {
            Debug.Assert(IsDebug && _curLoop.IsFocusSet);
            return _f.Loop(_f.Let(_f.InvokeOnCurrentNodeChanged(_curLoop.GetCurrent()!)), _f.Sequence());
        }

        [Conditional("DEBUG")]
        private void CheckSingletonFocus()
        {
            Debug.Assert(!_curLoop.IsFocusSet && !_funcFocus.IsFocusSet, "Must be compiled using singleton focus");
        }

        private void CompileInitializationCode()
        {
            // Initialization code should be executed before any other code (global variables/parameters or root expression)
            // For this purpose we insert it as THE FIRST global variable $init (global variables are calculated before global parameters)
            // and put all initialization code in it.
            // In retail mode global variables are calculated lasely if they don't have side effects.
            // To mark $init as variable with side effect we put all code to function and set SideEffect flag on this function.
            // ILGen expects that all library functions are sideeffect free. To prevent calls to RegisterDecimalFormat() to be optimized out
            // we add results returned from these calls and return them as a result of initialization function.
            QilNode init = _f.Int32(0);

            // Register all decimal formats, they are needed for format-number()
            if (_formatNumberDynamicUsed || IsDebug)
            {
                bool defaultDefined = false;
                foreach (DecimalFormatDecl format in _compiler.DecimalFormats)
                {
                    init = _f.Add(init, _f.InvokeRegisterDecimalFormat(format));
                    defaultDefined |= (format.Name == DecimalFormatDecl.Default.Name);
                }
                if (!defaultDefined)
                {
                    init = _f.Add(init, _f.InvokeRegisterDecimalFormat(DecimalFormatDecl.Default));
                }
            }

            // Xsl3 (P6): input-type-annotations="strip": the source documents' type annotations are ignored (XSLT 3.0 §4.4.1).
            if (IsV2 && _compiler.inputTypeAnnotations == "strip" && _compiler.Settings.SchemaAware)
            {
                init = _f.Add(init, XPath31.XPath31Compiler.Invoke(_f, nameof(XsltLibrary.X31StripInputAnnotations), T.IntX));
            }
            if (IsV2 && _compiler.Settings.SchemaAware)
            {
                init = _f.Add(init, XPath31.XPath31Compiler.Invoke(_f, nameof(XsltLibrary.X31SchemaAware), T.IntX));
            }
            // Xsl3 (P6): default-validation="preserve": nodes copied into constructed trees keep their type annotations.
            if (IsV2 && _compiler.DefaultValidation == "preserve" && _compiler.Settings.SchemaAware)
            {
                init = _f.Add(init, XPath31.XPath31Compiler.Invoke(_f, nameof(XsltLibrary.X31PreserveAnnotations), T.IntX));
            }

            // Register all script namespaces
#pragma warning disable SYSLIB0062 // Scripts field is obsolete
            foreach (string scriptNs in _compiler.Scripts.ScriptClasses.Keys)
            {
                init = _f.Add(init, _f.InvokeCheckScriptNamespace(scriptNs));
            }
#pragma warning restore SYSLIB0062

            if (init.NodeType == QilNodeType.Add)
            {
                QilFunction initFunction = _f.Function(_f.FormalParameterList(), init, /*sideEffects:*/_f.True());
                initFunction.DebugName = "Init";
                _functions.Add(initFunction);

                QilNode initBinding = _f.Invoke(initFunction, _f.ActualParameterList());
                if (IsDebug)
                {
                    // In debug mode all variables must have type item*
                    initBinding = _f.TypeAssert(initBinding, T.ItemS);
                }
                QilIterator initVar = _f.Let(initBinding);
                initVar.DebugName = _nameInit.ToString();
                _gloVars.Insert(0, initVar);
            }
        }

        private QilNode CompileRootExpression(XslNode applyTmpls)
        {
            // Compile start apply-templates call
            CheckSingletonFocus();
            _package31 = 0; // Xsl3: the entry point is the principal package's
            _component31 = null;
            _singlFocus.SetFocus(SingletonFocusType.InitialContextNode);
            // Xsl3: the entry point is apply-templates in a mode, a call of the initial named template, or a call of the
            // initial function.
            QilNode result = IsV2 && _compiler.InitialFunction != null ? CompileInitialFunction31()
                : IsV2 && UnknownInitialMode31(applyTmpls) is string unknown
                ? X31(nameof(XsltLibrary.X31Raise), T.ItemS, _f.String("XTDE0045"), _f.String($"The initial mode {unknown} has no template rules and isn't declared"))
                : applyTmpls.NodeType == XslNodeType.CallTemplate
                ? CompileInitialCall31((XslNodeEx)applyTmpls)
                : IsV2 && applyTmpls.Select != null
                ? CompileApplyTemplates31((XslNodeEx)applyTmpls)
                : GenerateApply(_compiler.Root!, applyTmpls);
            if (IsV2 && _compiler.GlobalContextItem is ("required", string requiredType))
            {
                // Xsl3: xsl:global-context-item use="required" as="…": the global context item must be of the type.
                result = _f.Sequence(X31(nameof(XsltLibrary.X31Discard), T.ItemS, InstanceCheck31(GetCurrentNode(), requiredType, "XTTE0590")), result);
            }
            if (IsV2 && _compiler.GlobalContextItem is ("required", _))
            {
                // Xsl3: use="required" and no global context item supplied: XTDE3086, whether or not anything uses it.
                result = _f.Sequence(X31(nameof(XsltLibrary.X31RequireGlobalContextItem), T.ItemS, GetCurrentNode()), result);
            }
            if (IsV2 && _compiler.PackageInfos.Count <= 1)
            {
                // Xsl3: a required stylesheet parameter must be supplied even if it is never used (XTDE0050). (With
                // packages, an override may make it optional: those are still checked when used.)
                foreach (VarPar par in _compiler.ExternalPars)
                {
                    if (par.NodeType == XslNodeType.Param && par.V2 is { Required: true, StaticValue: null } && par.Value is QilIterator it)
                    {
                        result = _f.Sequence(X31(nameof(XsltLibrary.X31Discard), T.ItemS, Box(it)), result);
                    }
                }
            }
            _singlFocus.SetFocus(null);

            if (IsV2 && (_compiler.RawResult || _compiler.Output.Method is { Namespace.Length: 0, Name: "json" or "adaptive" } || _compiler.Output.ItemSeparator != null))
            {
                // Xsl3: the json and adaptive methods serialize the raw result (XslCompiledTransform.TransformRaw).
                return Box(result);
            }
            return _f.DocumentCtor(IsV2 ? ContentNodes(result) : result);
        }

        // Xsl3: the initial call of a named template: without an input (XslCompiledTransform.AbsentInput) it has no
        // context item.
        private QilNode CompileInitialCall31(XslNodeEx call)
        {
            _initialCall31 = true;
            try
            {
                QilNode result = CompileCallTemplate(call);
                if (IsV2 && ResolveComponent31('T', call.Name!, -1) is QilName called && _compiler.NamedTemplates.TryGetValue(called, out Template? tmpl))
                {
                    // A required parameter of the initial template must be supplied, even if it isn't used (XTDE0700).
                    foreach (XslNode p in tmpl.Content)
                    {
                        if (p is { NodeType: XslNodeType.Param, V2.Required: true } &&
                            !call.Content.Any(w => w.Name!.Equals(p.Name) && (w.V2?.Tunnel ?? false) == p.V2.Tunnel))
                        {
                            result = _f.Sequence(X31(nameof(XsltLibrary.X31Raise), T.ItemS, _f.String("XTDE0700"),
                                _f.String($"The initial template's required parameter ${p.Name!.QualifiedName} was not supplied")), result);
                            break;
                        }
                    }
                }
                return result;
            }
            finally
            {
                _initialCall31 = false;
            }
        }

        private QilList? EnterScope(XslNode node)
        {
            // This is the only place where lastScope is changed
            _lastScope = node;
            _xslVersion = node.XslVersion;
            if (_scope.EnterScope(node.Namespaces))
            {
                return BuildDebuggerNamespaces();
            }
            return null;
        }

        private void ExitScope()
        {
            _scope.ExitScope();
        }

        private QilList? BuildDebuggerNamespaces()
        {
            if (IsDebug)
            {
                QilList nsDecls = _f.BaseFactory.Sequence();
                foreach (ScopeRecord rec in _scope)
                {
                    nsDecls.Add(_f.NamespaceDecl(_f.String(rec.ncName!), _f.String(rec.nsUri!)));
                }
                return nsDecls;
            }
            return null;
        }

        // For each call instruction - call-template, use-attribute-sets, apply-template, apply-imports - we have
        // to pass the current execution context which may be represented as three additional implicit arguments:
        // current, position, last.  In most cases the last two ones are never used, so for the purpose
        // of optimization in non-debug mode we bind them only if they are actually needed.

        // Strictly speaking, a (proto)template function is supplied with the additional position argument if both
        // the following conditions are true:
        //   1. At least one template within the given stylesheet contains a "naked" position() function invocation
        //      (needPositionArgs == true).  Naked here means "not within any of for-each instructions".
        //   2. THIS template contains a naked position() invocation or a naked call-template, use-attribute-sets,
        //      or apply-imports instruction.  Note: apply-template's are not taken into account because in that
        //      case the call will be actually wrapped in a tuple.
        //
        // The same is true for additional last arguments.

        // There are 3 cases when context methods may be called:
        // 1. In context of for-each expression
        // 2. In context of template
        // 3. In context of global variable
        // We treating this methods differentely when they are called to create implicit arguments.
        // Implicite argument (position, last) are rare and lead to uneficiant code. So we treating them
        // specially to be able eliminate them later, wen we compiled everithing and can detect was they used or not.

        // Returns context node
        private QilNode GetCurrentNode()
        {
            if (_nodeContext31 != null)
            {
                return _nodeContext31; // Xsl3: a node-typed context for xsl:number in 2.0 (an iterator, so shareable)
            }
            if (_curLoop.IsFocusSet)
            {
                // Xsl3: in 2.0 grouping and analyze-string loops the context item is derived from the loop item.
                return (IsV2 ? FocusOverride() : null) ?? _curLoop.GetCurrent()!;
            }
            else if (_fixedFocus31 is var (current, _, _))
            {
                return current; // Xsl3: the body of xsl:iterate
            }
            else if (_funcFocus.IsFocusSet)
            {
                return _funcFocus.GetCurrent();
            }
            else
            {
                return _singlFocus.GetCurrent();
            }
        }

        // Returns context position
        private QilNode GetCurrentPosition()
        {
            if (_curLoop.IsFocusSet)
            {
                return _curLoop.GetPosition();
            }
            else if (_fixedFocus31 is var (_, position, _))
            {
                return position;
            }
            else if (_funcFocus.IsFocusSet)
            {
                return _funcFocus.GetPosition();
            }
            else
            {
                return _singlFocus.GetPosition();
            }
        }

        // Returns context size
        private QilNode GetLastPosition()
        {
            if (_curLoop.IsFocusSet)
            {
                return _curLoop.GetLast();
            }
            else if (_fixedFocus31 is var (_, _, last))
            {
                return last;
            }
            else if (_funcFocus.IsFocusSet)
            {
                return _funcFocus.GetLast();
            }
            else
            {
                return _singlFocus.GetLast();
            }
        }

        private XmlQueryType ChooseBestType(VarPar var)
        {
            if (IsDebug || !InferXPathTypes || IsV2)
            {
                return T.ItemS;
            }

            switch (var.Flags & XslFlags.TypeFilter)
            {
                case XslFlags.String: return T.StringX;
                case XslFlags.Number: return T.DoubleX;
                case XslFlags.Boolean: return T.BooleanX;
                case XslFlags.Node: return T.NodeNotRtf;
                case XslFlags.Nodeset: return T.NodeNotRtfS;
                case XslFlags.Rtf: return T.Node;
                case XslFlags.Node | XslFlags.Rtf: return T.Node;
                case XslFlags.Node | XslFlags.Nodeset: return T.NodeNotRtfS;
                case XslFlags.Nodeset | XslFlags.Rtf: return T.NodeS;
                case XslFlags.Node | XslFlags.Nodeset | XslFlags.Rtf: return T.NodeS;
                default: return T.ItemS;
            }
        }

        // In debugger we need to pass to each (almost) template $namespace parameter with list of namespaces that
        // are defined on stylesheet and this template. In most cases this will be only xmlns:xsl="..."
        // To prevent creating these list with each call-template/apply-template we create one global variable for each unique set of namespaces
        // This function looks through list of existent global variables for suitable ns list and add one if none was found.
        private QilIterator GetNsVar(QilList nsList)
        {
            Debug.Assert(IsDebug, "This is debug only logic");
            // All global vars at this point are nsList like one we are looking now.
            foreach (QilIterator var in _nsVars)
            {
                Debug.Assert(var.XmlType!.IsSubtypeOf(T.NamespaceS));
                Debug.Assert(var.Binding is QilList);
                QilList varList = (QilList)var.Binding;
                if (varList.Count != nsList.Count)
                {
                    continue;
                }
                bool found = true;
                for (int i = 0; i < nsList.Count; i++)
                {
                    Debug.Assert(nsList[i].NodeType == QilNodeType.NamespaceDecl);
                    Debug.Assert(varList[i].NodeType == QilNodeType.NamespaceDecl);
                    if (
                        ((QilLiteral)((QilBinary)nsList[i]).Right).Value != ((QilLiteral)((QilBinary)varList[i]).Right).Value ||
                        ((QilLiteral)((QilBinary)nsList[i]).Left).Value != ((QilLiteral)((QilBinary)varList[i]).Left).Value
                    )
                    {
                        found = false;
                        break;
                    }
                }
                if (found)
                {
                    return var;  // Found!
                }
            }
            QilIterator newVar = _f.Let(nsList);
            newVar.DebugName = _f.QName($"ns{_nsVars.Count}", XmlReservedNs.NsXslDebug).ToString();
            _gloVars.Add(newVar);
            _nsVars.Add(newVar);
            return newVar;
        }

        private void PrecompileProtoTemplatesHeaders()
        {
            // All global variables should be in scoupe here.
            List<VarPar>? paramWithCalls = null;
            Dictionary<VarPar, Template>? paramToTemplate = null;
            Dictionary<VarPar, QilFunction>? paramToFunction = null;

            foreach (ProtoTemplate tmpl in _compiler.AllTemplates)
            {
                Debug.Assert(tmpl != null && tmpl.Function == null);
                Debug.Assert(tmpl.NodeType == XslNodeType.AttributeSet || tmpl.NodeType == XslNodeType.Template);
                QilList args = _f.FormalParameterList();
                XslFlags flags = !IsDebug ? tmpl.Flags : XslFlags.FullFocus;

                QilList? nsList = EnterScope(tmpl);
                if ((flags & XslFlags.Current) != 0)
                {
                    // Xsl3: in 2.0 a named template's context item may be atomic; a template rule's is a node.
                    // Xsl3: in 2.0 a named template's context item may be atomic; a template rule's is a node, unless
                    // its pattern is a predicate pattern (XSLT 3.0 §5.5.2).
                    args.Add(CreateXslParam(CloneName(_nameCurrent), IsV2 && !(tmpl is Template { Match: not null, Name: null } t && !Xsl3.XPath31.XPath31Compiler.MatchesItems(t.Match)) ? T.Item : T.NodeNotRtf));
                }
                if ((flags & XslFlags.Position) != 0)
                {
                    args.Add(CreateXslParam(CloneName(_namePosition), T.DoubleX));
                }
                if ((flags & XslFlags.Last) != 0)
                {
                    args.Add(CreateXslParam(CloneName(_nameLast), T.DoubleX));
                }
                if (IsV2)
                {
                    // Xsl3: tunnel parameters travel in one implicit argument (2.0).
                    args.Add(CreateXslParam(CloneName(_nameTunnel), T.ItemS));
                }
                if (tmpl is Template withAlternatives && _alternatives31 != null && _alternatives31.TryGetValue(withAlternatives, out List<TemplateMatch>? alternatives))
                {
                    // Xsl3: which alternative of the template's union pattern matched; -1 when it is called by name.
                    QilParameter alternative = CreateXslParam(CloneName(s_nameAlternative31), T.DoubleX);
                    alternative.DefaultValue = _f.Double(-1);
                    args.Add(alternative);
                    foreach (TemplateMatch match in alternatives)
                    {
                        VarPar arg = AstFactory.VarPar(XslNodeType.WithParam, CloneName(s_nameAlternative31), null, XslVersion.Current);
                        arg.Value = _f.Double(match.Alternative);
                        match.AlternativeArg = arg;
                    }
                }
                if (IsDebug && nsList != null)
                {
                    // AttributeSet doesn't need this logic because: 1) it doesn't have args; 2) we merge them.
                    // SimplifiedStylesheet has nsList == null as well.
                    QilParameter ns = CreateXslParam(CloneName(_nameNamespaces), T.NamespaceS);
                    ns.DefaultValue = GetNsVar(nsList);
                    args.Add(ns);
                }

                Template? template = tmpl as Template;
                if (template != null)
                {
                    Debug.Assert(tmpl.NodeType == XslNodeType.Template);

                    CheckSingletonFocus();
                    _funcFocus.StartFocus(args, flags);
                    for (int i = 0; i < tmpl.Content.Count; i++)
                    {
                        XslNode node = tmpl.Content[i];
                        if (node.NodeType is XslNodeType.Text or XslNodeType.ContextItem)
                        {
                            // NOTE: We should take care of a bizarre case when xsl:param comes after TextCtor:
                            // <xsl:template match="/" xml:space="preserve">  <xsl:param name="par"/>
                            continue;
                        }
                        if (node.NodeType == XslNodeType.Param)
                        {
                            VarPar xslPar = (VarPar)node;
                            if (IsV2 && xslPar.V2 is { Tunnel: true })
                            {
                                // Xsl3: bound from the tunnel argument in CompileProtoTemplate.
                                continue;
                            }
                            EnterScope(xslPar);
                            if (_scope.IsLocalVariable(xslPar.Name!.LocalName, xslPar.Name.NamespaceUri))
                            {
                                ReportError(/*[XT0580]*/SR.Xslt_DupLocalVariable, xslPar.Name.QualifiedName);
                            }
                            QilParameter param = CreateXslParam(xslPar.Name, ChooseBestType(xslPar));
                            if (IsDebug)
                            {
                                param.Annotation = xslPar;
                                // Actual compilation will happen in CompileProtoTemplate()
                            }
                            else
                            {
                                if (IsV2 && xslPar.V2 is { Required: true })
                                {
                                    // Xsl3: a required parameter that isn't supplied is a dynamic error here (XTSE0690 statically).
                                    param.DefaultValue = X31(nameof(XsltLibrary.X31Raise), T.ItemS, _f.String("XTDE0700"), _f.String($"The required parameter ${xslPar.Name.QualifiedName} was not supplied"));
                                }
                                else if ((xslPar.DefValueFlags & XslFlags.HasCalls) == 0)
                                {
                                    param.DefaultValue = CompileVarParValue(xslPar);
                                }
                                else
                                {
                                    // We can't compile param default value here because it contains xsl:call-template and
                                    // we will not be able to compile any calls befor we finish with all headers
                                    // So we compile this default value as a call to helper function. Now we create header for this function
                                    // and preserve this param in paramWithCall list. Later in this function we finaly compile all preserved
                                    // parameters and set resulted default values as helper function definition.
                                    QilList paramFormal = _f.FormalParameterList();
                                    QilList paramActual = _f.ActualParameterList();
                                    for (int j = 0; j < args.Count; j++)
                                    {
                                        QilParameter formal = _f.Parameter(args[j].XmlType!);
                                        {
                                            formal.DebugName = ((QilParameter)args[j]).DebugName!;
                                            formal.Name = CloneName(((QilParameter)args[j]).Name!);
                                            SetLineInfo(formal, args[j].SourceLine);
                                        }
                                        paramFormal.Add(formal);
                                        paramActual.Add(args[j]);
                                    }
                                    // Param doesn't know what implicit args it needs, so we pass all implicit args that was passed to its template.
                                    // let's reflect this fact in parans FocusFlags:
                                    xslPar.Flags |= (template.Flags & XslFlags.FocusFilter);
                                    QilFunction paramFunc = _f.Function(paramFormal,
                                        _f.Boolean((xslPar.DefValueFlags & XslFlags.SideEffects) != 0),
                                        ChooseBestType(xslPar)
                                    );
                                    paramFunc.SourceLine = SourceLineInfo.NoSource;
                                    paramFunc.DebugName = $"<xsl:param name=\"{xslPar.Name.QualifiedName}\">";
                                    param.DefaultValue = _f.Invoke(paramFunc, paramActual);
                                    // store VarPar here to compile it on next pass:
                                    if (paramWithCalls == null)
                                    {
                                        paramWithCalls = new List<VarPar>();
                                        paramToTemplate = new Dictionary<VarPar, Template>();
                                        paramToFunction = new Dictionary<VarPar, QilFunction>();
                                    }
                                    paramWithCalls.Add(xslPar);
                                    paramToTemplate!.Add(xslPar, template);
                                    paramToFunction!.Add(xslPar, paramFunc);
                                }
                            }
                            SetLineInfo(param, xslPar.SourceLine);
                            ExitScope();
                            _scope.AddVariable(xslPar.Name, param);
                            args.Add(param);
                            if (IsV2)
                            {
                                _paramDecls[param] = xslPar;
                            }
                        }
                        else
                        {
                            break;
                        }
                    }
                    _funcFocus.StopFocus();
                }
                ExitScope();

                tmpl.Function = _f.Function(args,
                    _f.Boolean((tmpl.Flags & XslFlags.SideEffects) != 0),
                    tmpl is AttributeSet ? T.AttributeS : IsV2 ? TemplateType31(tmpl) : T.NodeNotRtfS // Xsl3: 2.0 templates may return sequences
                );
                tmpl.Function.DebugName = tmpl.GetDebugName();
                Debug.Assert((template != null) == (tmpl.SourceLine != null), "Templates must have line information, and attribute sets must not");
                SetLineInfo(tmpl.Function, tmpl.SourceLine ?? SourceLineInfo.NoSource);
                _functions.Add(tmpl.Function);
            } // foreach (ProtoTemplate tmpl in compiler.AllTemplates)

            // Finish compiling postponed parameters (those having calls in their default values)
            if (paramWithCalls != null)
            {
                Debug.Assert(!IsDebug, "In debug mode we don't generate parumWithCalls functions. Otherwise focus flags should be adjusted");
                foreach (VarPar par in paramWithCalls)
                {
                    Template tmpl = paramToTemplate![par];
                    QilFunction func = paramToFunction![par];
                    CheckSingletonFocus();
                    _funcFocus.StartFocus(func.Arguments, par.Flags);
                    EnterScope(tmpl);
                    EnterScope(par);
                    foreach (QilParameter arg in func.Arguments)
                    {
                        _scope.AddVariable(arg.Name!, arg);
                    }
                    // Xsl3: the default is evaluated in the template: its current template rule, through the tunnel
                    // argument for a named template (xsl:apply-imports, xsl:next-match).
                    (Template? savedRule, bool savedNamed, QilNode? savedTunnel) = (_currentTemplateRule, _inNamedTemplate31, _currentTunnel);
                    if (IsV2)
                    {
                        _currentTemplateRule = tmpl.Match != null ? tmpl : null;
                        _inNamedTemplate31 = tmpl.Match == null;
                        _currentTunnel = func.Arguments.Cast<QilParameter>().FirstOrDefault(a => a.Name!.Equals(_nameTunnel));
                    }
                    func.Definition = CompileVarParValue(par);
                    (_currentTemplateRule, _inNamedTemplate31, _currentTunnel) = (savedRule, savedNamed, savedTunnel);
                    SetLineInfo(func.Definition, par.SourceLine);
                    ExitScope();
                    ExitScope();
                    _funcFocus.StopFocus();
                    _functions.Add(func);
                }
            }
        }

        private QilParameter CreateXslParam(QilName name, XmlQueryType xt)
        {
            QilParameter arg = _f.Parameter(xt);
            arg.DebugName = name.ToString();
            arg.Name = name;
            return arg;
        }

        private void CompileProtoTemplate(ProtoTemplate tmpl)
        {
            Debug.Assert(tmpl != null && tmpl.Function != null && tmpl.Function.Definition.NodeType == QilNodeType.Unknown);

            EnterScope(tmpl);
            EnterComponent31(tmpl);

            CheckSingletonFocus();
            _funcFocus.StartFocus(tmpl.Function.Arguments, !IsDebug ? tmpl.Flags : XslFlags.FullFocus);
            foreach (QilParameter arg in tmpl.Function.Arguments)
            {
                if (arg.Name!.NamespaceUri != XmlReservedNs.NsXslDebug)
                {
                    Debug.Assert(tmpl is Template, "Only templates can have explicit arguments");
                    if (IsDebug)
                    {
                        Debug.Assert(arg.DefaultValue == null, "Argument must not be compiled yet");
                        VarPar xslParam = (VarPar)arg.Annotation!;
                        QilList? nsListParam = EnterScope(xslParam);
                        arg.DefaultValue = CompileVarParValue(xslParam);
                        ExitScope();
                        arg.DefaultValue = SetDebugNs(arg.DefaultValue, nsListParam);
                    }
                    else
                    {
                        // in !IsDebug we compile argument default value in PrecompileProtoTemplatesHeaders()
                    }
                    _scope.AddVariable(arg.Name, arg);
                }
            }
            // Xsl3: xsl:next-match needs the template rule being compiled; 2.0 parameters are converted to their
            // types and tunnel parameters bound from the tunnel argument.
            _currentTemplateRule = tmpl is Template { Match: not null } rule ? rule : null;
            _modeRule31 = _currentTemplateRule;
            // (A named template with <xsl:context-item use="absent"/> has no current template rule either.)
            // An attribute set keeps its caller's too.
            _inNamedTemplate31 = tmpl is Template { Match: null, IsFunction: false } &&
                !tmpl.Content.Any(n => n.NodeType == XslNodeType.ContextItem && (string?)n.Arg == "absent") || tmpl is AttributeSet;
            List<QilIterator>? prologue = IsV2 ? BindTemplateParams31(tmpl) : null;
            QilIterator? ruleFrame = IsV2 ? WithCurrentRule31(_currentTemplateRule) : null;
            // Xsl3: <xsl:context-item use="absent"/>: the body has no focus, however the template is invoked (XPDY0002).
            bool savedNoFocus = _noFocus;
            if (IsV2 && tmpl.Content.Any(n => n.NodeType == XslNodeType.ContextItem && (string?)n.Arg == "absent"))
            {
                _noFocus = true;
            }
            _namedFocus31 = IsV2 && tmpl is Template { Match: null, IsFunction: false } && (XslFlags.Current & tmpl.Flags) != 0;
            _namedTemplateBody31 = IsV2 && tmpl is Template { Match: null, IsFunction: false };
            QilNode body = CompileInstructions(tmpl.Content);
            _namedTemplateBody31 = false;
            _namedFocus31 = false;
            _noFocus = savedNoFocus;
            if (IsV2)
            {
                body = FinishTemplate31(tmpl, body, prologue!);
                if (ruleFrame != null)
                {
                    body = _f.Loop(ruleFrame, body);
                }
            }
            _inNamedTemplate31 = false;
            if (IsV2 && tmpl.Function.XmlType == T.NodeNotRtfS)
            {
                body = ToNodes31(body);
            }
            if (IsV2 && tmpl.Name is QilName abstractName && (tmpl is Template || tmpl is AttributeSet) &&
                PackageComponents.IsAbstractWithoutOverride(_compiler, tmpl is Template ? 'T' : 'A', tmpl, abstractName, -1))
            {
                // Xsl3: an abstract template or attribute set that no package overrides (XSLT 3.0 §3.5.3.1).
                body = _f.TypeAssert(X31(nameof(XsltLibrary.X31Raise), T.ItemS, _f.String("XTDE3052"),
                    _f.String($"The abstract {(tmpl is Template ? "template" : "attribute set")} {abstractName.LocalName} isn't overridden")), tmpl.Function.XmlType!);
            }
            if (IsV2 && tmpl is Template { IsFunction: false } traced)
            {
                int frame = Frame31(tmpl.Function, "template", traced.Name, traced.Match, traced.Match != null ? traced.Mode : null, tmpl.SourceLine);
                if (_compiler.Settings.EnableTracing)
                {
                    body = Traced31(body, tmpl.Function.XmlType!, frame);
                }
            }
            tmpl.Function.Definition = body;
            _currentTemplateRule = null;
            _currentTunnel = null;
            // tmpl.Function.Definition = AddCurrentPositionLast(tmpl.Function.Definition); We don't mask Cur,Pos,Last parameters with Cur,Pos,Last wariables any more
            // tmpl.Function.Definition = SetDebugNs(tmpl.Function.Definition, nsList); We add it as parameter now.
            _funcFocus.StopFocus();

            ExitScope();
        }

        private QilList InstructionList()
        {
            return _f.BaseFactory.Sequence();
        }

        private QilNode CompileInstructions(IList<XslNode> instructions)
        {
            return CompileInstructions(instructions, 0, InstructionList());
        }

        private QilNode CompileInstructions(IList<XslNode> instructions, int from)
        {
            return CompileInstructions(instructions, from, InstructionList());
        }

        private QilNode CompileInstructions(IList<XslNode> instructions, QilList content)
        {
            return CompileInstructions(instructions, 0, content);
        }

        private QilNode CompileInstructions(IList<XslNode> instructions, int from, QilList content)
        {
            Debug.Assert(instructions != null);
            if (IsV2 && from == 0 && content.Count == 0 && !ReferenceEquals(instructions, _onEmptyList31) && HasOnEmpty31(instructions))
            {
                return CompileWithOnEmpty31(instructions);
            }
            for (int i = from; i < instructions.Count; i++)
            {
                XslNode node = instructions[i];
                XslNodeType nodeType = node.NodeType;
                if (nodeType == XslNodeType.Param)
                {
                    continue; // already compiled by CompileProtoTemplate()
                }
                QilList? nsList = EnterScope(node);
                QilNode result;

                // Xsl3: 2.0 instructions, and the 2.0 rules for those 1.0 has too.
                QilNode? v2Result = IsV2 ? CompileInstructionV2(node) : null;
                if (v2Result != null)
                {
                    result = v2Result;
                }
                else
                switch (nodeType)
                {
                    case XslNodeType.ApplyImports: result = CompileApplyImports(node); break;
                    case XslNodeType.ApplyTemplates: result = CompileApplyTemplates((XslNodeEx)node); break;
                    case XslNodeType.Attribute: result = CompileAttribute((NodeCtor)node); break;
                    case XslNodeType.CallTemplate: result = CompileCallTemplate((XslNodeEx)node); break;
                    case XslNodeType.Choose: result = CompileChoose(node); break;
                    case XslNodeType.Comment: result = CompileComment(node); break;
                    case XslNodeType.Copy: result = CompileCopy(node); break;
                    case XslNodeType.CopyOf: result = CompileCopyOf(node); break;
                    case XslNodeType.Element: result = CompileElement((NodeCtor)node); break;
                    case XslNodeType.Error: result = CompileError(node); break;
                    case XslNodeType.ForEach: result = CompileForEach((XslNodeEx)node); break;
                    case XslNodeType.If: result = CompileIf(node); break;
                    case XslNodeType.List: result = CompileList(node); break;
                    case XslNodeType.LiteralAttribute: result = CompileLiteralAttribute(node); break;
                    case XslNodeType.LiteralElement: result = CompileLiteralElement(node); break;
                    case XslNodeType.Message: result = CompileMessage(node); break;
                    case XslNodeType.Nop: result = CompileNop(); break;
                    case XslNodeType.Number: result = CompileNumber((Number)node); break;
                    //              case XslNodeType.Otherwise:         wrapped by Choose
                    //              case XslNodeType.Param:             already compiled by CompileProtoTemplate()
                    case XslNodeType.PI: result = CompilePI(node); break;
                    //              case XslNodeType.Sort:              wrapped by ForEach or ApplyTemplates, see CompileSorts()
                    //              case XslNodeType.Template:          global level element
                    case XslNodeType.Text: result = CompileText((Text)node); break;
                    case XslNodeType.UseAttributeSet: result = CompileUseAttributeSet(node); break;
                    case XslNodeType.ValueOf: result = CompileValueOf(node); break;
                    case XslNodeType.ValueOfDoe: result = CompileValueOfDoe(node); break;
                    case XslNodeType.Variable: result = CompileVariable(node); break;
                    //              case XslNodeType.WithParam:         wrapped by CallTemplate or ApplyTemplates, see CompileWithParam()
                    default: Debug.Fail($"Unexpected type of AST node: {nodeType}"); result = null; break;
                }

                ExitScope();
                Debug.Assert(result != null, "Result of compilation should not be null");
                if (IsV2 && node.V2 is { } validated && (validated.Validation is "strict" or "lax" || validated.ValidationType != null) && nodeType != XslNodeType.ResultDocument)
                {
                    result = Validate31(result, validated);
                }
                else if (IsV2 && node.V2?.Validation is string kept and ("preserve" or "strip") && nodeType is XslNodeType.LiteralElement or XslNodeType.Element or XslNodeType.Copy or XslNodeType.Document)
                {
                    // Xsl3 (P6): validation="preserve" or "strip" on a constructor: the nodes copied into it keep, or lose,
                    // their type annotations, whatever the stylesheet's default-validation.
                    result = _f.Sequence(
                        // (2: the element it makes is itself annotated xs:anyType, XSLT 3.0 §25.4.2.)
                        XPath31.XPath31Compiler.Invoke(_f, nameof(XsltLibrary.X31AnnotationScope), T.ItemS, _f.Double(kept == "preserve" ? nodeType == XslNodeType.Document ? 1 : 2 : 0)),
                        result,
                        XPath31.XPath31Compiler.Invoke(_f, nameof(XsltLibrary.X31AnnotationScope), T.ItemS, _f.Double(-1)));
                }
                if (result.NodeType == QilNodeType.Sequence && result.Count == 0)
                {
                    continue;
                }

                // Do not create sequence points for literal attributes and use-attribute-sets
                if (nodeType != XslNodeType.LiteralAttribute && nodeType != XslNodeType.UseAttributeSet)
                {
                    SetLineInfoCheck(result, node.SourceLine);
                }

                result = SetDebugNs(result, nsList);
                if (IsV2 && _tryBody31 > 0 && node.SourceLine is { Start.Line: > 0 } line &&
                    nodeType is not (XslNodeType.Variable or XslNodeType.LiteralAttribute or XslNodeType.UseAttributeSet or XslNodeType.Text))
                {
                    // Xsl3: in an xsl:try body, the instruction's line is recorded while it runs ($err:line-number).
                    result = TryLine31(line, result);
                }
                if (nodeType == XslNodeType.Variable)
                {
                    QilIterator var = _f.Let(result);
                    var.DebugName = node.Name!.ToString();
                    _scope.AddVariable(node.Name, var);
                    // Process all remaining instructions in the recursive call
                    QilNode rest = CompileInstructions(instructions, i + 1);
                    // Xsl3: a 2.0+ local variable nothing refers to isn't evaluated (XSLT 3.0 §9.11), so that,
                    // for example, its errors and circularities don't arise.
                    result = _scope.Referenced is { } referenced && !referenced.Contains(var) ? rest : _f.Loop(var, rest);
                    i = instructions.Count;
                }

                content.Add(result);
            }
            if (!IsDebug && content.Count == 1)
            {
                return content[0];
            }
            return content;
        }

        private QilNode CompileList(XslNode node)
        {
            return CompileInstructions(node.Content);
        }

        private QilNode CompileNop()
        {
            return _f.Nop(_f.Sequence());
        }

        private void AddNsDecl(QilList content, string prefix, string nsUri)
        {
            if (_outputScope.LookupNamespace(prefix) == nsUri)
            {
                return; // This prefix is already bound to required namespace. Nothing to do.
            }
            _outputScope.AddNamespace(prefix, nsUri);
            content.Add(_f.NamespaceDecl(_f.String(prefix), _f.String(nsUri)));
        }

        private QilNode CompileLiteralElement(XslNode node)
        {
            // IlGen requires that namespace declarations do not conflict with the namespace used by the element
            // constructor, see XmlILNamespaceAnalyzer.CheckNamespaceInScope() and SQLBUDT 389481, 389482. First
            // we try to replace all prefixes bound to aliases by result-prefixes of the corresponding
            // xsl:namespace-alias instructions. If there is at least one conflict, we leave all prefixes
            // untouched, changing only namespace URIs.
            bool changePrefixes = true;

        Start:
            _prefixesInUse.Clear();

            QilName qname = node.Name!;
            string prefix = qname!.Prefix;
            string nsUri = qname.NamespaceUri;

            _compiler.ApplyNsAliases(ref prefix!, ref nsUri, IsV2 ? _package31 : 0);
            if (changePrefixes)
            {
                _prefixesInUse.Add(prefix!, nsUri);
            }
            else
            {
                prefix = qname.Prefix;
            }

            _outputScope.PushScope();

            // Declare all namespaces that should be declared
            // <spec>http://www.w3.org/TR/xslt.html#literal-result-element</spec>
            QilList nsList = InstructionList();
            foreach (ScopeRecord rec in _scope)
            {
                string? recPrefix = rec.ncName;
                string? recNsUri = rec.nsUri;
                if (recPrefix != null && recPrefix.StartsWith('#'))
                {
                    continue; // Xsl3: xpath-default-namespace and default-collation, not namespaces
                }
                if (recNsUri != XmlReservedNs.NsXslt && !_scope.IsExNamespace(recNsUri!))
                {
                    _compiler.ApplyNsAliases(ref recPrefix, ref recNsUri!, IsV2 ? _package31 : 0);
                    if (changePrefixes)
                    {
                        if (_prefixesInUse.Contains(recPrefix!))
                        {
                            if ((string?)_prefixesInUse[recPrefix!] != recNsUri)
                            {
                                // Found a prefix conflict. Start again from the beginning leaving all prefixes untouched.
                                _outputScope.PopScope();
                                changePrefixes = false;
                                goto Start;
                            }
                        }
                        else
                        {
                            _prefixesInUse.Add(recPrefix!, recNsUri);
                        }
                    }
                    else
                    {
                        recPrefix = rec.ncName;
                    }
                    AddNsDecl(nsList, recPrefix!, recNsUri);
                }
            }

            QilNode content;
            if (IsV2)
            {
                // Xsl3: namespace declarations first, then the content under the 2.0 rules.
                nsList.Add(ContentNodes(CompileInstructions(node.Content)));
                content = nsList;
            }
            else
            {
                content = CompileInstructions(node.Content, nsList);
            }
            _outputScope.PopScope();

            qname.Prefix = prefix;
            qname.NamespaceUri = nsUri;
            return _f.ElementCtor(qname, content);
        }

        private QilNode CompileElement(NodeCtor node)
        {
            QilNode? qilNs = CompileStringAvt(node.NsAvt);
            QilNode qilName = CompileStringAvt(node.NameAvt);
            if (IsV2 && qilName.NodeType != QilNodeType.LiteralString)
            {
                // A computed name may have leading and trailing whitespace (it is collapsed, as an xs:QName is).
                qilName = _f.InvokeNormalizeSpace(qilName);
            }
            QilNode qname;

            if (qilName.NodeType == QilNodeType.LiteralString && (qilNs == null || qilNs.NodeType == QilNodeType.LiteralString))
            {
                string name = (string)(QilLiteral)qilName;
                string prefix, local, nsUri;

                bool isValid = _compiler.ParseQName(name, out prefix, out local, (IErrorHelper)this, "XTDE0820");

                if (qilNs == null)
                {
                    nsUri = isValid ? ResolvePrefix(/*ignoreDefaultNs:*/false, prefix) : _compiler.CreatePhantomNamespace();
                }
                else
                {
                    nsUri = (string)(QilLiteral)qilNs;
                }
                qname = _f.QName(local, nsUri, prefix);
            }
            else
            {           // Process AVT
                if (qilNs != null)
                {
                    qname = _f.StrParseQName(qilName, qilNs);
                }
                else
                {
                    qname = ResolveQNameDynamic(/*ignoreDefaultNs:*/false, qilName);
                }
            }

            _outputScope.PushScope();
            _outputScope.InvalidateAllPrefixes();
            QilNode content = CompileInstructions(node.Content);
            if (IsV2)
            {
                content = NoInherit31(node, ContentNodes(content));
            }
            _outputScope.PopScope();

            return _f.ElementCtor(qname, content);
        }

        private QilNode CompileLiteralAttribute(XslNode node)
        {
            QilName qname = node.Name!;
            string prefix = qname.Prefix;
            string nsUri = qname.NamespaceUri;
            // The default namespace do not apply directly to attributes
            if (prefix.Length != 0)
            {
                _compiler.ApplyNsAliases(ref prefix!, ref nsUri, IsV2 ? _package31 : 0);
            }
            qname.Prefix = prefix;
            qname.NamespaceUri = nsUri;
            return _f.AttributeCtor(qname, CompileTextAvt(node.Select!));
        }

        private QilNode CompileAttribute(NodeCtor node)
        {
            QilNode? qilNs = CompileStringAvt(node.NsAvt);
            QilNode qilName = CompileStringAvt(node.NameAvt);
            if (IsV2 && qilName.NodeType != QilNodeType.LiteralString)
            {
                // A computed name may have leading and trailing whitespace (it is collapsed, as an xs:QName is).
                qilName = _f.InvokeNormalizeSpace(qilName);
            }
            QilNode qname;
            bool explicitNamespace = false;

            if (qilName.NodeType == QilNodeType.LiteralString && (qilNs == null || qilNs.NodeType == QilNodeType.LiteralString))
            {
                string name = (string)(QilLiteral)qilName;
                string prefix, local, nsUri;

                bool isValid = _compiler.ParseQName(name, out prefix, out local, (IErrorHelper)this, "XTDE0850");

                if (qilNs == null)
                {
                    nsUri = isValid ? ResolvePrefix(/*ignoreDefaultNs:*/true, prefix) : _compiler.CreatePhantomNamespace();
                }
                else
                {
                    nsUri = (string)(QilLiteral)qilNs;
                    // if both name and ns are non AVT and this ns is already bind to the same prefix we can avoid resetting ns management
                    explicitNamespace = true;
                }
                // Check the case <xsl:attribute name="foo:xmlns" namespace=""/>
                if (name == "xmlns" || local == "xmlns" && nsUri.Length == 0)
                {
                    ReportError(/*[XT_031]*/SR.Xslt_XmlnsAttr, "name", name);
                }
                qname = _f.QName(local, nsUri, prefix);
            }
            else
            {
                // Process AVT
                if (qilNs != null)
                {
                    qname = _f.StrParseQName(qilName, qilNs);
                }
                else
                {
                    qname = ResolveQNameDynamic(/*ignoreDefaultNs:*/true, qilName);
                }
            }
            if (explicitNamespace)
            {
                // Optimization: attribute cannot change the default namespace
                _outputScope.InvalidateNonDefaultPrefixes();
            }
            return _f.AttributeCtor(qname, CompileInstructions(node.Content));
        }

        private readonly StringBuilder _unescapedText = new StringBuilder();

        private QilNode? ExtractText(string source, ref int pos)
        {
            Debug.Assert(pos < source.Length);
            int i, start = pos;

            _unescapedText.Length = 0;
            for (i = pos; i < source.Length; i++)
            {
                char ch = source[i];

                if (ch == '{' || ch == '}')
                {
                    if (i + 1 < source.Length && source[i + 1] == ch)
                    {     // "{{" or "}}"
                        // Double curly brace outside an expression is replaced by a single one
                        i++;
                        _unescapedText.Append(source, start, i - start);
                        start = i + 1;
                    }
                    else if (ch == '{')
                    {                                 // single '{'
                        // Expression encountered, returning
                        break;
                    }
                    else
                    {                                                // single '}'
                        // Single '}' outside an expression is an error
                        pos = source.Length;
                        if (_xslVersion != XslVersion.ForwardsCompatible)
                        {
                            ReportError(/*[XT0370]*/SR.Xslt_SingleRightBraceInAvt, source);
                            return null;
                        }
                        return _f.Error(_lastScope!.SourceLine, SR.Xslt_SingleRightBraceInAvt, source);
                    }
                }
            }

            Debug.Assert(i == source.Length || source[i] == '{');
            pos = i;
            if (_unescapedText.Length == 0)
            {
                return i > start ? _f.String(source.Substring(start, i - start)) : null;
            }
            else
            {
                _unescapedText.Append(source, start, i - start);
                return _f.String(_unescapedText.ToString());
            }
        }

        private QilNode CompileAvt(string source)
        {
            QilList result = _f.BaseFactory.Sequence();
            int pos = 0;
            while (pos < source.Length)
            {
                QilNode? fixedPart = ExtractText(source, ref pos);
                if (fixedPart != null)
                {
                    result.Add(fixedPart);
                }
                if (pos < source.Length)
                { // '{' encountered, parse an expression
                    pos++;
                    if (IsV2)
                    {
                        result.Add(CompileAvtExpression31(source, ref pos));
                        continue;
                    }
                    QilNode exp = CompileXPathExpressionWithinAvt(source, ref pos);
                    result.Add(_f.ConvertToString(exp));
                }
            }
            if (result.Count == 1)
            {
                return result[0];
            }
            return result;
        }

        [return: NotNullIfNotNull(nameof(avt))]
        private QilNode? CompileStringAvt(string? avt)
        {
            if (avt == null)
            {
                return null;
            }
            if (!avt.AsSpan().ContainsAny('{', '}'))
            {
                return _f.String(avt);
            }
            return _f.StrConcat(CompileAvt(avt));
        }

        private QilNode CompileTextAvt(string avt)
        {
            Debug.Assert(avt != null);
            if (!avt.AsSpan().ContainsAny('{', '}'))
            {
                return _f.TextCtor(_f.String(avt));
            }
            QilNode avtParts = CompileAvt(avt);
            if (avtParts.NodeType == QilNodeType.Sequence)
            {
                QilList result = InstructionList();
                foreach (QilNode node in avtParts)
                {
                    result.Add(_f.TextCtor(node));
                }
                return result;
            }
            else
            {
                return _f.TextCtor(avtParts);
            }
        }

        private QilNode CompileText(Text node)
        {
            if (IsV2 && _scope.LookupNamespace(XsltInput.ExpandTextPrefix) == "yes" && node.Select!.AsSpan().ContainsAny('{', '}'))
            {
                // Xsl3: a text value template (XSLT 3.0 §5.6.2): parsed like an attribute value template.
                // One text node with the parts' values concatenated.
                QilNode value = CompileStringAvt(node.Select)!;
                return node.Hints == SerializationHints.None ? _f.TextCtor(value) : _f.RawTextCtor(value);
            }
            if (node.Hints == SerializationHints.None)
                return _f.TextCtor(_f.String(node.Select!));

            return _f.RawTextCtor(_f.String(node.Select!));
        }

        private QilNode CompilePI(XslNode node)
        {
            QilNode qilName = CompileStringAvt(node.Select!);
            if (qilName.NodeType == QilNodeType.LiteralString)
            {
                string name = (string)(QilLiteral)qilName;
                Compiler.ValidatePiName(name, (IErrorHelper)this);
            }
            return _f.PICtor(qilName, CompileInstructions(node.Content));
        }

        private QilNode CompileComment(XslNode node)
        {
            return _f.CommentCtor(CompileInstructions(node.Content));
        }

        private QilNode CompileError(XslNode node)
        {
            return _f.Error(_f.String(node.Select!));
        }

        private QilNode WrapLoopBody(ISourceLineInfo? before, QilNode expr, ISourceLineInfo? after)
        {
            Debug.Assert(_curLoop.IsFocusSet);
            if (IsDebug)
            {
                return _f.Sequence(
                    SetLineInfo(InvokeOnCurrentNodeChanged(), before),
                    expr,
                    SetLineInfo(_f.Nop(_f.Sequence()), after)
                );
            }
            return expr;
        }

        private QilNode CompileForEach(XslNodeEx node)
        {
            QilNode result;
            IList<XslNode> content = node.Content;

            // Push new loop frame on the stack
            LoopFocus curLoopSaved = _curLoop;
            QilIterator it = _f.For(CompileNodeSetExpression(node.Select!));
            _curLoop.SetFocus(it);

            // Compile sort keys and body
            int varScope = _varHelper.StartVariables();
            _curLoop.Sort(CompileSorts(content, ref curLoopSaved));
            result = CompileInstructions(content);
            result = WrapLoopBody(node.ElemNameLi, result, node.EndTagLi);
            result = AddCurrentPositionLast(result);
            result = _curLoop.ConstructLoop(result);
            result = _varHelper.FinishVariables(result, varScope);

            // Pop loop frame
            _curLoop = curLoopSaved;
            return result;
        }

        private QilNode CompileApplyTemplates(XslNodeEx node)
        {
            QilNode result;
            IList<XslNode> content = node.Content;

            // Calculate select expression
            int varScope = _varHelper.StartVariables();

            QilIterator select = _f.Let(CompileNodeSetExpression(node.Select!));
            _varHelper.AddVariable(select);

            // Compile with-param's, they must be calculated outside the loop and
            // if they are neither constant nor reference we need to cache them in Let's
            for (int i = 0; i < content.Count; i++)
            {
                VarPar? withParam = content[i] as VarPar;
                if (withParam != null)
                {
                    Debug.Assert(withParam.NodeType == XslNodeType.WithParam);
                    CompileWithParam(withParam);
                    QilNode? val = withParam.Value;
                    if (IsDebug || !(val is QilIterator || val is QilLiteral))
                    {
                        QilIterator let = _f.Let(val!);
                        let.DebugName = _f.QName($"with-param {withParam.Name!.QualifiedName}", XmlReservedNs.NsXslDebug).ToString();
                        _varHelper.AddVariable(let);
                        withParam.Value = let;
                    }
                }
            }

            // Push new loop frame on the stack
            LoopFocus curLoopSaved = _curLoop;
            QilIterator it = _f.For(select);
            _curLoop.SetFocus(it);

            // Compile sort keys and body
            _curLoop.Sort(CompileSorts(content, ref curLoopSaved));

            result = GenerateApply(_compiler.Root!, node);

            result = WrapLoopBody(node.ElemNameLi, result, node.EndTagLi);
            result = AddCurrentPositionLast(result);
            result = _curLoop.ConstructLoop(result);

            // Pop loop frame
            _curLoop = curLoopSaved;

            result = _varHelper.FinishVariables(result, varScope);
            return result;
        }

        private QilNode CompileApplyImports(XslNode node)
        {
            Debug.Assert(node.NodeType == XslNodeType.ApplyImports);
            Debug.Assert(!_curLoop.IsFocusSet, "xsl:apply-imports cannot be inside of xsl:for-each");

            if (IsV2 && _currentTemplateRule == null)
            {
                // Xsl3: in a named template, the caller's current template rule decides (XSLT 3.0 §6.8).
                if (!(_callerRule31 && _inNamedTemplate31))
                {
                    return X31(nameof(XsltLibrary.X31Raise), T.ItemS, _f.String("XTDE0560"), _f.String("xsl:apply-imports is used where there is no current template rule"));
                }
                int scope = _varHelper.StartVariables();
                var args = new List<XslNode>();
                foreach (VarPar withParam in node.Content)
                {
                    CompileWithParam(withParam);
                    if (withParam.Value is not (QilIterator or QilLiteral))
                    {
                        QilIterator let = _f.Let(withParam.Value!);
                        _varHelper.AddVariable(let);
                        withParam.Value = let;
                    }
                    args.Add(withParam);
                }
                return _varHelper.FinishVariables(ApplyImportsFromCaller31(args), scope);
            }

            if (IsV2 && node.Content.Count > 0)
            {
                // Xsl3: xsl:apply-imports with xsl:with-param (XSLT 2.0).
                int varScope = _varHelper.StartVariables();
                foreach (VarPar withParam in node.Content)
                {
                    CompileWithParam(withParam);
                    if (withParam.Value is not (QilIterator or QilLiteral))
                    {
                        QilIterator let = _f.Let(withParam.Value!);
                        _varHelper.AddVariable(let);
                        withParam.Value = let;
                    }
                }
                return _varHelper.FinishVariables(GenerateApply((StylesheetLevel)node.Arg!, node), varScope);
            }
            return GenerateApply((StylesheetLevel)node.Arg!, node);
        }

        private QilNode CompileCallTemplate(XslNodeEx node)
        {
            if (node != _compiler.StartApplyTemplates)
            {
                VerifyXPathQName(node.Name!); // (an initial template's name comes from the API, not a prefix)
            }
            int varScope = _varHelper.StartVariables();

            IList<XslNode> content = node.Content;
            foreach (VarPar withParam in content)
            {
                CompileWithParam(withParam);
                // In debug mode precalculate all with-param's
                if (IsDebug)
                {
                    QilNode val = withParam.Value!;
                    QilIterator let = _f.Let(val);
                    let.DebugName = _f.QName($"with-param {withParam.Name!.QualifiedName}", XmlReservedNs.NsXslDebug).ToString();
                    _varHelper.AddVariable(let);
                    withParam.Value = let;
                }
            }

            QilNode result;
            {
                Template? tmpl;
                if (IsV2 ? ResolveComponent31('T', node.Name!, -1) is QilName called && _compiler.NamedTemplates.TryGetValue(called, out tmpl)
                    : _compiler.NamedTemplates.TryGetValue(node.Name!, out tmpl))
                {
                    Debug.Assert(tmpl.Function != null, "All templates should be already compiled");
                    if (IsV2 && node != _compiler.StartApplyTemplates)
                    {
                        // (Parameters supplied to the initial template that it doesn't declare are ignored.)
                        CheckWithParams31(content, tmpl, node);
                    }
                    result = _invkGen.GenerateInvoke(tmpl.Function, AddRemoveImplicitArgs(node.Content, tmpl.Flags)!);
                }
                else if (node.SourceLine == null && IsV2)
                {
                    // Xsl3: the initial template the caller named doesn't exist (XTDE0040).
                    result = X31(nameof(XsltLibrary.X31Raise), T.ItemS, _f.String("XTDE0040"), _f.String($"There is no template named {node.Name!.QualifiedName} to start with"));
                }
                else
                {
                    if (!Compiler.IsPhantomName(node.Name!))
                    {
                        _compiler.ReportError(/*[XT0710]*/node.SourceLine!, SR.Xslt_InvalidCallTemplate, node.Name!.QualifiedName);
                    }
                    result = _f.Sequence();
                }
            }

            // Do not create an additional sequence point if there are no parameters
            if (content.Count > 0)
            {
                result = SetLineInfo(result, node.ElemNameLi);
            }
            result = _varHelper.FinishVariables(result, varScope);
            if (IsDebug)
            {
                return _f.Nop(result);
            }
            return result;
        }

        private QilNode CompileUseAttributeSet(XslNode node)
        {
            VerifyXPathQName(node.Name!);
            // REVIEW: Future optimization: invalidate only if there were AVTs
            _outputScope.InvalidateAllPrefixes();

            AttributeSet? attSet;
            if (IsV2 ? ResolveComponent31('A', node.Name!, -1) is QilName used && _compiler.AttributeSets.TryGetValue(used, out attSet)
                : _compiler.AttributeSets.TryGetValue(node.Name!, out attSet))
            {
                Debug.Assert(attSet.Function != null, "All templates should be already compiled");
                return _invkGen.GenerateInvoke(attSet.Function, AddRemoveImplicitArgs(node.Content, attSet.Flags)!);
            }
            else
            {
                if (!Compiler.IsPhantomName(node.Name!))
                {
                    _compiler.ReportError(/*[XT0710]*/node.SourceLine!, SR.Xslt_NoAttributeSet, node.Name!.QualifiedName);
                }
                return _f.Sequence();
            }
        }

        private const XmlNodeKindFlags InvalidatingNodes = (XmlNodeKindFlags.Attribute | XmlNodeKindFlags.Namespace);

        private QilNode CompileCopy(XslNode copy)
        {
            QilNode node = GetCurrentNode();
            XPathQilFactory.CheckNodeNotRtf(node);
            if ((node.XmlType!.NodeKinds & InvalidatingNodes) != XmlNodeKindFlags.None)
            {
                _outputScope.InvalidateAllPrefixes();
            }
            if (node.XmlType.NodeKinds == XmlNodeKindFlags.Element)
            {
                // Context node is always an element
                // The namespace nodes of the current node are automatically copied
                QilList content = InstructionList();
                content.Add(_f.XPathNamespace(node));
                _outputScope.PushScope();
                _outputScope.InvalidateAllPrefixes();
                QilNode result = CompileInstructions(copy.Content, content);
                _outputScope.PopScope();
                return _f.ElementCtor(_f.NameOf(node), result);
            }
            else if (node.XmlType.NodeKinds == XmlNodeKindFlags.Document)
            {
                // Context node is always a document
                // xsl:copy will not create a document node, but will just use the content template
                return CompileInstructions(copy.Content);
            }
            else if ((node.XmlType.NodeKinds & (XmlNodeKindFlags.Element | XmlNodeKindFlags.Document)) == XmlNodeKindFlags.None)
            {
                // Context node is neither an element, nor a document
                // The content of xsl:copy is not instantiated
                return node;
            }
            else
            {
                // Static classifying of the context node is not possible
                return _f.XsltCopy(node, CompileInstructions(copy.Content));
            }
        }

        private QilNode CompileCopyOf(XslNode node)
        {
            QilNode selectExpr = CompileXPathExpression(node.Select);
            if (selectExpr.XmlType!.IsNode)
            {
                if ((selectExpr.XmlType.NodeKinds & InvalidatingNodes) != XmlNodeKindFlags.None)
                {
                    _outputScope.InvalidateAllPrefixes();
                }

                if (selectExpr.XmlType.IsNotRtf && (selectExpr.XmlType.NodeKinds & XmlNodeKindFlags.Document) == XmlNodeKindFlags.None)
                {
                    // Expression returns non-document nodes only
                    return selectExpr;
                }

                // May be an Rtf or may return Document nodes, so use XsltCopyOf operator
                if (selectExpr.XmlType.IsSingleton)
                {
                    return _f.XsltCopyOf(selectExpr);
                }
                else
                {
                    QilIterator it;
                    return _f.Loop(it = _f.For(selectExpr), _f.XsltCopyOf(it));
                }
            }
            else if (selectExpr.XmlType.IsAtomicValue)
            {
                // Expression returns non-nodes only
                // When the result is neither a node-set nor a result tree fragment, the result is converted
                // to a string and then inserted into the result tree, as with xsl:value-of.
                return _f.TextCtor(_f.ConvertToString(selectExpr));
            }
            else
            {
                // Static classifying is not possible
                QilIterator it;
                _outputScope.InvalidateAllPrefixes();
                return _f.Loop(
                    it = _f.For(selectExpr),
                    _f.Conditional(_f.IsType(it, T.Node),
                        _f.XsltCopyOf(_f.TypeAssert(it, T.Node)),
                        _f.TextCtor(_f.XsltConvert(it, T.StringX))
                    )
                );
            }
        }

        private QilNode CompileValueOf(XslNode valueOf)
        {
            return _f.TextCtor(_f.ConvertToString(CompileXPathExpression(/*select:*/valueOf.Select)));
        }

        private QilNode CompileValueOfDoe(XslNode valueOf)
        {
            return _f.RawTextCtor(_f.ConvertToString(CompileXPathExpression(/*select:*/valueOf.Select)));
        }

        private QilNode CompileWhen(XslNode whenNode, QilNode otherwise)
        {
            return _f.Conditional(
                _f.ConvertToBoolean(CompileXPathExpression(/*test:*/whenNode.Select)),
                CompileInstructions(whenNode.Content),
                otherwise
            );
        }

        private QilNode CompileIf(XslNode ifNode)
        {
            return CompileWhen(ifNode, InstructionList());
        }

        private QilNode CompileChoose(XslNode node)
        {
            IList<XslNode> cases = node.Content;
            QilNode? result = null;

            // It's easier to compile xsl:choose from bottom to top
            for (int i = cases.Count - 1; 0 <= i; i--)
            {
                XslNode when = cases[i];
                Debug.Assert(when.NodeType == XslNodeType.If || when.NodeType == XslNodeType.Otherwise);
                QilList? nsList = EnterScope(when);
                if (when.NodeType == XslNodeType.Otherwise)
                {
                    Debug.Assert(result == null, "xsl:otherwise must be the last child of xsl:choose");
                    result = CompileInstructions(when.Content);
                }
                else
                {
                    result = CompileWhen(when, /*otherwise:*/result ?? InstructionList());
                }
                ExitScope();
                SetLineInfoCheck(result, when.SourceLine);
                result = SetDebugNs(result, nsList);
            }
            if (result == null)
            {
                return _f.Sequence();
            }
            return IsDebug ? _f.Sequence(result) : result;
        }

        private QilNode CompileMessage(XslNode node)
        {
            string? baseUri = _lastScope!.SourceLine!.Uri;
            QilNode content = _f.RtfCtor(CompileInstructions(node.Content), _f.String(baseUri));

            //content = f.ConvertToString(content);
            content = _f.InvokeOuterXml(content);

            // If terminate="no", then create QilNodeType.Warning
            if (!(bool)node.Arg!)
            {
                return _f.Warning(content);
            }

            // Otherwise create both QilNodeType.Warning and QilNodeType.Error
            QilIterator i;
            return _f.Loop(i = _f.Let(content), _f.Sequence(_f.Warning(i), _f.Error(i)));
        }

        private QilNode CompileVariable(XslNode node)
        {
            Debug.Assert(node.NodeType == XslNodeType.Variable);
            // Xsl3: 2.0 allows a local variable to shadow another one.
            if (!IsV2 && _scope.IsLocalVariable(node.Name!.LocalName, node.Name.NamespaceUri))
            {
                ReportError(/*[XT_030]*/SR.Xslt_DupLocalVariable, node.Name.QualifiedName);
            }
            return CompileVarParValue(node);
        }

        private QilNode CompileVarParValue(XslNode node)
        {
            Debug.Assert(node.NodeType == XslNodeType.Variable || node.NodeType == XslNodeType.Param || node.NodeType == XslNodeType.WithParam);
            VerifyXPathQName(node.Name!);
            if (IsV2)
            {
                return CompileVarParValue31(node);
            }

            string? baseUri = _lastScope!.SourceLine!.Uri;
            IList<XslNode> content = node.Content;
            string? select = node.Select;
            QilNode varValue;

            if (select != null)
            {
                // In case of incorrect stylesheet, variable or parameter may have both a 'select' attribute and non-empty content
                QilList list = InstructionList();
                list.Add(CompileXPathExpression(select));
                varValue = CompileInstructions(content, list);
            }
            else if (content.Count != 0)
            {
                _outputScope.PushScope();
                // Rtf will be instantiated in an unknown namespace context, so we should not assume anything here
                _outputScope.InvalidateAllPrefixes();
                varValue = _f.RtfCtor(CompileInstructions(content), _f.String(baseUri));
                _outputScope.PopScope();
            }
            else
            {
                varValue = _f.String(string.Empty);
            }
            if (IsDebug)
            {
                // In debug mode every variable/param must be of type 'any'
                varValue = _f.TypeAssert(varValue, T.ItemS);
            }
            Debug.Assert(varValue.SourceLine == null);
            return varValue;
        }

        private void CompileWithParam(VarPar withParam)
        {
            Debug.Assert(withParam.NodeType == XslNodeType.WithParam);
            QilList? nsList = EnterScope(withParam);
            QilNode paramValue = CompileVarParValue(withParam);
            ExitScope();
            SetLineInfo(paramValue, withParam.SourceLine);
            paramValue = SetDebugNs(paramValue, nsList);
            withParam.Value = paramValue;
        }

        // REVIEW: Can we handle both sort's and with-param's in the document order?
        // CompileSorts() creates helper variables in varHelper
        private QilList? CompileSorts(IList<XslNode> content, ref LoopFocus parentLoop)
        {
            if (IsV2)
            {
                return CompileSorts31(content, ref parentLoop);
            }
            QilList keyList = _f.BaseFactory.SortKeyList();

            int i = 0;

            while (i < content.Count)
            {
                Sort? sort = content[i] as Sort;
                if (sort != null)
                {
                    CompileSort(sort, keyList, ref parentLoop);
                    content.RemoveAt(i);
                }
                else
                {
                    i++;
                }
            }

            if (keyList.Count == 0)
                return null;

            return keyList;
        }

        private QilNode? CompileLangAttribute(string? attValue, bool fwdCompat)
        {
            QilNode? result = CompileStringAvt(attValue);

            if (result == null)
            {
                // Do nothing
            }
            else if (result.NodeType == QilNodeType.LiteralString)
            {
                string lang = (string)(QilLiteral)result;
                string cultName = XsltLibrary.LangToNameInternal(lang, fwdCompat, (IErrorHelper)this);
                if (cultName == XsltLibrary.InvariantCultureName)
                {
                    result = null;
                }
            }
            else
            {
                // NOTE: We should have the same checks for both compile time and execution time
                QilIterator i;
                result = _f.Loop(i = _f.Let(result),
                    _f.Conditional(_f.Eq(_f.InvokeLangToLcid(i, fwdCompat), _f.Int32(XsltLibrary.InvariantCultureLcid)),
                        _f.String(string.Empty),
                        i
                    )
                );
            }
            return result;
        }

        private QilNode CompileLangAttributeToLcid(string? attValue, bool fwdCompat)
        {
            return CompileLangToLcid(CompileStringAvt(attValue), fwdCompat);
        }

        private QilNode CompileLangToLcid(QilNode? lang, bool fwdCompat)
        {
            if (lang == null)
            {
                return _f.Double(XsltLibrary.InvariantCultureLcid);
            }
            else if (lang.NodeType == QilNodeType.LiteralString)
            {
                return _f.String(XsltLibrary.LangToNameInternal((string)(QilLiteral)lang, fwdCompat, (IErrorHelper)this));
            }
            else
            {
                return _f.XsltConvert(_f.InvokeLangToLcid(lang, fwdCompat), T.DoubleX);
            }
        }

        private void CompileDataTypeAttribute(string? attValue, bool fwdCompat, ref QilNode select, out QilNode? select2)
        {
            const string DtText = "text";
            const string DtNumber = "number";
            QilNode? result = CompileStringAvt(attValue);
            if (result != null)
            {
                if (result.NodeType == QilNodeType.LiteralString)
                {
                    string dataType = (string)(QilLiteral)result;
                    if (dataType == DtNumber)
                    {
                        select = _f.ConvertToNumber(select);
                        select2 = null;
                        return;
                    }
                    else if (dataType == DtText)
                    {
                        // fall through to default case
                    }
                    else
                    {
                        if (!fwdCompat)
                        {
                            // check for qname-but-not-ncname
                            if (_compiler.ParseQName(dataType, out string prefix, out _, (IErrorHelper)this))
                            {
                                ResolvePrefix(/*ignoreDefaultNs:*/true, prefix);
                            }
                            else
                            {
                                _compiler.CreatePhantomNamespace();
                            }

                            // If nsUri.Length == 0, this is a ncname; we might report SR.Xslt_InvalidAttrValue,
                            // but the following error message is more user friendly

                            ReportError(/*[XT_034]*/SR.Xslt_BistateAttribute, "data-type", DtText, DtNumber);
                        }
                        // fall through to default case
                    }
                }
                else
                {
                    // Precalculate its value outside of for-each loop
                    QilIterator dt;

                    result = _f.Loop(dt = _f.Let(result),
                        _f.Conditional(_f.Eq(dt, _f.String(DtNumber)), _f.False(),
                        _f.Conditional(_f.Eq(dt, _f.String(DtText)), _f.True(),
                        fwdCompat ? _f.True() :
                        _f.Loop(_f.Let(ResolveQNameDynamic(/*ignoreDefaultNs:*/true, dt)),
                            _f.Error(_lastScope!.SourceLine,
                                SR.Xslt_BistateAttribute, "data-type", DtText, DtNumber
                            )
                        )
                    )));

                    QilIterator text = _f.Let(result);
                    _varHelper.AddVariable(text);

                    // Make two sort keys since heterogenous sort keys are not allowed
                    select2 = select.DeepClone(_f.BaseFactory);
                    select = _f.Conditional(text, _f.ConvertToString(select), _f.String(string.Empty));
                    select2 = _f.Conditional(text, _f.Double(0), _f.ConvertToNumber(select2));
                    return;
                }
            }

            // Default case
            select = _f.ConvertToString(select);
            select2 = null;
        }

        /// <summary>
        /// Compiles AVT with two possible values
        /// </summary>
        /// <param name="attName"  >NodeCtor name (used for constructing an error message)</param>
        /// <param name="attValue" >NodeCtor value</param>
        /// <param name="value0"   >First possible value of attribute</param>
        /// <param name="value1"   >Second possible value of attribute</param>
        /// <param name="fwdCompat">If true, unrecognized value does not report an error</param>
        /// <returns>
        /// If AVT is null (i.e. the attribute is omitted), null is returned. Otherwise, QilExpression
        /// returning "1" if AVT evaluates to value1, or "0" if AVT evaluates to value0 or any other value.
        /// If AVT evaluates to neither value0 nor value1 and fwdCompat == false, an error is reported.
        /// </returns>
        [return: NotNullIfNotNull(nameof(attName))]
        private QilNode CompileOrderAttribute(string attName, string? attValue, string value0, string value1, bool fwdCompat)
        {
            QilNode? result = CompileStringAvt(attValue);
            if (result != null)
            {
                if (result.NodeType == QilNodeType.LiteralString)
                {
                    string value = (string)(QilLiteral)result;
                    if (value == value1)
                    {
                        result = _f.String("1");
                    }
                    else
                    {
                        if (value != value0 && !fwdCompat)
                        {
                            ReportError(/*[XT_034]*/SR.Xslt_BistateAttribute, attName, value0, value1);
                        }
                        result = _f.String("0");
                    }
                }
                else
                {
                    QilIterator i;
                    result = _f.Loop(i = _f.Let(result),
                        _f.Conditional(_f.Eq(i, _f.String(value1)), _f.String("1"),
                        fwdCompat ? _f.String("0") :
                        _f.Conditional(_f.Eq(i, _f.String(value0)), _f.String("0"),
                        _f.Error(_lastScope!.SourceLine,
                            SR.Xslt_BistateAttribute, attName, value0, value1
                        )
                    )));
                }
                Debug.Assert(result.XmlType == T.StringX);
            }

            return result!;
        }

        private void CompileSort(Sort sort, QilList keyList, ref LoopFocus parentLoop)
        {
            Debug.Assert(sort.NodeType == XslNodeType.Sort);
            QilNode select;
            QilNode? select2, lang, order, caseOrder;
            bool fwdCompat;

            EnterScope(sort);
            fwdCompat = sort.ForwardsCompatible;

            select = CompileXPathExpression(sort.Select);

            if (sort.Lang != null || sort.DataType != null || sort.Order != null || sort.CaseOrder != null)
            {
                // Calculate these attributes in the context of the parent loop
                LoopFocus curLoopSaved = _curLoop;
                _curLoop = parentLoop;

                lang = CompileLangAttribute(sort.Lang, fwdCompat);

                CompileDataTypeAttribute(sort.DataType, fwdCompat, ref select, out select2);

                order = CompileOrderAttribute(
                    /*attName:  */  "order",
                    /*attValue: */  sort.Order,
                    /*value0:   */  "ascending",
                    /*value1:   */  "descending",
                    /*fwdCompat:*/  fwdCompat
                );

                caseOrder = CompileOrderAttribute(
                    /*attName:  */  "case-order",
                    /*attValue: */  sort.CaseOrder,
                    /*value0:   */  "lower-first",
                    /*value1:   */  "upper-first",
                    /*fwdCompat:*/  fwdCompat
                );

                // Restore loop context
                _curLoop = curLoopSaved;
            }
            else
            {
                select = _f.ConvertToString(select);
                select2 = lang = order = caseOrder = null;
            }

            _strConcat.Reset();
            _strConcat.Append(XmlReservedNs.NsCollationBase);
            _strConcat.Append('/');
            _strConcat.Append(lang);

            char separator = '?';
            if (order != null)
            {
                _strConcat.Append(separator);
                _strConcat.Append("descendingOrder=");
                _strConcat.Append(order);
                separator = '&';
            }
            if (caseOrder != null)
            {
                _strConcat.Append(separator);
                _strConcat.Append("upperFirst=");
                _strConcat.Append(caseOrder);
            }

            QilNode collation = _strConcat.ToQil();

            QilSortKey result = _f.SortKey(select, collation);
            // Line information is not copied
            keyList.Add(result);

            if (select2 != null)
            {
                result = _f.SortKey(select2, collation.DeepClone(_f.BaseFactory));
                // Line information is not copied
                keyList.Add(result);
            }

            ExitScope();
        }

        private QilNode MatchPattern(QilNode pattern, QilIterator testNode)
        {
            QilList list;
            if (pattern.NodeType == QilNodeType.Error)
            {
                // Invalid pattern
                return pattern;
            }
            else if (pattern.NodeType == QilNodeType.Sequence)
            {
                list = (QilList)pattern;
                Debug.Assert(0 < list.Count, "Pattern should have at least one filter");
            }
            else
            {
                list = _f.BaseFactory.Sequence();
                list.Add(pattern);
            }

            QilNode result = _f.False();
            for (int i = list.Count - 1; 0 <= i; i--)
            {
                QilLoop filter = (QilLoop)list[i];
                XPathPatternBuilder.AssertFilter(filter);
                result = _f.Or(
                    _refReplacer.Replace(filter.Body, filter.Variable, testNode),
                    result
                );
            }
            return result;
        }

        private QilNode MatchCountPattern(QilNode? countPattern, QilIterator testNode)
        {
            /*
                If the 'count' attribute is not specified, then it defaults to the pattern that matches any node
                with the same node kind as the context node and, if the context node has an expanded-QName, with
                the same expanded-QName as the context node.
            */
            if (countPattern != null)
            {
                return MatchPattern(countPattern, testNode);
            }
            else
            {
                QilNode current = GetCurrentNode();
                QilNode result;
                XmlNodeKindFlags nodeKinds = current.XmlType!.NodeKinds;

                // If node kind is not known, invoke a runtime function
                if ((nodeKinds & (nodeKinds - 1)) != 0)
                {
                    return _f.InvokeIsSameNodeSort(testNode, current);
                }

                // Otherwise generate IsType check along with expanded QName check
                switch (nodeKinds)
                {
                    case XmlNodeKindFlags.Document: return _f.IsType(testNode, T.Document);
                    case XmlNodeKindFlags.Element: result = _f.IsType(testNode, T.Element); break;
                    case XmlNodeKindFlags.Attribute: result = _f.IsType(testNode, T.Attribute); break;
                    case XmlNodeKindFlags.Text: return _f.IsType(testNode, T.Text);
                    case XmlNodeKindFlags.Comment: return _f.IsType(testNode, T.Comment);
                    case XmlNodeKindFlags.PI: return _f.And(_f.IsType(testNode, T.PI), _f.Eq(_f.LocalNameOf(testNode), _f.LocalNameOf(current)));
                    case XmlNodeKindFlags.Namespace: return _f.And(_f.IsType(testNode, T.Namespace), _f.Eq(_f.LocalNameOf(testNode), _f.LocalNameOf(current)));
                    default:
                        Debug.Fail($"Unexpected NodeKind: {nodeKinds}");
                        return _f.False();
                }

                // Elements and attributes have both local name and namespace URI
                return _f.And(result, _f.And(
                    _f.Eq(_f.LocalNameOf(testNode), _f.LocalNameOf(current)),
                    _f.Eq(_f.NamespaceUriOf(testNode), _f.NamespaceUriOf(GetCurrentNode()))
                ));
            }
        }

        private QilNode PlaceMarker(QilNode? countPattern, QilNode? fromPattern, bool multiple)
        {
            /*
                Quotation from XSLT 2.0 spec:
                * Let $A be the node sequence selected by the expression
                    ancestor-or-self::node()[matches-count(.)]          (level = "multiple")
                    ancestor-or-self::node()[matches-count(.)][1]       (level = "single")
                * Let $F be the node sequence selected by the expression
                    ancestor-or-self::node()[matches-from(.)][1]
                * Let $AF be the value of
                    $A intersect ($F/descendant-or-self::node())
                * Return the result of the expression
                    for $af in $AF return 1+count($af/preceding-sibling::node()[matches-count(.)])

                NOTE: There are some distinctions between XSLT 1.0 and XSLT 2.0 specs. In our 1.0 implementation we:
                1) Assume that the 'matches-from()' function does not match root nodes by default.
                2) Instead of '$A intersect ($F/descendant-or-self::node())' (which, by the way,
                   would filter out attribute and namespace nodes from $A) we calculate
                     '$A'           if the 'from' attribute is omitted,
                     '$A[. >> $F]'  if the 'from' attribute is present.
            */

            QilNode? countPattern2;
            QilNode countMatches, fromMatches, A, F, AF;
            QilIterator i, j;

            countPattern2 = countPattern?.DeepClone(_f.BaseFactory);
            countMatches = _f.Filter(i = _f.For(_f.AncestorOrSelf(GetCurrentNode())), MatchCountPattern(countPattern, i));
            if (multiple)
            {
                A = _f.DocOrderDistinct(countMatches);
            }
            else
            {
                A = _f.Filter(i = _f.For(countMatches), _f.Eq(_f.PositionOf(i), _f.Int32(1)));
            }

            if (fromPattern == null)
            {
                AF = A;
            }
            else
            {
                fromMatches = _f.Filter(i = _f.For(_f.AncestorOrSelf(GetCurrentNode())), MatchPattern(fromPattern, i));
                F = _f.Filter(i = _f.For(fromMatches), _f.Eq(_f.PositionOf(i), _f.Int32(1)));
                AF = _f.Loop(i = _f.For(F), _f.Filter(j = _f.For(A), _f.Before(i, j)));
            }

            return _f.Loop(j = _f.For(AF),
                _f.Add(_f.Int32(1), _f.Length(_f.Filter(i = _f.For(_f.PrecedingSibling(j)), MatchCountPattern(countPattern2, i))))
            );
        }

        private QilNode PlaceMarkerAny(QilNode? countPattern, QilNode? fromPattern)
        {
            /*
                Quotation from XSLT 2.0 spec:
                * If the context node is a document node, return the empty sequence, ()
                * Let $A be the node sequence selected by the expression
                    (preceding::node()|ancestor-or-self::node())[matches-count(.)]
                * Let $F be the node sequence selected by the expression
                    (preceding::node()|ancestor::node())[matches-from(.)][last()]
                * Let $AF be the node sequence $A[. is $F or . >> $F].
                * If $AF is empty, return the empty sequence, ()
                * Otherwise return the value of the expression count($AF)

                NOTE: There are some distinctions between XSLT 1.0 and XSLT 2.0 specs. In our 1.0 implementation we:
                1) Assume that the 'matches-from()' function does not match root nodes by default.
                2) Instead of '$A[. is $F or . >> $F]' we calculate
                     '$A'           if the 'from' attribute is omitted,
                     '$A[. >> $F]'  if the 'from' attribute is present.
            */

            QilNode range, fromMatches, F, AF;
            QilIterator i, j, k;

            if (fromPattern == null)
            {
                // According to XSLT 2.0 spec, if the 'from' attribute is omitted, matches-from() returns true
                // only for the root node. It means $F is a sequence of length one containing the root node,
                // and $AF = $A. XSLT 1.0 spec rules lead to the same result $AF = $A, so two specs are compliant here.
                range = _f.NodeRange(_f.Root(GetCurrentNode()), GetCurrentNode());
                AF = _f.Filter(i = _f.For(range), MatchCountPattern(countPattern, i));
            }
            else
            {
                fromMatches = _f.Filter(i = _f.For(_f.Preceding(GetCurrentNode())), MatchPattern(fromPattern, i));
                F = _f.Filter(i = _f.For(fromMatches), _f.Eq(_f.PositionOf(i), _f.Int32(1)));
                AF = _f.Loop(i = _f.For(F),
                    _f.Filter(j = _f.For(_f.Filter(k = _f.For(_f.NodeRange(i, GetCurrentNode())), MatchCountPattern(countPattern, k))),
                        _f.Not(_f.Is(i, j))
                    )
                );
            }

            return _f.Loop(k = _f.Let(_f.Length(AF)),
                _f.Conditional(_f.Eq(k, _f.Int32(0)), _f.Sequence(),
                k
            ));
        }

        // Returns one of XsltLibrary.LetterValue enum values
        private QilNode CompileLetterValueAttribute(string? attValue, bool fwdCompat)
        {
            const string Default = "default";
            const string Alphabetic = "alphabetic";
            const string Traditional = "traditional";

            string letterValue;
            QilNode? result = CompileStringAvt(attValue);

            if (result != null)
            {
                if (result.NodeType == QilNodeType.LiteralString)
                {
                    letterValue = (string)(QilLiteral)result;
                    if (letterValue != Alphabetic && letterValue != Traditional)
                    {
                        if (!fwdCompat)
                        {
                            ReportError(/*[XT_034]*/SR.Xslt_BistateAttribute, "letter-value", Alphabetic, Traditional);
                        }
                        else
                        {
                            // Use default value
                            return _f.String(Default);
                        }
                    }
                    return result;
                }
                else
                {
                    QilIterator i = _f.Let(result);
                    return _f.Loop(i,
                        _f.Conditional(
                            _f.Or(_f.Eq(i, _f.String(Alphabetic)), _f.Eq(i, _f.String(Traditional))),
                            i,
                            fwdCompat ? _f.String(Default) :
                            _f.Error(_lastScope!.SourceLine, SR.Xslt_BistateAttribute, "letter-value", Alphabetic, Traditional)
                    ));
                }
            }
            return _f.String(Default);
        }

        private QilNode CompileGroupingSeparatorAttribute(string? attValue, bool fwdCompat)
        {
            QilNode? result = CompileStringAvt(attValue);

            if (result == null)
            {
                // NOTE: string.Empty value denotes unspecified attribute
                result = _f.String(string.Empty);
            }
            else if (result.NodeType == QilNodeType.LiteralString)
            {
                string value = (string)(QilLiteral)result;
                if (value.Length != 1)
                {
                    if (!fwdCompat)
                    {
                        ReportError(/*[XT_035]*/SR.Xslt_CharAttribute, "grouping-separator");
                    }
                    // See the comment above
                    result = _f.String(string.Empty);
                }
            }
            else
            {
                QilIterator i = _f.Let(result);
                result = _f.Loop(i,
                    _f.Conditional(_f.Eq(_f.StrLength(i), _f.Int32(1)), i,
                    fwdCompat ? _f.String(string.Empty) :
                    _f.Error(_lastScope!.SourceLine, SR.Xslt_CharAttribute, "grouping-separator")
                ));
            }
            return result;
        }

        private QilNode CompileGroupingSizeAttribute(string? attValue)
        {
            QilNode? result = CompileStringAvt(attValue);

            if (result == null)
            {
                return _f.Double(0);
            }
            else if (result.NodeType == QilNodeType.LiteralString)
            {
                string groupingSize = (string)(QilLiteral)result;

                // NOTE: It is unclear from the spec what we should do with float numbers here.
                // Let's apply XPath number and round functions as usual, suppressing any conversion errors.
                double dblGroupingSize = XsltFunctions.Round(XPathConvert.StringToDouble(groupingSize));
                if (0 <= dblGroupingSize && dblGroupingSize <= int.MaxValue)
                {
                    return _f.Double(dblGroupingSize);
                }
                // NaN goes here as well
                return _f.Double(0);
            }
            else
            {
                // NOTE: We should have the same checks for both compile time and execution time
                QilIterator i = _f.Let(_f.ConvertToNumber(result));
                return _f.Loop(i,
                    _f.Conditional(_f.And(_f.Lt(_f.Double(0), i), _f.Lt(i, _f.Double(int.MaxValue))),
                        i,
                        _f.Double(0)
                    )
                );
            }
        }

        private QilNode CompileNumber(Number num)
        {
            QilNode value;

            if (num.Value != null)
            {
                // REVIEW: We may want to report a warning if any of 'level', 'count' and 'from' attributes
                // specified together with 'value'. It is ERR XT0975 in XSLT 2.0.
                value = _f.ConvertToNumber(CompileXPathExpression(num.Value));
            }
            else
            {
                QilNode? countPattern = (num.Count != null) ? CompileNumberPattern(num.Count) : null;
                QilNode? fromPattern = (num.From != null) ? CompileNumberPattern(num.From) : null;

                if (countPattern == null && fromPattern == null && num.Level == NumberLevel.Single)
                {
                    // Xsl3 (P4): the default single-level number counts on from the sibling numbered last.
                    value = XPath31.XPath31Compiler.Invoke(_f, nameof(XsltLibrary.X31SiblingNumber), T.IntX, GetCurrentNode(), _f.Int32(_siblingNumberSites++));
                }
                else
                switch (num.Level)
                {
                    case NumberLevel.Single: value = PlaceMarker(countPattern, fromPattern, false); break;
                    case NumberLevel.Multiple: value = PlaceMarker(countPattern, fromPattern, true); break;
                    default:
                        Debug.Assert(num.Level == NumberLevel.Any);
                        value = PlaceMarkerAny(countPattern, fromPattern);
                        break;
                }
            }

            bool fwdCompat = num.ForwardsCompatible;
            return _f.TextCtor(_f.InvokeNumberFormat(
                value, CompileStringAvt(num.Format),
                CompileLangAttributeToLcid(num.Lang, fwdCompat),
                CompileLetterValueAttribute(num.LetterValue, fwdCompat),
                CompileGroupingSeparatorAttribute(num.GroupingSeparator, fwdCompat),
                CompileGroupingSizeAttribute(num.GroupingSize)
            ));
        }

        // ------------- CompileAndSortMatchPatterns() -------------

        private void CompileAndSortMatches(Stylesheet sheet)
        {
            Debug.Assert(sheet.TemplateMatches.Count == 0);

            foreach (Template template in sheet.Templates)
            {
                if (template.Match != null)
                {
                    EnterScope(template);
                    EnterComponent31(template);
                    // Xsl3: a rule of several modes is compiled for each of them.
                    IList<QilName?> modes = template.V2 is { AllModes: false, Modes: { Count: > 1 } list } ? list.Cast<QilName?>().ToArray() : new QilName?[] { null };
                    foreach (QilName? mode in modes)
                    {
                        _templatePattern31 = true; // Xsl3: only template rules match items that aren't nodes
                        // Xsl3 (P6): in a mode with typed="strict" (or "lax", if declared), an element name test is schema-element(N).
                        _strictPattern31 = IsV2 && _compiler.Settings.SchemaAware && _compiler.ModeDecls.TryGetValue(mode ?? template.Mode, out ModeDecl? patternMode) && patternMode.Typed is "strict" or "lax" ? patternMode.Typed : null;
                        QilNode result = CompileMatchPattern(template.Match);
                        _templatePattern31 = false;
                        _strictPattern31 = null;
                        if (result.NodeType == QilNodeType.Sequence)
                        {
                            QilList filters = (QilList)result;
                            // Xsl3: without a priority attribute the alternatives of a union are rules of their own (XSLT
                            // 3.0 §6.5): xsl:next-match goes on below the one that matched, the template's other
                            // alternatives included (the template learns which through a hidden argument).
                            bool alternatives = IsV2 && !IsDebug && filters.Count > 1 && double.IsNaN(template.Priority) && modes.Count == 1 &&
                                template.V2 is not { AllModes: true } && UsesNextMatch31(template.Content);
                            for (int idx = 0; idx < filters.Count; idx++)
                            {
                                TemplateMatch match = sheet.AddTemplateMatch(template, (QilLoop)filters[idx], mode);
                                if (alternatives)
                                {
                                    match.Alternative = idx;
                                    (_alternatives31 ??= new Dictionary<Template, List<TemplateMatch>>()).TryAdd(template, new List<TemplateMatch>());
                                    _alternatives31[template].Add(match);
                                }
                            }
                        }
                        else
                        {
                            sheet.AddTemplateMatch(template, (QilLoop)result, mode);
                        }
                    }
                    ExitScope();
                }
            }

            sheet.SortTemplateMatches();

            foreach (Stylesheet import in sheet.Imports!)
            {
                CompileAndSortMatches(import);
            }
        }

        // ------------- CompileKeys() -------------

        private void CompileKeys()
        {
            CheckSingletonFocus();
            for (int idx = 0; idx < _compiler.Keys.Count; idx++)
            {
                foreach (Key key in _compiler.Keys[idx])
                {
                    EnterScope(key);
                    QilParameter context = _f.Parameter(T.NodeNotRtf);
                    _singlFocus.SetFocus(context);
                    QilIterator values = _f.For(_f.OptimizeBarrier(CompileKeyMatch(key.Match)));
                    _singlFocus.SetFocus(values);
                    QilIterator keys = _f.For(CompileKeyUse(key));
                    keys = _f.For(_f.OptimizeBarrier(_f.Loop(keys, _f.ConvertToString(keys))));

                    QilParameter value = _f.Parameter(T.StringX);

                    QilFunction func = _f.Function(_f.FormalParameterList(context, value),
                        _f.Filter(values,
                            _f.Not(_f.IsEmpty(_f.Filter(keys, _f.Eq(keys, value))))
                        ),
                        _f.False()
                    );

                    func.DebugName = key.GetDebugName();
                    SetLineInfo(func, key.SourceLine);
                    key.Function = func;
                    _functions.Add(func);
                    ExitScope();
                }
            }
            _singlFocus.SetFocus(null);
        }

        // ---------------------- Global variables and parameters -----------------------

        private void CreateGlobalVarPars()
        {
            foreach (VarPar par in _compiler.ExternalPars)
            {
                CreateGlobalVarPar(par);
            }
            foreach (VarPar var in _compiler.GlobalVars)
            {
                CreateGlobalVarPar(var);
            }
        }

        private void CreateGlobalVarPar(VarPar varPar)
        {
            Debug.Assert(varPar.NodeType == XslNodeType.Variable || varPar.NodeType == XslNodeType.Param);
            XmlQueryType xt = ChooseBestType(varPar);
            QilIterator it;
            if (varPar.NodeType == XslNodeType.Variable)
            {
                it = _f.Let(_f.Unknown(xt));
            }
            else
            {
                it = _f.Parameter(null, varPar.Name, xt);
            }
            it.DebugName = varPar.Name!.ToString();
            varPar.Value = it;
            SetLineInfo(it, varPar.SourceLine);
            _scope.AddVariable(varPar.Name, it);
        }

        private void CompileGlobalVariables()
        {
            CheckSingletonFocus();
            if (IsV2)
            {
                // Xsl3: the global context item, which may be absent (XSLT 3.0 §5.3.3, §9.7).
                _singlFocus.SetGlobalFocus(_compiler.GlobalContextItem is ("absent", _));
            }
            else
            {
                _singlFocus.SetFocus(SingletonFocusType.InitialDocumentNode);
            }

            foreach (VarPar par in _compiler.ExternalPars)
            {
                _extPars.Add(CompileGlobalVarPar(par));
            }
            foreach (VarPar var in _compiler.GlobalVars)
            {
                if (IsV2)
                {
                    // Xsl3: a used package's global variables have no global context item (XSLT 3.0 §2.3.2, XPDY0002).
                    _singlFocus.SetGlobalFocus(var.PackageId > 0 || _compiler.GlobalContextItem is ("absent", _));
                }
                _gloVars.Add(CompileGlobalVarPar(var));
            }

            _singlFocus.SetFocus(null);
        }

        private QilIterator CompileGlobalVarPar(VarPar varPar)
        {
            Debug.Assert(varPar.NodeType == XslNodeType.Variable || varPar.NodeType == XslNodeType.Param);
            QilIterator it = (QilIterator)varPar.Value!;

            QilList? nsList = EnterScope(varPar);
            EnterComponent31(varPar);
            _globalBeingCompiled31 = IsV2 ? varPar.Name : null;
            QilNode content = IsV2 && varPar.NodeType == XslNodeType.Param && varPar.V2 is { Required: true, StaticValue: null }
                // Xsl3: a required stylesheet parameter that isn't supplied (XTDE0050), when its value is used.
                ? _f.TypeAssert(X31(nameof(XsltLibrary.X31Raise), T.ItemS, _f.String("XTDE0050"), _f.String($"No value was supplied for the required parameter ${varPar.Name!.QualifiedName}")), T.ItemS)
                : CompileVarParValue(varPar);
            SetLineInfo(content, it.SourceLine);
            content = AddCurrentPositionLast(content);
            content = SetDebugNs(content, nsList);
            it.SourceLine = SourceLineInfo.NoSource;
            _globalBeingCompiled31 = null;
            it.Binding = content;
            ExitScope();
            return it;
        }

        // ------------- CompileXPathExpression() / CompileMatchPattern() / CompileKeyPattern() -----------

        private void ReportErrorInXPath(XslLoadException e)
        {
            XPathCompileException? ex = e as XPathCompileException;
            string errorText = (ex != null) ? ex.FormatDetailedMessage() : e.Message;
            _compiler.ReportError(_lastScope!.SourceLine!, SR.Xml_UserException, errorText);
        }

        private QilNode PhantomXPathExpression()
        {
            return _f.TypeAssert(_f.Sequence(), T.ItemS);
        }

        private QilNode PhantomKeyMatch()
        {
            return _f.TypeAssert(_f.Sequence(), T.NodeNotRtfS);
        }

        // Calls to CompileXPathExpression() can't be nested in the XSLT. So we can reuse the same instance of xpathBuilder.
        // The only thing we need to do before its use is adjustment of IXPathEnvironment to have correct context tuple.
        private QilNode CompileXPathExpression(string? expr)
        {
            XPathScanner scanner;
            QilNode result;

            SetEnvironmentFlags(/*allowVariables:*/true, /*allowCurrent:*/true, /*allowKey:*/true);
            if (expr == null)
            {
                result = PhantomXPathExpression();
            }
            else
            {
                try
                {
                    // Note that the constructor may throw an exception, for example, in case of the expression "'"
                    scanner = new XPathScanner(expr);
                    result = _xpathParser.Parse(scanner, _xpathBuilder, LexKind.Eof);
                }
                catch (XslLoadException e)
                {
                    if (_xslVersion != XslVersion.ForwardsCompatible)
                    {
                        ReportErrorInXPath(/*[XT0300]*/e);
                    }
                    result = _f.Error(_f.String(e.Message));
                }
            }
            if (result is QilIterator)
            {
                result = _f.Nop(result);
            }
            return result;
        }

        private QilNode CompileNodeSetExpression(string expr)
        {
            QilNode? result = _f.TryEnsureNodeSet(CompileXPathExpression(expr));
            if (result == null)
            {
                // The expression is never a node-set
                XPathCompileException e = new XPathCompileException(expr, 0, expr.Length, SR.XPath_NodeSetExpected, null);
                if (_xslVersion != XslVersion.ForwardsCompatible)
                {
                    ReportErrorInXPath(/*[XTTE_101]*/e);
                }
                result = _f.Error(_f.String(e.Message));
            }
            return result;
        }

        private QilNode CompileXPathExpressionWithinAvt(string expr, ref int pos)
        {
            Debug.Assert(expr != null);
            XPathScanner scanner;
            QilNode result;

            SetEnvironmentFlags(/*allowVariables:*/true, /*allowCurrent:*/true, /*allowKey:*/true);
            try
            {
                scanner = new XPathScanner(expr, pos);
                result = _xpathParser.Parse(scanner, _xpathBuilder, LexKind.RBrace);
                pos = scanner.LexStart + 1;
            }
            catch (XslLoadException e)
            {
                if (_xslVersion != XslVersion.ForwardsCompatible)
                {
                    ReportErrorInXPath(/*[XT0350][XT0360]*/e);
                }
                result = _f.Error(_f.String(e.Message));
                pos = expr.Length;
            }
            if (result is QilIterator)
            {
                result = _f.Nop(result);
            }
            return result;
        }

        private QilNode CompileMatchPattern(string pttrn)
        {
            Debug.Assert(pttrn != null);
            if (IsV2)
            {
                return CompilePattern31(pttrn);
            }
            XPathScanner scanner;
            QilNode result;

            SetEnvironmentFlags(/*allowVariables:*/false, /*allowCurrent:*/false, /*allowKey:*/true);
            try
            {
                scanner = new XPathScanner(pttrn);
                result = _ptrnParser.Parse(scanner, _ptrnBuilder);
            }
            catch (XslLoadException e)
            {
                if (_xslVersion != XslVersion.ForwardsCompatible)
                {
                    ReportErrorInXPath(/*[XT0340]*/e);
                }
                result = _f.Loop(_f.For(_ptrnBuilder.FixupNode),
                    _f.Error(_f.String(e.Message))
                );
                XPathPatternBuilder.SetPriority(result, 0.5);
            }
            return result;
        }

        private QilNode CompileNumberPattern(string pttrn)
        {
            Debug.Assert(pttrn != null);
            if (IsV2)
            {
                return CompilePattern31(pttrn);
            }
            XPathScanner scanner;
            QilNode result;

            SetEnvironmentFlags(/*allowVariables:*/true, /*allowCurrent:*/false, /*allowKey:*/true);
            try
            {
                scanner = new XPathScanner(pttrn);
                result = _ptrnParser.Parse(scanner, _ptrnBuilder);
            }
            catch (XslLoadException e)
            {
                if (_xslVersion != XslVersion.ForwardsCompatible)
                {
                    ReportErrorInXPath(/*[XT0340]*/e);
                }
                result = _f.Error(_f.String(e.Message));
            }
            return result;
        }

        private QilNode CompileKeyMatch(string? pttrn)
        {
            XPathScanner scanner;
            QilNode result;

            _keyMatchBuilder ??= new KeyMatchBuilder((IXPathEnvironment)this);
            SetEnvironmentFlags(/*allowVariables:*/false, /*allowCurrent:*/false, /*allowKey:*/false);
            if (pttrn == null)
            {
                result = PhantomKeyMatch();
            }
            else
            {
                try
                {
                    scanner = new XPathScanner(pttrn);
                    result = _ptrnParser.Parse(scanner, _keyMatchBuilder);
                }
                catch (XslLoadException e)
                {
                    if (_xslVersion != XslVersion.ForwardsCompatible)
                    {
                        ReportErrorInXPath(/*[XT0340]*/e);
                    }
                    result = _f.Error(_f.String(e.Message));
                }
            }
            return result;
        }

        private QilNode CompileKeyUse(Key key)
        {
            string? expr = key.Use;
            XPathScanner scanner;
            QilNode result;

            SetEnvironmentFlags(/*allowVariables:*/false, /*allowCurrent:*/true, /*allowKey:*/false);
            if (expr == null)
            {
                result = _f.Error(_f.String(XslLoadException.CreateMessage(key.SourceLine, SR.Xslt_MissingAttribute, "use")));
            }
            else
            {
                try
                {
                    scanner = new XPathScanner(expr);
                    result = _xpathParser.Parse(scanner, _xpathBuilder, LexKind.Eof);
                }
                catch (XslLoadException e)
                {
                    if (_xslVersion != XslVersion.ForwardsCompatible)
                    {
                        ReportErrorInXPath(/*[XT0300]*/e);
                    }
                    result = _f.Error(_f.String(e.Message));
                }
            }
            if (result is QilIterator)
            {
                result = _f.Nop(result);
            }
            return result;
        }

        private QilNode ResolveQNameDynamic(bool ignoreDefaultNs, QilNode qilName)
        {
            XPathQilFactory.CheckString(qilName);
            QilList nsDecls = _f.BaseFactory.Sequence();
            if (ignoreDefaultNs)
            {
                nsDecls.Add(_f.NamespaceDecl(_f.String(string.Empty), _f.String(string.Empty)));
            }
            foreach (ScopeRecord rec in _scope)
            {
                string recPrefix = rec.ncName!;
                string? recNsUri = rec.nsUri;

                if (ignoreDefaultNs && recPrefix.Length == 0)
                {
                    // Do not take into account the default namespace
                }
                else
                {
                    nsDecls.Add(_f.NamespaceDecl(_f.String(recPrefix), _f.String(recNsUri!)));
                }
            }
            return _f.StrParseQName(qilName, nsDecls);
        }

        // ----------------- apply-templates, apply-imports ----------------------------- //

        private QilNode GenerateApply(StylesheetLevel sheet, XslNode node)
        {
            Debug.Assert(
                node.NodeType == XslNodeType.ApplyTemplates && sheet is RootLevel ||
                node.NodeType == XslNodeType.ApplyImports && sheet is Stylesheet
            );

            if (_compiler.Settings.CheckOnly)
            {
                return _f.Sequence();
            }
            if (ReferenceEquals(node.Name, XsltLoader.CurrentMode) && _dynamicCurrentMode31 && !(_modeRule31 is Template rule && HasStaticMode31(rule)))
            {
                return ApplyInCurrentMode31(sheet, node.Content, nextMatchBelow: null);
            }
            // Xsl3: mode="#current" is the mode of the template rule being compiled (its first mode, if it has several).
            QilName mode = ReferenceEquals(node.Name, XsltLoader.CurrentMode) ? CurrentMode31()
                : IsV2 && node.Name!.LocalName.Length != 0 ? ResolveComponent31('M', node.Name, -1) ?? node.Name : node.Name!;
            return InvokeApplyFunction(sheet, mode, node.Content);
        }

        private static void SetArg(IList<XslNode> args, int pos, QilName name, QilNode value)
        {
            VarPar varPar;
            if (args.Count <= pos || args[pos].Name != name)
            {
                varPar = AstFactory.WithParam(name);
                args.Insert(pos, varPar);
            }
            else
            {
                varPar = (VarPar)args[pos];
            }
            varPar.Value = value;
        }
        private IList<XslNode>? AddRemoveImplicitArgs(IList<XslNode>? args, XslFlags flags)
        {
            //We currently don't reuse the same argument list. So remove is not needed and will not work in this code
            if (IsDebug)
            {
                flags = XslFlags.FullFocus;
            }
            if ((flags & XslFlags.FocusFilter) != 0)
            {
                if (args == null || args.IsReadOnly)
                {
                    args = new List<XslNode>(3);
                }
                int pos = 0;
                // Xsl3: in a function body the focus is absent; a called template gets an absent focus too.
                bool absent = IsV2 && FocusAbsent;
                if ((flags & XslFlags.Current) != 0)
                {
                    SetArg(args, pos++, _nameCurrent, absent ? X31(nameof(XsltLibrary.X31AbsentFocus), T.Item)
                        : IsV2 && _initialCall31 ? X31(nameof(XsltLibrary.X31InitialFocus), T.Item, GetCurrentNode()) // Xsl3: absent without an input
                        : GetCurrentNode());
                }
                if ((flags & XslFlags.Position) != 0) { SetArg(args, pos++, _namePosition, absent ? _f.Double(0) : GetCurrentPosition()); }
                if ((flags & XslFlags.Last) != 0) { SetArg(args, pos++, _nameLast, absent ? _f.Double(0) : GetLastPosition()); }
                if (IsV2)
                {
                    // Xsl3: the tunnel argument: the current tunnel parameters plus those of this call.
                    QilNode tunnel = TunnelFrame31(args, pos);
                    SetArg(args, pos++, _nameTunnel, tunnel);
                }
            }
            return args;
        }

        // Fills invokeArgs with values from actualArgs in order given by formalArgs
        // Returns true if formalArgs maps 1:1 with actual args.
        // Formally this is n*n algorithm. We can optimize it by calculationg "signature"
        // of the function as sum of all hashes of its args names.
        private static bool FillupInvokeArgs(QilList formalArgs, IList<XslNode> actualArgs, QilList invokeArgs)
        {
            if (actualArgs.Count != formalArgs.Count)
            {
                return false;
            }
            invokeArgs.Clear();
            for (int invArg = 0; invArg < formalArgs.Count; invArg++)
            {
                QilName formalArgName = ((QilParameter)formalArgs[invArg]).Name!;
                XmlQueryType paramType = formalArgs[invArg].XmlType!;
                QilNode? arg = null;
                {
                    for (int actArg = 0; actArg < actualArgs.Count; actArg++)
                    {
                        Debug.Assert(actualArgs[actArg].NodeType == XslNodeType.WithParam, "All Sorts was removed in CompileSorts()");
                        VarPar withParam = (VarPar)actualArgs[actArg];
                        if (formalArgName.Equals(withParam.Name))
                        {
                            QilNode value = withParam.Value!;
                            XmlQueryType valueType = value.XmlType!;
                            if (valueType != paramType)
                            {
                                if (valueType.IsNode && paramType.IsNode && valueType.IsSubtypeOf(paramType))
                                {
                                    // We can pass it
                                }
                                else
                                {
                                    // Formal argument has the same name but a different type
                                    return false;
                                }
                            }
                            arg = value;
                            break;
                        }
                    }
                }
                if (arg == null)
                {
                    // Formal argument has not been found among actual arguments
                    return false;
                }
                invokeArgs.Add(arg);
            }
            // All arguments have been found
            return true;
        }

        private QilNode InvokeApplyFunction(StylesheetLevel sheet, QilName mode, IList<XslNode>? actualArgs, Template? nextMatchBelow = null)
        {
            // Here we create function that has one argument for each with-param in apply-templates
            // We have actualArgs -- list of xsl:with-param(name, value)
            // From it we create:
            // invokeArgs -- values to use with QilInvoke
            // formalArgs -- list of iterators to use with QilFunction
            // actualArgs -- modify it to hold iterators (formalArgs) instead of values to ise in invoke generator inside function budy

            XslFlags flags;
            {
                if (!sheet.ModeFlags.TryGetValue(mode, out flags))
                {
                    flags = 0;
                }
                flags |= XslFlags.Current; // Due to recursive nature of Apply(Templates/Imports) we will need current node any way
                if (IsV2)
                {
                    flags |= _focus31; // Xsl3: 2.0 templates take the full focus (unless none uses position or last)
                }
            }
            actualArgs = AddRemoveImplicitArgs(actualArgs, flags);
            if (IsV2 && actualArgs![0] is VarPar currentArg && !currentArg.Value!.XmlType!.IsSubtypeOf(T.NodeNotRtf))
            {
                // Xsl3: apply-templates to items that aren't nodes (XSLT 3.0 §6.8): the template rules with
                // predicate patterns, else the built-in rules for items.
                QilIterator item = _f.Let(currentArg.Value);
                currentArg.Value = item;
                // Both branches pass the arguments: those that aren't references are bound once.
                var lets = new List<QilIterator> { item };
                foreach (XslNode a in actualArgs)
                {
                    if (a is VarPar { Value: not (QilIterator or QilLiteral) } vp)
                    {
                        QilIterator let = _f.Let(vp.Value!);
                        lets.Add(let);
                        vp.Value = let;
                    }
                }
                QilNode[] saved = actualArgs.Select(a => ((VarPar)a).Value!).ToArray();
                currentArg.Value = _f.TypeAssert(item, T.NodeNotRtf);
                QilNode forNodes = InvokeApplyFunction(sheet, mode, actualArgs, flags, nextMatchBelow);
                for (int i = 0; i < saved.Length; i++)
                {
                    ((VarPar)actualArgs[i]).Value = saved[i];
                }
                QilNode forItems = InvokeItemApplyFunction31(sheet, mode, actualArgs, flags, nextMatchBelow);
                for (int i = 0; i < saved.Length; i++)
                {
                    ((VarPar)actualArgs[i]).Value = saved[i];
                }
                QilNode result = _f.Conditional(_f.IsType(item, T.NodeNotRtf), forNodes, forItems);
                for (int i = lets.Count - 1; i >= 0; i--)
                {
                    result = _f.Loop(lets[i], result);
                }
                return result;
            }
            return InvokeApplyFunction(sheet, mode, actualArgs, flags, nextMatchBelow);
        }

        private QilNode InvokeApplyFunction(StylesheetLevel sheet, QilName mode, IList<XslNode>? actualArgs, XslFlags flags, Template? nextMatchBelow = null)
        {

            QilList invokeArgs = _f.ActualParameterList();
            QilFunction? applyFunction = null;

            // Look at the list of all functions that have been already built.  If a suitable one is found, reuse it.
            List<QilFunction>? functionsForMode;
            if (nextMatchBelow != null)
            {
                // Xsl3: xsl:next-match functions, one family per template rule.
                if (!_nextMatchFunctions.TryGetValue(((object?)_belowMatch31 ?? nextMatchBelow, ModeKey31(mode)), out functionsForMode))
                {
                    functionsForMode = _nextMatchFunctions[((object?)_belowMatch31 ?? nextMatchBelow, ModeKey31(mode))] = new List<QilFunction>();
                }
            }
            else if (!sheet.ApplyFunctions.TryGetValue(mode, out functionsForMode))
            {
                functionsForMode = sheet.ApplyFunctions[mode] = new List<QilFunction>();
            }

            foreach (QilFunction func in functionsForMode)
            {
                if (FillupInvokeArgs(func.Arguments, actualArgs!, /*ref*/invokeArgs))
                {
                    applyFunction = func;
                    break;
                }
            }

            // If a suitable function has not been found, create it
            if (applyFunction == null)
            {
                invokeArgs.Clear();
                // We wasn't able to find suitable function. Let's build new:
                // 1. Function arguments
                QilList formalArgs = _f.FormalParameterList();
                for (int i = 0; i < actualArgs!.Count; i++)
                {
                    Debug.Assert(actualArgs[i].NodeType == XslNodeType.WithParam, "All Sorts was removed in CompileSorts()");
                    VarPar withParam = (VarPar)actualArgs[i];

                    // Add actual arg to 'invokeArgs' array. No need to clone it since it must be
                    // a literal or a reference.
                    invokeArgs.Add(withParam.Value!);

                    // Create correspondent formal arg
                    QilParameter formalArg = _f.Parameter(i == 0 ? T.NodeNotRtf : withParam.Value!.XmlType!);
                    formalArg.Name = CloneName(withParam.Name!);
                    formalArgs.Add(formalArg);

                    // Change actual arg value to formalArg for reuse in calling built-in templates rules
                    withParam.Value = formalArg;
                }

                // 2. Function header
                applyFunction = _f.Function(formalArgs,
                    _f.Boolean((flags & XslFlags.SideEffects) != 0),
                    IsV2 ? ModeType31(mode) : T.NodeNotRtfS // Xsl3: 2.0 templates may return sequences
                );
                string attMode = (mode.LocalName.Length == 0) ? string.Empty : $" mode=\"{mode.QualifiedName}\"";
                applyFunction.DebugName = $"{(sheet is RootLevel ? "<xsl:apply-templates" : "<xsl:apply-imports")}{attMode}>";
                functionsForMode.Add(applyFunction);
                _functions.Add(applyFunction);

                // 3. Function body
                Debug.Assert(actualArgs[0].Name == _nameCurrent, "Caller should always pass $current as a first argument to apply-* calls.");
                QilIterator current = (QilIterator)formalArgs[0];
                QilNode? savedTunnel = _currentTunnel;
                if (IsV2)
                {
                    foreach (QilNode formal in formalArgs)
                    {
                        if (formal is QilParameter fp && fp.Name!.Equals(_nameTunnel))
                        {
                            _currentTunnel = fp;
                        }
                    }
                }
                QilIterator? modeFrame = WithCurrentMode31(mode, actualArgs);

                // 3.1 Built-in templates:
                // 3.1.1 loop over content of current element
                QilLoop loopOnContent;
                {
                    QilIterator iChild = _f.For(_f.Content(current));
                    QilNode filter = _f.Filter(iChild, _f.IsType(iChild, T.Content));
                    filter.XmlType = T.ContentS;    // not attribute

                    LoopFocus curLoopSaved = _curLoop;
                    _curLoop.SetFocus(_f.For(filter));

                    /* Prepare actual arguments */
                    // At XSLT 1.0, if a built-in template rule is invoked with parameters, the parameters are not
                    // passed on to any templates invoked by the built-in rule. At XSLT 2.0, these parameters are
                    // passed through the built-in template rule unchanged.

                    // we can't just modify current/position/last of actualArgs in XSLT 2.0 as we tried before,
                    // becuase flags for apply-import amy now be different then flags for apply-templates, so
                    // we may need to add some space for additional position/last arguments
                    // Xsl3: in 2.0+ the parameters are passed on (XSLT 3.0 §6.7); 1.0 drops them, as stock does.
                    QilNode body = InvokeApplyFunction(_compiler.Root!, mode, /*actualArgs:*/IsV2 ? PassedParams31(actualArgs) : null);
                    if (IsDebug)
                    {
                        body = _f.Sequence(InvokeOnCurrentNodeChanged(), body);
                    }
                    loopOnContent = _curLoop.ConstructLoop(body);
                    _curLoop = curLoopSaved;
                }

                // 3.1.2 switch on type of current node
                QilNode builtinTemplates = _f.BaseFactory.Conditional(_f.IsType(current, _elementOrDocumentType),
                    loopOnContent,
                    _f.Conditional(_f.IsType(current, _textOrAttributeType),
                        _f.TextCtor(_f.XPathNodeValue(current)),
                        _f.Sequence()
                    )
                );
                if (IsV2 && _compiler.ModeDecls.TryGetValue(mode, out ModeDecl? modeDecl) && modeDecl.OnNoMatch != "text-only-copy")
                {
                    // Xsl3: the built-in template rules of xsl:mode on-no-match (XSLT 3.0 §6.7).
                    builtinTemplates = BuiltinRules31(modeDecl.OnNoMatch, current, mode, PassedParams31(actualArgs));
                }

                // 3.2 Stylesheet templates
                _matcherBuilder.CollectPatterns(sheet, mode, nextMatchBelow, _belowMatch31);
                applyFunction.Definition = _matcherBuilder.BuildMatcher(current, actualArgs, /*otherwise:*/builtinTemplates,
                    IsV2 && _compiler.ModeDecls.TryGetValue(mode, out ModeDecl? multiple) && multiple.OnMultipleMatch == "fail");
                bool schemaAware = _compiler.Settings.SchemaAware;
                if (IsV2 && schemaAware && _compiler.ModeDecls.TryGetValue(mode, out ModeDecl? strictMode) && strictMode.Typed == "strict")
                {
                    CheckStrictPatterns31(mode);
                }
                if (IsV2 && _compiler.ModeDecls.TryGetValue(mode, out ModeDecl? untypedMode) && schemaAware && untypedMode.Typed is "no" or "false" or "0")
                {
                    // Xsl3 (P6): typed="no": processing a typed element or attribute in the mode is XTTE3110.
                    applyFunction.Definition = _f.Conditional(
                        _f.And(_f.Or(_f.IsType(current, T.Element), _f.IsType(current, T.Attribute)), _f.Not(X31(nameof(XsltLibrary.X31Untyped), T.BooleanX, current))),
                        _f.Error(_f.String("XTTE3110: a mode with typed=\"" + untypedMode.Typed + "\" processes a typed element or attribute")),
                        applyFunction.Definition);
                }
                if (IsV2 && _compiler.ModeDecls.TryGetValue(mode, out ModeDecl? typedMode) && (typedMode.Typed is "yes" or "true" or "1" or "strict" || schemaAware && typedMode.Typed == "lax"))
                {
                    // Xsl3: typed="yes" or "strict" (XSLT 3.0 §3.5.4.1): processing an untyped element or attribute in
                    // the mode is XTTE3100. (Without schemas every one is untyped; with them, P6, it is asked at run time.)
                    QilNode elementOrAttribute = _f.Or(_f.IsType(current, T.Element), _f.IsType(current, T.Attribute));
                    if (_compiler.Schemas != null || _compiler.Settings.SchemaAware)
                    {
                        elementOrAttribute = _f.And(elementOrAttribute, X31(nameof(XsltLibrary.X31Untyped), T.BooleanX, current));
                    }
                    applyFunction.Definition = _f.Conditional(elementOrAttribute,
                        _f.Error(_f.String("XTTE3100: a mode with typed=\"" + typedMode.Typed + "\" processes an untyped element or attribute")),
                        applyFunction.Definition);
                }
                if (modeFrame != null)
                {
                    applyFunction.Definition = _f.Loop(modeFrame, applyFunction.Definition);
                }
                _currentTunnel = savedTunnel;
            }
            return _f.Invoke(applyFunction, invokeArgs);
        }

        // -------------------------------- IErrorHelper --------------------------------

        public void ReportError(string res, params string?[]? args)
        {
            _compiler.ReportError(_lastScope!.SourceLine!, res, args);
        }

        public void ReportWarning(string res, params string?[]? args)
        {
            _compiler.ReportWarning(_lastScope!.SourceLine!, res, args);
        }

        // ------------------------------------------------------------------------------

        [Conditional("DEBUG")]
        private void VerifyXPathQName(QilName qname)
        {
            Debug.Assert(PackageComponents.Demangle(qname.NamespaceUri).Package != 0 || // Xsl3: a used package's name
                Compiler.IsPhantomName(qname) ||
                qname.Prefix.Length == 0 && qname.NamespaceUri.Length != 0 || // Xsl3: an EQName, Q{uri}local
                qname.NamespaceUri == ResolvePrefix(/*ignoreDefaultNs:*/true, qname.Prefix),
                "QilGenerator must resolve the prefix to the same namespace as XsltLoader"
            );
        }

        private string ResolvePrefix(bool ignoreDefaultNs, string prefix)
        {
            if (ignoreDefaultNs && prefix.Length == 0)
            {
                return string.Empty;
            }
            else
            {
                string? ns = _scope.LookupNamespace(prefix);
                if (ns == null)
                {
                    if (prefix.Length == 0)
                    {
                        ns = string.Empty;
                    }
                    else
                    {
                        ReportError(/*[XT0280]*/SR.Xslt_InvalidPrefix, prefix);
                        ns = _compiler.CreatePhantomNamespace();
                    }
                }
                return ns;
            }
        }

        private void SetLineInfoCheck(QilNode n, ISourceLineInfo? lineInfo)
        {
            // Prevent xsl:choose override xsl:when, etc.
            if (n.SourceLine == null)
            {
                SetLineInfo(n, lineInfo);
            }
            else
            {
                Debug.Assert(!IsDebug, "Attempt to override SourceLineInfo in debug mode");
            }
        }

        private static QilNode SetLineInfo(QilNode n, ISourceLineInfo? lineInfo)
        {
            Debug.Assert(n.SourceLine == null);
            if (lineInfo != null)
            {
                SourceLineInfo.Validate(lineInfo);
                if (0 < lineInfo.Start.Line && lineInfo.Start.LessOrEqual(lineInfo.End))
                {
                    n.SourceLine = lineInfo;
                }
            }
            return n;
        }

        private QilNode AddDebugVariable(QilName name, QilNode value, QilNode content)
        {
            QilIterator var = _f.Let(value);
            var.DebugName = name.ToString();
            return _f.Loop(var, content);
        }

        private QilNode SetDebugNs(QilNode n, QilList? nsList)
        {
            if (n != null && nsList != null)
            {
                QilNode nsVar = GetNsVar(nsList);
                Debug.Assert(nsVar.XmlType!.IsSubtypeOf(T.NamespaceS));
                if (nsVar.XmlType.Cardinality == XmlQueryCardinality.One)
                {
                    // We want CLR type to be XmlQuerySequence instead of XPathNavigator
                    nsVar = _f.TypeAssert(nsVar, T.NamespaceS);
                }
                n = AddDebugVariable(CloneName(_nameNamespaces), nsVar, n);
            }
            return n!;
        }

        private QilNode AddCurrentPositionLast(QilNode content)
        {
            if (IsDebug)
            {
                content = AddDebugVariable(CloneName(_nameLast), GetLastPosition(), content);
                content = AddDebugVariable(CloneName(_namePosition), GetCurrentPosition(), content);
                content = AddDebugVariable(CloneName(_nameCurrent), GetCurrentNode(), content);
            }
            return content;
        }

        private QilName CloneName(QilName name)
        {
            return (QilName)name.ShallowClone(_f.BaseFactory);
        }

        // This helper internal class is used for compiling sort's and with-param's
        private sealed class VariableHelper
        {
            private readonly Stack<QilIterator> _vars = new Stack<QilIterator>();
            private readonly XPathQilFactory _f;

            public VariableHelper(XPathQilFactory f)
            {
                _f = f;
            }

            public int StartVariables()
            {
                return _vars.Count;
            }

            public void AddVariable(QilIterator let)
            {
                Debug.Assert(let.NodeType == QilNodeType.Let);
                _vars.Push(let);
            }

            public QilNode FinishVariables(QilNode node, int varScope)
            {
                Debug.Assert(0 <= varScope && varScope <= _vars.Count);
                for (int i = _vars.Count - varScope; i-- != 0;)
                {
                    node = _f.Loop(_vars.Pop(), node);
                }
                return node;
            }

            [Conditional("DEBUG")]
            public void CheckEmpty()
            {
                Debug.Assert(_vars.Count == 0, "Accumulated variables left unclaimed");
            }
        }
    }
}
