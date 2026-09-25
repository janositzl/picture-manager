using FluentAssertions;
using PictureManager.Application.Scanning;
using Xunit;

namespace PictureManager.Application.Tests.Scanning;

public class PathNormalizerTests
{
    [Fact]
    public void Normalize_ConvertsToNfc()
    {
        var decomposed = "e" + "́"; // "e" + combining acute accent (U+0301) — NOT the precomposed "é"
        var result = PathNormalizer.Normalize(decomposed);
        result.Should().Be("é"); // precomposed "é"
    }

    [Fact]
    public void Combine_WithEmptyBase_ReturnsJustTheSegment()
    {
        PathNormalizer.Combine(string.Empty, "Vacation").Should().Be("Vacation");
    }

    [Fact]
    public void Combine_WithNonEmptyBase_JoinsWithForwardSlash()
    {
        PathNormalizer.Combine("Vacation", "Madeira").Should().Be("Vacation/Madeira");
    }

    [Theory]
    [InlineData("Vacation", "vacation", true)]
    [InlineData("Vacation", "Vacation ", false)]
    [InlineData(null, "", true)]
    public void NormalizedEquals_IsCaseInsensitive(string? a, string? b, bool expected)
    {
        PathNormalizer.NormalizedEquals(a, b).Should().Be(expected);
    }

    [Fact]
    public void FolderNameKey_FoldsCaseAndUnicodeNormalization()
    {
        PathNormalizer.FolderNameKey("Café").Should().Be(PathNormalizer.FolderNameKey("CAFÉ"));
    }
}
