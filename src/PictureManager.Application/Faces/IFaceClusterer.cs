using System.Threading;
using System.Threading.Tasks;

namespace PictureManager.Application.Faces;

public interface IFaceClusterer
{
    /// <summary>Attaches unassigned faces to existing people, groups the rest into unnamed people, removes empty groups.</summary>
    Task ClusterAsync(int faceModelId, CancellationToken cancellationToken = default);
}
