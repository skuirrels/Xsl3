#!/usr/bin/env bash
# Prints the dotnet/runtime paths (relative to the repository root) that Xsl3 was derived from.
# Used by .github/workflows/upstream-sync.yml; see docs/internals.md for the mapping.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
L=src/libraries
echo "$L/System.Private.Xml/src/System/Xml/Xsl"
echo "$L/System.Private.Xml/src/System/Xml/Xslt"
echo "$L/System.Private.Xml/src/Resources/Strings.resx"
echo "$L/System.Private.Xml/tests/Xslt"
echo "$L/Common/tests/System/Xml"
cd "$ROOT/src/Xsl3/Vendored"
find . -name '*.cs' ! -name 'XmlWriterSettings.Enums.cs' | sed 's#^\./##' | sort | while read -r f; do
  case "$f" in
    Common/*) echo "$L/Common/src/${f#Common/}" ;;
    *)        echo "$L/System.Private.Xml/src/$f" ;;
  esac
done
# XmlWriterSettings.Enums.cs holds two enums extracted from this file.
echo "$L/System.Private.Xml/src/System/Xml/Core/XmlWriterSettings.cs"
