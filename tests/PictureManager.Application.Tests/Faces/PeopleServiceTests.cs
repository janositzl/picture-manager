using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using PictureManager.Application.Common;
using PictureManager.Application.Faces;
using PictureManager.Application.Repositories;
using Xunit;

namespace PictureManager.Application.Tests.Faces;

public class PeopleServiceTests
{
    private readonly IPeopleRepository _people = Substitute.For<IPeopleRepository>();
    private readonly IClock _clock = Substitute.For<IClock>();

    public PeopleServiceTests()
    {
        _clock.UtcNow.Returns(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));
        _people.GetAsync(1, Arg.Any<CancellationToken>()).Returns(new PersonSummary(1, null, 5, 4, 100));
        _people.GetAsync(2, Arg.Any<CancellationToken>()).Returns(new PersonSummary(2, "Anna", 9, 8, 200));
    }

    private PeopleService Create() => new(_people, _clock);

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task NameAsync_Blank_IsInvalid(string? name)
    {
        var result = await Create().NameAsync(1, name);

        result.Status.Should().Be(ResultStatus.Invalid);
        await _people.DidNotReceiveWithAnyArgs().SetNameAsync(default, default!, default, default);
    }

    [Fact]
    public async Task NameAsync_UnknownPerson_IsNotFound()
    {
        (await Create().NameAsync(99, "Anna")).Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task NameAsync_NewName_TrimsAndSetsIt()
    {
        _people.FindIdByNameAsync("Bela", Arg.Any<CancellationToken>()).Returns((int?)null);

        var result = await Create().NameAsync(1, "  Bela ");

        result.Status.Should().Be(ResultStatus.Success);
        await _people.Received(1).SetNameAsync(1, "Bela", Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NameAsync_ExistingNameOtherCase_MergesIntoThatPerson()
    {
        _people.FindIdByNameAsync("anna", Arg.Any<CancellationToken>()).Returns(2);

        var result = await Create().NameAsync(1, "anna");

        result.Value!.Id.Should().Be(2);
        await _people.Received(1).MergeAsync(1, 2, Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        await _people.DidNotReceiveWithAnyArgs().SetNameAsync(default, default!, default, default);
    }

    [Fact]
    public async Task NameAsync_SamePersonsOwnName_JustRenames()
    {
        _people.FindIdByNameAsync("ANNA", Arg.Any<CancellationToken>()).Returns(2);

        await Create().NameAsync(2, "ANNA");

        await _people.Received(1).SetNameAsync(2, "ANNA", Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        await _people.DidNotReceiveWithAnyArgs().MergeAsync(default, default, default, default);
    }

    [Fact]
    public async Task NameAsync_TooLong_IsInvalid()
    {
        (await Create().NameAsync(1, new string('x', 201))).Status.Should().Be(ResultStatus.Invalid);
    }

    [Fact]
    public async Task DeleteAsync_ExistingPerson_IsOk()
    {
        _people.DeleteAsync(2, Arg.Any<CancellationToken>()).Returns(true);

        (await Create().DeleteAsync(2)).Status.Should().Be(ResultStatus.Success);
    }

    [Fact]
    public async Task DeleteAsync_MissingPerson_IsNotFound()
    {
        _people.DeleteAsync(99, Arg.Any<CancellationToken>()).Returns(false);

        (await Create().DeleteAsync(99)).Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task IgnoreGroupAsync_UnnamedGroup_ReturnsFaceCount()
    {
        _people.IgnoreGroupAsync(1, Arg.Any<CancellationToken>()).Returns(4);

        var result = await Create().IgnoreGroupAsync(1);

        result.Status.Should().Be(ResultStatus.Success);
        result.Value.Should().Be(new CountResponse(4));
    }

    [Fact]
    public async Task IgnoreGroupAsync_NamedPerson_IsInvalid()
    {
        (await Create().IgnoreGroupAsync(2)).Status.Should().Be(ResultStatus.Invalid);
        await _people.DidNotReceiveWithAnyArgs().IgnoreGroupAsync(default, default);
    }

    [Fact]
    public async Task IgnoreGroupAsync_MissingGroup_IsNotFound()
    {
        (await Create().IgnoreGroupAsync(99)).Status.Should().Be(ResultStatus.NotFound);
    }
}
