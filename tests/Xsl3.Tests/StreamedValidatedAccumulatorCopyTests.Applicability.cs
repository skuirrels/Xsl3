// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using Xsl3.Xdm;
using Xunit;

namespace Xsl3.Tests;

public partial class StreamedValidatedAccumulatorCopyTests
{
    public static IEnumerable<object[]> ApplicabilityCases()
    {
        foreach (int delivery in Deliveries())
        foreach (bool debug in new[] { false, true })
        foreach (bool forward in new[] { false, true })
        {
            foreach (string validation in new[] { "strict", "lax" })
            foreach (bool inherit in new[] { false, true })
            foreach (bool text in new[] { false, true })
            foreach (bool otherAccumulator in new[] { false, true })
                yield return new object[] { delivery, debug, forward, inherit, validation, text, otherAccumulator, false, true };
            yield return new object[] { delivery, debug, forward, true, "strict", true, true, true, true };
            foreach (bool text in new[] { false, true })
            foreach (bool otherAccumulator in new[] { false, true })
                yield return new object[] { delivery, debug, forward, true, "strict", text, otherAccumulator, false, false };
        }
    }

    [Theory]
    [MemberData(nameof(ApplicabilityCases))]
    public void ValidatedCopiesKeepSourceAccumulatorApplicability(int delivery, bool debug, bool forward,
        bool inherit, string validation, bool text, bool otherAccumulator, bool outerCopy, bool streamable)
    {
        string? actual = null;
        Exception? error = Record.Exception(() => actual = Transform(
            ApplicabilitySheet(inherit, validation, text, otherAccumulator, outerCopy, streamable), Input(1), delivery, debug, forward));
        if (inherit && (forward || streamable))
        {
            Assert.NotNull(error);
            Assert.Equal("XTDE3362", error switch {
                XsltDynamicException modern => modern.Code.LocalName,
                XdmException legacy => legacy.Code,
                _ => error.GetType().FullName,
            });
        }
        else
        {
            Assert.Null(error);
            Assert.Equal("true", actual);
        }
    }
}
