// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using Xsl3.Qil;
using Xsl3.Runtime;
using T = Xsl3.XmlQueryTypeFactory;

namespace Xsl3.Xslt;

internal sealed partial class QilGenerator
{
    private QilNode OpenStreamedSource31(QilNode href, XslNodeV2 data)
    {
        _streamedInputs31 = true;
        if (data.SourceAccumulators is string required && required != "#all" &&
            StreamedAccumulatorNamesError31(required.Split(' ', System.StringSplitOptions.RemoveEmptyEntries), _package31) is { } error) return error;
        bool untyped = data.SourceType is { Name: "untyped", Namespace: XmlReservedNs.NsXs };
        string validation = untyped ? "strip" : data.SourceValidation ?? "";
        string type = data.SourceType == null || untyped ? "" : $"Q{{{data.SourceType.Namespace}}}{data.SourceType.Name}";
        var args = new List<QilNode> {
            href, _f.String(StaticBaseUri31() ?? ""), _f.String(data.SourceAccumulators ?? ""), _f.String(validation), _f.String(type) };
        bool validating = validation is "strict" or "lax" || type.Length != 0;
        if (validating) args.Add(_f.Double(SchemasId31()));
        bool observing = !string.IsNullOrEmpty(data.SourceAccumulators);
        if (observing) { args.Add(AccumulatorItems31()); args.Add(_f.Double(_accumulatorTable31)); args.Add(_f.Double(_package31)); }
        string open = observing
            ? validating ? nameof(XsltLibrary.X31OpenObservedValidatedStreamedSource) : nameof(XsltLibrary.X31OpenObservedStreamedSource)
            : validating ? nameof(XsltLibrary.X31OpenValidatedStreamedSource) : nameof(XsltLibrary.X31OpenStreamedSource);
        return X31(open, T.NodeNotRtf, args.ToArray());
    }
}
