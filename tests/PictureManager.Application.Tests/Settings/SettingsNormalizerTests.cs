using FluentAssertions;
using PictureManager.Application.Settings;
using Xunit;

namespace PictureManager.Application.Tests.Settings;

public class SettingsNormalizerTests
{
    [Fact]
    public void TryNormalizeFolderNames_TrimsAndDedupesCaseInsensitively_KeepingFirstSpelling()
    {
        SettingsNormalizer.TryNormalizeFolderNames(new[] { " raw ", "RAW", "@eaDir" }, out var names, out _).Should().BeTrue();

        names.Should().Equal("raw", "@eaDir");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    public void TryNormalizeFolderNames_RejectsBlankOrSeparators(string? value)
    {
        SettingsNormalizer.TryNormalizeFolderNames(new[] { value }, out _, out var error).Should().BeFalse();
        error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void TryNormalizeExtensions_LowercasesAddsDotAndDedupes()
    {
        SettingsNormalizer.TryNormalizeExtensions(new[] { "HEIC", ".heic", " .Png " }, out var extensions, out _).Should().BeTrue();

        extensions.Should().Equal(".heic", ".png");
    }

    [Theory]
    [InlineData(".")]
    [InlineData(" ")]
    [InlineData("a/b")]
    public void TryNormalizeExtensions_RejectsBlankDotOrSeparators(string value)
    {
        SettingsNormalizer.TryNormalizeExtensions(new[] { value }, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void TryNormalize_NullList_IsEmpty()
    {
        SettingsNormalizer.TryNormalizeFolderNames(null, out var names, out _).Should().BeTrue();
        names.Should().BeEmpty();
    }
}
