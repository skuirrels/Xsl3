// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Xsl3.Runtime;

namespace System.Xml
{
    /// <summary>
    /// The internal members of <see cref="XmlWriterSettings"/> that the XSLT engine and the vendored raw
    /// writers use, plus the writer-chain factory that upstream keeps in XmlWriterSettings.CreateWriter.
    /// </summary>
    /// <remarks>
    /// Xsl3 keeps handing out real <see cref="XmlWriterSettings"/> objects (XslCompiledTransform.OutputSettings
    /// is public API, and XmlWriter.Create must honour the XSLT-specific values in it exactly as it does for
    /// stock). So the XSLT-only state is read and written on the real object: through [UnsafeAccessor] where the
    /// signature uses public types, and through reflection where it uses System.Private.Xml's internal TriState
    /// and XmlStandalone enums. Only members that survive trimming of the shipped System.Private.Xml are used;
    /// the AutoXmlDeclaration getter, for one, is trimmed away, so its backing field is read instead.
    /// </remarks>
    internal static class XmlWriterSettingsInternals
    {
        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_ReadOnly")] private static extern bool GetReadOnly(XmlWriterSettings s);
        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_ReadOnly")] private static extern void SetReadOnly(XmlWriterSettings s, bool value);
        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_OutputMethod")] private static extern void SetOutputMethodCore(XmlWriterSettings s, XmlOutputMethod value);
        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_DocTypeSystem")] private static extern string? GetDocTypeSystem(XmlWriterSettings s);
        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_DocTypeSystem")] private static extern void SetDocTypeSystem(XmlWriterSettings s, string? value);
        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_DocTypePublic")] private static extern string? GetDocTypePublic(XmlWriterSettings s);
        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_DocTypePublic")] private static extern void SetDocTypePublic(XmlWriterSettings s, string? value);
        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_MediaType")] private static extern string? GetMediaType(XmlWriterSettings s);
        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_MediaType")] private static extern void SetMediaType(XmlWriterSettings s, string? value);
        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_MergeCDataSections")] private static extern bool GetMergeCDataSections(XmlWriterSettings s);
        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_MergeCDataSections")] private static extern void SetMergeCDataSections(XmlWriterSettings s, bool value);
        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_CDataSectionElements")] private static extern List<XmlQualifiedName> GetCDataSectionElements(XmlWriterSettings s);
        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_autoXmlDecl")] private static extern ref bool AutoXmlDeclField(XmlWriterSettings s);

        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;

        [DynamicDependency("Standalone", typeof(XmlWriterSettings))]
        [DynamicDependency("IndentInternal", typeof(XmlWriterSettings))]
        private static readonly PropertyInfo s_standalone = typeof(XmlWriterSettings).GetProperty("Standalone", Instance)!;
        private static readonly PropertyInfo s_indentInternal = typeof(XmlWriterSettings).GetProperty("IndentInternal", Instance)!;

        extension(XmlWriterSettings settings)
        {
            internal bool ReadOnly
            {
                get => GetReadOnly(settings);
                set => SetReadOnly(settings, value);
            }

            internal string? DocTypeSystem
            {
                get => GetDocTypeSystem(settings);
                set => SetDocTypeSystem(settings, value);
            }

            internal string? DocTypePublic
            {
                get => GetDocTypePublic(settings);
                set => SetDocTypePublic(settings, value);
            }

            internal string? MediaType
            {
                get => GetMediaType(settings);
                set => SetMediaType(settings, value);
            }

            internal bool MergeCDataSections
            {
                get => GetMergeCDataSections(settings);
                set => SetMergeCDataSections(settings, value);
            }

            internal List<XmlQualifiedName> CDataSectionElements => GetCDataSectionElements(settings);

            internal bool AutoXmlDeclaration
            {
                get => AutoXmlDeclField(settings);
                set => AutoXmlDeclField(settings) = value;
            }

            internal XmlStandalone Standalone
            {
                get => (XmlStandalone)Convert.ToInt32(s_standalone.GetValue(settings));
                set => s_standalone.SetValue(settings, Enum.ToObject(s_standalone.PropertyType, (int)value));
            }

            internal TriState IndentInternal
            {
                get => (TriState)Convert.ToInt32(s_indentInternal.GetValue(settings));
                set => s_indentInternal.SetValue(settings, Enum.ToObject(s_indentInternal.PropertyType, (int)value));
            }

            // Mirrors the private XmlWriterSettings.IsQuerySpecific.
            internal bool IsQuerySpecific => settings.CDataSectionElements.Count != 0 || settings.DocTypePublic != null || settings.DocTypeSystem != null || settings.Standalone == XmlStandalone.Yes;

            /// <summary>The setter of <see cref="XmlWriterSettings.OutputMethod"/> is internal.</summary>
            internal void SetOutputMethod(XmlOutputMethod value) => SetOutputMethodCore(settings, value);

            // Upstream: XmlWriterSettings.GetObjectData(XmlQueryDataWriter). The stock method takes System.Private.Xml's
            // own XmlQueryDataWriter, so it is reimplemented here against Xsl3's.
            internal void GetObjectData(XmlQueryDataWriter writer)
            {
                // Encoding encoding;
                // NOTE: For Encoding we serialize only CodePage, and ignore EncoderFallback/DecoderFallback.
                // It suffices for XSLT purposes, but not in the general case.
                // Xsl3: and whether it writes a byte order mark (xsl:output byte-order-mark).
                writer.Write(settings.Encoding.CodePage);
                writer.Write(settings.Encoding.GetPreamble().Length > 0);
                // bool omitXmlDecl;
                writer.Write(settings.OmitXmlDeclaration);
                // NewLineHandling newLineHandling;
                writer.Write((sbyte)settings.NewLineHandling);
                // string newLineChars;
                writer.WriteStringQ(settings.NewLineChars);
                // TriState indent;
                writer.Write((sbyte)settings.IndentInternal);
                // string indentChars;
                writer.WriteStringQ(settings.IndentChars);
                // bool newLineOnAttributes;
                writer.Write(settings.NewLineOnAttributes);
                // bool closeOutput;
                writer.Write(settings.CloseOutput);
                // ConformanceLevel conformanceLevel;
                writer.Write((sbyte)settings.ConformanceLevel);
                // bool checkCharacters;
                writer.Write(settings.CheckCharacters);
                // XmlOutputMethod outputMethod;
                writer.Write((sbyte)settings.OutputMethod);
                // List<XmlQualifiedName> cdataSections;
                writer.Write(settings.CDataSectionElements.Count);
                foreach (XmlQualifiedName qName in settings.CDataSectionElements)
                {
                    writer.Write(qName.Name);
                    writer.Write(qName.Namespace);
                }
                // bool mergeCDataSections;
                writer.Write(settings.MergeCDataSections);
                // string mediaType;
                writer.WriteStringQ(settings.MediaType);
                // string docTypeSystem;
                writer.WriteStringQ(settings.DocTypeSystem);
                // string docTypePublic;
                writer.WriteStringQ(settings.DocTypePublic);
                // XmlStandalone standalone;
                writer.Write((sbyte)settings.Standalone);
                // bool autoXmlDecl;
                writer.Write(settings.AutoXmlDeclaration);
                // bool isReadOnly;
                writer.Write(settings.ReadOnly);
            }
        }

        // Upstream: internal XmlWriterSettings(XmlQueryDataReader reader).
        internal static XmlWriterSettings Create(XmlQueryDataReader reader)
        {
            var settings = new XmlWriterSettings();
            // Encoding encoding;
            int codePage = reader.ReadInt32();
            bool preamble = reader.ReadBoolean();
            settings.Encoding = codePage switch
            {
                65001 => new UTF8Encoding(preamble),
                1200 or 1201 => new UnicodeEncoding(codePage == 1201, preamble),
                _ => Encoding.GetEncoding(codePage),
            };
            // bool omitXmlDecl;
            settings.OmitXmlDeclaration = reader.ReadBoolean();
            // NewLineHandling newLineHandling;
            settings.NewLineHandling = (NewLineHandling)reader.ReadSByte(0, (sbyte)NewLineHandling.None);
            // string newLineChars;
            settings.NewLineChars = reader.ReadStringQ()!;
            // TriState indent;
            settings.IndentInternal = (TriState)reader.ReadSByte((sbyte)TriState.Unknown, (sbyte)TriState.True);
            // string indentChars;
            settings.IndentChars = reader.ReadStringQ()!;
            // bool newLineOnAttributes;
            settings.NewLineOnAttributes = reader.ReadBoolean();
            // bool closeOutput;
            settings.CloseOutput = reader.ReadBoolean();
            // ConformanceLevel conformanceLevel;
            settings.ConformanceLevel = (ConformanceLevel)reader.ReadSByte(0, (sbyte)ConformanceLevel.Document);
            // bool checkCharacters;
            settings.CheckCharacters = reader.ReadBoolean();
            // XmlOutputMethod outputMethod;
            settings.SetOutputMethod((XmlOutputMethod)reader.ReadSByte(0, (sbyte)XmlOutputMethod.AutoDetect));
            // List<XmlQualifiedName> cdataSections;
            int length = reader.ReadInt32();
            List<XmlQualifiedName> cdataSections = settings.CDataSectionElements;
            cdataSections.Clear();
            for (int idx = 0; idx < length; idx++)
            {
                cdataSections.Add(new XmlQualifiedName(reader.ReadString(), reader.ReadString()));
            }
            // bool mergeCDataSections;
            settings.MergeCDataSections = reader.ReadBoolean();
            // string mediaType;
            settings.MediaType = reader.ReadStringQ();
            // string docTypeSystem;
            settings.DocTypeSystem = reader.ReadStringQ();
            // string docTypePublic;
            settings.DocTypePublic = reader.ReadStringQ();
            // XmlStandalone standalone;
            settings.Standalone = (XmlStandalone)reader.ReadSByte(0, (sbyte)XmlStandalone.No);
            // bool autoXmlDecl;
            settings.AutoXmlDeclaration = reader.ReadBoolean();
            // bool isReadOnly;
            settings.ReadOnly = reader.ReadBoolean();
            return settings;
        }

        //
        // Writer chain. Upstream XmlWriterSettings.CreateWriter builds raw writer -> [QueryOutputWriter] ->
        // XmlWellFormedWriter, and XmlILCommand then unwraps the well-formed writer again to drive the raw
        // writer directly (the settings XSLT produces always have ConformanceLevel.Auto). The stock raw writers
        // are internal to System.Private.Xml, so Xsl3 builds the same chain from its vendored raw writers and
        // returns the raw writer itself.
        //

        internal static XmlRawWriter CreateRawWriter(XmlWriterSettings settings, string outputFileName)
        {
            ArgumentNullException.ThrowIfNull(outputFileName);

            // need to clone the settigns so that we can set CloseOutput to true to make sure the stream gets closed in the end
            XmlWriterSettings newSettings = settings;
            if (!newSettings.CloseOutput)
            {
                newSettings = newSettings.Clone();
                newSettings.CloseOutput = true;
            }

            FileStream? fs = null;
            try
            {
                // open file stream
                fs = new FileStream(outputFileName, FileMode.Create, FileAccess.Write, FileShare.Read, 0x1000, newSettings.Async);

                // create writer
                return CreateRawWriter(newSettings, fs);
            }
            catch
            {
                fs?.Dispose();
                throw;
            }
        }

        internal static XmlRawWriter CreateRawWriter(XmlWriterSettings settings, Stream output)
        {
            ArgumentNullException.ThrowIfNull(output);

            XmlRawWriter writer;

            // create raw writer
            Debug.Assert(Encoding.UTF8.WebName == "utf-8");
            if (settings.Encoding.WebName == "utf-8")
            { // Encoding.CodePage is not supported in Silverlight
                // create raw UTF-8 writer
                writer = settings.OutputMethod switch
                {
                    XmlOutputMethod.Xml => settings.Indent ? new XmlUtf8RawTextWriterIndent(output, settings) : new XmlUtf8RawTextWriter(output, settings),
                    XmlOutputMethod.Html => settings.Indent ? new HtmlUtf8RawTextWriterIndent(output, settings) : new HtmlUtf8RawTextWriter(output, settings),
                    XmlOutputMethod.Text => new TextUtf8RawTextWriter(output, settings),
                    XmlOutputMethod.AutoDetect => new XmlAutoDetectWriter(output, settings),
                    _ => throw new UnreachableException("Invalid XmlOutputMethod setting."),
                };
            }
            else
            {
                // Otherwise, create a general-purpose writer than can do any encoding
                writer = settings.OutputMethod switch
                {
                    XmlOutputMethod.Xml => settings.Indent ? new XmlEncodedRawTextWriterIndent(output, settings) : new XmlEncodedRawTextWriter(output, settings),
                    XmlOutputMethod.Html => settings.Indent ? new HtmlEncodedRawTextWriterIndent(output, settings) : new HtmlEncodedRawTextWriter(output, settings),
                    XmlOutputMethod.Text => new TextEncodedRawTextWriter(output, settings),
                    XmlOutputMethod.AutoDetect => new XmlAutoDetectWriter(output, settings),
                    _ => throw new UnreachableException("Invalid XmlOutputMethod setting."),
                };
            }

            return WrapQuerySpecific(settings, writer);
        }

        internal static XmlRawWriter CreateRawWriter(XmlWriterSettings settings, TextWriter output)
        {
            ArgumentNullException.ThrowIfNull(output);

            // create raw writer
            XmlRawWriter writer = settings.OutputMethod switch
            {
                XmlOutputMethod.Xml => settings.Indent ? new XmlEncodedRawTextWriterIndent(output, settings) : new XmlEncodedRawTextWriter(output, settings),
                XmlOutputMethod.Html => settings.Indent ? new HtmlEncodedRawTextWriterIndent(output, settings) : new HtmlEncodedRawTextWriter(output, settings),
                XmlOutputMethod.Text => new TextEncodedRawTextWriter(output, settings),
                XmlOutputMethod.AutoDetect => new XmlAutoDetectWriter(output, settings),
                _ => throw new UnreachableException("Invalid XmlOutputMethod setting."),
            };

            return WrapQuerySpecific(settings, writer);
        }

        private static XmlRawWriter WrapQuerySpecific(XmlWriterSettings settings, XmlRawWriter writer)
        {
            // Wrap with Xslt/XQuery specific writer if needed;
            // XmlOutputMethod.AutoDetect writer does this lazily when it creates the underlying Xml or Html writer.
            if (settings.OutputMethod != XmlOutputMethod.AutoDetect && settings.IsQuerySpecific)
            {
                // Create QueryOutputWriter if CData sections or DocType need to be tracked
                writer = new QueryOutputWriter(writer, settings);
            }

            return writer;
        }
    }
}
