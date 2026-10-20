// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.XPath;
using Xsl3.Xdm;

namespace Xsl3.XPath31.Functions
{
    // math:*, collation-taking variants of the string and sequence functions, and URI functions.
    internal static partial class FunctionLibrary
    {
        private static IList<XPathItem> Dbl(double d) => One(AtomicValue.OfDouble(d));

        private static IList<XPathItem> Math1(IList<XPathItem> arg, System.Func<double, double> f) => arg.Count == 0 ? Empty : Dbl(f(D(arg)));

        // A relative collation URI is resolved against the static base URI (F&O 3.1 §5.3.5).
        private static Collation Coll(IList<XPathItem> arg, FunctionContext c)
        {
            string uri = S(arg);
            if (c.StaticBaseUri != null && !UriReference.IsAbsolute(uri) && UriReference.IsAbsolute(c.StaticBaseUri))
            {
                uri = UriReference.Resolve(uri, c.StaticBaseUri);
            }
            return Collation.Resolve(uri);
        }

        private static void RegisterMisc()
        {
            // ---------------------------------------------------------- math (F&O §4.8)
            Define("math:pi() as xs:double", (c, a) => Dbl(System.Math.PI));
            Define("math:exp($arg as xs:double?) as xs:double?", (c, a) => Math1(a[0], System.Math.Exp));
            Define("math:exp10($arg as xs:double?) as xs:double?", (c, a) => Math1(a[0], x => System.Math.Pow(10, x)));
            Define("math:log($arg as xs:double?) as xs:double?", (c, a) => Math1(a[0], System.Math.Log));
            Define("math:log10($arg as xs:double?) as xs:double?", (c, a) => Math1(a[0], System.Math.Log10));
            Define("math:sqrt($arg as xs:double?) as xs:double?", (c, a) => Math1(a[0], System.Math.Sqrt));
            Define("math:sin($θ as xs:double?) as xs:double?", (c, a) => Math1(a[0], System.Math.Sin));
            Define("math:cos($θ as xs:double?) as xs:double?", (c, a) => Math1(a[0], System.Math.Cos));
            Define("math:tan($θ as xs:double?) as xs:double?", (c, a) => Math1(a[0], System.Math.Tan));
            Define("math:asin($arg as xs:double?) as xs:double?", (c, a) => Math1(a[0], System.Math.Asin));
            Define("math:acos($arg as xs:double?) as xs:double?", (c, a) => Math1(a[0], System.Math.Acos));
            Define("math:atan($arg as xs:double?) as xs:double?", (c, a) => Math1(a[0], System.Math.Atan));
            Define("math:atan2($y as xs:double, $x as xs:double) as xs:double", (c, a) => Dbl(System.Math.Atan2(D(a[0]), D(a[1]))));
            Define("math:pow($x as xs:double?, $y as xs:numeric) as xs:double?", (c, a) => a[0].Count == 0 ? Empty : Dbl(Pow(D(a[0]), (AtomicValue)a[1][0])));

            // ---------------------------------------------------------- collations (F&O §5.3)
            Define("fn:default-collation() as xs:string", (c, a) => Str(c.DefaultCollation.Uri));
            Define("fn:compare($comparand1 as xs:string?, $comparand2 as xs:string?, $collation as xs:string) as xs:integer?", (c, a) =>
                a[0].Count == 0 || a[1].Count == 0 ? Empty : Int(System.Math.Sign(Coll(a[2], c).Compare(S(a[0]), S(a[1])))));
            Define("fn:contains($arg1 as xs:string?, $arg2 as xs:string?, $collation as xs:string) as xs:boolean", (c, a) => Bool(StringMatch.Contains(S(a[0]), S(a[1]), Coll(a[2], c))));
            Define("fn:starts-with($arg1 as xs:string?, $arg2 as xs:string?, $collation as xs:string) as xs:boolean", (c, a) => Bool(StringMatch.StartsWith(S(a[0]), S(a[1]), Coll(a[2], c))));
            Define("fn:ends-with($arg1 as xs:string?, $arg2 as xs:string?, $collation as xs:string) as xs:boolean", (c, a) => Bool(StringMatch.EndsWith(S(a[0]), S(a[1]), Coll(a[2], c))));
            Define("fn:substring-before($arg1 as xs:string?, $arg2 as xs:string?, $collation as xs:string) as xs:string", (c, a) => Str(StringMatch.Before(S(a[0]), S(a[1]), Coll(a[2], c))));
            Define("fn:substring-after($arg1 as xs:string?, $arg2 as xs:string?, $collation as xs:string) as xs:string", (c, a) => Str(StringMatch.After(S(a[0]), S(a[1]), Coll(a[2], c))));
            Define("fn:index-of($seq as xs:anyAtomicType*, $search as xs:anyAtomicType, $collation as xs:string) as xs:integer*", (c, a) =>
            {
                FunctionContext cc = c.WithDefaultCollation(Coll(a[2], c));
                var search = (AtomicValue)a[1][0];
                var result = new List<XPathItem>();
                for (int i = 0; i < a[0].Count; i++)
                {
                    if (SequenceFunctions.EqualForIndexOf((AtomicValue)a[0][i], search, cc)) result.Add(AtomicValue.OfInteger(i + 1));
                }
                return result;
            });
            Define("fn:distinct-values($arg as xs:anyAtomicType*, $collation as xs:string) as xs:anyAtomicType*", (c, a) => SequenceFunctions.DistinctValues(a[0], c.WithDefaultCollation(Coll(a[1], c))));
            Define("fn:deep-equal($parameter1 as item()*, $parameter2 as item()*, $collation as xs:string) as xs:boolean", (c, a) => Bool(DeepEqual.Sequences(a[0], a[1], c.WithDefaultCollation(Coll(a[2], c)))));
            Define("fn:min($arg as xs:anyAtomicType*, $collation as xs:string) as xs:anyAtomicType?", (c, a) => Aggregates.MinMax(a[0], c.WithDefaultCollation(Coll(a[1], c)), max: false));
            Define("fn:max($arg as xs:anyAtomicType*, $collation as xs:string) as xs:anyAtomicType?", (c, a) => Aggregates.MinMax(a[0], c.WithDefaultCollation(Coll(a[1], c)), max: true));
            Define("fn:collation-key($key as xs:string) as xs:base64Binary", (c, a) => One(new BinaryValue(AtomicTypes.Base64Binary, c.DefaultCollation.KeyBytes(S(a[0])))));
            Define("fn:collation-key($key as xs:string, $collation as xs:string) as xs:base64Binary", (c, a) => One(new BinaryValue(AtomicTypes.Base64Binary, Coll(a[1], c).KeyBytes(S(a[0])))));
            Define("fn:contains-token($input as xs:string*, $token as xs:string) as xs:boolean", (c, a) => Bool(ContainsToken(a[0], S(a[1]), c.DefaultCollation)));
            Define("fn:contains-token($input as xs:string*, $token as xs:string, $collation as xs:string) as xs:boolean", (c, a) => Bool(ContainsToken(a[0], S(a[1]), Coll(a[2], c))));

            // ---------------------------------------------------------- URIs (F&O §6)
            Define("fn:encode-for-uri($uri-part as xs:string?) as xs:string", (c, a) => Str(PercentEncode(S(a[0]), ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or '.' or '~')));
            Define("fn:iri-to-uri($iri as xs:string?) as xs:string", (c, a) => Str(PercentEncode(S(a[0]), ch => ch > 0x20 && ch < 0x7F && ch is not ('<' or '>' or '"' or '{' or '}' or '|' or '\\' or '^' or '`'))));
            Define("fn:escape-html-uri($uri as xs:string?) as xs:string", (c, a) => Str(PercentEncode(S(a[0]), ch => ch >= 0x20 && ch < 0x7F)));
            Define("fn:resolve-uri($relative as xs:string?, $base as xs:string) as xs:anyURI?", (c, a) => a[0].Count == 0 ? Empty : One(new StringValue(AtomicTypes.AnyUri, ResolveUri(S(a[0]), S(a[1])))));
            Define("fn:resolve-uri($relative as xs:string?) as xs:anyURI?", (c, a) =>
            {
                if (a[0].Count == 0) return Empty;
                if (c.StaticBaseUri != null) return One(new StringValue(AtomicTypes.AnyUri, ResolveUri(S(a[0]), c.StaticBaseUri)));
                if (System.Uri.TryCreate(S(a[0]), System.UriKind.Absolute, out System.Uri? abs)) return One(new StringValue(AtomicTypes.AnyUri, S(a[0])));
                throw new XdmException("FONS0005", "The static base URI is absent");
            });
            Define("fn:default-language() as xs:language", (c, a) => One(new StringValue(AtomicTypes.Language, "en")));
            Define("fn:static-base-uri() as xs:anyURI?", (c, a) => c.StaticBaseUri == null ? Empty : One(new StringValue(AtomicTypes.AnyUri, c.StaticBaseUri)));

            // ---------------------------------------------------------- environment (F&O §19.5)
            // Xsl3: only with XsltSettings.EnableEnvironmentVariables (SPEC §21.1); otherwise there are none.
            Define("fn:environment-variable($name as xs:string) as xs:string?", (c, a) =>
                Xsl3.Runtime.XmlQueryRuntime.EnvironmentVariables && System.Environment.GetEnvironmentVariable(S(a[0])) is string value ? One(AtomicValue.OfString(value)) : Empty);
            Define("fn:available-environment-variables() as xs:string*", (c, a) =>
                Xsl3.Runtime.XmlQueryRuntime.EnvironmentVariables
                    ? System.Environment.GetEnvironmentVariables().Keys.Cast<string>().Order(System.StringComparer.Ordinal).Select(k => (XPathItem)AtomicValue.OfString(k)).ToList()
                    : Empty);
        }

        private static bool ContainsToken(IList<XPathItem> input, string token, Collation collation)
        {
            token = Lexical.Collapse(token);
            if (token.Length == 0)
            {
                return false;
            }
            foreach (XPathItem item in input)
            {
                foreach (string t in ((AtomicValue)item).Value.Split(new[] { ' ', '\t', '\n', '\r' }, System.StringSplitOptions.RemoveEmptyEntries))
                {
                    if (collation.Compare(t, token) == 0)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        // math:pow: an integer exponent gives the exact repeated product's double (so pow(-2, 3) is -8).
        private static double Pow(double x, AtomicValue y) => System.Math.Pow(x, Numeric.ToDouble(y));

        private static string PercentEncode(string s, System.Func<char, bool> keep)
        {
            var sb = new StringBuilder(s.Length);
            Span<byte> bytes = stackalloc byte[4];
            foreach (Rune r in s.EnumerateRunes())
            {
                if (r.IsAscii && keep((char)r.Value))
                {
                    sb.Append((char)r.Value);
                    continue;
                }
                int n = r.EncodeToUtf8(bytes);
                for (int i = 0; i < n; i++)
                {
                    sb.Append('%').Append(bytes[i].ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
                }
            }
            return sb.ToString();
        }

        private static string ResolveUri(string relative, string baseUri)
        {
            // A colon in the first segment must end a valid scheme ("a:b"); otherwise the reference is invalid (":").
            int colon = relative.IndexOf(':');
            int delimiter = relative.IndexOfAny(new[] { '/', '?', '#' });
            if (colon >= 0 && (delimiter < 0 || colon < delimiter))
            {
                string scheme = relative[..colon];
                if (scheme.Length == 0 || !char.IsAsciiLetter(scheme[0]) || !scheme.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '+' or '-' or '.'))
                {
                    throw new XdmException("FORG0002", $"'{relative}' is not a valid URI reference");
                }
            }
            if (relative.IndexOf('#') is int hash and >= 0 && relative.IndexOf('#', hash + 1) >= 0)
            {
                throw new XdmException("FORG0002", $"'{relative}' is not a valid URI reference: a fragment can't contain '#'");
            }
            if (UriReference.IsAbsolute(relative))
            {
                return relative;
            }
            if (!UriReference.IsAbsolute(baseUri) || !System.Uri.TryCreate(baseUri, System.UriKind.Absolute, out _) || baseUri.Contains('#'))
            {
                throw new XdmException("FORG0002", $"'{baseUri}' is not a valid absolute base URI");
            }
            return UriReference.Resolve(relative, baseUri);
        }
    }
}
