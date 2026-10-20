// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.Linq;
using System.Xml.XPath;
using Xsl3.XPath31;
using Xsl3.XPath31.Functions;
using Xsl3.Xdm;

namespace Xsl3.Runtime
{
    /// <summary>The signature of a compiled function item: its name (if any), parameter and return types.</summary>
    internal sealed record FunctionSignature(QName? Name, SequenceTypeTest[] Parameters, SequenceTypeTest Return);

    /// <summary>
    /// A function item whose body is compiled code: an inline function, a named function reference or a partial
    /// application, lambda-lifted to a QIL function (docs/plan/p3-xslt30.md, decision 3). It holds the lifted
    /// function's id, for the generated dispatcher, and the values it captured.
    /// </summary>
    internal sealed class CompiledFunction : XdmFunction
    {
        public CompiledFunction(int id, FunctionSignature signature, XmlQueryRuntime owner)
        {
            Id = id;
            Signature = signature;
            Owner = owner;
        }

        public int Id { get; }

        /// <summary>
        /// The runtime of the compiled unit the function belongs to: its id means something only to that unit's
        /// dispatcher (an expression of xsl:evaluate is a unit of its own; docs/plan/p5-evaluate.md, decision 5).
        /// </summary>
        public XmlQueryRuntime Owner { get; }

        public FunctionSignature Signature { get; }

        public List<IList<XPathItem>> Captured { get; } = new();

        public override int Arity => Signature.Parameters.Length;

        public override QName? Name => Signature.Name;

        public override SequenceTypeTest ParameterType(int index) => Signature.Parameters[index];

        public override SequenceTypeTest ReturnType => Signature.Return;

        public override string Describe() => Name is QName n ? $"function {n.EQName}#{Arity}" : $"an anonymous function of arity {Arity}";
    }

    /// <summary>A function item for a built-in function found at run time (fn:function-lookup with a computed name).</summary>
    internal sealed class LibraryFunction : XdmFunction
    {
        public LibraryFunction(FunctionDefinition definition, int arity, FunctionContext context)
        {
            Definition = definition;
            Arity = arity;
            Context = context;
        }

        public FunctionDefinition Definition { get; }

        public FunctionContext Context { get; }

        public override int Arity { get; }

        public override QName? Name => Definition.Name;

        public override SequenceTypeTest ParameterType(int index) => Definition.ParameterType(index);

        public override SequenceTypeTest ReturnType => Definition.ReturnType;

        public override string Describe() => $"function {Definition.Name.EQName}#{Arity}";
    }

    /// <summary>A dynamic function call being made: the function item and the arguments.</summary>
    internal sealed class PendingCall : XdmFunction
    {
        public PendingCall(XdmFunction function)
        {
            Function = function;
        }

        public XdmFunction Function { get; }

        public List<IList<XPathItem>> Arguments { get; } = new();

        public override int Arity => 0;

        public override SequenceTypeTest ParameterType(int index) => SequenceTypeTest.AnySequence;

        public override SequenceTypeTest ReturnType => SequenceTypeTest.AnySequence;

        public override string Describe() => "a function call";
    }

    public sealed partial class XsltLibrary
    {
        // ---------------------------------------------------------------- creating function items

        public XPathItem X31FunctionNew(double id, double signature) =>
            new CompiledFunction((int)id, (FunctionSignature)ConstantPool.Get((int)signature), _runtime);

        public XPathItem X31FunctionCapture(XPathItem function, IList<XPathItem> value)
        {
            ((CompiledFunction)function).Captured.Add(Copy(value));
            return function;
        }

        // ---------------------------------------------------------------- dynamic calls

        /// <summary>Starts a dynamic call: the function expression's value must be one function item of that arity (XPTY0004).</summary>
        public XPathItem X31CallNew(IList<XPathItem> function, double arity)
        {
            if (function.Count != 1 || function[0] is not XdmFunction f || function[0] is PendingCall)
            {
                throw Err.XPTY0004($"A dynamic function call needs a single function item, not {Coercion.Describe(function)}");
            }
            if (f.Arity != (int)arity)
            {
                throw Err.XPTY0004($"{f.Describe()} is called with {(int)arity} arguments");
            }
            return new PendingCall(f);
        }

        public XPathItem X31CallArgument(XPathItem call, IList<XPathItem> value)
        {
            var c = (PendingCall)call;
            // The function conversion rules to the declared parameter type (XPath 3.1 §3.1.5.2).
            int i = c.Arguments.Count;
            c.Arguments.Add(Coercion.CoerceTo(Copy(value), c.Function.ParameterType(i), "XPTY0004", () => $"Argument {i + 1} of {c.Function.Describe()}"));
            return c;
        }

        /// <summary>fn:apply: a call with the members of the array as the arguments.</summary>
        public XPathItem X31ApplyCall(IList<XPathItem> function, IList<XPathItem> arguments)
        {
            var array = (XdmArray)arguments[0];
            if (function.Count == 1 && function[0] is XdmFunction f && f.Arity != array.Count)
            {
                throw new XdmException("FOAP0001", $"fn:apply: {f.Describe()} doesn't take {array.Count} arguments");
            }
            XPathItem call = X31CallNew(function, array.Count);
            foreach (IList<XPathItem> member in array.Members)
            {
                X31CallArgument(call, member);
            }
            return call;
        }

        /// <summary>The id of the lifted function a call goes to, or -1 if the run-time library makes it.</summary>
        public double X31CallTarget(XPathItem call) => ((PendingCall)call).Function is CompiledFunction f && f.Owner == _runtime ? f.Id : -1;

        public IList<XPathItem> X31CallCaptured(XPathItem call, double index) => ((CompiledFunction)((PendingCall)call).Function).Captured[(int)index];

        public IList<XPathItem> X31CallArg(XPathItem call, double index) => ((PendingCall)call).Arguments[(int)index];

        /// <summary>A call the run-time library makes: of a map, an array or a built-in function.</summary>
        public IList<XPathItem> X31CallRuntime(XPathItem call)
        {
            var c = (PendingCall)call;
            switch (c.Function)
            {
                case XdmMap map:
                    return map.Get(Sequences.AtomizeOptional(c.Arguments[0], "a map key") ?? throw Err.XPTY0004("A map is called with an empty key")) ?? Sequences.Empty;
                case XdmArray array:
                    AtomicValue? index = Sequences.AtomizeOptional(c.Arguments[0], "an array index");
                    return index is IntegerValue i ? array.Get(i.Integer) : throw Err.XPTY0004("An array is called with an integer");
                case LibraryFunction lf:
                    return lf.Definition.Invoke(lf.Context, c.Arguments.ToArray());
                case NativeFunction nf:
                    return nf.Invoke(c.Arguments.ToArray());
                case CompiledFunction cf when cf.Owner != _runtime && cf.Owner.Dispatcher is { } foreign:
                    // A function item of another compiled unit: its own dispatcher, in its own runtime.
                    return foreign(cf.Owner, call);
                default:
                    throw new XdmException("XPTY0004", $"{c.Function.Describe()} can't be called here (it belongs to another compiled query)");
            }
        }

        /// <summary>The result of a function whose type is xs:boolean (fn:filter's predicate): XPTY0004 for anything else.</summary>
        public bool X31BooleanResult(IList<XPathItem> value) =>
            value.Count == 1 && value[0] is BooleanValue b ? b.Boolean : throw Err.XPTY0004($"The function must return a single xs:boolean, not {Coercion.Describe(value)}");

        /// <summary>Calls a function item from the run-time library: compiled functions through the dispatcher.</summary>
        internal IList<XPathItem> CallFunction(XdmFunction function, params IList<XPathItem>[] arguments)
        {
            var call = new PendingCall(function);
            foreach (IList<XPathItem> argument in arguments)
            {
                X31CallArgument(call, argument);
            }
            if (function is CompiledFunction cf)
            {
                var dispatch = cf.Owner.Dispatcher ?? throw new System.InvalidOperationException("No dispatcher was compiled");
                return dispatch(cf.Owner, call);
            }
            return X31CallRuntime(call);
        }

        /// <summary>fn:fold-left and fn:fold-right (and the array: ones, whose members arrive wrapped).</summary>
        public IList<XPathItem> X31Fold(IList<XPathItem> items, IList<XPathItem> zero, IList<XPathItem> function, bool left, bool wrapped)
        {
            if (function.Count != 1 || function[0] is not XdmFunction f || f.Arity != 2)
            {
                throw Err.XPTY0004($"The function argument of a fold must be a function of arity 2, not {Coercion.Describe(function)}");
            }
            IList<XPathItem> acc = Copy(zero);
            int n = items.Count;
            for (int k = 0; k < n; k++)
            {
                XPathItem item = items[left ? k : n - 1 - k];
                IList<XPathItem> value = wrapped ? ((XdmArray)item)[0] : Sequences.One(item);
                acc = Copy(left ? CallFunction(f, acc, value) : CallFunction(f, value, acc));
            }
            return acc;
        }

        /// <summary>
        /// xsl:try (XSLT 3.0 §8.3): the value of the try function, or, if it raises a dynamic error, of the first
        /// catch function whose name tests match the error code, called with the error's code, description, value,
        /// module, line and column numbers and additional information.
        /// </summary>
        public IList<XPathItem> X31Try(IList<XPathItem> tryFunction, IList<XPathItem> catchFunctions, double errorsId, string module)
        {
            try
            {
                return Copy(CallFunction((XdmFunction)tryFunction[0]));
            }
            catch (System.Exception e) when (DynamicError(e) is (QName code, string description, IList<XPathItem> value))
            {
                var errors = (List<List<string>>)ConstantPool.Get((int)errorsId);
                for (int i = 0; i < errors.Count; i++)
                {
                    if (errors[i].Exists(test => ErrorNameMatches(test, code)))
                    {
                        IList<XPathItem> none = System.Array.Empty<XPathItem>();
                        return Copy(CallFunction((XdmFunction)catchFunctions[i], Sequences.One(new QNameValue(AtomicTypes.QName, code)),
                            Sequences.One(AtomicValue.OfString(description)), value, module.Length == 0 ? none : Sequences.One(AtomicValue.OfString(module)), none, none, none));
                    }
                }
                throw;
            }
        }

        /// <summary>A pattern's condition, lifted: an error evaluating it is a non-match (XSLT 3.0 §5.5.4).</summary>
        public bool X31PatternTry(IList<XPathItem> test)
        {
            try
            {
                return Sequences.EffectiveBooleanValue(CallFunction((XdmFunction)test[0]));
            }
            catch (System.Exception e) when (DynamicError(e) != null)
            {
                return false;
            }
        }

        /// <summary>A captured number (a position) back as a double.</summary>
        public double X31CapturedNumber(IList<XPathItem> value) => System.Xml.XmlConvert.ToDouble(value[0].Value);

        // The code, description and value of a dynamic error, or null for exceptions that aren't errors.
        private static (QName, string, IList<XPathItem>)? DynamicError(System.Exception e)
        {
            switch (e)
            {
                case XdmException x:
                    return (x.Name ?? new QName(XdmException.ErrorNamespace, x.Code, "err"), x.Description ?? Describe(x.Message, x.Code),
                        x.ErrorObject ?? System.Array.Empty<XPathItem>());
                case XslTransformException or System.Xml.XmlException or System.FormatException or System.OverflowException or System.DivideByZeroException:
                    string message = e.Message;
                    var m = System.Text.RegularExpressions.Regex.Match(message, @"^([A-Z]{4}[0-9]{4})\b");
                    string code = m.Success ? m.Groups[1].Value : "XTDE0000";
                    return (new QName(XdmException.ErrorNamespace, code, "err"), Describe(message, code), System.Array.Empty<XPathItem>());
                case System.Reflection.TargetInvocationException { InnerException: System.Exception inner }:
                    return DynamicError(inner);
                default:
                    return null;
            }
        }

        private static string Describe(string message, string code) =>
            message.StartsWith(code + ": ", System.StringComparison.Ordinal) ? message[(code.Length + 2)..] : message;

        // A NameTest of xsl:catch/@errors: "*", "Q{ns}*", "*:local" or "Q{ns}local".
        private static bool ErrorNameMatches(string test, QName code)
        {
            if (test == "*")
            {
                return true;
            }
            if (test.StartsWith("*:", System.StringComparison.Ordinal))
            {
                return code.LocalName == test[2..];
            }
            int close = test.IndexOf('}');
            string ns = test[2..close];
            string local = test[(close + 1)..];
            return code.NamespaceUri == ns && (local == "*" || local == code.LocalName);
        }

        // ---------------------------------------------------------------- function item accessors

        public IList<XPathItem> X31FunctionName(IList<XPathItem> function) =>
            ((XdmFunction)function[0]).Name is QName n ? Sequences.One(new QNameValue(AtomicTypes.QName, n)) : Sequences.Empty;

        public XPathItem X31FunctionArity(IList<XPathItem> function) => AtomicValue.OfInteger(((XdmFunction)function[0]).Arity);

        /// <summary>fn:function-lookup for a built-in function (user functions are found when the name is a literal).</summary>
        public IList<XPathItem> X31FunctionLookup(IList<XPathItem> name, IList<XPathItem> arity, double sc)
        {
            // The arguments' types are xs:QName and xs:integer, exactly one each (XPTY0004).
            if (name.Count != 1 || name[0] is not QNameValue { QName: var q } || arity.Count != 1 || arity[0] is not IntegerValue arityValue)
            {
                throw Err.XPTY0004($"fn:function-lookup takes one xs:QName and one xs:integer, not {Coercion.Describe(name)} and {Coercion.Describe(arity)}");
            }
            if (arityValue.Integer < 0 || arityValue.Integer > int.MaxValue)
            {
                return Sequences.Empty;
            }
            int n = (int)arityValue.Integer;
            FunctionDefinition? def = FunctionLibrary.Find(q.NamespaceUri, q.LocalName, n);
            return def == null ? Sequences.Empty : Sequences.One(new LibraryFunction(def, n, X31ContextFor(sc)));
        }

        // ---------------------------------------------------------------- helpers for the higher-order functions

        public IList<XPathItem> X31ItemAt(IList<XPathItem> items, double position) => Sequences.One(items[(int)position - 1]);

        /// <summary>A sequence known to hold one item, as the item.</summary>
        public XPathItem X31SingleItemOf(IList<XPathItem> items) =>
            items.Count == 1 ? items[0] : throw new XdmException("XPDY0002", "The context item is absent");

        public double X31Count(IList<XPathItem> items) => items.Count;

        /// <summary>A sequence as a single item (an array with one member), so that it survives a QIL loop as one value.</summary>
        public XPathItem X31Wrap(IList<XPathItem> value) => XdmArray.Of(new[] { Copy(value) });

        public IList<XPathItem> X31Unwrap(XPathItem wrapped) => ((XdmArray)wrapped)[0];

        /// <summary>The wrapped values (X31Wrap) as an array's members.</summary>
        public XPathItem X31ArrayOfWrapped(IList<XPathItem> wrapped) => XdmArray.Of(wrapped.Select(w => ((XdmArray)w)[0]));

        public IList<XPathItem> X31ArrayMembersWrapped(IList<XPathItem> array) =>
            ((XdmArray)array[0]).Members.Select(m => (XPathItem)XdmArray.Of(new[] { m })).ToList();

        public IList<XPathItem> X31MapKeys(IList<XPathItem> map) => ((XdmMap)map[0]).Keys.Cast<XPathItem>().ToList();

        public IList<XPathItem> X31MapGet(IList<XPathItem> map, IList<XPathItem> key) => ((XdmMap)map[0]).Get((AtomicValue)key[0]) ?? Sequences.Empty;

        /// <summary>fn:sort#3 and array:sort#3: the items in the order of their keys (each key wrapped by X31Wrap).</summary>
        public IList<XPathItem> X31SortByKeys(IList<XPathItem> items, IList<XPathItem> wrappedKeys, IList<XPathItem> collation, double sc)
        {
            FunctionContext c = X31ContextFor(sc);
            if (collation.Count > 0)
            {
                c = c.WithDefaultCollation(Collation.Resolve(((AtomicValue)collation[0]).Value));
            }
            int[] order = FunctionLibrary.SortOrder(wrappedKeys.Select(k => Sequences.Atomize(((XdmArray)k)[0])).ToList(), c);
            return order.Select(i => items[i]).ToList();
        }
    }
}
