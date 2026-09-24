using System;
using FluentAssertions;
using PictureManager.Application.Scanning;
using Xunit;

namespace PictureManager.Application.Tests.Scanning;

public class ScanningOptionsTests
{
    [Fact]
    public void FromConfig_NormalizesExtensions_SoATypoCannotExcludeEveryFile()
    {
        var options = ScanningOptions.FromConfig(new[] { "jpg", " .PNG ", ".jpg" });

        options.SupportedExtensions.Should().Equal(".jpg", ".png");
    }

    [Fact]
    public void FromConfig_Missing_AllowsEverything()
    {
        ScanningOptions.FromConfig(null).SupportedExtensions.Should().BeEmpty();
    }

    [Fact]
    public void FromConfig_BlankEntry_FailsFast()
    {
        var act = () => ScanningOptions.FromConfig(new[] { ".jpg", " " });

        act.Should().Throw<InvalidOperationException>().WithMessage("*Scanning:SupportedExtensions*");
    }
}
