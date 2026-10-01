using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using FluentAssertions;
using PictureManager.Application.Duplicates;
using Xunit;

namespace PictureManager.Application.Tests.Duplicates;

public class SimilarityClustererTests
{
    [Fact]
    public void Cluster_GroupsWithinThreshold_ExcludesFarAndSingletons()
    {
        ulong baseHash = 0x0F0F_3C3C_5A5A_A5A5;
        var items = new List<(int, ulong)>
        {
            (1, baseHash), (2, baseHash ^ 0b111), (3, ~baseHash), (4, baseHash ^ (1UL << 63))
        };
        SimilarityClusterer.Cluster(items, 6).Should().BeEquivalentTo(
            new[] { new[] { 1, 2, 4 } }, o => o.WithStrictOrdering());
    }

    [Fact]
    public void Cluster_ChainsTransitively()
    {
        ulong a = 0x0F0F_3C3C_5A5A_A5A5, b = a ^ 0x3F, c = b ^ (0x3FUL << 32); // a~b=6, b~c=6, a~c=12
        SimilarityClusterer.Cluster(new List<(int, ulong)> { (1, a), (2, b), (3, c) }, 6)
            .Should().ContainSingle().Which.Should().Equal(1, 2, 3);
    }

    [Fact]
    public void Cluster_SkipsDegenerateHashes()
    {
        SimilarityClusterer.Cluster(new List<(int, ulong)> { (1, 0UL), (2, 0UL), (3, 1UL) }, 6).Should().BeEmpty();
    }

    [Fact]
    public void Cluster_FindsMatchesAcrossAllBands_Randomized()
    {
        var rng = new Random(42);
        var items = new List<(int, ulong)>();
        for (var i = 0; i < 500; i++) items.Add((i, (ulong)rng.NextInt64() | 0x0101_0101_0101_0101));

        // Plant near-duplicate pairs: for each band k, the pair agrees exactly on band k (and one more band)
        // and differs by 6 bits spread over six other bands.
        for (var k = 0; k < 8; k++)
        {
            ulong h;
            do { h = (ulong)rng.NextInt64(); } while (BitOperations.PopCount(h) is < 3 or > 61);
            var flipped = h;
            for (var j = 1; j <= 6; j++)
            {
                var band = (k + j) % 8;
                flipped ^= 1UL << (band * 8 + rng.Next(8));
            }
            items.Add((1000 + 2 * k, h));
            items.Add((1001 + 2 * k, flipped));
        }

        // brute-force oracle
        var expectedPairs = items.SelectMany(x => items.Where(y => y.Item1 > x.Item1
            && PerceptualHash.Distance(x.Item2, y.Item2) <= 6).Select(y => (x.Item1, y.Item1))).ToList();
        expectedPairs.Count.Should().BeGreaterThanOrEqualTo(8);

        var groups = SimilarityClusterer.Cluster(items, 6);
        foreach (var (p, q) in expectedPairs)
            groups.Should().Contain(g => g.Contains(p) && g.Contains(q));
    }
}
