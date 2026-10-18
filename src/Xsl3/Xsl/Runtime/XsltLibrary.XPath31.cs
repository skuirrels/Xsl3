// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.Numerics;
using System.Xml.XPath;
using Xsl3.XPath31;
using Xsl3.XPath31.Functions;
using Xsl3.Xdm;

namespace Xsl3.Runtime
{
    /// <summary>
    /// The run-time entry points of XPath 3.1 code (docs/plan/p1-xdm-xpath.md). Compiled QIL calls these through
    /// XsltInvokeEarlyBound; they are on XsltLibrary because the IL generator routes instance calls in the empty
    /// namespace to the per-evaluation XsltLibrary object, which gives them the dynamic context.
    /// Values travel as XPathItem / IList&lt;XPathItem&gt; holding Xdm.AtomicValue objects or navigators;
    /// numeric selectors (operators, constant ids) are passed as double, the only numeric type the IL
    /// generator passes to internal library methods.
    /// </summary>
    public sealed partial class XsltLibrary
    {
        private FunctionContext? _x31Context;

        internal FunctionContext X31Context => _x31Context ??= new FunctionContext(
            ImplicitTimezoneOverride ?? FunctionContext.LocalTimezoneMinutes,
            (a, b) => _runtime.ComparePosition(a, b))
        {
            Runtime = _runtime,
            StaticBaseUri = _runtime.ExternalContext.GetParameter(XPath31Executable.StaticBaseUriParam, XPath31Executable.ControlNamespace) as string,
            DefaultCollation = _runtime.ExternalContext.GetParameter(XPath31Executable.DefaultCollationParam, XPath31Executable.ControlNamespace) is string collation
                ? Collation.Resolve(collation) : Collation.Codepoint,
            DecimalFormats = _runtime.ExternalContext.GetParameter(XPath31Executable.DecimalFormatsParam, XPath31Executable.ControlNamespace) as DecimalFormatTable ?? DecimalFormatTable.Empty,
        };

        /// <summary>Set by the caller to fix the implicit timezone (minutes); otherwise the local timezone applies.</summary>
        internal int? ImplicitTimezoneOverride => _runtime.ExternalContext.GetParameter(XPath31Executable.ImplicitTimezoneParam, XPath31Executable.ControlNamespace) as int?;

        private int Tz => X31Context.ImplicitTimezoneMinutes;

        private static IList<XPathItem> One(XPathItem item) => new[] { item };

        private static IList<XPathItem> Bool(bool b) => One(AtomicValue.OfBoolean(b));

        // ------------------------------------------------------------ values

        public XPathItem X31Constant(double id) => (XPathItem)ConstantPool.Get((int)id);

        public XPathItem X31BooleanItem(bool value) => AtomicValue.OfBoolean(value);

        public XPathItem X31DoubleAsInteger(double value) => AtomicValue.OfInteger(new BigInteger(value));

        /// <summary>An external variable or the context item, supplied through the argument list.</summary>
        public IList<XPathItem> X31Variable(string localName, string namespaceUri)
        {
            object? value = _runtime.ExternalContext.GetParameter(localName, namespaceUri);
            return value switch
            {
                null => throw new XdmException("XPDY0002", $"No value supplied for ${localName}"),
                IList<XPathItem> items => items,
                XPathItem item => One(item),
                _ => throw new XdmException("XPTY0004", $"Unsupported value for ${localName}: {value.GetType()}"),
            };
        }

        /// <summary>The context item at the top level, or XPDY0002 when it is absent.</summary>
        public XPathItem X31ContextItem()
        {
            object? value = _runtime.ExternalContext.GetParameter(XPath31Executable.ContextItemParam, XPath31Executable.ControlNamespace);
            return value as XPathItem ?? throw new XdmException("XPDY0002", "The context item is absent");
        }

        /// <summary>position() and last() at the top level: 1, or XPDY0002 when there is no context item.</summary>
        public double X31TopLevelFocus()
        {
            X31ContextItem();
            return 1;
        }

        public IList<XPathItem> X31Raise(string code, string message) => throw new XdmException(code, message);

        // ------------------------------------------------------------ booleans

        public bool X31Ebv(IList<XPathItem> seq) => Sequences.EffectiveBooleanValue(seq);

        /// <summary>A predicate: a numeric singleton selects by position, anything else by effective boolean value.</summary>
        public bool X31Predicate(IList<XPathItem> value, double position)
        {
            if (value.Count == 1 && value[0] is AtomicValue a && a.Type.IsNumeric)
            {
                return Numeric.ToDouble(a) == position;
            }
            return Sequences.EffectiveBooleanValue(value);
        }

        // ------------------------------------------------------------ comparisons

        public IList<XPathItem> X31ValueCompare(IList<XPathItem> left, IList<XPathItem> right, double op)
        {
            AtomicValue? a = Sequences.AtomizeOptional(left, "an operand of a value comparison");
            AtomicValue? b = Sequences.AtomizeOptional(right, "an operand of a value comparison");
            if (a == null || b == null)
            {
                return Sequences.Empty;
            }
            return Bool(Comparison.Compare(a, b, (ValueComparisonOp)(int)op, Tz, X31Context.DefaultCollation));
        }

        /// <summary>A general comparison (XPath 3.1 §3.7.2): true if any pair of atomised items compares true.</summary>
        public bool X31GeneralCompare(IList<XPathItem> left, IList<XPathItem> right, double op)
        {
            IList<XPathItem> a = Sequences.Atomize(left), b = Sequences.Atomize(right);
            var opv = (ValueComparisonOp)(int)op;
            foreach (XPathItem x in a)
            {
                foreach (XPathItem y in b)
                {
                    if (GeneralPair((AtomicValue)x, (AtomicValue)y, opv))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        // xs:untypedAtomic is cast to the other operand's type: xs:double against a numeric, xs:string against
        // xs:untypedAtomic or xs:string, else the other type itself.
        private bool GeneralPair(AtomicValue x, AtomicValue y, ValueComparisonOp op)
        {
            bool ux = x.Type.Primitive == Primitive.UntypedAtomic, uy = y.Type.Primitive == Primitive.UntypedAtomic;
            if (ux && uy)
            {
                x = Casting.Cast(x, AtomicTypes.String);
                y = Casting.Cast(y, AtomicTypes.String);
            }
            else if (ux)
            {
                x = Casting.Cast(x, y.Type.IsNumeric ? AtomicTypes.Double : y.Type.Primitive is Primitive.String or Primitive.AnyUri ? AtomicTypes.String : AtomicTypes.Of(y.Type.Primitive));
            }
            else if (uy)
            {
                y = Casting.Cast(y, x.Type.IsNumeric ? AtomicTypes.Double : x.Type.Primitive is Primitive.String or Primitive.AnyUri ? AtomicTypes.String : AtomicTypes.Of(x.Type.Primitive));
            }
            return Comparison.Compare(x, y, op, Tz, X31Context.DefaultCollation);
        }

        /// <summary><c>is</c>, <c>&lt;&lt;</c>, <c>&gt;&gt;</c> (op 0, 1, 2).</summary>
        public IList<XPathItem> X31NodeCompare(IList<XPathItem> left, IList<XPathItem> right, double op)
        {
            if (left.Count == 0 || right.Count == 0)
            {
                return Sequences.Empty;
            }
            if (left.Count > 1 || right.Count > 1 || left[0] is not XPathNavigator a || right[0] is not XPathNavigator b)
            {
                throw Err.XPTY0004("Each operand of a node comparison must be a single node");
            }
            int cmp = _runtime.ComparePosition(a, b);
            return Bool(op switch { 0 => cmp == 0, 1 => cmp < 0, _ => cmp > 0 });
        }

        // ------------------------------------------------------------ arithmetic and strings

        public IList<XPathItem> X31Arithmetic(IList<XPathItem> left, IList<XPathItem> right, double op)
        {
            AtomicValue? a = Sequences.AtomizeOptional(left, "an arithmetic operand");
            AtomicValue? b = Sequences.AtomizeOptional(right, "an arithmetic operand");
            if (a == null || b == null)
            {
                return Sequences.Empty;
            }
            return One(Xdm.Arithmetic.Apply((ArithmeticOp)(int)op, a, b, Tz));
        }

        public IList<XPathItem> X31Unary(IList<XPathItem> operand, bool minus)
        {
            AtomicValue? a = Sequences.AtomizeOptional(operand, "the operand of a unary operator");
            return a == null ? Sequences.Empty : One(Xdm.Arithmetic.Unary(minus, a));
        }

        /// <summary><c>a || b</c>: string concatenation, the empty sequence counting as "".</summary>
        public IList<XPathItem> X31StringConcat(IList<XPathItem> left, IList<XPathItem> right)
        {
            AtomicValue? a = Sequences.AtomizeOptional(left, "an operand of ||");
            AtomicValue? b = Sequences.AtomizeOptional(right, "an operand of ||");
            return One(AtomicValue.OfString((a?.Value ?? "") + (b?.Value ?? "")));
        }

        /// <summary><c>a to b</c>.</summary>
        public IList<XPathItem> X31Range(IList<XPathItem> left, IList<XPathItem> right)
        {
            var start = (IntegerValue?)Coerce(left, AtomicTypes.Integer, "to");
            var end = (IntegerValue?)Coerce(right, AtomicTypes.Integer, "to");
            if (start == null || end == null || start.Integer > end.Integer)
            {
                return Sequences.Empty;
            }
            BigInteger count = end.Integer - start.Integer + 1;
            if (count > 100_000_000)
            {
                throw new XdmException("XPDY0130", $"The range {start.Value} to {end.Value} is too large to materialise");
            }
            var result = new XPathItem[(int)count];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = AtomicValue.OfInteger(start.Integer + i);
            }
            return result;
        }

        private static AtomicValue? Coerce(IList<XPathItem> seq, AtomicType type, string op)
        {
            AtomicValue? a = Sequences.AtomizeOptional(seq, $"an operand of '{op}'");
            if (a == null)
            {
                return null;
            }
            if (a.Type.Primitive == Primitive.UntypedAtomic)
            {
                return Casting.Cast(a, type);
            }
            if (!a.Type.DerivesFrom(type))
            {
                throw Err.XPTY0004($"An operand of '{op}' must be {type.Name}, not {a.Type.Name}");
            }
            return a;
        }

        // ------------------------------------------------------------ nodes and paths

        /// <summary>Requires every item to be a node, raising <paramref name="code"/> (XPTY0019, XPTY0004, …) otherwise.</summary>
        public IList<XPathNavigator> X31Nodes(IList<XPathItem> seq, string code)
        {
            var nodes = new XPathNavigator[seq.Count];
            for (int i = 0; i < nodes.Length; i++)
            {
                nodes[i] = seq[i] as XPathNavigator ?? throw new XdmException(code, "A node was expected, but an atomic value was found");
            }
            return new XmlQueryNodeSequence(nodes, nodes.Length); // the IL generator treats node sequences as XmlQueryNodeSequence
        }

        /// <summary>The context item of an axis step, which must be a node (XPTY0020).</summary>
        public XPathNavigator X31Node(XPathItem item) =>
            item as XPathNavigator ?? throw new XdmException("XPTY0020", "An axis step needs a node as its context item, but it is an atomic value");

        /// <summary>
        /// The result of E1/E2 when E2 isn't statically known to return nodes: nodes are put in document order
        /// without duplicates, atomic values are returned as they are, and a mix is XPTY0018.
        /// </summary>
        public IList<XPathItem> X31PathResult(IList<XPathItem> seq)
        {
            bool nodes = false, atomics = false;
            foreach (XPathItem item in seq)
            {
                if (item.IsNode) nodes = true; else atomics = true;
            }
            if (nodes && atomics)
            {
                throw new XdmException("XPTY0018", "The last step of a path returned both nodes and atomic values");
            }
            return nodes ? DocumentOrder(seq) : seq;
        }

        private IList<XPathItem> DocumentOrder(IList<XPathItem> seq)
        {
            var list = new List<XPathNavigator>(seq.Count);
            foreach (XPathItem item in seq)
            {
                list.Add((XPathNavigator)item);
            }
            list.Sort((a, b) => _runtime.ComparePosition(a, b));
            var result = new List<XPathItem>(list.Count);
            for (int i = 0; i < list.Count; i++)
            {
                if (i == 0 || _runtime.ComparePosition(list[i - 1], list[i]) != 0)
                {
                    result.Add(list[i]);
                }
            }
            return result;
        }

        /// <summary><c>union</c>, <c>intersect</c>, <c>except</c> (op 0, 1, 2), over nodes only.</summary>
        public IList<XPathItem> X31SetOp(IList<XPathItem> left, IList<XPathItem> right, double op)
        {
            X31Nodes(left, "XPTY0004");
            X31Nodes(right, "XPTY0004");
            if (op == 0)
            {
                var all = new List<XPathItem>(left.Count + right.Count);
                all.AddRange(left);
                all.AddRange(right);
                return DocumentOrder(all);
            }
            IList<XPathItem> l = DocumentOrder(left), r = DocumentOrder(right);
            var result = new List<XPathItem>();
            foreach (XPathItem x in l)
            {
                bool inRight = false;
                foreach (XPathItem y in r)
                {
                    if (_runtime.ComparePosition((XPathNavigator)x, (XPathNavigator)y) == 0)
                    {
                        inRight = true;
                        break;
                    }
                }
                if (inRight == (op == 1))
                {
                    result.Add(x);
                }
            }
            return result;
        }

        // ------------------------------------------------------------ types

        public IList<XPathItem> X31Cast(IList<XPathItem> seq, double targetId, bool allowEmpty)
        {
            var target = (CastTarget)ConstantPool.Get((int)targetId);
            AtomicValue? a = Sequences.AtomizeOptional(seq, "the operand of a cast");
            if (a == null)
            {
                return allowEmpty ? Sequences.Empty : throw Err.XPTY0004($"An empty sequence can't be cast to {target.Name}");
            }
            return target.Apply(a);
        }

        public bool X31Castable(IList<XPathItem> seq, double targetId, bool allowEmpty)
        {
            var target = (CastTarget)ConstantPool.Get((int)targetId);
            IList<XPathItem> atomized = Sequences.Atomize(seq);
            if (atomized.Count == 0)
            {
                return allowEmpty;
            }
            return atomized.Count == 1 && target.IsCastable((AtomicValue)atomized[0]);
        }

        public bool X31InstanceOf(IList<XPathItem> seq, double typeId) => ((SequenceTypeTest)ConstantPool.Get((int)typeId)).Matches(seq);

        public IList<XPathItem> X31Treat(IList<XPathItem> seq, double typeId)
        {
            var type = (SequenceTypeTest)ConstantPool.Get((int)typeId);
            return type.Matches(seq) ? seq : throw new XdmException("XPDY0050", $"The value does not match the type {type} required by 'treat as'");
        }

        // ------------------------------------------------------------ function calls

        public IList<XPathItem> X31Call0(double id) => Call(id);

        public IList<XPathItem> X31Call1(double id, IList<XPathItem> a) => Call(id, a);

        public IList<XPathItem> X31Call2(double id, IList<XPathItem> a, IList<XPathItem> b) => Call(id, a, b);

        public IList<XPathItem> X31Call3(double id, IList<XPathItem> a, IList<XPathItem> b, IList<XPathItem> c) => Call(id, a, b, c);

        public IList<XPathItem> X31Call4(double id, IList<XPathItem> a, IList<XPathItem> b, IList<XPathItem> c, IList<XPathItem> d) => Call(id, a, b, c, d);

        public IList<XPathItem> X31Call5(double id, IList<XPathItem> a, IList<XPathItem> b, IList<XPathItem> c, IList<XPathItem> d, IList<XPathItem> e) => Call(id, a, b, c, d, e);

        private IList<XPathItem> Call(double id, params IList<XPathItem>[] args) => FunctionLibrary.ById((int)id).Invoke(X31Context, args);
    }
}
