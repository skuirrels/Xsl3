// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.Xml.XPath;
using Xsl3.Xdm;

namespace Xsl3.Runtime
{
    /// <summary>
    /// Xsl3 (perf, remaining-scope 1.5): automatic indexing of paths like <c>/orders/product[@sku = $v]</c>
    /// (XPath31Compiler.IndexedPath). Per transformation, per path and per document: the first evaluation tests every
    /// node as written, the second builds an index from key strings to nodes, and later ones look the values up.
    /// </summary>
    public sealed partial class XsltLibrary
    {
        private sealed class PathIndex
        {
            public required XPathNavigator Root;
            public int Uses;
            public Dictionary<string, List<(int Position, XPathNavigator Node)>>? Keys;
            public int Count;
        }

        // The indexes of each path, one per document it was evaluated in.
        private Dictionary<int, List<PathIndex>>? _pathIndexes;

        // The index X31IndexAdd fills (set by X31IndexState when it asks for one to be built; the candidates' keys are
        // simple steps, so no other index is built meanwhile).
        private PathIndex? _building;

        /// <summary>
        /// What the path at <paramref name="site"/> does now in the document of <paramref name="root"/>, given the values
        /// it compares with: 0 to be evaluated as written, 1 to look the values up, 2 to build the index first.
        /// </summary>
        public double X31IndexState(double site, XPathNavigator context, IList<XPathItem> values)
        {
            if (Root(context) is not { NodeType: XPathNodeType.Root })
            {
                return 0; // the path as written raises XPDY0050
            }
            for (int i = 0; i < values.Count; i++)
            {
                // Strings and nodes compare with the keys (untyped) as strings; anything else as its own type.
                if (values[i] is not (XPathNavigator or StringValue { Type.Primitive: Primitive.String or Primitive.UntypedAtomic }))
                {
                    return 0;
                }
            }
            PathIndex index = Index(site, context);
            if (index.Keys != null)
            {
                return 1;
            }
            // Evaluated once as written: an index pays for itself from the second evaluation on.
            if (++index.Uses < 2)
            {
                return 0;
            }
            index.Keys = new Dictionary<string, List<(int, XPathNavigator)>>(System.StringComparer.Ordinal);
            _building = index;
            return 2;
        }

        // The index of the path at the site for the document of the root.
        private static XPathNavigator Root(XPathNavigator node)
        {
            XPathNavigator root = node.Clone();
            root.MoveToRoot();
            return root;
        }

        private PathIndex Index(double site, XPathNavigator context)
        {
            XPathNavigator document = Root(context);
            _pathIndexes ??= new Dictionary<int, List<PathIndex>>();
            if (!_pathIndexes.TryGetValue((int)site, out List<PathIndex>? indexes))
            {
                _pathIndexes[(int)site] = indexes = new List<PathIndex>(1);
            }
            foreach (PathIndex candidate in indexes)
            {
                if (candidate.Root.IsSamePosition(document))
                {
                    return candidate;
                }
            }
            var index = new PathIndex { Root = document };
            indexes.Add(index);
            return index;
        }

        /// <summary>
        /// A candidate of the path: with <paramref name="state"/> 2, added to the index being built (and not kept);
        /// otherwise kept when one of its keys equals one of the values, as the predicate as written says.
        /// </summary>
        public bool X31IndexFilter(double state, XPathNavigator node, IList<XPathItem> keys, IList<XPathItem> values, double sc)
        {
            if (state == 2)
            {
                X31IndexAdd(node, keys);
                return false;
            }
            return X31GeneralCompare(keys, values, (double)ValueComparisonOp.Eq, sc);
        }

        /// <summary>Adds a candidate node under each of its key values to the index being built.</summary>
        public IList<XPathItem> X31IndexAdd(XPathNavigator node, IList<XPathItem> keys)
        {
            PathIndex index = _building!;
            XPathNavigator? kept = null;
            int position = index.Count++;
            for (int i = 0; i < keys.Count; i++)
            {
                string key = keys[i].Value;
                if (!index.Keys!.TryGetValue(key, out var nodes))
                {
                    index.Keys[key] = nodes = new List<(int, XPathNavigator)>(1);
                }
                if (nodes.Count == 0 || nodes[^1].Position != position)
                {
                    nodes.Add((position, kept ??= node.Clone()));
                }
            }
            return Sequences.Empty;
        }

        /// <summary>The indexed nodes whose key equals one of the values, in document order.</summary>
        public IList<XPathNavigator> X31IndexGet(double site, XPathNavigator context, IList<XPathItem> values)
        {
            PathIndex index = Index(site, context);
            if (values.Count == 1)
            {
                if (!index.Keys!.TryGetValue(values[0].Value, out var nodes))
                {
                    return System.Array.Empty<XPathNavigator>();
                }
                var result = new XPathNavigator[nodes.Count];
                for (int i = 0; i < result.Length; i++)
                {
                    result[i] = nodes[i].Node;
                }
                return result;
            }
            var found = new SortedDictionary<int, XPathNavigator>();
            for (int v = 0; v < values.Count; v++)
            {
                if (index.Keys!.TryGetValue(values[v].Value, out var nodes))
                {
                    foreach (var (position, node) in nodes)
                    {
                        found[position] = node;
                    }
                }
            }
            return new List<XPathNavigator>(found.Values);
        }
    }
}
