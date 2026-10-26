// Licensed under the MIT license. See LICENSE.TXT in the repository root.

namespace Xsl3.Streaming;

internal sealed partial class StreamedDocument
{
    private StreamedHeaderSpool? _headerSpool;
    internal StreamedHeaderSpool HeaderSpool
    {
        get { Check(); return _headerSpool ??= new(this); }
    }
    internal void CheckHeaderSpool() => Check();
    private void DisposeHeaderSpool()
    {
        var spool = _headerSpool;
        _headerSpool = null;
        spool?.Dispose();
    }
}
