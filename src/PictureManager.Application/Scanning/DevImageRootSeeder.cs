using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Application.Common;
using PictureManager.Application.Repositories;
using PictureManager.Model;

namespace PictureManager.Application.Scanning;

public sealed class DevImageRootSeeder : IDevImageRootSeeder
{
    private readonly IImageRootRepository _imageRootRepository;
    private readonly IClock _clock;
    private readonly DevImageRootOptions _options;

    public DevImageRootSeeder(IImageRootRepository imageRootRepository, IClock clock, DevImageRootOptions options)
    {
        _imageRootRepository = imageRootRepository;
        _clock = clock;
        _options = options;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.Name) || string.IsNullOrWhiteSpace(_options.MountPath))
            return;

        var existingRoots = await _imageRootRepository.GetAllAsync(cancellationToken);
        if (existingRoots.Any(r => r.Name == _options.Name))
            return;

        await _imageRootRepository.AddAsync(new ImageRoot
        {
            Name = _options.Name,
            MountPath = _options.MountPath,
            IsActive = true,
            CreatedUtc = _clock.UtcNow
        }, cancellationToken);
    }
}
