using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using PictureManager.Infrastructure.Faces;
using Xunit;

namespace PictureManager.Infrastructure.Tests.Faces;

public class ScrfdDecoderTests
{
    private const int Input = 64; // strides 8/16/32 -> 8x8, 4x4, 2x2 cells, 2 anchors each

    private static (List<float[]> Scores, List<float[]> Boxes, List<float[]> Kps) EmptyOutputs()
    {
        var scores = new List<float[]>(); var boxes = new List<float[]>(); var kps = new List<float[]>();
        foreach (var stride in ScrfdDecoder.Strides)
        {
            var anchors = (Input / stride) * (Input / stride) * ScrfdDecoder.AnchorsPerCell;
            scores.Add(new float[anchors]); boxes.Add(new float[anchors * 4]); kps.Add(new float[anchors * 10]);
        }
        return (scores, boxes, kps);
    }

    [Fact]
    public void Decode_ScoreAboveThreshold_ProducesBoxAroundAnchorCenter()
    {
        var (scores, boxes, kps) = EmptyOutputs();
        // stride 8, anchor index 19 -> cell 9 -> column 1, row 1 -> center (8, 8)
        scores[0][19] = 0.9f;
        boxes[0][19 * 4 + 0] = 1; boxes[0][19 * 4 + 1] = 1; boxes[0][19 * 4 + 2] = 2; boxes[0][19 * 4 + 3] = 2;
        kps[0][19 * 10 + 0] = 0.5f; kps[0][19 * 10 + 1] = -0.5f;

        var detection = ScrfdDecoder.Decode(Input, scores, boxes, kps, threshold: 0.5f).Single();

        detection.X1.Should().Be(0); detection.Y1.Should().Be(0);
        detection.X2.Should().Be(24); detection.Y2.Should().Be(24);
        detection.Score.Should().Be(0.9f);
        detection.Landmarks[0].Should().Be((12f, 4f));
    }

    [Fact]
    public void Decode_ScoreBelowThreshold_IsDropped()
    {
        var (scores, boxes, kps) = EmptyOutputs();
        scores[1][3] = 0.4f;

        ScrfdDecoder.Decode(Input, scores, boxes, kps, threshold: 0.5f).Should().BeEmpty();
    }

    [Fact]
    public void NonMaxSuppression_KeepsHigherScoreOfOverlappingPair_AndDisjointBoxes()
    {
        var landmarks = new (float, float)[5];
        var best = new RawDetection(0, 0, 10, 10, 0.9f, landmarks);
        var overlapping = new RawDetection(1, 1, 11, 11, 0.8f, landmarks);
        var disjoint = new RawDetection(50, 50, 60, 60, 0.7f, landmarks);

        var kept = ScrfdDecoder.NonMaxSuppression(new[] { overlapping, disjoint, best }, 0.4f);

        kept.Should().Equal(best, disjoint);
    }
}
