// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;
using Xsl3.W3C.Qt3;
using Xsl3.XPath31;
using Xsl3.XPath31.Functions;
using Xsl3.Xdm;

namespace Xsl3.W3C.Xslt30;

/// <summary>
/// Runs xslt30-test cases through Xsl3's XslCompiledTransform and checks their assertions (see the suite's
/// runner/assert.xsl for their meaning). The principal result is serialized as the stylesheet's xsl:output
/// says; tree assertions parse that serialization back. XPath assertions are evaluated with Xsl3's XPath 3.1
/// engine, with the result document as the context item.
/// </summary>
public sealed class Runner
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(30);

    private sealed record RunResult(string? Serialized, Exception? Error, IReadOnlyList<string> Messages)
    {
        public Dictionary<string, StringWriter> SecondaryResults { get; init; } = new();
    }

    public Outcome Run(TestCase test)
    {
        Outcome outcome = Outcome.Fail("timeout");
        using var cancel = new CancellationTokenSource();
        var thread = new Thread(() =>
        {
            try
            {
                // A test that runs away is stopped at its next template or function entry.
                Xsl3.Runtime.XmlQueryRuntime.Cancellation = cancel.Token;
                outcome = RunCore(test);
            }
            catch (Exception e)
            {
                outcome = Outcome.Fail($"harness: {e.GetType().Name}: {e.Message}");
            }
        }, 64 * 1024 * 1024);
        thread.IsBackground = true;
        thread.Start();
        if (!thread.Join(s_timeout))
        {
            cancel.Cancel();
            thread.Join(TimeSpan.FromSeconds(5));
            return Outcome.Fail("timeout");
        }
        return outcome;
    }

    private Outcome RunCore(TestCase test)
    {
        IReadOnlyList<TestEnvironment?> environments = test.Environments;
        if (environments.Any(e => e == null))
        {
            return Outcome.NotRun("unknown environment");
        }
        var envs = environments.Select(e => e!).ToList();
        string? unsupported = envs.SelectMany(e => e.UnsupportedParts).FirstOrDefault();
        if (unsupported != null)
        {
            return Outcome.NotRun("environment: " + unsupported);
        }
        XElement t = test.TestElement;
        if (t.Element(Catalog.Ns + "package") != null || t.Element(Catalog.Ns + "initial-function") != null)
        {
            return Outcome.NotRun("packages and initial functions (P3+)");
        }
        XElement? sheet = t.Elements(Catalog.Ns + "stylesheet").FirstOrDefault(s => (string?)s.Attribute("role") is null or "principal");
        if (sheet == null)
        {
            return Outcome.NotRun("no principal stylesheet");
        }
        var parameters = t.Elements(Catalog.Ns + "param").Select(TestEnvironment.Param0).Concat(envs.SelectMany(e => e.Params)).ToList();
        if (parameters.Any(p => p.Static))
        {
            return Outcome.NotRun("static parameters (P3)");
        }
        XElement? initialMode = t.Element(Catalog.Ns + "initial-mode");
        if (initialMode?.Attribute("select") != null)
        {
            return Outcome.NotRun("initial match selection by expression");
        }

        RunResult run = Execute(test, envs, Path.Combine(test.Set.Directory, (string)sheet.Attribute("file")!),
            ResolveQName(t.Element(Catalog.Ns + "initial-template"), "name"), ResolveQName(initialMode, "name"), parameters);
        return Check(test, test.Result, run);
    }

    private static XmlQualifiedName? ResolveQName(XElement? element, string attribute)
    {
        string? value = (string?)element?.Attribute(attribute);
        if (value == null)
        {
            return null;
        }
        if (value.StartsWith("Q{", StringComparison.Ordinal))
        {
            int close = value.IndexOf('}');
            return new XmlQualifiedName(value[(close + 1)..], value[2..close]);
        }
        int colon = value.IndexOf(':');
        return colon < 0
            ? new XmlQualifiedName(value)
            : new XmlQualifiedName(value[(colon + 1)..], element!.GetNamespaceOfPrefix(value[..colon])?.NamespaceName ?? "");
    }

    private RunResult Execute(TestCase test, List<TestEnvironment> envs, string stylesheet, XmlQualifiedName? initialTemplate, XmlQualifiedName? initialMode,
        List<TestEnvironment.Param> parameters)
    {
        var messages = new List<string>();
        var secondary = new Dictionary<string, StringWriter>(StringComparer.Ordinal);
        Xsl3.Runtime.XmlQueryRuntime.ResultDocumentHandler = (uri, method) => secondary[uri] = new Utf8StringWriter();
        try
        {
            var resolver = new TestResolver(envs);
            XPathNavigator? input = null;
            TestEnvironment.Source? inputSource = null;
            var args = new XsltArgumentList();
            args.XsltMessageEncountered += (_, e) => messages.Add(e.Message);
            foreach (TestEnvironment env in envs)
            {
                foreach (TestEnvironment.Source source in env.Sources)
                {
                    if (source.Role == null)
                    {
                        continue; // available by URI only
                    }
                    XPathNavigator doc = LoadSource(source);
                    if (source.Role == ".")
                    {
                        input = doc;
                        inputSource = source;
                    }
                    else if (source.Role.StartsWith('$'))
                    {
                        args.AddParam(source.Role[1..], "", doc);
                    }
                }
            }
            foreach (TestEnvironment.Param p in parameters)
            {
                (string ns, string local) = SplitParamName(p);
                args.AddParam(local, ns, ToArgument(Evaluate(p.Select ?? "()", p.Element, input)));
            }

            var transform = new XslCompiledTransform { InitialTemplate = initialTemplate, InitialMode = initialMode };
            // Some stylesheets declare entities in an internal DTD subset.
            using (XmlReader stylesheetReader = XmlReader.Create(stylesheet, new XmlReaderSettings { DtdProcessing = DtdProcessing.Parse, XmlResolver = resolver }))
            {
                transform.Load(stylesheetReader, new XsltSettings(enableDocumentFunction: true, enableScript: false), resolver);
            }
            var sw = new StringWriter();
            // No source document: the context is an empty document (standing in for an absent context item).
            XPathNavigator context = input ?? EmptyDocument();
            try
            {
                // The engine creates the principal writer, so xsl:result-document without href can configure it; the
                // bytes are decoded as their XML declaration says.
                var bytes = new MemoryStream();
                transform.Transform(context, args, bytes, resolver);
                sw.Write(Decode(bytes.ToArray()));
            }
            catch (Exception e) when (inputSource != null && e.Message.StartsWith("Whitespace cannot be stripped", StringComparison.Ordinal))
            {
                // xsl:strip-space needs the input as a reader (upstream can't strip an already-loaded tree).
                sw = new StringWriter();
                messages.Clear();
                secondary.Clear();
                var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Parse, XmlResolver = new XmlUrlResolver() };
                using XmlReader reader = inputSource.Content != null
                    ? XmlReader.Create(new StringReader(inputSource.Content), settings, inputSource.File ?? "")
                    : XmlReader.Create(inputSource.File!, settings);
                var bytes = new MemoryStream();
                transform.Transform(reader, args, bytes, resolver);
                sw.Write(Decode(bytes.ToArray()));
            }
            return new RunResult(sw.ToString(), null, messages) { SecondaryResults = secondary };
        }
        catch (Exception e) when (e is not OutOfMemoryException and not ThreadAbortException)
        {
            return new RunResult(null, e, messages);
        }
    }

    // Serialized output as text: the encoding of its XML declaration (or byte order mark), else UTF-8.
    private static string Decode(byte[] bytes)
    {
        Encoding encoding = new UTF8Encoding(false);
        if (bytes.Length >= 2 && (bytes[0] == 0xFF && bytes[1] == 0xFE || bytes[0] == 0xFE && bytes[1] == 0xFF))
        {
            encoding = bytes[0] == 0xFF ? Encoding.Unicode : Encoding.BigEndianUnicode;
        }
        else
        {
            string head = Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 200));
            var m = System.Text.RegularExpressions.Regex.Match(head, @"^<\?xml[^>]*encoding=[""']([^""']+)");
            if (m.Success)
            {
                try
                {
                    encoding = Encoding.GetEncoding(m.Groups[1].Value);
                }
                catch (ArgumentException)
                {
                }
            }
        }
        return encoding.GetString(bytes); // a byte order mark stays, as U+FEFF
    }

    private static XmlWriterSettings OutputSettings(XslCompiledTransform transform)
    {
        XmlWriterSettings settings = transform.OutputSettings?.Clone() ?? new XmlWriterSettings();
        settings.CloseOutput = false;
        return settings;
    }

    private static XPathNavigator EmptyDocument()
    {
        var doc = new InternalXPathDocument(new NameTable());
        doc.LoadFromWriter(InternalXPathDocument.LoadFlags.None, "").Close();
        return doc.CreateNavigator();
    }

    private static (string Ns, string Local) SplitParamName(TestEnvironment.Param p)
    {
        int colon = p.Name.IndexOf(':');
        return colon < 0 ? ("", p.Name) : (p.Element.GetNamespaceOfPrefix(p.Name[..colon])?.NamespaceName ?? "", p.Name[(colon + 1)..]);
    }

    // XSLT 1.0 parameter values are strings, numbers, booleans and node-sets.
    private static object ToArgument(IList<XPathItem> value)
    {
        if (value.Count == 1 && value[0] is AtomicValue a)
        {
            return a.Type.IsNumeric ? Numeric.ToDouble(a) : a.Type.Primitive == Primitive.Boolean ? ((BooleanValue)a).Value == "true" : a.Value;
        }
        if (value.All(i => i is XPathNavigator))
        {
            return value.Count == 1 ? value[0] : new XPathDocument(new StringReader("<x/>")).CreateNavigator().Select("/..");
        }
        return string.Join(" ", value.Select(i => i.Value));
    }

    private static IList<XPathItem> Evaluate(string expression, XElement scope, XPathItem? context, IList<XPathItem>? result = null)
    {
        var sc = new XPathStaticContext();
        foreach (XAttribute ns in scope.AncestorsAndSelf().SelectMany(e => e.Attributes()).Where(a => a.IsNamespaceDeclaration).Reverse())
        {
            string prefix = ns.Name.NamespaceName.Length == 0 ? "" : ns.Name.LocalName;
            if (prefix.Length > 0)
            {
                sc.Namespaces[prefix] = ns.Value;
            }
        }
        var variables = new Dictionary<QName, IList<XPathItem>>();
        if (result != null)
        {
            var name = new QName("", "result");
            sc.ExternalVariables.Add(name);
            variables[name] = result;
        }
        return XPath31Compiler.Compile(expression, sc).Evaluate(context, variables, 0);
    }

    // Source documents are read-only, so one parse serves every test that uses the file (the unicode-90 sets
    // share a few documents of tens of megabytes).
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, XPathDocument> s_documents = new(StringComparer.Ordinal);

    private static XPathNavigator LoadSource(TestEnvironment.Source source)
    {
        if (source.Content == null)
        {
            return s_documents.GetOrAdd(Path.GetFullPath(source.File!), Parse).CreateNavigator();
        }
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Parse, XmlResolver = new XmlUrlResolver() };
        using XmlReader reader = XmlReader.Create(new StringReader(source.Content), settings, source.File ?? "");
        return new XPathDocument(reader, XmlSpace.Preserve).CreateNavigator();
    }

    private static XPathDocument Parse(string file)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Parse, XmlResolver = new XmlUrlResolver() };
        using XmlReader reader = XmlReader.Create(file, settings);
        return new XPathDocument(reader, XmlSpace.Preserve);
    }

    // ------------------------------------------------------------ assertions

    private Outcome Check(TestCase test, XElement assertion, RunResult run)
    {
        string kind = assertion.Name.LocalName;
        switch (kind)
        {
            case "all-of":
                foreach (XElement a in assertion.Elements())
                {
                    Outcome o = Check(test, a, run);
                    if (o.Kind != OutcomeKind.Pass) return o;
                }
                return Outcome.Pass();
            case "any-of":
            {
                var details = new List<string>();
                foreach (XElement a in assertion.Elements())
                {
                    Outcome o = Check(test, a, run);
                    if (o.Kind == OutcomeKind.Pass) return o;
                    details.Add(o.Detail);
                }
                return Outcome.Fail(string.Join(" | ", details));
            }
            case "not":
                return Check(test, assertion.Elements().Single(), run).Kind == OutcomeKind.Pass ? Outcome.Fail("not: the assertion holds") : Outcome.Pass();
            case "error":
            case "assert-serialization-error":
            {
                string code = (string?)assertion.Attribute("code") ?? "*";
                return run.Error != null ? Outcome.Pass($"error code {code} expected; got {Describe(run.Error)}") : Outcome.Fail($"expected error {code}, got a result");
            }
        }
        if (run.Error != null)
        {
            return Outcome.Fail($"{kind} expected, got {Describe(run.Error)}");
        }
        string serialized = run.Serialized!;
        try
        {
            switch (kind)
            {
                case "assert-xml":
                {
                    string? file = (string?)assertion.Attribute("file");
                    string expected = file != null ? File.ReadAllText(Path.Combine(test.Set.Directory, file)) : assertion.Value;
                    return XmlEquals(serialized, expected) ? Outcome.Pass() : Outcome.Fail($"assert-xml mismatch: got {Trim(serialized)}");
                }
                case "assert":
                {
                    XPathNavigator doc = ResultDocument(serialized);
                    bool ok = Sequences.EffectiveBooleanValue(Evaluate(assertion.Value, assertion, doc, new XPathItem[] { doc }));
                    return ok ? Outcome.Pass() : Outcome.Fail($"assert {Trim(assertion.Value)} failed on {Trim(serialized)}");
                }
                case "assert-string-value":
                {
                    string actual = ResultDocument(serialized).Value, expected = assertion.Value;
                    if ((string?)assertion.Attribute("normalize-space") is "true" or "1")
                    {
                        actual = Lexical.Collapse(actual);
                        expected = Lexical.Collapse(expected);
                    }
                    return actual == expected ? Outcome.Pass() : Outcome.Fail($"expected string value '{Trim(expected)}', got '{Trim(actual)}'");
                }
                case "serialization-matches":
                {
                    string flags = (string?)assertion.Attribute("flags") ?? "";
                    return XPathRegex.Get(assertion.Value, flags).Regex.IsMatch(serialized) ? Outcome.Pass() : Outcome.Fail($"serialization doesn't match /{Trim(assertion.Value)}/: {Trim(serialized)}");
                }
                case "assert-serialization":
                {
                    string? file = (string?)assertion.Attribute("file");
                    string expected = file != null ? File.ReadAllText(Path.Combine(test.Set.Directory, file)) : assertion.Value;
                    bool same = serialized.ReplaceLineEndings("\n") == expected.ReplaceLineEndings("\n") || XmlEquals(serialized, expected);
                    return same ? Outcome.Pass() : Outcome.Fail($"assert-serialization mismatch: got {Trim(serialized)}");
                }
                case "assert-message":
                {
                    foreach (string message in run.Messages)
                    {
                        var messageRun = new RunResult(message, null, Array.Empty<string>());
                        if (assertion.Elements().All(a => Check(test, a, messageRun).Kind == OutcomeKind.Pass))
                        {
                            return Outcome.Pass();
                        }
                    }
                    return Outcome.Fail($"no xsl:message satisfies the assertion ({run.Messages.Count} message(s))");
                }
                case "assert-type":
                case "assert-eq":
                case "assert-deep-eq":
                case "assert-count":
                case "assert-empty":
                case "assert-true":
                case "assert-false":
                {
                    XPathNavigator doc = ResultDocument(serialized);
                    string text = assertion.Value;
                    string expression = kind switch
                    {
                        "assert-type" => $"$result instance of {text}",
                        "assert-eq" => $"$result eq ({text})",
                        "assert-deep-eq" => $"deep-equal($result, ({text}))",
                        "assert-count" => $"count($result) = {text}",
                        "assert-empty" => "empty($result)",
                        "assert-true" => "$result instance of xs:boolean and $result",
                        _ => "$result instance of xs:boolean and not($result)",
                    };
                    bool ok = Sequences.EffectiveBooleanValue(Evaluate(expression, assertion, doc, new XPathItem[] { doc }));
                    return ok ? Outcome.Pass() : Outcome.Fail($"{kind} {Trim(text)} failed on {Trim(serialized)}");
                }
                case "assert-result-document":
                {
                    string uri = (string)assertion.Attribute("uri")!;
                    foreach (var (written, text) in run.SecondaryResults)
                    {
                        if (written == uri || written.EndsWith("/" + uri, StringComparison.Ordinal))
                        {
                            var docRun = new RunResult(text.ToString(), null, Array.Empty<string>());
                            foreach (XElement a in assertion.Elements())
                            {
                                Outcome o = Check(test, a, docRun);
                                if (o.Kind != OutcomeKind.Pass) return o;
                            }
                            return Outcome.Pass();
                        }
                    }
                    return Outcome.Fail($"no result document {uri} ({run.SecondaryResults.Count} written)");
                }
                case "assert-posture-and-sweep":
                    return Outcome.NotRun("streamability (P7)");
                default:
                    return Outcome.Fail($"assertion {kind} not supported by the runner");
            }
        }
        catch (Exception e) when (e is XdmException or XPathStaticException or XmlException)
        {
            return Outcome.Fail($"{kind} could not be evaluated: {OneLine(e.Message)}");
        }
    }

    /// <summary>The result tree: the serialization parsed back as a document (a fragment: text and several elements are fine).</summary>
    private static XPathNavigator ResultDocument(string serialized)
    {
        string text = StripDeclaration(serialized);
        var settings = new XmlReaderSettings { ConformanceLevel = ConformanceLevel.Fragment, DtdProcessing = DtdProcessing.Ignore };
        using XmlReader reader = XmlReader.Create(new StringReader(text), settings);
        return new XPathDocument(reader, XmlSpace.Preserve).CreateNavigator();
    }

    private static string StripDeclaration(string s)
    {
        string trimmed = s.TrimStart('﻿');
        if (trimmed.StartsWith("<?xml ", StringComparison.Ordinal) || trimmed.StartsWith("<?xml?", StringComparison.Ordinal))
        {
            int end = trimmed.IndexOf("?>", StringComparison.Ordinal);
            return end < 0 ? trimmed : trimmed[(end + 2)..];
        }
        return trimmed;
    }

    private static bool XmlEquals(string actual, string expected)
    {
        actual = StripDeclaration(actual);
        expected = StripDeclaration(expected);
        if (actual == expected)
        {
            return true;
        }
        try
        {
            XElement a = XElement.Parse("<w>" + actual + "</w>", LoadOptions.None);
            XElement e = XElement.Parse("<w>" + expected + "</w>", LoadOptions.None);
            return XNode.DeepEquals(Normalize(a), Normalize(e));
        }
        catch (XmlException)
        {
            return false;
        }
    }

    // Namespace declarations are compared by their effect, not their spelling; attribute order doesn't matter.
    private static XElement Normalize(XElement e) =>
        new(e.Name,
            e.Attributes().Where(a => !a.IsNamespaceDeclaration).OrderBy(a => a.Name.ToString()).Select(a => new XAttribute(a.Name, a.Value)),
            e.Nodes().Select(n => n is XElement c ? Normalize(c) : n));

    private static string Trim(string s) => s.Length > 160 ? s[..157].ReplaceLineEndings(" ") + "..." : s.ReplaceLineEndings(" ");

    private static string OneLine(string s) => Trim(s);

    // An exception and its inner exceptions, which carry the detail of "XSLT compile error".
    private static string Describe(Exception e)
    {
        var parts = new List<string>();
        for (Exception? x = e; x != null && parts.Count < 3; x = x.InnerException)
        {
            parts.Add($"{x.GetType().Name}: {x.Message}");
        }
        string all = string.Join(" <- ", parts).ReplaceLineEndings(" ");
        return all.Length > 500 ? all[..497] + "..." : all;
    }

    /// <summary>Maps the URIs an environment gives documents to their files.</summary>
    private sealed class TestResolver(IReadOnlyList<TestEnvironment> envs) : XmlUrlResolver
    {
        public override object? GetEntity(Uri absoluteUri, string? role, Type? ofObjectToReturn)
        {
            foreach (TestEnvironment env in envs)
            {
                foreach (TestEnvironment.Source s in env.Sources)
                {
                    if (s.Uri != null && (absoluteUri.OriginalString == s.Uri || absoluteUri.AbsoluteUri == s.Uri || absoluteUri.AbsoluteUri.EndsWith("/" + s.Uri, StringComparison.Ordinal)))
                    {
                        return s.Content != null ? new MemoryStream(Encoding.UTF8.GetBytes(s.Content)) : File.OpenRead(s.File!);
                    }
                }
            }
            return base.GetEntity(absoluteUri, role, ofObjectToReturn);
        }
    }
}

/// <summary>A StringWriter whose writers declare UTF-8, the encoding the test results assume.</summary>
internal sealed class Utf8StringWriter : StringWriter
{
    public override Encoding Encoding => Encoding.UTF8;
}
