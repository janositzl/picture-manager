using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Application.Repositories;

namespace PictureManager.Application.Faces;

public interface IFaceClusterer
{
    /// <summary>Attaches unassigned faces to existing people, groups the rest into unnamed people, removes empty groups.</summary>
    Task ClusterAsync(int faceModelId, CancellationToken cancellationToken = default);

    /// <summary>Tries to attach just these Unknown faces to existing people (the matching step of ClusterAsync). Returns how many were suggested.</summary>
    Task<int> MatchAsync(int faceModelId, IReadOnlyList<FaceCandidate> faces, CancellationToken cancellationToken = default);
}
