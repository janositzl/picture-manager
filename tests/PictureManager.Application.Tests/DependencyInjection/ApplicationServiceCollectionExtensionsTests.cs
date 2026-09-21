using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PictureManager.Application.Common;
using PictureManager.Application.DependencyInjection;
using Xunit;

namespace PictureManager.Application.Tests.DependencyInjection;

public class ApplicationServiceCollectionExtensionsTests
{
    [Fact]
    public void AddApplication_RegistersIClockAsSystemClock()
    {
        var services = new ServiceCollection();

        services.AddApplication();
        var provider = services.BuildServiceProvider();

        var clock = provider.GetService<IClock>();

        clock.Should().NotBeNull();
        clock.Should().BeOfType<SystemClock>();
    }
}
