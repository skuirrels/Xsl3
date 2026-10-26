// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Xml;
using Xunit;

namespace Xsl3.Tests;

public class SavedSchemaMetadataTests
{
#if NET9_0_OR_GREATER
    private const string Schema = "<xsl:import-schema namespace='urn:types'><xs:schema targetNamespace='urn:types' elementFormDefault='qualified' xmlns:s='urn:types'>"
        + "<xs:simpleType name='small'><xs:restriction base='xs:integer'><xs:minInclusive value='1'/><xs:maxInclusive value='3'/></xs:restriction></xs:simpleType>"
        + "<xs:simpleType name='ints'><xs:list itemType='xs:integer'/></xs:simpleType>"
        + "<xs:simpleType name='choice'><xs:union memberTypes='xs:integer xs:date'/></xs:simpleType>"
        + "<xs:element name='doc'><xs:complexType><xs:sequence><xs:element name='row' type='s:small'/></xs:sequence></xs:complexType></xs:element>"
        + "<xs:element name='head' type='s:small' abstract='true'/><xs:element name='member' type='s:small' substitutionGroup='s:head'/>"
        + "</xs:schema></xsl:import-schema>";

    [Theory]
    [InlineData("s:small('2') instance of s:small", "true")]
    [InlineData("4 castable as s:small", "false")]
    [InlineData("count(s:ints('1 2'))", "2")]
    [InlineData("s:choice('2') instance of xs:integer", "true")]
    [InlineData("type-available('s:small')", "true")]
    [InlineData("(s:small#1)(2) instance of s:small", "true")]
    public void SavedMetadataRetainsRealSchemaTypeSemantics(string expression, string expected)
    {
        foreach (bool debug in new[] { false, true })
            Assert.Equal(expected, Run("<xsl:value-of select=\"" + expression + "\"/>", "", debug));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FunctionSignaturesShareTheSameRestoredSchemaType(bool debug)
    {
        string function = "<xsl:function name='s:echo' as='s:small'><xsl:param name='value' as='s:small'/><xsl:sequence select='$value'/></xsl:function>";
        Assert.Equal("true", Run("<xsl:value-of select=\"s:echo(s:small('2')) instance of s:small\"/>", function, debug));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SavedDynamicEvaluationRetainsItsSchemaContext(bool debug)
    {
        Assert.Equal("true", Run("<xsl:evaluate schema-aware='yes' xpath=\"'s:small(2) instance of s:small'\"/>", "", debug));
    }

    [Theory]
    [InlineData("<s:doc xsl:validation='strict'><s:row>2</s:row></s:doc>", "$node instance of schema-element(s:doc)")]
    [InlineData("<s:member xsl:validation='strict'>2</s:member>", "$node instance of schema-element(s:head)")]
    [InlineData("<s:member xsl:validation='strict'>2</s:member>", "$node instance of element(s:member, s:small)")]
    public void NodeTestsRetainAnonymousTypesAndSubstitutionGroups(string constructor, string expression)
    {
        foreach (bool debug in new[] { false, true })
            Assert.Equal("true", Run("<xsl:variable name='node' as='element()'>" + constructor + "</xsl:variable><xsl:value-of select=\"" + expression + "\"/>", "", debug));
    }

    private static string Run(string body, string declarations, bool debug)
    {
        string sheet = "<xsl:stylesheet version='3.0' xmlns:xsl='http://www.w3.org/1999/XSL/Transform' xmlns:xs='http://www.w3.org/2001/XMLSchema' xmlns:s='urn:types'>"
            + Schema + declarations + "<xsl:output method='text'/><xsl:template match='/'>" + body + "</xsl:template></xsl:stylesheet>";
        var settings = new XsltSettings { SchemaAware = true, EnableEvaluate = true };
        using var image = new MemoryStream();
        using (XmlReader source = XmlReader.Create(new StringReader(sheet)))
            new XslCompiledTransform(debug).CompileToAssembly(source, settings, null, "Metadata" + Guid.NewGuid().ToString("N"), "Saved.Metadata", image);
        image.Position = 0;
        var context = new AssemblyLoadContext("saved-schema-metadata", isCollectible: true);
        try
        {
            Assembly assembly = context.LoadFromStream(image);
            var transform = new XslCompiledTransform();
            transform.Load(assembly.GetType("Saved.Metadata", throwOnError: true)!, settings);
            using XmlReader input = XmlReader.Create(new StringReader("<input/>"));
            using var output = new StringWriter();
            transform.Transform(input, null, output);
            return output.ToString();
        }
        finally { context.Unload(); }
    }
#endif
}
