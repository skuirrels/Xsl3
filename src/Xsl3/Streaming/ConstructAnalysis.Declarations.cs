// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Linq;
using Xsl3.Qil;
using Xsl3.XPath31;
using Xsl3.Xdm;
using Xsl3.Xslt;

namespace Xsl3.Streaming;

internal sealed partial class ConstructAnalysis
{
    private readonly HashSet<Template> _reachableTemplates = new();
    /// <summary>Run before AST rewriting. Ordinary components are walked only for nested streaming declarations.</summary>
    internal static void CheckDeclarations(Compiler compiler)
    {
        var analysis = new ConstructAnalysis(compiler);
        compiler.StreamabilityResults = new(ReferenceEqualityComparer.Instance);
        analysis._scope.EnterScope();
        try
        {
            foreach (VarPar global in compiler.AllGlobalVarPars.Values)
            {
                try { analysis.AddGlobal(global); }
                catch (XPathStaticException error) { StaticError(global, error); }
            }
            if (compiler.CompilerErrorColl.Cast<CompilerError>().Any(error => !error.IsWarning)) return;
            IEnumerable<XslNode> roots = compiler.AllTemplates.Cast<XslNode>()
                .Concat(compiler.Functions.Values.Select(f => f.Function))
                .Concat(compiler.Accumulators.Values.Select(a => a.Declaration))
                .Concat(compiler.AllGlobalVarPars.Values);
            XslNode[] declarations = roots.Distinct().ToArray();
            analysis.FindReachableTemplates(declarations);
            foreach (XslNode root in declarations)
            {
                try { analysis.CheckComponent(root); }
                catch (XPathStaticException error) { StaticError(root, error); }
            }

            void StaticError(XslNode node, XPathStaticException error) => compiler.ReportError(
                node.SourceLine ?? SourceLineInfo.NoSource, SR.Xml_UserException, $"{error.Code}: {error.Message}");
        }
        finally { analysis._scope.ExitScope(); }
    }

    private void FindReachableTemplates(IEnumerable<XslNode> declarations)
    {
        // The 3.0 executable compiles a new unit for each named entry point. Unused named
        // templates can be eliminated from this unit before streamability analysis (§19.10).
        QilName entry = _compiler.InitialTemplate is { } name ? AstFactory.QName(name.Name, name.Namespace, "")
            : AstFactory.QName("initial-template", XmlReservedNs.NsXslt, "xsl");
        QilName? actualEntry = PackageComponents.Resolve(_compiler, 0, 'T', entry, -1);
        if (actualEntry != null && _compiler.NamedTemplates.TryGetValue(actualEntry, out Template? initial)) _reachableTemplates.Add(initial);
        var pending = new Queue<XslNode>(declarations.Where(n => n is not Template { Match: null, IsFunction: false }));
        foreach (Template template in _reachableTemplates) pending.Enqueue(template);
        while (pending.TryDequeue(out XslNode? node))
        {
            if (node.NodeType == XslNodeType.CallTemplate)
            {
                QilName? actual = PackageComponents.Resolve(_compiler, node.PackageId, 'T', node.Name!, -1);
                if (actual != null && _compiler.NamedTemplates.TryGetValue(actual, out Template? target) && _reachableTemplates.Add(target))
                    pending.Enqueue(target);
            }
            foreach (XslNode child in node.Content) pending.Enqueue(child);
        }
    }

    private void Within(XslNode node, Action action)
    {
        XslNode? oldNode = _node;
        _node = node;
        _scope.EnterScope(node.Namespaces);
        try { action(); }
        finally { _scope.ExitScope(); _node = oldNode; }
    }

    private void CheckComponent(XslNode node)
    {
        _package = node.PackageId;
        Within(node, () =>
        {
            if (node is Template { IsFunction: true } function)
            {
                FunctionStreamability.TryParse(function.V2?.StreamabilityCategory ?? "unclassified", out var category);
                if (category != FunctionStreamabilityCategory.Unclassified)
                {
                    // The declaration's first parameter is the only variable allowed to carry streamed nodes.
                    XslNode? first = function.Content.FirstOrDefault(n => n.NodeType == XslNodeType.Param);
                    if (first != null)
                    {
                        SequenceTypeTest type = RequiredTypeInScope(first);
                        _scope.AddVariable(first.Name!, new(type) { StreamingParameter = FunctionStreamability.StreamingParameter(type, category) });
                    }
                    Assess(function, () =>
                    {
                        AnalysisFunction signature = ResolveFunction(new(function.Name!.NamespaceUri, function.Name.LocalName),
                            function.Content.Count(n => n.NodeType == XslNodeType.Param))!;
                        Streamability body = Sequence(function.Content, new(UType.Empty, Posture.Roaming));
                        return (FunctionStreamability.AdjustType(body, signature.Signature.ReturnType!),
                            FunctionStreamability.IsGuaranteed(signature.Signature, category, body), $"function category {function.V2!.StreamabilityCategory}");
                    });
                }
            }
            else if (node is Template { Match: not null } template && IsStreamableTemplate(_compiler, template))
            {
                Assess(template, () =>
                {
                    StreamabilityContext context = new(MatchType(template.Match!), Posture.Striding);
                    bool motionlessPattern = Pattern(template.Match, context).Sweep == Sweep.Motionless;
                    bool defaultsMotionless = true;
                    foreach (XslNode parameter in template.Content.Where(n => n.NodeType == XslNodeType.Param))
                    {
                        Within(parameter, () => defaultsMotionless &= SelectOrContent(parameter, parameter.Select, context).Sweep == Sweep.Motionless);
                        _scope.AddVariable(parameter.Name!, new(RequiredTypeInScope(parameter)));
                    }
                    Streamability body = FunctionStreamability.AdjustType(Sequence(template.Content, context), RequiredType(template.V2?.As));
                    return (body, motionlessPattern && defaultsMotionless && body.Posture == Posture.Grounded && body.Sweep != Sweep.FreeRanging,
                        !motionlessPattern ? "template match pattern is not motionless" : !defaultsMotionless ? "template parameter default is not motionless" : "template body must be grounded and motionless or consuming");
                });
            }
            else if (node is AttributeSet set && set.Content.Any(d => d.V2?.Streamable == TriState.True))
            {
                Assess(set, () =>
                {
                    bool declared = set.Content.All(d => d.V2?.Streamable == TriState.True);
                    foreach (XslNode declaration in set.Content.Where(d => d.V2?.Streamable == TriState.True))
                        foreach (XslNode use in declaration.Content.Where(n => n.NodeType == XslNodeType.UseAttributeSet))
                        {
                            QilName? target = PackageComponents.Resolve(_compiler, _package, 'A', use.Name!, -1);
                            if (target != null && _compiler.AttributeSets.TryGetValue(target, out AttributeSet? referenced)
                                && referenced.Content.Any(d => d.V2?.Streamable != TriState.True))
                                _compiler.ReportError(use.SourceLine ?? set.SourceLine ?? SourceLineInfo.NoSource, SR.Xml_UserException,
                                    "XTSE0730: a streamable attribute set must reference only streamable attribute sets");
                        }
                    Streamability body = Sequence(set.Content, new(UType.Any, Posture.Striding));
                    return (body, declared && body.Posture == Posture.Grounded && body.Sweep == Sweep.Motionless,
                        !declared ? "every declaration of the attribute set must specify streamable=yes" : "attribute set must be grounded and motionless");
                });
            }
            else if (node.NodeType == XslNodeType.Accumulator && node.V2?.Streamable == TriState.True)
            {
                Assess(node, () =>
                {
                    Streamability initial = XPath(node.Select, new(UType.Any, Posture.Striding));
                    bool guaranteed = GroundedMotionless(initial);
                    SequenceTypeTest type = RequiredType(node.V2.As);
                    _scope.EnterScope();
                    try
                    {
                        _scope.AddVariable(AstFactory.QName("value", "", ""), new(type));
                        foreach (XslNode rule in node.Content)
                            Within(rule, () =>
                            {
                                _accumulatorPhase = rule.V2?.Phase == "end" ? AccumulatorPhase.End : AccumulatorPhase.Start;
                                try
                                {
                                    StreamabilityContext context = new(MatchType(rule.V2!.Match!), Posture.Striding);
                                    guaranteed &= Pattern(rule.V2.Match, context).Sweep == Sweep.Motionless;
                                    Streamability value = FunctionStreamability.AdjustType(SelectOrContent(rule, rule.Select, context), type);
                                    guaranteed &= GroundedMotionless(value);
                                    _results[rule] = value;
                                }
                                finally { _accumulatorPhase = AccumulatorPhase.None; }
                            });
                    }
                    finally { _scope.ExitScope(); }
                    return (initial, guaranteed, "accumulator initial value and rules must be grounded and motionless, with motionless match patterns");
                });
            }
            if (node is not Template { Match: null, IsFunction: false } named || _reachableTemplates.Contains(named))
                WalkChildren(node);
        });
    }

    internal static bool IsStreamableTemplate(Compiler compiler, Template template)
    {
        if (template.V2?.AllModes == true)
            return compiler.ModeDecls.Any(m => PackageComponents.Demangle(m.Key.NamespaceUri).Package == template.PackageId && Streamable(m.Value));
        return (template.V2?.Modes ?? new List<QilName> { template.Mode }).Any(name =>
        {
            QilName? actual = PackageComponents.Resolve(compiler, template.PackageId, 'M', name, -1);
            return actual != null && compiler.ModeDecls.TryGetValue(actual, out ModeDecl? mode) && Streamable(mode);
        });
    }

    private static bool Streamable(ModeDecl mode) => mode.Attributes.GetValueOrDefault("streamable") is "yes" or "true" or "1";
    private static bool GroundedMotionless(Streamability value) => value.Posture == Posture.Grounded && value.Sweep == Sweep.Motionless;

    private UType MatchType(string pattern) => Expressions(pattern: true).Analyze(ParseXPath(pattern), new(UType.Any, Posture.Grounded)).Type;

    private void WalkChildren(XslNode parent)
    {
        // Preserve lexical namespaces and typed local bindings, without assessing ordinary expressions.
        // Each source-document starts a new streamed focus; references to its containing variables still bind here.
        _scope.EnterScope();
        Streamability? oldGroup = _currentGroup;
        if (parent.NodeType == XslNodeType.ForEachGroup && _groupSelections.TryGetValue(parent, out Streamability selection))
            _currentGroup = selection;
        try
        {
            foreach (XslNode child in parent.Content)
            {
                Within(child, () =>
                {
                    if (child.V2?.Streamable == TriState.True && child.V2.IsSourceDocument)
                        Assess(child, () =>
                        {
                            Streamability body = FocusSequence(child.Content, new(UType.Document, Posture.Striding));
                            return (body, body.Posture == Posture.Grounded && body.Sweep != Sweep.FreeRanging,
                                "source-document body must be grounded and motionless or consuming");
                        });
                    else if (child.NodeType == XslNodeType.MergeSource && child.V2?.Streamable == TriState.True)
                        Assess(child, () =>
                        {
                            Streamability select = XPath(child.Select, new(UType.Document, Posture.Striding));
                            return (select, child.V2.SourceForEachSource != null && !child.V2.SortBeforeMerge
                                && select.Posture is Posture.Grounded or Posture.Striding && select.Sweep != Sweep.FreeRanging,
                                "merge source needs for-each-source, no sorting, and a grounded or striding select expression");
                        });
                    WalkChildren(child);
                });
                if (child.NodeType is XslNodeType.Variable or XslNodeType.Param)
                {
                    // Keep a function's pre-bound streaming parameter; all other bindings are grounded (§19.8.8.12).
                    if (child.NodeType != XslNodeType.Param || _scope.LookupVariable(child.Name!.LocalName, child.Name.NamespaceUri)?.StreamingParameter == null)
                        _scope.AddVariable(child.Name!, new(RequiredTypeInScope(child)));
                }
            }
        }
        finally { _scope.ExitScope(); _currentGroup = oldGroup; }
    }

    private void Assess(XslNode node, Func<(Streamability Value, bool Guaranteed, string Requirement)> assess)
    {
        // A merged attribute set intentionally has no code-generation location; its constituent
        // declarations retain their source spans. Use one of those for a declaration diagnostic.
        ISourceLineInfo line = node.SourceLine
            ?? (node is AttributeSet ? node.Content.FirstOrDefault()?.SourceLine : null) ?? SourceLineInfo.NoSource;
        _results.Clear();
        int diagnosticsBefore = _compiler.CompilerErrorColl.Count;
        StreamabilityVerdict verdict;
        try
        {
            var (value, guaranteed, requirement) = assess();
            Streamability failure = _results.Values.FirstOrDefault(v => v.Sweep == Sweep.FreeRanging, value);
            string reason = guaranteed ? "guaranteed-streamable" : $"{requirement}; {failure.Posture}, {failure.Sweep} (rule {failure.Rule})";
            verdict = new(value, guaranteed, reason);
        }
        catch (NotSupportedException error)
        {
            verdict = new(Free(UType.Any, "19"), false, error.Message);
        }
        catch (XPathStaticException error)
        {
            _compiler.ReportError(line, SR.Xml_UserException, $"{error.Code}: {error.Message}");
            return;
        }
        string kind = node.V2?.IsSourceDocument == true ? "source-document" : node is Template { IsFunction: true } ? "function" : node.NodeType.ToString();
        verdict = verdict with { Declaration = $"{kind} {node.Name?.QualifiedName} at {line.Uri}:{line.Start.Line}" };
        _compiler.StreamabilityResults![node] = verdict;
        if (!verdict.Guaranteed && _compiler.Settings.Streamability == XsltStreamabilityMode.Strict)
        {
            // Keep a specific declaration error and its source span instead of adding a redundant
            // generic error at the enclosing declaration, which sorts before the actual violation.
            // Other components still receive their own streamability diagnostics.
            if (_compiler.CompilerErrorColl.Cast<CompilerError>().Skip(diagnosticsBefore).Any(error => !error.IsWarning)) return;
            _compiler.ReportError(line, SR.Xml_UserException, $"XTSE3430: {verdict.Reason}");
        }
    }
}

/// <summary>Kept separate from QIL iterator annotations, which the compiler uses for parameter bindings.</summary>
internal sealed record StreamabilityVerdict(Streamability Value, bool Guaranteed, string Reason) : IQilAnnotation
{
    internal string Declaration { get; init; } = "";
    public string Name => "streamability";
    public override string ToString() => $"{(Guaranteed ? "guaranteed" : "fallback")}; {Value.Posture}; {Value.Sweep}; rule {Value.Rule}; {Declaration}; {Reason}"
        .Replace("--", "- -").Replace("--", "- -"); // Also split boundaries introduced between adjacent replacements.

    internal static void Attach(Compiler compiler, XslNode source, QilNode target)
    {
        if (compiler.StreamabilityResults?.TryGetValue(source, out StreamabilityVerdict? verdict) == true)
            target.Annotation = target.Annotation == null ? verdict : new List<object> { target.Annotation, verdict };
    }
}
