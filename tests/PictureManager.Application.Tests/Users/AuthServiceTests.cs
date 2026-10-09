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

public class AuthServiceTests
{
    private static readonly DateTime Now = new(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);
    private readonly IAppUserRepository _users = Substitute.For<IAppUserRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly AppUser _bob = new()
    {
        Id = 5, Username = "Bob", NormalizedUsername = "bob", DisplayName = "Bob B", Role = UserRole.User,
        PasswordHash = "hash:secret-pw", IsActive = true
    };
    private readonly AuthService _service;

    public AuthServiceTests()
    {
        _clock.UtcNow.Returns(Now);
        _hasher.Verify(Arg.Any<string>(), Arg.Any<string>()).Returns(c => c.ArgAt<string>(0) == "hash:" + c.ArgAt<string>(1));
        _hasher.Hash(Arg.Any<string>()).Returns(c => "hash:" + c.Arg<string>());
        _users.GetByNormalizedUsernameAsync("bob", Arg.Any<CancellationToken>()).Returns(_bob);
        _users.GetByIdAsync(5, Arg.Any<CancellationToken>()).Returns(_bob);
        _service = new AuthService(_users, _hasher, _clock);
    }

    [Fact]
    public async Task Login_CorrectPassword_CaseInsensitiveUsername_ReturnsUserAndStampsLastLogin()
    {
        var user = await _service.LoginAsync("  BOB ", "secret-pw");

        user.Should().NotBeNull();
        user!.Id.Should().Be(5);
        user.CanRunFolderActions.Should().BeFalse();
        _bob.LastLoginAt.Should().Be(Now);
        await _users.Received(1).UpdateAsync(_bob, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("bob", "wrong-pw")]
    [InlineData("nobody", "secret-pw")]
    [InlineData(null, "secret-pw")]
    [InlineData("bob", null)]
    public async Task Login_BadCredentials_ReturnsNull(string? username, string? password) =>
        (await _service.LoginAsync(username, password)).Should().BeNull();

    [Fact]
    public async Task Login_DisabledUser_ReturnsNull()
    {
        _bob.IsActive = false;
        (await _service.LoginAsync("bob", "secret-pw")).Should().BeNull();
    }

    [Fact]
    public async Task Login_UserWithoutPassword_ReturnsNull()
    {
        _bob.PasswordHash = null;
        (await _service.LoginAsync("bob", "secret-pw")).Should().BeNull();
    }

    [Fact]
    public async Task AdminSnapshot_AlwaysCanRunFolderActions()
    {
        _bob.Role = UserRole.Admin;
        (await _service.LoginAsync("bob", "secret-pw"))!.CanRunFolderActions.Should().BeTrue();
    }

    [Fact]
    public async Task ChangePassword_Valid_RehashesClearsFlagAndRotatesStamp()
    {
        _bob.MustChangePassword = true;
        var stamp = _bob.SecurityStamp;

        var result = await _service.ChangePasswordAsync(5, "secret-pw", "brand-new-pw");

        result.IsSuccess.Should().BeTrue();
        result.Value!.MustChangePassword.Should().BeFalse();
        _bob.PasswordHash.Should().Be("hash:brand-new-pw");
        _bob.SecurityStamp.Should().NotBe(stamp);
        result.Value.SecurityStamp.Should().Be(_bob.SecurityStamp);
    }

    [Theory]
    [InlineData("wrong-pw", "brand-new-pw", "currentPassword")]
    [InlineData("secret-pw", "short", "newPassword")]
    [InlineData("secret-pw", "secret-pw", "newPassword")]
    [InlineData("secret-pw", null, "newPassword")]
    public async Task ChangePassword_Invalid_ReportsTheField(string? current, string? next, string field)
    {
        var result = await _service.ChangePasswordAsync(5, current, next);

        result.Status.Should().Be(ResultStatus.Invalid);
        result.Errors!.Should().ContainKey(field);
        await _users.DidNotReceive().UpdateAsync(Arg.Any<AppUser>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetActiveUser_DisabledOrMissing_ReturnsNull()
    {
        (await _service.GetActiveUserAsync(99)).Should().BeNull();
        _bob.IsActive = false;
        (await _service.GetActiveUserAsync(5)).Should().BeNull();
    }
}
