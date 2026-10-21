// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Xml.XPath;
using Xsl3.Xdm;
using Xsl3.Xslt;
using Xunit;

namespace Xsl3.Tests;

/// <summary>XSLT 3.0 instructions and functions (docs/plan/p3-xslt30.md).</summary>
public class Xslt30Tests
{
    private const string Xsl = "http://www.w3.org/1999/XSL/Transform";

    private static string Run(string templates, string input = "<doc/>", Dictionary<XmlQualifiedName, IList<XPathItem>>? staticParameters = null)
    {
        string stylesheet = $"<xsl:stylesheet version='3.0' xmlns:xsl='{Xsl}' xmlns:xs='http://www.w3.org/2001/XMLSchema' " +
                            "xmlns:err='http://www.w3.org/2005/xqt-errors' exclude-result-prefixes='xs err'>" +
                            "<xsl:output method='xml' omit-xml-declaration='yes'/>" + templates + "</xsl:stylesheet>";
        var transform = new XslCompiledTransform { StaticParameters = staticParameters };
        transform.Load(XmlReader.Create(new StringReader(stylesheet)));
        var sw = new StringWriter();
        using (XmlWriter w = XmlWriter.Create(sw, transform.OutputSettings))
        {
            transform.Transform(XmlReader.Create(new StringReader(input)), w);
        }
        return sw.ToString();
    }

    /// <summary>Map and array values keep the nodes they were given, not the caller's moving iterator.</summary>
    [Fact]
    public void MapEntriesKeepTheirNodes() =>
        Assert.Equal("<o>A B C|A B C|B</o>", Run("""
            <xsl:template match="/r" xmlns:map="http://www.w3.org/2005/xpath-functions/map" xmlns:array="http://www.w3.org/2005/xpath-functions/array" exclude-result-prefixes="#all">
              <xsl:variable name="m" select="map:merge(p ! map:entry(string(@k), .))"/>
              <xsl:variable name="a" select="fold-left(p, [], function($a, $x) { array:append($a, $x) })"/>
              <o><xsl:value-of select="$m('a'), $m('b'), $m('c')"/>|<xsl:value-of select="$a?*"/>|<xsl:value-of select="map:put(map{}, 'k', p[2])?k"/></o>
            </xsl:template>
            """, "<r><p k='a'>A</p><p k='b'>B</p><p k='c'>C</p></r>"));

    /// <summary>
    /// Native values kept by maps and arrays: the generated code wraps a native value in a list it reuses, which a
    /// map or array has to copy.
    /// </summary>
    [Fact]
    public void MapEntriesKeepTheirNativeValues() =>
        Assert.Equal("<o>1 2 1|a b b c|2 1</o>", Run("""
            <xsl:template match="/r" xmlns:map="http://www.w3.org/2005/xpath-functions/map" xmlns:array="http://www.w3.org/2005/xpath-functions/array" exclude-result-prefixes="#all">
              <xsl:variable name="e" select="for $k in distinct-values(p/@k) return map:entry($k, count(p[@k = $k]))"/>
              <xsl:variable name="a" select="fold-left(p, [], function($a, $x) { array:append($a, upper-case($x/@k)) })"/>
              <xsl:variable name="b" select="for $k in ('b', 'c') return array:append([], count(p[@k = $k]))"/>
              <o><xsl:value-of select="$e ! ?*"/>|<xsl:value-of select="lower-case(string-join($a?*, ' '))"/>|<xsl:value-of select="$b ! ?*"/></o>
            </xsl:template>
            """, "<r><p k='a'/><p k='b'/><p k='b'/><p k='c'/></r>"));

    /// <summary>
    /// group-by with string-like keys of mixed types (strings, untyped values, URIs equal when their strings are),
    /// several keys for an item, and keys of other types, which equal no string.
    /// </summary>
    [Fact]
    public void GroupByMixedKeys() =>
        Assert.Equal("<o>a:1 2 3 5|b:3 4|1:6|c:6</o>", Run("""
            <xsl:template match="/r" xmlns:xs="http://www.w3.org/2001/XMLSchema">
              <o><xsl:for-each-group select="p" group-by="if (position() = 6) then (1, 'c') else if (position() = 3) then ('b', xs:anyURI('a'), @k) else if (position() mod 2 = 0) then string(@k) else @k">
                <xsl:if test="position() > 1">|</xsl:if><xsl:value-of select="current-grouping-key(), ':'" separator=""/><xsl:value-of select="current-group()/@n"/>
              </xsl:for-each-group></o>
            </xsl:template>
            """, "<r><p k='a' n='1'/><p k='a' n='2'/><p k='a' n='3'/><p k='b' n='4'/><p k='a' n='5'/><p k='z' n='6'/></r>"));

    /// <summary>
    /// A map read many times (P4: it then gets a hash table): numeric keys equal across types, strings, missing keys,
    /// and the map updated after the reads.
    /// </summary>
    [Fact]
    public void MapReadManyTimes() =>
        Assert.Equal("<o>840 true false 40 x|y 0 23</o>", Run("""
            <xsl:template match="/" xmlns:map="http://www.w3.org/2005/xpath-functions/map" xmlns:xs="http://www.w3.org/2001/XMLSchema" exclude-result-prefixes="#all">
              <xsl:variable name="m" select="map:merge(((1 to 20) ! map:entry(., . * 2), map:entry('x', 'x'), map:entry(xs:untypedAtomic('y'), 'y')))"/>
              <xsl:variable name="m2" select="map:put($m, 21, 0)"/>
              <o><xsl:value-of select="sum((1 to 40) ! ($m(.), $m(xs:double(.)), $m(xs:decimal(.)) div 2) ! .) - sum((1 to 20) ! .),
                map:contains($m, 20.0), map:contains($m, 21), $m(20), concat($m('x'), '|', $m('y')), $m2(21), map:size($m2)"/></o>
            </xsl:template>
            """, "<r/>"));

    /// <summary>
    /// Sort keys of number(...) (P4: native doubles): NaN first (last descending), -0 equal to 0 (stable), and as
    /// the general encoding orders them with data-type="number".
    /// </summary>
    [Fact]
    public void NumberSortKeys() =>
        Assert.Equal("<o>x NaN -1 -0 0 2 10|10 2 -0 0 -1 x NaN|10 2 -0 0 -1 x NaN</o>", Run("""
            <xsl:template match="/r">
              <o><xsl:for-each select="p"><xsl:sort select="number(.)"/><xsl:value-of select="if (position() > 1) then ' ' else '', ." separator=""/></xsl:for-each>|<xsl:for-each select="p"><xsl:sort select="number(.)" order="descending"/><xsl:value-of select="if (position() > 1) then ' ' else '', ." separator=""/></xsl:for-each>|<xsl:for-each select="p"><xsl:sort select="." data-type="number" order="descending"/><xsl:value-of select="if (position() > 1) then ' ' else '', ." separator=""/></xsl:for-each></o>
            </xsl:template>
            """, "<r><p>x</p><p>10</p><p>NaN</p><p>-0</p><p>2</p><p>0</p><p>-1</p></r>"));

    /// <summary>
    /// Steps from current-group() (P4: for a population of child steps, its members are in document order at one
    /// depth, and a step from them needs no merge): child and attribute steps, a further step, and a population
    /// that isn't child steps.
    /// </summary>
    [Fact]
    public void StepsFromCurrentGroup() =>
        Assert.Equal("<o>a:1 3|b:2|a:x z|b:y|a:3|b:2|a:1 3 4</o>", Run("""
            <xsl:template match="/r">
              <o><xsl:for-each-group select="g/p" group-by="@k"><xsl:value-of select="current-grouping-key(), ':', string-join(current-group()/v, ' '), '|'" separator=""/></xsl:for-each-group>
              <xsl:for-each-group select="g/p" group-by="@k"><xsl:value-of select="current-grouping-key(), ':', string-join(current-group()/@n, ' '), '|'" separator=""/></xsl:for-each-group>
              <xsl:for-each-group select="g/p" group-by="@k"><xsl:value-of select="current-grouping-key(), ':', sum(current-group()/w/u), '|'" separator=""/></xsl:for-each-group>
              <xsl:for-each-group select="//p | //q" group-by="@k"><xsl:if test="@k = 'a'"><xsl:value-of select="current-grouping-key(), ':', string-join(current-group()/v, ' ')" separator=""/></xsl:if></xsl:for-each-group></o>
            </xsl:template>
            """, "<r><g><p k='a' n='x'><v>1</v><w><u>1</u><u>2</u></w></p><p k='b' n='y'><v>2</v><w><u>2</u></w></p></g><g><p k='a' n='z'><v>3</v></p><q k='a'><v>4</v></q></g></r>"));

    /// <summary>Value templates and string functions over zero, one or several nodes (the native-string paths).</summary>
    [Fact]
    public void ValueTemplatesOfNodes() =>
        Assert.Equal("<o all=\"A B C\" one=\"B\" none=\"\" up=\"B\" e=\"\" n=\"7\" s=\"3\">A|B C|</o>", Run("""
            <xsl:template match="/r">
              <o all="{p}" one="{p[2]}" none="{q}" up="{upper-case(p[2]/@k)}" e="{upper-case(q/@k)}" n="{string-length(concat(p[1], q, 'xyzabc'))}" s="{count(p)}" xsl:expand-text="yes">{p[1]}|{p[2], p[3]}|{q}</o>
            </xsl:template>
            """, "<r><p k='a'>A</p><p k='b'>B</p><p k='c'>C</p></r>"));

    /// <summary>A variable used once inside a loop is computed once, not for every iteration (the result is the same).</summary>
    [Fact]
    public void VariableUsedInALoopIsComputedOnce() =>
        Assert.Equal("<o>a=1 b=2 c=1</o>", Run("""
            <xsl:template match="/r" xmlns:map="http://www.w3.org/2005/xpath-functions/map" exclude-result-prefixes="#all">
              <xsl:variable name="counts" select="map:merge(for $k in distinct-values(p/@k) return map:entry($k, count(p[@k = $k])))"/>
              <o><xsl:value-of select="for $k in ('a', 'b', 'c') return $k || '=' || $counts($k)"/></o>
            </xsl:template>
            """, "<r><p k='a'/><p k='b'/><p k='b'/><p k='c'/></r>"));

    [Fact]
    public void TryCatchesDynamicErrorsWithTheirCode() =>
        Assert.Equal("<out><a v=\"-10\" /><caught pos=\"2\" code=\"err:FOAR0001\" /><a v=\"10\" /></out>", Run(
            "<xsl:template match='/'><out><xsl:for-each select='1 to 3'><xsl:try><a v='{10 idiv (. - 2)}'/>" +
            "<xsl:catch errors='err:FOAR0001'><caught pos='{position()}' code='{$err:code}'/></xsl:catch></xsl:try></xsl:for-each></out></xsl:template>"));

    [Fact]
    public void TryChoosesTheCatchMatchingTheErrorAndSeesOuterVariables() =>
        Assert.Equal("<out>3.14 boom</out>", Run(
            "<xsl:template match='/'><xsl:variable name='pi' select='3.14'/><out><xsl:try select=\"error(QName('urn:x', 'x:E'), 'boom')\">" +
            "<xsl:catch errors='err:*' select=\"'wrong'\"/><xsl:catch errors='*:E' select='$pi, $err:description'/></xsl:try></out></xsl:template>"));

    [Fact]
    public void ErrorsNotMatchedByAnyCatchPropagate() =>
        Assert.ThrowsAny<System.Exception>(() => Run(
            "<xsl:template match='/'><xsl:try select='1 idiv 0'><xsl:catch errors='err:XPTY0004'/></xsl:try></xsl:template>"));

    [Fact]
    public void ApplyTemplatesToAtomicValuesWithPredicatePatterns() =>
        Assert.Equal("<out><i>1</i><d>1.5</d>x</out>", Run(
            "<xsl:template match='/'><out><xsl:apply-templates select=\"1, 1.5, 'x'\"/></out></xsl:template>" +
            "<xsl:template match='.[. instance of xs:integer]' priority='2'><i><xsl:value-of select='.'/></i></xsl:template>" +
            "<xsl:template match='.[. instance of xs:decimal]'><d><xsl:value-of select='.'/></d></xsl:template>"));

    [Fact]
    public void PatternsWithParenthesizedUnionSteps() =>
        Assert.Equal("<out>A B x</out>", Run(
            "<xsl:template match='/'><out><xsl:apply-templates select='//x/*'/></out></xsl:template>" +
            "<xsl:template match='x/(a|b)'><xsl:value-of select='upper-case(name())'/><xsl:text> </xsl:text></xsl:template>" +
            "<xsl:template match='*'><xsl:value-of select='name()'/></xsl:template>",
            "<doc><x><a/><b/><x/></x></doc>"));

    [Fact]
    public void ExceptPatternsTakeTheDifferenceForEachContextNode() =>
        // x/(descendant::a except child::a): the a elements below some x that aren't its children.
        Assert.Equal("<x><aa /><x><AA /></x></x>", Run(
            "<xsl:mode on-no-match='shallow-copy'/>" +
            "<xsl:template match='x/(descendant::a except child::a)' priority='2'><AA/></xsl:template>" +
            "<xsl:template match='a'><aa/></xsl:template>",
            "<x><a/><x><a/></x></x>"));

    [Fact]
    public void ErrorsInPredicatePatternsAreNonMatches() =>
        Assert.Equal("<out>[1][x]</out>", Run(
            "<xsl:template match='/'><out><xsl:apply-templates select=\"1, 'x'\"/></out></xsl:template>" +
            "<xsl:template match=\".[. eq 'x']\" priority='2'>[x]</xsl:template>" +
            "<xsl:template match='.[. instance of xs:integer]'>[<xsl:value-of select='.'/>]</xsl:template>"));

    [Fact]
    public void CurrentModeInNamedTemplatesAndRulesOfSeveralModes() =>
        Assert.Equal("<out><x>X:s</x><y>Y:s</y></out>", Run(
            "<xsl:template match='/'><out><x><xsl:apply-templates select='doc' mode='X'/></x><y><xsl:apply-templates select='doc' mode='Y'/></y></out></xsl:template>" +
            "<xsl:template match='doc' mode='X Y'><xsl:call-template name='inner'/></xsl:template>" +
            "<xsl:template name='inner'><xsl:apply-templates select='s' mode='#current'/></xsl:template>" +
            "<xsl:template match='s' mode='X'>X:s</xsl:template>" +
            "<xsl:template match='s' mode='Y'>Y:s</xsl:template>",
            "<doc><s/></doc>"));

    [Fact]
    public void NextMatchFromAnAllModesRuleStaysInTheMode() =>
        Assert.Equal("<out><m1>[all]m1</m1><m2>[all]m2</m2></out>", Run(
            "<xsl:template match='/'><out><m1><xsl:apply-templates select='doc' mode='m1'/></m1><m2><xsl:apply-templates select='doc' mode='m2'/></m2></out></xsl:template>" +
            "<xsl:template match='doc' mode='#all' priority='3'>[all]<xsl:next-match/></xsl:template>" +
            "<xsl:template match='doc' mode='m1'>m1</xsl:template>" +
            "<xsl:template match='doc' mode='m2'>m2</xsl:template>"));

    [Fact]
    public void NextMatchInANamedTemplateUsesTheCallersRule() =>
        Assert.Equal("<out>[high]<low /></out>", Run(
            "<xsl:template match='/'><out><xsl:apply-templates select='doc'/></out></xsl:template>" +
            "<xsl:template match='doc' priority='2'>[high]<xsl:call-template name='inner'/></xsl:template>" +
            "<xsl:template name='inner'><xsl:next-match/></xsl:template>" +
            "<xsl:template match='doc' priority='1'><low/></xsl:template>"));

    [Fact]
    public void EmptyTextNodesSeparateAtomicValues() =>
        // <xsl:text/> is a zero-length text node: the atomic values either side aren't adjacent (XSLT 3.0 §5.7.1).
        Assert.Equal("<out n=\"3\">ab</out>", Run(
            "<xsl:template match='/'><xsl:variable name='v' as='item()*'><xsl:sequence select=\"'a'\"/><xsl:text/><xsl:sequence select=\"'b'\"/></xsl:variable>" +
            "<out n='{count($v)}'><xsl:sequence select=\"'a'\"/><xsl:text/><xsl:sequence select=\"'b'\"/></out></xsl:template>"));

    [Fact]
    public void SnapshotOfAnAttributeKeepsItsParent() =>
        Assert.Equal("<out>k true b</out>", Run(
            "<xsl:template match='/'><out><xsl:variable name='s' select='snapshot(/a/b/@k)'/>" +
            "<xsl:value-of select='name($s), $s instance of attribute(), name($s/..)'/></out></xsl:template>",
            "<a><b k='1'/></a>"));

    [Fact]
    public void ApplyTemplatesToAnArrayProcessesItsMembers() =>
        Assert.Equal("<out><e n=\"a\" /><e n=\"b\" /></out>", Run(
            "<xsl:template match='/'><xsl:variable name='d' as='element()*'><a/><b/></xsl:variable><out><xsl:apply-templates select='array{$d}'/></out></xsl:template>" +
            "<xsl:template match='*'><e n='{name()}'/></xsl:template>"));

    [Fact]
    public void ReferencesToStylesheetFunctionsHaveTheirDeclaredSignature() =>
        Assert.Equal("<out>true false</out>", Run(
            "<xsl:function name='f:f' xmlns:f='urn:f' as='xs:string'><xsl:param name='x' as='xs:integer'/><xsl:sequence select='string($x)'/></xsl:function>" +
            "<xsl:template match='/'><out xmlns:f='urn:f' xsl:exclude-result-prefixes='f'><xsl:value-of select='f:f#1 instance of function(xs:integer) as xs:string, f:f#1 instance of function(xs:string) as xs:string'/></out></xsl:template>"));

    [Fact]
    public void ArraysInContentAreFlattened() =>
        Assert.Equal("<out><e /><e /></out>", Run(
            "<xsl:template match='/'><xsl:variable name='e'><e/></xsl:variable><out><xsl:sequence select='[$e/e, $e/e]'/></out></xsl:template>"));

    [Fact]
    public void CopyOfADocumentNodeInASequenceMakesADocumentNode() =>
        Assert.Equal("<out n=\"2\" />", Run(
            "<xsl:template match='/'><xsl:variable name='docs' as='document-node()*'>" +
            "<xsl:copy><a/></xsl:copy><xsl:copy><b/></xsl:copy></xsl:variable><out n='{count($docs)}'/></xsl:template>"));

    [Fact]
    public void GroupStartingWithPredicatePatternOnAtomicValues() =>
        Assert.Equal("<out><g>1 2</g><g>3 4 5</g><g>6 7</g></out>", Run(
            "<xsl:template match='/'><out><xsl:for-each-group select='1 to 7' group-starting-with='.[. mod 3 = 0]'>" +
            "<g><xsl:value-of select='current-group()'/></g></xsl:for-each-group></out></xsl:template>"));

    [Fact]
    public void NextMatchWithAtomicContextItems() =>
        Assert.Equal("<out><a><b>7</b></a></out>", Run(
            "<xsl:template match='/'><out><xsl:apply-templates select='7'/></out></xsl:template>" +
            "<xsl:template match='.[. instance of xs:integer]' priority='3'><a><xsl:next-match/></a></xsl:template>" +
            "<xsl:template match='.'><b><xsl:value-of select='.'/></b></xsl:template>"));

    [Fact]
    public void SnapshotKeepsAncestors() =>
        Assert.Equal("<out>c b a</out>", Run(
            "<xsl:template match='/'><out><xsl:value-of select='snapshot(//c)/ancestor-or-self::*/name() => reverse()'/></out></xsl:template>",
            "<a><b><c/></b><z/></a>"));

    [Fact]
    public void CopyOfMakesNewNodes() =>
        Assert.Equal("<out>false 1</out>", Run(
            "<xsl:template match='/'><out><xsl:value-of select='copy-of(/a) is /a, count(copy-of(/a)/b)'/></out></xsl:template>",
            "<a><b/></a>"));

    [Fact]
    public void StaticParametersFeedUseWhenAndShadowAttributes() =>
        Assert.Equal("<out>on:3</out>", Run(
            "<xsl:param name='mode' static='yes' as='xs:string' select=\"'off'\"/><xsl:variable name='n' static='yes' select='3'/>" +
            "<xsl:template match='/'><out><xsl:value-of use-when=\"$mode = 'on'\" _select=\"'{$mode}:{$n}'\"/></out></xsl:template>",
            staticParameters: new() { [new XmlQualifiedName("mode")] = new XPathItem[] { AtomicValue.OfString("on") } }));

    [Fact]
    public void OnEmptyAndOnNonEmpty() =>
        Assert.Equal("<out><x>none</x><y><h />1</y></out>", Run(
            "<xsl:template match='/'><out><x><xsl:sequence select='()'/><xsl:on-empty>none</xsl:on-empty></x>" +
            "<y><xsl:on-non-empty><h/></xsl:on-non-empty><xsl:sequence select='1'/><xsl:on-empty>none</xsl:on-empty></y></out></xsl:template>"));

    [Fact]
    public void WherePopulatedDropsEmptyElements() =>
        Assert.Equal("<out><b>1</b></out>", Run(
            "<xsl:template match='/'><out><xsl:where-populated><a/><b>1</b></xsl:where-populated></out></xsl:template>"));

    [Fact]
    public void IterateWithParametersBreakAndCompletion() =>
        Assert.Equal("<out><i p=\"1\">1</i><i p=\"2\">3</i><stop sum=\"3\" /></out><out>2 4 6</out>", Run(
            "<xsl:template match='/'><out><xsl:iterate select='1 to 10'><xsl:param name='sum' as='xs:integer' select='0'/>" +
            "<xsl:on-completion><total/></xsl:on-completion>" +
            "<xsl:choose><xsl:when test='. gt 2'><xsl:break><stop sum='{$sum}'/></xsl:break></xsl:when>" +
            "<xsl:otherwise><i p='{position()}'><xsl:value-of select='. + $sum'/></i>" +
            "<xsl:next-iteration><xsl:with-param name='sum' select='$sum + .'/></xsl:next-iteration></xsl:otherwise></xsl:choose></xsl:iterate></out>" +
            "<out><xsl:iterate select='1 to 3'><xsl:param name='acc' select='()'/><xsl:on-completion select='$acc'/>" +
            "<xsl:next-iteration><xsl:with-param name='acc' select='$acc, . * 2'/></xsl:next-iteration></xsl:iterate></out></xsl:template>"));

    [Fact]
    public void NextIterationMustBeInATailPosition() =>
        Assert.ThrowsAny<System.Exception>(() => Run(
            "<xsl:template match='/'><xsl:iterate select='1 to 3'><xsl:if test='. = 2'><xsl:break/></xsl:if><x/></xsl:iterate></xsl:template>"));

    [Fact]
    public void CopySelectCopiesTheSelectedItem() =>
        Assert.Equal("<out><b x=\"1\">y</b></out>", Run(
            "<xsl:template match='/'><out><xsl:copy select='/a/b'><xsl:copy-of select='@x'/>y</xsl:copy></out></xsl:template>",
            "<a><b x='1'><c/></b></a>"));

    [Fact]
    public void DefaultModeAppliesToTemplatesAndApplyTemplates() =>
        Assert.Equal("<m>x</m>", Run(
            "<xsl:template match='/' mode='#unnamed'><xsl:apply-templates select='*' default-mode='m'/></xsl:template>" +
            "<xsl:template match='a' default-mode='m'><m><xsl:value-of select='.'/></m></xsl:template>" +
            "<xsl:template match='a'>wrong</xsl:template>",
            "<a>x</a>"));

    [Fact]
    public void SequenceWithContent() =>
        Assert.Equal("<out>1|2</out>", Run(
            "<xsl:template match='/'><out><xsl:sequence><xsl:value-of select='1'/><xsl:text>|</xsl:text><xsl:value-of select='2'/></xsl:sequence></out></xsl:template>"));

    [Fact]
    public void AnalyzeStringRegexMayMatchTheEmptyStringIn30() =>
        // The CSV example of XSLT 3.0 §15.1: an empty field is a zero-length match.
        Assert.Equal("<out>[a][][b]</out>", Run(
            "<xsl:template match='/'><out><xsl:analyze-string select=\"'a,,b'\" regex='(?:^|,)([^,]*)'>" +
            "<xsl:matching-substring>[<xsl:value-of select='regex-group(1)'/>]</xsl:matching-substring></xsl:analyze-string></out></xsl:template>"));

    [Fact]
    public void CurrentOutputUriIsAbsentInFunctions() =>
        Assert.Equal("<out f=\"true\" />", Run(
            "<xsl:function name='f:uri' xmlns:f='urn:f'><xsl:sequence select='empty(current-output-uri())'/></xsl:function>" +
            "<xsl:template match='/'><out f='{f:uri()}' xmlns:f='urn:f' xsl:exclude-result-prefixes='f'/></xsl:template>"));

    [Fact]
    public void IndentationWithAnXmlDeclaration()
    {
        string stylesheet = $"<xsl:stylesheet version='3.0' xmlns:xsl='{Xsl}'><xsl:output method='xml' indent='yes'/>" +
                            "<xsl:template match='/'><a><b/></a></xsl:template></xsl:stylesheet>";
        var transform = new XslCompiledTransform();
        transform.Load(XmlReader.Create(new StringReader(stylesheet)));
        var bytes = new MemoryStream();
        transform.Transform(XmlReader.Create(new StringReader("<doc/>")), null, bytes);
        string xml = System.Text.Encoding.UTF8.GetString(bytes.ToArray());
        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"UTF-8\"?>", xml);
        Assert.Contains("<a>" + System.Environment.NewLine + "  <b />", xml);
    }

    /// <summary>
    /// A 2.0+ stylesheet without xsl:result-document writes its principal result with a raw writer directly (P4); the
    /// bytes are those of the principal-result writer, which the same stylesheet with an (unused) xsl:result-document
    /// takes.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("<xsl:output encoding='utf-8'/>")]
    [InlineData("<xsl:output encoding='iso-8859-1' indent='yes'/>")]
    [InlineData("<xsl:output encoding='UTF-16' standalone='yes'/>")]
    [InlineData("<xsl:output omit-xml-declaration='yes' doctype-system='a.dtd' cdata-section-elements='b'/>")]
    [InlineData("<xsl:output byte-order-mark='yes' doctype-public='-//A//EN' doctype-system='a.dtd'/>")]
    public void DirectPrincipalResultMatchesThePrincipalResultWriter(string output)
    {
        byte[] Run(string extra)
        {
            string stylesheet = $"<xsl:stylesheet version='3.0' xmlns:xsl='{Xsl}' xmlns:p='urn:p'>{output}" +
                                "<xsl:template match='/'><a x='1' p:y='&lt;&amp;&quot;'><b>café € &lt; &#x1F600;</b><p:c/><!--c--><?pi d?></a></xsl:template>" +
                                extra + "</xsl:stylesheet>";
            var transform = new XslCompiledTransform();
            transform.Load(XmlReader.Create(new StringReader(stylesheet)));
            var bytes = new MemoryStream();
            transform.Transform(XmlReader.Create(new StringReader("<doc/>")), null, bytes);
            return bytes.ToArray();
        }
        byte[] direct = Run("");
        byte[] principal = Run("<xsl:template name='unused'><xsl:result-document href='x.xml'><x/></xsl:result-document></xsl:template>");
        Assert.Equal(System.Convert.ToHexString(principal), System.Convert.ToHexString(direct));
    }

    [Fact]
    public void XhtmlHtml5VoidElementsAndPrefixNormalization()
    {
        string stylesheet = $"<xsl:stylesheet version='3.0' xmlns:xsl='{Xsl}'><xsl:output method='xhtml' html-version='5' indent='no' omit-xml-declaration='yes'/>" +
                            "<xsl:template match='/'><h:html xmlns:h='http://www.w3.org/1999/xhtml'><h:body><h:br/></h:body></h:html></xsl:template></xsl:stylesheet>";
        var transform = new XslCompiledTransform();
        transform.Load(XmlReader.Create(new StringReader(stylesheet)));
        var sw = new StringWriter();
        transform.Transform(XmlReader.Create(new StringReader("<doc/>")), null, sw);
        Assert.Equal("<!DOCTYPE html><html xmlns=\"http://www.w3.org/1999/xhtml\"><body><br /></body></html>", sw.ToString());
    }

    [Fact]
    public void Html5Serialization()
    {
        string stylesheet = $"<xsl:stylesheet version='3.0' xmlns:xsl='{Xsl}'><xsl:output method='html' html-version='5' indent='no'/>" +
                            "<xsl:template match='/'><html><body><wbr/><br/><p/></body></html></xsl:template></xsl:stylesheet>";
        var transform = new XslCompiledTransform();
        transform.Load(XmlReader.Create(new StringReader(stylesheet)));
        var sw = new StringWriter();
        transform.Transform(XmlReader.Create(new StringReader("<doc/>")), null, sw);
        string html = sw.ToString();
        Assert.StartsWith("<!DOCTYPE html>", html);
        Assert.Contains("<br><p></p>", html);
    }

    [Fact]
    public void WhitespaceIsStrippedFromLoadedDocuments()
    {
        string stylesheet = $"<xsl:stylesheet version='3.0' xmlns:xsl='{Xsl}'><xsl:strip-space elements='*'/><xsl:output method='text'/>" +
                            "<xsl:template match='/'><xsl:value-of select='count(/a/node())'/></xsl:template></xsl:stylesheet>";
        var transform = new XslCompiledTransform();
        transform.Load(XmlReader.Create(new StringReader(stylesheet)));
        var doc = new System.Xml.XPath.XPathDocument(new StringReader("<a> <b/> <c/> </a>"));
        var sw = new StringWriter();
        transform.Transform(doc, null, sw);
        Assert.Equal("2", sw.ToString());
    }

    private static string RunText(string stylesheetBody, string input = "<doc/>")
    {
        string stylesheet = $"<xsl:stylesheet version='3.0' xmlns:xsl='{Xsl}' xmlns:xs='http://www.w3.org/2001/XMLSchema' exclude-result-prefixes='xs'>{stylesheetBody}</xsl:stylesheet>";
        var transform = new XslCompiledTransform();
        transform.Load(XmlReader.Create(new StringReader(stylesheet)));
        var sw = new StringWriter();
        transform.Transform(XmlReader.Create(new StringReader(input)), null, sw);
        return sw.ToString();
    }

    [Fact]
    public void JsonOutputOfMapsAndArrays() =>
        Assert.Equal("{\"a\":[1,\"x\\/y\"]}", RunText(
            "<xsl:output method='json'/><xsl:template match='/'><xsl:map><xsl:map-entry key=\"'a'\" select=\"[1, 'x/y']\"/></xsl:map></xsl:template>"));

    [Fact]
    public void AdaptiveOutput() =>
        // A node is written by the xml method, with an XML declaration unless omit-xml-declaration="yes" (Serialization 3.1 §10.1.6).
        Assert.Equal("\"a\"\n1\ntrue()\n<?xml version=\"1.0\" encoding=\"UTF-8\"?><e/>", RunText(
            "<xsl:output method='adaptive'/><xsl:template match='/'><xsl:sequence select=\"'a', 1, true()\"/><e/></xsl:template>"));

    [Fact]
    public void SerializeWithAMapOfParameters() =>
        Assert.Equal("<out>[1,2]</out>", Run(
            "<xsl:template match='/'><out><xsl:value-of select=\"serialize([1, 2], map{'method': 'json'})\"/></out></xsl:template>"));

    [Fact]
    public void AccumulatorsCountElementsInDocumentOrder() =>
        Assert.Equal("<out><n b=\"0\" a=\"2\" /><n b=\"2\" a=\"3\" /></out>", Run(
            "<xsl:mode use-accumulators='count'/>" +
            "<xsl:accumulator name='count' as='xs:integer' initial-value='0'><xsl:accumulator-rule match='i' select='$value + 1'/></xsl:accumulator>" +
            "<xsl:template match='/'><out><xsl:for-each select='//g'><n b=\"{accumulator-before('count')}\" a=\"{accumulator-after('count')}\"/></xsl:for-each></out></xsl:template>",
            "<doc><g><i/><i/></g><g><i/></g></doc>"));

    /// <summary>
    /// A version="1.0" region of a 2.0+ stylesheet runs in backwards-compatible mode (XSLT 3.0 §3.9): value-of and
    /// attribute value templates take the first item, comparisons and arithmetic follow XPath 1.0's conversions.
    /// </summary>
    [Fact]
    public void VersionOneRegionsAreBackwardsCompatible() =>
        Assert.Equal("<o a=\"1\"><c>1|true|true|NaN|c</c><d>1 2 3</d></o>", Run("""
            <xsl:template match="/">
              <o a="{1 to 3}" xsl:version="1.0"><c><xsl:value-of select="1 to 3"/>|<xsl:value-of select="'1' = 1"/>|<xsl:value-of select="true() = 'x'"/>|<xsl:value-of select="'a' + 1"/>|<xsl:value-of select="substring('abc', '3')"/></c><d xsl:version="2.0"><xsl:value-of select="1 to 3"/></d></o>
            </xsl:template>
            """));

    /// <summary>
    /// XsltVersionMode.BackwardsCompatible runs a version="1.0" stylesheet on the 3.0 processor; the default
    /// (Legacy) keeps stock's 1.0 engine, which doesn't know XPath 2.0 syntax.
    /// </summary>
    [Fact]
    public void VersionModeChoosesTheProcessorForVersionOneStylesheets()
    {
        string stylesheet = $"<xsl:stylesheet version='1.0' xmlns:xsl='{Xsl}'><xsl:output omit-xml-declaration='yes'/>" +
            "<xsl:template match='/'><o><xsl:value-of select='(1 to 3)[2]'/>|<xsl:value-of select='system-property(\"xsl:version\")'/></o></xsl:template></xsl:stylesheet>";
        var transform = new XslCompiledTransform();
        transform.Load(XmlReader.Create(new StringReader(stylesheet)), new XsltSettings { VersionMode = XsltVersionMode.BackwardsCompatible }, null);
        var sw = new StringWriter();
        transform.Transform(XmlReader.Create(new StringReader("<doc/>")), null, sw);
        Assert.Equal("<o>2|3.0</o>", sw.ToString());
        Assert.ThrowsAny<XsltException>(() => new XslCompiledTransform().Load(XmlReader.Create(new StringReader(stylesheet))));
    }

    /// <summary>
    /// format-number rounds a double's shortest digits half to even: values whose scaled form sits next to a half
    /// (2.675 is 267.49999999999997 scaled) round as their digits do, others by scaling.
    /// </summary>
    [Fact]
    public void FormatNumberRoundsNearHalvesByDigits() =>
        Assert.Equal("<o>2.68 1.00 0.12 370.35 3,703.50 0.10</o>", Run("""
            <xsl:template match="/"><o><xsl:value-of select="format-number(2.675e0, '0.00'), format-number(1.005e0, '0.00'), format-number(0.125e0, '0.00'),
              format-number(3 * 123.45e0, '#,##0.00'), format-number(30 * 123.45e0, '#,##0.00'), format-number(0.1e0, '0.00')"/></o></xsl:template>
            """));

    /// <summary>The initial mode's xsl:mode names the accumulators applicable to the source document; by default none (XTDE3362).</summary>
    [Fact]
    public void AccumulatorsTheInitialModeDoesNotNameAreXTDE3362()
    {
        System.Exception e = Assert.ThrowsAny<System.Exception>(() => Run(
            "<xsl:accumulator name='count' as='xs:integer' initial-value='0'><xsl:accumulator-rule match='i' select='$value + 1'/></xsl:accumulator>" +
            "<xsl:template match='/'><out><xsl:value-of select=\"accumulator-after('count')\"/></out></xsl:template>",
            "<doc><i/></doc>"));
        Assert.Contains("XTDE3362", e.ToString());
    }

    /// <summary>$err:line-number is the line of the innermost instruction of the xsl:try body that was running.</summary>
    [Fact]
    public void CatchSeesTheLineNumber() =>
        Assert.Equal("<o>4</o>", Run("""

            <xsl:template match="/"><o><xsl:try>
              <xsl:if test="true()">
                <xsl:value-of select="1 div 0"/>
              </xsl:if>
              <xsl:catch><xsl:value-of select="$err:line-number"/></xsl:catch>
            </xsl:try></o></xsl:template>
            """.Replace("\r", "")));

    /// <summary>A local variable nothing refers to isn't evaluated, so its errors don't arise.</summary>
    [Fact]
    public void UnreferencedLocalVariablesAreNotEvaluated() =>
        Assert.Equal("<o>3</o>", Run("""
            <xsl:function name="f:f" xmlns:f="urn:f"><xsl:param name="a"/><xsl:variable name="unused" select="error()"/><xsl:sequence select="$a + 2"/></xsl:function>
            <xsl:template match="/" xmlns:f="urn:f" exclude-result-prefixes="f"><o><xsl:variable name="x" select="error()"/><xsl:value-of select="f:f(1)"/></o></xsl:template>
            """));

    /// <summary>xsl:where-populated drops an array whose members' items are all deemed empty.</summary>
    [Fact]
    public void WherePopulatedDropsArraysOfEmptyItems() =>
        Assert.Equal("<o>0 0 1</o>", Run("""
            <xsl:template match="/">
              <xsl:variable name="e" as="element()"><e/></xsl:variable>
              <xsl:variable name="a" as="item()*"><xsl:where-populated><xsl:sequence select="[$e]"/></xsl:where-populated></xsl:variable>
              <xsl:variable name="b" as="item()*"><xsl:where-populated><xsl:sequence select="[[$e, ''], ()]"/></xsl:where-populated></xsl:variable>
              <xsl:variable name="c" as="item()*"><xsl:where-populated><xsl:sequence select="[$e, 1]"/></xsl:where-populated></xsl:variable>
              <o><xsl:value-of select="count($a), count($b), count($c)"/></o>
            </xsl:template>
            """));

    /// <summary>element(*) and document-node(element(*)) patterns have the default priority -0.5 (XSLT 3.0 §6.5).</summary>
    [Fact]
    public void ElementWildcardPatternsHaveTheLowestNamePriority() =>
        Assert.Equal("<o>B</o>", Run("""
            <xsl:template match="/"><o><xsl:apply-templates select="doc"/></o></xsl:template>
            <xsl:template match="element(*)">A</xsl:template>
            <xsl:template match="doc" priority="-0.4">B</xsl:template>
            """));

    /// <summary>Parentless nodes of separate temporary trees are in the order they were made.</summary>
    [Fact]
    public void TemporaryTreesAreInCreationOrder() =>
        Assert.Equal("<o>3 1 2</o>", Run("""
            <xsl:variable name="data" as="element()*"><a>3</a><a>1</a><a>2</a></xsl:variable>
            <xsl:template match="/"><o><xsl:value-of select="$data/self::a"/></o></xsl:template>
            """));

    /// <summary>element(N) admits nilled elements and any type, so it isn't a subtype of element(N, xs:anyType).</summary>
    [Fact]
    public void FunctionTypesCompareElementTypesAndNillability() =>
        Assert.Equal("<o>true false false</o>", Run("""
            <xsl:function name="f:f" as="element(e)?" xmlns:f="urn:f"><xsl:param name="x"/><e/></xsl:function>
            <xsl:template match="/" xmlns:f="urn:f" exclude-result-prefixes="f"><o><xsl:value-of select="
              f:f#1 instance of function(item()*) as element(e)*,
              f:f#1 instance of function(item()*) as element(e, xs:anyType)*,
              f:f#1 instance of function(item()*) as element(*, xs:untyped)?"/></o></xsl:template>
            """));

    /// <summary>xsl:context-item's use attribute defaults to optional: a named template may run without a context item.</summary>
    [Fact]
    public void ContextItemIsOptionalByDefault()
    {
        string stylesheet = $"<xsl:stylesheet version='3.0' xmlns:xsl='{Xsl}' xmlns:xs='http://www.w3.org/2001/XMLSchema' exclude-result-prefixes='xs'>" +
            "<xsl:output omit-xml-declaration='yes'/>" +
            "<xsl:template name='t'><xsl:context-item as='xs:integer'/><xsl:param name='p' select='3'/><xsl:sequence select='if ($p = 3) then . else $p'/></xsl:template>" +
            "<xsl:template name='main'><o><xsl:for-each select='1 to 2'><xsl:call-template name='t'/></xsl:for-each>" +
            "<xsl:call-template name='t'><xsl:with-param name='p' select='6'/></xsl:call-template></o></xsl:template></xsl:stylesheet>";
        var transform = new XslCompiledTransform { InitialTemplate = new XmlQualifiedName("main") };
        transform.Load(XmlReader.Create(new StringReader(stylesheet)));
        var sw = new StringWriter();
        transform.Transform(XslCompiledTransform.AbsentInput, null, sw);
        Assert.Equal("<o>1 2 6</o>", sw.ToString());
    }

    /// <summary>
    /// The initial template's parameters, tunnel ones too (XSLT 3.0 §2.3.4), supplied as reserved parameters; a
    /// required one that isn't supplied is XTDE0700 even if it's unused.
    /// </summary>
    [Fact]
    public void InitialTemplateParameters()
    {
        string stylesheet = $"<xsl:stylesheet version='3.0' xmlns:xsl='{Xsl}' xmlns:my='urn:my' exclude-result-prefixes='my'>" +
            "<xsl:output omit-xml-declaration='yes'/>" +
            "<xsl:param name='a' select='12'/><xsl:variable name='b' select='$a * 2'/>" +
            "<xsl:template name='t'><xsl:param name='a' required='yes'/><xsl:param name='my:b' tunnel='yes'/><o><xsl:sequence select='$a, $my:b, $b'/></o></xsl:template>" +
            "</xsl:stylesheet>";
        string RunWith(bool supply)
        {
            var transform = new XslCompiledTransform
            {
                InitialTemplate = new XmlQualifiedName("t"),
                InitialParameters = supply ? new List<(XmlQualifiedName, bool)> { (new XmlQualifiedName("a"), false), (new XmlQualifiedName("b", "urn:my"), true) } : null,
            };
            transform.Load(XmlReader.Create(new StringReader(stylesheet)));
            var args = new XsltArgumentList();
            args.AddParam("a", "", 77);
            if (supply)
            {
                XmlQualifiedName first = XslCompiledTransform.InitialParameter(1), second = XslCompiledTransform.InitialParameter(2);
                args.AddParam(first.Name, first.Namespace, 1234);
                args.AddParam(second.Name, second.Namespace, 999);
            }
            var sw = new StringWriter();
            transform.Transform(XslCompiledTransform.AbsentInput, args, sw);
            return sw.ToString();
        }
        Assert.Equal("<o>1234 999 154</o>", RunWith(supply: true));
        Assert.Contains("XTDE0700", Assert.ThrowsAny<System.Exception>(() => RunWith(supply: false)).ToString());
    }

    [Fact]
    public void PackagesHidePrivateComponentsAndHonourOverrides()
    {
        string dir = Path.Combine(Path.GetTempPath(), "xsl3-packages-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string used = Path.Combine(dir, "used.xsl");
            File.WriteAllText(used,
                $"<xsl:package name='urn:used' package-version='1.2.0' version='3.0' xmlns:xsl='{Xsl}' xmlns:u='urn:u'>" +
                "<xsl:function name='u:secret' visibility='private'><xsl:sequence select=\"'secret'\"/></xsl:function>" +
                "<xsl:function name='u:greet' visibility='public'><xsl:param name='n'/><xsl:sequence select=\"u:prefix() || $n || u:secret()\"/></xsl:function>" +
                "<xsl:function name='u:prefix' visibility='public'><xsl:sequence select=\"'hello '\"/></xsl:function>" +
                "</xsl:package>");
            string principal =
                $"<xsl:package name='urn:main' version='3.0' xmlns:xsl='{Xsl}' xmlns:u='urn:u' exclude-result-prefixes='u'>" +
                "<xsl:use-package name='urn:used' package-version='1.*'><xsl:override>" +
                "<xsl:function name='u:prefix' visibility='public'><xsl:sequence select=\"'[' || xsl:original() || ']'\"/></xsl:function>" +
                "</xsl:override></xsl:use-package>" +
                "<xsl:template name='xsl:initial-template' visibility='public'><out><xsl:value-of select=\"u:greet('you'), function-available('u:secret')\"/></out></xsl:template>" +
                "</xsl:package>";
            var transform = new XslCompiledTransform
            {
                InitialTemplate = new XmlQualifiedName("initial-template", Xsl),
                Packages = new List<(string, string, System.Uri)> { ("urn:used", "1.2.0", new System.Uri(used)) },
            };
            transform.Load(XmlReader.Create(new StringReader(principal)), new XsltSettings(), new XmlUrlResolver());
            var sw = new StringWriter();
            transform.Transform(XmlReader.Create(new StringReader("<doc/>")), null, sw);
            // The override replaces u:prefix everywhere, even in the used package; u:secret stays private to it.
            Assert.EndsWith("<out>[hello ]yousecret false</out>", sw.ToString());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void DecimalFormatsAndKeysAreLocalToAPackage()
    {
        string dir = Path.Combine(Path.GetTempPath(), "xsl3-packages-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string used = Path.Combine(dir, "used.xsl");
            File.WriteAllText(used,
                $"<xsl:package name='urn:used' package-version='1.0' version='3.0' xmlns:xsl='{Xsl}' xmlns:u='urn:u'>" +
                "<xsl:decimal-format grouping-separator='~'/><xsl:key name='k' match='x' use='@id'/>" +
                "<xsl:function name='u:f' visibility='public'><xsl:param name='doc'/>" +
                "<xsl:sequence select=\"format-number(1234, '0~000'), count(key('k', 'a', $doc))\"/></xsl:function>" +
                "</xsl:package>");
            string principal =
                $"<xsl:package name='urn:main' version='3.0' xmlns:xsl='{Xsl}' xmlns:u='urn:u' exclude-result-prefixes='u'>" +
                "<xsl:use-package name='urn:used' package-version='1.0'/>" +
                "<xsl:decimal-format grouping-separator='^'/><xsl:key name='k' match='y' use='@id'/>" +
                "<xsl:template name='xsl:initial-template' visibility='public'><xsl:variable name='d'><x id='a'/><y id='a'/><y id='a'/></xsl:variable>" +
                "<out><xsl:value-of select=\"u:f($d), format-number(1234, '0^000'), count(key('k', 'a', $d))\"/></out></xsl:template>" +
                "</xsl:package>";
            var transform = new XslCompiledTransform
            {
                InitialTemplate = new XmlQualifiedName("initial-template", Xsl),
                Packages = new List<(string, string, System.Uri)> { ("urn:used", "1.0", new System.Uri(used)) },
            };
            transform.Load(XmlReader.Create(new StringReader(principal)), new XsltSettings(), new XmlUrlResolver());
            var sw = new StringWriter();
            transform.Transform(XmlReader.Create(new StringReader("<doc/>")), null, sw);
            Assert.EndsWith("<out>1~234 1 1^234 2</out>", sw.ToString());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void CDataSectionsSplitAroundCharactersTheEncodingLacks()
    {
        string stylesheet = $"<xsl:stylesheet version='3.0' xmlns:xsl='{Xsl}' xmlns='http://www.w3.org/1999/xhtml'><xsl:output method='xhtml' cdata-section-elements='example' indent='no' encoding='US-ASCII'/>" +
                            "<xsl:template match='/'><html><example>foo &#xaa; bar</example></html></xsl:template></xsl:stylesheet>";
        var transform = new XslCompiledTransform();
        transform.Load(XmlReader.Create(new StringReader(stylesheet)));
        var bytes = new MemoryStream();
        transform.Transform(XmlReader.Create(new StringReader("<doc/>")), null, bytes);
        Assert.Contains("<example><![CDATA[foo ]]>&#xAA;<![CDATA[ bar]]></example>", System.Text.Encoding.ASCII.GetString(bytes.ToArray()));
    }

    /// <summary>A template called from xsl:matching-substring sees its captured substrings; a function doesn't (XSLT 3.0 §5.4.4).</summary>
    [Fact]
    public void RegexGroupInCalledTemplates() =>
        Assert.Equal("<o>a|b||</o>", Run("""
            <xsl:template match="/">
              <xsl:variable name="d" select="doc"/>
              <o><xsl:analyze-string select="'ab'" regex="(a)(b)"><xsl:matching-substring><xsl:call-template name="t"/>|<xsl:apply-templates select="$d"/>|<xsl:value-of select="f:g()" xmlns:f="urn:f"/></xsl:matching-substring></xsl:analyze-string>|<xsl:call-template name="t"/></o>
            </xsl:template>
            <xsl:template name="t"><xsl:value-of select="regex-group(1)"/></xsl:template>
            <xsl:template match="doc"><xsl:value-of select="regex-group(2)"/></xsl:template>
            <xsl:function name="f:g" xmlns:f="urn:f"><xsl:sequence select="regex-group(1)"/></xsl:function>
            """));

    /// <summary><c>$x = (a to b)</c> is a range test: the same answers as comparing with each integer, without building the range.</summary>
    [Theory]
    [InlineData("5", "true")]
    [InlineData("5.0", "true")]
    [InlineData("5.5", "false")]
    [InlineData("xs:double('NaN')", "false")]
    [InlineData("1e300", "false")]
    [InlineData("xs:untypedAtomic('7')", "true")]
    [InlineData("(0, 2000000)", "true")]
    [InlineData("()", "false")]
    public void GeneralComparisonWithARange(string value, string expected) =>
        Assert.Equal($"<o>{expected}</o>", Run($"<xsl:template match='/'><o><xsl:value-of select=\"({value}) = (1 to 2000000)\"/></o></xsl:template>"));

    /// <summary>Rounding works on the double's own value (F&amp;O 3.1 §4.4.4–5), beyond decimal's range too.</summary>
    [Fact]
    public void RoundingDoubles() =>
        Assert.Equal("<o>-13.7 150.01 250.03 1.0E-98 5.0E101 3567.812</o>", Run("""
            <xsl:template match="/"><o><xsl:value-of select="round(-1.365e1, 1), round-half-to-even(150.015e0, 2), round-half-to-even(250.025e0, 2),
              round(9.9e-99, 99), round(45e100, -101), round-half-to-even(3.567812E+3, 4294967296)"/></o></xsl:template>
            """));

    [Fact]
    public void SortCaseOrder() =>
        Assert.Equal("<o>a,B,b,C,c|a,b,B,c,C</o>", Run("""
            <xsl:template match="/" xmlns:f="urn:f" exclude-result-prefixes="f"><o><xsl:value-of select="string-join(f:sort('upper-first'), ',')"/>|<xsl:value-of select="string-join(f:sort('lower-first'), ',')"/></o></xsl:template>
            <xsl:function name="f:sort" xmlns:f="urn:f"><xsl:param name="case"/>
              <xsl:perform-sort select="('c', 'B', 'a', 'C', 'b')"><xsl:sort select="." lang="en" case-order="{$case}"/></xsl:perform-sort></xsl:function>
            """));

    /// <summary>A message that holds a map, or fails, doesn't end the transformation (XSLT 3.0 §23.1).</summary>
    [Fact]
    public void NonTerminatingMessagesNeverFail() =>
        Assert.Equal("<o>done</o>", Run("""
            <xsl:template match="/"><xsl:message select="map{1: 2}"/><xsl:message><xsl:attribute name="a" select="1"/></xsl:message>
              <xsl:message select="1 div 0"/><o>done</o></xsl:template>
            """));

    [Fact]
    public void NumberOfHugeIntegers() =>
        Assert.Equal("<o>1881676371789154860897069003</o>", Run("""
            <xsl:template match="/"><o><xsl:number value="1234567890 * 1234567890 * 1234567890 + 3"/></o></xsl:template>
            """));

    /// <summary>Decimal format characters may be outside the BMP.</summary>
    [Fact]
    public void DecimalFormatOutsideTheBmp()
    {
        string decimalSeparator = char.ConvertFromUtf32(110000), groupingSeparator = char.ConvertFromUtf32(110001);
        Assert.Equal($"<o>1{groupingSeparator}234{decimalSeparator}123</o>", Run("""
            <xsl:decimal-format decimal-separator="&#110000;" grouping-separator="&#110001;"/>
            <xsl:template match="/"><o><xsl:value-of select="format-number(1234.123, '&#110001;000&#110000;000')"/></o></xsl:template>
            """));
    }

    /// <summary>Arities no function can have answer false, quickly (an int.MaxValue arity once looped for ever).</summary>
    [Fact]
    public void FunctionAvailableWithImpossibleArities() =>
        Assert.Equal("<o>false false true false</o>", Run("""
            <xsl:template match="/"><o><xsl:value-of select="function-available('abs', 2147483647), function-available('abs', -5),
              function-available('abs', 1), function-available('abs', 1e300)"/></o></xsl:template>
            """));

    /// <summary>Rounding at a precision beyond the value's digits, or of values below 1 to many places.</summary>
    [Fact]
    public void RoundingEdgeCases() =>
        Assert.Equal("<o>1.0E20 1.23E-25 1.235E-25 1.5</o>", Run("""
            <xsl:template match="/"><o><xsl:value-of select="round(1e20, 10), round-half-to-even(1.23456e-25, 27), round(1.23456e-25, 28), round(xs:decimal('1.5'), 10)"/></o></xsl:template>
            """));

    [Fact]
    public void InvalidCaseOrderIsXTDE0030()
    {
        System.Exception e = Assert.ThrowsAny<System.Exception>(() => Run("""
            <xsl:template match="/"><o><xsl:perform-sort select="('b', 'a')"><xsl:sort select="." case-order="{'sideways'}"/></xsl:perform-sort></o></xsl:template>
            """));
        Assert.Contains("XTDE0030", e.ToString());
    }

    /// <summary>An EQName error code is written with doubled braces in the attribute value template.</summary>
    [Fact]
    public void AssertWithAnEQNameErrorCode()
    {
        string stylesheet = $"<xsl:stylesheet version='3.0' xmlns:xsl='{Xsl}'><xsl:template match='/'><out><xsl:try>" +
                            "<xsl:assert test='(1, 2) = 3' error-code='Q{{urn:x}}E1'/><xsl:catch errors='Q{urn:x}E1' select=\"'caught'\"/></xsl:try></out></xsl:template></xsl:stylesheet>";
        var transform = new XslCompiledTransform();
        transform.Load(XmlReader.Create(new StringReader(stylesheet)), new XsltSettings { EnableAssertions = true }, null);
        var sw = new StringWriter();
        transform.Transform(XmlReader.Create(new StringReader("<doc/>")), null, sw);
        Assert.EndsWith("<out>caught</out>", sw.ToString());
    }

    /// <summary>An error the stylesheet raises itself inside a non-terminating message still ends the transformation.</summary>
    [Fact]
    public void FnErrorInAMessageStillFails()
    {
        System.Exception e = Assert.ThrowsAny<System.Exception>(() => Run("""
            <xsl:template match="/"><xsl:message><xsl:sequence select="error(QName('urn:x', 'E2'))"/></xsl:message><o/></xsl:template>
            """));
        Assert.Contains("E2", e.ToString());
    }

    [Fact]
    public void CaseOrderWithAComputedLang() =>
        Assert.Equal("<o>a B b</o>", Run("""
            <xsl:template match="/"><xsl:variable name="l" select="'en'"/><o><xsl:perform-sort select="('b', 'B', 'a')"><xsl:sort select="." lang="{$l}" case-order="upper-first"/></xsl:perform-sort></o></xsl:template>
            """));

    /// <summary>group-starting-with has no grouping key, in its sort keys too (XTDE1071).</summary>
    [Fact]
    public void NoGroupingKeyInSortsOfStartingWith()
    {
        System.Exception e = Assert.ThrowsAny<System.Exception>(() => Run("""
            <xsl:template match="/"><o><xsl:for-each-group select="1 to 4" group-starting-with=".[. mod 2 = 1]"><xsl:sort select="current-grouping-key()"/><xsl:value-of select="."/></xsl:for-each-group></o></xsl:template>
            """));
        Assert.Contains("XTDE1071", e.ToString());
    }

    /// <summary>A memo function (XSLT 3.0 §10.3.2) returns the same nodes for the same arguments, and makes naive recursion fast.</summary>
    [Fact]
    public void MemoFunctions() =>
        Assert.Equal("<o>3 12586269025</o>", Run("""
            <xsl:function name="f:e" xmlns:f="urn:f" new-each-time="no"><xsl:param name="n"/><e><xsl:value-of select="$n"/></e></xsl:function>
            <xsl:function name="f:fib" xmlns:f="urn:f" cache="yes" as="xs:integer"><xsl:param name="n" as="xs:integer"/>
              <xsl:sequence select="if ($n lt 2) then $n else f:fib($n - 1) + f:fib($n - 2)"/></xsl:function>
            <xsl:template match="/" xmlns:f="urn:f" exclude-result-prefixes="f"><o><xsl:value-of select="count((1, 2, 1, 3, 2) ! f:e(.) | ()), f:fib(50)"/></o></xsl:template>
            """));

    /// <summary>copy-namespaces="no" keeps only the namespaces the copied names need (XSLT 3.0 §11.9.1–2).</summary>
    [Fact]
    public void CopyNamespacesNo()
    {
        string result = Run("""
            <xsl:template match="/"><o><xsl:copy-of select="a:e" copy-namespaces="no" xmlns:a="urn:a"/><xsl:for-each select="*"><xsl:copy copy-namespaces="no"/></xsl:for-each></o></xsl:template>
            """, "<a:e xmlns:a='urn:a' xmlns:b='urn:b' xmlns:unused='urn:u' b:x='1'><c/></a:e>");
        Assert.DoesNotContain("urn:u", result);
        Assert.Contains("xmlns:b=\"urn:b\"", result);
        Assert.EndsWith("<a:e xmlns:a=\"urn:a\" /></o>", result);
    }

    /// <summary>inherit-namespaces="no" (XSLT 3.0 §11.1.2): children don't inherit the element's namespaces, in trees and output.</summary>
    [Fact]
    public void InheritNamespacesNo()
    {
        // Sorted prefixes: x:a has x, b none, c the default namespace; none has p's.
        Assert.Equal("<o>x xml|xml| xml</o>", Run("""
            <xsl:template match="/">
              <xsl:variable name="v"><xsl:element name="p" namespace="urn:p" inherit-namespaces="no"><x:a xmlns:x="urn:x"/><b/><c xmlns="urn:c"/></xsl:element></xsl:variable>
              <o><xsl:value-of select="string-join($v/*/*!string-join(sort(in-scope-prefixes(.)), ' '), '|')"/></o>
            </xsl:template>
            """));
        Assert.Contains("<b xmlns=\"\" />", Run("""
            <xsl:template match="/"><xsl:element name="p" namespace="urn:p" inherit-namespaces="no"><b/></xsl:element></xsl:template>
            """));
    }

    /// <summary>
    /// A predicate that can't depend on the focus ($s[$i]) is evaluated once and used as a position, with the same
    /// results as evaluating it per item: numbers select by position (none for fractions or out of range), anything
    /// else keeps all items or none, errors are raised only when there are items to filter.
    /// </summary>
    [Fact]
    public void FocusFreePredicatesSelectByPosition()
    {
        Assert.Equal("<o>c|b|d|||a b c d e|||a</o>", Run("""
            <xsl:template match="/">
              <xsl:variable name="s" select="('a', 'b', 'c', 'd', 'e')"/>
              <xsl:variable name="i" select="3"/>
              <xsl:variable name="half" select="2.5"/>
              <xsl:variable name="yes" select="true()"/>
              <xsl:variable name="no" select="''"/>
              <xsl:variable name="none" select="()"/>
              <o><xsl:value-of select="$s[$i]"/>|<xsl:value-of select="$s[$i - 1]"/>|<xsl:value-of select="$s[($i + 1)]"/>|<xsl:value-of select="$s[$half]"/>|<xsl:value-of select="$s[$i * 10]"/>|<xsl:value-of select="$s[$yes]"/>|<xsl:value-of select="$s[$no]"/>|<xsl:value-of select="$none[$i + 'x']"/>|<xsl:value-of select="$s[-$i + 4 idiv 1]"/></o>
            </xsl:template>
            """));
        // A sequence of two numbers has no effective boolean value (FORG0006), when there are items to filter.
        Assert.ThrowsAny<System.Exception>(() => Run("""
            <xsl:template match="/"><xsl:variable name="two" select="(1, 2)"/><o><xsl:value-of select="(1, 2, 3)[$two]"/></o></xsl:template>
            """));
    }

    /// <summary>
    /// A sequence converted to a parameter's declared type is passed on to the same parameter (a recursive call)
    /// without being checked again; a value of the wrong type is still rejected.
    /// </summary>
    [Fact]
    public void RecursiveCallsPassConvertedSequencesOn()
    {
        Assert.Equal("<o>55</o>", Run("""
            <xsl:function name="f:sum" as="xs:decimal" xmlns:f="urn:f">
              <xsl:param name="items" as="xs:decimal*"/>
              <xsl:param name="i" as="xs:integer"/>
              <xsl:sequence select="if ($i gt count($items)) then 0 else $items[$i] + f:sum($items, $i + 1)"/>
            </xsl:function>
            <xsl:template match="/"><o xmlns:f="urn:f" xsl:exclude-result-prefixes="f"><xsl:value-of select="f:sum(1 to 10, 1)"/></o></xsl:template>
            """));
        Assert.ThrowsAny<System.Exception>(() => Run("""
            <xsl:function name="f:sum" as="xs:decimal" xmlns:f="urn:f">
              <xsl:param name="items" as="xs:decimal*"/>
              <xsl:sequence select="sum($items)"/>
            </xsl:function>
            <xsl:function name="f:dates" as="xs:date*" xmlns:f="urn:f">
              <xsl:param name="items" as="xs:date*"/>
              <xsl:sequence select="$items"/>
            </xsl:function>
            <xsl:template match="/"><o xmlns:f="urn:f"><xsl:value-of select="f:sum(f:dates((1 to 10) ! xs:date('2026-10-21')))"/></o></xsl:template>
            """));
    }

    /// <summary>fn:idref finds the attributes a DTD declares IDREF or IDREFS that refer to an ID, in document order.</summary>
    [Fact]
    public void IdrefFindsDtdIdrefAttributes()
    {
        string input = """
            <!DOCTYPE r [
              <!ELEMENT r (a*)>
              <!ELEMENT a EMPTY>
              <!ATTLIST a id ID #IMPLIED ref IDREF #IMPLIED refs IDREFS #IMPLIED other CDATA #IMPLIED>
            ]>
            <r><a id="x"/><a id="y" ref="x"/><a refs="y  x" other="x"/><a ref="z"/></r>
            """;
        var transform = new XslCompiledTransform();
        transform.Load(XmlReader.Create(new StringReader($"<xsl:stylesheet version='3.0' xmlns:xsl='{Xsl}'><xsl:output omit-xml-declaration='yes'/>" +
            "<xsl:template match='/'><o><xsl:value-of select=\"idref('x') ! name()\"/>|<xsl:value-of select=\"idref(' y ') ! name()\"/>|<xsl:value-of select=\"idref('nope') ! name()\"/></o></xsl:template></xsl:stylesheet>")));
        var text = new StringWriter();
        transform.Transform(XmlReader.Create(new StringReader(input), new XmlReaderSettings { DtdProcessing = DtdProcessing.Parse }), null, text);
        Assert.Equal("<o>ref refs|refs|</o>", text.ToString());
    }

    /// <summary>
    /// Automatic indexing of /path[key = $value] gives what the filter gives, evaluated many times (the index is built
    /// on the second): strings and nodes by string, numbers as numbers (no index), several values in document order,
    /// repeated child keys, descendant paths, and each document its own index.
    /// </summary>
    [Fact]
    public void IndexedPathsAgreeWithTheFilter()
    {
        string input = "<r><p k='1' n='a'><c>x</c><c>y</c></p><p k='2' n='b'><c>y</c></p><p k='1.0' n='c'/><p k='3' n='d'/></r>";
        Assert.Equal("<o>a,a,a|a c,a c,a c|a b d,a b d,a b d|a b,a b,a b|b c d,b c d,b c d|,,|1 2 1 2 1 2</o>", Run("""
            <xsl:template match="/">
              <xsl:variable name="d" select="."/>
              <xsl:variable name="other" select="parse-xml('&lt;r>&lt;p k=&quot;1&quot; n=&quot;z&quot;/>&lt;p k=&quot;1&quot; n=&quot;y&quot;/>&lt;/r>')"/>
              <o>
                <xsl:value-of select="string-join(for $i in 1 to 3 return let $v := '1' return string-join(/r/p[@k = $v]/@n, ' '), ',')"/>|<xsl:value-of
                  select="string-join(for $i in 1 to 3 return let $v := 1 return string-join(/r/p[@k = $v]/@n, ' '), ',')"/>|<xsl:value-of
                  select="string-join(for $i in 1 to 3 return let $v := ('3', '2', '1') return string-join(/r/p[$v = @k]/@n, ' '), ',')"/>|<xsl:value-of
                  select="string-join(for $i in 1 to 3 return let $v := 'y' return string-join(/r/p[c = $v]/@n, ' '), ',')"/>|<xsl:value-of
                  select="string-join(for $i in 1 to 3 return let $v := $d/r/p[2]/@k return string-join(//p[@k = ($v, '1.0', '3')]/@n, ' '), ',')"/>|<xsl:value-of
                  select="string-join(for $i in 1 to 3 return let $v := () return string-join(/r/p[@k = $v]/@n, ' '), ',')"/>|<xsl:for-each
                  select="1 to 3"><xsl:for-each select="$d, $other"><xsl:value-of select="count(/r/p[@k = '1'])"/><xsl:text> </xsl:text></xsl:for-each></xsl:for-each>
              </o>
            </xsl:template>
            """, input).Replace(" </o>", "</o>"));
    }

    /// <summary>
    /// XTSE3450: a static variable declared with another value earlier in the stylesheet at a lower import precedence
    /// (an import before it) is an error; declared first at the higher precedence (before the import), it isn't.
    /// </summary>
    [Fact]
    public void StaticVariablesMustAgreeWithLowerPrecedenceOnesBeforeThem()
    {
        string dir = Path.Combine(Path.GetTempPath(), "xsl3-static-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "imported.xsl"), $"<xsl:stylesheet version='3.0' xmlns:xsl='{Xsl}'><xsl:variable name='v' static='yes' select='1'/></xsl:stylesheet>");
            string Load(string body)
            {
                string main = Path.Combine(dir, "main.xsl");
                File.WriteAllText(main, $"<xsl:stylesheet version='3.0' xmlns:xsl='{Xsl}'>{body}<xsl:template match='/'><out v='{{$v}}'/></xsl:template></xsl:stylesheet>");
                var transform = new XslCompiledTransform();
                transform.Load(main, XsltSettings.Default, new XmlUrlResolver());
                var text = new StringWriter();
                transform.Transform(XmlReader.Create(new StringReader("<doc/>")), null, text);
                return text.ToString();
            }
            Assert.Contains("v=\"2\"", Load("<xsl:variable name='v' static='yes' select='2'/><xsl:import href='imported.xsl'/>"));
            Assert.Contains("XTSE3450", Assert.ThrowsAny<XsltException>(() => Load("<xsl:import href='imported.xsl'/><xsl:variable name='v' static='yes' select='2'/>")).Message);
            Assert.Contains("v=\"1\"", Load("<xsl:import href='imported.xsl'/><xsl:variable name='v' static='yes' select='1'/>"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
