"""Ports dotnet/runtime's XslCompiledTransform tests into tests/Xsl3.Upstream.Tests (SPEC §6.1 step 4).

    py eng/port-upstream-tests.py <dotnet-runtime-checkout>

The port is mechanical so it can be re-run when upstream changes:
  * the XslCompiledTransformApi tests and their test files are copied unchanged, apart from
    retargeting System.Xml.Xsl to Xsl3;
  * the shared XML test infrastructure (ModuleCore, XmlCoreTest, XmlDiff, BaseLibManaged) is copied
    under Infrastructure/;
  * the few helpers borrowed from the old XslTransform tests are copied, but not those tests
    themselves (XslTransform is not carried over, SPEC §2.2).
Anything that cannot be ported mechanically lives in tests/Xsl3.Upstream.Tests/Shims and is
listed in docs/internals.md.
"""
import pathlib
import re
import shutil
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
DEST = ROOT / "tests" / "Xsl3.Upstream.Tests"

# Helpers from tests/Xslt/XslTransformApi that the XslCompiledTransform tests use.
XSLTRANSFORM_HELPERS = ["CThreads.cs", "cthread.cs", "ThreadFunc.cs", "MyNavigator.cs", "DataHelper.cs", "ReaderType.cs", "CXmlCache.cs"]
INFRASTRUCTURE = ["ModuleCore", "XmlCoreTest", "XmlDiff", "BaseLibManaged"]
TEST_UTILITIES = ["System/ThreadCultureChange.cs"]

HEADER = "// Ported from dotnet/runtime by eng/port-upstream-tests.py. Do not edit; fix the port script or add a shim instead.\n"


def retarget(text):
    text = re.sub(r"\bSystem\.Xml\.Xsl\b", "Xsl3", text)
    return text


def copy_cs(src, dst):
    dst.parent.mkdir(parents=True, exist_ok=True)
    text = src.read_text(encoding="utf-8-sig")
    dst.write_text(HEADER + retarget(text), encoding="utf-8", newline="\n")


def main():
    if len(sys.argv) != 2:
        sys.exit(__doc__)
    libs = pathlib.Path(sys.argv[1]) / "src" / "libraries"
    tests = libs / "System.Private.Xml" / "tests"
    common = libs / "Common" / "tests"

    for sub in ["Xslt", "Infrastructure"]:
        shutil.rmtree(DEST / sub, ignore_errors=True)

    for f in sorted((tests / "Xslt" / "XslCompiledTransformApi").glob("*.cs")):
        copy_cs(f, DEST / "Xslt" / "XslCompiledTransformApi" / f.name)
    for name in XSLTRANSFORM_HELPERS:
        copy_cs(tests / "Xslt" / "XslTransformApi" / name, DEST / "Xslt" / "XslTransformApi" / name)
    for name in ["ExceptionVerifier.cs", "AllowDefaultResolverContext.cs"]:
        copy_cs(tests / name, DEST / name)
    for proj in INFRASTRUCTURE:
        for f in sorted((common / "System" / "Xml" / proj).glob("*.cs")):
            copy_cs(f, DEST / "Infrastructure" / proj / f.name)
    for rel in TEST_UTILITIES:
        copy_cs(common / "TestUtilities" / rel, DEST / "Infrastructure" / "TestUtilities" / rel)

    # Test files are data: copied byte for byte.
    shutil.copytree(tests / "Xslt" / "TestFiles", DEST / "Xslt" / "TestFiles")
    print("ported into", DEST)


if __name__ == "__main__":
    main()
