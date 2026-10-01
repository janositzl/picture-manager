using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using PictureManager.Application.Common;
using PictureManager.Application.Duplicates;
using PictureManager.Application.Images;
using PictureManager.Application.Repositories;
using Xunit;

namespace PictureManager.Application.Tests.Duplicates;

public class DuplicateServiceTests
{
    private readonly IImageQueryRepository _images = Substitute.For<IImageQueryRepository>();

    private DuplicateService CreateService() => new(_images);

    private static DuplicateMemberRow Member(int id, string hash, string name, string relativePath, int? width = null, int? height = null, long fileSize = 0) =>
        new(new ImageRow(id, 1, name, ".jpg", width, height, null, false, hash, DateTime.UtcNow, name.ToLowerInvariant(), "nas", relativePath), "nas", relativePath, fileSize);

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task ListAsync_LimitOutOfRange_ReturnsInvalid(int limit)
    {
        (await CreateService().ListAsync(null, limit)).Errors!.Keys.Should().Contain("limit");
    }

    [Fact]
    public async Task ListAsync_MalformedCursor_ReturnsInvalid()
    {
        (await CreateService().ListAsync("!!", null)).Errors!.Keys.Should().Contain("cursor");
    }

    [Fact]
    public async Task ListAsync_AssemblesGroups_MembersOrderedByFolderPathThenName_WithNextCursor()
    {
        _images.GetDuplicateGroupsAsync(null, 2, Arg.Any<CancellationToken>()).Returns(new[]
        {
            new DuplicateGroupKey("AAA", 2), new DuplicateGroupKey("BBB", 2)
        });
        _images.GetDuplicateMembersAsync(Arg.Is<IReadOnlyCollection<string>>(h => h.SequenceEqual(new[] { "AAA" })), Arg.Any<CancellationToken>())
            .Returns(new[] { Member(2, "AAA", "b", "Trip"), Member(1, "AAA", "a", "") });

        var page = (await CreateService().ListAsync(null, 1)).Value!;

        page.Items.Should().ContainSingle();
        var group = page.Items[0];
        group.ContentHash.Should().Be("AAA");
        group.Count.Should().Be(2);
        group.Images.Select(i => i.FolderPath).Should().Equal("nas", "nas/Trip");
        group.Images[0].ThumbnailUrl.Should().Be("/api/images/1/thumbnail?v=AAA");
        CursorCodec.TryDecode<DuplicateCursor>(page.NextCursor, out var cursor).Should().BeTrue();
        cursor.Should().Be(new DuplicateCursor(2, "AAA"));
    }

    [Fact]
    public async Task ListAsync_CursorIsPassedAsKeyset()
    {
        _images.GetDuplicateGroupsAsync(Arg.Any<DuplicateGroupKey?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<DuplicateGroupKey>());

        var page = (await CreateService().ListAsync(CursorCodec.Encode(new DuplicateCursor(3, "XYZ")), null)).Value!;

        page.Items.Should().BeEmpty();
        page.NextCursor.Should().BeNull();
        await _images.Received(1).GetDuplicateGroupsAsync(new DuplicateGroupKey("XYZ", 3), 51, Arg.Any<CancellationToken>());
        await _images.DidNotReceive().GetDuplicateMembersAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(8)]
    public async Task ListSimilarAsync_ThresholdOutOfRange_ReturnsInvalid(int threshold)
    {
        (await CreateService().ListSimilarAsync(threshold, null, null)).Errors!.Keys.Should().Contain("threshold");
    }

    [Fact]
    public async Task ListSimilarAsync_MalformedCursor_ReturnsInvalid()
    {
        (await CreateService().ListSimilarAsync(null, "!!", null)).Errors!.Keys.Should().Contain("cursor");
    }

    [Fact]
    public async Task ListSimilarAsync_GroupsAndOrdersLargestFirst()
    {
        _images.GetPerceptualHashesAsync(Arg.Any<CancellationToken>()).Returns(new[]
        {
            new PerceptualHashRow(5, "0F0F0F0F0F0F0F0F"),
            new PerceptualHashRow(3, "0F0F0F0F0F0F0F0E"),
            new PerceptualHashRow(9, "0F0F0F0F0F0F0F0C"),
            new PerceptualHashRow(7, "AAAAAAAAAAAAAAAA"),
            new PerceptualHashRow(8, "not-a-hash")
        });
        _images.GetMembersByIdsAsync(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>()).Returns(new[]
        {
            Member(5, "H5", "small", "", 800, 600, 10),
            Member(3, "H3", "big", "", 4000, 3000, 30),
            Member(9, "H9", "mid", "", 1600, 1200, 20)
        });

        var page = (await CreateService().ListSimilarAsync(null, null, null)).Value!;

        page.Items.Should().ContainSingle();
        var group = page.Items[0];
        group.Key.Should().Be("s3");
        group.Count.Should().Be(3);
        group.MaxDistance.Should().Be(2);
        group.Images.Select(i => i.Id).Should().Equal(3, 9, 5);
        group.Images[0].FileSize.Should().Be(30);
        page.NextCursor.Should().BeNull();
    }

    [Fact]
    public async Task ListSimilarAsync_PagesByOffset()
    {
        _images.GetPerceptualHashesAsync(Arg.Any<CancellationToken>()).Returns(new[]
        {
            new PerceptualHashRow(1, "0F0F0F0F0F0F0F0F"), new PerceptualHashRow(2, "0F0F0F0F0F0F0F0E"),
            new PerceptualHashRow(3, "AAAAAAAAAAAAAAAA"), new PerceptualHashRow(4, "AAAAAAAAAAAAAAA8"),
            new PerceptualHashRow(5, "3C3C3C3C3C3C3C3C"), new PerceptualHashRow(6, "3C3C3C3C3C3C3C3D")
        });
        _images.GetMembersByIdsAsync(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<IReadOnlyCollection<int>>().Select(id => Member(id, "H" + id, "n" + id, "")).ToList());

        var first = (await CreateService().ListSimilarAsync(null, null, 2)).Value!;
        first.Items.Should().HaveCount(2);
        first.NextCursor.Should().NotBeNull();

        var second = (await CreateService().ListSimilarAsync(null, first.NextCursor, 2)).Value!;
        second.Items.Should().HaveCount(1);
        second.NextCursor.Should().BeNull();
        second.Items.Concat(first.Items).Select(g => g.Key).Should().OnlyHaveUniqueItems();
    }
}
