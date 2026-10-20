// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using System.Xml;
using Xsl3.Runtime;

namespace Xsl3.Xslt
{
    internal sealed partial class QilGenerator
    {
        /// <summary>
        /// An output definition's writer settings and serialization parameters, with its parameter document applied
        /// (Serialization 3.1 §3.1): the document's parameters, then the declaration's own (the loader has left out
        /// those the declaration sets); its character map, then the declaration's use-character-maps.
        /// </summary>
        private (XmlWriterSettings Settings, OutputExtras? Extras) WithParameterDocument31(Output output)
        {
            OutputExtras? extras = OutputExtras31(output);
            if (output.ParameterDocument is not { } document)
            {
                return (output.Settings, extras);
            }
            var names = new XsltNameContext(new Dictionary<string, string>(), new HashSet<(string, string, int)>());
            var (settings, applied, _) = SerializationParameters.Apply(
                new ResultFormat(output.Settings, extras, output.Settings.OutputMethod == XmlOutputMethod.Text), document.Encode(), names);
            if (document.CharacterMap.Count > 0)
            {
                var map = new Dictionary<int, string>(document.CharacterMap);
                foreach (var (c, s) in applied?.CharacterMap ?? new Dictionary<int, string>())
                {
                    map[c] = s;
                }
                applied ??= new OutputExtras();
                applied.CharacterMap = map;
            }
            return (settings, applied);
        }
    }
}
