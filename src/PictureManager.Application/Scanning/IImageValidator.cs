using System.Threading;
using System.Threading.Tasks;

namespace PictureManager.Application.Scanning;

public interface IImageValidator
{
    Task<bool> IsValidAsync(string filePath, CancellationToken cancellationToken = default);
}
