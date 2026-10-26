// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Text.Json.Serialization;

namespace Xsl3.Tests;

[JsonSerializable(typeof(StreamingProbe.Result))]
internal partial class StreamingProbeJsonContext : JsonSerializerContext;
