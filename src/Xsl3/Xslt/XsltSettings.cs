// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// <spec>http://webdata/xml/specs/XslCompiledTransform.xml</spec>
//------------------------------------------------------------------------------

namespace Xsl3
{
    /// <summary>Xsl3: how a stylesheet whose principal module has <c>version="1.0"</c> is run (SPEC §4.3).</summary>
    public enum XsltVersionMode
    {
        /// <summary>
        /// On the XSLT 1.0 engine of <c>System.Xml.Xsl</c>, with byte-identical results, <c>msxsl:</c> extensions and
        /// stock's string semantics. The default.
        /// </summary>
        Legacy,

        /// <summary>
        /// On the XSLT 3.0 processor in backwards-compatible mode (XSLT 3.0 §3.9): XPath 1.0 compatibility mode, the
        /// first item of a value-of or attribute value template, and the 3.0 instructions available.
        /// </summary>
        BackwardsCompatible,
    }

    public sealed class XsltSettings
    {
        private bool _enableDocumentFunction;
        private bool _enableScript;
        private bool _checkOnly;
        private bool _includeDebugInformation;
        private int _warningLevel = -1;     // -1 means not set
        private bool _treatWarningsAsErrors;
        private bool _enableEvaluate;

        public XsltSettings() { }

        public XsltSettings(bool enableDocumentFunction, bool enableScript)
        {
            _enableDocumentFunction = enableDocumentFunction;
            _enableScript = enableScript;
        }

        public static XsltSettings Default
        {
            get { return new XsltSettings(false, false); }
        }

        public static XsltSettings TrustedXslt
        {
            get { return new XsltSettings(true, true) { EnableEvaluate = true }; }
        }

        public bool EnableDocumentFunction
        {
            get { return _enableDocumentFunction; }
            set { _enableDocumentFunction = value; }
        }

        /// <summary>
        /// Xsl3: whether <c>xsl:evaluate</c> may compile and run XPath expressions built at run time (XSLT 3.0 §10.4).
        /// Off by default, since the expressions may come from untrusted data. When off, <c>xsl:evaluate</c> is
        /// statically disabled: <c>element-available('xsl:evaluate')</c> is false,
        /// <c>system-property('xsl:supports-dynamic-evaluation')</c> is "no", an <c>xsl:fallback</c> child is
        /// used, and otherwise evaluating the instruction raises XTDE3175. <see cref="TrustedXslt"/> turns it on.
        /// </summary>
        /// <summary>
        /// Xsl3: how a stylesheet whose principal module has <c>version="1.0"</c> runs: <see cref="XsltVersionMode.Legacy"/>
        /// (the default) gives stock's results; <see cref="XsltVersionMode.BackwardsCompatible"/> runs it on the 3.0
        /// processor, as the XSLT 3.0 specification describes. Regions with <c>version="1.0"</c> inside 2.0+ stylesheets
        /// are always backwards-compatible.
        /// </summary>
        public XsltVersionMode VersionMode { get; set; }

        public bool EnableEvaluate
        {
            get { return _enableEvaluate; }
            set { _enableEvaluate = value; }
        }

        /// <summary>
        /// Xsl3: whether <c>xsl:assert</c> is evaluated (XSLT 3.0 §23.2). Off by default, when assertions are ignored;
        /// on, a false assertion raises XTMM9001 (or its <c>error-code</c>).
        /// </summary>
        public bool EnableAssertions { get; set; }

        /// <summary>
        /// Xsl3: whether <c>xsl:result-document</c> may write secondary results (SPEC §21.1). Off by default, as a
        /// stylesheet could otherwise write files. When on, a secondary result is written to a file, and only under
        /// <see cref="XslCompiledTransform.BaseOutputUri"/> (the results file for <c>Transform(string, string)</c>);
        /// anywhere else is XTDE1480.
        /// </summary>
        public bool EnableResultDocument { get; set; }

        /// <summary>
        /// Xsl3: whether <c>fn:environment-variable</c> and <c>fn:available-environment-variables</c> see the
        /// process's environment variables (SPEC §21.1). Off by default: there are none.
        /// </summary>
        public bool EnableEnvironmentVariables { get; set; }

        /// <summary>
        /// Xsl3: how long a transformation may run; it is then stopped at its next template or function entry with
        /// the error <c>Q{http://xsl3/err}cancelled</c> (SPEC §21.1). Null: no limit.
        /// </summary>
        public TimeSpan? Timeout
        {
            get => _timeout;
            set
            {
                if (value is TimeSpan t && (t <= TimeSpan.Zero || t.TotalMilliseconds > int.MaxValue))
                {
                    throw new ArgumentOutOfRangeException(nameof(value), t, "The timeout must be positive and at most int.MaxValue milliseconds.");
                }
                _timeout = value;
            }
        }
        private TimeSpan? _timeout;

        /// <summary>
        /// Xsl3: run each transformation on a thread of its own with a stack of <see cref="DedicatedThreadStackSize"/>
        /// bytes, for stylesheets that recurse deeply without tail calls (SPEC §21.2). Off by default.
        /// </summary>
        public bool UseDedicatedThread { get; set; }

        /// <summary>Xsl3: the stack size of the thread <see cref="UseDedicatedThread"/> runs on, in bytes (64 MB by default; 0 for the process default).</summary>
        public int DedicatedThreadStackSize
        {
            get => _dedicatedThreadStackSize;
            set
            {
                ArgumentOutOfRangeException.ThrowIfNegative(value);
                _dedicatedThreadStackSize = value;
            }
        }
        private int _dedicatedThreadStackSize = 64 * 1024 * 1024;

        /// <summary>
        /// Xsl3: whether the extension objects of <see cref="XsltArgumentList.AddExtensionObject"/> are called (SPEC
        /// §21.1). On by default, as stock; off, their functions are unknown, as if none had been added.
        /// </summary>
        public bool EnableExtensionFunctions { get; set; } = true;

        /// <summary>
        /// Xsl3: the deepest nesting of template and function calls a transformation may reach (SPEC §21.2); beyond
        /// it, the transformation stops with <c>Q{http://xsl3/err}recursion-limit</c>. 0, the default: no count, only
        /// the stack probe at every call that stops a transformation before the stack runs out. Tail calls don't add to
        /// the depth. Set before <c>Load</c>: the counting is compiled in.
        /// </summary>
        public int MaxRecursionDepth
        {
            get => _maxRecursionDepth;
            set
            {
                ArgumentOutOfRangeException.ThrowIfNegative(value);
                _maxRecursionDepth = value;
            }
        }
        private int _maxRecursionDepth;

        /// <summary>
        /// Xsl3: the most bytes a transformation may write to its principal result, counted as UTF-8 for a
        /// <c>TextWriter</c> (SPEC §21.1); beyond it, the transformation stops with
        /// <c>Q{http://xsl3/err}output-limit</c>. Null, the default: no limit. Applies to the <c>Stream</c> and
        /// <c>TextWriter</c> overloads of <c>Transform</c>; an <c>XmlWriter</c> the caller supplies is the caller's to
        /// limit.
        /// </summary>
        public long? MaxOutputBytes
        {
            get => _maxOutputBytes;
            set
            {
                if (value is long n && n <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), n, "The output limit must be positive.");
                }
                _maxOutputBytes = value;
            }
        }
        private long? _maxOutputBytes;

        /// <summary>Xsl3: a copy, so a transform keeps the settings it was loaded with.</summary>
        internal XsltSettings Clone() => (XsltSettings)MemberwiseClone();

        [Obsolete(Obsoletions.XsltSettingsEnableScriptMessage, DiagnosticId = Obsoletions.XsltSettingsEnableScriptDiagId, UrlFormat = Obsoletions.SharedUrlFormat)]
        public bool EnableScript
        {
            get { return _enableScript; }
            set { _enableScript = value; }
        }

        internal bool CheckOnly
        {
            get { return _checkOnly; }
            set { _checkOnly = value; }
        }

        internal bool IncludeDebugInformation
        {
            get { return _includeDebugInformation; }
            set { _includeDebugInformation = value; }
        }

        internal int WarningLevel
        {
            get { return _warningLevel; }
            set { _warningLevel = value; }
        }

        internal bool TreatWarningsAsErrors
        {
            get { return _treatWarningsAsErrors; }
            set { _treatWarningsAsErrors = value; }
        }
    }
}
