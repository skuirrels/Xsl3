// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Xsl3.IlGen;
using Xsl3.Qil;

namespace Xsl3.Runtime
{
    /// <summary>
    /// Contains all static data that is used by the runtime.
    /// </summary>
    internal sealed class XmlQueryStaticData
    {
        // Name of the field to serialize to
        public const string DataFieldName = "staticData";
        public const string TypesFieldName = "ebTypes";

        // Format version marker to support versioning: (major << 8) | minor
        // Xsl3: minor version 1 adds the 2.0+ static data and constants (Sink B); 2 the stylesheet frames.
        private const int CurrentFormatVersion = (0 << 8) | 6; // 3: function library fingerprint; 4: streamed initial mode; 5: accumulator streamability; 6: built-in ID usage

        private readonly XmlWriterSettings _defaultWriterSettings;
        private readonly IList<WhitespaceRule>? _whitespaceRules;
        private readonly string[]? _names;
        private readonly StringPair[][]? _prefixMappingsList;
        private readonly Int32Pair[]? _filters;
        private readonly XmlQueryType[]? _types;
        private readonly XmlCollation[]? _collations;
        private readonly string[]? _globalNames;
        private readonly EarlyBoundInfo[]? _earlyBound;

        /// <summary>
        /// Constructor.
        /// </summary>
        [RequiresUnreferencedCode("This method will create a copy that uses earlybound types which cannot be statically analyzed.")]
        public XmlQueryStaticData(XmlWriterSettings defaultWriterSettings, IList<WhitespaceRule> whitespaceRules, StaticDataManager staticData)
        {
            Debug.Assert(defaultWriterSettings != null && staticData != null);
            _defaultWriterSettings = defaultWriterSettings;
            _whitespaceRules = whitespaceRules;
            _names = staticData.Names;
            _prefixMappingsList = staticData.PrefixMappingsList;
            _filters = staticData.NameFilters;
            _types = staticData.XmlTypes;
            _collations = staticData.Collations;
            _globalNames = staticData.GlobalNames;
            _earlyBound = staticData.EarlyBound;
            UsesBuiltinFunctionIds = staticData.UsesBuiltinFunctionIds;

#if DEBUG
            // Round-trip check
            byte[] data;
            Type[]? ebTypes;
            this.GetObjectData(out data, out ebTypes);
            XmlQueryStaticData copy = new XmlQueryStaticData(data, ebTypes);

            _defaultWriterSettings = copy._defaultWriterSettings;
            _whitespaceRules = copy._whitespaceRules;
            _names = copy._names;
            _prefixMappingsList = copy._prefixMappingsList;
            _filters = copy._filters;
            _types = copy._types;
            _collations = copy._collations;
            _globalNames = copy._globalNames;
            _earlyBound = copy._earlyBound;
#endif
        }

        /// <summary>
        /// Deserialize XmlQueryStaticData object from a byte array.
        /// </summary>
        [RequiresUnreferencedCode("This method will create EarlyBoundInfo from passed in ebTypes array which cannot be statically analyzed.")]
        public XmlQueryStaticData(byte[] data, Type[]? ebTypes)
        {
            MemoryStream dataStream = new MemoryStream(data, writable: false);
            XmlQueryDataReader dataReader = new XmlQueryDataReader(dataStream);
            int length;

            // Read a format version
            int formatVersion = dataReader.Read7BitEncodedInt();

            // Changes in the major part of version are not supported
            if ((formatVersion & ~0xff) > CurrentFormatVersion)
                throw new NotSupportedException();

            int version = formatVersion & 0xff;
            int fingerprint = version >= 3 ? dataReader.ReadInt32() : 0;
            // Older formats carry no usage proof, so preserve their unconditional check.
            if (version is >= 3 and < 6) ValidateFunctionFingerprint(fingerprint);

            // XmlWriterSettings defaultWriterSettings;
            _defaultWriterSettings = XmlWriterSettingsInternals.Create(dataReader);

            // IList<WhitespaceRule> whitespaceRules;
            length = dataReader.ReadInt32();
            if (length != 0)
            {
                _whitespaceRules = new WhitespaceRule[length];
                for (int idx = 0; idx < length; idx++)
                {
                    _whitespaceRules[idx] = new WhitespaceRule(dataReader);
                }
            }

            // string[] names;
            length = dataReader.ReadInt32();
            if (length != 0)
            {
                _names = new string[length];
                for (int idx = 0; idx < length; idx++)
                {
                    _names[idx] = dataReader.ReadString();
                }
            }

            // StringPair[][] prefixMappingsList;
            length = dataReader.ReadInt32();
            if (length != 0)
            {
                _prefixMappingsList = new StringPair[length][];
                for (int idx = 0; idx < length; idx++)
                {
                    int length2 = dataReader.ReadInt32();
                    _prefixMappingsList[idx] = new StringPair[length2];
                    for (int idx2 = 0; idx2 < length2; idx2++)
                    {
                        _prefixMappingsList[idx][idx2] = new StringPair(dataReader.ReadString(), dataReader.ReadString());
                    }
                }
            }

            // Int32Pair[] filters;
            length = dataReader.ReadInt32();
            if (length != 0)
            {
                _filters = new Int32Pair[length];
                for (int idx = 0; idx < length; idx++)
                {
                    _filters[idx] = new Int32Pair(dataReader.Read7BitEncodedInt(), dataReader.Read7BitEncodedInt());
                }
            }

            // XmlQueryType[] types;
            length = dataReader.ReadInt32();
            if (length != 0)
            {
                _types = new XmlQueryType[length];
                for (int idx = 0; idx < length; idx++)
                {
                    _types[idx] = XmlQueryTypeFactory.Deserialize(dataReader);
                }
            }

            // XmlCollation[] collations;
            length = dataReader.ReadInt32();
            if (length != 0)
            {
                _collations = new XmlCollation[length];
                for (int idx = 0; idx < length; idx++)
                {
                    _collations[idx] = new XmlCollation(dataReader);
                }
            }

            // string[] globalNames;
            length = dataReader.ReadInt32();
            if (length != 0)
            {
                _globalNames = new string[length];
                for (int idx = 0; idx < length; idx++)
                {
                    _globalNames[idx] = dataReader.ReadString();
                }
            }

            // EarlyBoundInfo[] earlyBound;
            length = dataReader.ReadInt32();
            if (length != 0)
            {
                _earlyBound = new EarlyBoundInfo[length];
                for (int idx = 0; idx < length; idx++)
                {
                    _earlyBound[idx] = new EarlyBoundInfo(dataReader.ReadString(), ebTypes![idx]);
                }
            }

            if ((formatVersion & 0xff) >= 1)
            {
                // Xsl3 (Sink B): the 2.0+ static data.
                Xslt2 = dataReader.ReadBoolean();
                CompactTree = dataReader.ReadBoolean();
                ResultDocuments = dataReader.ReadBoolean();
                OutputExtras = Xsl3.XPath31.ConstantSerializer.ReadOutputExtras(dataReader);
                if (dataReader.ReadBoolean())
                {
                    Constants = Xsl3.XPath31.ConstantSerializer.Read(dataReader);
                }
                string dispatcher = dataReader.ReadString();
                DispatcherName = dispatcher.Length > 0 ? dispatcher : null;
            }
            if ((formatVersion & 0xff) >= 2 && dataReader.ReadInt32() is int frames and > 0)
            {
                Frames = new Dictionary<string, int>(frames);
                for (int idx = 0; idx < frames; idx++)
                {
                    Frames[dataReader.ReadString()] = dataReader.ReadInt32();
                }
            }

            if ((formatVersion & 0xff) >= 4) StreamedInitialMode = dataReader.ReadBoolean();
            UsesBuiltinFunctionIds = version < 6 || dataReader.ReadBoolean();
            if (version >= 6 && UsesBuiltinFunctionIds) ValidateFunctionFingerprint(fingerprint);

            Debug.Assert(formatVersion != CurrentFormatVersion || dataReader.Read() == -1, "Extra data at the end of the stream");
            dataReader.Dispose();
        }

        private static void ValidateFunctionFingerprint(int fingerprint)
        {
            if (fingerprint != Xsl3.XPath31.Functions.FunctionLibrary.Fingerprint)
                throw new NotSupportedException("The stylesheet was compiled by another version of Xsl3, whose function library differs: compile it again with this one.");
        }

        // The IL emitter proves absence of numeric references; names resolved at run time
        // do not depend on a previous engine's registry ordering.
        public bool UsesBuiltinFunctionIds { get; private set; }

        /// <summary>
        /// Serialize XmlQueryStaticData object into a byte array.
        /// </summary>
        public void GetObjectData(out byte[] data, out Type[]? ebTypes)
        {
            MemoryStream dataStream = new MemoryStream(4096);
            XmlQueryDataWriter dataWriter = new XmlQueryDataWriter(dataStream);

            // First put the format version
            dataWriter.Write7BitEncodedInt(CurrentFormatVersion);
            dataWriter.Write(Xsl3.XPath31.Functions.FunctionLibrary.Fingerprint); // Xsl3: version 3

            // XmlWriterSettings defaultWriterSettings;
            _defaultWriterSettings.GetObjectData(dataWriter);

            // IList<WhitespaceRule> whitespaceRules;
            if (_whitespaceRules == null)
            {
                dataWriter.Write(0);
            }
            else
            {
                dataWriter.Write(_whitespaceRules.Count);
                foreach (WhitespaceRule rule in _whitespaceRules)
                {
                    rule.GetObjectData(dataWriter);
                }
            }

            // string[] names;
            if (_names == null)
            {
                dataWriter.Write(0);
            }
            else
            {
                dataWriter.Write(_names.Length);
                foreach (string name in _names)
                {
                    dataWriter.Write(name);
                }
            }

            // StringPair[][] prefixMappingsList;
            if (_prefixMappingsList == null)
            {
                dataWriter.Write(0);
            }
            else
            {
                dataWriter.Write(_prefixMappingsList.Length);
                foreach (StringPair[] mappings in _prefixMappingsList)
                {
                    dataWriter.Write(mappings.Length);
                    foreach (StringPair mapping in mappings)
                    {
                        dataWriter.Write(mapping.Left);
                        dataWriter.Write(mapping.Right);
                    }
                }
            }

            // Int32Pair[] filters;
            if (_filters == null)
            {
                dataWriter.Write(0);
            }
            else
            {
                dataWriter.Write(_filters.Length);
                foreach (Int32Pair filter in _filters)
                {
                    dataWriter.Write7BitEncodedInt(filter.Left);
                    dataWriter.Write7BitEncodedInt(filter.Right);
                }
            }

            // XmlQueryType[] types;
            if (_types == null)
            {
                dataWriter.Write(0);
            }
            else
            {
                dataWriter.Write(_types.Length);
                foreach (XmlQueryType type in _types)
                {
                    XmlQueryTypeFactory.Serialize(dataWriter, type);
                }
            }

            // XmlCollation[] collations;
            if (_collations == null)
            {
                dataWriter.Write(0);
            }
            else
            {
                dataWriter.Write(_collations.Length);
                foreach (XmlCollation collation in _collations)
                {
                    collation.GetObjectData(dataWriter);
                }
            }

            // string[] globalNames;
            if (_globalNames == null)
            {
                dataWriter.Write(0);
            }
            else
            {
                dataWriter.Write(_globalNames.Length);
                foreach (string name in _globalNames)
                {
                    dataWriter.Write(name);
                }
            }

            // EarlyBoundInfo[] earlyBound;
            if (_earlyBound == null)
            {
                dataWriter.Write(0);
                ebTypes = null;
            }
            else
            {
                dataWriter.Write(_earlyBound.Length);
                ebTypes = new Type[_earlyBound.Length];
                int idx = 0;
                foreach (EarlyBoundInfo info in _earlyBound)
                {
                    dataWriter.Write(info.NamespaceUri);
                    ebTypes[idx++] = info.EarlyBoundType;
                }
            }

            // Xsl3 (Sink B): the 2.0+ static data, the constants and the dispatcher's method name.
            dataWriter.Write(Xslt2);
            dataWriter.Write(CompactTree);
            dataWriter.Write(ResultDocuments);
            Xsl3.XPath31.ConstantSerializer.WriteOutputExtras(dataWriter, OutputExtras);
            dataWriter.Write(Constants != null);
            if (Constants != null)
            {
                Xsl3.XPath31.ConstantSerializer.Write(dataWriter, Constants);
            }
            dataWriter.Write(DispatcherName ?? "");
            dataWriter.Write(Frames?.Count ?? 0);
            if (Frames != null)
            {
                foreach (KeyValuePair<string, int> frame in Frames)
                {
                    dataWriter.Write(frame.Key);
                    dataWriter.Write(frame.Value);
                }
            }
            dataWriter.Write(StreamedInitialMode);
            // Append-only: older readers retain their conservative fingerprint check.
            dataWriter.Write(UsesBuiltinFunctionIds);
            dataWriter.Dispose();
            data = dataStream.ToArray();
        }

        /// <summary>Xsl3: serialization parameters the result writer applies (XSLT 2.0+), or null.</summary>
        public OutputExtras? OutputExtras { get; set; }

        /// <summary>Xsl3: the stylesheet has xsl:result-document instructions (XSLT 2.0+).</summary>
        public bool ResultDocuments { get; set; }

        /// <summary>
        /// Xsl3: the compiled dispatcher of function items (docs/plan/p3-xslt30.md, decision 4), so that the run-time
        /// library can call them (folds, sort keys, …).
        /// </summary>
        public System.Func<XmlQueryRuntime, System.Xml.XPath.XPathItem, IList<System.Xml.XPath.XPathItem>>? Dispatcher { get; set; }

        /// <summary>
        /// Xsl3: the stylesheet frames (XsltFrame constants) of templates and functions by the names of the methods they
        /// compiled to, for the stylesheet stack of a dynamic error without tracing.
        /// </summary>
        public Dictionary<string, int>? Frames { get; set; }

        /// <summary>Xsl3 (Sink B): in a saved assembly, the name of the dispatcher's method, bound when it is loaded.</summary>
        public string? DispatcherName { get; set; }

        /// <summary>Xsl3: compiled by the XSLT 2.0+ path (its principal result is a <see cref="PrincipalResult"/>).</summary>
        public bool Xslt2 { get; set; }

        /// <summary>The selected initial mode accepts a forward reader input.</summary>
        internal bool StreamedInitialMode { get; set; }

        /// <summary>Xsl3 (P4): build the documents the transform parses as compact trees (always for 2.0+).</summary>
        public bool CompactTree { get; set; }

        /// <summary>Xsl3: the stylesheet compilation's constants (ConstantPool); null for the process-wide table.</summary>
        public Xsl3.XPath31.ConstantTable? Constants { get; set; }

        /// <summary>
        /// Return the default writer settings.
        /// </summary>
        public XmlWriterSettings DefaultWriterSettings
        {
            get { return _defaultWriterSettings; }
        }

        /// <summary>
        /// Return the rules used for whitespace stripping/preservation.
        /// </summary>
        public IList<WhitespaceRule>? WhitespaceRules
        {
            get { return _whitespaceRules; }
        }

        /// <summary>
        /// Return array of names used by this query.
        /// </summary>
        public string[]? Names
        {
            get { return _names; }
        }

        /// <summary>
        /// Return array of prefix mappings used by this query.
        /// </summary>
        public StringPair[][]? PrefixMappingsList
        {
            get { return _prefixMappingsList; }
        }

        /// <summary>
        /// Return array of name filter specifications used by this query.
        /// </summary>
        public Int32Pair[]? Filters
        {
            get { return _filters; }
        }

        /// <summary>
        /// Return array of types used by this query.
        /// </summary>
        public XmlQueryType[]? Types
        {
            get { return _types; }
        }

        /// <summary>
        /// Return array of collations used by this query.
        /// </summary>
        public XmlCollation[]? Collations
        {
            get { return _collations; }
        }

        /// <summary>
        /// Return names of all global variables and parameters used by this query.
        /// </summary>
        public string[]? GlobalNames
        {
            get { return _globalNames; }
        }

        /// <summary>
        /// Return array of early bound object information related to this query.
        /// </summary>
        public EarlyBoundInfo[]? EarlyBound
        {
            get { return _earlyBound; }
        }
    }

    /// <summary>
    /// Subclass of BinaryReader used to serialize query static data.
    /// </summary>
    internal sealed class XmlQueryDataReader : BinaryReader
    {
        public XmlQueryDataReader(Stream input) : base(input) { }

        /// <summary>
        /// Read a string value from the stream. Value can be null.
        /// </summary>
        public string? ReadStringQ()
        {
            return ReadBoolean() ? ReadString() : null;
        }

        /// <summary>
        /// Read a signed byte value from the stream and check if it belongs to the given diapason.
        /// </summary>
        public sbyte ReadSByte(sbyte minValue, sbyte maxValue)
        {
            sbyte value = ReadSByte();

            ArgumentOutOfRangeException.ThrowIfGreaterThan(minValue, value);
            ArgumentOutOfRangeException.ThrowIfLessThan(maxValue, value);

            return value;
        }
    }

    /// <summary>
    /// Subclass of BinaryWriter used to deserialize query static data.
    /// </summary>
    internal sealed class XmlQueryDataWriter : BinaryWriter
    {
        public XmlQueryDataWriter(Stream output) : base(output) { }

        /// <summary>
        /// Write a string value to the stream. Value can be null.
        /// </summary>
        public void WriteStringQ(string? value)
        {
            Write(value != null);
            if (value != null)
            {
                Write(value);
            }
        }
    }
}
