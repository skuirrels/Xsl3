// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Xml;
using System.Xml.XPath;

namespace Xsl3.Runtime
{
    public sealed partial class XsltLibrary
    {
        // Xsl3 (P4): per xsl:number instruction, the node it numbered last and its number.
        private (XPathNavigator? Node, int Number)[]? _siblingNumbers;

        /// <summary>
        /// xsl:number with level="single" and the default count pattern, without from: 1 + the number of
        /// preceding siblings of the same sort (kind and name) as the node. Numbering the siblings of a parent in
        /// document order, as apply-templates does, counts on from the node numbered last instead of from the first
        /// sibling, so it is linear rather than quadratic.
        /// </summary>
        public int X31SiblingNumber(XPathNavigator node, int site)
        {
            if (node.NodeType is XPathNodeType.Root or XPathNodeType.Attribute or XPathNodeType.Namespace)
            {
                return 1;
            }
            _siblingNumbers ??= new (XPathNavigator?, int)[System.Math.Max(4, site + 1)];
            if (site >= _siblingNumbers.Length)
            {
                System.Array.Resize(ref _siblingNumbers, System.Math.Max(site + 1, _siblingNumbers.Length * 2));
            }
            var (last, lastNumber) = _siblingNumbers[site];
            int number = -1;
            if (last != null && ReferenceEquals(last.NameTable, node.NameTable) && IsSameNodeSort(last, node) &&
                last.ComparePosition(node) == XmlNodeOrder.Before && SameParent(last, node))
            {
                XPathNavigator sibling = last.Clone();
                int n = lastNumber;
                while (sibling.MoveToNext())
                {
                    if (IsSameNodeSort(sibling, node))
                    {
                        n++;
                    }
                    if (sibling.IsSamePosition(node))
                    {
                        number = n;
                        break;
                    }
                }
            }
            if (number < 0)
            {
                XPathNavigator sibling = node.Clone();
                number = 1;
                while (sibling.MoveToPrevious())
                {
                    if (IsSameNodeSort(sibling, node))
                    {
                        number++;
                    }
                }
            }
            _siblingNumbers[site] = (node.Clone(), number);
            return number;
        }

        private static bool SameParent(XPathNavigator a, XPathNavigator b)
        {
            XPathNavigator pa = a.Clone(), pb = b.Clone();
            return pa.MoveToParent() && pb.MoveToParent() && pa.IsSamePosition(pb);
        }
    }
}
