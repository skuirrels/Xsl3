// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using Xsl3.Streaming;
using Xunit;

namespace Xsl3.Tests;

public class StreamedDispatchReadPositionTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (string api in new[] { "modern", "legacy"
#if NET10_0_OR_GREATER
            , "saved"
#endif
        })
        foreach (bool debug in new[] { false, true })
        foreach (bool forward in new[] { false, true })
            yield return new object[] { api, debug, forward };
    }

    // S4-29: early output alone cannot show that next-match itself leaves the physical
    // reader on the current header. Observe both dispatches before any consuming body.
    [Theory]
    [MemberData(nameof(Cases))]
    public void NextMatchPreservesThePhysicalReaderPositionUntilTheSelectedBodyConsumes(
        string api, bool debug, bool forward)
    {
        string[] values = { "one", "two", "three", "four", "five" };
        string xml = "<doc>" + string.Concat(values.Select((value, index) =>
            $"<row id='{index + 1}'><value>{value}</value></row>")) + "</doc>";
        const string sheet = """
            <xsl:stylesheet version='3.0' xmlns:xsl='http://www.w3.org/1999/XSL/Transform'>
              <xsl:mode streamable='yes'/><xsl:output method='text'/>
              <xsl:template match='/'><xsl:apply-templates select='doc/row'/></xsl:template>
              <xsl:template match='row' priority='30'>
                <xsl:message select="concat('upper:', @id)"/><xsl:next-match/>
              </xsl:template>
              <xsl:template match='row' priority='20'>
                <xsl:message select="concat('middle:', @id)"/><xsl:next-match/>
              </xsl:template>
              <xsl:template match='row[@id]' priority='10'>
                <xsl:message select="concat('leaf:', @id)"/>
                <xsl:value-of select='value'/><xsl:text>|</xsl:text>
              </xsl:template>
            </xsl:stylesheet>
            """;
        using var input = new TrackingReader(XmlReader.Create(new StringReader(xml)));
        using var output = new StringWriter();
        using var capture = new StreamingExecutionCapture();
        var observations = new List<(string Message, Position Position)>();
        void Observe(string message) => observations.Add((message, input.Position));
        var settings = new XsltSettings { StreamedExecution = forward, IncludeDebugInformation = debug };
        if (api == "modern")
        {
            var transformer = new XsltCompiler { Settings = settings }.Compile(new StringReader(sheet)).CreateTransformer();
            transformer.MessageHandler = Observe;
            transformer.ApplyTemplates(input, output);
        }
        else
        {
            var transformer = new XslCompiledTransform(debug);
            using var stylesheet = XmlReader.Create(new StringReader(sheet));
#if NET10_0_OR_GREATER
            if (api == "saved")
            {
                using var assembly = new MemoryStream();
                transformer.CompileToAssembly(stylesheet, settings, null, "DispatchPositions", "Saved.DispatchPositions", assembly);
                transformer.Load(Assembly.Load(assembly.ToArray()).GetType("Saved.DispatchPositions", throwOnError: true)!);
            }
            else
#endif
                transformer.Load(stylesheet, settings, null);
            var arguments = new XsltArgumentList();
            arguments.XsltMessageEncountered += (_, message) => Observe(message.Message);
            transformer.Transform(input, arguments, output);
        }

        Assert.Equal(string.Concat(values.Select(value => value + "|")), output.ToString());
        Assert.Equal(values.Length * 3, observations.Count);
        for (int row = 0; row < values.Length; row++)
        {
            int first = row * 3;
            Position position = observations[first].Position;
            Assert.Equal($"upper:{row + 1}", observations[first].Message);
            Assert.Equal($"middle:{row + 1}", observations[first + 1].Message);
            Assert.Equal($"leaf:{row + 1}", observations[first + 2].Message);
            Assert.Equal(position, observations[first + 1].Position);
            Assert.Equal(position, observations[first + 2].Position);
            if (forward)
            {
                Assert.Equal(ReadState.Interactive, position.State);
                Assert.Equal(XmlNodeType.Element, position.Kind);
                Assert.Equal("row", position.Name);
                Assert.Equal(row + 1, position.RowsStarted);
                Assert.Equal(row, position.RowsEnded);
                Assert.Equal(row, position.ValuesStarted);
            }
            else
            {
                Assert.Equal(ReadState.EndOfFile, position.State);
                Assert.Equal(values.Length, position.RowsStarted);
                Assert.Equal(values.Length, position.RowsEnded);
                Assert.Equal(values.Length, position.ValuesStarted);
            }
        }
        Assert.Equal(values.Length, input.Position.RowsEnded);
        Assert.NotEqual(ReadState.Closed, input.ReadState);
        var execution = Assert.Single(capture.Snapshot().Executions);
        Assert.True(execution.BodyCompleted);
        var source = Assert.Single(execution.Sources);
        Assert.Equal(forward, source.ActualForward);
        Assert.Equal(forward, source.NavigatorType == typeof(StreamedNavigator).FullName);
    }

    private sealed record Position(int ReadCalls, int RowsStarted, int RowsEnded, int ValuesStarted,
        ReadState State, XmlNodeType Kind, string Name);

    private sealed class TrackingReader(XmlReader reader) : XmlWrappingReader(reader)
    {
        private int _reads, _rowsStarted, _rowsEnded, _valuesStarted;
        internal Position Position => new(_reads, _rowsStarted, _rowsEnded, _valuesStarted, ReadState, NodeType, LocalName);

        public override bool Read()
        {
            _reads++;
            bool read = base.Read();
            if (read && NodeType == XmlNodeType.Element)
            {
                if (LocalName == "row") _rowsStarted++;
                if (LocalName == "value") _valuesStarted++;
            }
            if (read && NodeType == XmlNodeType.EndElement && LocalName == "row") _rowsEnded++;
            return read;
        }
    }
}
