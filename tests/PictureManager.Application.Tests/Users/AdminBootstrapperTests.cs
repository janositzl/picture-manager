using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PictureManager.Application.Repositories;
using PictureManager.Application.Users;
using PictureManager.Model;
using Xunit;

namespace PictureManager.Application.Tests.Users;

public class AdminBootstrapperTests
{
    private readonly IAppUserRepository _users = Substitute.For<IAppUserRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly AppUser _admin = new() { Id = 1, Username = "admin", NormalizedUsername = "admin", Role = UserRole.Admin };

    private AdminBootstrapper Create(string? password) =>
        new(_users, _hasher, new AuthOptions { InitialAdminPassword = password }, NullLogger<AdminBootstrapper>.Instance);

    public AdminBootstrapperTests()
    {
        _users.GetFirstActiveAdminAsync(Arg.Any<CancellationToken>()).Returns(_admin);
        _hasher.Hash(Arg.Any<string>()).Returns(c => "hashed:" + c.Arg<string>());
    }

    [Fact]
    public async Task NoAdminHasPassword_AndOneIsConfigured_SetsItAndForcesAChange()
    {
        var stamp = _admin.SecurityStamp;

        (await Create("initial-pass").EnsureInitialAdminPasswordAsync()).Should().BeTrue();

        _admin.PasswordHash.Should().Be("hashed:initial-pass");
        _admin.MustChangePassword.Should().BeTrue();
        _admin.SecurityStamp.Should().NotBe(stamp);
        await _users.Received(1).UpdateAsync(_admin, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnAdminAlreadyHasAPassword_ChangesNothing()
    {
        _users.AnyActiveAdminWithPasswordAsync(Arg.Any<CancellationToken>()).Returns(true);

        (await Create("initial-pass").EnsureInitialAdminPasswordAsync()).Should().BeFalse();

        await _users.DidNotReceive().UpdateAsync(Arg.Any<AppUser>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("short")]
    public async Task MissingOrTooShortPassword_ChangesNothing(string? password)
    {
        (await Create(password).EnsureInitialAdminPasswordAsync()).Should().BeFalse();

        _admin.PasswordHash.Should().BeNull();
        await _users.DidNotReceive().UpdateAsync(Arg.Any<AppUser>(), Arg.Any<CancellationToken>());
    }
}
