using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http.HttpResults;
using NSubstitute;
using PictureManager.Api.Endpoints;
using PictureManager.Application.Faces;
using Xunit;

namespace PictureManager.Api.Tests.Endpoints;

public class PeopleEndpointsTests
{
    private readonly IFaceCropService _crops = Substitute.For<IFaceCropService>();

    [Fact]
    public async Task GetFaceThumbnailAsync_AbsolutePath_ReturnsPhysicalJpegFile()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".jpg");
        await File.WriteAllBytesAsync(path, new byte[] { 1 });
        try
        {
            _crops.GetOrCreateCropPathAsync(5, Arg.Any<CancellationToken>()).Returns(path);

            var result = await PeopleEndpoints.GetFaceThumbnailAsync(5, _crops, CancellationToken.None);

            var file = result.Should().BeOfType<PhysicalFileHttpResult>().Subject;
            file.FileName.Should().Be(path);
            file.ContentType.Should().Be("image/jpeg");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task GetFaceThumbnailAsync_NoCrop_ReturnsNotFound()
    {
        _crops.GetOrCreateCropPathAsync(6, Arg.Any<CancellationToken>()).Returns((string?)null);

        (await PeopleEndpoints.GetFaceThumbnailAsync(6, _crops, CancellationToken.None)).Should().BeOfType<NotFound>();
    }
}
