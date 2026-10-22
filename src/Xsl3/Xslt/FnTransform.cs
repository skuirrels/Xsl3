// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Xml;
using System.Xml.XPath;
using Xsl3.Runtime;
using Xsl3.XPath31.Functions;
using Xsl3.Xdm;

namespace Xsl3
{
    /// <summary>
    /// fn:transform (F&amp;O 3.1 §16.4.1): runs another XSLT transformation and returns its results as a map, the
    /// principal result under "output" and each secondary result under its absolute URI.
    /// </summary>
    /// <remarks>
    /// Like xsl:evaluate it compiles at run time, so it is allowed only where <see cref="XsltSettings.EnableEvaluate"/>
    /// is on (FOXT0004 otherwise). Supported options: stylesheet-location, stylesheet-node, stylesheet-text,
    /// source-node, initial-template, initial-mode, initial-function with function-params, stylesheet-params,
    /// static-params, initial-match-selection, delivery-format (document, raw, serialized) and base-output-uri.
    /// The package options raise FOXT0001.
    /// </remarks>
    internal static class FnTransform
    {
        [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Allowed only with XsltSettings.EnableEvaluate; refused without dynamic code.")]
        public static IList<XPathItem> Invoke(MapItem options, FunctionContext c)
        {
            if (XmlQueryRuntime.DynamicTransforms != true)
            {
                throw new XdmException("FOXT0004", "fn:transform is disabled: it needs XsltSettings.EnableEvaluate");
            }
            if (!System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported)
            {
                throw new XdmException("FOXT0004", "fn:transform compiles a stylesheet at run time, which needs dynamic code");
            }
            IList<XPathItem>? Option(string name) => options.Get(AtomicValue.OfString(name));
            string? OptionString(string name) => Option(name) is { Count: 1 } v ? v[0].Value : null;
            QName? OptionQName(string name) => Option(name) is { Count: 1 } v && v[0] is QNameValue q ? q.QName : null;

            foreach (string unsupported in new[] { "package-location", "package-node", "package-text", "post-process" })
            {
                if (Option(unsupported) is { Count: > 0 })
                {
                    throw new XdmException("FOXT0001", $"fn:transform: the option '{unsupported}' isn't supported");
                }
            }
            // package-name: a package of the calling transformation's library (XslCompiledTransform.Packages).
            Uri? packageLocation = null;
            if (OptionString("package-name") is string packageName)
            {
                string? wanted = OptionString("package-version");
                packageLocation = BestPackage(packageName, wanted)
                    ?? throw new XdmException("FOXT0002", $"fn:transform: no package {packageName} {wanted} in the package library");
            }

            string format = OptionString("delivery-format") ?? "document";
            if (format is not ("document" or "raw" or "serialized"))
            {
                throw new XdmException("FOXT0002", $"fn:transform: delivery-format '{format}' isn't document, raw or serialized");
            }
            XmlResolver resolver = c.Runtime?.ExternalContext.DataSources is XmlResolver r && !ReferenceEquals(r, XmlResolver.ThrowingResolver) ? r : new XmlUrlResolver();
            string? baseOutput = OptionString("base-output-uri") ?? c.StaticBaseUri;

            var transform = new XslCompiledTransform { RawResult = format != "serialized", Packages = XmlQueryRuntime.Packages };
            if (OptionQName("initial-template") is QName template)
            {
                transform.InitialTemplate = new XmlQualifiedName(template.LocalName, template.NamespaceUri);
            }
            if (OptionQName("initial-mode") is QName mode)
            {
                transform.InitialMode = new XmlQualifiedName(mode.LocalName, mode.NamespaceUri);
            }
            var arguments = new XsltArgumentList();
            if (OptionQName("initial-function") is QName function)
            {
                IList<XPathItem> parameters = Option("function-params") ?? Array.Empty<XPathItem>();
                ArrayItem args = parameters is { Count: 1 } && parameters[0] is ArrayItem a ? a : ArrayItem.Empty;
                transform.InitialFunction = new XmlQualifiedName(function.LocalName, function.NamespaceUri);
                transform.InitialFunctionArity = args.Count;
                for (int i = 0; i < args.Count; i++)
                {
                    XmlQualifiedName argument = XslCompiledTransform.InitialFunctionArgument(i + 1);
                    arguments.AddParam(argument.Name, argument.Namespace, args.Get(i + 1));
                }
            }
            if (Option("static-params") is { Count: 1 } staticParams && staticParams[0] is MapItem statics)
            {
                transform.StaticParameters = new Dictionary<XmlQualifiedName, IList<XPathItem>>();
                foreach (var (key, value) in statics.Entries)
                {
                    QName name = ((QNameValue)key).QName;
                    transform.StaticParameters[new XmlQualifiedName(name.LocalName, name.NamespaceUri)] = value;
                }
            }
            if (Option("stylesheet-params") is { Count: 1 } stylesheetParams && stylesheetParams[0] is MapItem parametersMap)
            {
                foreach (var (key, value) in parametersMap.Entries)
                {
                    QName name = ((QNameValue)key).QName;
                    arguments.AddParam(name.LocalName, name.NamespaceUri, value);
                }
            }

            // An initial match selection other than one node is supplied as a parameter (compiled in at Load).
            IList<XPathItem>? matchSelection = Option("source-node") is not { Count: 1 } && Option("initial-match-selection") is { Count: > 0 } ms
                && !(ms is { Count: 1 } && ms[0] is XPathNavigator) ? ms : null;
            transform.InitialMatchSelection = matchSelection != null;

            // The stylesheet.
            var settings = new XsltSettings(enableDocumentFunction: true, enableScript: false) { EnableEvaluate = true };
            try
            {
                if (packageLocation != null)
                {
                    using XmlReader reader = XmlReader.Create(packageLocation.AbsoluteUri, new XmlReaderSettings { DtdProcessing = DtdProcessing.Parse, XmlResolver = resolver });
                    transform.Load(reader, settings, resolver);
                }
                else if (OptionString("stylesheet-location") is string location)
                {
                    Uri uri = Uri.TryCreate(c.StaticBaseUri, UriKind.Absolute, out Uri? b) ? new Uri(b, location) : new Uri(location, UriKind.RelativeOrAbsolute);
                    using XmlReader reader = XmlReader.Create(uri.AbsoluteUri, new XmlReaderSettings { DtdProcessing = DtdProcessing.Parse, XmlResolver = resolver });
                    transform.Load(reader, settings, resolver);
                }
                else if (Option("stylesheet-node") is { Count: 1 } node && node[0] is XPathNavigator stylesheetNode)
                {
                    transform.Load(stylesheetNode, settings, resolver);
                }
                else if (OptionString("stylesheet-text") is string text)
                {
                    using XmlReader reader = XmlReader.Create(new StringReader(text), new XmlReaderSettings { DtdProcessing = DtdProcessing.Parse, XmlResolver = resolver }, c.StaticBaseUri);
                    transform.Load(reader, settings, resolver);
                }
                else
                {
                    throw new XdmException("FOXT0002", "fn:transform: no stylesheet (stylesheet-location, stylesheet-node or stylesheet-text)");
                }
            }
            catch (XsltException e)
            {
                throw new XdmException("FOXT0002", "fn:transform: the stylesheet has a static error: " + e.Message);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or XmlException or NotSupportedException or System.Net.WebException)
            {
                // A stylesheet that can't be read or parsed (transform-001).
                throw new XdmException("FOXT0002", "fn:transform: the stylesheet can't be loaded: " + e.Message);
            }
            if (Uri.TryCreate(baseOutput, UriKind.Absolute, out Uri? baseOutputUri))
            {
                transform.BaseOutputUri = baseOutputUri;
            }

            // The input: the source node, or a single-node initial match selection.
            XPathNavigator input;
            if (Option("source-node") is { Count: 1 } source && source[0] is XPathNavigator sourceNode)
            {
                input = sourceNode;
            }
            else if (Option("initial-match-selection") is { Count: 1 } single && single[0] is XPathNavigator selected)
            {
                input = selected;
            }
            else if (matchSelection != null)
            {
                // Any other sequence: applied to as the stylesheet's initial match selection, with no input document.
                arguments.AddParam(InitialFunctionArguments.Selection.Name, InitialFunctionArguments.Selection.Namespace, matchSelection);
                input = Parse("", null);
            }
            else
            {
                input = Parse("", null); // an empty document stands in for an absent one
            }

            // Secondary results are collected as text, then delivered in the requested format.
            var secondary = new Dictionary<string, (StringWriter Text, string Method)>();
            Func<string, string, TextWriter>? savedHandler = XmlQueryRuntime.ResultDocumentHandler;
            XmlQueryRuntime.ResultDocumentHandler = (uri, method) =>
            {
                var writer = new StringWriter();
                secondary[uri] = (writer, method);
                return writer;
            };
            IList<XPathItem> output;
            try
            {
                if (format == "serialized")
                {
                    // Through bytes, so the XML declaration names the stylesheet's encoding (not a StringWriter's UTF-16).
                    var bytes = new MemoryStream();
                    transform.Transform(input, arguments, bytes);
                    System.Text.Encoding encoding = transform.OutputSettings?.Encoding ?? System.Text.Encoding.UTF8;
                    string text = encoding.GetString(bytes.GetBuffer(), 0, (int)bytes.Length);
                    output = new XPathItem[] { AtomicValue.OfString(text.Length > 0 && text[0] == '\uFEFF' ? text[1..] : text) };
                }
                else
                {
                    IList<XPathItem> items = transform.TransformToItems(input, arguments, resolver);
                    output = format == "raw" ? items : new XPathItem[] { AsDocument(items) };
                }
            }
            finally
            {
                XmlQueryRuntime.ResultDocumentHandler = savedHandler;
            }

            var entries = new List<KeyValuePair<AtomicValue, IList<XPathItem>>> { new(AtomicValue.OfString("output"), output) };
            foreach (var (uri, (text, method)) in secondary)
            {
                IList<XPathItem> value = format == "serialized" || method is "text" or "json" or "adaptive"
                    ? new XPathItem[] { AtomicValue.OfString(text.ToString()) }
                    : new XPathItem[] { Parse(text.ToString(), uri) };
                entries.Add(new(new StringValue(AtomicTypes.AnyUri, uri), value));
            }
            return new XPathItem[] { MapItem.Of(entries) };
        }

        // The highest version of the named package that matches the requested version: exact, a "1.*" prefix, or any.
        private static Uri? BestPackage(string name, string? wanted)
        {
            Uri? best = null;
            Version? bestVersion = null;
            foreach (var (packageName, version, location) in XmlQueryRuntime.Packages ?? Array.Empty<(string, string, Uri)>())
            {
                bool matches = packageName == name && (wanted is null or "*" || version == wanted
                    || wanted.EndsWith(".*", StringComparison.Ordinal) && version.StartsWith(wanted[..^1], StringComparison.Ordinal));
                if (matches && Version.TryParse(version.Split('-')[0], out Version? v) && (bestVersion == null || v > bestVersion))
                {
                    (best, bestVersion) = (location, v);
                }
                else if (matches && best == null)
                {
                    best = location;
                }
            }
            return best;
        }

        // A document node whose children are the items (document delivery): nodes copied, atomic values as text.
        private static XPathNavigator AsDocument(IList<XPathItem> items)
        {
            var sb = new System.Text.StringBuilder();
            using (XmlWriter w = XmlWriter.Create(sb, new XmlWriterSettings { ConformanceLevel = ConformanceLevel.Fragment, OmitXmlDeclaration = true }))
            {
                bool atomic = false;
                foreach (XPathItem item in items)
                {
                    if (item is XPathNavigator node)
                    {
                        if (node.NodeType == XPathNodeType.Root)
                        {
                            XPathNavigator child = node.Clone();
                            if (child.MoveToFirstChild())
                            {
                                do
                                {
                                    w.WriteNode(child, false);
                                }
                                while (child.MoveToNext());
                            }
                        }
                        else
                        {
                            w.WriteNode(node, false);
                        }
                        atomic = false;
                    }
                    else
                    {
                        w.WriteString((atomic ? " " : "") + item.Value);
                        atomic = true;
                    }
                }
            }
            return Parse(sb.ToString(), null);
        }

        private static XPathNavigator Parse(string xml, string? baseUri)
        {
            using XmlReader reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { ConformanceLevel = ConformanceLevel.Fragment, DtdProcessing = DtdProcessing.Ignore }, baseUri);
            return Xsl3.Tree.CompactDocument.Load(reader, XmlSpace.Preserve).CreateNavigator();
        }
    }
}
