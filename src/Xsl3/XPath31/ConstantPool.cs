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
    /// Process-wide and append-only for now, which is simple and thread-safe but never frees entries. P4 moves
    /// constants into the compiled executable's own static data (SPEC §12.1) so they unload with it.
    /// </remarks>
    internal static class ConstantPool
    {
        // Xsl3 (P4): reads are lock-free (compiled code reads constants on every evaluation): an entry is written
        // before its id is returned, and a full array is replaced by a larger copy, never changed in place.
        private static object[] s_entries = new object[256];
        private static int s_count;
        private static readonly object s_lock = new();

        public static int Add(object value)
        {
            lock (s_lock)
            {
                object[] entries = s_entries;
                if (s_count == entries.Length)
                {
                    var larger = new object[entries.Length * 2];
                    System.Array.Copy(entries, larger, entries.Length);
                    entries = larger;
                }
                entries[s_count] = value;
                System.Threading.Volatile.Write(ref s_entries, entries);
                return s_count++;
            }
        }

        public static object Get(int id) => System.Threading.Volatile.Read(ref s_entries)[id];
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
