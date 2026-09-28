using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using PictureManager.Api.Endpoints;
using PictureManager.Application.Discovery;
using Xunit;

namespace PictureManager.Api.Tests.Endpoints;

public class DiscoveryEndpointsTests
{
    [Fact]
    public async Task StartDiscoveryAsync_PassesIsRecursiveThrough()
    {
        var discoveryService = Substitute.For<IDiscoveryService>();
        discoveryService.QueueDiscoveryAsync(null, 5, false, Arg.Any<CancellationToken>()).Returns(42);

        var result = await DiscoveryEndpoints.StartDiscoveryAsync(
            new DiscoveryRequest(null, 5, false), discoveryService, CancellationToken.None);

        result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<DiscoveryStartedResponse>>();
        await discoveryService.Received(1).QueueDiscoveryAsync(null, 5, false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void DiscoveryRequest_MissingIsRecursiveInJson_DefaultsToTrue()
    {
        var request = JsonSerializer.Deserialize<DiscoveryRequest>("""{"folderId":5}""");

        request!.IsRecursive.Should().BeTrue();
    }
}
