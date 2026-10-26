// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.XPath;
using Xsl3.XPath31;
using Xsl3.XPath31.Functions;
using Xsl3.Xdm;
using Xunit;

namespace Xsl3.Tests;

public class LazyTimezoneTests
{
    private static FunctionContext Context(ImplicitTimezone timezone) => new(timezone, (_, _) => 0);
    private static StaticInfo Static => new(null, Collation.CodepointUri, DecimalFormatTable.Empty);

    [Fact]
    public void StaticAndCollationContextsDoNotResolveTemporalState()
    {
        var context = Context(new ImplicitTimezone(() => throw new InvalidOperationException("Timezone was not requested")));
        FunctionContext clone = context.WithStatic(Static).WithDefaultCollation(Collation.Codepoint);
        Assert.True(Comparison.Compare(AtomicValue.OfString("a"), AtomicValue.OfString("a"), ValueComparisonOp.Eq, clone.Timezone));
        Assert.Equal("3", Arithmetic.Apply(ArithmeticOp.Add, AtomicValue.OfInteger(1), AtomicValue.OfInteger(2), clone.Timezone).Value);
    }

    [Theory]
    [InlineData("sum")]
    [InlineData("avg")]
    [InlineData("min")]
    [InlineData("max")]
    [InlineData("distinct-values")]
    public void NumericFunctionsDoNotResolveTimezone(string name)
    {
        var context = Context(new ImplicitTimezone(() => throw new InvalidOperationException("Timezone was not requested"))).WithStatic(Static);
        FunctionDefinition function = FunctionLibrary.Find(QName.Ns.Fn, name, 1)!;
        IList<XPathItem> result = function.Invoke(context, [new XPathItem[] { AtomicValue.OfInteger(1), AtomicValue.OfInteger(2) }]);
        Assert.NotEmpty(result);
    }

    [Fact]
    public void ExplicitTimezoneValuesDoNotNeedAnImplicitTimezone()
    {
        var context = Context(new ImplicitTimezone(() => throw new InvalidOperationException("Explicit timezone")));
        var local = new DateTimeValue(AtomicTypes.DateTime, new XsDateTime(2001, 2, 3, 5, 30, 0, 330));
        var utc = new DateTimeValue(AtomicTypes.DateTime, new XsDateTime(2001, 2, 3, 0, 0, 0, 0));
        Assert.True(Comparison.Compare(local, utc, ValueComparisonOp.Eq, context.Timezone));
        Assert.Equal(0, ((DurationValue)Arithmetic.Apply(ArithmeticOp.Subtract, local, utc, context.Timezone)).Duration.Seconds);
    }

    [Theory]
    [InlineData(-60)]
    [InlineData(0)]
    [InlineData(330)]
    public void MissingTimezoneResolvesTheConfiguredProviderOnceAcrossClones(int minutes)
    {
        int calls = 0;
        var context = Context(new ImplicitTimezone(() => { Interlocked.Increment(ref calls); return minutes; }));
        FunctionContext clone = context.WithStatic(Static);
        Assert.Equal(0, calls);
        var missing = new DateTimeValue(AtomicTypes.DateTime, new XsDateTime(2001, 2, 3, 0, 0, 0, null));
        var explicitZone = new DateTimeValue(AtomicTypes.DateTime, new XsDateTime(2001, 2, 3, 0, 0, 0, minutes));
        Assert.True(Comparison.Compare(missing, explicitZone, ValueComparisonOp.Eq, clone.Timezone));
        Assert.Equal(0, ((DurationValue)Arithmetic.Apply(ArithmeticOp.Subtract, missing, explicitZone, context.Timezone)).Duration.Seconds);
        Assert.Equal(minutes, context.ImplicitTimezoneMinutes);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void FixedHostClockIsPreservedWithoutResolvingTimezone()
    {
        var now = new XsDateTime(2001, 2, 3, 4, 5, 6.25m, 330);
        var context = new FunctionContext(new ImplicitTimezone(() => throw new InvalidOperationException("Host fixed clock")), (_, _) => 0)
        { FixedCurrentDateTime = now };
        Assert.Equal(now, context.WithStatic(Static).CurrentDateTime);
        Assert.Equal(now, context.WithDefaultCollation(Collation.Codepoint).CurrentDateTime);
        Assert.Equal(now, context.CurrentDateTime);
    }

    [Fact]
    public void SharedClockAndTimezoneRemainStableUnderConcurrentUse()
    {
        int calls = 0;
        var context = Context(new ImplicitTimezone(() => { Interlocked.Increment(ref calls); return 330; }));
        FunctionContext[] clones = Enumerable.Range(0, 16).Select(_ => context.WithStatic(Static)).ToArray();
        Assert.Equal(0, calls);
        var values = new XsDateTime[clones.Length];
        Parallel.For(0, clones.Length, i => values[i] = clones[i].CurrentDateTime);
        Assert.All(values, value => Assert.Equal(context.CurrentDateTime, value));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void ProviderErrorsAreNotReplacedByADefaultTimezone()
    {
        int calls = 0;
        var context = Context(new ImplicitTimezone(() => { calls++; throw new InvalidOperationException("No timezone provider"); }));
        Assert.Throws<InvalidOperationException>(() => context.ImplicitTimezoneMinutes);
        Assert.Throws<InvalidOperationException>(() => context.WithStatic(Static).ImplicitTimezoneMinutes);
        Assert.Equal(1, calls);
        Assert.Throws<InvalidOperationException>(() => default(ImplicitTimezone).Minutes);
    }
}
