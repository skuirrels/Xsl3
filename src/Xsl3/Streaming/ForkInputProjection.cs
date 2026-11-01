// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml.XPath;
using Xsl3.Xdm;

namespace Xsl3.Streaming;

// A compiler-proven union of relative child paths. Structural nodes keep their
// headers; Whole nodes additionally keep every descendant event.
internal sealed class ForkInputProjection
{
    private struct Shape(string local, string ns, bool whole)
    {
        internal readonly string Local = local, Namespace = ns;
        internal readonly bool Whole = whole;
        internal int FirstChild, ChildCount;
    }

    private readonly Shape[] _shapes;
    private readonly int[] _children;

    internal bool AllowCompactProducer { get; }

    internal ForkInputProjection(string?[] data, bool allowCompactProducer = false)
    {
        AllowCompactProducer = allowCompactProducer;
        if (data.Length == 0 || data.Length % 4 != 0) throw new ArgumentException("Invalid fork projection", nameof(data));
        _shapes = new Shape[data.Length / 4];
        var parents = new int[_shapes.Length];
        for (int i = 0; i < _shapes.Length; i++)
        {
            int offset = i * 4;
            if (!int.TryParse(data[offset], NumberStyles.AllowLeadingSign, InvariantNumberFormat.Info, out int parent)
                || parent >= i || parent < -1 || (i == 0) != (parent == -1)
                || data[offset + 1] == null || data[offset + 2] == null || data[offset + 3] is not ("0" or "1"))
                throw new ArgumentException("Invalid fork projection", nameof(data));
            _shapes[i] = new(data[offset + 1]!, data[offset + 2]!, data[offset + 3] == "1");
            parents[i] = parent;
            if (parent >= 0) _shapes[parent].ChildCount++;
        }
        _children = new int[_shapes.Length - 1];
        int next = 0;
        for (int i = 0; i < _shapes.Length; i++)
        {
            _shapes[i].FirstChild = next;
            next += _shapes[i].ChildCount;
        }
        var written = new int[_shapes.Length];
        for (int i = 1; i < _shapes.Length; i++)
            _children[_shapes[parents[i]].FirstChild + written[parents[i]]++] = i;
        var order = new ChildOrder(_shapes);
        foreach (Shape shape in _shapes)
        {
            Array.Sort(_children, shape.FirstChild, shape.ChildCount, order);
            int end = shape.FirstChild + shape.ChildCount;
            for (int i = shape.FirstChild + 1; i < end; i++)
                if (order.Compare(_children[i - 1], _children[i]) == 0)
                    throw new ArgumentException("Invalid fork projection", nameof(data));
        }
    }

    private sealed class ChildOrder(Shape[] shapes) : IComparer<int>
    {
        public int Compare(int x, int y) => CompareName(shapes[x], shapes[y].Local, shapes[y].Namespace);
    }

    private static int CompareName(Shape shape, string local, string ns)
    {
        int result = string.CompareOrdinal(shape.Local, local);
        return result != 0 ? result : string.CompareOrdinal(shape.Namespace, ns);
    }

    internal Matcher NewMatcher(StreamedDocument.Node boundary) => new(this, boundary);

    internal sealed class Matcher
    {
        internal readonly bool AllowCompactProducer;
        private readonly Shape[] _shapes;
        private readonly int[] _children;
        private readonly List<int> _path = new() { 0 };
        private readonly StreamedDocument.Node _boundary;

        internal Matcher(ForkInputProjection projection, StreamedDocument.Node boundary)
        {
            AllowCompactProducer = projection.AllowCompactProducer;
            _shapes = projection._shapes;
            _children = projection._children;
            _boundary = boundary;
        }

        internal bool Keep(StreamedDocument.Node node, bool end)
        {
            if (node == _boundary) return true;
            return Keep(node.Depth, node.Kind, node.LocalName, node.Namespace, end);
        }

        internal bool Keep(int sourceDepth, XPathNodeType kind, string local, string ns, bool end)
        {
            int depth = sourceDepth - _boundary.Depth;
            if (depth < 1) throw new InvalidOperationException("A fork projection left its boundary");
            if (end) return _path[depth] >= 0;
            int parent = _path[depth - 1];
            bool whole = parent >= 0 && _shapes[parent].Whole;
            if (kind != XPathNodeType.Element) return whole;
            int shape = -1;
            if (whole) shape = parent;
            else if (parent >= 0)
            {
                int first = _shapes[parent].FirstChild, last = first + _shapes[parent].ChildCount - 1;
                while (first <= last)
                {
                    int middle = first + (last - first) / 2, child = _children[middle];
                    int comparison = CompareName(_shapes[child], local, ns);
                    if (comparison == 0) { shape = child; break; }
                    if (comparison < 0) first = middle + 1;
                    else last = middle - 1;
                }
            }
            if (_path.Count == depth) _path.Add(shape);
            else _path[depth] = shape;
            return shape >= 0;
        }
    }
}
