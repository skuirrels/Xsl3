// Licensed under the MIT license. See LICENSE.TXT in the repository root.

namespace Xsl3.Streaming;

internal sealed partial class StreamedValueReplay
{
    internal const int MemoryLimit = StreamedReplayStorage.MemoryLimit;
    internal int MemoryCapacity => _storage.MemoryCapacity;
}
