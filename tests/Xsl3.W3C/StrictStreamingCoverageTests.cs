// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System.Collections.Generic;
using Xunit;
using Xunit.Sdk;

namespace Xsl3.W3C;

public class StrictStreamingCoverageTests
{
    [Fact]
    public void TheFrozenPopulationAllowsFailureFixesWithoutChangingCoverage() =>
        // Pass/fail identities are checked separately by Baseline.Check; coverage does not freeze their counts.
        Xslt30RunTests.CheckStrictStreamingCoverage(2712, 0, new Dictionary<string, int> { ["environment: collection"] = 1 });

    [Fact]
    public void LosingAPassingCaseCannotHideBehindAnUnchangedFailureList() =>
        Assert.ThrowsAny<XunitException>(() => Xslt30RunTests.CheckStrictStreamingCoverage(2711, 0,
            new Dictionary<string, int> { ["environment: collection"] = 1 }));

    [Fact]
    public void ANewSkipCannotReplaceAPassingCase() =>
        Assert.ThrowsAny<XunitException>(() => Xslt30RunTests.CheckStrictStreamingCoverage(2712, 0,
            new Dictionary<string, int> { ["environment: collection"] = 1, ["initial match selection by expression"] = 1 }));

    [Fact]
    public void TheSameNumberOfSkipsCannotHideAChangedReason() =>
        Assert.ThrowsAny<XunitException>(() => Xslt30RunTests.CheckStrictStreamingCoverage(2712, 0,
            new Dictionary<string, int> { ["environment: missing source"] = 1 }));

    [Fact]
    public void AnExpectedErrorWithTheWrongCodeCannotBecomeAPass() =>
        Assert.ThrowsAny<XunitException>(() => Xslt30RunTests.CheckStrictStreamingCoverage(2712, 1,
            new Dictionary<string, int> { ["environment: collection"] = 1 }));
}
