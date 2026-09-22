using System.Threading;
using System.Threading.Tasks;

namespace PictureManager.Application.Scanning;

public interface IDevImageRootSeeder
{
    Task SeedAsync(CancellationToken cancellationToken = default);
}
