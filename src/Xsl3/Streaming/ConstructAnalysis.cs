// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Linq;
using Xsl3.Qil;
using Xsl3.XPath31;
using Xsl3.Xdm;
using Xsl3.Xslt;
using QName = Xsl3.Xdm.QName;

namespace Xsl3.Streaming;

/// <summary>Streamability over the source XSL AST (§19.8.3-7), before QIL rewrites.</summary>
internal sealed partial class ConstructAnalysis
{
    private readonly Compiler _compiler;
    private readonly CompilerScopeManager<AnalysisVariable> _scope = new();
    private readonly Dictionary<XslNode, Streamability> _results = new(ReferenceEqualityComparer.Instance);
    private XslNode? _node;
    private bool _precedingConsuming;
    private int _repeated;
    private AccumulatorPhase _accumulatorPhase = AccumulatorPhase.None;
    private Streamability? _currentGroup;
    private readonly Dictionary<XslNode, Streamability> _groupSelections = new(ReferenceEqualityComparer.Instance);
    private int _package;
    private IReadOnlyList<XslNode>? _iterationParameters;
    private readonly Dictionary<XslNode, SequenceTypeTest> _iterationParameterTypes = new(ReferenceEqualityComparer.Instance);

    internal ConstructAnalysis(Compiler compiler) => _compiler = compiler;
    internal IReadOnlyDictionary<XslNode, Streamability> Results => _results;

    internal Streamability Analyze(XslNode node, StreamabilityContext context,
        IReadOnlyDictionary<QName, AnalysisVariable>? variables = null)
    {
        _results.Clear();
        _package = node.PackageId;
        _scope.EnterScope();
        try
        {
            foreach (VarPar global in _compiler.AllGlobalVarPars.Values)
                AddGlobal(global);
            if (variables != null)
                foreach (var (name, variable) in variables)
                    _scope.AddVariable(AstFactory.QName(name.LocalName, name.NamespaceUri, ""), variable);
            return Visit(node, context);
        }
        finally { _scope.ExitScope(); }
    }

    private Streamability Visit(XslNode node, StreamabilityContext context, bool forkChild = false)
    {
        XslNode? old = _node;
        _node = node;
        _scope.EnterScope(node.Namespaces);
        try
        {
            Streamability value = Instruction(node, context, forkChild);
            _results[node] = value;
            return value;
        }
        finally { _scope.ExitScope(); _node = old; }
    }

    private Streamability Sequence(IList<XslNode> content, StreamabilityContext context)
    {
        bool preceding = _precedingConsuming;
        _scope.EnterScope();
        try
        {
            var operands = new List<AnalysisOperand>();
            UType type = UType.Empty;
            foreach (XslNode child in content)
            {
                if (child.NodeType == XslNodeType.Param)
                {
                    if (_scope.LookupVariable(child.Name!.LocalName, child.Name.NamespaceUri)?.StreamingParameter == null)
                        _scope.AddVariable(child.Name, new(RequiredTypeInScope(child)));
                    continue;
                }
                if (child.NodeType is XslNodeType.ContextItem or XslNodeType.Sort) continue;
                Streamability value = Visit(child, context);
                operands.Add(new(value, OperandUsage.Transmission));
                type |= value.Type;
                // Literal attributes are not sibling instructions in the element's sequence constructor.
                if (child.NodeType != XslNodeType.LiteralAttribute) _precedingConsuming |= value.Sweep == Sweep.Consuming;
                if (child.NodeType == XslNodeType.Variable)
                {
                    // The binding belongs to the sequence's scope, not the initializer's scope.
                    _scope.AddVariable(child.Name!, new(RequiredTypeInScope(child)));
                }
            }
            return General(type, "19.8.3", operands.ToArray());
        }
        finally { _scope.ExitScope(); _precedingConsuming = preceding; }
    }

    private SequenceTypeTest RequiredTypeInScope(XslNode node)
    {
        if (_iterationParameterTypes.TryGetValue(node, out SequenceTypeTest? iterationType)) return iterationType;
        _scope.EnterScope(node.Namespaces);
        try { return RequiredType(node.V2?.As); }
        finally { _scope.ExitScope(); }
    }

    private void AddGlobal(VarPar global) => _scope.AddVariable(global.Name!, new(RequiredTypeInScope(global))
        { ComponentName = new(global.Name!.NamespaceUri, global.Name.LocalName) });

    private StaticTypeResolver Types() => new(prefix => _scope.LookupNamespace(prefix)
        ?? (prefix == "xml" ? QName.Ns.Xml : null),
        _scope.LookupNamespace(XsltInput.XPathDefaultNamespacePrefix) ?? "", _compiler.Schemas);

    private SequenceTypeTest RequiredType(string? source)
    {
        if (source == null) return SequenceTypeTest.AnySequence;
        return ExpressionAnalysis.ResolveType(Types(), Parser.ParseSequenceType(source));
    }

    private ExpressionAnalysis Expressions(bool pattern = false)
    {
        var variables = new Dictionary<QName, AnalysisVariable>();
        foreach (var record in _scope.GetActiveRecords())
        {
            if (!record.IsVariable) continue;
            QName name = new(record.nsUri!, record.ncName!);
            if (record.value.ComponentName is { } component)
            {
                var (_, ns) = PackageComponents.Demangle(component.NamespaceUri);
                QilName? actual = PackageComponents.Resolve(_compiler, _package, 'V', AstFactory.QName(component.LocalName, ns, ""), -1);
                if (actual == null || actual.NamespaceUri != component.NamespaceUri || actual.LocalName != component.LocalName) continue;
                name = new(ns, component.LocalName);
            }
            variables.TryAdd(name, record.value);
        }
        return new(new ExpressionAnalysisOptions
        {
            Types = Types(), Variables = variables, ResolveFunction = ResolveFunction, InPattern = pattern,
            InSequenceConstructor = true, PrecedingConsumingInstruction = _precedingConsuming,
            AccumulatorPhase = _accumulatorPhase, CurrentGroupSelection = _currentGroup,
            AllowStreamabilityRewrites = true,
            StreamingParameterRepeated = _repeated != 0,
        });
    }

    private AnalysisFunction? ResolveFunction(QName name, int arity)
    {
        QilName actual = AstFactory.QName(name.LocalName, name.NamespaceUri, "");
        QilName? resolved = PackageComponents.Resolve(_compiler, _package, 'F', actual, arity);
        if (resolved == null || !_compiler.Functions.TryGetValue((resolved, arity), out var definition))
        {
            // Registered delegates have the public item()* signature and no streaming contract.
            if (_compiler.Settings.EnableExtensionFunctions && _compiler.RegisteredFunctions?.ContainsKey((name.NamespaceUri, name.LocalName, arity)) == true)
                return FunctionStreamability.Describe(new(Enumerable.Repeat(SequenceTypeTest.AnySequence, arity).ToArray(), SequenceTypeTest.AnySequence), FunctionStreamabilityCategory.Unclassified);
            return null;
        }
        Template function = definition.Function;
        FunctionItemTest signature = StylesheetFunctionTypes.Resolve(function, _compiler.Schemas, ExpressionAnalysis.ResolveType);
        FunctionStreamability.TryParse(function.V2?.StreamabilityCategory ?? "unclassified", out FunctionStreamabilityCategory category);
        return FunctionStreamability.Describe(signature, category);
    }

    private Expr ParseXPath(string source)
    {
        XslNodeV2 data = _node!.V2 ??= new();
        data.ParsedExpressions ??= new(StringComparer.Ordinal);
        if (!data.ParsedExpressions.TryGetValue(source, out Expr? expression))
            data.ParsedExpressions.Add(source, expression = Parser.ParseXPath(source));
        return expression;
    }

    private Streamability XPath(string? source, StreamabilityContext context) => source == null ? Empty()
        : Expressions().Analyze(ParseXPath(source), context);

    private Streamability Avt(string? source, StreamabilityContext context)
    {
        if (source == null) return Empty();
        var operands = new List<AnalysisOperand>();
        for (int i = 0; i < source.Length; i++)
        {
            if (source[i] is not ('{' or '}')) continue;
            if (i + 1 < source.Length && source[i + 1] == source[i]) { i++; continue; }
            if (source[i] == '}') throw new XPathStaticException("XTSE0370", "Unescaped closing brace in a value template", i);
            XslNodeV2 data = _node!.V2 ??= new();
            data.ParsedValueTemplates ??= new();
            var key = (source, i + 1);
            if (!data.ParsedValueTemplates.TryGetValue(key, out var parsed))
            {
                Expr? value = Parser.ParseEnclosedExpr(source, i + 1, out int end);
                data.ParsedValueTemplates.Add(key, parsed = (value, end));
            }
            Expr? expression = parsed.Expression;
            if (expression != null) operands.Add(new(Expressions().Analyze(expression, context), OperandUsage.Absorption));
            i = parsed.End;
        }
        return General(UType.String, "19.8.7", operands.ToArray());
    }

    private Streamability SelectOrContent(XslNode node, string? select, StreamabilityContext context) =>
        select != null ? XPath(select, context) : Sequence(node.Content, context);

    private static StreamabilityContext Focus(Streamability value) => new(value.Type, value.Posture) { ItemType = value.ItemType };
    private static Streamability Empty() => new(UType.Empty, Posture.Grounded, Sweep.Motionless, "19.8.3");
    private static Streamability Free(UType type, string rule) => new(type, Posture.Roaming, Sweep.FreeRanging, rule);
    private static Streamability General(UType type, string rule, params AnalysisOperand[] operands) => ExpressionAnalysis.General_19_8_1(type, rule, operands);
    private static AnalysisOperand Absorb(Streamability value) => new(value, OperandUsage.Absorption);
    private static AnalysisOperand Inspect(Streamability value) => new(value, OperandUsage.Inspection);
    private static AnalysisOperand Transmit(Streamability value) => new(value, OperandUsage.Transmission);
    private static Sweep Wider(Sweep left, Sweep right) => left > right ? left : right;

    private Streamability Instruction(XslNode node, StreamabilityContext context, bool forkChild)
    {
        if (node.V2?.IsSourceDocument == true) return SourceDocument(node, context);
        if (node.V2?.IsFork == true) return Fork(node, context);
        if (node.V2?.Evaluate != null) return Evaluate(node, context);
        if (node.V2?.Assert != null) return Assert(node, context);
        if (node.V2?.CopySelect is string copySelect)
        {
            Streamability select = XPath(copySelect, context);
            Streamability body = FocusSequence(node.Content.Single().Content, Focus(select));
            // The loader implements copy/select as one forward loop. A striding selection and
            // a grounded body can be fused into that loop without revisiting streamed nodes.
            // Keep streaming-parameter captures under the formal higher-order rule.
            if (select.Posture == Posture.Striding && select.Sweep != Sweep.FreeRanging
                && body.Posture == Posture.Grounded && body.Sweep != Sweep.FreeRanging
                && !_scope.GetActiveRecords().Any(r => r.IsVariable && r.value.StreamingParameter != null))
                return new(select.Type, Posture.Grounded, Wider(select.Sweep, body.Sweep), "19.8.4.12");
            return General(UType.Any, "19.8.4.12", Inspect(select), new AnalysisOperand(body, OperandUsage.Absorption, HigherOrder: true));
        }
        switch (node.NodeType)
        {
            case XslNodeType.Sequence: case XslNodeType.OnEmpty: case XslNodeType.OnNonEmpty: case XslNodeType.Break:
            {
                Streamability value = SelectOrContent(node, node.Select, context);
                return General(value.Type, node.NodeType == XslNodeType.Break ? "19.8.4.8" : "19.8.4.36", Transmit(value));
            }
            case XslNodeType.List: case XslNodeType.Template: case XslNodeType.Otherwise: case XslNodeType.MatchingSubstring: case XslNodeType.NonMatchingSubstring:
                return Sequence(node.Content, context);
            case XslNodeType.Text: return new(UType.Text, Posture.Grounded, Sweep.Motionless, "19.8.4.38");
            case XslNodeType.Nop: case XslNodeType.ContextItem: return Empty();
            case XslNodeType.WherePopulated: return Sequence(node.Content, context) with { Rule = "19.8.4.42" };
            case XslNodeType.Variable: case XslNodeType.WithParam: case XslNodeType.Param:
            {
                Streamability value = SelectOrContent(node, node.Select, context);
                OperandUsage usage = node.V2?.As is string type ? ExpressionAnalysis.TypeDeterminedUsage(RequiredType(type).Item)
                    : node.Select == null ? OperandUsage.Absorption : OperandUsage.Navigation;
                return General(UType.Empty, "19.8.4.41", new AnalysisOperand(value, usage));
            }
            case XslNodeType.If:
            {
                Streamability body = Sequence(node.Content, context);
                return General(body.Type, "19.8.4.21", Inspect(XPath(node.Select, context)), Transmit(body));
            }
            case XslNodeType.Choose: return Choose(node, context);
            case XslNodeType.ValueOf: case XslNodeType.ValueOfDoe:
                return General(UType.Text, "19.8.4.40", Absorb(SelectOrContent(node, node.Select, context)), Absorb(Avt(node.V2?.Separator, context)));
            case XslNodeType.CopyOf: return General(UType.Nodes, "19.8.4.13", Absorb(XPath(node.Select, context)));
            case XslNodeType.Document: return General(UType.Document, "19.8.4.14", Absorb(Sequence(node.Content, context)));
            case XslNodeType.LiteralElement: return General(UType.Element, "19.8.4.1", Absorb(Sequence(node.Content, context)));
            case XslNodeType.LiteralAttribute: return General(UType.Attribute, "19.8.4.1", Absorb(Avt(node.Select, context)));
            case XslNodeType.Element: case XslNodeType.Attribute:
            {
                var ctor = (NodeCtor)node;
                bool attribute = node.NodeType == XslNodeType.Attribute;
                return General(attribute ? UType.Attribute : UType.Element, attribute ? "19.8.4.7" : "19.8.4.15",
                    Absorb(Avt(ctor.NameAvt, context)), Absorb(Avt(ctor.NsAvt, context)),
                    Absorb(SelectOrContent(node, node.V2?.Select, context)), Absorb(Avt(node.V2?.Separator, context)));
            }
            case XslNodeType.Comment: return General(UType.Comment, "19.8.4.11", Absorb(SelectOrContent(node, node.V2?.Select, context)));
            case XslNodeType.PI: return General(UType.ProcessingInstruction, "19.8.4.34", Absorb(Avt(node.Select, context)), Absorb(SelectOrContent(node, node.V2?.Select, context)));
            case XslNodeType.Namespace: return General(UType.Namespace, "19.8.4.27", Absorb(Avt(node.V2?.NameAvt, context)), Absorb(SelectOrContent(node, node.Select, context)));
            case XslNodeType.Copy:
            {
                Streamability selected = XPath(node.Select ?? ".", context);
                Streamability body = Sequence(node.Content, Focus(selected));
                return General(UType.Any, "19.8.4.12", Inspect(selected), new AnalysisOperand(body, OperandUsage.Absorption, node.Select != null));
            }
            case XslNodeType.ForEach: return ForEach(node, context);
            case XslNodeType.ApplyTemplates: return ApplyTemplates(node, context);
            case XslNodeType.ForEachGroup: return ForEachGroup(node, context, forkChild);
            case XslNodeType.AnalyzeString: return AnalyzeString(node, context);
            case XslNodeType.PerformSort: return PerformSort(node, context);
            case XslNodeType.Try: return Try(node, context);
            case XslNodeType.Iterate: return Iterate(node, context);
            case XslNodeType.NextIteration: return General(UType.Empty, "19.8.4.28", Parameters(node, context, _iterationParameters).ToArray());
            case XslNodeType.Number: return Number((Number)node, context);
            case XslNodeType.Merge: return Merge(node, context);
            case XslNodeType.ApplyImports: case XslNodeType.NextMatch:
                return General(UType.Any, "19.8.4.4", Parameters(node, context).Prepend(Absorb(XPath(".", context))).ToArray());
            case XslNodeType.CallTemplate: return CallTemplate(node, context);
            case XslNodeType.UseAttributeSet:
            {
                QilName? target = PackageComponents.Resolve(_compiler, _package, 'A', node.Name!, -1);
                bool declared = target != null && _compiler.AttributeSets.TryGetValue(target, out AttributeSet? set)
                    && set.Content.All(d => d.V2?.Streamable == TriState.True);
                return new(UType.Attribute, Posture.Grounded, declared ? Sweep.Motionless : Sweep.FreeRanging, "19.8.6");
            }
            case XslNodeType.MapEntry:
                return General(UType.Function, "19.8.4.24", Absorb(XPath(node.V2?.NameAvt, context)), new AnalysisOperand(SelectOrContent(node, node.Select, context), OperandUsage.Navigation));
            case XslNodeType.Map: return Map(node, context);
            case XslNodeType.ResultDocument:
            {
                var operands = new List<AnalysisOperand> { Absorb(Avt(node.V2?.Href, context)), Absorb(Sequence(node.Content, context)) };
                if (node.V2?.Serialization != null) operands.AddRange(node.V2.Serialization.Select(a => Absorb(Avt(a.Value, context))));
                return General(UType.Empty, "19.8.4.35", operands.ToArray());
            }
            case XslNodeType.Message:
                return General(UType.Empty, "19.8.4.26", Absorb(Avt(node.V2?.NameAvt, context)), Absorb(Avt(node.V2?.ErrorCodeAvt, context)), Absorb(Sequence(node.Content, context)));
            default: throw new NotSupportedException($"Construct analysis of xsl:{node.NodeType} is not implemented");
        }
    }

    private Streamability Choose(XslNode node, StreamabilityContext context)
    {
        var operands = new List<AnalysisOperand>();
        UType type = UType.Empty;
        foreach (XslNode branch in node.Content)
        {
            XslNode? oldNode = _node;
            _node = branch;
            _scope.EnterScope(branch.Namespaces);
            try
            {
                if (branch.NodeType == XslNodeType.If) operands.Add(Inspect(XPath(branch.Select, context)));
                Streamability body = Sequence(branch.Content, context);
                type |= body.Type;
                operands.Add(new(body, OperandUsage.Transmission, ChoiceGroup: 1));
                _results[branch] = body;
            }
            finally { _scope.ExitScope(); _node = oldNode; }
        }
        return General(type, "19.8.4.10", operands.ToArray());
    }

    private Streamability ForEach(XslNode node, StreamabilityContext context)
    {
        Streamability select = XPath(node.Select, context);
        Streamability body = FocusSequence(node.Content, Focus(select));
        if (select.Posture == Posture.Grounded)
            return General(body.Type, "19.8.4.18", Sorts(node, select, context).Prepend(new AnalysisOperand(body, OperandUsage.Transmission, HigherOrder: true)).Prepend(Inspect(select)).ToArray());
        if (node.Content.Any(n => n.NodeType == XslNodeType.Sort)) return Free(body.Type, "19.8.4.18");
        if (select.Posture == Posture.Crawling && body.Sweep == Sweep.Consuming) return Free(body.Type, "19.8.4.18");
        return new(body.Type, body.Posture, Wider(select.Sweep, body.Sweep), "19.8.4.18");
    }

    private IEnumerable<AnalysisOperand> Parameters(XslNode node, StreamabilityContext context, IReadOnlyList<XslNode>? target = null)
        => Parameters(node.Content, context, target);

    private IEnumerable<AnalysisOperand> Parameters(IEnumerable<XslNode> children, StreamabilityContext context, IReadOnlyList<XslNode>? target = null)
    {
        foreach (XslNode parameter in children.Where(n => n.NodeType == XslNodeType.WithParam))
        {
            XslNode? oldNode = _node;
            _node = parameter;
            _scope.EnterScope(parameter.Namespaces);
            try
            {
                OperandUsage usage = ExpressionAnalysis.TypeDeterminedUsage(RequiredType(parameter.V2?.As).Item);
                XslNode? declared = target?.FirstOrDefault(p => p.NodeType == XslNodeType.Param && p.Name!.Equals(parameter.Name));
                if (declared != null)
                {
                    OperandUsage targetUsage = ExpressionAnalysis.TypeDeterminedUsage(RequiredTypeInScope(declared).Item);
                    if (usage == OperandUsage.Navigation || targetUsage == OperandUsage.Absorption) usage = targetUsage;
                }
                yield return new(SelectOrContent(parameter, parameter.Select, context), usage);
            }
            finally { _scope.ExitScope(); _node = oldNode; }
        }
    }

    private Streamability CallTemplate(XslNode node, StreamabilityContext context)
    {
        QilName? target = PackageComponents.Resolve(_compiler, _package, 'T', node.Name!, -1);
        if (target == null || !_compiler.NamedTemplates.TryGetValue(target, out Template? template))
            throw new NotSupportedException($"The target template {node.Name} is not available for analysis");
        var operands = Parameters(node, context, template.Content.ToArray()).ToList();
        XslNode? declaration = template.Content.FirstOrDefault(n => n.NodeType == XslNodeType.ContextItem);
        if (declaration?.Select != "absent")
            operands.Add(new(XPath(".", context), ExpressionAnalysis.TypeDeterminedUsage(declaration == null ? AnyItemTest.Instance : RequiredTypeInScope(declaration).Item)));
        return General(UTypes.FromSequence(RequiredTypeInScope(template)), "19.8.4.9", operands.ToArray());
    }

    private Streamability Fork(XslNode node, StreamabilityContext context)
    {
        Streamability[] branches = node.Content.Select(n => Visit(n, context, forkChild: true)).ToArray();
        if (node.Content.Any(n => n.NodeType == XslNodeType.ForEachGroup)) return branches.Single() with { Rule = "19.8.4.20" };
        UType type = branches.Aggregate(UType.Empty, (t, v) => t | v.Type);
        // A single consuming prong, surrounded by grounded motionless prongs, is an ordinary
        // sequence constructor after eliminating the fork. Its streamed result keeps its order.
        if (branches.Count(v => v.Posture != Posture.Grounded) == 1
            && branches.Where(v => v.Posture != Posture.Grounded).Single() is { Sweep: Sweep.Consuming, Posture: Posture.Striding or Posture.Crawling } streamed
            && branches.Where(v => v.Posture == Posture.Grounded).All(v => v.Sweep == Sweep.Motionless))
            return streamed with { Type = type, Rule = "19.8.4.20" };
        return branches.Any(v => v.Posture != Posture.Grounded || v.Sweep == Sweep.FreeRanging)
            ? Free(type, "19.8.4.20") : new(type, Posture.Grounded, branches.Aggregate(Sweep.Motionless, (s, v) => Wider(s, v.Sweep)), "19.8.4.20");
    }

    private Streamability Map(XslNode node, StreamabilityContext context)
    {
        if (node.Content.Any(n => n.NodeType != XslNodeType.MapEntry)) return Sequence(node.Content, context) with { Type = UType.Function, Rule = "19.8.4.23" };
        Streamability[] entries = node.Content.Select(n => Visit(n, context)).ToArray();
        return entries.Any(v => v.Posture == Posture.Roaming || v.Sweep == Sweep.FreeRanging) ? Free(UType.Function, "19.8.4.23")
            : new(UType.Function, Posture.Grounded, entries.Aggregate(Sweep.Motionless, (s, v) => Wider(s, v.Sweep)), "19.8.4.23");
    }

    private Streamability SourceDocument(XslNode node, StreamabilityContext context)
    {
        // Its own document is assessed separately at declaration level (§18.1). From an outer
        // streamed construct only href can consume the outer stream (§19.8.4.37).
        Streamability href = Avt(node.V2!.SourceHref, context);
        Streamability body = FocusSequence(node.Content, new(UType.Document, Posture.Grounded));
        if (body.Posture != Posture.Grounded || body.Sweep == Sweep.FreeRanging) return Free(body.Type, "19.8.4.37");
        return new(body.Type, Posture.Grounded, href.Sweep, "19.8.4.37");
    }
}
