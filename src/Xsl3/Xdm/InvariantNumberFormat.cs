// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Globalization;

namespace Xsl3.Xdm;

/// <summary>The BCL invariant numeric format, independent of culture initialization.</summary>
internal static class InvariantNumberFormat
{
    // The parameterless constructor supplies the invariant numeric data. Accessing
    // InvariantInfo instead initializes CultureInfo and, on Linux, can load ICU even
    // when the caller only needs XML's culture-independent numeric lexical space.
    internal static readonly NumberFormatInfo Info = NumberFormatInfo.ReadOnly(new NumberFormatInfo());
}
