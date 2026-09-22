using System.Threading;
using System.Threading.Tasks;

namespace PictureManager.Application.Scanning;

public interface IExifReader
{
    Task<ExifData> ReadAsync(string filePath, CancellationToken cancellationToken = default);
}
