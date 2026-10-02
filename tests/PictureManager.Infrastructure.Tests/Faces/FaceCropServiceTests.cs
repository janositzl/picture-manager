using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using PictureManager.Application.Repositories;
using PictureManager.Application.Thumbnails;
using PictureManager.Infrastructure.Faces;
using SkiaSharp;
using Xunit;

namespace PictureManager.Infrastructure.Tests.Faces;

public class FaceCropServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pm-crop-" + Guid.NewGuid().ToString("N"));
    private readonly IPeopleRepository _people = Substitute.For<IPeopleRepository>();
    private readonly IThumbnailService _thumbnails = Substitute.For<IThumbnailService>();

    public FaceCropServiceTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private FaceCropService Create() => new(_people, _thumbnails, new ThumbnailCacheOptions { RootPath = Path.Combine(_dir, "cache") });

    [Fact]
    public async Task GetOrCreateCropPathAsync_CropsJpegWithinBounds_AndCaches()
    {
        var previewPath = Path.Combine(_dir, "preview.png");
        using (var bitmap = new SKBitmap(800, 600))
        {
            bitmap.Erase(SKColors.CornflowerBlue);
            using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            await File.WriteAllBytesAsync(previewPath, data.ToArray());
        }
        _people.GetFaceCropSourceAsync(7, Arg.Any<CancellationToken>())
            .Returns(new FaceCropSource(7, "hash", "/mnt", "", "a", ".jpg", null, 0.25f, 0.25f, 0.5f, 0.5f));
        _thumbnails.GetOrCreateDerivativePathAsync(default!, default!, default, default, default)
            .ReturnsForAnyArgs(previewPath);
        var service = Create();

        var path = await service.GetOrCreateCropPathAsync(7);

        path.Should().NotBeNull();
        File.Exists(path).Should().BeTrue();
        using (var crop = SKBitmap.Decode(path!))
        {
            crop.Should().NotBeNull();
            Math.Max(crop.Width, crop.Height).Should().BeLessThanOrEqualTo(160);
        }
        using (var codec = SKCodec.Create(path!))
            codec.EncodedFormat.Should().Be(SKEncodedImageFormat.Jpeg);

        var again = await service.GetOrCreateCropPathAsync(7);

        again.Should().Be(path);
        await _people.Received(1).GetFaceCropSourceAsync(7, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetOrCreateCropPathAsync_UnknownFace_ReturnsNull()
    {
        _people.GetFaceCropSourceAsync(9, Arg.Any<CancellationToken>()).Returns((FaceCropSource?)null);

        (await Create().GetOrCreateCropPathAsync(9)).Should().BeNull();
    }
}
