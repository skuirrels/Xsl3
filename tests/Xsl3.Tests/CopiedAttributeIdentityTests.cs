// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Xml;
using System.Xml.Schema;
using System.Xml.XPath;
using Xsl3.Schema;
using Xsl3.Tree;
using Xsl3.Xdm;
using Xunit;

namespace Xsl3.Tests;

public class CopiedAttributeIdentityTests
{
    public static IEnumerable<object[]> Sources()
    {
        foreach (var (declaration, validation) in CopiedAttributeIdentityFixtures.Contracts())
        foreach (bool declared in new[] { false, true })
            yield return new object[] { declaration, validation, declared };
    }

    public static IEnumerable<object[]> Paths()
    {
        foreach (object[] source in Sources())
        foreach (bool legacy in new[] { false, true })
        foreach (bool debug in new[] { false, true })
        foreach (bool forward in new[] { false, true })
        foreach (bool positioned in new[] { false, true })
            yield return new object[] { source[0], source[1], source[2], legacy, debug, forward, positioned };
    }

    public static IEnumerable<object[]> SavedPaths()
    {
        foreach (object[] path in Paths())
            if ((bool)path[3]) yield return path;
    }

    public static IEnumerable<object[]> Contracts()
    {
        foreach (var (declaration, validation) in CopiedAttributeIdentityFixtures.Contracts())
            yield return new object[] { declaration, validation };
    }

    [Theory]
    [MemberData(nameof(Sources))]
    public void SourceLookupsRecognizeDtdDeclarations(string declaration, string validation, bool declared)
    {
        // The copy policy is deliberately irrelevant at this source-only positive control.
        _ = validation;
        foreach (bool legacy in new[] { false, true })
        {
            using var input = CopiedAttributeIdentityFixtures.Reader(declaration, declared);
            string expected = declaration == "ID" ? "1|0|1" : "0|1|1";
            Assert.Equal(expected, Run(CopiedAttributeIdentityFixtures.SourceLookupSheet(), input,
                legacy, debug: false, forward: false));
        }
    }

    [Theory]
    [MemberData(nameof(Contracts))]
    public void IdentityMetadataAlsoSurvivesTypedParentlessCopies(string declaration, string validation)
    {
        var type = XmlSchemaType.GetBuiltInSimpleType(new XmlQualifiedName(declaration, "http://www.w3.org/2001/XMLSchema"));
        Assert.NotNull(type);
        string value = declaration == "IDREFS" ? "target other" : "target";
        var builder = new CompactDocument.Builder(new NameTable(), "", fragment: true, small: true);
        builder.Attribute("", "identity", "", value);
        builder.AnnotateAttribute(type, value);
        var original = (CompactNavigator)builder.Finish().CreateNavigator();
        AssertIdentity(original, declaration);
        var copy = Assert.IsType<CompactNavigator>(NodeValidator.Preserve(original, copyNamespaces: true, keepTypes: validation == "preserve"));
        AssertIdentity(copy, declaration);
        Assert.False(copy.MoveToParent());
        Assert.Equal(value, copy.Value);
        Assert.False(original.IsSamePosition(copy));
        Assert.Equal(validation == "preserve", copy.SchemaInfo?.SchemaType != null);
    }

    [Theory]
    [MemberData(nameof(Sources))]
    public void SourceAndDetachedCopyKeepDtdIdentity(string declaration, string validation, bool declared)
    {
        using var reader = CopiedAttributeIdentityFixtures.Reader(declaration, declared);
        var source = Assert.IsType<CompactNavigator>(XdmNode.Load(reader).Navigator);
        Assert.True(source.MoveToFirstChild());
        Assert.True(source.MoveToFirstChild());
        Assert.True(source.MoveToAttribute("identity", CopiedAttributeIdentityFixtures.AttributeNamespace));
        AssertIdentity(source, declaration);
        Assert.Equal(declaration == "ID", source.Clone().MoveToId("target"));
        XPathNavigator copy = NodeValidator.Preserve(source, copyNamespaces: true, keepTypes: validation == "preserve");
        AssertIdentity(Assert.IsType<CompactNavigator>(copy), declaration);
        Assert.False(copy.MoveToParent());
        Assert.False(source.IsSamePosition(copy));
        Assert.Equal(source.Name, copy.Name);
        Assert.Equal(source.Value, copy.Value);
        Assert.Null(copy.SchemaInfo);
        Assert.NotEqual(ReadState.Closed, reader.ReadState);
    }

    [Theory]
    [MemberData(nameof(Paths))]
    public void RoundTripsKeepIdentityAcrossBothConstructionPolicies(string declaration, string validation, bool declared,
        bool legacy, bool debug, bool forward, bool positioned)
    {
        using var reader = CopiedAttributeIdentityFixtures.Reader(declaration, declared, positioned);
        string actual = Run(CopiedAttributeIdentityFixtures.RoundTripSheet(validation), reader,
            legacy, debug, forward);
        Assert.Equal(CopiedAttributeIdentityFixtures.Expected(declaration), actual);
        Assert.NotEqual(ReadState.Closed, reader.ReadState);
    }

    [Theory]
    [MemberData(nameof(Sources))]
    public void ReturnedCopiesRetainFlagsAfterReaderCloses(string declaration, string validation, bool declared)
    {
        foreach (bool debug in new[] { false, true })
        foreach (bool forward in new[] { false, true })
        {
            XdmValue result;
            using (var reader = CopiedAttributeIdentityFixtures.Reader(declaration, declared))
                result = new XsltCompiler { Settings = new() { IncludeDebugInformation = debug, StreamedExecution = forward } }
                    .Compile(new StringReader(CopiedAttributeIdentityFixtures.DetachedSheet(validation)))
                    .CreateTransformer().ApplyTemplates(reader);
            Assert.Equal(2, result.Count);
            var first = Assert.IsType<CompactNavigator>(Assert.IsType<XdmNode>(result[0]).Navigator);
            var second = Assert.IsType<CompactNavigator>(Assert.IsType<XdmNode>(result[1]).Navigator);
            AssertIdentity(first, declaration);
            AssertIdentity(second, declaration);
            Assert.False(first.MoveToParent());
            Assert.False(second.MoveToParent());
            Assert.False(first.IsSamePosition(second));
        }
    }

#if NET10_0_OR_GREATER
    [Theory]
    [MemberData(nameof(SavedPaths))]
    public void SavedRoundTripsKeepIdentity(string declaration, string validation, bool declared,
        bool legacy, bool debug, bool forward, bool positioned)
    {
        // The saved assembly API is XslCompiledTransform.Load(Type). Source compilation of both
        // public APIs is covered above; the saved consumer has no runtime stylesheet compilation.
        using var reader = CopiedAttributeIdentityFixtures.Reader(declaration, declared, positioned);
        Assert.Equal(CopiedAttributeIdentityFixtures.Expected(declaration),
            Run(CopiedAttributeIdentityFixtures.RoundTripSheet(validation), reader, legacy, debug, forward, saved: true));
        Assert.NotEqual(ReadState.Closed, reader.ReadState);
    }
#endif

    private static void AssertIdentity(CompactNavigator attribute, string declaration)
    {
        Assert.Equal(declaration == "ID", attribute.IsIdAttribute);
        Assert.Equal(declaration != "ID", attribute.IsIdrefAttribute);
    }

    private static string Run(string sheet, XmlReader input, bool legacy, bool debug, bool forward, bool saved = false)
    {
        var settings = new XsltSettings { IncludeDebugInformation = debug, StreamedExecution = forward };
        using var output = new StringWriter();
        if (legacy)
        {
            var transform = new XslCompiledTransform(debug);
            using var stylesheet = XmlReader.Create(new StringReader(sheet));
            if (saved)
            {
#if NET10_0_OR_GREATER
                using var image = new MemoryStream();
                transform.CompileToAssembly(stylesheet, settings, null, "P7G" + Guid.NewGuid().ToString("N"), "Saved.Contract", image);
                Type type = Assembly.Load(image.ToArray()).GetType("Saved.Contract", true)!;
                var data = (byte[])type.GetField(Runtime.XmlQueryStaticData.DataFieldName, BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
                Assert.Equal(forward, new Runtime.XmlQueryStaticData(data, null).StreamedInitialMode);
                transform.Load(type, settings);
#else
                throw new NotSupportedException("Saved emission requires .NET 9 or later.");
#endif
            }
            else transform.Load(stylesheet, settings, null);
            transform.Transform(input, null, output);
        }
        else new XsltCompiler { Settings = settings }.Compile(new StringReader(sheet)).CreateTransformer().ApplyTemplates(input, output);
        return output.ToString();
    }
}
