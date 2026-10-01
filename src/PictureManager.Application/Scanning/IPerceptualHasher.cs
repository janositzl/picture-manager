using System.Threading;
using System.Threading.Tasks;

namespace PictureManager.Application.Scanning;

public interface IPerceptualHasher
{
    /// <summary>64-bit dHash as 16 lowercase hex chars, computed after applying EXIF orientation; null if undecodable.</summary>
    Task<string?> ComputeAsync(string filePath, int? orientation, CancellationToken cancellationToken = default);
}
