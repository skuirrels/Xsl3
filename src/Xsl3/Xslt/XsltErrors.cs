// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using CompilerError = Xsl3.Xslt.CompilerError;
using CompilerErrorCollection = Xsl3.Xslt.CompilerErrorCollection;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Xsl3.Runtime;
using Xsl3.Xdm;

namespace Xsl3
{
    /// <summary>
    /// A dynamic error that ended a transformation run through the XSLT 3.0 API (XSLT 3.0 §2.14): its code, as
    /// <c>xsl:catch</c> would see it in <c>$err:code</c>, its description and its error object.
    /// </summary>
    public sealed class XsltDynamicException : Exception
    {
        public XsltDynamicException(QName code, string description, XdmValue? errorObject = null, Exception? innerException = null)
            : base($"{code.LocalName}: {description}", innerException)
        {
            Code = code ?? throw new ArgumentNullException(nameof(code));
            Description = description;
            ErrorObject = errorObject ?? XdmValue.Empty;
        }

        /// <summary>
        /// The error code: in the <c>http://www.w3.org/2005/xqt-errors</c> namespace for the specifications' errors
        /// (XTDE0640, FOAR0001, …), the stylesheet's own for <c>fn:error</c> and <c>xsl:message terminate="yes"</c>, and
        /// <c>Q{http://xsl3/err}</c> for Xsl3's limits (<c>cancelled</c>, <c>recursion-limit</c>, <c>output-limit</c>,
        /// <c>extension-error</c>).
        /// </summary>
        public QName Code { get; }

        /// <summary>The description, without the code.</summary>
        public string Description { get; }

        /// <summary>The error object <c>fn:error</c> was given, or the empty sequence.</summary>
        public XdmValue ErrorObject { get; }

        private static readonly Regex s_xsl3Code = new(@"^Q\{(http://xsl3/err)\}([\w-]+): ", RegexOptions.CultureInvariant);

        /// <summary>The API's form of an exception a run raised: a dynamic error, or null for other exceptions.</summary>
        internal static XsltDynamicException? From(Exception e)
        {
            if (e is XsltDynamicException d)
            {
                return d;
            }
            if (e is XslTransformException && s_xsl3Code.Match(e.Message) is { Success: true } limit)
            {
                return new XsltDynamicException(new QName(limit.Groups[1].Value, limit.Groups[2].Value, "xsl3"), e.Message[limit.Length..], null, e);
            }
            return XsltLibrary.DynamicError(e) is (QName code, string description, var value)
                ? new XsltDynamicException(code, description, XdmValue.Wrap(value), e)
                : null;
        }
    }

    /// <summary>One static error (or warning) in a stylesheet module: its code when it has one, and where it is.</summary>
    public sealed class XsltStaticError
    {
        internal XsltStaticError(CompilerError error)
        {
            Message = error.ErrorText;
            ModuleUri = string.IsNullOrEmpty(error.FileName) ? null : error.FileName;
            LineNumber = error.Line;
            ColumnNumber = error.Column;
            IsWarning = error.IsWarning;
            Match code = Regex.Match(error.ErrorText, @"\b([A-Z]{4}[0-9]{4})\b");
            Code = code.Success ? new QName("http://www.w3.org/2005/xqt-errors", code.Groups[1].Value, "err") : null;
        }

        /// <summary>The error code (XTSE0010, XPST0008, …), or null when the message names none.</summary>
        public QName? Code { get; }

        public string Message { get; }

        /// <summary>The URI of the module the error is in, if known.</summary>
        public string? ModuleUri { get; }

        /// <summary>The line, from 1 (0 if unknown).</summary>
        public int LineNumber { get; }

        /// <summary>The column, from 1 (0 if unknown).</summary>
        public int ColumnNumber { get; }

        public bool IsWarning { get; }

        public override string ToString() => $"{ModuleUri}({LineNumber},{ColumnNumber}): {Message}";
    }

    /// <summary>A stylesheet that can't be compiled: every static error found (XSLT 3.0 §2.14).</summary>
    public sealed class XsltStaticException : Exception
    {
        internal XsltStaticException(IReadOnlyList<XsltStaticError> errors, Exception? innerException)
            : base(errors.FirstOrDefault(e => !e.IsWarning)?.ToString() ?? innerException?.Message ?? "The stylesheet has static errors", innerException)
        {
            Errors = errors;
        }

        /// <summary>The errors and warnings, in the order they were found.</summary>
        public IReadOnlyList<XsltStaticError> Errors { get; }
    }
}
