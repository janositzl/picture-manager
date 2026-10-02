using System.Collections.Generic;
using FluentAssertions;
using PictureManager.Application.Faces;
using Xunit;

namespace PictureManager.Application.Tests.Faces;

public class FaceMatchingTests
{
    [Fact]
    public void IoU_IdenticalBoxesIsOne_DisjointIsZero_PartialIsIntersectionOverUnion()
    {
        var box = new FaceBox(0.1f, 0.1f, 0.2f, 0.2f);

        FaceMatching.IoU(box, box).Should().BeApproximately(1.0, 1e-6);
        FaceMatching.IoU(box, new FaceBox(0.5f, 0.5f, 0.2f, 0.2f)).Should().Be(0);
        // Shifted by half a width: intersection 0.1x0.2 = 0.02, union 0.04 + 0.04 - 0.02 = 0.06.
        FaceMatching.IoU(box, new FaceBox(0.2f, 0.1f, 0.2f, 0.2f)).Should().BeApproximately(1.0 / 3, 1e-6);
        FaceMatching.IoU(box, new FaceBox(0.1f, 0.1f, 0f, 0f)).Should().Be(0);
    }

    [Fact]
    public void MatchByOverlap_PairsEachNewBoxWithTheOldBoxItOverlapsMost()
    {
        var previous = new List<FaceBox> { new(0.1f, 0.1f, 0.2f, 0.2f), new(0.6f, 0.6f, 0.2f, 0.2f) };
        var current = new List<FaceBox> { new(0.61f, 0.6f, 0.2f, 0.2f), new(0.11f, 0.11f, 0.2f, 0.2f), new(0.4f, 0.0f, 0.1f, 0.1f) };

        var matches = FaceMatching.MatchByOverlap(previous, current);

        matches.Should().BeEquivalentTo(new Dictionary<int, int> { [0] = 1, [1] = 0 });
    }

    [Fact]
    public void MatchByOverlap_BelowMinimumOverlapDoesNotMatch()
    {
        var previous = new List<FaceBox> { new(0.1f, 0.1f, 0.2f, 0.2f) };
        var current = new List<FaceBox> { new(0.2f, 0.1f, 0.2f, 0.2f) }; // IoU 1/3

        FaceMatching.MatchByOverlap(previous, current).Should().BeEmpty();
    }

    [Fact]
    public void MatchByOverlap_IsOneToOne_BestPairWins()
    {
        var previous = new List<FaceBox> { new(0.1f, 0.1f, 0.2f, 0.2f) };
        var current = new List<FaceBox> { new(0.13f, 0.1f, 0.2f, 0.2f), new(0.11f, 0.1f, 0.2f, 0.2f) };

        FaceMatching.MatchByOverlap(previous, current).Should().BeEquivalentTo(new Dictionary<int, int> { [1] = 0 });
    }

    [Fact]
    public void MatchByOverlap_GreedyTakesTheHighestPairFirst()
    {
        // new[0] overlaps old[0] best, but old[0] is new[1]'s exact match: new[1] takes it, new[0] falls back to old[1].
        var previous = new List<FaceBox> { new(0.10f, 0.1f, 0.2f, 0.2f), new(0.16f, 0.1f, 0.2f, 0.2f) };
        var current = new List<FaceBox> { new(0.12f, 0.1f, 0.2f, 0.2f), new(0.10f, 0.1f, 0.2f, 0.2f) };

        FaceMatching.MatchByOverlap(previous, current).Should().BeEquivalentTo(new Dictionary<int, int> { [1] = 0, [0] = 1 });
    }

    [Fact]
    public void MatchByOverlap_EmptyInputs_NoMatches()
    {
        FaceMatching.MatchByOverlap(new List<FaceBox>(), new List<FaceBox> { new(0, 0, 0.1f, 0.1f) }).Should().BeEmpty();
        FaceMatching.MatchByOverlap(new List<FaceBox> { new(0, 0, 0.1f, 0.1f) }, new List<FaceBox>()).Should().BeEmpty();
    }
}
