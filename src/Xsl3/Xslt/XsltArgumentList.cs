// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace Xsl3
{
    public abstract class XsltMessageEncounteredEventArgs : EventArgs
    {
        public abstract string Message { get; }
    }

    public delegate void XsltMessageEncounteredEventHandler(object sender, XsltMessageEncounteredEventArgs e);

    public class XsltArgumentList
    {
        private readonly Hashtable _parameters = new Hashtable();
        private readonly Hashtable _extensions = new Hashtable();
        private const string ExtensionObjectWarning = @"The stylesheet may have calls to methods of the extension object passed in which cannot be statically analyzed " +
            "by the trimmer. Ensure all methods that may be called are preserved.";
        internal const string ExtensionObjectSuppresion = @"In order for this code path to be hit, a previous call to XsltArgumentList.AddExtensionObject is " +
            "required. That method is already annotated as unsafe and throwing a warning, so we can suppress here.";

        // Used for reporting xsl:message's during execution
        internal XsltMessageEncounteredEventHandler? xsltMessageEncountered;

        public XsltArgumentList() { }

        /// <summary>
        /// Xsl3: stops a transformation that uses this argument list: it is checked at every template and function
        /// entry, and raises <c>Q{http://xsl3/err}cancelled</c> (SPEC §21.1).
        /// </summary>
        public System.Threading.CancellationToken CancellationToken { get; set; }

        /// <summary>
        /// Xsl3: receives each <c>fn:trace</c> call of a 2.0+ stylesheet (F&amp;O 3.1 §3.2.1): its label ("" without
        /// one) and its value, written out (nodes as XML, atomic values as strings, separated by ", "). Without a
        /// handler, <c>fn:trace</c> just returns its value.
        /// </summary>
        public Action<string, string>? TraceHandler { get; set; }

        public object? GetParam(string name, string namespaceUri)
        {
            return _parameters[new XmlQualifiedName(name, namespaceUri)];
        }

        [RequiresUnreferencedCode(ExtensionObjectWarning)]
        public object? GetExtensionObject(string namespaceUri)
        {
            return _extensions[namespaceUri];
        }

        public void AddParam(string name, string namespaceUri, object parameter)
        {
            ArgumentNullException.ThrowIfNull(name);
            ArgumentNullException.ThrowIfNull(namespaceUri);
            ArgumentNullException.ThrowIfNull(parameter);

            XmlQualifiedName qname = new XmlQualifiedName(name, namespaceUri);
            qname.Verify();
            _parameters.Add(qname, parameter);
        }

        [RequiresUnreferencedCode(ExtensionObjectWarning)]
        public void AddExtensionObject(string namespaceUri, object extension)
        {
            ArgumentNullException.ThrowIfNull(namespaceUri);
            ArgumentNullException.ThrowIfNull(extension);

            _extensions.Add(namespaceUri, extension);
        }

        public object? RemoveParam(string name, string namespaceUri)
        {
            XmlQualifiedName qname = new XmlQualifiedName(name, namespaceUri);
            object? parameter = _parameters[qname];
            _parameters.Remove(qname);
            return parameter;
        }

        public object? RemoveExtensionObject(string namespaceUri)
        {
            object? extension = _extensions[namespaceUri];
            _extensions.Remove(namespaceUri);
            return extension;
        }

        public event XsltMessageEncounteredEventHandler XsltMessageEncountered
        {
            add
            {
                xsltMessageEncountered += value;
            }
            remove
            {
                xsltMessageEncountered -= value;
            }
        }

        public void Clear()
        {
            _parameters.Clear();
            _extensions.Clear();
            xsltMessageEncountered = null;
        }
    }
}
