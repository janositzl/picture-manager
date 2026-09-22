using System.Threading;
using System.Threading.Tasks;

namespace PictureManager.Application.Scanning;

public interface IContentHasher
{
    Task<string> ComputeAsync(string filePath, long fileSize, CancellationToken cancellationToken = default);
}
