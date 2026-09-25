using System.IO;
using FluentAssertions;
using PictureManager.Application.Scanning;
using Xunit;

namespace PictureManager.Application.Tests.Scanning;

public class ImagePathResolverTests
{
    [Fact]
    public void ResolvePhysicalPath_AtRoot_CombinesMountAndFileName()
    {
        var result = ImagePathResolver.ResolvePhysicalPath("/images", string.Empty, "IMG001", ".jpg");
        result.Should().Be(Path.Combine("/images", "IMG001.jpg"));
    }

    [Fact]
    public void ResolvePhysicalPath_InSubfolder_CombinesAllSegments()
    {
        var result = ImagePathResolver.ResolvePhysicalPath("/images", "Vacation/Madeira", "IMG001", ".jpg");
        result.Should().Be(Path.Combine("/images", "Vacation", "Madeira", "IMG001.jpg"));
    }

    [Fact]
    public void ResolveFolderPath_TopFolder_IsTheMountPath()
    {
        ImagePathResolver.ResolveFolderPath("/mnt/dev", "").Should().Be("/mnt/dev");
    }

    [Fact]
    public void ResolveFolderPath_Subfolder_UsesTheOsSeparator()
    {
        ImagePathResolver.ResolveFolderPath("/mnt/dev", "Trips/Madeira")
            .Should().Be(Path.Combine("/mnt/dev", "Trips", "Madeira"));
    }
}
