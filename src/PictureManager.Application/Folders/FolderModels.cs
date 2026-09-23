using System.Collections.Generic;

namespace PictureManager.Application.Folders;

/// <summary>Tree node. ImageCount = visible images directly in the folder (what its grid shows).</summary>
public sealed record FolderNode(int Id, string Name, bool HasChildren, int ImageCount);

public sealed record BreadcrumbItem(int Id, string Name);

/// <summary>Breadcrumb runs from the root's top folder down to (and including) this folder.</summary>
public sealed record FolderDetail(
    int Id,
    string Name,
    int RootId,
    string RootName,
    string RelativePath,
    int ImageCount,
    IReadOnlyList<BreadcrumbItem> Breadcrumb);

public sealed record RemovedFolder(int Id, string Name, string RootName, string RelativePath);
