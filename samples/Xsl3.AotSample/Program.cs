// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.IO;
using System.Xml;
using Xsl3;

// A precompiled stylesheet (Stylesheets.Report, made by Xsl3.Build from Report.xsl): no IL is generated at run time,
// so this runs under NativeAOT.
const string Input = """
    <orders>
      <order customer="b" price="2.50" quantity="4"/>
      <order customer="a" price="10" quantity="1"/>
      <order customer="b" price="1" quantity="3"/>
    </orders>
    """;
const string Expected = """<report title="Q4"><customer name="a" orders="1" total="10"/><customer name="b" orders="2" total="13"/><largest>13</largest></report>""";

var transform = new XslCompiledTransform();
transform.Load(typeof(Stylesheets.Report), new XsltSettings());
var arguments = new XsltArgumentList();
arguments.AddParam("title", "", "Q4");
var output = new StringWriter();
transform.Transform(XmlReader.Create(new StringReader(Input)), arguments, output);
Console.WriteLine(output);
if (output.ToString() != Expected)
{
    Console.Error.WriteLine("Unexpected output; expected " + Expected);
    return 1;
}
return 0;
