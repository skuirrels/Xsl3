// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.Xml.XPath;
using Xsl3.Xdm;

namespace Xsl3.XPath31.Functions
{
    /// <summary>
    /// A function item whose body is library code (the <c>next</c> and <c>permute</c> functions of
    /// fn:random-number-generator): called by the run-time library, like a map or a built-in function.
    /// </summary>
    internal sealed class NativeFunction(SequenceTypeTest[] parameters, SequenceTypeTest returnType, System.Func<IList<XPathItem>[], IList<XPathItem>> body, string description)
        : FunctionItem
    {
        internal readonly record struct RandomFunctionState(ulong Value, bool Permute);

        // Only the generator factory can associate serializable state with an implementation.
        internal RandomFunctionState? RandomState { get; private init; }

        internal static NativeFunction Random(ulong state, bool permute) => permute
            ? new NativeFunction(new[] { SequenceTypeTest.AnySequence }, SequenceTypeTest.AnySequence,
                a => FunctionLibrary.Permute(state, a[0]), "the permute function of a random number generator")
                { RandomState = new(state, true) }
            : new NativeFunction(System.Array.Empty<SequenceTypeTest>(), FunctionLibrary.GeneratorType,
                _ => Sequences.One(FunctionLibrary.RandomGenerator(state)), "the next function of a random number generator")
                { RandomState = new(state, false) };

        public override int Arity => parameters.Length;

        public override SequenceTypeTest ParameterType(int index) => parameters[index];

        public override SequenceTypeTest ReturnType => returnType;

        public IList<XPathItem> Invoke(IList<XPathItem>[] arguments) => body(arguments);

        public override string Describe() => description;
    }

    internal static partial class FunctionLibrary
    {
        private static void RegisterRandom()
        {
            // F&O 3.1 §17.3: without a seed (or with an empty one), the same generator throughout an evaluation.
            Define("fn:random-number-generator() as map(xs:string, item())", (c, a) => One(RandomGenerator(Seed(c, null))));
            Define("fn:random-number-generator($seed as xs:anyAtomicType?) as map(xs:string, item())", (c, a) => One(RandomGenerator(Seed(c, Opt(a[0])))));
        }

        private static readonly ulong s_processSeed = (ulong)System.Random.Shared.NextInt64();

        private static ulong Seed(FunctionContext c, AtomicValue? seed)
        {
            // A seed's value and type, hashed stably (string.GetHashCode is randomized per process).
            if (seed == null)
            {
                // One random seed per transformation (per process for XPath evaluated outside one).
                return c.Runtime?.RandomSeed ?? s_processSeed;
            }
            string text = seed.Type.Name + "\u0001" + seed.Value;
            ulong hash = 14695981039346656037UL; // FNV-1a
            foreach (char ch in text)
            {
                hash = (hash ^ ch) * 1099511628211UL;
            }
            return hash;
        }

        // SplitMix64: a small generator whose state is one number, so a generator is a value.
        private static ulong Next(ref ulong state)
        {
            ulong z = state += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        // Resolved on first use: the signature types it needs are initialized in another part of this class.
        private static SequenceTypeTest? s_generatorType;
        internal static SequenceTypeTest GeneratorType => s_generatorType ??= ResolveType("map(xs:string, item())");

        /// <summary>The map of a generator in state <paramref name="state"/>: number, next() and permute($arg).</summary>
        internal static MapItem RandomGenerator(ulong state)
        {
            ulong numberState = state;
            double number = (Next(ref numberState) >> 11) * (1.0 / (1UL << 53));
            var next = NativeFunction.Random(numberState, false);
            var permute = NativeFunction.Random(state, true);
            return MapItem.Empty
                .Put(AtomicValue.OfString("number"), One(AtomicValue.OfDouble(number)))
                .Put(AtomicValue.OfString("next"), One(next))
                .Put(AtomicValue.OfString("permute"), One(permute));
        }

        internal static IList<XPathItem> Permute(ulong state, IList<XPathItem> input)
        {
            // Fisher–Yates, from this generator's state.
            var items = new List<XPathItem>(input);
            ulong s = state ^ 0xD1B54A32D192ED03UL;
            for (int i = items.Count - 1; i > 0; i--)
            {
                int j = (int)(Next(ref s) % (ulong)(i + 1));
                (items[i], items[j]) = (items[j], items[i]);
            }
            return items;
        }
    }
}
