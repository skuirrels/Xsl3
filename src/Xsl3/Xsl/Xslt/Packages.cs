// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Linq;
using Xsl3.Qil;

namespace Xsl3.Xslt
{
    /// <summary>
    /// Xsl3: a name test of xsl:expose or xsl:accept (XSLT 3.0 §3.5.3.1): a namespace and a local name, either of
    /// which may be a wildcard (null), and for functions an optional arity.
    /// </summary>
    internal sealed record PackageNameTest(string? Namespace, string? Local, int? Arity, string Token)
    {
        public bool IsWildcard => Namespace == null || Local == null;
        public bool IsAll => Namespace == null && Local == null;

        public bool Matches(string ns, string local, int arity) =>
            (Namespace == null || Namespace == ns) && (Local == null || Local == local) && (Arity == null || Arity == arity);
    }

    /// <summary>An xsl:expose or xsl:accept: the component kind ('*', or T, F, V, A, M), name tests and visibility.</summary>
    internal sealed record PackageRule(char Kind, List<PackageNameTest> Names, string Visibility, ISourceLineInfo? Line)
    {
        public bool Used;
        public bool Matches(char kind, string ns, string local, int arity) =>
            (Kind == '*' || Kind == kind) && Names.Any(n => n.Matches(ns, local, arity));
    }

    /// <summary>A package and what it uses (XSLT 3.0 §3.5): principal is 0; used packages get ids as found.</summary>
    internal sealed class PackageInfo
    {
        public PackageInfo(int id) => Id = id;

        public int Id { get; }
        public bool IsPackage; // xsl:package rather than xsl:stylesheet (whose components are public)
        public bool DeclaredModes;
        public List<PackageRule> Expose { get; } = new();
        public List<UsedPackage> Uses { get; } = new();

        // The components the package's code can see, by kind and name, and their visibility in this package.
        public Dictionary<(char Kind, string Ns, string Local, int Arity), PackageComponent> Visible { get; } = new();
        public bool Built;
    }

    internal sealed class UsedPackage
    {
        public UsedPackage(int id, ISourceLineInfo? line)
        {
            Id = id;
            Line = line;
        }

        public int Id { get; }
        public ISourceLineInfo? Line { get; }
        public List<PackageRule> Accept { get; } = new();
    }

    /// <summary>A component as a package sees it: the name it's registered under, its visibility there, its declaration.</summary>
    internal sealed record PackageComponent(QilName Actual, string Visibility, XslNode? Declaration, int Owner);

    /// <summary>
    /// The component tables of the packages of a compilation. Every component of a used package is registered
    /// under a name in a namespace of its own (<see cref="Mangle"/>), so that one compiled program holds every
    /// package; names in a package's code are resolved through that package's table, which follows the
    /// visibility rules, and overridden components are redirected to their overrides.
    /// </summary>
    internal static class PackageComponents
    {
        private const string Marker = "\u0001xsl3-package-";

        private static readonly ISourceLineInfo NoSource = new SourceLineInfo("", 0, 0, 0, 0);

        public static string MangleNamespace(int package, string ns) => package == 0 ? ns : $"{Marker}{package}\u0001{ns}";

        public static QilName Mangle(QilName name, int package) =>
            package == 0 ? name : new QilName(QilNodeType.LiteralQName, name.LocalName, MangleNamespace(package, name.NamespaceUri), name.Prefix);

        public static (int Package, string Ns) Demangle(string ns)
        {
            if (!ns.StartsWith(Marker, StringComparison.Ordinal))
            {
                return (0, ns);
            }
            int end = ns.IndexOf('\u0001', Marker.Length);
            return (int.Parse(ns.AsSpan(Marker.Length, end - Marker.Length), System.Globalization.CultureInfo.InvariantCulture), ns[(end + 1)..]);
        }

        private static string Key(char kind, QilName actual, int arity) => $"{kind}|{actual.NamespaceUri}|{actual.LocalName}|{arity}";

        /// <summary>Builds every package's table, reporting the static errors of §3.5.3.</summary>
        public static void Build(Compiler compiler)
        {
            if (compiler.PackageInfos.Count == 0 || compiler.PackageInfos.Count == 1 && !compiler.PackageInfos[0].IsPackage)
            {
                return;
            }
            var components = Collect(compiler);
            foreach (PackageInfo p in compiler.PackageInfos)
            {
                Build(compiler, p, components);
            }
            // A mode named in a package's code (or an xsl:override's) is the mode that package sees by that name,
            // which may come from a package it uses.
            foreach (ProtoTemplate pt in compiler.AllTemplates)
            {
                if (pt is Template { Match: not null } t)
                {
                    foreach (QilName m in t.V2?.Modes ?? (IEnumerable<QilName>)new[] { t.Mode })
                    {
                        if (m.LocalName.Length != 0 && Demangle(m.NamespaceUri).Package > 0 && Resolve(compiler, 0, 'M', m, -1) is QilName actual)
                        {
                            m.NamespaceUri = actual.NamespaceUri;
                        }
                    }
                }
            }
            CheckEntryPoints(compiler);
            foreach (PackageInfo p in compiler.PackageInfos)
            {
                CheckDeclaredModes(compiler, p);
            }
        }

        // Every declared component: kind, plain name, arity, registered name, declaration, package, override target.
        private static List<(char Kind, string Ns, string Local, int Arity, QilName Actual, XslNode Decl, int Package, int OverrideOf)> Collect(Compiler compiler)
        {
            var list = new List<(char, string, string, int, QilName, XslNode, int, int)>();
            void Add(char kind, QilName name, int arity, XslNode decl)
            {
                var (_, ns) = Demangle(name.NamespaceUri);
                list.Add((kind, ns, name.LocalName, arity, name, decl, decl.PackageId, decl.OverrideOf));
            }
            foreach (Template t in compiler.NamedTemplates.Values)
            {
                Add('T', t.Name!, -1, t);
            }
            foreach (var ((name, arity), (function, _)) in compiler.Functions)
            {
                Add('F', name, arity, function);
            }
            foreach (VarPar v in compiler.AllGlobalVarPars.Values)
            {
                Add('V', v.Name!, -1, v);
            }
            foreach (AttributeSet a in compiler.AttributeSets.Values)
            {
                Add('A', a.Name!, -1, a);
            }
            var modes = new HashSet<QilName>();
            foreach (ProtoTemplate pt in compiler.AllTemplates)
            {
                if (pt is Template { Match: not null } t)
                {
                    foreach (QilName m in t.V2?.Modes ?? (IEnumerable<QilName>)new[] { t.Mode })
                    {
                        if (m.LocalName.Length != 0 && modes.Add(m))
                        {
                            Add('M', m, -1, t);
                        }
                    }
                }
            }
            foreach (var (m, decl) in compiler.ModeDecls)
            {
                if (m.LocalName.Length != 0 && decl.Declaration != null)
                {
                    list.RemoveAll(c => c.Item1 == 'M' && c.Item5.Equals(m));
                    Add('M', m, -1, decl.Declaration);
                }
            }
            return list;
        }

        private static void Build(Compiler compiler, PackageInfo p, List<(char Kind, string Ns, string Local, int Arity, QilName Actual, XslNode Decl, int Package, int OverrideOf)> components)
        {
            if (p.Built)
            {
                return;
            }
            p.Built = true;
            void Error(ISourceLineInfo? line, string message) => compiler.ReportError(line ?? NoSource, SR.Xml_UserException, message);

            // The package's own components.
            foreach (var c in components.Where(c => c.Package == p.Id && c.OverrideOf < 0))
            {
                string visibility = OwnVisibility(compiler, p, c.Kind, c.Ns, c.Local, c.Arity, c.Decl);
                if (visibility == "abstract" && p.Id == 0 && p.IsPackage)
                {
                    Error(c.Decl.SourceLine, $"XTSE3080: the top-level package contains the abstract component {c.Local}");
                }
                p.Visible[(c.Kind, c.Ns, c.Local, c.Arity)] = new PackageComponent(c.Actual, visibility, c.Decl, p.Id);
            }
            foreach (PackageRule rule in p.Expose)
            {
                foreach (PackageNameTest test in rule.Names.Where(n => !n.IsWildcard))
                {
                    if (rule.Kind == 'F' && test.Arity == null)
                    {
                        // A function is named with its arity (erratum E36).
                        Error(rule.Line, $"XTSE3020: xsl:expose names the function {test.Token} without its arity");
                        continue;
                    }
                    bool any = components.Any(c => c.Package == p.Id && c.OverrideOf < 0 && (rule.Kind == '*' || rule.Kind == c.Kind) && test.Matches(c.Ns, c.Local, c.Arity)
                        && !(c.Kind == 'V' && c.Decl.NodeType == XslNodeType.Param));
                    if (!any)
                    {
                        Error(rule.Line, $"XTSE3020: xsl:expose names {test.Token}, which isn't a component of the package");
                    }
                }
            }

            // The components of the packages it uses, as accepted or overridden.
            var fromUsed = new Dictionary<(char, string, string, int), int>();
            foreach (UsedPackage use in p.Uses)
            {
                PackageInfo q = compiler.PackageInfos[use.Id];
                Build(compiler, q, components);
                // Template rules in xsl:override add to the used package's modes; they don't override the mode.
                var overrides = components.Where(c => c.Package == p.Id && c.OverrideOf == use.Id && c.Kind != 'M').ToList();
                foreach (var o in overrides)
                {
                    // A component named (not by a wildcard) in xsl:accept can't also be overridden (XTSE3051).
                    if (use.Accept.Any(r => (r.Kind == '*' || r.Kind == o.Kind) && r.Names.Any(n => !n.IsWildcard && n.Matches(o.Ns, o.Local, o.Arity))))
                    {
                        Error(o.Decl.SourceLine, $"XTSE3051: {o.Local} is both accepted and overridden");
                        continue;
                    }
                    if (!q.Visible.TryGetValue((o.Kind, o.Ns, o.Local, o.Arity), out PackageComponent? original) ||
                        original.Visibility is "private" or "hidden")
                    {
                        Error(o.Decl.SourceLine, $"XTSE3058: xsl:override declares {o.Local}, which the used package doesn't make visible");
                        continue;
                    }
                    if (original.Visibility == "final")
                    {
                        Error(o.Decl.SourceLine, $"XTSE3060: {o.Local} is final in the used package and can't be overridden");
                        continue;
                    }
                    if (original.Declaration != null && !SameSignature(o.Kind, o.Decl, original.Declaration))
                    {
                        Error(o.Decl.SourceLine, $"XTSE3070: the override of {o.Local} doesn't have the same type signature");
                    }
                    compiler.PackageRedirects[Key(o.Kind, original.Actual, o.Arity)] = o.Actual;
                    compiler.PackageOriginals[o.Decl] = original.Actual;
                    p.Visible[(o.Kind, o.Ns, o.Local, o.Arity)] = new PackageComponent(o.Actual, o.Decl.Visibility ?? "private", o.Decl, p.Id);
                    fromUsed[(o.Kind, o.Ns, o.Local, o.Arity)] = use.Id;
                }
                // Template rules in xsl:override go in a mode of the used package, which must be public (XTSE3440).
                foreach (Template rule in compiler.AllTemplates.OfType<Template>().Where(t => t.Match != null && t.PackageId == p.Id && t.OverrideOf == use.Id))
                {
                    foreach (QilName mode in rule.V2?.Modes ?? (IEnumerable<QilName>)new[] { rule.Mode })
                    {
                        string modeNs = Demangle(mode.NamespaceUri).Ns;
                        if (mode.LocalName.Length == 0 || ReferenceEquals(mode, XsltLoader.CurrentMode) ||
                            !q.Visible.TryGetValue(('M', modeNs, mode.LocalName, -1), out PackageComponent? modeComponent) || modeComponent.Visibility != "public")
                        {
                            Error(rule.SourceLine, $"XTSE3440: the mode {(mode.LocalName.Length == 0 ? "#unnamed" : mode.LocalName)} isn't public in the used package, so xsl:override can't add template rules to it");
                        }
                    }
                }
                foreach (var ((kind, ns, local, arity), component) in q.Visible)
                {
                    if (component.Visibility == "hidden" || overrides.Any(o => o.Kind == kind && o.Ns == ns && o.Local == local && o.Arity == arity))
                    {
                        continue;
                    }
                    bool param = kind == 'V' && component.Declaration?.NodeType == XslNodeType.Param;
                    // A private component can't be given a visibility by name (XTSE3040; a wildcard just doesn't match it).
                    if (!param && component.Visibility == "private" && use.Accept.FirstOrDefault(r => (r.Kind == '*' || r.Kind == kind)
                        && r.Names.Any(n => !n.IsWildcard && n.Matches(ns, local, arity))) is PackageRule named)
                    {
                        Error(named.Line, $"XTSE3040: xsl:accept can't give {local}, private in the used package, visibility {named.Visibility}");
                    }
                    string visibility = param ? "public" : component.Visibility switch
                    {
                        "public" or "final" => "private",
                        _ => "hidden",
                    };
                    // xsl:accept only applies to components the used package makes visible; private ones stay hidden.
                    if (!param && component.Visibility is "public" or "final" or "abstract" && MatchRule(use.Accept, kind, ns, local, arity) is PackageRule accept)
                    {
                        if (!AcceptCompatible(component.Visibility, accept.Visibility))
                        {
                            Error(accept.Line, $"XTSE3040: xsl:accept can't give {local}, {component.Visibility} in the used package, visibility {accept.Visibility}");
                        }
                        else
                        {
                            visibility = accept.Visibility;
                            if (visibility == "abstract" && p.Id == 0)
                            {
                                // The top-level package can't have abstract components, used or not (XTSE3080).
                                Error(accept.Line, $"XTSE3080: the top-level package accepts the abstract component {local}");
                            }
                        }
                    }
                    if (visibility == "hidden")
                    {
                        p.Visible.TryAdd((kind, ns, local, arity), component with { Visibility = "hidden" });
                        continue;
                    }
                    if (p.Visible.TryGetValue((kind, ns, local, arity), out PackageComponent? existing) && existing.Visibility != "hidden")
                    {
                        Error(existing.Declaration?.SourceLine ?? use.Line,
                            $"XTSE3050: {local} is declared in the package and also accepted from a used package");
                        continue;
                    }
                    p.Visible[(kind, ns, local, arity)] = component with { Visibility = visibility };
                    fromUsed[(kind, ns, local, arity)] = use.Id;
                }
                foreach (PackageRule rule in use.Accept)
                {
                    foreach (PackageNameTest test in rule.Names.Where(n => !n.IsWildcard))
                    {
                        if (!q.Visible.Any(e => (rule.Kind == '*' || rule.Kind == e.Key.Kind) && test.Matches(e.Key.Ns, e.Key.Local, e.Key.Arity) && e.Value.Visibility != "hidden"))
                        {
                            Error(rule.Line, $"XTSE3030: xsl:accept names {test.Token}, which the used package doesn't make visible");
                        }
                    }
                }
            }
        }

        // §2.3.2: the initial named template and the initial mode of a principal package must be public or final.
        private static void CheckEntryPoints(Compiler compiler)
        {
            PackageInfo principal = compiler.PackageInfos[0];
            if (!principal.IsPackage)
            {
                return;
            }
            if (compiler.StartApplyTemplates is XslNodeEx { NodeType: XslNodeType.CallTemplate, Name: QilName template } &&
                principal.Visible.TryGetValue(('T', template.NamespaceUri, template.LocalName, -1), out PackageComponent? t) &&
                t.Visibility is not ("public" or "final"))
            {
                compiler.ReportError(t.Declaration?.SourceLine ?? NoSource, SR.Xml_UserException,
                    $"XTDE0040: the initial template {template.LocalName} isn't public");
            }
            // (The mode the package's default-mode names may be private; any other one the caller names must be public.)
            if (compiler.InitialMode != null && compiler.StartApplyTemplates is { NodeType: XslNodeType.ApplyTemplates, Name: QilName mode } && mode.LocalName.Length != 0 && !mode.Equals(compiler.DefaultInitialMode) &&
                principal.Visible.TryGetValue(('M', mode.NamespaceUri, mode.LocalName, -1), out PackageComponent? m) &&
                m.Visibility is not ("public" or "final"))
            {
                compiler.ReportError(m.Declaration?.SourceLine ?? NoSource, SR.Xml_UserException,
                    $"XTDE0044: the initial mode {mode.LocalName} isn't public");
            }
        }

        // §3.5.3.3: an overriding component has the overridden one's type: functions and templates the same
        // declared types of result and parameters (and templates the same parameter names), variables the same type.
        private static bool SameSignature(char kind, XslNode o, XslNode original)
        {
            static string Norm(string? type) => type == null ? "" : string.Concat(type.Where(c => !char.IsWhiteSpace(c)));
            if (kind == 'V' || kind == 'F' || kind == 'T')
            {
                if (Norm(o.V2?.As) != Norm(original.V2?.As))
                {
                    return false;
                }
            }
            if (kind is 'F' or 'T')
            {
                var a = o.Content.OfType<VarPar>().Where(v => v.NodeType == XslNodeType.Param).ToList();
                var b = original.Content.OfType<VarPar>().Where(v => v.NodeType == XslNodeType.Param).ToList();
                if (kind == 'F')
                {
                    // The same parameter types, and the same determinism (new-each-time).
                    return a.Count == b.Count && a.Zip(b).All(x => Norm(x.First.V2?.As) == Norm(x.Second.V2?.As)) &&
                        (o.V2?.NewEachTime ?? "maybe") == (original.V2?.NewEachTime ?? "maybe");
                }
                // Templates: the overridden template's non-tunnel parameters, by name and type; others not required.
                VarPar? Same(VarPar q) => a.FirstOrDefault(x => x.Name!.LocalName == q.Name!.LocalName && Demangle(x.Name.NamespaceUri).Ns == Demangle(q.Name.NamespaceUri).Ns);
                foreach (VarPar q in b.Where(q => !(q.V2?.Tunnel ?? false)))
                {
                    VarPar? match = Same(q);
                    if (match == null || Norm(match.V2?.As) != Norm(q.V2?.As) || (match.V2?.Tunnel ?? false) != (q.V2?.Tunnel ?? false))
                    {
                        return false;
                    }
                }
                // Tunnel parameters both declare: the same type.
                if (b.Where(q => q.V2?.Tunnel ?? false).Any(q => Same(q) is VarPar match && Norm(match.V2?.As) != Norm(q.V2?.As)))
                {
                    return false;
                }
                // The same context item declaration (xsl:context-item use and as).
                static (string, string) Context(XslNode t) => t.Content.FirstOrDefault(n => n.NodeType == XslNodeType.ContextItem) is XslNode c
                    ? ((string?)c.Arg ?? "optional", Norm(c.V2?.As ?? "item()"))
                    : ("optional", "item()");
                if (Context(o) != Context(original))
                {
                    return false;
                }
                return a.All(x => b.Any(q => q.Name!.LocalName == x.Name!.LocalName) || !(x.V2?.Required ?? false));
            }
            return true;
        }

        // §3.5.4.2: in a package with declared-modes="yes", the modes of its template rules and xsl:apply-templates
        // instructions (the unnamed mode too) are declared by xsl:mode or come from a used package (XTSE3085).
        private static void CheckDeclaredModes(Compiler compiler, PackageInfo p)
        {
            if (!p.IsPackage || !p.DeclaredModes)
            {
                return;
            }
            bool Declared(QilName mode)
            {
                if (compiler.ModeDecls.TryGetValue(mode, out ModeDecl? decl) && decl.Declaration?.PackageId == p.Id)
                {
                    return true;
                }
                var (owner, ns) = Demangle(mode.NamespaceUri);
                return mode.LocalName.Length != 0 && (owner != p.Id ||
                    p.Visible.TryGetValue(('M', ns, mode.LocalName, -1), out PackageComponent? seen) && seen.Owner != p.Id && seen.Visibility != "hidden");
            }
            void Check(XslNode node)
            {
                foreach (XslNode child in node.Content)
                {
                    if (child.NodeType == XslNodeType.ApplyTemplates && child.Name is QilName mode && !ReferenceEquals(mode, XsltLoader.CurrentMode) && !Declared(mode))
                    {
                        compiler.ReportError(child.SourceLine ?? NoSource, SR.Xml_UserException,
                            $"XTSE3085: the mode {(mode.LocalName.Length == 0 ? "#unnamed" : mode.LocalName)} isn't declared by xsl:mode");
                    }
                    Check(child);
                }
            }
            foreach (ProtoTemplate pt in compiler.AllTemplates)
            {
                if (pt.PackageId != p.Id || pt.OverrideOf >= 0)
                {
                    continue;
                }
                if (pt is Template { Match: not null } t)
                {
                    foreach (QilName m in t.V2?.Modes ?? (IEnumerable<QilName>)new[] { t.Mode })
                    {
                        if (!Declared(m))
                        {
                            compiler.ReportError(t.SourceLine ?? NoSource, SR.Xml_UserException,
                                $"XTSE3085: the mode {(m.LocalName.Length == 0 ? "#unnamed" : m.LocalName)} isn't declared by xsl:mode");
                        }
                    }
                }
                Check(pt);
            }
        }

        // The visibility of a package's own component (§3.5.3.1): explicit xsl:expose, the declaration, partial
        // wildcards, "*", else private (public in a stylesheet that isn't a package).
        private static string OwnVisibility(Compiler compiler, PackageInfo p, char kind, string ns, string local, int arity, XslNode decl)
        {
            if (kind == 'V' && decl.NodeType == XslNodeType.Param)
            {
                return "public";
            }
            // A mode's declared visibility is its xsl:mode's; a mode used only by template rules has none.
            string? declared = kind == 'M' ? (decl is ModeDeclNode ? decl.Visibility : null) : decl.Visibility;
            if (!p.IsPackage)
            {
                return declared ?? "public";
            }
            PackageRule? explicitRule = p.Expose.LastOrDefault(r => (r.Kind == '*' || r.Kind == kind) && r.Names.Any(n => !n.IsWildcard && n.Matches(ns, local, arity)));
            if (explicitRule != null)
            {
                if (declared != null && !Consistent(explicitRule.Visibility, declared))
                {
                    compiler.ReportError(explicitRule.Line ?? NoSource, SR.Xml_UserException,
                        $"XTSE3010: xsl:expose gives {local} visibility {explicitRule.Visibility}, inconsistent with its declared {declared}");
                }
                return Abstract(compiler, explicitRule, declared);
            }
            if (declared != null)
            {
                return declared;
            }
            PackageRule? partial = p.Expose.LastOrDefault(r => (r.Kind == '*' || r.Kind == kind) && r.Names.Any(n => n.IsWildcard && !n.IsAll && n.Matches(ns, local, arity)));
            if (partial != null)
            {
                return Abstract(compiler, partial, declared);
            }
            PackageRule? all = p.Expose.LastOrDefault(r => (r.Kind == '*' || r.Kind == kind) && r.Names.Any(n => n.IsAll && n.Matches(ns, local, arity)));
            return all != null ? Abstract(compiler, all, declared) : "private";
        }

        private static string Abstract(Compiler compiler, PackageRule rule, string? declared)
        {
            if (rule.Visibility == "abstract" && declared != "abstract")
            {
                compiler.ReportError(rule.Line ?? NoSource, SR.Xml_UserException,
                    "XTSE3025: xsl:expose can't make a component abstract that isn't declared abstract");
            }
            return rule.Visibility;
        }

        // The table of §3.5.3.1: which exposed visibilities a declared visibility allows.
        private static bool Consistent(string exposed, string declared) => (exposed, declared) switch
        {
            ("public", "public") => true,
            ("private", "public" or "private" or "final") => true,
            ("final", "public" or "final") => true,
            ("abstract", "abstract") => true,
            _ => false,
        };

        // §3.5.3.2: which visibilities xsl:accept may give a component of each visibility in the used package.
        private static bool AcceptCompatible(string used, string accepted) => used switch
        {
            "public" => true,
            "final" => accepted is "private" or "final" or "hidden",
            "abstract" => accepted is "abstract" or "hidden",
            _ => accepted == "hidden",
        };

        private static PackageRule? MatchRule(List<PackageRule> rules, char kind, string ns, string local, int arity)
        {
            PackageRule? explicitRule = rules.LastOrDefault(r => (r.Kind == '*' || r.Kind == kind) && r.Names.Any(n => !n.IsWildcard && n.Matches(ns, local, arity)));
            return explicitRule
                ?? rules.LastOrDefault(r => (r.Kind == '*' || r.Kind == kind) && r.Names.Any(n => n.IsWildcard && !n.IsAll && n.Matches(ns, local, arity)))
                ?? rules.LastOrDefault(r => (r.Kind == '*' || r.Kind == kind) && r.Names.Any(n => n.IsAll && n.Matches(ns, local, arity)));
        }

        /// <summary>
        /// The registered name a reference from <paramref name="package"/>'s code binds to: the name itself if the
        /// compilation uses no packages or the package's table doesn't know it, null if it's hidden there, else
        /// the component's registered name, redirected to its override if it has one.
        /// </summary>
        public static QilName? Resolve(Compiler compiler, int package, char kind, QilName name, int arity, bool redirect = true)
        {
            if (compiler.PackageInfos.Count <= 1 || package >= compiler.PackageInfos.Count)
            {
                return name;
            }
            var (owner, plainNs) = Demangle(name.NamespaceUri);
            if (owner > 0 && owner < compiler.PackageInfos.Count)
            {
                // A name already qualified with a package (a mode in that package's code): as that package sees it.
                return compiler.PackageInfos[owner].Visible.TryGetValue((kind, plainNs, name.LocalName, arity), out PackageComponent? seen) && seen.Visibility != "hidden"
                    ? (redirect ? Redirect(compiler, kind, seen.Actual, arity) : seen.Actual) : name;
            }
            if (!compiler.PackageInfos[package].Visible.TryGetValue((kind, name.NamespaceUri, name.LocalName, arity), out PackageComponent? component))
            {
                // A used package can't see the functions of the packages that use it.
                return kind == 'F' && package > 0 ? null : name;
            }
            if (component.Visibility == "hidden")
            {
                return null;
            }
            return redirect ? Redirect(compiler, kind, component.Actual, arity) : component.Actual;
        }

        public static QilName Redirect(Compiler compiler, char kind, QilName actual, int arity)
        {
            for (int i = 0; i < 32 && compiler.PackageRedirects.TryGetValue(Key(kind, actual, arity), out QilName? target); i++)
            {
                actual = target;
            }
            return actual;
        }

        public static bool IsAbstractWithoutOverride(Compiler compiler, char kind, XslNode decl, QilName actual, int arity) =>
            decl.Visibility == "abstract" && !compiler.PackageRedirects.ContainsKey(Key(kind, actual, arity));
    }

    /// <summary>Xsl3: an xsl:mode declaration as a component declaration (for its line and visibility).</summary>
    internal sealed class ModeDeclNode : XslNode
    {
        public ModeDeclNode(QilName name) : base(XslNodeType.Nop, name, null, XslVersion.Current) { }
    }
}
