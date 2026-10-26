// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.IO;
using System.Xml;
using Xunit;

namespace Xsl3.Tests;

public class StreamedItemParameterTests
{
    private const string Input = "<root><item k='a'>one</item><item k='a'>two</item><item k='b'>three</item><item k='b'>four</item><item k='c'>five</item><item k='c'>six</item><item k='d'>seven</item></root>";
    private const string Prefix = "<xsl:stylesheet version='3.0' xmlns:xsl='http://www.w3.org/1999/XSL/Transform' xmlns:xs='http://www.w3.org/2001/XMLSchema' xmlns:f='urn:test' exclude-result-prefixes='xs f'><xsl:mode streamable='yes'/><xsl:output omit-xml-declaration='yes'/>";

    private const string GroupResult = "<out><chunk><item k=\"a\">one</item><item k=\"a\">two</item><item k=\"b\">three</item></chunk><chunk><item k=\"b\">four</item><item k=\"c\">five</item><item k=\"c\">six</item></chunk><chunk><item k=\"d\">seven</item></chunk></out>";
    private static string GroupSheet(string? outerType, string innerType)
    {
        string type = outerType == null ? "" : " as='" + outerType + "'";
        return Prefix + "<xsl:function name='f:chunks' as='item()*' streamability='absorbing'><xsl:param name='items'" + type + "/>"
            + "<xsl:for-each-group select='$items' group-adjacent='(position() - 1) idiv 3'><xsl:sequence select='f:wrap(current-group())'/></xsl:for-each-group></xsl:function>"
            + "<xsl:function name='f:wrap' as='element()' streamability='absorbing'><xsl:param name='items' as='" + innerType + "'/><chunk><xsl:sequence select='$items'/></chunk></xsl:function>"
            + "<xsl:template match='/'><out><xsl:sequence select='f:chunks(root/item)'/></out></xsl:template></xsl:stylesheet>";
    }

    private static string Run(string sheet, bool forward, bool modern = false, bool debug = false, bool saved = false)
    {
        var settings = new XsltSettings { StreamedExecution = forward, StreamingBufferCapacity = 1 };
        using var input = XmlReader.Create(new StringReader(Input)); using var output = new StringWriter();
        using var style = XmlReader.Create(new StringReader(sheet));
        if (modern) new XsltCompiler { Settings = settings }.Compile(style).CreateTransformer().ApplyTemplates(input, output);
        else
        {
            var transform = new XslCompiledTransform(enableDebug: debug);
            if (saved)
            {
#if NET10_0_OR_GREATER
                using var assembly = new MemoryStream();
                transform.CompileToAssembly(style, settings, null, "Xsl3.Tests.ItemParameters", "Saved.ItemParameters", assembly);
                transform.Load(System.Reflection.Assembly.Load(assembly.ToArray()).GetType("Saved.ItemParameters", true)!, settings);
#endif
            }
            else transform.Load(style, settings, null);
            transform.Transform(input, null, output);
        }
        Assert.NotEqual(ReadState.Closed, input.ReadState);
        return output.ToString();
    }

    [Theory]
    [InlineData("item()*", "element()*")]
    [InlineData("item()*", "item()*")]
    [InlineData("node()*", "element()*")]
    [InlineData(null, "item()*")]
    public void AdjacentGroupsPassThroughAbsorbingFunctionsWithoutCollectingLiveMembers(string? outerType, string innerType)
    {
        string sheet = GroupSheet(outerType, innerType);
        foreach (bool forward in new[] { false, true })
        {
            Assert.Equal(GroupResult, Run(sheet, forward));
            Assert.Equal(GroupResult, Run(sheet, forward, modern: true));
            Assert.Equal(GroupResult, Run(sheet, forward, debug: true));
        }
    }

    [Fact]
    public void ItemParametersPreserveMixedXdmItemsAndLiveNodes()
    {
        string sheet = Prefix + "<xsl:function name='f:describe' as='xs:string*' streamability='absorbing'><xsl:param name='items' as='item()*'/>"
            + "<xsl:for-each select='$items'><xsl:sequence select=\"if (. instance of map(*)) then 'map' else if (. instance of array(*)) then 'array' else if (. instance of function(*)) then 'function' else string(.)\"/></xsl:for-each></xsl:function>"
            + "<xsl:template match='/'><out><xsl:value-of select=\"f:describe((1,map{},[2],function(){3},root/item[1]))\" separator='|'/></out></xsl:template></xsl:stylesheet>";
        foreach (bool forward in new[] { false, true }) Assert.Equal("<out>1|map|array|function|one</out>", Run(sheet, forward));
    }

    [Fact]
    public void ItemTypedParametersRetainForwardChildNavigation()
    {
        string sheet = Prefix + "<xsl:function name='f:keys' as='xs:string' streamability='absorbing'><xsl:param name='items' as='item()*'/><xsl:sequence select=\"string-join($items/@k,'|')\"/></xsl:function>"
            + "<xsl:template match='/'><out><xsl:value-of select='f:keys(root/item)'/></out></xsl:template></xsl:stylesheet>";
        foreach (bool forward in new[] { false, true }) Assert.Equal("<out>a|a|b|b|c|c|d</out>", Run(sheet, forward));
    }

    [Theory]
    [InlineData("(1,2,3)", "3 3")]
    [InlineData("(map{},[2],function(){3})", "3 3")]
    [InlineData("root/item", "7 7")]
    public void GroundedAndLiveItemArgumentsRetainForkBehavior(string argument, string expected)
    {
        string sheet = Prefix + "<xsl:function name='f:counts' as='xs:integer*' streamability='absorbing'><xsl:param name='items' as='item()*'/>"
            + "<xsl:fork><xsl:sequence select='count($items)'/><xsl:sequence select='count($items)'/></xsl:fork></xsl:function>"
            + "<xsl:template match='/'><out><xsl:value-of select='f:counts(" + argument + ")'/></out></xsl:template></xsl:stylesheet>";
        foreach (bool forward in new[] { false, true })
            foreach (bool debug in new[] { false, true }) Assert.Equal("<out>" + expected + "</out>", Run(sheet, forward, debug: debug));
    }

    [Theory]
    [InlineData("(1,2,3)", "3 3")]
    [InlineData("(map{},[2],function(){3})", "3 3")]
    [InlineData("f:make(root/item[1])", "1 1")]
    [InlineData("root/item", "7 7")]
    public void ForwardedArgumentsRetainGroundedAndLiveForkSemantics(string argument, string expected)
    {
        string sheet = Prefix + "<xsl:function name='f:relay' as='xs:integer*' streamability='absorbing'><xsl:param name='items' as='item()*'/><xsl:sequence select='f:counts($items)'/></xsl:function>"
            + "<xsl:function name='f:make' as='element()*' streamability='absorbing'><xsl:param name='items' as='item()*'/><xsl:sequence select='copy-of($items)'/></xsl:function>"
            + "<xsl:function name='f:counts' as='xs:integer*' streamability='absorbing'><xsl:param name='items' as='item()*'/><xsl:fork><xsl:sequence select='count($items)'/><xsl:sequence select='count($items)'/></xsl:fork></xsl:function>"
            + "<xsl:template match='/'><out><xsl:value-of select='f:relay(" + argument + ")'/></out></xsl:template></xsl:stylesheet>";
        foreach (bool forward in new[] { false, true }) Assert.Equal("<out>" + expected + "</out>", Run(sheet, forward));
    }

    [Theory]
    [InlineData("1 to 3", 3)]
    [InlineData("root/item", 7)]
    public void SingletonItemArgumentsKeepTheirForkBindings(string population, int count)
    {
        string sheet = Prefix + "<xsl:function name='f:counts' as='xs:integer*' streamability='absorbing'><xsl:param name='items' as='item()*'/><xsl:fork><xsl:sequence select='count($items)'/><xsl:sequence select='count($items)'/></xsl:fork></xsl:function>"
            + "<xsl:template match='/'><out><xsl:for-each select='" + population + "'><v><xsl:value-of select='f:counts(.)'/></v></xsl:for-each></out></xsl:template></xsl:stylesheet>";
        string expected = "<out>" + string.Concat(System.Linq.Enumerable.Repeat("<v>1 1</v>", count)) + "</out>";
        foreach (bool forward in new[] { false, true }) Assert.Equal(expected, Run(sheet, forward));
    }

    [Theory]
    [InlineData("item()+", "()")]
    [InlineData("element(item)*", "(root/item[1],1)")]
    [InlineData("element(item)*", "(1,root/item[1])")]
    public void ParameterErrorsCannotBeSwallowedByTheFunctionBody(string type, string argument)
    {
        string sheet = Prefix + "<xsl:function name='f:count' as='xs:integer' streamability='absorbing'><xsl:param name='items' as='" + type + "'/>"
            + "<xsl:try><xsl:sequence select='count($items)'/><xsl:catch><xsl:sequence select='0'/></xsl:catch></xsl:try></xsl:function>"
            + "<xsl:template match='/'><xsl:value-of select='f:count(" + argument + ")'/></xsl:template></xsl:stylesheet>";
        foreach (bool forward in new[] { false, true })
            Assert.Equal("XPTY0004", Assert.Throws<Xsl3.Xdm.XdmException>(() => Run(sheet, forward)).Code);
    }

#if NET10_0_OR_GREATER
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SavedAssembliesKeepItemParameterAndGroupContinuationContracts(bool debug) =>
        Assert.Equal(GroupResult, Run(GroupSheet("item()*", "element()*"), true, debug: debug, saved: true));
#endif
}
