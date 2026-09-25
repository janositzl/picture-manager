namespace PictureManager.Application.Scanning;

/// <summary>
/// A scan whose job row exists (Enumerating) and whose walk waits for ScanBackgroundService. FolderId set = a
/// folder-scoped scan; otherwise RootId (one root) or neither (all active roots).
/// </summary>
public sealed record QueuedScan(int ScanJobId, int? RootId, int? FolderId, bool IsRecursive);
