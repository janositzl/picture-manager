using System.Threading.Tasks;
using PictureManager.Application.Scanning;

namespace PictureManager.Application.Tests.Scanning;

internal static class ScanServiceTestExtensions
{
    /// <summary>Queue + run in one call: what ScanBackgroundService does, minus the queue hop.</summary>
    public static async Task<int> ScanNowAsync(this ScanService scanService, int? rootId, bool isRecursive)
    {
        var scanJobId = await scanService.QueueScanAsync(rootId, isRecursive);
        await scanService.RunScanAsync(new QueuedScan(scanJobId, rootId, isRecursive));
        return scanJobId;
    }
}
