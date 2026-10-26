// Licensed under the MIT license. See LICENSE.TXT in the repository root.

#if NET9_0_OR_GREATER
using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Xml;

namespace Xsl3;

/// <summary>References the engine's common BCL contract rather than the producer's runtime patch level.</summary>
internal static class SavedAssemblyReferences
{
    internal static Version MinimumVersion => Version.Parse(typeof(XslCompiledTransform).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(attribute => attribute.Key == "Xsl3.MinimumSavedRuntimeVersion").Value!);

    internal static void Normalize(byte[] image)
    {
        Version target = MinimumVersion;
        AssemblyName[] framework = [typeof(object).Assembly.GetName(), typeof(XmlReader).Assembly.GetName()];
        using var stream = new MemoryStream(image, writable: false);
        using var pe = new PEReader(stream);
        MetadataReader metadata = pe.GetMetadataReader();
        int start = pe.PEHeaders.MetadataStartOffset + metadata.GetTableMetadataOffset(TableIndex.AssemblyRef);
        int size = metadata.GetTableRowSize(TableIndex.AssemblyRef);
        foreach (AssemblyReferenceHandle handle in metadata.AssemblyReferences)
        {
            AssemblyReference reference = metadata.GetAssemblyReference(handle);
            string name = metadata.GetString(reference.Name);
            AssemblyName? owner = framework.FirstOrDefault(assembly => assembly.Name == name);
            if (owner == null || reference.Version <= target ||
                metadata.GetString(reference.Culture) != (owner.CultureName ?? "") ||
                !metadata.GetBlobBytes(reference.PublicKeyOrToken).SequenceEqual((reference.Flags.HasFlag(AssemblyFlags.PublicKey) ? owner.GetPublicKey() : owner.GetPublicKeyToken()) ?? [])) continue;

            // ECMA-335 II.22.5: AssemblyRef starts with four UInt16 version components. Its remaining
            // columns, all heap handles, IL, initialized data and embedded PDB bytes retain their positions.
            int row = start + (MetadataTokens.GetRowNumber(handle) - 1) * size;
            BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(row, 2), checked((ushort)target.Major));
            BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(row + 2, 2), checked((ushort)target.Minor));
            BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(row + 4, 2), checked((ushort)target.Build));
            BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(row + 6, 2), checked((ushort)target.Revision));
        }
    }
}
#endif
