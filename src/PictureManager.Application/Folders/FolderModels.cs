using System.Collections.Generic;

namespace PictureManager.Application.Folders;

/// <summary>
/// Tree node. ImageCount = images directly in the folder that are not individually missing: for a present
/// folder that is what its grid shows; for a missing folder (IsMissing) it is what comes back if the folder
/// reappears, and what "remove from collection" would purge. IsScanned = a scan of this folder has completed.
/// </summary>
public sealed record FolderNode(int Id, string Name, bool HasChildren, int ImageCount, bool IsMissing, bool IsExcluded, bool IsScanned = false);

public sealed record BreadcrumbItem(int Id, string Name);

/// <summary>Breadcrumb runs from the root's top folder down to (and including) this folder.</summary>
public sealed record FolderDetail(
    int Id,
    string Name,
    int RootId,
    string RootName,
    string RelativePath,
    int ImageCount,
    bool IsMissing,
    IReadOnlyList<BreadcrumbItem> Breadcrumb);

public sealed record RemovedFolder(int Id, string Name, string RootName, string RelativePath);
