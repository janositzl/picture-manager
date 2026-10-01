using System.Threading;
using FluentAssertions;
using PictureManager.Application.Common;
using Xunit;

namespace PictureManager.Application.Tests.Common;

public class JobCancellationRegistryTests
{
    [Fact]
    public void Cancel_RegisteredJob_CancelsItsToken()
    {
        var registry = new JobCancellationRegistry();
        var token = registry.Register(5);

        registry.Cancel(5).Should().BeTrue();

        token.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public void Register_SameJobTwice_ReturnsTheSameToken()
    {
        var registry = new JobCancellationRegistry();
        var first = registry.Register(5);
        registry.Cancel(5);

        registry.Register(5).IsCancellationRequested.Should().BeTrue();
        first.Should().Be(registry.Register(5));
    }

    [Fact]
    public void Cancel_UnknownJob_ReturnsFalse()
    {
        new JobCancellationRegistry().Cancel(42).Should().BeFalse();
    }

    [Fact]
    public void Cancel_AfterRelease_ReturnsFalse()
    {
        var registry = new JobCancellationRegistry();
        registry.Register(5);
        registry.Release(5);

        registry.Cancel(5).Should().BeFalse();
    }

    [Fact]
    public void Release_UnknownJob_DoesNotThrow()
    {
        var act = () => new JobCancellationRegistry().Release(1);
        act.Should().NotThrow();
    }
}
