// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.Xml.XPath;
using Xsl3.Xdm;

namespace Xsl3.Runtime
{
    public sealed partial class XsltLibrary
    {
        /// <summary>
        /// The arguments of one call of a memo function (XSLT 3.0 §10.3.2): equal when the function is the same and
        /// each argument has the same items, nodes by identity and atomic values by type and value.
        /// </summary>
        internal sealed class MemoKey(double function) : AtomicValue(AtomicTypes.String)
        {
            public readonly double Function = function;
            public readonly List<IList<XPathItem>> Arguments = new();

            public override string Value => "";

            public override object ClrValue => Value;

            public bool SameAs(MemoKey other)
            {
                if (Function != other.Function || Arguments.Count != other.Arguments.Count)
                {
                    return false;
                }
                for (int i = 0; i < Arguments.Count; i++)
                {
                    IList<XPathItem> a = Arguments[i], b = other.Arguments[i];
                    if (a.Count != b.Count)
                    {
                        return false;
                    }
                    for (int j = 0; j < a.Count; j++)
                    {
                        bool same = (a[j], b[j]) switch
                        {
                            (XPathNavigator m, XPathNavigator n) => m.IsSamePosition(n),
                            (AtomicValue x, AtomicValue y) => x.Type == y.Type && Text(x) == Text(y),
                            var (x, y) => ReferenceEquals(x, y),
                        };
                        if (!same)
                        {
                            return false;
                        }
                    }
                }
                return true;
            }

            // A QName by its expanded name (two prefixes can name one namespace, one prefix two).
            private static string Text(AtomicValue value) => value is QNameValue q ? q.QName.EQName : value.Value;

            public int Hash()
            {
                var hash = new System.HashCode();
                hash.Add(Function);
                foreach (IList<XPathItem> argument in Arguments)
                {
                    hash.Add(argument.Count);
                    foreach (XPathItem item in argument)
                    {
                        hash.Add(item is XPathNavigator node ? node.LocalName : item is AtomicValue atomic ? Text(atomic) : null);
                    }
                }
                return hash.ToHashCode();
            }
        }

        private sealed class MemoKeyComparer : IEqualityComparer<MemoKey>
        {
            public static readonly MemoKeyComparer Instance = new();

            public bool Equals(MemoKey? x, MemoKey? y) => ReferenceEquals(x, y) || x != null && y != null && x.SameAs(y);

            public int GetHashCode(MemoKey key) => key.Hash();
        }

        // The results of memo functions in this transformation.
        private Dictionary<MemoKey, IList<XPathItem>>? _memo;

        public XPathItem X31MemoKey(double function) => new MemoKey(function);

        public XPathItem X31MemoKeyAdd(XPathItem key, IList<XPathItem> argument)
        {
            // Copied: the generated code may reuse the list, and navigators move.
            var items = new XPathItem[argument.Count];
            for (int i = 0; i < items.Length; i++)
            {
                items[i] = argument[i] is XPathNavigator node ? node.Clone() : argument[i];
            }
            ((MemoKey)key).Arguments.Add(items);
            return key;
        }

        public bool X31MemoHas(XPathItem key) => _memo != null && _memo.ContainsKey((MemoKey)key);

        public IList<XPathItem> X31MemoGet(XPathItem key) => _memo![(MemoKey)key];

        public IList<XPathItem> X31MemoPut(XPathItem key, IList<XPathItem> value)
        {
            var items = new XPathItem[value.Count];
            for (int i = 0; i < items.Length; i++)
            {
                items[i] = value[i] is XPathNavigator node ? node.Clone() : value[i];
            }
            (_memo ??= new Dictionary<MemoKey, IList<XPathItem>>(MemoKeyComparer.Instance))[(MemoKey)key] = items;
            return items;
        }
    }
}
