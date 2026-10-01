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
        _faces.GetNearestAsync(Arg.Any<int>(), ModelId, Arg.Any<NeighborPool>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<FaceNeighbor>());
    }

    private FaceClusterer Create() => new(_faces, _options, _clock);

    [Fact]
    public async Task ClusterAsync_AttachesFaceToPersonWhenAssignedNeighboursAgree()
    {
        _faces.GetUnassignedFacesAsync(ModelId, 0f, Arg.Any<CancellationToken>()).Returns(new List<FaceCandidate> { new(1, 0.9f) });
        _faces.GetUnassignedFacesAsync(ModelId, _options.MinQualityForClustering, Arg.Any<CancellationToken>()).Returns(new List<FaceCandidate>());
        _faces.GetNearestAsync(1, ModelId, NeighborPool.Assigned, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<FaceNeighbor> { new(10, 7, 0.1f), new(11, 7, 0.2f), new(12, 8, 0.9f) });

        await Create().ClusterAsync(ModelId);

        await _faces.Received(1).AssignAsync(Arg.Is<IReadOnlyCollection<int>>(ids => ids.SequenceEqual(new[] { 1 })), 7, Arg.Any<CancellationToken>());
        await _faces.DidNotReceiveWithAnyArgs().CreateUnnamedPersonAsync(default, default, default);
    }

    [Fact]
    public async Task ClusterAsync_GroupsDenseRemainder_WithBestQualityAsCover_AndCleansUp()
    {
        var candidates = new List<FaceCandidate> { new(1, 0.6f), new(2, 0.95f), new(3, 0.7f) };
        _faces.GetUnassignedFacesAsync(ModelId, 0f, Arg.Any<CancellationToken>()).Returns(candidates);
        _faces.GetUnassignedFacesAsync(ModelId, _options.MinQualityForClustering, Arg.Any<CancellationToken>()).Returns(candidates);
        foreach (var id in new[] { 1, 2, 3 })
            _faces.GetNearestAsync(id, ModelId, NeighborPool.Unassigned, Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(new[] { 1, 2, 3 }.Where(o => o != id).Select(o => new FaceNeighbor(o, null, 0.2f)).ToList());
        _faces.CreateUnnamedPersonAsync(2, Now, Arg.Any<CancellationToken>()).Returns(55);

        await Create().ClusterAsync(ModelId);

        await _faces.Received(1).CreateUnnamedPersonAsync(2, Now, Arg.Any<CancellationToken>());
        await _faces.Received(1).AssignAsync(
            Arg.Is<IReadOnlyCollection<int>>(ids => ids.OrderBy(i => i).SequenceEqual(new[] { 1, 2, 3 })), 55, Arg.Any<CancellationToken>());
        await _faces.Received(1).DeleteEmptyUnnamedPeopleAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ClusterAsync_LowQualityFacesAreNeverGrouped_AndMatchingUsesNoQualityFloor()
    {
        _faces.GetUnassignedFacesAsync(ModelId, 0f, Arg.Any<CancellationToken>())
            .Returns(new List<FaceCandidate> { new(1, 0.1f), new(2, 0.1f), new(3, 0.1f) });
        _faces.GetUnassignedFacesAsync(ModelId, _options.MinQualityForClustering, Arg.Any<CancellationToken>()).Returns(new List<FaceCandidate>());

        await Create().ClusterAsync(ModelId);

        await _faces.Received(1).GetUnassignedFacesAsync(ModelId, 0f, Arg.Any<CancellationToken>());
        await _faces.Received(1).GetUnassignedFacesAsync(ModelId, _options.MinQualityForClustering, Arg.Any<CancellationToken>());
        await _faces.DidNotReceiveWithAnyArgs().CreateUnnamedPersonAsync(default, default, default);
        await _faces.Received(1).DeleteEmptyUnnamedPeopleAsync(Arg.Any<CancellationToken>());
    }
}
