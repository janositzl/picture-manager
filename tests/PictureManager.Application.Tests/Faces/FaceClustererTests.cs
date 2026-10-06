using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using PictureManager.Application.Common;
using PictureManager.Application.Faces;
using PictureManager.Application.Repositories;
using Xunit;

namespace PictureManager.Application.Tests.Faces;

public class FaceClustererTests
{
    private const int ModelId = 3;
    private static readonly DateTime Now = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly IFaceRepository _faces = Substitute.For<IFaceRepository>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly FaceRecognitionOptions _options = new();

    public FaceClustererTests()
    {
        _clock.UtcNow.Returns(Now);
        _faces.GetNearestAsync(Arg.Any<int>(), ModelId, Arg.Any<NeighborPool>(), Arg.Any<int>(), Arg.Any<float>(), Arg.Any<CancellationToken>())
            .Returns(new List<FaceNeighbor>());
    }

    private FaceClusterer Create() => new(_faces, _options, _clock);

    [Fact]
    public async Task ClusterAsync_AttachesFaceToPersonWhenAssignedNeighboursAgree()
    {
        _faces.GetUnclusteredFacesAsync(ModelId, 0f, Arg.Any<CancellationToken>()).Returns(new List<FaceCandidate> { new(1, 0.9f) });
        _faces.GetUnclusteredFacesAsync(ModelId, _options.MinQualityForClustering, Arg.Any<CancellationToken>()).Returns(new List<FaceCandidate>());
        _faces.GetNearestAsync(1, ModelId, NeighborPool.Assigned, Arg.Any<int>(), Arg.Any<float>(), Arg.Any<CancellationToken>())
            .Returns(new List<FaceNeighbor> { new(10, 7, 0.1f), new(11, 7, 0.2f), new(12, 8, 0.9f) });

        await Create().ClusterAsync(ModelId);

        await _faces.Received(1).AssignAsync(Arg.Is<IReadOnlyCollection<int>>(ids => ids.SequenceEqual(new[] { 1 })), 7, Arg.Any<float?>(), Arg.Any<CancellationToken>());
        await _faces.DidNotReceiveWithAnyArgs().CreateUnnamedPersonAsync(default, default, default);
        await _faces.Received(1).MarkClusteredAsync(Arg.Is<IReadOnlyCollection<int>>(ids => ids.SequenceEqual(new[] { 1 })), Now, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ClusterAsync_GroupsDenseRemainder_WithBestQualityAsCover_AndCleansUp()
    {
        var candidates = new List<FaceCandidate> { new(1, 0.6f), new(2, 0.95f), new(3, 0.7f) };
        _faces.GetUnclusteredFacesAsync(ModelId, 0f, Arg.Any<CancellationToken>()).Returns(candidates);
        _faces.GetUnclusteredFacesAsync(ModelId, _options.MinQualityForClustering, Arg.Any<CancellationToken>()).Returns(candidates);
        foreach (var id in new[] { 1, 2, 3 })
            _faces.GetNearestAsync(id, ModelId, NeighborPool.Unassigned, Arg.Any<int>(), Arg.Any<float>(), Arg.Any<CancellationToken>())
                .Returns(new[] { 1, 2, 3 }.Where(o => o != id).Select(o => new FaceNeighbor(o, null, 0.2f)).ToList());
        _faces.CreateUnnamedPersonAsync(2, Now, Arg.Any<CancellationToken>()).Returns(55);

        await Create().ClusterAsync(ModelId);

        await _faces.Received(1).CreateUnnamedPersonAsync(2, Now, Arg.Any<CancellationToken>());
        await _faces.Received(1).AssignAsync(
            Arg.Is<IReadOnlyCollection<int>>(ids => ids.OrderBy(i => i).SequenceEqual(new[] { 1, 2, 3 })), 55, Arg.Any<float?>(), Arg.Any<CancellationToken>());
        await _faces.Received(1).DeleteEmptyUnnamedPeopleAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ClusterAsync_MatchingQueriesAllAssignedFaces_GroupingOnlyGoodQualityNeighbours()
    {
        var candidates = new List<FaceCandidate> { new(1, 0.9f) };
        _faces.GetUnclusteredFacesAsync(ModelId, 0f, Arg.Any<CancellationToken>()).Returns(candidates);
        _faces.GetUnclusteredFacesAsync(ModelId, _options.MinQualityForClustering, Arg.Any<CancellationToken>()).Returns(candidates);

        await Create().ClusterAsync(ModelId);

        await _faces.Received(1).GetNearestAsync(1, ModelId, NeighborPool.Assigned, Arg.Any<int>(), 0f, Arg.Any<CancellationToken>());
        await _faces.Received(1).GetNearestAsync(
            1, ModelId, NeighborPool.Unassigned, Arg.Any<int>(), _options.MinQualityForClustering, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ClusterAsync_LowQualityFacesAreNeverGrouped_AndMatchingUsesNoQualityFloor()
    {
        _faces.GetUnclusteredFacesAsync(ModelId, 0f, Arg.Any<CancellationToken>())
            .Returns(new List<FaceCandidate> { new(1, 0.1f), new(2, 0.1f), new(3, 0.1f) });
        _faces.GetUnclusteredFacesAsync(ModelId, _options.MinQualityForClustering, Arg.Any<CancellationToken>()).Returns(new List<FaceCandidate>());

        await Create().ClusterAsync(ModelId);

        await _faces.Received(1).GetUnclusteredFacesAsync(ModelId, 0f, Arg.Any<CancellationToken>());
        await _faces.Received(1).GetUnclusteredFacesAsync(ModelId, _options.MinQualityForClustering, Arg.Any<CancellationToken>());
        await _faces.DidNotReceive().GetNearestAsync(
            Arg.Any<int>(), ModelId, NeighborPool.Unassigned, Arg.Any<int>(), Arg.Any<float>(), Arg.Any<CancellationToken>());
        await _faces.DidNotReceiveWithAnyArgs().CreateUnnamedPersonAsync(default, default, default);
        await _faces.Received(1).DeleteEmptyUnnamedPeopleAsync(Arg.Any<CancellationToken>());
        // Left as noise, but still stamped: they are never seeds again.
        await _faces.Received(1).MarkClusteredAsync(
            Arg.Is<IReadOnlyCollection<int>>(ids => ids.OrderBy(i => i).SequenceEqual(new[] { 1, 2, 3 })), Now, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ClusterAsync_OnlyUnclusteredFacesAreSeeds_AndAllOfThemAreStamped()
    {
        // 1 matches a person, 2 is a good-quality loner, 3 is blurry: all three are seeds of this run and get stamped.
        _faces.GetUnclusteredFacesAsync(ModelId, 0f, Arg.Any<CancellationToken>())
            .Returns(new List<FaceCandidate> { new(1, 0.9f), new(2, 0.8f), new(3, 0.1f) });
        _faces.GetUnclusteredFacesAsync(ModelId, _options.MinQualityForClustering, Arg.Any<CancellationToken>())
            .Returns(new List<FaceCandidate> { new(2, 0.8f) });
        _faces.GetNearestAsync(1, ModelId, NeighborPool.Assigned, Arg.Any<int>(), Arg.Any<float>(), Arg.Any<CancellationToken>())
            .Returns(new List<FaceNeighbor> { new(10, 7, 0.1f), new(11, 7, 0.2f) });

        await Create().ClusterAsync(ModelId);

        await _faces.DidNotReceiveWithAnyArgs().GetUnassignedFacesAsync(default, default, default);
        foreach (var id in new[] { 1, 2, 3 })
            await _faces.Received(1).GetNearestAsync(id, ModelId, NeighborPool.Assigned, Arg.Any<int>(), Arg.Any<float>(), Arg.Any<CancellationToken>());
        await _faces.Received(1).GetNearestAsync(
            Arg.Any<int>(), ModelId, NeighborPool.Unassigned, Arg.Any<int>(), Arg.Any<float>(), Arg.Any<CancellationToken>());
        await _faces.Received(1).GetNearestAsync(2, ModelId, NeighborPool.Unassigned, Arg.Any<int>(), Arg.Any<float>(), Arg.Any<CancellationToken>());
        await _faces.Received(1).MarkClusteredAsync(
            Arg.Is<IReadOnlyCollection<int>>(ids => ids.OrderBy(i => i).SequenceEqual(new[] { 1, 2, 3 })), Now, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ClusterAsync_NewFaceGroupsWithOldUnassignedNeighbours()
    {
        // 1 is new; 20 and 21 are older never-grouped faces (noise on their own, already stamped).
        _faces.GetUnclusteredFacesAsync(ModelId, Arg.Any<float>(), Arg.Any<CancellationToken>()).Returns(new List<FaceCandidate> { new(1, 0.9f) });
        var graph = new Dictionary<int, int[]> { [1] = new[] { 20, 21 }, [20] = new[] { 1, 21 }, [21] = new[] { 1, 20 } };
        foreach (var (id, near) in graph)
            _faces.GetNearestAsync(id, ModelId, NeighborPool.Unassigned, Arg.Any<int>(), Arg.Any<float>(), Arg.Any<CancellationToken>())
                .Returns(near.Select(o => new FaceNeighbor(o, null, 0.2f)).ToList());
        _faces.CreateUnnamedPersonAsync(1, Now, Arg.Any<CancellationToken>()).Returns(55);

        await Create().ClusterAsync(ModelId);

        await _faces.Received(1).AssignAsync(
            Arg.Is<IReadOnlyCollection<int>>(ids => ids.OrderBy(i => i).SequenceEqual(new[] { 1, 20, 21 })), 55, Arg.Any<float?>(), Arg.Any<CancellationToken>());
        await _faces.Received(1).MarkClusteredAsync(Arg.Is<IReadOnlyCollection<int>>(ids => ids.SequenceEqual(new[] { 1 })), Now, Arg.Any<CancellationToken>());
    }
}
