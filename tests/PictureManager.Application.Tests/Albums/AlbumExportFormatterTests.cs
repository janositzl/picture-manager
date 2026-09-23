using FluentAssertions;
using PictureManager.Application.Albums;
using Xunit;

namespace PictureManager.Application.Tests.Albums;

public class AlbumExportFormatterTests
{
    private static readonly AlbumExportRow Madeira = new("nas-photos", "family_photos", "Holidays/Madeira", "IMG_4471", ".jpg");
    private static readonly AlbumExportRow TopLevel = new("nas-photos", "family_photos", "", "IMG_0001", ".jpg");
    private static readonly AlbumExportRow NoAlias = new("work-nas", null, "2025/Q3", "whiteboard", ".png");

    [Theory]
    [InlineData(null, "/family_photos/IMG_0001.jpg")]
    [InlineData("", "/family_photos/IMG_0001.jpg")]
    [InlineData("/mnt", "/mnt/family_photos/IMG_0001.jpg")]
    [InlineData("/mnt/", "/mnt/family_photos/IMG_0001.jpg")]
    [InlineData("/mnt//", "/mnt/family_photos/IMG_0001.jpg")]
    [InlineData(@"\\nas\share", @"\\nas\share/family_photos/IMG_0001.jpg")]
    public void FormatLine_TopLevelImage_DropsEmptyRelativePath(string? prefix, string expected)
    {
        AlbumExportFormatter.FormatLine(prefix, TopLevel).Should().Be(expected);
    }

    [Fact]
    public void FormatLine_NestedImage_IncludesRelativePath()
    {
        AlbumExportFormatter.FormatLine("/mnt", Madeira).Should().Be("/mnt/family_photos/Holidays/Madeira/IMG_4471.jpg");
    }

    [Fact]
    public void FormatLine_RootWithoutAlias_UsesRootName()
    {
        AlbumExportFormatter.FormatLine(null, NoAlias).Should().Be("/work-nas/2025/Q3/whiteboard.png");
    }

    [Fact]
    public void Format_EndsEveryLineWithNewline_InRowOrder()
    {
        AlbumExportFormatter.Format(null, new[] { Madeira, NoAlias })
            .Should().Be("/family_photos/Holidays/Madeira/IMG_4471.jpg\n/work-nas/2025/Q3/whiteboard.png\n");
    }

    [Fact]
    public void Format_NoRows_IsEmpty()
    {
        AlbumExportFormatter.Format("/mnt", System.Array.Empty<AlbumExportRow>()).Should().BeEmpty();
    }

    [Theory]
    [InlineData("Nyaralás 2025", "Nyaralás 2025.txt")]
    [InlineData("a/b:c*?\"<>|d", "a_b_c______d.txt")]
    [InlineData("   ", "album.txt")]
    public void FileName_ReplacesCharactersInvalidInFileNames(string albumName, string expected)
    {
        AlbumExportFormatter.FileName(albumName).Should().Be(expected);
    }
}
