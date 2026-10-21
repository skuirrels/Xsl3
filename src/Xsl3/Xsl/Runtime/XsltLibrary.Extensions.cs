// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.Xml.XPath;
using Xsl3.Xdm;

namespace Xsl3.Runtime
{
    /// <summary>An extension function registered through the 3.0 API (XsltCompiler.RegisterExtensionFunction).</summary>
    internal sealed record RegisteredFunction(QName Name, int Arity, System.Func<XdmValue[], XdmValue> Body);

    public sealed partial class XsltLibrary
    {
        // A call of a registered extension function while its arguments are gathered.
        private sealed class PendingExtension(RegisteredFunction function) : AtomicValue(AtomicTypes.String)
        {
            public RegisteredFunction Function { get; } = function;

            public List<XdmValue> Arguments { get; } = new();

            public override string Value => "";

            public override object ClrValue => Function;
        }

        public XPathItem X31ExtensionStart(double functionId) => new PendingExtension((RegisteredFunction)Constant((int)functionId));

        public XPathItem X31ExtensionArgument(XPathItem call, IList<XPathItem> value)
        {
            // (A copy: the generated code may reuse the list it passed.)
            ((PendingExtension)call).Arguments.Add(XdmValue.Wrap(Copy(value)));
            return call;
        }

        /// <summary>
        /// Calls a registered extension function. Its .NET exceptions are Q{http://xsl3/err}extension-error, which
        /// xsl:try can catch; a dynamic error it raises as XsltDynamicException keeps its code.
        /// </summary>
        public IList<XPathItem> X31ExtensionCall(XPathItem call)
        {
            var pending = (PendingExtension)call;
            XdmValue? result;
            try
            {
                result = pending.Function.Body(pending.Arguments.ToArray());
            }
            catch (XsltDynamicException e)
            {
                throw new XdmException(e.Code.LocalName, e.Description) { Name = e.Code, Description = e.Description, ErrorObject = e.ErrorObject.Items };
            }
            catch (System.Exception e) when (e is not (System.OperationCanceledException or System.OutOfMemoryException or XdmException))
            {
                var error = new XslTransformException(e, SR.Xml_UserException, $"The extension function {pending.Function.Name.EQName} failed: {e.Message}");
                error.Data[XmlExtensionFunction.ExtensionErrorKey] = true;
                throw error;
            }
            return result?.Items ?? Sequences.Empty;
        }
    }
}
