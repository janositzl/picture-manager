using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Application.Common;
using PictureManager.Application.Repositories;
using PictureManager.Model;

namespace PictureManager.Application.Scanning;

public sealed class ImageEnrichmentService : IImageEnrichmentService
{
    private readonly IImageRepository _imageRepository;
    private readonly IContentHasher _contentHasher;
    private readonly IExifReader _exifReader;
    private readonly IImageValidator _imageValidator;
    private readonly IPerceptualHasher _perceptualHasher;
    private readonly IClock _clock;

    public ImageEnrichmentService(
        IImageRepository imageRepository, IContentHasher contentHasher, IExifReader exifReader, IImageValidator imageValidator,
        IPerceptualHasher perceptualHasher, IClock clock)
    {
        _imageRepository = imageRepository;
        _contentHasher = contentHasher;
        _exifReader = exifReader;
        _imageValidator = imageValidator;
        _perceptualHasher = perceptualHasher;
        _clock = clock;
    }

    public async Task EnrichAsync(int imageId, CancellationToken cancellationToken = default)
    {
        var image = await _imageRepository.GetByIdWithFolderAsync(imageId, cancellationToken);
        if (image?.Folder?.Root is null)
            return;

        var physicalPath = ImagePathResolver.ResolvePhysicalPath(
            image.Folder.Root.MountPath, image.Folder.RelativePath, image.FileName, image.Extension);

        if (!File.Exists(physicalPath))
        {
            image.MissingSinceUtc = _clock.UtcNow;
            image.UpdatedAt = _clock.UtcNow;
            await _imageRepository.UpdateAsync(image, cancellationToken);
            return;
        }

        var hash = await _contentHasher.ComputeAsync(physicalPath, image.FileSize, cancellationToken);
        var exif = await _exifReader.ReadAsync(physicalPath, cancellationToken);
        var isValid = await _imageValidator.IsValidAsync(physicalPath, cancellationToken);

        // "" marks an image whose hash couldn't be computed (undecodable), so it isn't retried forever.
        // A failing hash must never fail enrichment: only cancellation propagates.
        string? perceptual = null;
        if (isValid)
        {
            try
            {
                perceptual = await _perceptualHasher.ComputeAsync(physicalPath, exif.Orientation, cancellationToken) ?? string.Empty;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                perceptual = string.Empty;
            }
        }

        var possibleMove = await _imageRepository.GetMissingByContentHashAsync(hash, cancellationToken);
        if (possibleMove is not null && possibleMove.Id != image.Id)
        {
            await _imageRepository.DeleteAsync(possibleMove, cancellationToken);
        }

        image.ContentHash = hash;
        image.Width = exif.Width;
        image.Height = exif.Height;
        image.Orientation = exif.Orientation;
        image.DateTaken = exif.DateTaken;
        image.CameraMake = exif.CameraMake;
        image.CameraModel = exif.CameraModel;
        image.LensModel = exif.LensModel;
        image.Latitude = exif.Latitude;
        image.Longitude = exif.Longitude;
        image.RawMetadata = exif.RawMetadataJson;
        image.PerceptualHash = perceptual;
        image.IndexState = isValid ? IndexState.Indexed : IndexState.Invalid;
        image.MissingSinceUtc = null;
        image.UpdatedAt = _clock.UtcNow;

        await _imageRepository.UpdateAsync(image, cancellationToken);
    }
}
