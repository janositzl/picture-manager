namespace PictureManager.Application.Scanning;

/// <summary>A scan whose job row exists (Enumerating) and whose walk waits for ScanBackgroundService.</summary>
public sealed record QueuedScan(int ScanJobId, int? RootId, bool IsRecursive);
