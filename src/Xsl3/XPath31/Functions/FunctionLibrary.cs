// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.Linq;
using System.Xml.XPath;
using Xsl3.Xdm;

namespace Xsl3.XPath31.Functions
{
    /// <summary>The implementation of a built-in function: arguments arrive already converted to the declared types.</summary>
    internal delegate IList<XPathItem> FunctionBody(FunctionContext context, IList<XPathItem>[] args);

    /// <summary>A built-in function of one arity (F&amp;O 3.1), as declared by its signature.</summary>
    internal sealed class FunctionDefinition
    {
        public FunctionDefinition(int id, QName name, IReadOnlyList<(string Name, SequenceTypeTest Type)> parameters, SequenceTypeTest returnType, FunctionBody body, bool variadic)
        {
            Id = id;
            Name = name;
            Parameters = parameters;
            ReturnType = returnType;
            Body = body;
            IsVariadic = variadic;
        }

        /// <summary>Index in <see cref="FunctionLibrary.ById"/>; compiled code refers to functions by it.</summary>
        public int Id { get; }

        public QName Name { get; }

        public IReadOnlyList<(string Name, SequenceTypeTest Type)> Parameters { get; }

        public int Arity => Parameters.Count;

        public SequenceTypeTest ReturnType { get; }

        public FunctionBody Body { get; }

        /// <summary>fn:concat: any arity of two or more, every argument typed like the last declared one.</summary>
        public bool IsVariadic { get; }

        public SequenceTypeTest ParameterType(int index) => Parameters[System.Math.Min(index, Parameters.Count - 1)].Type;

        public IList<XPathItem> Invoke(FunctionContext context, IList<XPathItem>[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                args[i] = Coercion.Coerce(args[i], ParameterType(i), this, i);
            }
            return Body(context, args);
        }

        public override string ToString() => $"{Name}#{Arity}";
    }

    /// <summary>
    /// The function conversion rules (XPath 3.1 §3.1.5.2) applied to an argument: atomisation for atomic
    /// parameter types, casting of xs:untypedAtomic, numeric and URI promotion, then a type check (XPTY0004).
    /// </summary>
    internal static class Coercion
    {
        // (The argument is named only when there is an error: a closure per argument and call costs, on every call.)
        public static IList<XPathItem> Coerce(IList<XPathItem> value, SequenceTypeTest expected, FunctionDefinition function, int argIndex) =>
            CoerceTo(value, expected, "XPTY0004", null, function, argIndex);

        /// <summary>The function conversion rules to <paramref name="expected"/>; <paramref name="errorCode"/> if the value doesn't conform.</summary>
        public static IList<XPathItem> CoerceTo(IList<XPathItem> value, SequenceTypeTest expected, string errorCode, System.Func<string> what) =>
            CoerceTo(value, expected, errorCode, what, null, 0);

        private static IList<XPathItem> CoerceTo(IList<XPathItem> value, SequenceTypeTest expected, string errorCode, System.Func<string>? what, FunctionDefinition? function, int argIndex)
        {
            ItemTest? itemTest = expected.Item;
            if (itemTest is AnyItemTest && expected.Cardinality == Cardinality.ZeroOrMore)
            {
                // Xsl3 (P4): item()* accepts anything as it is.
                return value;
            }
            if (itemTest is AtomicTypeTest or NumericTest)
            {
                value = Sequences.Atomize(value);
                XPathItem[]? converted = null;
                for (int i = 0; i < value.Count; i++)
                {
                    var a = (AtomicValue)value[i];
                    AtomicValue c = Convert(a, itemTest);
                    if (!ReferenceEquals(c, a))
                    {
                        converted ??= value.ToArray();
                        converted[i] = c;
                    }
                }
                if (converted != null)
                {
                    value = converted;
                }
            }
            if (!expected.Matches(value) && !(itemTest is FunctionItemTest { Parameters: not null } ft && FunctionCoercible(value, expected, ft)))
            {
                throw new XdmException(errorCode, $"{(what != null ? what() : $"Argument {argIndex + 1} of {function}")} must be {expected}, but is {Describe(value)}");
            }
            return value;
        }

        // Function coercion (XPath 3.1 §3.1.5.3): a function item of the right arity is accepted for a typed function
        // test; its arguments and result are converted when it is called.
        private static bool FunctionCoercible(IList<XPathItem> value, SequenceTypeTest expected, FunctionItemTest test)
        {
            if (value.Count == 0 ? !expected.AllowsEmpty : value.Count > 1 && !expected.AllowsMany)
            {
                return false;
            }
            return value.All(v => v is FunctionItem f and not MapItem and not ArrayItem && f.Arity == test.Parameters!.Count || test.Matches(v));
        }

        private static AtomicValue Convert(AtomicValue a, ItemTest test)
        {
            AtomicType target = test is AtomicTypeTest t ? t.Type : AtomicTypes.Double;
            if (a.Type.DerivesFrom(target) || (test is NumericTest && a.Type.IsNumeric))
            {
                return a;
            }
            if (a.Type.Primitive == Primitive.UntypedAtomic)
            {
                return Casting.Cast(a, target);
            }
            // Numeric promotion: decimal (incl. integer) → float → double; and xs:anyURI → xs:string.
            if (a.Type.IsNumeric && target.IsNumeric)
            {
                bool promotes = target.Primitive switch
                {
                    Primitive.Double => true,
                    Primitive.Float => a.Type.Primitive is Primitive.Decimal or Primitive.Integer,
                    _ => false,
                };
                if (promotes)
                {
                    return Casting.Cast(a, target);
                }
            }
            if (a.Type.Primitive == Primitive.AnyUri && target.Primitive == Primitive.String)
            {
                return Casting.Cast(a, AtomicTypes.String);
            }
            return a;
        }

        public static string Describe(IList<XPathItem> value) => value.Count switch
        {
            0 => "an empty sequence",
            1 => value[0] switch
            {
                AtomicValue a => $"{a.Type.Name} '{a.Value}'",
                FunctionItem f => f.Describe(),
                var n => $"a {((XPathNavigator)n).NodeType} node",
            },
            _ => $"a sequence of {value.Count} items",
        };
    }

    /// <summary>The registry of built-in functions, keyed by expanded name and arity.</summary>
    internal static partial class FunctionLibrary
    {
        private static readonly List<FunctionDefinition> s_byId = new();
        private static readonly Dictionary<(string Ns, string Local), List<FunctionDefinition>> s_byName = new();

        static FunctionLibrary()
        {
            RegisterCore();
        }

        public static FunctionDefinition ById(int id) => s_byId[id];

        /// <summary>The function with this name and arity, or null. Variadic functions accept any arity from their declared one.</summary>
        public static FunctionDefinition? Find(string namespaceUri, string localName, int arity)
        {
            if (!s_byName.TryGetValue((namespaceUri, localName), out List<FunctionDefinition>? overloads))
            {
                return null;
            }
            return overloads.FirstOrDefault(f => f.Arity == arity) ?? overloads.FirstOrDefault(f => f.IsVariadic && arity >= f.Arity);
        }

        public static bool Exists(string namespaceUri, string localName) => s_byName.ContainsKey((namespaceUri, localName));

        /// <summary>
        /// Registers a function from its F&amp;O signature, e.g.
        /// <c>fn:substring($sourceString as xs:string?, $start as xs:double) as xs:string</c>.
        /// A trailing <c>...</c> in the parameter list marks a variadic function.
        /// </summary>
        private static void Define(string signature, FunctionBody body)
        {
            int open = signature.IndexOf('(');
            int close = open;
            for (int depth = 0; ; close++)
            {
                if (signature[close] == '(') depth++;
                else if (signature[close] == ')' && --depth == 0) break;
            }
            string name = signature[..open].Trim();
            string paramText = signature[(open + 1)..close];
            string returnText = signature[(close + 1)..].Trim();
            bool variadic = paramText.TrimEnd().EndsWith("...", System.StringComparison.Ordinal);
            if (variadic)
            {
                paramText = paramText.TrimEnd()[..^3].TrimEnd().TrimEnd(',');
            }

            var parameters = new List<(string, SequenceTypeTest)>();
            foreach (string p in SplitTopLevel(paramText))
            {
                int asPos = p.IndexOf(" as ", System.StringComparison.Ordinal);
                parameters.Add((p[..asPos].Trim().TrimStart('$'), ResolveType(p[(asPos + 4)..].Trim())));
            }
            if (!returnText.StartsWith("as ", System.StringComparison.Ordinal))
            {
                throw new System.InvalidOperationException("Bad signature: " + signature);
            }
            SequenceTypeTest returnType = ResolveType(returnText[3..].Trim());

            int colon = name.IndexOf(':');
            string ns = name[..colon] switch
            {
                "fn" => QName.Ns.Fn,
                "math" => QName.Ns.Math,
                "map" => QName.Ns.Map,
                "array" => QName.Ns.Array,
                "xs" => QName.Ns.Xs,
                var other => throw new System.InvalidOperationException("Unknown prefix " + other),
            };
            var def = new FunctionDefinition(s_byId.Count, new QName(ns, name[(colon + 1)..], name[..colon]), parameters, returnType, body, variadic);
            s_byId.Add(def);
            if (!s_byName.TryGetValue((ns, def.Name.LocalName), out List<FunctionDefinition>? list))
            {
                s_byName[(ns, def.Name.LocalName)] = list = new List<FunctionDefinition>();
            }
            list.Add(def);
        }

        private static IEnumerable<string> SplitTopLevel(string text)
        {
            int depth = 0, start = 0;
            for (int i = 0; i < text.Length; i++)
            {
                switch (text[i])
                {
                    case '(': depth++; break;
                    case ')': depth--; break;
                    case ',' when depth == 0:
                        yield return text[start..i];
                        start = i + 1;
                        break;
                }
            }
            if (text.Trim().Length > 0)
            {
                yield return text[start..];
            }
        }

        private static readonly StaticTypeResolver s_signatureTypes = new(prefix => prefix switch
        {
            "xs" => QName.Ns.Xs,
            "fn" => QName.Ns.Fn,
            _ => null,
        });

        private static SequenceTypeTest ResolveType(string text) => s_signatureTypes.Resolve(Parser.ParseSequenceType(text));
    }
}
