// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// Extracted from upstream System/Xml/Core/XmlWriterSettings.cs. The public XmlWriterSettings
// class itself is not vendored: Xsl3 must keep handing out the real System.Xml.XmlWriterSettings,
// so its internal members are reached through XmlWriterSettingsInternals instead.

namespace System.Xml
{
    /// <summary>
    /// Three-state logic enumeration.
    /// </summary>
    internal enum TriState
    {
        Unknown = -1,
        False = 0,
        True = 1,
    };

    internal enum XmlStandalone
    {
        // Do not change the constants - XmlBinaryWriter depends in it
        Omit = 0,
        Yes = 1,
        No = 2,
    }
}
