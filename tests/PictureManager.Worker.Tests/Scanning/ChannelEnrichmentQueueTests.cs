using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using PictureManager.Worker.Scanning;
using Xunit;

namespace PictureManager.Worker.Tests.Scanning;

public class ChannelEnrichmentQueueTests
{
    [Fact]
    public async Task Enqueue_ThenReadAllAsync_YieldsTheItem()
    {
        var queue = new ChannelEnrichmentQueue();
        queue.Enqueue(scanJobId: 1, imageId: 42);

        using var cts = new CancellationTokenSource();
        await foreach (var item in queue.ReadAllAsync(cts.Token))
        {
            item.Should().Be((1, 42));
            break;
        }
    }
}
