// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// <spec>http://webdata/xml/specs/XslCompiledTransform.xml</spec>
//------------------------------------------------------------------------------

namespace Xsl3
{
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
        public TimeSpan? Timeout { get; set; }

        /// <summary>
        /// Xsl3: run each transformation on a thread of its own with a stack of <see cref="DedicatedThreadStackSize"/>
        /// bytes, for stylesheets that recurse deeply without tail calls (SPEC §21.2). Off by default.
        /// </summary>
        public bool UseDedicatedThread { get; set; }

        /// <summary>Xsl3: the stack size of the thread <see cref="UseDedicatedThread"/> runs on (64 MB by default).</summary>
        public int DedicatedThreadStackSize { get; set; } = 64 * 1024 * 1024;

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
