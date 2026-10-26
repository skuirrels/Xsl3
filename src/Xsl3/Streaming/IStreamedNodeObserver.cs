// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;

namespace Xsl3.Streaming;

/// <summary>Motionless callbacks at source-node boundaries, before template code observes those nodes.</summary>
internal interface IStreamedNodeObserver : IDisposable
{
    void Start(StreamedNavigator node);
    void End(StreamedNavigator node);
}
