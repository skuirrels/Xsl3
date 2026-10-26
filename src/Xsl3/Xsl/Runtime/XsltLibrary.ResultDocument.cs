// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.Xml;
using System.Xml.XPath;
using Xsl3.Xdm;

namespace Xsl3.Runtime;

public sealed partial class XsltLibrary
{
    // The generated body pushes one normalized document to the runtime's current output.
    // Function captures retain only the normal focus/variable bindings, not a result tree.
    public IList<XPathItem> X31ResultDocumentPush(string href, XPathItem body, string baseUri, double formatId,
        string formatName, double formatsId, string parameters, double namesId, IList<XPathItem> callerFrame)
    {
        CheckResultOutputState(callerFrame);
        XmlQueryRuntime.CheckCancellation();
        if (href.Length == 0 || XmlQueryRuntime.ResultTreeHandler != null)
        {
            // Empty dynamic href retains principal ownership semantics. A consumer asking for
            // an actual result tree explicitly requires materialization and its result URI.
            var cache = new XmlCachedSequenceWriter(compact: true);
            _runtime.WithOutput(cache, () => CallFunction((FunctionItem)body));
            return X31ResultDocument(href, cache.ResultSequence, baseUri, formatId, formatName,
                formatsId, parameters, namesId, callerFrame);
        }
        var (settings, extras, text) = ResultSerialization(baseUri, formatId, formatName, formatsId, parameters, namesId);
        var (_, output) = OpenSecondaryResult(href, baseUri, settings, extras, text);
        using (output)
        {
            settings.CloseOutput = false; // This scope owns the handler's writer, including on body errors.
            using XmlWriter writer = PrincipalResult.Create(
                s => XmlWriter.Create(EmptyTagFilter.Wrap(output, s, extras), s), settings, extras);
            XmlRawWriter target = text ? new TextResultWriter(writer) : new XmlRawWriterWrapper(writer);
            _runtime.WithOutput(new XmlMergeSequenceWriter(target), () => CallFunction((FunctionItem)body));
            writer.Flush();
        }
        return Sequences.Empty;
    }
}
