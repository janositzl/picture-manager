using FluentAssertions;
using PictureManager.Infrastructure.Users;
using Xunit;

namespace PictureManager.Infrastructure.Tests.Users;

public class IdentityPasswordHasherTests
{
    private readonly IdentityPasswordHasher _hasher = new();

    [Fact]
    public void Verify_CorrectPassword_ReturnsTrue()
    {
        var hash = _hasher.Hash("correct horse");
        hash.Should().NotContain("correct horse");
        _hasher.Verify(hash, "correct horse").Should().BeTrue();
    }

    [Fact]
    public void Verify_WrongPassword_ReturnsFalse() =>
        _hasher.Verify(_hasher.Hash("correct horse"), "battery staple").Should().BeFalse();

    [Fact]
    public void Verify_GarbageHash_ReturnsFalse() =>
        _hasher.Verify("not-a-hash", "anything").Should().BeFalse();
}
