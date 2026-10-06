using System;
using System.Linq;
using FluentAssertions;
using PictureManager.Application.Faces;
using Xunit;

namespace PictureManager.Application.Tests.Faces;

public class FaceCoverageRollupTests
{
    [Fact]
    public void Roll_SumsEachFolderIntoEveryAncestor()
    {
        // top(1) -> trips(2) -> madeira(3); top -> trips2(4)
        var result = FaceCoverageRollup.Roll(new[]
        {
            new FolderFaceCounts(1, null, Total: 1, Done: 1, Failed: 0, Stale: 0),
            new FolderFaceCounts(2, 1, Total: 4, Done: 1, Failed: 1, Stale: 1),
            new FolderFaceCounts(3, 2, Total: 3, Done: 3, Failed: 0, Stale: 0),
            new FolderFaceCounts(4, 1, Total: 2, Done: 0, Failed: 0, Stale: 2),
        });

        result.Should().BeEquivalentTo(new[]
        {
            new FolderFaceCoverage(1, Total: 10, Done: 5, Failed: 1, Stale: 3),
            new FolderFaceCoverage(2, Total: 7, Done: 4, Failed: 1, Stale: 1),
            new FolderFaceCoverage(3, Total: 3, Done: 3, Failed: 0, Stale: 0),
            new FolderFaceCoverage(4, Total: 2, Done: 0, Failed: 0, Stale: 2),
        });
    }

    [Fact]
    public void Roll_LeavesOutFoldersWithNoPhotosInTheirSubtree_ButKeepsEmptyParentsOfPhotos()
    {
        var result = FaceCoverageRollup.Roll(new[]
        {
            new FolderFaceCounts(1, null, 0, 0, 0, 0),
            new FolderFaceCounts(2, 1, 0, 0, 0, 0),   // empty parent of a folder with photos: kept
            new FolderFaceCounts(3, 2, 5, 5, 0, 0),
            new FolderFaceCounts(4, 1, 0, 0, 0, 0),   // empty leaf: left out
        });

        result.Select(r => r.FolderId).Should().Equal(1, 2, 3);
        result.Single(r => r.FolderId == 1).Should().Be(new FolderFaceCoverage(1, 5, 5, 0, 0));
    }

    [Fact]
    public void Roll_IgnoresAParentThatIsNotInTheList()
    {
        // A folder whose parent isn't visible (not returned) still reports its own counts.
        var result = FaceCoverageRollup.Roll(new[] { new FolderFaceCounts(7, 99, 2, 1, 0, 1) });

        result.Should().Equal(new FolderFaceCoverage(7, 2, 1, 0, 1));
    }

    [Fact]
    public void Roll_Empty_ReturnsEmpty() =>
        FaceCoverageRollup.Roll(Array.Empty<FolderFaceCounts>()).Should().BeEmpty();
}
