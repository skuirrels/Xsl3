// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Xml.XPath;

namespace Xsl3.Streaming;

/// <summary>A reader, stream or URI selection, kept separate from the grounded global context item.</summary>
internal sealed record InitialSource(object Input, Uri? BaseUri, XPathNavigator GlobalContext);
