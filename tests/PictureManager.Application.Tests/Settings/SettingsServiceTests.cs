using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using PictureManager.Application.Common;
using PictureManager.Application.Repositories;
using PictureManager.Application.Settings;
using PictureManager.Model;
using Xunit;

namespace PictureManager.Application.Tests.Settings;

public class SettingsServiceTests
{
    private readonly IAppSettingsRepository _repository = Substitute.For<IAppSettingsRepository>();
    private readonly AppSettings _current = new()
    {
        Id = 1,
        ExcludedFolderNames = new List<string> { "raw" },
        ExcludedExtensions = new List<string> { ".heic" },
        IncludedExtensions = null
    };

    public SettingsServiceTests()
    {
        _repository.GetAsync(Arg.Any<CancellationToken>()).Returns(_current);
    }

    private SettingsService CreateService() => new(_repository);

    private static SettingsInput Input(string?[]? folders, string?[]? excluded, string?[]? included = null) =>
        new(folders, excluded, included);

    [Fact]
    public async Task UpdateAsync_SameRules_DoesNotFlagPrune()
    {
        var result = await CreateService().UpdateAsync(Input(new[] { "RAW" }, new[] { "heic" }));

        result.Value!.PruneOnNextScan.Should().BeFalse();
        await _repository.Received(1).UpdateAsync(_current, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_RemovingAnExclusion_DoesNotFlagPrune()
    {
        (await CreateService().UpdateAsync(Input(new string[0], new string[0]))).Value!.PruneOnNextScan.Should().BeFalse();
    }

    [Theory]
    [InlineData(new[] { "raw", "backup" }, new[] { ".heic" }, null)]
    [InlineData(new[] { "raw" }, new[] { ".heic", ".png" }, null)]
    [InlineData(new[] { "raw" }, new[] { ".heic" }, new[] { ".jpg" })]
    public async Task UpdateAsync_NewlyExcludingSomething_FlagsPrune(string[] folders, string[] excluded, string[]? included)
    {
        (await CreateService().UpdateAsync(Input(folders, excluded, included))).Value!.PruneOnNextScan.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateAsync_NarrowingAnExistingIncludeList_FlagsPrune()
    {
        _current.IncludedExtensions = new List<string> { ".jpg", ".png" };

        (await CreateService().UpdateAsync(Input(new[] { "raw" }, new[] { ".heic" }, new[] { ".jpg" })))
            .Value!.PruneOnNextScan.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateAsync_EmptyIncludeList_MeansAllowAll()
    {
        var result = await CreateService().UpdateAsync(Input(new[] { "raw" }, new[] { ".heic" }, new string[0]));

        result.Value!.IncludedExtensions.Should().BeNull();
        _current.IncludedExtensions.Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_SavesNormalizedValues()
    {
        var result = await CreateService().UpdateAsync(Input(new[] { " raw ", "Backup" }, new[] { "HEIC" }));

        result.Value!.ExcludedFolderNames.Should().Equal("raw", "Backup");
        _current.ExcludedExtensions.Should().Equal(".heic");
    }

    [Theory]
    [InlineData("excludedFolderNames")]
    [InlineData("excludedExtensions")]
    [InlineData("includedExtensions")]
    public async Task UpdateAsync_InvalidValue_ReturnsInvalidForThatField_AndSavesNothing(string field)
    {
        var input = field switch
        {
            "excludedFolderNames" => Input(new[] { "a/b" }, new string[0]),
            "excludedExtensions" => Input(new string[0], new[] { " " }),
            _ => Input(new string[0], new string[0], new[] { "." })
        };

        var result = await CreateService().UpdateAsync(input);

        result.Errors!.Keys.Should().Contain(field);
        await _repository.DidNotReceive().UpdateAsync(Arg.Any<AppSettings>(), Arg.Any<CancellationToken>());
    }
}
