// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Xsl3.Qil;

namespace Xsl3.Xslt
{
    internal class StylesheetLevel
    {
        public Stylesheet[]? Imports;

        // If (this is Stylesheet) {
        //   ModeFlags and ApplyFunctions are abblout apply-imports
        // } else {
        //   ModeFlags and ApplyFunctions are abblout apply-templates
        // }
        // mode -> FocusFlags; Used to generate and call apply-imports/apply-template functions
        public Dictionary<QilName, XslFlags> ModeFlags = new Dictionary<QilName, XslFlags>();
        // mode -> xsl:apply-import functions for that mode
        public Dictionary<QilName, List<QilFunction>> ApplyFunctions = new Dictionary<QilName, List<QilFunction>>();
    }

    internal sealed class Stylesheet : StylesheetLevel
    {
        private readonly Compiler _compiler;
        public List<Uri> ImportHrefs = new List<Uri>();

        // Xsl3: the declarations seen so far at this import precedence (includes too), and the count when each
        // xsl:import was seen: the tree order of static variable declarations (XTSE3450).
        public int Declarations;
        public List<int> ImportPositions = new List<int>();
        public List<XslNode>? GlobalVarPars = new List<XslNode>();

        // xsl:attribute-set/@name -> AttributeSet
        public Dictionary<QilName, AttributeSet>? AttributeSets = new Dictionary<QilName, AttributeSet>();

        private readonly int _importPrecedence;
        private int _orderNumber;

        /*
            WhitespaceRules[0] - rules with default priority  0
            WhitespaceRules[1] - rules with default priority -0.25
            WhitespaceRules[2] - rules with default priority -0.5
        */
        public List<WhitespaceRule>[]? WhitespaceRules = new List<WhitespaceRule>[3];

        public List<Template> Templates = new List<Template>();  // Templates defined on this level. Empty for RootLevel.
        // xsl:template/@mode -> list of @match'es
        public Dictionary<QilName, List<TemplateMatch>> TemplateMatches = new Dictionary<QilName, List<TemplateMatch>>();

        // Xsl3: template rules with mode="#all" (2.0), merged into every mode by MatcherBuilder.
        public List<TemplateMatch> AllModesMatches = new List<TemplateMatch>();

        /// <param name="mode">For a template rule of several modes (Xsl3), the one this filter is for: each mode
        /// gets its own filter, since a TemplateMatch takes its filter apart.</param>
        public TemplateMatch AddTemplateMatch(Template template, QilLoop filter, QilName? mode = null)
        {
            var match = new TemplateMatch(template, filter);
            if (template.V2 is XslNodeV2 v2 && (v2.AllModes || v2.Modes != null))
            {
                if (v2.AllModes)
                {
                    AllModesMatches.Add(match);
                    return match;
                }
                QilName m = mode ?? v2.Modes![0];
                if (!TemplateMatches.TryGetValue(m, out List<TemplateMatch>? list))
                {
                    list = TemplateMatches[m] = new List<TemplateMatch>();
                }
                list.Add(match);
                return match;
            }
            List<TemplateMatch>? matchesForMode;
            if (!TemplateMatches.TryGetValue(template.Mode, out matchesForMode))
            {
                matchesForMode = TemplateMatches[template.Mode] = new List<TemplateMatch>();
            }
            matchesForMode.Add(match);
            return match;
        }

        public void SortTemplateMatches()
        {
            foreach (QilName mode in TemplateMatches.Keys)
            {
                TemplateMatches[mode].Sort(TemplateMatch.Comparer);
            }
            AllModesMatches.Sort(TemplateMatch.Comparer);
        }

        public Stylesheet(Compiler compiler, int importPrecedence)
        {
            _compiler = compiler;
            _importPrecedence = importPrecedence;

            WhitespaceRules![0] = new List<WhitespaceRule>();
            WhitespaceRules![1] = new List<WhitespaceRule>();
            WhitespaceRules![2] = new List<WhitespaceRule>();
        }

        public int ImportPrecedence { get { return _importPrecedence; } }

        public void AddWhitespaceRule(int index, WhitespaceRule rule)
        {
            WhitespaceRules![index].Add(rule);
        }

        public bool AddVarPar(VarPar var)
        {
            Debug.Assert(var.NodeType == XslNodeType.Variable || var.NodeType == XslNodeType.Param);
            Debug.Assert(var.Name!.NamespaceUri != null, "Name must be resolved in XsltLoader");
            foreach (XslNode prevVar in GlobalVarPars!)
            {
                if (prevVar.Name!.Equals(var.Name))
                {
                    // [ERR XT0630] It is a static error if a stylesheet contains more than one binding
                    // of a global variable with the same name and same import precedence, unless it also
                    // contains another binding with the same name and higher import precedence.
                    return _compiler.AllGlobalVarPars.ContainsKey(var.Name);
                }
            }
            GlobalVarPars.Add(var);
            return true;
        }

        public bool AddTemplate(Template template)
        {
            Debug.Assert(template.ImportPrecedence == 0);

            template.ImportPrecedence = _importPrecedence;
            template.OrderNumber = _orderNumber++;

            _compiler.AllTemplates.Add(template);

            if (template.Name != null)
            {
                Template? old;
                if (!_compiler.NamedTemplates.TryGetValue(template.Name, out old))
                {
                    _compiler.NamedTemplates[template.Name] = template;
                }
                else
                {
                    Debug.Assert(template.ImportPrecedence <= old.ImportPrecedence, "Global objects are processed in order of decreasing import precedence");
                    if (old.ImportPrecedence == template.ImportPrecedence)
                    {
                        return false;
                    }
                }
            }

            if (template.Match != null)
            {
                Templates.Add(template);
            }
            return true;
        }
    }
}
