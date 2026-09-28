using System;
using System.Collections.Generic;
using FluentAssertions;
using PictureManager.Application.Scanning;
using PictureManager.Model;
using Xunit;

namespace PictureManager.Application.Tests.Scanning;

public class ScanExcludeRulesTests
{
    private static readonly string[] AnyExtension = Array.Empty<string>();

    [Fact]
    public void IsFolderExcluded_IsCaseInsensitive()
    {
        var rules = new ScanExcludeRules(new AppSettings { ExcludedFolderNames = new List<string> { "raw", "@eaDir" } }, AnyExtension);

        rules.IsFolderExcluded("RAW").Should().BeTrue();
        rules.IsFolderExcluded("@eadir").Should().BeTrue();
        rules.IsFolderExcluded("Vacation").Should().BeFalse();
    }

    [Theory]
    [InlineData("$RECYCLE.BIN")]
    [InlineData("System Volume Information")]
    [InlineData("@eaDir")]
    [InlineData("#recycle")]
    [InlineData(".snapshot")]
    [InlineData("@EADIR")]
    public void IsFolderExcluded_OsAndNasHousekeepingFolders_AreAlwaysExcluded(string name)
    {
        new ScanExcludeRules(new AppSettings { ExcludedFolderNames = new List<string>() }, Array.Empty<string>())
            .IsFolderExcluded(name).Should().BeTrue();
    }

    [Fact]
    public void IsExtensionAllowed_ExcludedExtension_IsAlwaysRejected()
    {
        var rules = new ScanExcludeRules(new AppSettings { ExcludedExtensions = new List<string> { ".heic" } }, AnyExtension);

        rules.IsExtensionAllowed(".HEIC").Should().BeFalse();
        rules.IsExtensionAllowed(".jpg").Should().BeTrue();
    }

    [Fact]
    public void IsExtensionAllowed_WithIncludedExtensionsSet_OnlyThoseAreAllowed()
    {
        var rules = new ScanExcludeRules(new AppSettings { IncludedExtensions = new List<string> { ".jpg", ".png" } }, AnyExtension);

        rules.IsExtensionAllowed(".jpg").Should().BeTrue();
        rules.IsExtensionAllowed(".gif").Should().BeFalse();
    }

    [Fact]
    public void IsExtensionAllowed_NoIncludedExtensionsConfigured_AllowsAnythingNotExcluded()
    {
        var rules = new ScanExcludeRules(new AppSettings(), AnyExtension);

        rules.IsExtensionAllowed(".anything").Should().BeTrue();
    }

    [Fact]
    public void IsExtensionAllowed_WithSupportedExtensions_RejectsNonImageFiles()
    {
        var rules = new ScanExcludeRules(new AppSettings(), new[] { ".jpg", ".png" });

        rules.IsExtensionAllowed(".JPG").Should().BeTrue();
        rules.IsExtensionAllowed(".png").Should().BeTrue();
        rules.IsExtensionAllowed(".gitkeep").Should().BeFalse();
        rules.IsExtensionAllowed(".txt").Should().BeFalse();
    }

    [Fact]
    public void IsExtensionAllowed_IncludedExtensionsCannotWidenTheSupportedList()
    {
        var rules = new ScanExcludeRules(
            new AppSettings { IncludedExtensions = new List<string> { ".jpg", ".txt" } }, new[] { ".jpg" });

        rules.IsExtensionAllowed(".jpg").Should().BeTrue();
        rules.IsExtensionAllowed(".txt").Should().BeFalse();
    }
}
