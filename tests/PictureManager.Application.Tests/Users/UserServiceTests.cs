using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using PictureManager.Application.Common;
using PictureManager.Application.Repositories;
using PictureManager.Application.Users;
using PictureManager.Model;
using Xunit;

namespace PictureManager.Application.Tests.Users;

public class UserServiceTests
{
    private static readonly DateTime Now = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
    private readonly IAppUserRepository _users = Substitute.For<IAppUserRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly ICurrentUser _caller = Substitute.For<ICurrentUser>();
    private readonly AppUser _bob = new()
    {
        Id = 5, Username = "bob", NormalizedUsername = "bob", DisplayName = "Bob", Role = UserRole.User,
        IsActive = true, PasswordHash = "hash:old-password", SecurityStamp = Guid.NewGuid()
    };
    private readonly UserService _service;

    public UserServiceTests()
    {
        _clock.UtcNow.Returns(Now);
        _caller.UserId.Returns(1);
        _caller.IsAdmin.Returns(true);
        _hasher.Hash(Arg.Any<string>()).Returns(c => "hash:" + c.Arg<string>());
        _users.GetByIdAsync(5, Arg.Any<CancellationToken>()).Returns(_bob);
        _users.AddAsync(Arg.Any<AppUser>(), Arg.Any<CancellationToken>()).Returns(c =>
        {
            var user = c.Arg<AppUser>();
            user.Id = 9;
            return user;
        });
        _users.CountActiveAdminsAsync(Arg.Any<CancellationToken>()).Returns(2);
        _service = new UserService(_users, _hasher, _clock, _caller);
    }

    private static UserCreateInput NewUser(string? username = "carol", string? password = "long-enough-pw", string? role = "User") =>
        new(username, "Carol C", password, role, false);

    [Fact]
    public async Task Create_ValidInput_HashesPassword_ForcesPasswordChange_AndReturnsDto()
    {
        var result = await _service.CreateAsync(NewUser());

        result.IsSuccess.Should().BeTrue();
        result.Value!.Id.Should().Be(9);
        result.Value.Username.Should().Be("carol");
        result.Value.Role.Should().Be("User");
        result.Value.MustChangePassword.Should().BeTrue();
        result.Value.AlbumCount.Should().Be(0);
        await _users.Received(1).AddAsync(Arg.Is<AppUser>(u =>
            u.NormalizedUsername == "carol" && u.PasswordHash == "hash:long-enough-pw" && u.MustChangePassword
            && u.IsActive && u.CreatedAt == Now && !u.CanRunFolderActions), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_UsernameTakenIgnoringCaseAndWhitespace_IsConflict()
    {
        _users.GetByNormalizedUsernameAsync("bob", Arg.Any<CancellationToken>()).Returns(_bob);

        var result = await _service.CreateAsync(NewUser(username: "  BoB "));

        result.Status.Should().Be(ResultStatus.Conflict);
        await _users.DidNotReceive().AddAsync(Arg.Any<AppUser>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null, "username")]
    [InlineData("   ", "username")]
    [InlineData("has space", "username")]
    [InlineData("ünïcode", "username")]
    public async Task Create_BadUsername_IsInvalid(string? username, string field)
    {
        var result = await _service.CreateAsync(NewUser(username: username));

        result.Status.Should().Be(ResultStatus.Invalid);
        result.Errors.Should().ContainKey(field);
    }

    [Fact]
    public async Task Create_UsernameOver64Characters_IsInvalid() =>
        (await _service.CreateAsync(NewUser(username: new string('a', 65)))).Errors.Should().ContainKey("username");

    [Theory]
    [InlineData(null)]
    [InlineData("short")]
    public async Task Create_ShortPassword_IsInvalid(string? password) =>
        (await _service.CreateAsync(NewUser(password: password))).Errors.Should().ContainKey("password");

    [Theory]
    [InlineData(null)]
    [InlineData("root")]
    [InlineData("1")]
    public async Task Create_UnknownRole_IsInvalid(string? role) =>
        (await _service.CreateAsync(NewUser(role: role))).Errors.Should().ContainKey("role");

    [Fact]
    public async Task Create_BlankOrOverlongDisplayName_IsInvalid()
    {
        (await _service.CreateAsync(NewUser() with { DisplayName = "  " })).Errors.Should().ContainKey("displayName");
        (await _service.CreateAsync(NewUser() with { DisplayName = new string('x', 201) })).Errors.Should().ContainKey("displayName");
    }

    [Fact]
    public async Task Update_RoleChange_RotatesStamp_AndSaves()
    {
        var before = _bob.SecurityStamp;

        var result = await _service.UpdateAsync(5, new UserUpdateInput(null, "Admin", null, null));

        result.IsSuccess.Should().BeTrue();
        _bob.Role.Should().Be(UserRole.Admin);
        _bob.SecurityStamp.Should().NotBe(before);
        await _users.Received(1).UpdateAsync(_bob, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_DisplayNameOnly_DoesNotRotateStamp()
    {
        var before = _bob.SecurityStamp;

        var result = await _service.UpdateAsync(5, new UserUpdateInput("  Robert  ", null, null, null));

        result.Value!.DisplayName.Should().Be("Robert");
        _bob.SecurityStamp.Should().Be(before);
    }

    [Fact]
    public async Task Update_FolderActionsAndActiveFlags_RotateStamp()
    {
        var before = _bob.SecurityStamp;
        await _service.UpdateAsync(5, new UserUpdateInput(null, null, null, true));
        var afterFlag = _bob.SecurityStamp;
        await _service.UpdateAsync(5, new UserUpdateInput(null, null, false, null));

        _bob.CanRunFolderActions.Should().BeTrue();
        _bob.IsActive.Should().BeFalse();
        afterFlag.Should().NotBe(before);
        _bob.SecurityStamp.Should().NotBe(afterFlag);
    }

    [Fact]
    public async Task Update_UnknownUser_IsNotFound() =>
        (await _service.UpdateAsync(77, new UserUpdateInput("x", null, null, null))).Status.Should().Be(ResultStatus.NotFound);

    [Fact]
    public async Task Update_UnknownRole_IsInvalid() =>
        (await _service.UpdateAsync(5, new UserUpdateInput(null, "root", null, null))).Errors.Should().ContainKey("role");

    [Theory]
    [InlineData(false, null)]
    [InlineData(null, "User")]
    public async Task Update_AdminDisablingOrDemotingThemselves_IsConflict(bool? isActive, string? role)
    {
        var self = new AppUser { Id = 1, Username = "admin", Role = UserRole.Admin, IsActive = true };
        _users.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(self);

        var result = await _service.UpdateAsync(1, new UserUpdateInput(null, role, isActive, null));

        result.Status.Should().Be(ResultStatus.Conflict);
        self.Role.Should().Be(UserRole.Admin);
        self.IsActive.Should().BeTrue();
        await _users.DidNotReceive().UpdateAsync(Arg.Any<AppUser>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_AdminKeepingRoleAndActive_StillAllowedOnThemselves()
    {
        var self = new AppUser { Id = 1, Username = "admin", Role = UserRole.Admin, IsActive = true };
        _users.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(self);

        var result = await _service.UpdateAsync(1, new UserUpdateInput("Boss", "Admin", true, null));

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Update_DemotingTheLastActiveAdmin_IsConflict()
    {
        var other = new AppUser { Id = 6, Username = "root2", Role = UserRole.Admin, IsActive = true };
        _users.GetByIdAsync(6, Arg.Any<CancellationToken>()).Returns(other);
        _users.CountActiveAdminsAsync(Arg.Any<CancellationToken>()).Returns(1);

        var result = await _service.UpdateAsync(6, new UserUpdateInput(null, "User", null, null));

        result.Status.Should().Be(ResultStatus.Conflict);
        other.Role.Should().Be(UserRole.Admin);
    }

    [Fact]
    public async Task ResetPassword_HashesForcesChangeAndRotatesStamp()
    {
        var before = _bob.SecurityStamp;

        var result = await _service.ResetPasswordAsync(5, "brand-new-pw");

        result.IsSuccess.Should().BeTrue();
        _bob.PasswordHash.Should().Be("hash:brand-new-pw");
        _bob.MustChangePassword.Should().BeTrue();
        _bob.SecurityStamp.Should().NotBe(before);
        await _users.Received(1).UpdateAsync(_bob, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResetPassword_ShortPassword_IsInvalid_UnknownUser_IsNotFound()
    {
        (await _service.ResetPasswordAsync(5, "short")).Errors.Should().ContainKey("newPassword");
        (await _service.ResetPasswordAsync(77, "long-enough-pw")).Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task Delete_Transfer_PassesCallerAsTarget()
    {
        var result = await _service.DeleteAsync(5, AlbumDisposition.Transfer);

        result.IsSuccess.Should().BeTrue();
        await _users.Received(1).DeleteAsync(_bob, 1, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_DeleteAlbums_PassesNoTarget()
    {
        await _service.DeleteAsync(5, AlbumDisposition.Delete);

        await _users.Received(1).DeleteAsync(_bob, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_Self_IsConflict()
    {
        _users.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(new AppUser { Id = 1, Role = UserRole.Admin, IsActive = true });

        (await _service.DeleteAsync(1, AlbumDisposition.Transfer)).Status.Should().Be(ResultStatus.Conflict);
        await _users.DidNotReceive().DeleteAsync(Arg.Any<AppUser>(), Arg.Any<int?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_LastActiveAdmin_IsConflict_UnknownUser_IsNotFound()
    {
        var other = new AppUser { Id = 6, Username = "root2", Role = UserRole.Admin, IsActive = true };
        _users.GetByIdAsync(6, Arg.Any<CancellationToken>()).Returns(other);
        _users.CountActiveAdminsAsync(Arg.Any<CancellationToken>()).Returns(1);

        (await _service.DeleteAsync(6, AlbumDisposition.Transfer)).Status.Should().Be(ResultStatus.Conflict);
        (await _service.DeleteAsync(77, AlbumDisposition.Transfer)).Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task List_MapsRowsToDtos()
    {
        _users.ListAsync(Arg.Any<CancellationToken>()).Returns(new[] { new UserRow(_bob, 3) });

        var list = await _service.ListAsync();

        list.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Id = 5, Username = "bob", Role = "User", IsActive = true, AlbumCount = 3
        });
    }

    [Fact]
    public async Task Directory_ExcludesTheCaller()
    {
        _users.ListActiveDirectoryAsync(1, Arg.Any<CancellationToken>()).Returns(new[] { new DirectoryEntry(5, "Bob") });

        (await _service.DirectoryAsync()).Should().ContainSingle().Which.DisplayName.Should().Be("Bob");
    }
}
