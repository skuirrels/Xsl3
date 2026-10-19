// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;
using Xsl3.XPath31;
using Xsl3.XPath31.Functions;
using Xsl3.Xdm;

namespace Xsl3.W3C.Qt3;

public enum OutcomeKind { Pass, Fail, NotRun }

public sealed record Outcome(OutcomeKind Kind, string Detail = "")
{
    public static Outcome Pass(string detail = "") => new(OutcomeKind.Pass, detail);
    public static Outcome Fail(string detail) => new(OutcomeKind.Fail, detail);
    public static Outcome NotRun(string detail) => new(OutcomeKind.NotRun, detail);
}

/// <summary>
/// Runs QT3 test cases against Xsl3's XPath 3.1 implementation and checks their assertions
/// (see the suite's guide/running.html). Assertions that are themselves XPath (assert, assert-eq,
/// assert-deep-eq, assert-type) are evaluated with Xsl3 too, with the result bound to <c>$result</c>.
/// </summary>
public sealed class Runner
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(20);
    private readonly ConcurrentDictionary<string, XPathNavigator> _documents = new();

    /// <summary>The implicit timezone the tests run with. QT3 expects results not to depend on it, except where they say so.</summary>
    public int ImplicitTimezoneMinutes { get; init; } = 0;

    public Outcome Run(TestCase test)
    {
        Outcome outcome = Outcome.Fail("timeout");
        // Each test runs on its own thread with a large stack (deeply nested expressions recurse in the compiler)
        // and a timeout, so one runaway test can't stop the run.
        var thread = new Thread(() =>
        {
            try
            {
                outcome = RunCore(test);
            }
            catch (Exception e)
            {
                outcome = Outcome.Fail($"harness: {e.GetType().Name}: {e.Message}");
            }
        }, 64 * 1024 * 1024);
        thread.IsBackground = true;
        thread.Start();
        thread.Join(s_timeout);
        return outcome;
    }

    private Outcome RunCore(TestCase test)
    {
        TestEnvironment? env = test.Environment;
        if (env == null)
        {
            return Outcome.NotRun("unknown environment");
        }
        if (env.Sources.Any(s => s.Validation is "strict" or "lax"))
        {
            return Outcome.NotRun("schema validation");
        }

        XPathStaticContext sc = CreateStaticContext(env, test);
        var variables = new Dictionary<QName, IList<XPathItem>>();
        XPathItem? contextItem = null;
        try
        {
            foreach (TestEnvironment.Source source in env.Sources)
            {
                XPathNavigator doc = LoadDocument(source.File);
                if (source.Role == ".")
                {
                    contextItem = doc;
                }
                else if (source.Role.StartsWith('$'))
                {
                    var name = new QName("", source.Role[1..]);
                    sc.ExternalVariables.Add(name);
                    variables[name] = new XPathItem[] { doc };
                }
            }
            foreach (TestEnvironment.Param p in env.Params)
            {
                var name = new QName("", p.Name);
                IList<XPathItem> value = p.Select != null ? Evaluate(p.Select, CreateStaticContext(env), contextItem, null) : Array.Empty<XPathItem>();
                sc.ExternalVariables.Add(name);
                variables[name] = value;
            }
            if (env.ContextItem != null)
            {
                IList<XPathItem> ctx = Evaluate(env.ContextItem, CreateStaticContext(env), contextItem, null);
                contextItem = ctx.Count == 1 ? ctx[0] : null;
            }
        }
        catch (Exception e)
        {
            return Outcome.NotRun($"environment: {e.Message}");
        }

        Result result;
        try
        {
            ApplyDecimalFormats(env, sc);
            if (env.DefaultCollation != null)
            {
                sc.DefaultCollation = env.DefaultCollation;
            }
            XPath31Executable exe = XPath31Compiler.Compile(test.Expression, sc);
            result = Result.Of(exe.Evaluate(contextItem, variables, ImplicitTimezoneMinutes, new TestResolver(env)));
        }
        catch (XPathStaticException e)
        {
            result = Result.Error(e.Code, e.Message);
        }
        catch (XdmException e)
        {
            result = Result.Error(e.Code, e.Message);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            result = Result.Crash(e);
        }

        return Check(test.Result, result, env, contextItem);
    }

    private static XPathStaticContext CreateStaticContext(TestEnvironment env, TestCase? test = null)
    {
        var sc = new XPathStaticContext();
        // The static base URI is the environment's, else the test set file's location.
        sc.BaseUri = env.StaticBaseUri switch
        {
            "#UNDEFINED" => null,
            string uri => uri,
            null => test != null ? new System.Uri(Path.GetFullPath(test.Set.File)).AbsoluteUri : null,
        };
        foreach (var (prefix, uri) in env.Namespaces)
        {
            if (prefix.Length == 0)
            {
                sc.DefaultElementNamespace = uri;
            }
            else
            {
                sc.Namespaces[prefix] = uri;
            }
        }
        return sc;
    }

    private static void ApplyDecimalFormats(TestEnvironment env, XPathStaticContext sc)
    {
        foreach (XElement df in env.DecimalFormats)
        {
            var properties = df.Attributes().Where(a => !a.IsNamespaceDeclaration && a.Name.LocalName != "name")
                .Select(a => new KeyValuePair<string, string>(a.Name.LocalName, a.Value));
            string? name = (string?)df.Attribute("name");
            if (name == null)
            {
                sc.DefaultDecimalFormat = XPathDecimalFormat.FromProperties(properties);
                continue;
            }
            int colon = name.IndexOf(':');
            string ns = colon < 0 ? "" : df.GetNamespaceOfPrefix(name[..colon])?.NamespaceName ?? "";
            sc.DecimalFormats[new QName(ns, colon < 0 ? name : name[(colon + 1)..])] = XPathDecimalFormat.FromProperties(properties);
        }
    }

    /// <summary>Maps the URIs an environment declares for its documents and resources to their files.</summary>
    private sealed class TestResolver(TestEnvironment env) : XmlUrlResolver
    {
        public override object? GetEntity(System.Uri absoluteUri, string? role, System.Type? ofObjectToReturn)
        {
            foreach (TestEnvironment.Resource r in env.Resources)
            {
                if (r.Uri == absoluteUri.OriginalString || r.Uri == absoluteUri.AbsoluteUri)
                {
                    return File.OpenRead(r.File);
                }
            }
            return base.GetEntity(absoluteUri, role, ofObjectToReturn);
        }

        public override System.Uri ResolveUri(System.Uri? baseUri, string? relativeUri)
        {
            foreach (TestEnvironment.Resource r in env.Resources)
            {
                if (r.Uri == relativeUri)
                {
                    return new System.Uri(r.Uri, System.UriKind.RelativeOrAbsolute).IsAbsoluteUri ? new System.Uri(r.Uri) : base.ResolveUri(baseUri, relativeUri);
                }
            }
            return base.ResolveUri(baseUri, relativeUri);
        }
    }

    private XPathNavigator LoadDocument(string file) => _documents.GetOrAdd(Path.GetFullPath(file), path =>
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Parse, XmlResolver = new XmlUrlResolver() };
        using XmlReader reader = XmlReader.Create(path, settings);
        return new XPathDocument(reader, XmlSpace.Preserve).CreateNavigator();
    });

    private IList<XPathItem> Evaluate(string expression, XPathStaticContext sc, XPathItem? contextItem, IList<XPathItem>? result)
    {
        var variables = new Dictionary<QName, IList<XPathItem>>();
        if (result != null)
        {
            var name = new QName("", "result");
            sc.ExternalVariables.Add(name);
            variables[name] = result;
        }
        return XPath31Compiler.Compile(expression, sc).Evaluate(contextItem, variables, ImplicitTimezoneMinutes);
    }

    // ------------------------------------------------------------ assertions

    private sealed record Result(IList<XPathItem>? Value, string? ErrorCode, string Message)
    {
        public static Result Of(IList<XPathItem> value) => new(value, null, "");
        public static Result Error(string code, string message) => new(null, code, message);
        public static Result Crash(Exception e) => new(null, "!crash", $"{e.GetType().Name}: {e.Message}");
        public bool IsError => ErrorCode != null;
        public override string ToString() => IsError ? $"error {ErrorCode}: {Message}" : $"[{string.Join(", ", Value!.Take(10).Select(i => i switch { AtomicValue a => $"{a.Type.Name}('{a.Value}')", XdmFunction f => f.Describe(), _ => $"{((XPathNavigator)i).NodeType}" }))}{(Value!.Count > 10 ? ", …" : "")}]";
    }

    private Outcome Check(XElement assertion, Result result, TestEnvironment env, XPathItem? contextItem)
    {
        string kind = assertion.Name.LocalName;
        if (result.ErrorCode == "!crash")
        {
            return Outcome.Fail("crash: " + result.Message);
        }
        if (kind == "error")
        {
            string code = (string)assertion.Attribute("code")!;
            if (!result.IsError)
            {
                return Outcome.Fail($"expected error {code}, got {result}");
            }
            // Any error passes, as the QT3 guide allows; a different code is recorded.
            return Outcome.Pass(result.ErrorCode == code || code == "*" ? "" : $"error code {result.ErrorCode} instead of {code}");
        }
        if (kind == "any-of")
        {
            var outcomes = assertion.Elements().Select(a => Check(a, result, env, contextItem)).ToList();
            return outcomes.FirstOrDefault(o => o.Kind == OutcomeKind.Pass) ?? Outcome.Fail(string.Join(" | ", outcomes.Select(o => o.Detail)));
        }
        if (kind == "all-of")
        {
            var outcomes = assertion.Elements().Select(a => Check(a, result, env, contextItem)).ToList();
            return outcomes.FirstOrDefault(o => o.Kind != OutcomeKind.Pass) ?? Outcome.Pass();
        }
        if (kind == "not")
        {
            Outcome inner = Check(assertion.Elements().Single(), result, env, contextItem);
            return inner.Kind == OutcomeKind.Pass ? Outcome.Fail("not: " + result) : Outcome.Pass();
        }
        if (result.IsError)
        {
            return Outcome.Fail($"{kind} expected, got {result}");
        }

        IList<XPathItem> value = result.Value!;
        string text = assertion.Value;
        try
        {
            switch (kind)
            {
                case "assert-true":
                    return value.Count == 1 && value[0] is BooleanValue { Boolean: true } ? Outcome.Pass() : Outcome.Fail($"expected true, got {result}");
                case "assert-false":
                    return value.Count == 1 && value[0] is BooleanValue { Boolean: false } ? Outcome.Pass() : Outcome.Fail($"expected false, got {result}");
                case "assert-empty":
                    return value.Count == 0 ? Outcome.Pass() : Outcome.Fail($"expected empty, got {result}");
                case "assert-count":
                    return value.Count == int.Parse(text.Trim(), System.Globalization.CultureInfo.InvariantCulture) ? Outcome.Pass() : Outcome.Fail($"expected {text.Trim()} items, got {value.Count}");
                case "assert-string-value":
                {
                    string actual = string.Join(" ", value.Select(Sequences.StringValue));
                    string expected = text;
                    if ((string?)assertion.Attribute("normalize-space") == "true")
                    {
                        actual = Lexical.Collapse(actual);
                        expected = Lexical.Collapse(expected);
                    }
                    return actual == expected ? Outcome.Pass() : Outcome.Fail($"expected string value '{Trim(expected)}', got '{Trim(actual)}'");
                }
                case "assert":
                    return XPathAssertion(text, value, env, contextItem, out string detail) ? Outcome.Pass() : Outcome.Fail($"assert {Trim(text)} failed on {result}{detail}");
                case "assert-eq":
                    if (value.Count != 1)
                    {
                        return Outcome.Fail($"assert-eq {Trim(text)}: expected one item, got {result}");
                    }
                    // $result eq VALUE: a node is atomized.
                    return XPathAssertion($"deep-equal(data($result), ({text}))", value, env, contextItem, out string d1) ? Outcome.Pass() : Outcome.Fail($"assert-eq {Trim(text)} failed on {result}{d1}");
                case "assert-deep-eq":
                    return XPathAssertion($"deep-equal($result, ({text}))", value, env, contextItem, out string d2) ? Outcome.Pass() : Outcome.Fail($"assert-deep-eq {Trim(text)} failed on {result}{d2}");
                case "assert-type":
                    return XPathAssertion($"$result instance of {text}", value, env, contextItem, out string d3) ? Outcome.Pass() : Outcome.Fail($"assert-type {text} failed on {result}{d3}");
                case "assert-permutation":
                {
                    IList<XPathItem> expected = Evaluate(text, CreateStaticContext(env), contextItem, null);
                    return IsPermutation(value, expected) ? Outcome.Pass() : Outcome.Fail($"assert-permutation {Trim(text)} failed on {result}");
                }
                case "assert-xml":
                {
                    string? file = (string?)assertion.Attribute("file");
                    string expectedXml = file != null ? File.ReadAllText(Path.Combine(env.BaseDirectory, file)) : text;
                    return XmlEquals(value, expectedXml, (string?)assertion.Attribute("ignore-prefixes") == "true") ? Outcome.Pass() : Outcome.Fail($"assert-xml mismatch: got {Trim(Serialize(value))}");
                }
                default:
                    return Outcome.Fail($"assertion {kind} not supported by the runner");
            }
        }
        catch (Exception e) when (e is XdmException or XPathStaticException)
        {
            return Outcome.Fail($"{kind} could not be evaluated: {e.Message}");
        }
    }

    private bool XPathAssertion(string expression, IList<XPathItem> value, TestEnvironment env, XPathItem? contextItem, out string detail)
    {
        IList<XPathItem> r = Evaluate(expression, CreateStaticContext(env), contextItem, value);
        detail = "";
        return Sequences.EffectiveBooleanValue(r);
    }

    private static bool IsPermutation(IList<XPathItem> actual, IList<XPathItem> expected)
    {
        if (actual.Count != expected.Count)
        {
            return false;
        }
        var context = new FunctionContext(0, (a, b) => a.ComparePosition(b) switch { XmlNodeOrder.Before => -1, XmlNodeOrder.After => 1, _ => 0 });
        var remaining = expected.ToList();
        foreach (XPathItem item in actual)
        {
            int match = remaining.FindIndex(e => DeepEqual.Items(item, e, context));
            if (match < 0)
            {
                return false;
            }
            remaining.RemoveAt(match);
        }
        return true;
    }

    // The result serialised as XML: nodes as markup, adjacent atomic values separated by spaces.
    private static string Serialize(IList<XPathItem> value)
    {
        var sb = new StringBuilder();
        bool lastAtomic = false;
        foreach (XPathItem item in value)
        {
            if (item is XPathNavigator nav)
            {
                sb.Append(nav.NodeType switch
                {
                    XPathNodeType.Root => nav.InnerXml,
                    XPathNodeType.Attribute => $"{nav.Name}=\"{System.Security.SecurityElement.Escape(nav.Value)}\"",
                    _ => nav.OuterXml,
                });
                lastAtomic = false;
            }
            else
            {
                if (lastAtomic)
                {
                    sb.Append(' ');
                }
                sb.Append(System.Security.SecurityElement.Escape(item.Value));
                lastAtomic = true;
            }
        }
        return sb.ToString();
    }

    private static bool XmlEquals(IList<XPathItem> value, string expected, bool ignorePrefixes)
    {
        string actual = Serialize(value);
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

    // Namespace declarations are compared by their effect, not their spelling.
    private static XElement Normalize(XElement e) =>
        new(e.Name,
            e.Attributes().Where(a => !a.IsNamespaceDeclaration).OrderBy(a => a.Name.ToString()).Select(a => new XAttribute(a.Name, a.Value)),
            e.Nodes().Select(n => n is XElement c ? Normalize(c) : n));

    private static string Trim(string s) => s.Length > 120 ? s[..117].ReplaceLineEndings(" ") + "..." : s.ReplaceLineEndings(" ");
}
