// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.IO;
using System.Linq;
using System.Security;
using Xsl3.W3C.Qt3;
using Xunit;

namespace Xsl3.W3C;

public class Qt3ErrorCodeAccountingTests
{
    [Theory]
    [InlineData("<any-of><error code='FOAR0001'/><error code='FOER0000'/></any-of>", false)]
    [InlineData("<any-of><error code='FOER0000'/><error code='FOAR0001'/></any-of>", false)]
    [InlineData("<any-of><error code='FOAR0001'/><any-of><error code='FODC0002'/><error code='FOER0000'/></any-of></any-of>", false)]
    [InlineData("<any-of><error code='FOAR0001'/><error code='*'/></any-of>", false)]
    [InlineData("<any-of><error code='FOAR0001'/><error code='XPTY0004'/></any-of>", true)]
    [InlineData("<all-of><error code='FOAR0001'/><error code='FOER0000'/></all-of>", true)]
    [InlineData("<all-of><error code='FOER0000'/><error code='FOER0000'/></all-of>", false)]
    [InlineData("<any-of><all-of><error code='FOAR0001'/></all-of><error code='FOER0000'/></any-of>", false)]
    [InlineData("<any-of><all-of><error code='FOAR0001'/></all-of><error code='FODC0002'/></any-of>", true)]
    public void BooleanAssertionsRetainOnlyUnmatchedErrorCodeNotes(string assertion, bool mismatch)
    {
        string directory = Path.Combine(Path.GetTempPath(), "xsl3-qt3-accounting-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            const string ns = "http://www.w3.org/2010/09/qt-fots-catalog";
            File.WriteAllText(Path.Combine(directory, "catalog.xml"), $"<catalog xmlns='{ns}'><test-set name='accounting' file='cases.xml'/></catalog>");
            string expression = SecurityElement.Escape("error(QName('http://www.w3.org/2005/xqt-errors', 'FOER0000'))")!;
            File.WriteAllText(Path.Combine(directory, "cases.xml"), $"<test-set xmlns='{ns}' name='accounting'><test-case name='case'><test>{expression}</test><result>{assertion}</result></test-case></test-set>");
            var catalog = Xsl3.W3C.Qt3.Catalog.Load(directory);
            Outcome outcome = new Xsl3.W3C.Qt3.Runner().Run(catalog.TestCases.Single());
            Assert.Equal(OutcomeKind.Pass, outcome.Kind);
            if (mismatch) Assert.StartsWith("error code FOER0000 instead of ", outcome.Detail);
            else Assert.Empty(outcome.Detail);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
