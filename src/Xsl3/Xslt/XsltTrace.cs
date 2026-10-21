// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using Xsl3.Xdm;

namespace Xsl3
{
    /// <summary>
    /// Receives the start and end of each template and stylesheet function a transformation runs (SPEC §21.3), when the
    /// stylesheet was loaded with <see cref="XsltSettings.EnableTracing"/>. Set it on
    /// <see cref="XsltArgumentList.TraceListener"/> or <see cref="Xslt30Transformer.TraceListener"/>. An error ends
    /// frames without <see cref="Leave"/>.
    /// </summary>
    public interface IXsltTraceListener
    {
        void Enter(XsltFrame frame);

        void Leave(XsltFrame frame);
    }

    /// <summary>A template or stylesheet function, as a trace listener and a stylesheet stack trace show it.</summary>
    public sealed class XsltFrame
    {
        internal XsltFrame(string kind, QName? name, string? match, QName? mode, string? moduleUri, int lineNumber, int columnNumber)
        {
            Kind = kind;
            Name = name;
            Match = match;
            Mode = mode;
            ModuleUri = moduleUri;
            LineNumber = lineNumber;
            ColumnNumber = columnNumber;
        }

        /// <summary>"template" or "function".</summary>
        public string Kind { get; }

        /// <summary>The template's or function's name; null for a template rule without one.</summary>
        public QName? Name { get; }

        /// <summary>A template rule's match pattern.</summary>
        public string? Match { get; }

        /// <summary>A template rule's mode (null for the unnamed mode).</summary>
        public QName? Mode { get; }

        /// <summary>The URI of the module the declaration is in.</summary>
        public string? ModuleUri { get; }

        public int LineNumber { get; }

        public int ColumnNumber { get; }

        public override string ToString() =>
            $"{(Kind == "function" ? "function " + Name?.EQName : Name != null ? "template " + Name.EQName : $"template match=\"{Match}\"")}" +
            $"{(Mode != null ? " mode=" + Mode.EQName : "")} at {ModuleUri}({LineNumber},{ColumnNumber})";
    }
}
