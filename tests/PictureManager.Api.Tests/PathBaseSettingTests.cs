using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace PictureManager.Api.Tests;

public class PathBaseSettingTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("/")]
    public void Parse_UnsetOrRoot_IsEmpty(string? value)
    {
        PathBaseSetting.Parse(value).Should().Be(PathString.Empty);
    }

    [Theory]
    [InlineData("pm")]
    [InlineData("/pm")]
    [InlineData("/pm/")]
    [InlineData(" /pm/ ")]
    public void Parse_NormalizesSlashes(string value)
    {
        PathBaseSetting.Parse(value).Should().Be(new PathString("/pm"));
    }

    [Fact]
    public void Parse_KeepsNestedSegments()
    {
        PathBaseSetting.Parse("/apps/pm/").Should().Be(new PathString("/apps/pm"));
    }
}
