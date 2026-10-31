// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;

namespace Xsl3.Streaming;

/// <summary>Motionless callbacks at source-node boundaries, before template code observes those nodes.</summary>
internal interface IStreamedNodeObserver : IDisposable
{
    void Start(StreamedNavigator node);
    void End(StreamedNavigator node);

    // Intrinsic observers can consume the frame directly. Ordinary callbacks still
    // receive a fresh position that they may retain after the source advances.
    void Start(StreamedDocument document, StreamedDocument.Node node) => Start(new StreamedNavigator(document, node));
    void End(StreamedDocument document, StreamedDocument.Node node) => End(new StreamedNavigator(document, node));
}
