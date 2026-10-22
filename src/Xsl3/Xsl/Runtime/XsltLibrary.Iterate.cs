// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Xml.Schema;
using System.Xml.XPath;
using Xsl3.Xdm;

namespace Xsl3.Runtime
{
    /// <summary>
    /// The control item that ends the value of the body of xsl:iterate after xsl:next-iteration (with the
    /// parameters' new values) or xsl:break; also the parameters' initial values. It never escapes X31Iterate.
    /// </summary>
    internal sealed class IterationControl : XPathItem
    {
        public IterationControl(bool isBreak) => IsBreak = isBreak;

        public bool IsBreak { get; }

        public List<IList<XPathItem>> Values { get; } = new();

        public override bool IsNode => false;
        public override XmlSchemaType? XmlType => null;
        public override string Value => throw new InvalidOperationException("An xsl:iterate control item has no value");
        public override object TypedValue => Value;
        public override Type ValueType => typeof(IterationControl);
        public override bool ValueAsBoolean => throw new InvalidCastException();
        public override DateTime ValueAsDateTime => throw new InvalidCastException();
        public override double ValueAsDouble => throw new InvalidCastException();
        public override int ValueAsInt => throw new InvalidCastException();
        public override long ValueAsLong => throw new InvalidCastException();
        public override object ValueAs(Type returnType, System.Xml.IXmlNamespaceResolver? nsResolver) => throw new InvalidCastException();
    }

    public sealed partial class XsltLibrary
    {
        public XPathItem X31IterationNew(bool isBreak) => new IterationControl(isBreak);

        public XPathItem X31IterationAdd(XPathItem control, IList<XPathItem> value)
        {
            ((IterationControl)control).Values.Add(Copy(value));
            return control;
        }

        /// <summary>
        /// xsl:iterate (XSLT 3.0 §7.2): calls the body for each item, with the item, its position, the size and
        /// the parameters' values; a body ending in xsl:next-iteration passes new values on, one ending in
        /// xsl:break ends the iteration (without xsl:on-completion).
        /// </summary>
        public IList<XPathItem> X31Iterate(IList<XPathItem> items, IList<XPathItem> body, IList<XPathItem> completion, IList<XPathItem> initial)
        {
            List<IList<XPathItem>> values = ((IterationControl)initial[0]).Values;
            var bodyFunction = (FunctionItem)body[0];
            var output = new List<XPathItem>();
            IList<XPathItem> size = Sequences.One(AtomicValue.OfInteger(items.Count));
            for (int i = 0; i < items.Count; i++)
            {
                var arguments = new IList<XPathItem>[3 + values.Count];
                // (The lists are the call's own: the item cloned as X31CallArgument would, the parameters' values copied
                // by X31IterationAdd.)
                arguments[0] = Sequences.One(Unshare(items[i]));
                arguments[1] = Sequences.One(AtomicValue.OfInteger(i + 1));
                arguments[2] = size;
                values.CopyTo(arguments, 3);
                IList<XPathItem> result = CallFunction(bodyFunction, arguments, owned: true);
                // The control item is last (xsl:next-iteration and xsl:break are in tail position, XTSE3120).
                int count = result.Count;
                if (count > 0 && result[count - 1] is IterationControl control)
                {
                    for (int k = 0; k < count - 1; k++)
                    {
                        output.Add(result[k] is XPathNavigator n ? n.Clone() : result[k]);
                    }
                    if (control.IsBreak)
                    {
                        return output;
                    }
                    values = control.Values;
                }
                else
                {
                    output.AddRange(Copy(result));
                }
            }
            if (completion.Count != 0)
            {
                output.AddRange(Copy(CallFunction((FunctionItem)completion[0], values.ToArray())));
            }
            return output;
        }
    }
}
