using System;

namespace PictureManager.Application.Thumbnails;

public static class ThumbnailResizeCalculator
{
    public static (int Width, int Height) CalculateTargetDimensions(int originalWidth, int originalHeight, int longestEdgeTarget)
    {
        var longestEdge = Math.Max(originalWidth, originalHeight);
        if (longestEdge <= longestEdgeTarget)
            return (originalWidth, originalHeight);

        var scale = (double)longestEdgeTarget / longestEdge;
        var width = (int)Math.Round(originalWidth * scale);
        var height = (int)Math.Round(originalHeight * scale);
        return (Math.Max(width, 1), Math.Max(height, 1));
    }

    public static int NormalizeOrientation(int? orientation)
    {
        return orientation is >= 1 and <= 8 ? orientation.Value : 1;
    }
}
