using System;
using System.Collections.Generic;
using System.Linq;

namespace PictureManager.Infrastructure.Faces;

/// <summary>A face candidate in detector-input pixels: box corners, score and 5 landmarks.</summary>
public sealed record RawDetection(float X1, float Y1, float X2, float Y2, float Score, (float X, float Y)[] Landmarks)
{
    public RawDetection Scale(float factor) => new(
        X1 * factor, Y1 * factor, X2 * factor, Y2 * factor, Score,
        Landmarks.Select(p => (p.X * factor, p.Y * factor)).ToArray());
}

/// <summary>
/// Decodes InsightFace SCRFD outputs (det_10g): per stride 8/16/32, 2 anchors per grid cell, distances to the box
/// edges and landmark offsets in units of the stride, relative to the cell's top-left corner.
/// </summary>
public static class ScrfdDecoder
{
    public static readonly int[] Strides = { 8, 16, 32 };
    public const int AnchorsPerCell = 2;

    public static List<RawDetection> Decode(
        int inputSize, IReadOnlyList<float[]> scores, IReadOnlyList<float[]> boxes, IReadOnlyList<float[]> landmarks, float threshold)
    {
        var detections = new List<RawDetection>();
        for (var s = 0; s < Strides.Length; s++)
        {
            var stride = Strides[s];
            var cells = inputSize / stride;
            var score = scores[s]; var box = boxes[s]; var kps = landmarks[s];

            for (var i = 0; i < score.Length; i++)
            {
                if (score[i] < threshold)
                    continue;

                var cell = i / AnchorsPerCell;
                float cx = cell % cells * stride, cy = cell / cells * stride;
                var points = new (float X, float Y)[5];
                for (var k = 0; k < 5; k++)
                    points[k] = (cx + kps[i * 10 + 2 * k] * stride, cy + kps[i * 10 + 2 * k + 1] * stride);

                detections.Add(new RawDetection(
                    cx - box[i * 4] * stride, cy - box[i * 4 + 1] * stride,
                    cx + box[i * 4 + 2] * stride, cy + box[i * 4 + 3] * stride,
                    score[i], points));
            }
        }
        return detections;
    }

    public static List<RawDetection> NonMaxSuppression(IEnumerable<RawDetection> detections, float iouThreshold)
    {
        var kept = new List<RawDetection>();
        foreach (var candidate in detections.OrderByDescending(d => d.Score))
        {
            if (kept.All(k => IoU(k, candidate) <= iouThreshold))
                kept.Add(candidate);
        }
        return kept;
    }

    private static float IoU(RawDetection a, RawDetection b)
    {
        var w = Math.Max(0, Math.Min(a.X2, b.X2) - Math.Max(a.X1, b.X1));
        var h = Math.Max(0, Math.Min(a.Y2, b.Y2) - Math.Max(a.Y1, b.Y1));
        var intersection = w * h;
        var union = (a.X2 - a.X1) * (a.Y2 - a.Y1) + (b.X2 - b.X1) * (b.Y2 - b.Y1) - intersection;
        return union <= 0 ? 0 : intersection / union;
    }
}
