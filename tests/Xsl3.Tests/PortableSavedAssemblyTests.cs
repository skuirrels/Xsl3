// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Xml;
using Xunit;

namespace Xsl3.Tests;

public class PortableSavedAssemblyTests
{
#if NET9_0_OR_GREATER
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SavedStylesheetsReferenceTheMinimumSupportedFramework(bool debug)
    {
        const string sheet = "<xsl:stylesheet version='3.0' xmlns:xsl='http://www.w3.org/1999/XSL/Transform'><xsl:output method='text'/><xsl:template match='/'><xsl:try><xsl:source-document streamable='yes' href='urn:fixture:input'><xsl:value-of select='count(//*)'/></xsl:source-document><xsl:catch>error</xsl:catch></xsl:try></xsl:template></xsl:stylesheet>";
        using var image = new MemoryStream();
        using (XmlReader source = XmlReader.Create(new StringReader(sheet), new XmlReaderSettings(), "file:///portable/input.xsl"))
            new XslCompiledTransform(debug).CompileToAssembly(source, new() { EnableDocumentFunction = true, StreamedExecution = true }, null,
                "PortableFramework", "Portable.Style", image);
        image.Position = 0;
        using var pe = new PEReader(image);
        MetadataReader metadata = pe.GetMetadataReader();
        var xml = metadata.AssemblyReferences.Select(metadata.GetAssemblyReference)
            .Single(reference => metadata.GetString(reference.Name) == typeof(XmlReader).Assembly.GetName().Name);
        Assert.Equal(new Version(8, 0, 0, 0), xml.Version);
        if (debug)
        {
            DebugDirectoryEntry entry = Assert.Single(pe.ReadDebugDirectory().Where(e => e.Type == DebugDirectoryEntryType.EmbeddedPortablePdb));
            using MetadataReaderProvider symbols = pe.ReadEmbeddedPortablePdbDebugDirectoryData(entry);
            MetadataReader pdb = symbols.GetMetadataReader();
            Assert.Contains(pdb.Documents, document => pdb.GetString(pdb.GetDocument(document).Name) == new Uri("file:///portable/input.xsl").LocalPath);
            Assert.Contains(pdb.MethodDebugInformation, method => pdb.GetMethodDebugInformation(method).GetSequencePoints().Any(point => !point.IsHidden));
        }
    }

    [Fact]
    public void ReferenceNormalizationOnlyChangesFrameworkVersionColumns()
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("MetadataShape"), typeof(object).Assembly);
        TypeBuilder type = assembly.DefineDynamicModule("MetadataShape").DefineType("Shape", TypeAttributes.Public);
        MethodBuilder method = type.DefineMethod("Echo", MethodAttributes.Public | MethodAttributes.Static, typeof(XmlReader), [typeof(XmlReader)]);
        ILGenerator il = method.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ret); type.CreateType();
        using var stream = new MemoryStream(); assembly.Save(stream);
        byte[] before = stream.ToArray(), after = (byte[])before.Clone();
        SavedAssemblyReferences.Normalize(after);
        Assert.NotEqual(before, after);
        using var pe = new PEReader(new MemoryStream(before));
        MetadataReader metadata = pe.GetMetadataReader();
        int table = pe.PEHeaders.MetadataStartOffset + metadata.GetTableMetadataOffset(TableIndex.AssemblyRef);
        int size = metadata.GetTableRowSize(TableIndex.AssemblyRef);
        int[] versionBytes = metadata.AssemblyReferences.SelectMany(handle => Enumerable.Range(table + (MetadataTokens.GetRowNumber(handle) - 1) * size, 8)).ToArray();
        for (int i = 0; i < before.Length; i++)
            if (!versionBytes.Contains(i)) Assert.Equal(before[i], after[i]);
        using var portable = new PEReader(new MemoryStream(after));
        Assert.Equal(pe.GetMethodBody(metadata.GetMethodDefinition(metadata.MethodDefinitions.Single(handle => metadata.GetString(metadata.GetMethodDefinition(handle).Name) == "Echo")).RelativeVirtualAddress).GetILBytes(),
            portable.GetMethodBody(portable.GetMetadataReader().GetMethodDefinition(metadata.MethodDefinitions.Single(handle => metadata.GetString(metadata.GetMethodDefinition(handle).Name) == "Echo")).RelativeVirtualAddress).GetILBytes());
    }

    [Fact]
    public void UnrelatedAssemblyVersionsArePreserved()
    {
        var externalName = new AssemblyName("ExternalSavedContract") { Version = new Version(42, 1, 2, 3) };
        AssemblyBuilder external = AssemblyBuilder.DefineDynamicAssembly(externalName, AssemblyBuilderAccess.Run);
        Type externalType = external.DefineDynamicModule("External").DefineType("External.Contract", TypeAttributes.Public).CreateType()!;
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("ReferenceIsolation"), typeof(object).Assembly);
        TypeBuilder type = assembly.DefineDynamicModule("ReferenceIsolation").DefineType("Shape", TypeAttributes.Public);
        type.DefineField("External", externalType, FieldAttributes.Public);
        type.CreateType();
        using var stream = new MemoryStream(); assembly.Save(stream);
        byte[] image = stream.ToArray(); SavedAssemblyReferences.Normalize(image);
        using var pe = new PEReader(new MemoryStream(image));
        MetadataReader metadata = pe.GetMetadataReader();
        AssemblyReference reference = metadata.AssemblyReferences.Select(metadata.GetAssemblyReference)
            .Single(candidate => metadata.GetString(candidate.Name) == externalName.Name);
        Assert.Equal(externalName.Version, reference.Version);
    }
#endif
}
