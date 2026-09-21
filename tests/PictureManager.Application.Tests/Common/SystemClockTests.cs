using System;
using FluentAssertions;
using PictureManager.Application.Common;
using Xunit;

namespace PictureManager.Application.Tests.Common;

public class SystemClockTests
{
    [Fact]
    public void UtcNow_ReturnsCurrentUtcTime()
    {
        var clock = new SystemClock();
        var before = DateTime.UtcNow;

        var result = clock.UtcNow;

        var after = DateTime.UtcNow;
        result.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
        result.Kind.Should().Be(DateTimeKind.Utc);
    }
}
