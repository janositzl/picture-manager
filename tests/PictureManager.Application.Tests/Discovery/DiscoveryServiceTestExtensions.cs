using System.Threading.Tasks;
using PictureManager.Application.Discovery;

namespace PictureManager.Application.Tests.Discovery;

internal static class DiscoveryServiceTestExtensions
{
    /// <summary>Queue + run in one call: what DiscoveryBackgroundService does, minus the queue hop.</summary>
    public static async Task<int> DiscoverNowAsync(this DiscoveryService discoveryService, int? rootId, bool isRecursive = true)
    {
        var discoveryJobId = await discoveryService.QueueDiscoveryAsync(rootId, folderId: null, isRecursive);
        await discoveryService.RunDiscoveryAsync(new QueuedDiscovery(discoveryJobId, rootId, null, isRecursive));
        return discoveryJobId;
    }

    /// <summary>Same as DiscoverNowAsync, for a folder-scoped (refresh) discovery.</summary>
    public static async Task<int> DiscoverFolderNowAsync(this DiscoveryService discoveryService, int folderId, bool isRecursive = true)
    {
        var discoveryJobId = await discoveryService.QueueDiscoveryAsync(rootId: null, folderId, isRecursive);
        await discoveryService.RunDiscoveryAsync(new QueuedDiscovery(discoveryJobId, null, folderId, isRecursive));
        return discoveryJobId;
    }
}
