// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Xsl3
{
    internal interface ISourceLineInfo
    {
        string? Uri { get; }
        bool IsNoSource { get; }
        Location Start { get; }
        Location End { get; }
    }
}
