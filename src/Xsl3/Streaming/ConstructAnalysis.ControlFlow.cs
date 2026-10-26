// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.Linq;
using Xsl3.XPath31;
using Xsl3.Xdm;
using Xsl3.Xslt;
using QName = Xsl3.Xdm.QName;

namespace Xsl3.Streaming;

internal sealed partial class ConstructAnalysis
{
    private Streamability Evaluate(XslNode node, StreamabilityContext context)
    {
        EvaluateSyntax syntax = node.V2!.Evaluate!;
        var operands = Parameters(syntax.Parameters, context).ToList();
        operands.AddRange(new[] { Absorb(XPath(syntax.XPath, context)),
            new AnalysisOperand(XPath(syntax.ContextItem, context), OperandUsage.Navigation),
            new AnalysisOperand(XPath(syntax.WithParams, context), OperandUsage.Navigation),
            Absorb(Avt(syntax.BaseUri, context)), Inspect(XPath(syntax.NamespaceContext, context)), Absorb(Avt(syntax.SchemaAware, context)) });
        return General(UTypes.FromSequence(RequiredType(node.V2.As)), "19.8.4.16", operands.ToArray());
    }

    private Streamability Assert(XslNode node, StreamabilityContext context)
    {
        AssertSyntax syntax = node.V2!.Assert!;
        return General(UType.Empty, "19.8.4.6", Inspect(XPath(syntax.Test, context)), Absorb(Avt(syntax.ErrorCode, context)),
            Absorb(syntax.Select != null ? XPath(syntax.Select, context) : Sequence(syntax.Content, context)));
    }

    private Streamability Pattern(string? source, StreamabilityContext context)
    {
        if (source == null) return Empty();
        Expr pattern = ParseXPath(source);
        bool motionless = context.Posture == Posture.Grounded
            ? Expressions().Analyze(pattern, context).Sweep == Sweep.Motionless
            : Expressions(pattern: true).IsMotionlessPattern(pattern);
        return new(UType.Boolean, Posture.Grounded, motionless ? Sweep.Motionless : Sweep.FreeRanging, "19.8.10");
    }

    private Streamability FocusSequence(IList<XslNode> content, StreamabilityContext context)
    {
        Streamability? group = _currentGroup;
        bool preceding = _precedingConsuming;
        if (_currentGroup?.Posture != Posture.Grounded) _currentGroup = null;
        _precedingConsuming = false;
        _repeated++;
        try { return Sequence(content, context); }
        finally { _currentGroup = group; _precedingConsuming = preceding; _repeated--; }
    }

    private IEnumerable<AnalysisOperand> Sorts(XslNode node, Streamability selected, StreamabilityContext outer)
    {
        foreach (Sort sort in node.Content.OfType<Sort>())
        {
            XslNode? oldNode = _node;
            _node = sort;
            _scope.EnterScope(sort.Namespaces);
            try
            {
                foreach (string? avt in new[] { sort.Lang, sort.DataType, sort.Order, sort.CaseOrder, sort.V2?.Collation })
                    yield return Absorb(Avt(avt, outer));
                yield return new(SelectOrContent(sort, sort.Select ?? ".", Focus(selected)), OperandUsage.Absorption, HigherOrder: true);
            }
            finally { _scope.ExitScope(); _node = oldNode; }
        }
    }

    private Streamability ApplyTemplates(XslNode node, StreamabilityContext context)
    {
        Streamability select = XPath(node.Select ?? "child::node()", context);
        var operands = Parameters(node, context).ToList();
        operands.Insert(0, Absorb(select));
        if (select.Posture == Posture.Grounded)
        {
            operands.AddRange(Sorts(node, select, context));
            return General(UType.Any, "19.8.4.5", operands.ToArray());
        }
        Qil.QilName? actualMode = node.Name == null ? null : PackageComponents.Resolve(_compiler, _package, 'M', node.Name, -1);
        bool streamableMode = ReferenceEquals(node.Name, XsltLoader.CurrentMode)
            || actualMode != null && _compiler.ModeDecls.TryGetValue(actualMode, out ModeDecl? mode)
                && mode.Attributes.GetValueOrDefault("streamable") is "yes" or "true" or "1";
        if (!streamableMode || node.Content.Any(n => n.NodeType == XslNodeType.Sort) || select.Posture is Posture.Climbing or Posture.Crawling)
            return Free(UType.Any, "19.8.4.5");
        return General(UType.Any, "19.8.4.5", operands.ToArray());
    }

    private Streamability AnalyzeString(XslNode node, StreamabilityContext context)
    {
        var operands = new List<AnalysisOperand> { Absorb(XPath(node.Select, context)), Absorb(Avt(node.V2?.Regex, context)), Absorb(Avt(node.V2?.Flags, context)) };
        UType type = UType.Empty;
        foreach (XslNode branch in node.Content)
        {
            Streamability value = FocusSequence(new[] { branch }, new(UType.String, Posture.Grounded));
            type |= value.Type;
            operands.Add(new(value, OperandUsage.Navigation, HigherOrder: true));
        }
        return General(type, "19.8.4.3", operands.ToArray());
    }

    private Streamability PerformSort(XslNode node, StreamabilityContext context)
    {
        Streamability selected = node.Select != null ? XPath(node.Select, context) : Sequence(node.Content, context);
        return General(selected.Type, "19.8.4.33", Sorts(node, selected, context).Prepend(new AnalysisOperand(selected, OperandUsage.Navigation)).ToArray());
    }

    private Streamability ForEachGroup(XslNode node, StreamabilityContext context, bool parentFork)
    {
        Streamability select = XPath(node.Select, context);
        _groupSelections[node] = select;
        Streamability key = XPath(node.V2?.GroupBy ?? node.V2?.GroupAdjacent, Focus(select));
        Streamability start = Pattern(node.V2?.GroupStartingWith, Focus(select)), end = Pattern(node.V2?.GroupEndingWith, Focus(select));
        Streamability collation = Avt(node.V2?.Collation, context);
        Streamability? group = _currentGroup;
        _currentGroup = select.Posture == Posture.Grounded ? select with { Sweep = Sweep.Motionless } : select;
        Streamability body;
        bool preceding = _precedingConsuming;
        _precedingConsuming = false;
        _repeated++;
        try { body = Sequence(node.Content, Focus(select)); }
        finally { _currentGroup = group; _precedingConsuming = preceding; _repeated--; }
        if (select.Posture == Posture.Grounded)
            return General(body.Type, "19.8.4.19", Sorts(node, select, context).Concat(new[] {
                Inspect(select), Absorb(collation), Absorb(key), Inspect(start), Inspect(end), new AnalysisOperand(body, OperandUsage.Navigation, HigherOrder: true) }).ToArray());
        if (node.V2?.GroupBy != null && !parentFork || key.Sweep != Sweep.Motionless || collation.Sweep != Sweep.Motionless
            || start.Sweep != Sweep.Motionless || end.Sweep != Sweep.Motionless
            || node.Content.Any(n => n.NodeType == XslNodeType.Sort) && !parentFork
            || select.Posture == Posture.Crawling && body.Sweep == Sweep.Consuming) return Free(body.Type, "19.8.4.19");
        return new(body.Type, body.Posture, Wider(select.Sweep, body.Sweep), "19.8.4.19");
    }

    private Streamability Try(XslNode node, StreamabilityContext context)
    {
        Streamability body = node.Select != null ? XPath(node.Select, context)
            : Sequence(node.Content.Where(n => n.NodeType != XslNodeType.Catch).ToArray(), context);
        var operands = new List<AnalysisOperand> { Transmit(body) };
        UType type = body.Type;
        foreach (XslNode catcher in node.Content.Where(n => n.NodeType == XslNodeType.Catch))
        {
            _scope.EnterScope(catcher.Namespaces);
            try
            {
                foreach (string local in new[] { "code", "description", "value", "module", "line-number", "column-number", "additional" })
                    _scope.AddVariable(AstFactory.QName(local, XdmException.ErrorNamespace, "err"), new(SequenceTypeTest.AnySequence));
                Streamability value = SelectOrContent(catcher, catcher.Select, context);
                type |= value.Type;
                operands.Add(new(value, OperandUsage.Transmission, ChoiceGroup: 1));
                _results[catcher] = value;
            }
            finally { _scope.ExitScope(); }
        }
        return General(type, "19.8.4.39", operands.ToArray());
    }

    private Streamability Iterate(XslNode node, StreamabilityContext context)
    {
        Streamability select = XPath(node.Select, context);
        IReadOnlyList<XslNode>? oldParameters = _iterationParameters;
        _iterationParameters = node.Content.Where(n => n.NodeType == XslNodeType.Param).ToArray();
        _scope.EnterScope();
        try
        {
            var initializers = new List<Streamability>();
            foreach (XslNode parameter in node.Content.Where(n => n.NodeType == XslNodeType.Param))
            {
                _scope.EnterScope(parameter.Namespaces);
                Streamability value;
                try { value = SelectOrContent(parameter, parameter.Select, context); }
                finally { _scope.ExitScope(); }
                initializers.Add(value);
                SequenceTypeTest parameterType = RequiredTypeInScope(parameter);
                _iterationParameterTypes[parameter] = parameterType;
                _scope.AddVariable(parameter.Name!, new(parameterType));
            }
            Streamability complete = Empty();
            foreach (XslNode completion in node.Content.Where(n => n.NodeType == XslNodeType.OnCompletion))
            {
                _scope.EnterScope(completion.Namespaces);
                try { complete = SelectOrContent(completion, completion.Select, new(UType.Empty, Posture.Roaming)); }
                finally { _scope.ExitScope(); }
            }
            Streamability body = FocusSequence(node.Content.Where(n => n.NodeType != XslNodeType.OnCompletion).ToArray(), Focus(select));
            UType type = body.Type | complete.Type;
            if (select.Posture == Posture.Grounded)
                return General(type, "19.8.4.22", initializers.Select(v => new AnalysisOperand(v, OperandUsage.Navigation))
                    .Concat(new[] { Inspect(select), Transmit(body), Transmit(complete) }).ToArray());
            if (initializers.Any(v => v.Posture != Posture.Grounded || v.Sweep != Sweep.Motionless)
                || complete.Posture != Posture.Grounded || complete.Sweep != Sweep.Motionless
                || select.Posture == Posture.Crawling && body.Sweep == Sweep.Consuming) return Free(type, "19.8.4.22");
            return new(type, body.Posture, Wider(select.Sweep, body.Sweep), "19.8.4.22");
        }
        finally { _scope.ExitScope(); _iterationParameters = oldParameters; }
    }

    private Streamability Number(Number node, StreamabilityContext context)
    {
        var operands = new List<AnalysisOperand>();
        if (node.Value != null) operands.Add(Absorb(XPath(node.Value, context)));
        else operands.Add(new(XPath(node.V2?.Select ?? ".", context), OperandUsage.Navigation));
        foreach (string? avt in new[] { node.Format, node.Lang, node.LetterValue, node.GroupingSeparator, node.GroupingSize, node.V2?.Ordinal, node.V2?.StartAt })
            operands.Add(Absorb(Avt(avt, context)));
        operands.Add(new(Pattern(node.From, context), OperandUsage.Inspection, HigherOrder: true));
        operands.Add(new(Pattern(node.Count, context), OperandUsage.Inspection, HigherOrder: true));
        return General(UType.Text, "19.8.4.30", operands.ToArray());
    }

    private Streamability Merge(XslNode node, StreamabilityContext context)
    {
        foreach (XslNode source in node.Content.Where(n => n.NodeType == XslNodeType.MergeSource))
        {
            _scope.EnterScope(source.Namespaces);
            try
            {
                Streamability item = XPath(source.V2?.ForEachItem, context), document = XPath(source.V2?.SourceForEachSource, context);
                if (item.Posture != Posture.Grounded || item.Sweep != Sweep.Motionless
                    || document.Posture != Posture.Grounded || document.Sweep != Sweep.Motionless) return Free(UType.Any, "19.8.4.25");
                if (source.V2?.ForEachItem == null && source.V2?.SourceForEachSource == null)
                {
                    Streamability select = XPath(source.Select, context);
                    if (select.Posture != Posture.Grounded || select.Sweep != Sweep.Motionless) return Free(UType.Any, "19.8.4.25");
                }
            }
            finally { _scope.ExitScope(); }
        }
        return new(UType.Any, Posture.Grounded, Sweep.Motionless, "19.8.4.25");
    }
}
