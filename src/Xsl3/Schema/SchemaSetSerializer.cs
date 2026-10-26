// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Schema;

namespace Xsl3.Schema;

/// <summary>Saves resolved schema modules without requiring their original resources when loading.</summary>
internal static class SchemaSetSerializer
{
    private const int FormatVersion = 1;

    internal static void Write(BinaryWriter writer, XmlSchemaSet schemas)
    {
        RequireCompiled(schemas);
        XmlSchema[] roots = schemas.Schemas().Cast<XmlSchema>().ToArray();
        var graph = new ModuleGraph(roots);
        writer.Write(FormatVersion);
        writer.Write(schemas.CompilationSettings.EnableUpaCheck);
        writer.Write(roots.Length);
        graph.Write(writer);
    }

    internal static XmlSchemaSet Read(BinaryReader reader)
    {
        ReadVersion(reader);
        bool upa = reader.ReadBoolean();
        int roots = reader.ReadInt32();
        if (roots < 0) throw new InvalidDataException("Invalid saved schema graph size");
        ModuleGraph graph = ModuleGraph.Read(reader, roots);
        return Build(graph, Enumerable.Range(0, roots).ToArray(), upa, compiled: true);
    }

    /// <summary>All sets in a table share one module graph, preserving shared schema-type identities.</summary>
    internal static void WriteBundle(BinaryWriter writer, IReadOnlyList<XmlSchemaSet> schemas)
    {
        var roots = schemas.Select(set => set.Schemas().Cast<XmlSchema>().ToArray()).ToArray();
        foreach (XmlSchemaSet set in schemas) if (set.Count != 0) RequireCompiled(set);
        var graph = new ModuleGraph(roots.SelectMany(set => set));
        writer.Write(FormatVersion);
        writer.Write(schemas.Count);
        for (int i = 0; i < schemas.Count; i++)
        {
            writer.Write(schemas[i].IsCompiled);
            writer.Write(schemas[i].CompilationSettings.EnableUpaCheck);
            writer.Write(roots[i].Length);
            foreach (XmlSchema root in roots[i]) writer.Write(graph.Index(root));
        }
        graph.Write(writer);
    }

    internal static XmlSchemaSet[] ReadBundle(BinaryReader reader)
    {
        ReadVersion(reader);
        int count = reader.ReadInt32();
        if (count < 0) throw new InvalidDataException("Invalid saved schema-set count");
        var sets = new (bool Compiled, bool Upa, int[] Roots)[count];
        for (int i = 0; i < count; i++)
        {
            bool compiled = reader.ReadBoolean(), upa = reader.ReadBoolean();
            int length = reader.ReadInt32();
            if (length < 0 || length != 0 && !compiled)
                throw new InvalidDataException("Invalid saved schema-set roots");
            var roots = new int[length];
            for (int j = 0; j < length; j++) roots[j] = reader.ReadInt32();
            sets[i] = (compiled, upa, roots);
        }
        ModuleGraph graph = ModuleGraph.Read(reader);
        return sets.Select(set => Build(graph, set.Roots, set.Upa, set.Compiled)).ToArray();
    }

    private static XmlSchemaSet Build(ModuleGraph graph, int[] roots, bool upa, bool compiled)
    {
        var set = new XmlSchemaSet { XmlResolver = null };
        set.CompilationSettings.EnableUpaCheck = upa;
        foreach (int index in roots)
        {
            if (index < 0 || index >= graph.Count) throw new InvalidDataException("Invalid saved schema root");
            set.Add(graph[index]);
        }
        if (compiled) set.Compile();
        return set;
    }

    private static void RequireCompiled(XmlSchemaSet schemas)
    {
        if (!schemas.IsCompiled) throw new NotSupportedException("Schemas must be compiled before they can be saved");
    }

    private static void ReadVersion(BinaryReader reader)
    {
        if (reader.ReadInt32() != FormatVersion) throw new NotSupportedException("Unsupported saved schema graph version");
    }

    private sealed class ModuleGraph
    {
        private readonly List<XmlSchema> _modules = new();
        private readonly Dictionary<XmlSchema, int> _identities = new(ReferenceEqualityComparer.Instance);
        internal int Count => _modules.Count;
        internal XmlSchema this[int index] => _modules[index];
        internal int Index(XmlSchema schema) => _identities[schema];

        internal ModuleGraph(IEnumerable<XmlSchema> roots)
        {
            foreach (XmlSchema root in roots) Add(root);
            for (int i = 0; i < _modules.Count; i++)
                foreach (XmlSchemaExternal external in _modules[i].Includes)
                    if (external.Schema is { } schema) Add(schema);
        }

        private void Add(XmlSchema schema)
        {
            if (_identities.TryAdd(schema, _modules.Count)) _modules.Add(schema);
        }

        internal void Write(BinaryWriter writer)
        {
            writer.Write(Count);
            foreach (XmlSchema schema in _modules)
            {
                writer.Write(schema.SourceUri ?? "");
                using var text = new StringWriter(CultureInfo.InvariantCulture);
                using (XmlWriter xml = XmlWriter.Create(text, new XmlWriterSettings { OmitXmlDeclaration = true })) schema.Write(xml);
                writer.Write(text.ToString());
                writer.Write(schema.Includes.Count);
                foreach (XmlSchemaExternal external in schema.Includes)
                    writer.Write(external.Schema is { } target ? Index(target) : -1);
            }
        }

        internal static ModuleGraph Read(BinaryReader reader, int roots = -1)
        {
            int count = reader.ReadInt32();
            if (count < 0 || roots >= 0 && (roots > count || roots == 0 && count != 0)) throw new InvalidDataException("Invalid saved schema graph size");
            var graph = new ModuleGraph(Array.Empty<XmlSchema>());
            var edges = new int[count][];
            for (int i = 0; i < count; i++)
            {
                string sourceUri = reader.ReadString(), text = reader.ReadString();
                using XmlReader xml = XmlReader.Create(new StringReader(text), new XmlReaderSettings
                    { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }, sourceUri);
                XmlSchema schema = XmlSchema.Read(xml, null) ?? throw new InvalidDataException("Missing saved schema document");
                graph.Add(schema);
                int length = reader.ReadInt32();
                if (length != schema.Includes.Count) throw new InvalidDataException("Saved schema references do not match its document");
                edges[i] = new int[length];
                for (int j = 0; j < length; j++)
                {
                    int target = reader.ReadInt32();
                    if (target < -1 || target >= count) throw new InvalidDataException("Invalid saved schema reference");
                    edges[i][j] = target;
                }
            }
            for (int i = 0; i < count; i++)
                for (int j = 0; j < edges[i].Length; j++)
                    if (edges[i][j] >= 0) ((XmlSchemaExternal)graph[i].Includes[j]).Schema = graph[edges[i][j]];
            return graph;
        }
    }
}
