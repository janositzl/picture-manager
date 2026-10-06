using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using PictureManager.Application.Faces;
using PictureManager.Application.Repositories;
using Xunit;

namespace PictureManager.Application.Tests.Faces;

public class FaceClusteringTests
{
    [Fact]
    public void MajorityPerson_NeedsMinVotesAndStrictMajorityWithinDistance()
    {
        var neighbors = new List<FaceNeighbor>
        {
            new(1, 10, 0.2f), new(2, 10, 0.3f), new(3, 11, 0.35f), new(4, 11, 0.9f), new(5, 11, 0.95f)
        };

        FaceClustering.MajorityPerson(neighbors, maxDistance: 0.4f, minVotes: 2).Should().Be(10);
        FaceClustering.MajorityPerson(neighbors, maxDistance: 0.4f, minVotes: 3).Should().BeNull();
        FaceClustering.MajorityPerson(neighbors.Take(1).ToList(), maxDistance: 0.4f, minVotes: 2).Should().BeNull();
    }

    [Fact]
    public void MajorityPerson_NeverPicksTheExcludedPerson()
    {
        var neighbors = new List<FaceNeighbor> { new(1, 10, 0.1f), new(2, 10, 0.1f), new(3, 10, 0.2f) };

        FaceClustering.MajorityPerson(neighbors, maxDistance: 0.4f, minVotes: 2).Should().Be(10);
        FaceClustering.MajorityPerson(neighbors, maxDistance: 0.4f, minVotes: 2, excludedPersonId: 10).Should().BeNull();
    }

    [Fact]
    public void MajorityPerson_TieIsNotAMajority()
    {
        var neighbors = new List<FaceNeighbor> { new(1, 10, 0.1f), new(2, 10, 0.1f), new(3, 11, 0.1f), new(4, 11, 0.1f) };

        FaceClustering.MajorityPerson(neighbors, 0.4f, 2).Should().BeNull();
    }

    [Fact]
    public async Task Dbscan_GroupsDenseComponents_AndLeavesNoiseOut()
    {
        // 1-2-3-4 chain (dense), 5-6 pair (too small for minPoints 3), 7 isolated, 8 not a seed but reachable from 4.
        var graph = new Dictionary<int, int[]>
        {
            [1] = new[] { 2, 3 }, [2] = new[] { 1, 3 }, [3] = new[] { 1, 2, 4 }, [4] = new[] { 3, 8 },
            [5] = new[] { 6 }, [6] = new[] { 5 }, [7] = new int[0], [8] = new[] { 4 }
        };

        var clusters = await FaceClustering.DbscanAsync(
            new[] { 1, 2, 3, 4, 5, 6, 7 },
            id => Task.FromResult<IReadOnlyList<int>>(graph[id]),
            minPoints: 3,
            CancellationToken.None);

        clusters.Should().ContainSingle().Which.Should().BeEquivalentTo(new[] { 1, 2, 3, 4, 8 });
    }

    [Fact]
    public async Task Dbscan_SeedNearTwoOldNoiseFaces_FormsAClusterOfThree()
    {
        // 20 and 21 are not seeds (older faces left as noise), but they are the new face's neighbours.
        var graph = new Dictionary<int, int[]> { [1] = new[] { 20, 21 }, [20] = new[] { 1, 21 }, [21] = new[] { 1, 20 } };

        var clusters = await FaceClustering.DbscanAsync(
            new[] { 1 }, id => Task.FromResult<IReadOnlyList<int>>(graph[id]), minPoints: 3, CancellationToken.None);

        clusters.Should().ContainSingle().Which.Should().BeEquivalentTo(new[] { 1, 20, 21 });
    }

    [Fact]
    public async Task Dbscan_OldNoiseAloneIsNeverASeed()
    {
        // 5-6-7 would be dense, but none of them is a seed and the only seed (9) does not reach them.
        var graph = new Dictionary<int, int[]> { [9] = new int[0], [5] = new[] { 6, 7 }, [6] = new[] { 5, 7 }, [7] = new[] { 5, 6 } };
        var asked = new List<int>();

        var clusters = await FaceClustering.DbscanAsync(
            new[] { 9 },
            id =>
            {
                asked.Add(id);
                return Task.FromResult<IReadOnlyList<int>>(graph[id]);
            },
            minPoints: 3,
            CancellationToken.None);

        clusters.Should().BeEmpty();
        asked.Should().Equal(9);
    }
}
