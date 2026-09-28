namespace PictureManager.Application.Discovery;

/// <summary>
/// A discovery whose job row exists (Enumerating) and whose walk waits for DiscoveryBackgroundService. FolderId
/// set = a folder-scoped discovery (refresh); otherwise RootId (a whole root). IsRecursive = false diffs only the
/// target's direct children, without descending into them.
/// </summary>
public sealed record QueuedDiscovery(int DiscoveryJobId, int? RootId, int? FolderId, bool IsRecursive = true);
