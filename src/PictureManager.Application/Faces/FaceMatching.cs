using System;
using System.Collections.Generic;
using System.Linq;

namespace PictureManager.Application.Faces;

/// <summary>A normalized (0-1) face box. Model-independent, so boxes from different runs can be compared.</summary>
public readonly record struct FaceBox(float X, float Y, float Width, float Height);

/// <summary>Pairs the faces of a re-processed image with its previous faces by box overlap.</summary>
public static class FaceMatching
{
    /// <summary>The minimum intersection-over-union for a new face to be the same face as an old one.</summary>
    public const double MinOverlap = 0.5;

    public static double IoU(FaceBox a, FaceBox b)
    {
        var width = Math.Min(a.X + a.Width, b.X + b.Width) - Math.Max(a.X, b.X);
        var height = Math.Min(a.Y + a.Height, b.Y + b.Height) - Math.Max(a.Y, b.Y);
        if (width <= 0 || height <= 0)
            return 0;

        var intersection = (double)width * height;
        var union = (double)a.Width * a.Height + (double)b.Width * b.Height - intersection;
        return union <= 0 ? 0 : intersection / union;
    }

    /// <summary>
    /// Greedy one-to-one matching, highest IoU pair first (ties: lower current index, then lower previous index).
    /// Returns current index -> previous index for the pairs with IoU ≥ minOverlap.
    /// </summary>
    public static IReadOnlyDictionary<int, int> MatchByOverlap(
        IReadOnlyList<FaceBox> previous, IReadOnlyList<FaceBox> current, double minOverlap = MinOverlap)
    {
        var pairs = new List<(int Current, int Previous, double IoU)>();
        for (var c = 0; c < current.Count; c++)
            for (var p = 0; p < previous.Count; p++)
            {
                var iou = IoU(current[c], previous[p]);
                if (iou >= minOverlap)
                    pairs.Add((c, p, iou));
            }

        var matches = new Dictionary<int, int>();
        var usedPrevious = new HashSet<int>();
        foreach (var pair in pairs.OrderByDescending(x => x.IoU).ThenBy(x => x.Current).ThenBy(x => x.Previous))
        {
            if (matches.ContainsKey(pair.Current) || usedPrevious.Contains(pair.Previous))
                continue;
            matches[pair.Current] = pair.Previous;
            usedPrevious.Add(pair.Previous);
        }

        return matches;
    }
}
