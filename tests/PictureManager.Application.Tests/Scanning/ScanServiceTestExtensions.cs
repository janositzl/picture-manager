using System.Threading.Tasks;
using PictureManager.Application.Scanning;

namespace PictureManager.Application.Tests.Scanning;

internal static class ScanServiceTestExtensions
{
    /// <summary>Queue + run in one call: what ScanBackgroundService does, minus the queue hop.</summary>
    public static async Task<int> ScanNowAsync(this ScanService scanService, int? rootId, bool isRecursive)
    {
        var scanJobId = await scanService.QueueScanAsync(rootId, folderId: null, isRecursive);
        await scanService.RunScanAsync(new QueuedScan(scanJobId, rootId, null, isRecursive));
        return scanJobId;
    }

    /// <summary>Same as ScanNowAsync, for a folder-scoped scan.</summary>
    public static async Task<int> ScanFolderNowAsync(this ScanService scanService, int folderId, bool isRecursive)
    {
        var scanJobId = await scanService.QueueScanAsync(rootId: null, folderId, isRecursive);
        await scanService.RunScanAsync(new QueuedScan(scanJobId, null, folderId, isRecursive));
        return scanJobId;
    }
}
