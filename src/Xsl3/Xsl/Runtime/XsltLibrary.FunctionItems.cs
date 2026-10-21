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
    internal sealed class CompiledFunction : FunctionItem
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
    internal sealed class LibraryFunction : FunctionItem
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
    internal sealed class PendingCall : FunctionItem
    {
        public PendingCall(FunctionItem function)
        {
            Function = function;
        }

        public FunctionItem Function { get; }

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
            new CompiledFunction((int)id, (FunctionSignature)Constant((int)signature), _runtime);

        public XPathItem X31FunctionCapture(XPathItem function, IList<XPathItem> value)
        {
            ((CompiledFunction)function).Captured.Add(Copy(value));
            return function;
        }

        // ---------------------------------------------------------------- dynamic calls

        /// <summary>Starts a dynamic call: the function expression's value must be one function item of that arity (XPTY0004).</summary>
        public XPathItem X31CallNew(IList<XPathItem> function, double arity)
        {
            if (function.Count != 1 || function[0] is not FunctionItem f || function[0] is PendingCall)
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
            var array = (ArrayItem)arguments[0];
            if (function.Count == 1 && function[0] is FunctionItem f && f.Arity != array.Count)
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
                case MapItem map:
                    return map.Get(Sequences.AtomizeOptional(c.Arguments[0], "a map key") ?? throw Err.XPTY0004("A map is called with an empty key")) ?? Sequences.Empty;
                case ArrayItem array:
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
        internal IList<XPathItem> CallFunction(FunctionItem function, params IList<XPathItem>[] arguments)
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

        /// <summary>The function of a partial application, f(a, ?): a single function item of the call's arity (XPTY0004).</summary>
        public IList<XPathItem> X31PartialTarget(IList<XPathItem> function, double arity) =>
            function.Count == 1 && function[0] is FunctionItem f && f.Arity == (int)arity ? function
                : throw Err.XPTY0004($"A partial application with {arity} arguments needs a function of arity {arity}, not {Coercion.Describe(function)}");

        /// <summary>fn:fold-left and fn:fold-right (and the array: ones, whose members arrive wrapped).</summary>
        public IList<XPathItem> X31Fold(IList<XPathItem> items, IList<XPathItem> zero, IList<XPathItem> function, bool left, bool wrapped)
        {
            if (function.Count != 1 || function[0] is not FunctionItem f || f.Arity != 2)
            {
                throw Err.XPTY0004($"The function argument of a fold must be a function of arity 2, not {Coercion.Describe(function)}");
            }
            IList<XPathItem> acc = Copy(zero);
            int n = items.Count;
            for (int k = 0; k < n; k++)
            {
                XPathItem item = items[left ? k : n - 1 - k];
                IList<XPathItem> value = wrapped ? ((ArrayItem)item)[0] : Sequences.One(item);
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
            var output = _runtime.SaveOutput();
            List<int> lines = _runtime.TryLines;
            int mark = lines.Count;
            try
            {
                return Copy(CallFunction((FunctionItem)tryFunction[0]));
            }
            catch (System.Exception e) when (DynamicError(e) is (QName code, string description, IList<XPathItem> value))
            {
                _runtime.RestoreOutput(output);
                int line = lines.Count > mark ? lines[^1] : 0;
                lines.RemoveRange(mark, lines.Count - mark);
                var errors = (List<List<string>>)Constant((int)errorsId);
                for (int i = 0; i < errors.Count; i++)
                {
                    if (errors[i].Exists(test => ErrorNameMatches(test, code)))
                    {
                        IList<XPathItem> none = System.Array.Empty<XPathItem>();
                        return Copy(CallFunction((FunctionItem)catchFunctions[i], Sequences.One(new QNameValue(AtomicTypes.QName, code)),
                            Sequences.One(AtomicValue.OfString(description)), value, module.Length == 0 ? none : Sequences.One(AtomicValue.OfString(module)),
                            line > 0 ? Sequences.One(AtomicValue.OfInteger(line)) : none, none, none));
                    }
                }
                throw;
            }
            finally
            {
                if (lines.Count > mark)
                {
                    lines.RemoveRange(mark, lines.Count - mark);
                }
            }
        }

        /// <summary>A template or function starts (XsltSettings.EnableTracing).</summary>
        public IList<XPathItem> X31TraceEnter(double frameId)
        {
            var frame = (XsltFrame)Constant((int)frameId);
            _runtime.TraceStack.Add(frame);
            _runtime.ExternalContext.TraceListener?.Enter(frame);
            return Sequences.Empty;
        }

        /// <summary>A template or function ends (XsltSettings.EnableTracing).</summary>
        public IList<XPathItem> X31TraceLeave(double frameId)
        {
            var frame = (XsltFrame)Constant((int)frameId);
            List<XsltFrame> stack = _runtime.TraceStack;
            if (stack.Count > 0)
            {
                stack.RemoveAt(stack.Count - 1);
            }
            _runtime.ExternalContext.TraceListener?.Leave(frame);
            return Sequences.Empty;
        }

        /// <summary>An instruction in an xsl:try body starts: its line is the catch's $err:line-number until it ends.</summary>
        public IList<XPathItem> X31TryEnter(double line)
        {
            _runtime.TryLines.Add((int)line);
            return Sequences.Empty;
        }

        /// <summary>An instruction in an xsl:try body ends.</summary>
        public IList<XPathItem> X31TryExit()
        {
            List<int> lines = _runtime.TryLines;
            if (lines.Count > 0)
            {
                lines.RemoveAt(lines.Count - 1);
            }
            return Sequences.Empty;
        }

        /// <summary>A pattern's condition, lifted: an error evaluating it is a non-match (XSLT 3.0 §5.5.4).</summary>
        public bool X31PatternTry(IList<XPathItem> test)
        {
            var output = _runtime.SaveOutput();
            try
            {
                return Sequences.EffectiveBooleanValue(CallFunction((FunctionItem)test[0]));
            }
            catch (System.Exception e) when (DynamicError(e) != null)
            {
                _runtime.RestoreOutput(output);
                return false;
            }
        }

        /// <summary>
        /// The text of a non-terminating xsl:message (XSLT 3.0 §23.1): its content as the serializer would show it, with
        /// attributes, maps and functions described rather than refused, and an error in the content reported in the
        /// message rather than ending the transformation.
        /// </summary>
        public string X31MessageText(IList<XPathItem> body)
        {
            var output = _runtime.SaveOutput();
            IList<XPathItem> items;
            try
            {
                items = CallFunction((FunctionItem)body[0]);
            }
            // Errors the stylesheet raises itself (fn:error, a terminating xsl:message, xsl:assert) still end it.
            catch (System.Exception e) when (DynamicError(e) is (QName code, string description, _)
                && !(e is XdmException { Name: not null } or XdmException { Code: "FOER0000" } || code.LocalName.StartsWith("XTMM", System.StringComparison.Ordinal)))
            {
                _runtime.RestoreOutput(output);
                return $"[the content of xsl:message raised {code.LocalName}: {description}]";
            }
            var sb = new System.Text.StringBuilder();
            bool atomic = false;
            foreach (XPathItem item in items)
            {
                switch (item)
                {
                    case XPathNavigator { NodeType: XPathNodeType.Root } document:
                        sb.Append(document.InnerXml);
                        break;
                    case XPathNavigator { NodeType: XPathNodeType.Attribute } attribute:
                        sb.Append(attribute.Name).Append("=\"").Append(attribute.Value).Append('"');
                        break;
                    case XPathNavigator { NodeType: XPathNodeType.Namespace } ns:
                        sb.Append(ns.Name.Length == 0 ? "xmlns" : "xmlns:" + ns.Name).Append("=\"").Append(ns.Value).Append('"');
                        break;
                    case XPathNavigator node:
                        sb.Append(node.OuterXml);
                        break;
                    case FunctionItem function:
                        sb.Append(function.Describe());
                        break;
                    default:
                        if (atomic)
                        {
                            sb.Append(' ');
                        }
                        sb.Append(item.Value);
                        break;
                }
                atomic = item is not XPathNavigator and not FunctionItem;
            }
            return sb.ToString();
        }

        /// <summary>A captured number (a position) back as a double.</summary>
        public double X31CapturedNumber(IList<XPathItem> value) => System.Xml.XmlConvert.ToDouble(value[0].Value);

        // The code, description and value of a dynamic error, or null for exceptions that aren't errors.
        internal static (QName, string, IList<XPathItem>)? DynamicError(System.Exception e)
        {
            switch (e)
            {
                case XdmException x:
                    return (x.Name ?? new QName(XdmException.ErrorNamespace, x.Code, "err"), x.Description ?? Describe(x.Message, x.Code),
                        x.ErrorObject ?? System.Array.Empty<XPathItem>());
                case XslTransformException when e.Data.Contains(XmlExtensionFunction.ExtensionErrorKey):
                    // An extension function's .NET exception (SPEC §16.7).
                    return (new QName("http://xsl3/err", "extension-error", "xsl3"), (e.InnerException ?? e).Message, System.Array.Empty<XPathItem>());
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
            ((FunctionItem)function[0]).Name is QName n ? Sequences.One(new QNameValue(AtomicTypes.QName, n)) : Sequences.Empty;

        public XPathItem X31FunctionArity(IList<XPathItem> function) => AtomicValue.OfInteger(((FunctionItem)function[0]).Arity);

        /// <summary>Whether a function-lookup arity (an xs:integer, unchecked here) is <paramref name="arity"/>.</summary>
        public bool X31ArityIs(IList<XPathItem> value, double arity) => value.Count == 1 && value[0] is IntegerValue i && i.Integer == (int)arity;

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
        public XPathItem X31Wrap(IList<XPathItem> value) => ArrayItem.Of(new[] { Copy(value) });

        public IList<XPathItem> X31Unwrap(XPathItem wrapped) => ((ArrayItem)wrapped)[0];

        /// <summary>The wrapped values (X31Wrap) as an array's members.</summary>
        public XPathItem X31ArrayOfWrapped(IList<XPathItem> wrapped) => ArrayItem.Of(wrapped.Select(w => ((ArrayItem)w)[0]));

        public IList<XPathItem> X31ArrayMembersWrapped(IList<XPathItem> array) =>
            ((ArrayItem)array[0]).Members.Select(m => (XPathItem)ArrayItem.Of(new[] { m })).ToList();

        public IList<XPathItem> X31MapKeys(IList<XPathItem> map) => ((MapItem)map[0]).Keys.Cast<XPathItem>().ToList();

        public IList<XPathItem> X31MapGet(IList<XPathItem> map, IList<XPathItem> key) => ((MapItem)map[0]).Get((AtomicValue)key[0]) ?? Sequences.Empty;

        /// <summary>fn:sort#3 and array:sort#3: the items in the order of their keys (each key wrapped by X31Wrap).</summary>
        public IList<XPathItem> X31SortByKeys(IList<XPathItem> items, IList<XPathItem> wrappedKeys, IList<XPathItem> collation, double sc)
        {
            FunctionContext c = X31ContextFor(sc);
            if (collation.Count > 0)
            {
                c = c.WithDefaultCollation(Collation.Resolve(((AtomicValue)collation[0]).Value));
            }
            int[] order = FunctionLibrary.SortOrder(wrappedKeys.Select(k => Sequences.Atomize(((ArrayItem)k)[0])).ToList(), c);
            return order.Select(i => items[i]).ToList();
        }
    }
}
