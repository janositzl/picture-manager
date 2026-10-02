using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Application.Common;
using PictureManager.Application.Repositories;

namespace PictureManager.Application.Faces;

/// <summary>
/// Idempotent: re-running only touches Unassigned faces; Confirmed/Rejected are never changed. Each pass seeds only
/// from faces no earlier pass has seen (ClusteredUtc null) and stamps them, so a job costs O(new faces), not
/// O(library); the neighbour pool stays global, so a new face can still group with older unassigned faces.
/// </summary>
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
        var seeds = await _faces.GetUnclusteredFacesAsync(faceModelId, minQuality: 0f, cancellationToken);
        foreach (var face in seeds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var neighbors = await _faces.GetNearestAsync(
                face.Id, faceModelId, NeighborPool.Assigned, MatchNeighbors, minQuality: 0f, cancellationToken);
            if (FaceClustering.MajorityPerson(neighbors, _options.AutoMatchDistance, MatchMinVotes) is int personId)
                await _faces.AssignAsync(new[] { face.Id }, personId, cancellationToken);
        }

        // (b) Group what's left (good-quality faces only) into new unnamed people. DBSCAN starts from the new faces
        // but may expand into any good-quality Unassigned face of the model (older noise included).
        var remaining = await _faces.GetUnclusteredFacesAsync(faceModelId, _options.MinQualityForClustering, cancellationToken);
        var quality = remaining.ToDictionary(f => f.Id, f => f.Quality);
        var clusters = await FaceClustering.DbscanAsync(
            remaining.Select(f => f.Id).ToList(),
            async id => (await _faces.GetNearestAsync(
                    id, faceModelId, NeighborPool.Unassigned, ClusterNeighbors, _options.MinQualityForClustering, cancellationToken))
                .Where(n => n.Distance <= _options.ClusterDistance)
                .Select(n => n.FaceId)
                .ToList(),
            _options.MinFacesPerGroup,
            cancellationToken);

        foreach (var cluster in clusters)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Every cluster contains a seed; older members' quality is not loaded, so the best seed is the cover.
            var cover = cluster.Where(quality.ContainsKey).MaxBy(id => quality[id]);
            var personId = await _faces.CreateUnnamedPersonAsync(cover, _clock.UtcNow, cancellationToken);
            await _faces.AssignAsync(cluster, personId, cancellationToken);
        }

        // Matched, grouped or left as noise: these faces are never seeds again (they stay neighbour candidates).
        await _faces.MarkClusteredAsync(seeds.Select(f => f.Id).Union(remaining.Select(f => f.Id)).ToList(), _clock.UtcNow, cancellationToken);

        // (c) Groups emptied by re-processing (content changed, image deleted) disappear; named people stay.
        await _faces.DeleteEmptyUnnamedPeopleAsync(cancellationToken);
    }
}
