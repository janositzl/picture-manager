namespace PictureManager.Application.Discovery;

/// <summary>
/// A discovery whose job row exists (Enumerating) and whose walk waits for DiscoveryBackgroundService. FolderId
/// set = a folder-scoped discovery (refresh); otherwise RootId (a whole root).
/// </summary>
public sealed record QueuedDiscovery(int DiscoveryJobId, int? RootId, int? FolderId);
