using System.Collections.Generic;
using FluentAssertions;
using PictureManager.Application.Scanning;
using PictureManager.Model;
using Xunit;

namespace PictureManager.Application.Tests.Scanning;

public class ScanExcludeRulesTests
{
    [Fact]
    public void IsFolderExcluded_IsCaseInsensitive()
    {
        var rules = new ScanExcludeRules(new AppSettings { ExcludedFolderNames = new List<string> { "raw", "@eaDir" } });

        rules.IsFolderExcluded("RAW").Should().BeTrue();
        rules.IsFolderExcluded("@eadir").Should().BeTrue();
        rules.IsFolderExcluded("Vacation").Should().BeFalse();
    }

    [Fact]
    public void IsExtensionAllowed_ExcludedExtension_IsAlwaysRejected()
    {
        var rules = new ScanExcludeRules(new AppSettings { ExcludedExtensions = new List<string> { ".heic" } });

        rules.IsExtensionAllowed(".HEIC").Should().BeFalse();
        rules.IsExtensionAllowed(".jpg").Should().BeTrue();
    }

    [Fact]
    public void IsExtensionAllowed_WithIncludedExtensionsSet_OnlyThoseAreAllowed()
    {
        var rules = new ScanExcludeRules(new AppSettings { IncludedExtensions = new List<string> { ".jpg", ".png" } });

        rules.IsExtensionAllowed(".jpg").Should().BeTrue();
        rules.IsExtensionAllowed(".gif").Should().BeFalse();
    }

    [Fact]
    public void IsExtensionAllowed_NoIncludedExtensionsConfigured_AllowsAnythingNotExcluded()
    {
        var rules = new ScanExcludeRules(new AppSettings());

        rules.IsExtensionAllowed(".anything").Should().BeTrue();
    }
}
