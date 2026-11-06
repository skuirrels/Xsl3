// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Xml;
using Xsl3.Streaming;
using Xunit;

namespace Xsl3.Tests;

public class ForkNameTableTests
{
    [Fact]
    public void NullStringKeepsTheNameTableArgumentError()
    {
        InProng(table =>
        {
            var expected = Assert.Throws<ArgumentNullException>(() => new NameTable().Add(null!));
            var actual = Assert.Throws<ArgumentNullException>(() => table.Add(null!));
            Assert.Equal(expected.ParamName, actual.ParamName);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RepeatedAndEvictedNamesKeepTheProngsCanonicalReference(bool characterSeed)
    {
        InProng(table =>
        {
            string seed = new("canonical-name".ToCharArray());
            string atom = characterSeed ? table.Add(seed.ToCharArray(), 0, seed.Length) : table.Add(seed);
            string incoming = new(seed.ToCharArray());
            Assert.NotSame(atom, incoming);
            for (int round = 0; round < 3; round++)
            {
                for (int repeat = 0; repeat < 20; repeat++) Assert.Same(atom, table.Add(incoming));
                Assert.Same(atom, table.Add(new string(seed.ToCharArray())));
                Assert.Same(atom, table.Get(incoming));
                Assert.Same(atom, table.Get(incoming.ToCharArray(), 0, incoming.Length));
                // More distinct names than the bounded recent-name working set.
                for (int i = 0; i < 128; i++) table.Add("other-" + i);
                Assert.Same(atom, table.Add(incoming));
            }
            Assert.Same(string.Empty, table.Add(string.Empty));
            Assert.Same(string.Empty, table.Get(string.Empty));
        });
    }

    [Fact]
    public void AlternateInputReferencesHaveBoundedAdditionalRetention()
    {
        InProng(table =>
        {
            string canonical = table.Add("same-value");
            WeakReference[] references = AddAlternateReferences(table, canonical);
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            // The authoritative name table owns one atom. Its optional recent-name
            // cache may additionally retain at most eight alternate input references.
            Assert.InRange(references.Count(reference => reference.IsAlive), 0, 8);
            Assert.Same(canonical, table.Get("same-value"));
            GC.KeepAlive(table);
        });
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference[] AddAlternateReferences(XmlNameTable table, string canonical)
    {
        var references = new WeakReference[256];
        for (int i = 0; i < references.Length; i++)
        {
            string incoming = new(canonical.ToCharArray());
            references[i] = new(incoming);
            Assert.Same(canonical, table.Add(incoming));
        }
        return references;
    }

    private static void InProng(Action<XmlNameTable> check)
    {
        using var input = XmlReader.Create(new StringReader("<root/>"));
        using var document = new StreamedDocument(input);
        using var fork = document.Fork((StreamedNavigator)document.CreateNavigator());
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        fork.Run(new Action<StreamedNavigator, CancellationToken>[] {
            (view, _) => check(view.NameTable), (view, _) => check(view.NameTable)
        }, 1, cancellation.Token);
        Assert.Equal(ReadState.Initial, input.ReadState);
    }
}
