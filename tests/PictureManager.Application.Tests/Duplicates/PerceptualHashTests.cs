using FluentAssertions;
using PictureManager.Application.Duplicates;
using Xunit;

namespace PictureManager.Application.Tests.Duplicates;

public class PerceptualHashTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("xyz")]
    [InlineData("0123")]
    public void Parse_Invalid_ReturnsNull(string? s) => PerceptualHash.Parse(s).Should().BeNull();

    [Fact]
    public void Parse_Hex_RoundTrips() => PerceptualHash.Parse("00000000000000ff").Should().Be(0xFFUL);

    [Fact]
    public void Distance_CountsDifferingBits() => PerceptualHash.Distance(0b1011, 0b0001).Should().Be(2);

    [Theory]
    [InlineData(0UL)]
    [InlineData(ulong.MaxValue)]
    [InlineData(0b11UL)]
    public void IsDegenerate_NearUniform(ulong h) => PerceptualHash.IsDegenerate(h).Should().BeTrue();
}
