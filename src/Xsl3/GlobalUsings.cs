// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// Upstream sources lived in namespaces nested under System.Xml, so they resolved
// System and System.Xml types through the enclosing namespaces. These global
// usings restore that after the move to the Xsl3 namespace.
global using System;
global using System.Xml;

// Both System.LocalAppContextSwitches (Common helpers) and System.Xml.LocalAppContextSwitches are vendored;
// upstream engine code meant the latter, which its enclosing System.Xml namespace used to select.
global using LocalAppContextSwitches = System.Xml.LocalAppContextSwitches;
