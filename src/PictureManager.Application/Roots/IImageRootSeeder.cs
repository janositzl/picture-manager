using System.Threading;
using System.Threading.Tasks;

namespace PictureManager.Application.Roots;

public interface IImageRootSeeder
{
    Task SeedAsync(CancellationToken cancellationToken = default);
}
