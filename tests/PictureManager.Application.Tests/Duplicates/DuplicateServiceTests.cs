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

    private static DuplicateMemberRow Member(int id, string hash, string name, string relativePath) =>
        new(new ImageRow(id, 1, name, ".jpg", null, null, null, false, hash, DateTime.UtcNow, name.ToLowerInvariant()), "nas", relativePath);

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
}
