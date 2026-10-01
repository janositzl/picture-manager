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
    public void MajorityPerson_TieIsNotAMajority()
    {
        var neighbors = new List<FaceNeighbor> { new(1, 10, 0.1f), new(2, 10, 0.1f), new(3, 11, 0.1f), new(4, 11, 0.1f) };

        FaceClustering.MajorityPerson(neighbors, 0.4f, 2).Should().BeNull();
    }

    [Fact]
    public async Task Dbscan_GroupsDenseComponents_AndLeavesNoiseOut()
    {
        // 1-2-3-4 chain (dense), 5-6 pair (too small for minPoints 3), 7 isolated, 8 outside the input set.
        var graph = new Dictionary<int, int[]>
        {
            [1] = new[] { 2, 3 }, [2] = new[] { 1, 3 }, [3] = new[] { 1, 2, 4 }, [4] = new[] { 3, 8 },
            [5] = new[] { 6 }, [6] = new[] { 5 }, [7] = new int[0]
        };

        var clusters = await FaceClustering.DbscanAsync(
            new[] { 1, 2, 3, 4, 5, 6, 7 },
            id => Task.FromResult<IReadOnlyList<int>>(graph[id]),
            minPoints: 3,
            CancellationToken.None);

        clusters.Should().ContainSingle().Which.Should().BeEquivalentTo(new[] { 1, 2, 3, 4 });
    }
}
