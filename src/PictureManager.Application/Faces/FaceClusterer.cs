using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Application.Common;
using PictureManager.Application.Repositories;

namespace PictureManager.Application.Faces;

/// <summary>Idempotent: re-running only touches Unassigned faces; Confirmed/Rejected are never changed.</summary>
public sealed class FaceClusterer : IFaceClusterer
{
    private const int MatchNeighbors = 5;
    private const int MatchMinVotes = 2;
    private const int ClusterNeighbors = 32;

    private readonly IFaceRepository _faces;
    private readonly FaceRecognitionOptions _options;
    private readonly IClock _clock;

    public FaceClusterer(IFaceRepository faces, FaceRecognitionOptions options, IClock clock)
    {
        _faces = faces;
        _options = options;
        _clock = clock;
    }

    public async Task ClusterAsync(int faceModelId, CancellationToken cancellationToken = default)
    {
        // (a) Join existing people (named or unnamed groups) when a clear majority of near neighbours agree.
        foreach (var face in await _faces.GetUnassignedFacesAsync(faceModelId, minQuality: 0f, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var neighbors = await _faces.GetNearestAsync(face.Id, faceModelId, NeighborPool.Assigned, MatchNeighbors, cancellationToken);
            if (FaceClustering.MajorityPerson(neighbors, _options.AutoMatchDistance, MatchMinVotes) is int personId)
                await _faces.AssignAsync(new[] { face.Id }, personId, cancellationToken);
        }

        // (b) Group what's left (good-quality faces only) into new unnamed people.
        var remaining = await _faces.GetUnassignedFacesAsync(faceModelId, _options.MinQualityForClustering, cancellationToken);
        var quality = remaining.ToDictionary(f => f.Id, f => f.Quality);
        var clusters = await FaceClustering.DbscanAsync(
            remaining.Select(f => f.Id).ToList(),
            async id => (await _faces.GetNearestAsync(id, faceModelId, NeighborPool.Unassigned, ClusterNeighbors, cancellationToken))
                .Where(n => n.Distance <= _options.ClusterDistance)
                .Select(n => n.FaceId)
                .ToList(),
            _options.MinFacesPerGroup,
            cancellationToken);

        foreach (var cluster in clusters)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cover = cluster.MaxBy(id => quality[id]);
            var personId = await _faces.CreateUnnamedPersonAsync(cover, _clock.UtcNow, cancellationToken);
            await _faces.AssignAsync(cluster, personId, cancellationToken);
        }

        // (c) Groups emptied by re-processing (content changed, image deleted) disappear; named people stay.
        await _faces.DeleteEmptyUnnamedPeopleAsync(cancellationToken);
    }
}
