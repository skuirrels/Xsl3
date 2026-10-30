// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.XPath;
using Xsl3.Runtime;

namespace Xsl3.W3C.Xslt30;

public sealed partial class Runner
{
    private static bool SerializedResult(TestCase test) =>
        test.TestElement.Element(Catalog.Ns + "output")?.Attribute("serialize")?.Value == "yes";

    // The serialized run still determines transformation/serialization errors. A tree assertion
    // replays that invocation once through the same entry point and reader-resolution path.
    // Its messages and secondary results must not replace the observations from the original run.
    internal static Func<XPathNavigator> ReplayResultTree(Action<XmlWriter> run, List<string> messages,
        Dictionary<string, StringWriter> secondary)
    {
        var result = new Lazy<XPathNavigator>(() =>
        {
            // The assertion triggers this deferred callback, but the callback runs
            // the stylesheet. Keep its commands separate from assertion evaluation.
            using var stage = Xsl3.Streaming.StreamingExecutionCapture.ObserveStage(Xsl3.Streaming.StreamingExecutionCapture.Stage.Stylesheet);
            string[] savedMessages = messages.ToArray();
            KeyValuePair<string, StringWriter>[] savedSecondary = secondary.ToArray();
            Dictionary<string, XPathNavigator>? trees = t_resultTrees;
            KeyValuePair<string, XPathNavigator>[]? savedTrees = trees?.ToArray();
            PrincipalResult? savedPrincipal = XmlQueryRuntime.Principal;
            try
            {
                var (writer, root) = Xsl3.Tree.CompactDocument.TemporaryTree(new NameTable(), "", fragment: false, noDocumentUri: true, compact: true);
                // Raw annotated writers bypass XmlILCommand's serializer wrapper. Keep explicit
                // principal writes attached to this tree, including writes nested in a secondary result.
                XmlQueryRuntime.Principal = new PrincipalResult(writer, extras: null);
                using (writer) run(writer);
                return root();
            }
            finally
            {
                XmlQueryRuntime.Principal = savedPrincipal;
                messages.Clear();
                messages.AddRange(savedMessages);
                secondary.Clear();
                foreach (var pair in savedSecondary) secondary.Add(pair.Key, pair.Value);
                t_resultTrees = trees;
                if (trees != null)
                {
                    trees.Clear();
                    foreach (var pair in savedTrees!) trees.Add(pair.Key, pair.Value);
                }
            }
        });
        return () => result.Value.Clone();
    }

    private static bool XmlEquals(XPathNavigator actual, string expected)
    {
        // XPathNavigator.OuterXml can indent xml:space='preserve' content and normalize
        // attribute newlines. Assertion comparison must not introduce either transformation.
        using var text = new StringWriter();
        using (XmlWriter writer = XmlWriter.Create(text, new XmlWriterSettings {
            ConformanceLevel = ConformanceLevel.Fragment, OmitXmlDeclaration = true, NewLineHandling = NewLineHandling.Entitize,
        })) actual.WriteSubtree(writer);
        return XmlEquals(text.ToString(), expected);
    }

}
