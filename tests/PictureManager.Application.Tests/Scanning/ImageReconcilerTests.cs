using System;
using FluentAssertions;
using PictureManager.Application.Scanning;
using PictureManager.Model;
using Xunit;

namespace PictureManager.Application.Tests.Scanning;

public class ImageReconcilerTests
{
    [Fact]
    public void Decide_NoExistingImage_ReturnsNew()
    {
        ImageReconciler.Decide(null, 100, DateTime.UtcNow).Should().Be(ReconcileAction.New);
    }

    [Fact]
    public void Decide_SameSizeAndModifiedTime_ReturnsUnchanged()
    {
        var modified = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var existing = new Image { FileSize = 100, FileModified = modified, MissingSinceUtc = null };

        ImageReconciler.Decide(existing, 100, modified).Should().Be(ReconcileAction.Unchanged);
    }

    [Fact]
    public void Decide_DifferentSize_ReturnsModified()
    {
        var modified = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var existing = new Image { FileSize = 100, FileModified = modified };

        ImageReconciler.Decide(existing, 200, modified).Should().Be(ReconcileAction.Modified);
    }

    [Fact]
    public void Decide_DifferentModifiedTime_ReturnsModified()
    {
        var existing = new Image { FileSize = 100, FileModified = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) };

        ImageReconciler.Decide(existing, 100, new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)).Should().Be(ReconcileAction.Modified);
    }

    [Fact]
    public void Decide_PreviouslyMissingFileReappears_ReturnsModified()
    {
        var modified = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var existing = new Image { FileSize = 100, FileModified = modified, MissingSinceUtc = modified.AddDays(1) };

        ImageReconciler.Decide(existing, 100, modified).Should().Be(ReconcileAction.Modified);
    }
}
