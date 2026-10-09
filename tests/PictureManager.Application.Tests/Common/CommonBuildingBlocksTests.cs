using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PictureManager.Application.Common;
using PictureManager.Application.DependencyInjection;
using PictureManager.Model;
using Xunit;

namespace PictureManager.Application.Tests.Common;

public class CommonBuildingBlocksTests
{
    private sealed record SampleCursor(
        [property: JsonPropertyName("k")] string Key,
        [property: JsonPropertyName("i")] int Id);

    [Fact]
    public void CursorCodec_RoundTrips()
    {
        var encoded = CursorCodec.Encode(new SampleCursor("abc", 42));

        CursorCodec.TryDecode<SampleCursor>(encoded, out var decoded).Should().BeTrue();
        decoded.Should().Be(new SampleCursor("abc", 42));
    }

    [Fact]
    public void CursorCodec_EncodedCursorIsUrlSafe()
    {
        var encoded = CursorCodec.Encode(new SampleCursor(new string('?', 40) + "~~>>", 7));

        encoded.Should().NotContainAny("+", "/", "=");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not base64 !!")]
    [InlineData("bm90IGpzb24")] // base64url of "not json"
    public void CursorCodec_TryDecode_Garbage_ReturnsFalse(string? cursor)
    {
        CursorCodec.TryDecode<SampleCursor>(cursor, out var decoded).Should().BeFalse();
        decoded.Should().BeNull();
    }

    [Fact]
    public void ImageUrls_WithHash_AreVersioned()
    {
        ImageUrls.Thumbnail(12, "D5A2").Should().Be("/api/images/12/thumbnail?v=D5A2");
        ImageUrls.Preview(12, "D5A2").Should().Be("/api/images/12/preview?v=D5A2");
    }

    [Fact]
    public void ImageUrls_RotatedThumbnail_VersionIncludesTheRotation()
    {
        ImageUrls.Thumbnail(12, "D5A2", 90).Should().Be("/api/images/12/thumbnail?v=D5A2-r90");
        ImageUrls.Thumbnail(12, "D5A2", 0).Should().Be("/api/images/12/thumbnail?v=D5A2");
    }

    [Fact]
    public void ImageUrls_WithoutHash_AreNull()
    {
        ImageUrls.Thumbnail(12, string.Empty).Should().BeNull();
        ImageUrls.Preview(12, string.Empty).Should().BeNull();
    }

    [Theory]
    [InlineData("nas", "", "nas")]
    [InlineData("nas", "Holidays/Madeira", "nas/Holidays/Madeira")]
    public void FolderDisplayPath_JoinsRootNameAndRelativePath(string root, string relative, string expected)
    {
        FolderDisplayPath.For(root, relative).Should().Be(expected);
    }

    [Fact]
    public void Result_FailureConvertsToGenericResult_KeepingStatusAndErrors()
    {
        Result<int> converted = Result.Invalid("limit", "Must be between 1 and 200.");

        converted.Status.Should().Be(ResultStatus.Invalid);
        converted.Errors!["limit"].Should().Equal("Must be between 1 and 200.");
    }

    [Fact]
    public void Result_SuccessDoesNotConvertToGenericResult()
    {
        var act = () => { Result<int> _ = Result.Ok(); };

        act.Should().Throw<System.InvalidOperationException>();
    }
}
