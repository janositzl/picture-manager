# Phase 4 — Thumbnails & Image Serving Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Serve browser-displayable, EXIF-orientation-corrected WebP derivatives of indexed images — a ~300px
grid thumbnail and a ~1800px viewer preview — generated lazily on first request and cached content-addressed
on disk, with a per-deployment toggle to skip preview generation and fall back to serving the original file.

**Architecture:** A new `IThumbnailService` (Application) is implemented by `SkiaSharpThumbnailService`
(Infrastructure) — the same seam already used for `IContentHasher`/`IExifReader`. It takes a source path,
content hash, and orientation, and returns the path to a cached WebP derivative, generating it on cache miss.
Two new minimal-API endpoints in `PictureManager.Api` resolve the requested `Image` row, resolve its physical
path via the existing `ImagePathResolver`, and either serve the cached/generated derivative or — for
`/preview` only, when disabled by config — the original file directly. No new database tables or columns:
the cache key is entirely derived from `Image.ContentHash` (already populated by phase 3) plus the requested
size.

**Tech Stack:** `SkiaSharp` (new this phase — MIT-licensed, per the spec's phase-1 imaging-library decision) for
decode/orient/resize/WebP-encode. No new EF Core/database work.

**Spec:** [`docs/superpowers/specs/2026-09-21-picturemanager-v1-design.md`](../specs/2026-09-21-picturemanager-v1-design.md)
(phase 4 decisions 7-11), and the source brief's "Thumbnails — lazy, not eager" section in
[`Documents/PictureManager-brief.md`](../../../Documents/PictureManager-brief.md).

## Global Constraints

- Target framework: `net10.0` everywhere on the backend (unchanged).
- **FluentAssertions pinned to `[7.0.0,8.0.0)`**, **NSubstitute** for mocking (not Moq) — unchanged.
- **New package this phase** (installed via `dotnet add package SkiaSharp`, no pinned version — same
  unpinned style as phases 2/3; record whatever version resolves in the commit), added to
  `PictureManager.Infrastructure.csproj` only.
- **No database changes this phase.** The cache key is `Image.ContentHash` (phase 3) + a `DerivativeSize`;
  nothing new needs persisting. No migration, no `ScanJob`/`AppSettings` changes.
- **Filesystem-touching library integration lives in `PictureManager.Infrastructure`**, behind an
  Application-defined interface (`IThumbnailService`) — same pattern as `IContentHasher`/`IExifReader`. Pure
  logic with no I/O (shard-path derivation, resize-dimension math, content-type resolution) stays in
  `PictureManager.Application.Thumbnails` as static/pure functions, unit-tested without touching disk or
  SkiaSharp — same split already used for `PathNormalizer`/`ImagePathResolver` vs. the scanning services.
- **Cache root path is static config, not DB-backed**: `ThumbnailCache:RootPath` in appsettings, bound to a
  new `ThumbnailCacheOptions` POCO in `Program.cs` exactly the way `DevImageRootOptions` is bound today (no
  `IOptions<T>` wrapper anywhere in this codebase — don't introduce one here either). `.gitignore` already
  excludes `thumbnail-cache/` at the repo root (pre-existing entry) — the local dev value is
  `../../thumbnail-cache`, mirroring `DevImageRoot:MountPath`'s `../../dev-data/images`.
- **Never upscale.** If the source image's longest edge is already ≤ the target size, the derivative keeps
  the source's original dimensions (still re-encoded as WebP with orientation correction) rather than being
  stretched larger.
- **WebP quality: 82** for both derivatives (thumbnail and preview) — a fixed constant, not configurable in
  v1.
- **Orientation source of truth is the already-stored `Image.Orientation` column** (populated by phase 3's
  `ImageEnrichmentService`) — `IThumbnailService` never re-reads EXIF itself. `null` or any value outside
  1-8 is treated as "no transform" (orientation 1), not an error.
- **Error boundary**: only an actual decode failure (source file isn't a valid/supported image) makes
  `IThumbnailService` return `null`. A filesystem error while writing the cache (permission denied, disk
  full, cache root missing and uncreatable) is an operational fault and must propagate as an exception —
  never silently mapped to the same `null`/404 outcome as "this isn't a photo".
- **Sharded cache layout**, exactly as the brief's example: `{RootPath}/{hash[0..2]}/{hash[2..4]}/{hash}-{size}.webp`,
  where `{size}` is the derivative's target pixel value (`300` or `1800`). Shard directories are created on
  demand — never assumed to pre-exist.
- **Preview toggle**: `ThumbnailCache:PreviewEnabled` (bool, default `true`). Thumbnails are never affected.
  When `false`, `GET /api/images/{id}/preview` skips `IThumbnailService` entirely and serves the original
  file with `Cache-Control: private, max-age=300` (no `immutable`) and a content type resolved by
  `ImageContentTypeResolver`. When `true` (default), it behaves exactly like `/thumbnail`:
  `Cache-Control: private, max-age=31536000, immutable`.
- **404 rules** for both endpoints: unknown image id, `Image.MissingSinceUtc` set, or (enabled-derivative
  path only) `IThumbnailService` returning `null` all produce `Results.NotFound()`. No server-side
  placeholder image.
- **SkiaSharp API note**: the exact resize/encode method names below are believed correct for the SkiaSharp
  version that resolves from an unpinned `dotnet add package`, but — same caveat phase 3 recorded for
  MetadataExtractor — adjust call sites to match whatever the installed version's actual API surface is if it
  differs; the intent (decode → orient → resize longest-edge → encode WebP quality 82) is what must be
  preserved, not the literal method names.

## Review Focus

- **Corrupt/undecodable source file leaves no stray temp file in the cache directory.** A request against a
  file that fails to decode must return `null` cleanly, with nothing left behind in the shard directory —
  otherwise every bad file leaks a temp file on every request. (Task 2)
- **First-ever request against a fresh cache root creates the shard subdirectories on demand.** Nothing may
  assume `{RootPath}/{hash[0..2]}/{hash[2..4]}/` already exists. (Task 2)
- **Null or out-of-range `Image.Orientation` (not 1-8) never throws** — it must fall back to "no transform",
  since not every source format populates this reliably and phase 3 stores whatever EXIF happened to report,
  unvalidated. (Task 1 dimension/orientation helpers, Task 2 service)
- **File-extension content-type resolution is case-insensitive and safe on unknown extensions** — `.JPG`,
  `.Jpeg`, and an extension nobody anticipated (e.g. `.avif`) must all resolve without throwing, the last
  case falling back to `application/octet-stream`. (Task 1)
- **A cache-write I/O failure is distinguishable from "not a photo."** The service must not catch a broad
  `Exception` around the whole generate-and-cache path in a way that turns a permissions error or a full disk
  into the same `null` result as a genuine decode failure — only the decode step's failure maps to `null`.
  (Task 2)

---

## Task 1: Pure helpers — cache paths, resize math, content types, config

**Files:**
- Create: `src/PictureManager.Application/Thumbnails/DerivativeSize.cs`
- Create: `src/PictureManager.Application/Thumbnails/ThumbnailCacheOptions.cs`
- Create: `src/PictureManager.Application/Thumbnails/ThumbnailCachePathResolver.cs`
- Create: `src/PictureManager.Application/Thumbnails/ThumbnailResizeCalculator.cs`
- Create: `src/PictureManager.Application/Thumbnails/ImageContentTypeResolver.cs`
- Test: `tests/PictureManager.Application.Tests/Thumbnails/ThumbnailCachePathResolverTests.cs`
- Test: `tests/PictureManager.Application.Tests/Thumbnails/ThumbnailResizeCalculatorTests.cs`
- Test: `tests/PictureManager.Application.Tests/Thumbnails/ImageContentTypeResolverTests.cs`

**Interfaces:**
- Consumes: nothing from earlier phases beyond BCL types.
- Produces (used directly by Task 2 and Task 3):
  - `enum DerivativeSize { Thumbnail = 300, Preview = 1800 }`
  - `sealed class ThumbnailCacheOptions { string? RootPath { get; set; } bool PreviewEnabled { get; set; } = true; }`
  - `static class ThumbnailCachePathResolver { static string GetPath(string rootPath, string contentHash, DerivativeSize size); static string GetTempPath(string rootPath, string contentHash, DerivativeSize size); }`
  - `static class ThumbnailResizeCalculator { static (int Width, int Height) CalculateTargetDimensions(int originalWidth, int originalHeight, int longestEdgeTarget); static int NormalizeOrientation(int? orientation); }`
  - `static class ImageContentTypeResolver { static string Resolve(string extension); }`

- [ ] **Step 1: Write the failing tests for `ThumbnailCachePathResolver`**

```csharp
using FluentAssertions;
using PictureManager.Application.Thumbnails;
using Xunit;

namespace PictureManager.Application.Tests.Thumbnails;

public class ThumbnailCachePathResolverTests
{
    [Fact]
    public void GetPath_ShardsByFirstFourHashCharacters_AndAppendsSizeSuffix()
    {
        var path = ThumbnailCachePathResolver.GetPath("/cache", "a3f9c2abcdef", DerivativeSize.Thumbnail);

        path.Should().Be(System.IO.Path.Combine("/cache", "a3", "f9", "a3f9c2abcdef-300.webp"));
    }

    [Fact]
    public void GetPath_PreviewSize_UsesPreviewSuffix()
    {
        var path = ThumbnailCachePathResolver.GetPath("/cache", "a3f9c2abcdef", DerivativeSize.Preview);

        path.Should().EndWith("a3f9c2abcdef-1800.webp");
    }

    [Fact]
    public void GetTempPath_IsInTheSameShardDirectory_ButNotEqualToTheFinalPath()
    {
        var finalPath = ThumbnailCachePathResolver.GetPath("/cache", "a3f9c2abcdef", DerivativeSize.Thumbnail);
        var tempPath = ThumbnailCachePathResolver.GetTempPath("/cache", "a3f9c2abcdef", DerivativeSize.Thumbnail);

        System.IO.Path.GetDirectoryName(tempPath).Should().Be(System.IO.Path.GetDirectoryName(finalPath));
        tempPath.Should().NotBe(finalPath);
    }

    [Fact]
    public void GetPath_HashShorterThanFourCharacters_ThrowsArgumentException()
    {
        var act = () => ThumbnailCachePathResolver.GetPath("/cache", "ab", DerivativeSize.Thumbnail);

        act.Should().Throw<System.ArgumentException>();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/PictureManager.Application.Tests --filter ThumbnailCachePathResolverTests`
Expected: FAIL — `ThumbnailCachePathResolver` does not exist.

- [ ] **Step 3: Implement `DerivativeSize` and `ThumbnailCachePathResolver`**

```csharp
namespace PictureManager.Application.Thumbnails;

public enum DerivativeSize
{
    Thumbnail = 300,
    Preview = 1800
}
```

```csharp
using System;
using System.IO;

namespace PictureManager.Application.Thumbnails;

public static class ThumbnailCachePathResolver
{
    public static string GetPath(string rootPath, string contentHash, DerivativeSize size)
    {
        var (shard1, shard2) = GetShards(contentHash);
        var fileName = $"{contentHash}-{(int)size}.webp";
        return Path.Combine(rootPath, shard1, shard2, fileName);
    }

    public static string GetTempPath(string rootPath, string contentHash, DerivativeSize size)
    {
        var (shard1, shard2) = GetShards(contentHash);
        var fileName = $"{contentHash}-{(int)size}-{Guid.NewGuid():N}.tmp";
        return Path.Combine(rootPath, shard1, shard2, fileName);
    }

    private static (string Shard1, string Shard2) GetShards(string contentHash)
    {
        if (contentHash is null || contentHash.Length < 4)
            throw new ArgumentException("Content hash must be at least 4 characters long.", nameof(contentHash));

        return (contentHash[..2], contentHash[2..4]);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/PictureManager.Application.Tests --filter ThumbnailCachePathResolverTests`
Expected: PASS (4/4)

- [ ] **Step 5: Write the failing tests for `ThumbnailResizeCalculator`**

```csharp
using FluentAssertions;
using PictureManager.Application.Thumbnails;
using Xunit;

namespace PictureManager.Application.Tests.Thumbnails;

public class ThumbnailResizeCalculatorTests
{
    [Fact]
    public void CalculateTargetDimensions_LandscapeLargerThanTarget_ScalesByWidth()
    {
        var (width, height) = ThumbnailResizeCalculator.CalculateTargetDimensions(4000, 2000, 300);

        width.Should().Be(300);
        height.Should().Be(150);
    }

    [Fact]
    public void CalculateTargetDimensions_PortraitLargerThanTarget_ScalesByHeight()
    {
        var (width, height) = ThumbnailResizeCalculator.CalculateTargetDimensions(2000, 4000, 300);

        width.Should().Be(150);
        height.Should().Be(300);
    }

    [Fact]
    public void CalculateTargetDimensions_SourceSmallerThanTarget_NeverUpscales()
    {
        var (width, height) = ThumbnailResizeCalculator.CalculateTargetDimensions(200, 100, 300);

        width.Should().Be(200);
        height.Should().Be(100);
    }

    [Fact]
    public void CalculateTargetDimensions_LongestEdgeExactlyAtTarget_ReturnsOriginalDimensions()
    {
        var (width, height) = ThumbnailResizeCalculator.CalculateTargetDimensions(300, 200, 300);

        width.Should().Be(300);
        height.Should().Be(200);
    }

    [Theory]
    [InlineData(null, 1)]
    [InlineData(1, 1)]
    [InlineData(6, 6)]
    [InlineData(8, 8)]
    [InlineData(0, 1)]
    [InlineData(9, 1)]
    [InlineData(-3, 1)]
    public void NormalizeOrientation_OutOfRangeOrNull_FallsBackToOne(int? input, int expected)
    {
        ThumbnailResizeCalculator.NormalizeOrientation(input).Should().Be(expected);
    }
}
```

- [ ] **Step 6: Run tests to verify they fail**

Run: `dotnet test tests/PictureManager.Application.Tests --filter ThumbnailResizeCalculatorTests`
Expected: FAIL — `ThumbnailResizeCalculator` does not exist.

- [ ] **Step 7: Implement `ThumbnailResizeCalculator`**

```csharp
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
```

- [ ] **Step 8: Run tests to verify they pass**

Run: `dotnet test tests/PictureManager.Application.Tests --filter ThumbnailResizeCalculatorTests`
Expected: PASS (11/11: 4 dimension-calculation facts + 7 orientation-normalization theory cases)

- [ ] **Step 9: Write the failing tests for `ImageContentTypeResolver`**

```csharp
using FluentAssertions;
using PictureManager.Application.Thumbnails;
using Xunit;

namespace PictureManager.Application.Tests.Thumbnails;

public class ImageContentTypeResolverTests
{
    [Theory]
    [InlineData(".jpg", "image/jpeg")]
    [InlineData(".JPG", "image/jpeg")]
    [InlineData(".jpeg", "image/jpeg")]
    [InlineData(".png", "image/png")]
    [InlineData(".PNG", "image/png")]
    [InlineData(".heic", "image/heic")]
    [InlineData(".webp", "image/webp")]
    [InlineData(".gif", "image/gif")]
    [InlineData(".bmp", "image/bmp")]
    [InlineData(".tiff", "image/tiff")]
    [InlineData(".tif", "image/tiff")]
    public void Resolve_KnownExtension_ReturnsExpectedMimeType(string extension, string expected)
    {
        ImageContentTypeResolver.Resolve(extension).Should().Be(expected);
    }

    [Fact]
    public void Resolve_UnknownExtension_FallsBackToOctetStream()
    {
        ImageContentTypeResolver.Resolve(".avif").Should().Be("application/octet-stream");
    }
}
```

- [ ] **Step 10: Run tests to verify they fail**

Run: `dotnet test tests/PictureManager.Application.Tests --filter ImageContentTypeResolverTests`
Expected: FAIL — `ImageContentTypeResolver` does not exist.

- [ ] **Step 11: Implement `ImageContentTypeResolver` and `ThumbnailCacheOptions`**

```csharp
using System;

namespace PictureManager.Application.Thumbnails;

public static class ImageContentTypeResolver
{
    public static string Resolve(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".heic" => "image/heic",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".tiff" or ".tif" => "image/tiff",
            _ => "application/octet-stream"
        };
    }
}
```

```csharp
namespace PictureManager.Application.Thumbnails;

public sealed class ThumbnailCacheOptions
{
    public string? RootPath { get; set; }
    public bool PreviewEnabled { get; set; } = true;
}
```

- [ ] **Step 12: Run all Task 1 tests to verify they pass**

Run: `dotnet test tests/PictureManager.Application.Tests --filter "FullyQualifiedName~Thumbnails"`
Expected: PASS (27/27: 4 path-resolver + 11 resize-calculator + 12 content-type)

- [ ] **Step 13: Commit**

```bash
git add src/PictureManager.Application/Thumbnails tests/PictureManager.Application.Tests/Thumbnails
git commit -m "feat: add thumbnail cache path, resize, and content-type helpers"
```

---

## Task 2: `IThumbnailService` + `SkiaSharpThumbnailService`

**Files:**
- Create: `src/PictureManager.Application/Thumbnails/IThumbnailService.cs`
- Create: `src/PictureManager.Infrastructure/Thumbnails/SkiaSharpThumbnailService.cs`
- Modify: `src/PictureManager.Infrastructure/PictureManager.Infrastructure.csproj` (add `SkiaSharp`)
- Modify: `src/PictureManager.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs`
- Test: `tests/PictureManager.Infrastructure.Tests/Thumbnails/SkiaSharpThumbnailServiceTests.cs`

**Interfaces:**
- Consumes: `DerivativeSize`, `ThumbnailCacheOptions`, `ThumbnailCachePathResolver`, `ThumbnailResizeCalculator`
  (Task 1).
- Produces (used by Task 3's endpoints):
  - `interface IThumbnailService { Task<string?> GetOrCreateDerivativePathAsync(string contentHash, string sourcePath, int? orientation, DerivativeSize size, CancellationToken cancellationToken = default); }`
  - DI registration: `services.AddSingleton<IThumbnailService, SkiaSharpThumbnailService>();` in
    `InfrastructureServiceCollectionExtensions.AddInfrastructure`.

- [ ] **Step 1: Add the SkiaSharp package**

Run: `dotnet add src/PictureManager.Infrastructure package SkiaSharp`
Expected: package reference added to `PictureManager.Infrastructure.csproj`, `dotnet restore` succeeds.

- [ ] **Step 2: Write the interface**

```csharp
using System.Threading;
using System.Threading.Tasks;

namespace PictureManager.Application.Thumbnails;

public interface IThumbnailService
{
    /// <summary>
    /// Returns the absolute path to a cached WebP derivative of the source image at the requested size,
    /// generating and caching it first if it doesn't already exist. Returns null if the source file cannot
    /// be decoded as an image (never throws for that case). A filesystem error while writing the cache
    /// propagates as an exception -- it is not mapped to null.
    /// </summary>
    Task<string?> GetOrCreateDerivativePathAsync(
        string contentHash,
        string sourcePath,
        int? orientation,
        DerivativeSize size,
        CancellationToken cancellationToken = default);
}
```

- [ ] **Step 3: Write the failing tests**

These use small real bitmaps built in-test with SkiaSharp itself (no external fixture files needed) so the
suite has no binary test assets to maintain.

```csharp
using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using PictureManager.Application.Thumbnails;
using PictureManager.Infrastructure.Thumbnails;
using SkiaSharp;
using Xunit;

namespace PictureManager.Infrastructure.Tests.Thumbnails;

public class SkiaSharpThumbnailServiceTests : IDisposable
{
    private readonly string _cacheRoot = Path.Combine(Path.GetTempPath(), "pm-thumb-tests-" + Guid.NewGuid());
    private readonly string _sourcePath;

    public SkiaSharpThumbnailServiceTests()
    {
        Directory.CreateDirectory(_cacheRoot);
        _sourcePath = Path.Combine(_cacheRoot, "source.jpg");
        WriteTestJpeg(_sourcePath, width: 400, height: 200);
    }

    public void Dispose()
    {
        if (Directory.Exists(_cacheRoot))
            Directory.Delete(_cacheRoot, recursive: true);
    }

    private static void WriteTestJpeg(string path, int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.CornflowerBlue);
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);
        using var stream = File.OpenWrite(path);
        data.SaveTo(stream);
    }

    private SkiaSharpThumbnailService CreateService() =>
        new(new ThumbnailCacheOptions { RootPath = _cacheRoot, PreviewEnabled = true });

    [Fact]
    public async Task GetOrCreateDerivativePathAsync_CacheMiss_GeneratesFile_AtTheShardedPath()
    {
        var service = CreateService();

        var path = await service.GetOrCreateDerivativePathAsync("abcd1234", _sourcePath, orientation: 1, DerivativeSize.Thumbnail);

        path.Should().Be(ThumbnailCachePathResolver.GetPath(_cacheRoot, "abcd1234", DerivativeSize.Thumbnail));
        File.Exists(path).Should().BeTrue();
    }

    [Fact]
    public async Task GetOrCreateDerivativePathAsync_GeneratedFile_IsAValidWebpImage()
    {
        var service = CreateService();

        var path = await service.GetOrCreateDerivativePathAsync("abcd1234", _sourcePath, orientation: 1, DerivativeSize.Thumbnail);

        var bytes = await File.ReadAllBytesAsync(path!);
        bytes.Length.Should().BeGreaterThan(12);
        // RIFF....WEBP header
        System.Text.Encoding.ASCII.GetString(bytes, 0, 4).Should().Be("RIFF");
        System.Text.Encoding.ASCII.GetString(bytes, 8, 4).Should().Be("WEBP");
    }

    [Fact]
    public async Task GetOrCreateDerivativePathAsync_LandscapeSource_ResizedToLongestEdge()
    {
        var service = CreateService();

        var path = await service.GetOrCreateDerivativePathAsync("abcd1234", _sourcePath, orientation: 1, DerivativeSize.Thumbnail);

        using var stream = File.OpenRead(path!);
        using var resultBitmap = SKBitmap.Decode(stream);
        resultBitmap.Width.Should().Be(300);
        resultBitmap.Height.Should().Be(150); // 400x200 source, longest edge (400) -> 300, height scales to 150
    }

    [Fact]
    public async Task GetOrCreateDerivativePathAsync_OrientationSixRotatesNinetyDegrees_SwappingDimensions()
    {
        var service = CreateService();

        // Source is 400x200 (landscape). Orientation 6 = rotate 90 CW, so the output should be portrait.
        var path = await service.GetOrCreateDerivativePathAsync("rot6hash", _sourcePath, orientation: 6, DerivativeSize.Thumbnail);

        using var stream = File.OpenRead(path!);
        using var resultBitmap = SKBitmap.Decode(stream);
        resultBitmap.Width.Should().BeLessThan(resultBitmap.Height);
    }

    [Fact]
    public async Task GetOrCreateDerivativePathAsync_CacheHit_DoesNotRewriteTheFile()
    {
        var service = CreateService();
        var firstPath = await service.GetOrCreateDerivativePathAsync("abcd1234", _sourcePath, orientation: 1, DerivativeSize.Thumbnail);
        var firstWriteTimeUtc = File.GetLastWriteTimeUtc(firstPath!);

        await Task.Delay(50);
        var secondPath = await service.GetOrCreateDerivativePathAsync("abcd1234", _sourcePath, orientation: 1, DerivativeSize.Thumbnail);

        secondPath.Should().Be(firstPath);
        File.GetLastWriteTimeUtc(secondPath!).Should().Be(firstWriteTimeUtc);
    }

    [Fact]
    public async Task GetOrCreateDerivativePathAsync_UndecodableSource_ReturnsNull_AndLeavesNoTempFileBehind()
    {
        var badPath = Path.Combine(_cacheRoot, "not-an-image.jpg");
        await File.WriteAllBytesAsync(badPath, new byte[] { 0x00, 0x01, 0x02, 0x03 });
        var service = CreateService();

        var result = await service.GetOrCreateDerivativePathAsync("badbadbad", badPath, orientation: 1, DerivativeSize.Thumbnail);

        result.Should().BeNull();
        var shardDir = Path.GetDirectoryName(ThumbnailCachePathResolver.GetPath(_cacheRoot, "badbadbad", DerivativeSize.Thumbnail))!;
        if (Directory.Exists(shardDir))
            Directory.GetFiles(shardDir).Should().BeEmpty();
    }

    [Fact]
    public async Task GetOrCreateDerivativePathAsync_MissingSourceFile_ReturnsNull()
    {
        var service = CreateService();

        var result = await service.GetOrCreateDerivativePathAsync("nofilehash", Path.Combine(_cacheRoot, "does-not-exist.jpg"), orientation: 1, DerivativeSize.Thumbnail);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetOrCreateDerivativePathAsync_CacheRootUnwritable_PropagatesException_DoesNotReturnNull()
    {
        // Point the cache root at a path that is actually a FILE, not a directory, so Directory.CreateDirectory
        // throws while creating the shard subdirectory. This must NOT be swallowed into a null "decode failure"
        // result -- it's an operational fault (bad config / disk issue), not "this source isn't a photo".
        var blockingFilePath = Path.Combine(_cacheRoot, "blocked-root");
        await File.WriteAllTextAsync(blockingFilePath, "not a directory");
        var service = new SkiaSharpThumbnailService(new ThumbnailCacheOptions { RootPath = blockingFilePath, PreviewEnabled = true });

        var act = () => service.GetOrCreateDerivativePathAsync("abcd1234", _sourcePath, orientation: 1, DerivativeSize.Thumbnail);

        await act.Should().ThrowAsync<IOException>();
    }

    [Fact]
    public async Task GetOrCreateDerivativePathAsync_ConcurrentCallsForSameKey_AllSucceed_WithIdenticalValidOutput()
    {
        var service = CreateService();

        var tasks = Enumerable.Range(0, 5)
            .Select(_ => service.GetOrCreateDerivativePathAsync("concurrenthash", _sourcePath, orientation: 1, DerivativeSize.Thumbnail))
            .ToArray();
        var results = await Task.WhenAll(tasks);

        results.Should().AllSatisfy(path => path.Should().Be(ThumbnailCachePathResolver.GetPath(_cacheRoot, "concurrenthash", DerivativeSize.Thumbnail)));
        File.Exists(results[0]).Should().BeTrue();
        var bytes = await File.ReadAllBytesAsync(results[0]!);
        System.Text.Encoding.ASCII.GetString(bytes, 8, 4).Should().Be("WEBP");
    }
}
```

Add `using System.Linq;` to the usings block for the `Enumerable.Range`/`.Select` call in the last test.

- [ ] **Step 4: Run tests to verify they fail**

Run: `dotnet test tests/PictureManager.Infrastructure.Tests --filter SkiaSharpThumbnailServiceTests`
Expected: FAIL — `SkiaSharpThumbnailService` does not exist.

- [ ] **Step 5: Implement `SkiaSharpThumbnailService`**

```csharp
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Application.Thumbnails;
using SkiaSharp;

namespace PictureManager.Infrastructure.Thumbnails;

public sealed class SkiaSharpThumbnailService : IThumbnailService
{
    private const int WebPQuality = 82;
    private readonly ThumbnailCacheOptions _options;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public SkiaSharpThumbnailService(ThumbnailCacheOptions options)
    {
        _options = options;
    }

    public async Task<string?> GetOrCreateDerivativePathAsync(
        string contentHash, string sourcePath, int? orientation, DerivativeSize size, CancellationToken cancellationToken = default)
    {
        var rootPath = _options.RootPath ?? throw new InvalidOperationException("ThumbnailCache:RootPath is not configured.");
        var finalPath = ThumbnailCachePathResolver.GetPath(rootPath, contentHash, size);

        if (File.Exists(finalPath))
            return finalPath;

        if (!File.Exists(sourcePath))
            return null;

        var key = $"{contentHash}-{(int)size}";
        var gate = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(finalPath))
                return finalPath;

            return await GenerateAsync(sourcePath, orientation, size, rootPath, contentHash, finalPath, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private static async Task<string?> GenerateAsync(
        string sourcePath, int? orientation, DerivativeSize size, string rootPath, string contentHash, string finalPath, CancellationToken cancellationToken)
    {
        using var sourceBitmap = DecodeSafely(sourcePath);
        if (sourceBitmap is null)
            return null;

        var normalizedOrientation = ThumbnailResizeCalculator.NormalizeOrientation(orientation);
        using var orientedBitmap = ApplyOrientation(sourceBitmap, normalizedOrientation);

        var (targetWidth, targetHeight) = ThumbnailResizeCalculator.CalculateTargetDimensions(
            orientedBitmap.Width, orientedBitmap.Height, (int)size);

        using var resizedBitmap = orientedBitmap.Resize(
            new SKImageInfo(targetWidth, targetHeight), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        if (resizedBitmap is null)
            return null;

        var shardDirectory = Path.GetDirectoryName(finalPath)!;
        Directory.CreateDirectory(shardDirectory);

        var tempPath = ThumbnailCachePathResolver.GetTempPath(rootPath, contentHash, size);
        using (var image = SKImage.FromBitmap(resizedBitmap))
        using (var data = image.Encode(SKEncodedImageFormat.Webp, WebPQuality))
        await using (var fileStream = File.Create(tempPath))
        {
            data.SaveTo(fileStream);
        }

        File.Move(tempPath, finalPath, overwrite: true);
        return finalPath;
    }

    private static SKBitmap? DecodeSafely(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return SKBitmap.Decode(stream);
        }
        catch (Exception ex) when (ex is IOException or SKException or InvalidOperationException)
        {
            return null;
        }
    }

    private static SKBitmap ApplyOrientation(SKBitmap source, int orientation)
    {
        if (orientation == 1)
            return source.Copy();

        var swapDimensions = orientation is 5 or 6 or 7 or 8;
        var width = swapDimensions ? source.Height : source.Width;
        var height = swapDimensions ? source.Width : source.Height;

        var rotated = new SKBitmap(width, height);
        using var canvas = new SKCanvas(rotated);
        var matrix = orientation switch
        {
            2 => SKMatrix.CreateScale(-1, 1).PostConcat(SKMatrix.CreateTranslation(width, 0)),
            3 => SKMatrix.CreateRotationDegrees(180, width / 2f, height / 2f),
            4 => SKMatrix.CreateScale(1, -1).PostConcat(SKMatrix.CreateTranslation(0, height)),
            5 => SKMatrix.CreateRotationDegrees(90).PostConcat(SKMatrix.CreateScale(-1, 1)).PostConcat(SKMatrix.CreateTranslation(width, 0)),
            6 => SKMatrix.CreateRotationDegrees(90).PostConcat(SKMatrix.CreateTranslation(width, 0)),
            7 => SKMatrix.CreateRotationDegrees(-90).PostConcat(SKMatrix.CreateScale(-1, 1)).PostConcat(SKMatrix.CreateTranslation(0, height)),
            8 => SKMatrix.CreateRotationDegrees(-90).PostConcat(SKMatrix.CreateTranslation(0, height)),
            _ => SKMatrix.Identity
        };
        canvas.SetMatrix(matrix);
        canvas.DrawBitmap(source, 0, 0);
        return rotated;
    }
}
```

**Note for the implementer:** the exact `SKMatrix`/`SKCanvas` transform calls above encode the intent (rotate
90° CW for orientation 6, etc.) but SkiaSharp's matrix-composition API has shifted across versions (`PostConcat`
vs. operator overloads vs. `SKMatrix.Concat`). Verify against whichever SkiaSharp version resolves, and confirm
with the orientation-6 test (Step 3) that dimensions actually swap and the rotation direction is visually
correct — that test is the ground truth here, not the code above.

- [ ] **Step 6: Register the service in DI**

In `src/PictureManager.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs`, add
alongside the existing `IContentHasher`/`IExifReader` registrations:

```csharp
        services.AddSingleton<IThumbnailService, SkiaSharpThumbnailService>();
```

Add `using PictureManager.Application.Thumbnails;` and `using PictureManager.Infrastructure.Thumbnails;` to
that file's usings. Note this registration requires a `ThumbnailCacheOptions` instance to already be
registered in the container — that happens in Task 3's `Program.cs` change, not here (mirrors how
`DevImageRootOptions` is registered directly in `Program.cs`, not inside `AddInfrastructure`).

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test tests/PictureManager.Infrastructure.Tests --filter SkiaSharpThumbnailServiceTests`
Expected: PASS (9/9)

- [ ] **Step 8: Run the full solution test suite**

Run: `dotnet test`
Expected: PASS, all projects (this task doesn't touch DI registration in a way that's resolvable until
Task 3 registers `ThumbnailCacheOptions` — `AddInfrastructure` itself has no test that resolves the full
container, matching the existing pattern for `IContentHasher`/`IExifReader`).

- [ ] **Step 9: Commit**

```bash
git add src/PictureManager.Application/Thumbnails/IThumbnailService.cs \
        src/PictureManager.Infrastructure/Thumbnails \
        src/PictureManager.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs \
        src/PictureManager.Infrastructure/PictureManager.Infrastructure.csproj \
        tests/PictureManager.Infrastructure.Tests/Thumbnails
git commit -m "feat: add SkiaSharp-based thumbnail generation service"
```

---

## Task 3: `ImageEndpoints` — thumbnail and preview routes

**Files:**
- Create: `src/PictureManager.Api/Endpoints/ImageEndpoints.cs`
- Test: `tests/PictureManager.Api.Tests/Endpoints/ImageEndpointsTests.cs`

**Interfaces:**
- Consumes: `IImageRepository.GetByIdWithFolderAsync` (phase 3, includes `Folder.Root`),
  `ImagePathResolver.ResolvePhysicalPath` (phase 3), `IThumbnailService.GetOrCreateDerivativePathAsync`
  (Task 2), `ImageContentTypeResolver.Resolve` (Task 1), `ThumbnailCacheOptions.PreviewEnabled` (Task 1).
- Produces (used by Task 4's `Program.cs` wiring):
  - `static class ImageEndpoints { static void MapImageEndpoints(this WebApplication app); static Task<IResult> GetThumbnailAsync(int id, IImageRepository, IThumbnailService, CancellationToken); static Task<IResult> GetPreviewAsync(int id, IImageRepository, IThumbnailService, ThumbnailCacheOptions, CancellationToken); }`

- [ ] **Step 1: Write the failing tests**

```csharp
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http.HttpResults;
using NSubstitute;
using PictureManager.Api.Endpoints;
using PictureManager.Application.Repositories;
using PictureManager.Application.Thumbnails;
using PictureManager.Model;
using Xunit;

namespace PictureManager.Api.Tests.Endpoints;

public class ImageEndpointsTests
{
    private static Image BuildImage(int id, bool missing = false) => new()
    {
        Id = id,
        FolderId = 1,
        FileName = "photo",
        Extension = ".jpg",
        ContentHash = "abcd1234",
        MissingSinceUtc = missing ? System.DateTime.UtcNow : null,
        Folder = new Folder
        {
            Id = 1,
            RootId = 1,
            RelativePath = "vacation",
            Root = new ImageRoot { Id = 1, MountPath = "/images" }
        }
    };

    [Fact]
    public async Task GetThumbnailAsync_UnknownId_ReturnsNotFound()
    {
        var imageRepository = Substitute.For<IImageRepository>();
        imageRepository.GetByIdWithFolderAsync(1, Arg.Any<CancellationToken>()).Returns((Image?)null);
        var thumbnailService = Substitute.For<IThumbnailService>();

        var result = await ImageEndpoints.GetThumbnailAsync(1, imageRepository, thumbnailService, CancellationToken.None);

        result.Should().BeOfType<NotFound>();
    }

    [Fact]
    public async Task GetThumbnailAsync_MissingSinceUtcSet_ReturnsNotFound()
    {
        var imageRepository = Substitute.For<IImageRepository>();
        imageRepository.GetByIdWithFolderAsync(1, Arg.Any<CancellationToken>()).Returns(BuildImage(1, missing: true));
        var thumbnailService = Substitute.For<IThumbnailService>();

        var result = await ImageEndpoints.GetThumbnailAsync(1, imageRepository, thumbnailService, CancellationToken.None);

        result.Should().BeOfType<NotFound>();
    }

    [Fact]
    public async Task GetThumbnailAsync_ThumbnailServiceReturnsNull_ReturnsNotFound()
    {
        var imageRepository = Substitute.For<IImageRepository>();
        imageRepository.GetByIdWithFolderAsync(1, Arg.Any<CancellationToken>()).Returns(BuildImage(1));
        var thumbnailService = Substitute.For<IThumbnailService>();
        thumbnailService.GetOrCreateDerivativePathAsync("abcd1234", Arg.Any<string>(), Arg.Any<int?>(), DerivativeSize.Thumbnail, Arg.Any<CancellationToken>())
            .Returns((string?)null);

        var result = await ImageEndpoints.GetThumbnailAsync(1, imageRepository, thumbnailService, CancellationToken.None);

        result.Should().BeOfType<NotFound>();
    }

    [Fact]
    public async Task GetThumbnailAsync_Success_ReturnsPhysicalFileResult_WithImmutableCacheControl()
    {
        var imageRepository = Substitute.For<IImageRepository>();
        imageRepository.GetByIdWithFolderAsync(1, Arg.Any<CancellationToken>()).Returns(BuildImage(1));
        var thumbnailService = Substitute.For<IThumbnailService>();
        thumbnailService.GetOrCreateDerivativePathAsync("abcd1234", Arg.Any<string>(), Arg.Any<int?>(), DerivativeSize.Thumbnail, Arg.Any<CancellationToken>())
            .Returns("/cache/ab/cd/abcd1234-300.webp");

        var result = await ImageEndpoints.GetThumbnailAsync(1, imageRepository, thumbnailService, CancellationToken.None);

        var fileResult = result.Should().BeOfType<PhysicalFileHttpResult>().Subject;
        fileResult.FileName.Should().Be("/cache/ab/cd/abcd1234-300.webp");
        fileResult.ContentType.Should().Be("image/webp");
        fileResult.EnableRangeProcessing.Should().BeTrue();
    }

    [Fact]
    public async Task GetPreviewAsync_PreviewEnabled_UsesThumbnailServiceForTheLargerSize()
    {
        var imageRepository = Substitute.For<IImageRepository>();
        imageRepository.GetByIdWithFolderAsync(1, Arg.Any<CancellationToken>()).Returns(BuildImage(1));
        var thumbnailService = Substitute.For<IThumbnailService>();
        thumbnailService.GetOrCreateDerivativePathAsync("abcd1234", Arg.Any<string>(), Arg.Any<int?>(), DerivativeSize.Preview, Arg.Any<CancellationToken>())
            .Returns("/cache/ab/cd/abcd1234-1800.webp");
        var options = new ThumbnailCacheOptions { PreviewEnabled = true };

        var result = await ImageEndpoints.GetPreviewAsync(1, imageRepository, thumbnailService, options, CancellationToken.None);

        var fileResult = result.Should().BeOfType<PhysicalFileHttpResult>().Subject;
        fileResult.FileName.Should().Be("/cache/ab/cd/abcd1234-1800.webp");
        fileResult.ContentType.Should().Be("image/webp");
    }

    [Fact]
    public async Task GetPreviewAsync_PreviewDisabled_ServesOriginalFile_WithShortCacheAndResolvedContentType()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "pm-preview-fallback-" + System.Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(tempDir, "vacation"));
        var originalPath = Path.Combine(tempDir, "vacation", "photo.jpg");
        await File.WriteAllBytesAsync(originalPath, new byte[] { 1, 2, 3 });
        try
        {
            var image = BuildImage(1);
            image.Folder!.Root!.MountPath = tempDir;
            var imageRepository = Substitute.For<IImageRepository>();
            imageRepository.GetByIdWithFolderAsync(1, Arg.Any<CancellationToken>()).Returns(image);
            var thumbnailService = Substitute.For<IThumbnailService>();
            var options = new ThumbnailCacheOptions { PreviewEnabled = false };

            var result = await ImageEndpoints.GetPreviewAsync(1, imageRepository, thumbnailService, options, CancellationToken.None);

            var fileResult = result.Should().BeOfType<PhysicalFileHttpResult>().Subject;
            fileResult.FileName.Should().Be(originalPath);
            fileResult.ContentType.Should().Be("image/jpeg");
            await thumbnailService.DidNotReceive().GetOrCreateDerivativePathAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<DerivativeSize>(), Arg.Any<CancellationToken>());
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task GetPreviewAsync_PreviewDisabled_OriginalFileMissing_ReturnsNotFound()
    {
        var image = BuildImage(1);
        image.Folder!.Root!.MountPath = Path.Combine(Path.GetTempPath(), "pm-preview-missing-" + System.Guid.NewGuid());
        var imageRepository = Substitute.For<IImageRepository>();
        imageRepository.GetByIdWithFolderAsync(1, Arg.Any<CancellationToken>()).Returns(image);
        var thumbnailService = Substitute.For<IThumbnailService>();
        var options = new ThumbnailCacheOptions { PreviewEnabled = false };

        var result = await ImageEndpoints.GetPreviewAsync(1, imageRepository, thumbnailService, options, CancellationToken.None);

        result.Should().BeOfType<NotFound>();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/PictureManager.Api.Tests --filter ImageEndpointsTests`
Expected: FAIL — `ImageEndpoints` does not exist.

- [ ] **Step 3: Implement `ImageEndpoints`**

```csharp
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Application.Repositories;
using PictureManager.Application.Scanning;
using PictureManager.Application.Thumbnails;

namespace PictureManager.Api.Endpoints;

public static class ImageEndpoints
{
    public static void MapImageEndpoints(this WebApplication app)
    {
        app.MapGet("/api/images/{id:int}/thumbnail", GetThumbnailAsync);
        app.MapGet("/api/images/{id:int}/preview", GetPreviewAsync);
    }

    public static async Task<IResult> GetThumbnailAsync(
        int id, IImageRepository imageRepository, IThumbnailService thumbnailService, CancellationToken cancellationToken)
    {
        var image = await imageRepository.GetByIdWithFolderAsync(id, cancellationToken);
        if (image?.Folder?.Root is null || image.MissingSinceUtc is not null)
            return Results.NotFound();

        var physicalPath = ImagePathResolver.ResolvePhysicalPath(
            image.Folder.Root.MountPath, image.Folder.RelativePath, image.FileName, image.Extension);

        var derivativePath = await thumbnailService.GetOrCreateDerivativePathAsync(
            image.ContentHash, physicalPath, image.Orientation, DerivativeSize.Thumbnail, cancellationToken);
        if (derivativePath is null)
            return Results.NotFound();

        return Results.File(derivativePath, "image/webp", enableRangeProcessing: true);
    }

    public static async Task<IResult> GetPreviewAsync(
        int id, IImageRepository imageRepository, IThumbnailService thumbnailService, ThumbnailCacheOptions cacheOptions, CancellationToken cancellationToken)
    {
        var image = await imageRepository.GetByIdWithFolderAsync(id, cancellationToken);
        if (image?.Folder?.Root is null || image.MissingSinceUtc is not null)
            return Results.NotFound();

        var physicalPath = ImagePathResolver.ResolvePhysicalPath(
            image.Folder.Root.MountPath, image.Folder.RelativePath, image.FileName, image.Extension);

        if (!cacheOptions.PreviewEnabled)
        {
            if (!File.Exists(physicalPath))
                return Results.NotFound();

            return Results.File(physicalPath, ImageContentTypeResolver.Resolve(image.Extension), enableRangeProcessing: true);
        }

        var derivativePath = await thumbnailService.GetOrCreateDerivativePathAsync(
            image.ContentHash, physicalPath, image.Orientation, DerivativeSize.Preview, cancellationToken);
        if (derivativePath is null)
            return Results.NotFound();

        return Results.File(derivativePath, "image/webp", enableRangeProcessing: true);
    }
}
```

**Note:** `Results.File(path, ...)` for a physical on-disk path returns a `PhysicalFileHttpResult` at
runtime, matching the test assertions above. The `Cache-Control` headers from the Global Constraints
(`immutable, max-age=31536000` for derivatives; `max-age=300` for the original-fallback path) are applied
via `context.Response.Headers` in Task 4's `Program.cs`/middleware wiring — see Task 4 Step 1 — rather than
inside these handlers, so the handler unit tests above can assert on the `IResult` shape without spinning up
a real `HttpContext`.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/PictureManager.Api.Tests --filter ImageEndpointsTests`
Expected: PASS (7/7)

- [ ] **Step 5: Commit**

```bash
git add src/PictureManager.Api/Endpoints/ImageEndpoints.cs tests/PictureManager.Api.Tests/Endpoints/ImageEndpointsTests.cs
git commit -m "feat: add thumbnail and preview image-serving endpoints"
```

---

## Task 4: Cache-Control middleware, config, DI wiring, and live verification

**Files:**
- Create: `src/PictureManager.Api/Middleware/ImageCacheControlMiddleware.cs`
- Modify: `src/PictureManager.Api/Program.cs`
- Modify: `src/PictureManager.Api/appsettings.json`
- Modify: `src/PictureManager.Api/appsettings.Development.json`
- Test: `tests/PictureManager.Api.Tests/Middleware/ImageCacheControlMiddlewareTests.cs`

**Interfaces:**
- Consumes: `ImageEndpoints.MapImageEndpoints` (Task 3), `ThumbnailCacheOptions` (Task 1),
  `SkiaSharpThumbnailService` DI registration (Task 2).
- Produces: nothing consumed by a later task — this is the phase's final integration point.

- [ ] **Step 1: Write the failing middleware test**

The middleware sets `Cache-Control` based on path shape: `/preview` when `PreviewEnabled` is `false` gets the
short-lived header; both `/thumbnail` and an enabled `/preview` get the immutable header; everything else is
untouched.

```csharp
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using PictureManager.Api.Middleware;
using PictureManager.Application.Thumbnails;
using Xunit;

namespace PictureManager.Api.Tests.Middleware;

public class ImageCacheControlMiddlewareTests
{
    private static async Task<DefaultHttpContext> InvokeAsync(string path, bool previewEnabled)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        var options = new ThumbnailCacheOptions { PreviewEnabled = previewEnabled };
        var middleware = new ImageCacheControlMiddleware(_ => Task.CompletedTask, options);

        await middleware.InvokeAsync(context);
        return context;
    }

    [Fact]
    public async Task ThumbnailPath_AlwaysGetsImmutableLongCache()
    {
        var context = await InvokeAsync("/api/images/5/thumbnail", previewEnabled: false);

        context.Response.Headers.CacheControl.ToString().Should().Be("private, max-age=31536000, immutable");
    }

    [Fact]
    public async Task PreviewPath_WhenEnabled_GetsImmutableLongCache()
    {
        var context = await InvokeAsync("/api/images/5/preview", previewEnabled: true);

        context.Response.Headers.CacheControl.ToString().Should().Be("private, max-age=31536000, immutable");
    }

    [Fact]
    public async Task PreviewPath_WhenDisabled_GetsShortRevalidatingCache()
    {
        var context = await InvokeAsync("/api/images/5/preview", previewEnabled: false);

        context.Response.Headers.CacheControl.ToString().Should().Be("private, max-age=300");
    }

    [Fact]
    public async Task UnrelatedPath_NoCacheControlHeaderAdded()
    {
        var context = await InvokeAsync("/api/scans", previewEnabled: false);

        context.Response.Headers.CacheControl.ToString().Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/PictureManager.Api.Tests --filter ImageCacheControlMiddlewareTests`
Expected: FAIL — `ImageCacheControlMiddleware` does not exist.

- [ ] **Step 3: Implement the middleware**

```csharp
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using PictureManager.Application.Thumbnails;

namespace PictureManager.Api.Middleware;

public sealed class ImageCacheControlMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ThumbnailCacheOptions _options;

    public ImageCacheControlMiddleware(RequestDelegate next, ThumbnailCacheOptions options)
    {
        _next = next;
        _options = options;
    }

    public Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        var isThumbnail = path.EndsWith("/thumbnail", StringComparison.OrdinalIgnoreCase);
        var isPreview = path.EndsWith("/preview", StringComparison.OrdinalIgnoreCase);

        if (isThumbnail || (isPreview && _options.PreviewEnabled))
        {
            context.Response.Headers.CacheControl = "private, max-age=31536000, immutable";
        }
        else if (isPreview)
        {
            context.Response.Headers.CacheControl = "private, max-age=300";
        }

        return _next(context);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/PictureManager.Api.Tests --filter ImageCacheControlMiddlewareTests`
Expected: PASS (4/4)

- [ ] **Step 5: Add configuration values**

In `src/PictureManager.Api/appsettings.json`, add a `ThumbnailCache` section alongside `ConnectionStrings`:

```json
  "ThumbnailCache": {
    "PreviewEnabled": true
  },
```

In `src/PictureManager.Api/appsettings.Development.json`, add the dev-local root path alongside
`DevImageRoot`:

```json
  "ThumbnailCache": {
    "RootPath": "../../thumbnail-cache"
  }
```

- [ ] **Step 6: Wire `Program.cs`**

Add near the existing `DevImageRootOptions` binding block:

```csharp
    var thumbnailCacheOptions = new ThumbnailCacheOptions();
    builder.Configuration.GetSection("ThumbnailCache").Bind(thumbnailCacheOptions);
    builder.Services.AddSingleton(thumbnailCacheOptions);
```

Add `using PictureManager.Application.Thumbnails;` and `using PictureManager.Api.Middleware;` to `Program.cs`'s
usings. After `app.MapScanEndpoints();`, add:

```csharp
    app.UseMiddleware<ImageCacheControlMiddleware>();
    app.MapImageEndpoints();
```

Note `ImageCacheControlMiddleware` is registered as ASP.NET Core middleware via `UseMiddleware<T>`, which
constructs it once per app (not per-request) and resolves `ThumbnailCacheOptions` from DI automatically since
it's already registered as a singleton — no manual factory needed.

- [ ] **Step 7: Run the full solution test suite**

Run: `dotnet build && dotnet test`
Expected: 0 warnings, 0 errors; all tests passing across all five test projects — 135 total (88 from phase 3
+ 47 new this phase: 27 helper tests (Task 1) + 9 thumbnail-service tests (Task 2) + 7 endpoint tests
(Task 3) + 4 middleware tests (Task 4)).

- [ ] **Step 8: Commit**

```bash
git add src/PictureManager.Api/Middleware src/PictureManager.Api/Program.cs \
        src/PictureManager.Api/appsettings.json src/PictureManager.Api/appsettings.Development.json \
        tests/PictureManager.Api.Tests/Middleware
git commit -m "feat: wire thumbnail cache-control middleware, config, and image endpoints"
```

- [ ] **Step 9: Live verification (controller-performed — needs a real photo and a running process, not
  delegable to a subagent, same precedent as phase 3's Task 11 Steps 5-7)**

1. Ensure the phase-3 `db` container is running (`docker compose up -d db`) and the dev `ImageRoot` has at
   least one real or synthetic photo indexed (phase 3's smoke-test JPEG generation approach can be reused, or
   run a fresh scan against `dev-data/images/`).
2. Create the local thumbnail cache directory if it doesn't already exist:
   `mkdir -p thumbnail-cache` at the repo root (matches the gitignored, already-excluded `thumbnail-cache/`
   entry).
3. Run the app (`dotnet run --project src/PictureManager.Api`), then:
   - `GET /api/images/{id}/thumbnail` for a known indexed image id — verify HTTP 200, `Content-Type:
     image/webp`, `Cache-Control: private, max-age=31536000, immutable`, and that
     `thumbnail-cache/{shard}/{shard}/{hash}-300.webp` now exists on disk.
   - Repeat the same request — verify the file's mtime on disk is unchanged (cache hit, not regenerated).
   - `GET /api/images/{id}/preview` with the default config (`PreviewEnabled: true`) — verify a `-1800.webp`
     file is created and served the same way.
   - Set `ThumbnailCache:PreviewEnabled` to `false` (e.g. via an environment variable override
     `ThumbnailCache__PreviewEnabled=false` or editing `appsettings.Development.json` temporarily), restart
     the app, and repeat the `/preview` request — verify it now returns the original file's bytes directly
     (compare `Content-Length` against the source file's size) with `Content-Type` matching the source
     extension and `Cache-Control: private, max-age=300`, and that no new file appears under
     `thumbnail-cache/`.
   - `GET /api/images/999999/thumbnail` (nonexistent id) — verify HTTP 404.
4. Record the results in the task report; revert any temporary config override made for step 3's toggle
   test.
