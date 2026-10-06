using System.Collections.Generic;
using System.Linq;

namespace PictureManager.Application.Faces;

/// <summary>One visible folder's own images (not its subfolders'), as the current face model sees them.</summary>
public sealed record FolderFaceCounts(int FolderId, int? ParentId, int Total, int Done, int Failed, int Stale);

/// <summary>
/// Face coverage of a folder and everything beneath it. Total = visible Indexed images, Done = Completed for the
/// current model and content, Failed = PermanentlyFailed likewise, Stale = processed (Completed or
/// PermanentlyFailed) but by another model or for older content. Total - Done - Failed - Stale = never processed
/// (or retryable Failed). A face job on the folder would process everything but Done and Failed.
/// </summary>
public sealed record FolderFaceCoverage(int FolderId, int Total, int Done, int Failed, int Stale);

public static class FaceCoverageRollup
{
    /// <summary>
    /// Adds each folder's own counts to every ancestor (by ParentId). Folders with no photos anywhere beneath
    /// them are left out. Ordered by FolderId.
    /// </summary>
    public static IReadOnlyList<FolderFaceCoverage> Roll(IReadOnlyList<FolderFaceCounts> folders)
    {
        var parents = folders.ToDictionary(f => f.FolderId, f => f.ParentId);
        var totals = folders.ToDictionary(f => f.FolderId, f => (f.Total, f.Done, f.Failed, f.Stale));

        foreach (var folder in folders.Where(f => f.Total > 0))
        {
            // The visited set only guards against a corrupt ParentId cycle; a real tree never revisits.
            var visited = new HashSet<int> { folder.FolderId };
            var parentId = folder.ParentId;
            while (parentId is int id && parents.TryGetValue(id, out var next) && visited.Add(id))
            {
                var t = totals[id];
                totals[id] = (t.Total + folder.Total, t.Done + folder.Done, t.Failed + folder.Failed, t.Stale + folder.Stale);
                parentId = next;
            }
        }

        return totals
            .Where(p => p.Value.Total > 0)
            .OrderBy(p => p.Key)
            .Select(p => new FolderFaceCoverage(p.Key, p.Value.Total, p.Value.Done, p.Value.Failed, p.Value.Stale))
            .ToList();
    }
}
