// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Xml;

namespace Xsl3.Runtime;

// Serialization 3.1 §4 replaces the normalized tree by its string value before
// character maps and Unicode normalization for the text method. Filtering here,
// before the serializer, preserves normalization across ignored markup/comments.
internal sealed class TextResultWriter(XmlWriter target) : XmlRawWriter
{
    private bool _attribute;
    public override void WriteStartElement(string? prefix, string localName, string? ns) { }
    internal override void StartElementContent() { }
    internal override void WriteEndElement(string prefix, string localName, string ns) { }
    internal override void WriteFullEndElement(string prefix, string localName, string ns) { }
    internal override void WriteNamespaceDeclaration(string prefix, string ns) { }
    public override void WriteStartAttribute(string? prefix, string localName, string? ns) => _attribute = true;
    public override void WriteEndAttribute() => _attribute = false;
    public override void WriteString(string? text) { if (!_attribute) target.WriteString(text); }
    public override void WriteRaw(string data) => WriteString(data);
    public override void WriteRaw(char[] buffer, int index, int count) => WriteString(new string(buffer, index, count));
    public override void WriteSurrogateCharEntity(char lowChar, char highChar) => WriteString(new string(new[] { highChar, lowChar }));
    public override void WriteEntityRef(string name) => WriteString(name switch
    {
        "lt" => "<", "gt" => ">", "amp" => "&", "apos" => "'", "quot" => "\"",
        _ => throw new System.ArgumentException("An unresolved entity has no text value", nameof(name)),
    });
    public override void WriteComment(string? text) { }
    public override void WriteProcessingInstruction(string name, string? text) { }
    public override void WriteDocType(string name, string? pubid, string? sysid, string? subset) { }
    public override void Flush() => target.Flush();
}
