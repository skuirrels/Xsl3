// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.Xml.XPath;
using Xsl3.Xdm;

namespace Xsl3.XPath31
{
    /// <summary>
    /// Objects that compiled code refers to by number: literal values, cast targets, sequence types.
    /// </summary>
    /// <remarks>
    /// Xsl3 (Sink B, B2; docs/plan/p4-sink-b.md decision 3): each stylesheet compilation has its own table, shared by
    /// every unit compiled for it (the stylesheet, its use-when expressions, the targets of its xsl:evaluate), and
    /// reached at run time through the unit's static data, so the constants go when the stylesheet does. Code
    /// compiled outside a stylesheet (XPath evaluated directly) uses the process-wide table.
    /// </remarks>
    internal static class ConstantPool
    {
        private static readonly ConstantTable s_global = new();

        [System.ThreadStatic]
        private static ConstantTable? t_current;

        /// <summary>The table constants are added to on this thread: the compilation's, if one is open.</summary>
        public static ConstantTable Current => t_current ?? s_global;

        /// <summary>The open compilation's table; null outside one.</summary>
        public static ConstantTable? Compilation => t_current;

        /// <summary>Makes <paramref name="table"/> the current one until the result is disposed.</summary>
        public static Scope Use(ConstantTable? table)
        {
            var scope = new Scope(t_current);
            t_current = table;
            return scope;
        }

        public readonly struct Scope(ConstantTable? previous) : System.IDisposable
        {
            public void Dispose() => t_current = previous;
        }

        /// <summary>The size of the process-wide table (for tests: stylesheets don't add to it).</summary>
        internal static int GlobalCount => s_global.Count;

        public static int Add(object value) => Current.Add(value);

        /// <summary>A constant of the current table (while compiling).</summary>
        public static object Get(int id) => Current.Get(id);

        /// <summary>A constant of a unit's table (at run time), or of the process-wide one.</summary>
        public static object Get(ConstantTable? table, int id) => (table ?? s_global).Get(id);
    }

    /// <summary>A table of constants, appended to while compiling and read lock-free by compiled code.</summary>
    internal sealed class ConstantTable
    {
        // Reads are lock-free (compiled code reads constants on every evaluation): an entry is written before its id
        // is returned, and a full array is replaced by a larger copy, never changed in place.
        private object[] _entries = new object[64];
        private int _count;
        private readonly object _lock = new();

        public int Count => _count;

        public int Add(object value)
        {
            lock (_lock)
            {
                object[] entries = _entries;
                if (_count == entries.Length)
                {
                    var larger = new object[entries.Length * 2];
                    System.Array.Copy(entries, larger, entries.Length);
                    entries = larger;
                }
                entries[_count] = value;
                System.Threading.Volatile.Write(ref _entries, entries);
                return _count++;
            }
        }

        public object Get(int id) => System.Threading.Volatile.Read(ref _entries)[id];
    }

    /// <summary>
    /// The parts of an expression's static context that functions and operators read at run time (XPath 3.1
    /// §2.1.1): compiled code passes its id with every call that needs them.
    /// </summary>
    internal sealed record StaticInfo(string? BaseUri, string DefaultCollation, Functions.DecimalFormatTable DecimalFormats);

    internal enum CastKind
    {
        Atomic,

        /// <summary>xs:numeric, the union of xs:double, xs:float and xs:decimal.</summary>
        Numeric,

        /// <summary>xs:NMTOKENS, xs:IDREFS, xs:ENTITIES: whitespace-separated lists of <see cref="CastTarget.Type"/>.</summary>
        List,
    }

    /// <summary>The target of <c>cast as</c>/<c>castable as</c>, with the namespaces xs:QName casts resolve prefixes against.</summary>
    internal sealed record CastTarget(AtomicType Type, IXmlNamespaceResolver? Namespaces, CastKind Kind = CastKind.Atomic)
    {
        public string Name => Kind switch
        {
            CastKind.Numeric => "xs:numeric",
            CastKind.List => Type.Name + "S",
            _ => Type.Name.ToString(),
        };

        public IList<XPathItem> Apply(AtomicValue value)
        {
            switch (Kind)
            {
                case CastKind.Numeric:
                    // A cast to a union type tries its member types in order: xs:double comes first.
                    return new XPathItem[] { value.Type.IsNumeric ? value : Casting.Cast(value, AtomicTypes.Double) };
                case CastKind.List:
                    if (value.Type.Primitive is not (Primitive.String or Primitive.UntypedAtomic))
                    {
                        throw Err.XPTY0004($"Only a string can be cast to the list type {Name}, not {value.Type.Name}");
                    }
                    string[] tokens = value.Value.Split((char[]?)null, System.StringSplitOptions.RemoveEmptyEntries);
                    if (tokens.Length == 0)
                    {
                        throw new XdmException("FORG0001", $"A value of {Name} must have at least one item");
                    }
                    var items = new XPathItem[tokens.Length];
                    for (int i = 0; i < tokens.Length; i++)
                    {
                        items[i] = Casting.Cast(AtomicValue.OfString(tokens[i]), Type);
                    }
                    return items;
                default:
                    return new XPathItem[] { Casting.Cast(value, Type, Namespaces) };
            }
        }

        public bool IsCastable(AtomicValue value)
        {
            if (Kind == CastKind.Atomic)
            {
                return Casting.IsCastable(value, Type, Namespaces);
            }
            try
            {
                Apply(value);
                return true;
            }
            catch (XdmException)
            {
                return false;
            }
        }
    }
}
