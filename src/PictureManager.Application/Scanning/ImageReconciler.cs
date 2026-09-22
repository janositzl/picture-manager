using System;
using PictureManager.Model;

namespace PictureManager.Application.Scanning;

public enum ReconcileAction
{
    New,
    Unchanged,
    Modified
}

public static class ImageReconciler
{
    public static ReconcileAction Decide(Image? existing, long observedSize, DateTime observedModifiedUtc)
    {
        if (existing is null)
            return ReconcileAction.New;

        var unchanged = existing.FileSize == observedSize
            && existing.FileModified == observedModifiedUtc
            && existing.MissingSinceUtc is null;

        return unchanged ? ReconcileAction.Unchanged : ReconcileAction.Modified;
    }
}
