# Similar-Photo Detection (Resized / Re-encoded Copies) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> On approval, copy this file to `docs/superpowers/plans/2026-10-01-similar-photo-detection.md` (repo convention). Implementation is **not** part of this request; this is the plan only.

**Goal:** Let the Duplicates view find copies of a photo that were resized, recompressed or converted to another format, in addition to today's byte-identical copies.

**Architecture:** During enrichment, decode each image with SkiaSharp, apply EXIF orientation, and compute a 64-bit **dHash** (difference hash), stored as 16 hex chars in the existing, unused `Image.PerceptualHash` column. A new `/similar` query loads all `(id, hash)` pairs, clusters them with union-find over Hamming distance ≤ threshold (8×8-bit band index, so it doesn't compare every pair), and returns groups with the largest-resolution copy first. A one-time backfill requeues already-indexed images through the existing enrichment queue. The UI gets an "Exact / Similar" toggle on the Duplicates view.

**Tech Stack:** .NET 10, EF Core 10 + PostgreSQL, SkiaSharp 4.152.1 (already referenced), xUnit + FluentAssertions + NSubstitute; React 19 + TanStack Query + MUI.

**Spec:** this conversation. Requirement: "detect if an image is converted, i.e. from a large size to a lower size or resolution", surfaced through the duplicate-search feature.

## Context

`DuplicateService` ([src/PictureManager.Application/Duplicates/DuplicateService.cs](src/PictureManager.Application/Duplicates/DuplicateService.cs)) groups by `ContentHash`, an xxHash of the file size plus the first and last 64 KB ([XxHashContentHasher.cs](src/PictureManager.Infrastructure/Scanning/XxHashContentHasher.cs)). A downscaled or re-encoded copy has different bytes, so it never matches. `Image.PerceptualHash` (`string?`, max 200, [Image.cs:13](src/PictureManager.Model/Image.cs#L13)) has existed since the initial migration but is never written. No schema migration is needed.

## Global Constraints

- No new NuGet packages: SkiaSharp is already in `PictureManager.Infrastructure`.
- `PerceptualHash` encoding: exactly 16 lowercase hex chars (64-bit dHash, row-major, bit 63 first). `""` = attempted but undecodable (stops endless requeue). `null` = never computed.
- Default similarity threshold: Hamming distance ≤ **6** of 64. Request `threshold` range: 0–7 (`SimilarityClusterer.MaxThreshold`; the 8-band index is exact only up to 7).
- The existing exact `/duplicates` endpoint and its behaviour stay unchanged.
- Only visible images (`WhereVisible()`) take part, same as exact duplicates.
- Backend conventions per CLAUDE.md: file-scoped namespaces, `Result<T>` from Application services, minimal-API endpoints with `.ToOk()`.

## Review Focus

1. **EXIF-rotated copy:** a portrait original with Orientation=6 and an exported copy with pixels physically rotated (Orientation=1) must still match. Hash after `ApplyOrientation` (Task 1 test).
2. **Uniform / near-blank images** (black frames, white scans) all hash to ~0 and would form one giant false group. Skip hashes with popcount ≤ 2 or ≥ 62 from clustering (Task 3 test).
3. **Exact copies also appear in Similar mode:** that's fine and expected (distance 0). Group headers must say "similar", not "copies" (Task 5).
4. **Transitive chaining:** union-find links A~B~C into one group even when A and C are far apart. This is accepted (copies of copies belong together). Each group reports `MaxDistance` so the UI can show it, and a test pins that a chain forms one group (Task 3).
5. **Undecodable file during enrichment:** must not throw or fail enrichment. Store `""` and keep the rest of the enrichment (Task 2 test).

---

## File Structure

| File | Action | Responsibility |
|---|---|---|
| `src/PictureManager.Application/Scanning/IPerceptualHasher.cs` | Create | `Task<string?> ComputeAsync(string path, int? orientation, CancellationToken)` |
| `src/PictureManager.Infrastructure/Imaging/SkiaBitmapOps.cs` | Create | `DecodeSafely` + `ApplyOrientation`, moved out of `SkiaSharpThumbnailService` (now `internal static`, shared) |
| `src/PictureManager.Infrastructure/Thumbnails/SkiaSharpThumbnailService.cs` | Modify | Call `SkiaBitmapOps` instead of its private copies |
| `src/PictureManager.Infrastructure/Scanning/SkiaDHashPerceptualHasher.cs` | Create | dHash implementation |
| `src/PictureManager.Application/Scanning/ImageEnrichmentService.cs` | Modify | Compute and store `PerceptualHash` |
| `src/PictureManager.Application/Duplicates/PerceptualHash.cs` | Create | Pure helpers: `Parse`, `Distance`, `IsDegenerate` |
| `src/PictureManager.Application/Duplicates/SimilarityClusterer.cs` | Create | Band-index + union-find clustering (pure, no I/O) |
| `src/PictureManager.Application/Duplicates/DuplicateModels.cs` | Modify | Add `SimilarGroup`, `PerceptualHashRow`, `SimilarCursor`; add `FileSize` to `DuplicateImageItem` |
| `src/PictureManager.Application/Duplicates/IDuplicateService.cs` + `DuplicateService.cs` | Modify | `ListSimilarAsync(threshold, cursor, limit)` |
| `src/PictureManager.Application/Repositories/IImageQueryRepository.cs` + `Infrastructure/.../ImageQueryRepository.cs` | Modify | `GetPerceptualHashesAsync`, `GetMembersByIdsAsync` |
| `src/PictureManager.Application/Repositories/IImageRepository.cs` + `ImageRepository.cs` | Modify | `GetIdsMissingPerceptualHashAsync` |
| `src/PictureManager.Application/Scanning/ScanService.cs` | Modify | `RequeueMissingPerceptualHashAsync` (mirrors `RequeueStalledEnrichmentAsync`, line 151) |
| Worker startup that calls `RequeueStalledEnrichmentAsync` | Modify | Also call the new requeue |
| DI registration (`Infrastructure` service-collection extension) | Modify | Register `IPerceptualHasher` |
| `src/PictureManager.Api/Endpoints/DuplicateEndpoints.cs` | Modify | `GET /duplicates/similar?threshold=&cursor=&limit=` |
| `web/src/api/types.ts`, `web/src/api/queries.ts` | Modify | `SimilarGroup` type, `useSimilarDuplicates(threshold)` |
| `web/src/views/DuplicatesView.tsx` (+ test) | Modify | Exact/Similar toggle via URL param `mode=similar` |

---

### Task 1: dHash computation in Infrastructure

**Files:**
- Create: `src/PictureManager.Application/Scanning/IPerceptualHasher.cs`
- Create: `src/PictureManager.Infrastructure/Imaging/SkiaBitmapOps.cs`
- Modify: `src/PictureManager.Infrastructure/Thumbnails/SkiaSharpThumbnailService.cs` (lines 92–165: move `DecodeSafely` and `ApplyOrientation` out)
- Create: `src/PictureManager.Infrastructure/Scanning/SkiaDHashPerceptualHasher.cs`
- Test: `tests/PictureManager.Infrastructure.Tests/Scanning/SkiaDHashPerceptualHasherTests.cs`

**Interfaces:**
- Produces: `IPerceptualHasher.ComputeAsync(string filePath, int? orientation, CancellationToken ct) : Task<string?>`. Returns 16 hex chars, or `null` if the file can't be decoded.

- [ ] **Step 1: Write failing tests.** Generate the fixtures in-test with SkiaSharp (a gradient plus shapes, 2000×1500), saved to a temp dir:

```csharp
public sealed class SkiaDHashPerceptualHasherTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory().FullName;
    private readonly SkiaDHashPerceptualHasher _hasher = new();

    private string Save(SKBitmap bmp, string name, SKEncodedImageFormat fmt, int quality)
    {
        var path = Path.Combine(_dir, name);
        using var data = bmp.Encode(fmt, quality);
        File.WriteAllBytes(path, data.ToArray());
        return path;
    }

    private static SKBitmap Scene(int w, int h)
    {
        var bmp = new SKBitmap(w, h);
        using var c = new SKCanvas(bmp);
        using var shader = SKShader.CreateLinearGradient(new(0, 0), new(w, h),
            [SKColors.Navy, SKColors.Orange], SKShaderTileMode.Clamp);
        c.DrawRect(0, 0, w, h, new SKPaint { Shader = shader });
        c.DrawCircle(w * 0.3f, h * 0.4f, h * 0.2f, new SKPaint { Color = SKColors.White });
        c.DrawRect(w * 0.6f, h * 0.5f, w * 0.25f, h * 0.3f, new SKPaint { Color = SKColors.Black });
        return bmp;
    }

    private static SKBitmap Resize(SKBitmap src, int w, int h) =>
        src.Resize(new SKImageInfo(w, h), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));

    private static SKBitmap Rotate90(SKBitmap src)
    {
        var dst = new SKBitmap(src.Height, src.Width);
        using var c = new SKCanvas(dst);
        c.Translate(dst.Width, 0); c.RotateDegrees(90); c.DrawBitmap(src, 0, 0);
        return dst;
    }

    [Fact]
    public async Task DownscaledJpegCopy_IsWithinThreshold()
    {
        using var original = Scene(2000, 1500);
        using var small = Resize(original, 400, 300);
        var a = await _hasher.ComputeAsync(Save(original, "a.png", SKEncodedImageFormat.Png, 100), 1);
        var b = await _hasher.ComputeAsync(Save(small, "b.jpg", SKEncodedImageFormat.Jpeg, 60), 1);
        a.Should().MatchRegex("^[0-9a-f]{16}$");
        PerceptualHash.Distance(PerceptualHash.Parse(a!)!.Value, PerceptualHash.Parse(b!)!.Value).Should().BeLessThanOrEqualTo(6);
    }

    [Fact]
    public async Task ExifRotatedOriginal_MatchesPhysicallyRotatedCopy()
    {
        using var landscape = Scene(1600, 1200);
        using var rotated = Rotate90(landscape);
        var tagged = await _hasher.ComputeAsync(Save(landscape, "t.jpg", SKEncodedImageFormat.Jpeg, 90), 6); // EXIF 6 = rotate 90 CW
        var physical = await _hasher.ComputeAsync(Save(rotated, "p.jpg", SKEncodedImageFormat.Jpeg, 90), 1);
        PerceptualHash.Distance(PerceptualHash.Parse(tagged!)!.Value, PerceptualHash.Parse(physical!)!.Value).Should().BeLessThanOrEqualTo(6);
    }

    [Fact]
    public async Task DifferentScene_IsFarApart()
    {
        using var a = Scene(800, 600);
        using var b = Rotate90(Scene(600, 800)); // different composition
        var ha = await _hasher.ComputeAsync(Save(a, "x.png", SKEncodedImageFormat.Png, 100), 1);
        var hb = await _hasher.ComputeAsync(Save(b, "y.png", SKEncodedImageFormat.Png, 100), 1);
        PerceptualHash.Distance(PerceptualHash.Parse(ha!)!.Value, PerceptualHash.Parse(hb!)!.Value).Should().BeGreaterThan(10);
    }

    [Fact]
    public async Task UndecodableFile_ReturnsNull()
    {
        var path = Path.Combine(_dir, "bad.jpg");
        await File.WriteAllBytesAsync(path, [1, 2, 3, 4]);
        (await _hasher.ComputeAsync(path, null)).Should().BeNull();
    }

    public void Dispose() => Directory.Delete(_dir, true);
}
```

(`PerceptualHash` comes from Task 3's pure helper. If you implement Task 1 first, create `PerceptualHash.cs` from Task 3 Step 3 now; it has no dependencies.)

- [ ] **Step 2: Run, expect a compile failure** (`SkiaDHashPerceptualHasher` is not defined): `dotnet test tests/PictureManager.Infrastructure.Tests --filter SkiaDHashPerceptualHasherTests`

- [ ] **Step 3: Extract the shared bitmap ops.** Move `DecodeSafely` and `ApplyOrientation` verbatim from `SkiaSharpThumbnailService` into:

```csharp
namespace PictureManager.Infrastructure.Imaging;

internal static class SkiaBitmapOps
{
    internal static SKBitmap? DecodeSafely(string path) { /* moved body */ }
    internal static SKBitmap ApplyOrientation(SKBitmap source, int orientation) { /* moved body */ }
}
```

Replace the call sites in `SkiaSharpThumbnailService.Generate` with `SkiaBitmapOps.*`. The existing thumbnail tests must stay green.

- [ ] **Step 4: Implement.**

```csharp
namespace PictureManager.Application.Scanning;

public interface IPerceptualHasher
{
    /// <summary>64-bit dHash as 16 lowercase hex chars, computed after applying EXIF orientation; null if undecodable.</summary>
    Task<string?> ComputeAsync(string filePath, int? orientation, CancellationToken cancellationToken = default);
}
```

```csharp
namespace PictureManager.Infrastructure.Scanning;

public sealed class SkiaDHashPerceptualHasher : IPerceptualHasher
{
    private const int Width = 9, Height = 8;
    private static readonly SKSamplingOptions Downsample = new(SKFilterMode.Linear, SKMipmapMode.Linear);

    public Task<string?> ComputeAsync(string filePath, int? orientation, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var decoded = SkiaBitmapOps.DecodeSafely(filePath);
        if (decoded is null) return Task.FromResult<string?>(null);

        using var oriented = SkiaBitmapOps.ApplyOrientation(decoded, ThumbnailResizeCalculator.NormalizeOrientation(orientation));
        using var image = SKImage.FromBitmap(oriented);
        using var small = new SKBitmap(new SKImageInfo(Width, Height, SKColorType.Gray8, SKAlphaType.Opaque));
        using (var canvas = new SKCanvas(small))
            canvas.DrawImage(image, new SKRect(0, 0, Width, Height), Downsample); // mipmaps avoid aliasing on big sources

        ulong hash = 0;
        for (var y = 0; y < Height; y++)
            for (var x = 0; x < Width - 1; x++)
                hash = (hash << 1) | (small.GetPixel(x, y).Red < small.GetPixel(x + 1, y).Red ? 1UL : 0UL);

        return Task.FromResult<string?>(hash.ToString("x16"));
    }
}
```

Check that `ThumbnailResizeCalculator.NormalizeOrientation` accepts `int?`. If it doesn't, pass `orientation ?? 1`.

- [ ] **Step 5: Run the Task 1 tests and the existing thumbnail tests, expect PASS.** If `DownscaledJpegCopy` lands at 7–8, don't loosen the test. Switch the downsample to a 2-step resize (first to 64×64, then 9×8) and re-run.

- [ ] **Step 6: Register in DI** next to `XxHashContentHasher`: `services.AddSingleton<IPerceptualHasher, SkiaDHashPerceptualHasher>();`

- [ ] **Step 7: Commit** `feat: compute 64-bit dHash perceptual hash with SkiaSharp`

---

### Task 2: Store the hash during enrichment, and backfill

**Files:**
- Modify: `src/PictureManager.Application/Scanning/ImageEnrichmentService.cs`
- Modify: `IImageRepository.cs`, `Infrastructure/Persistence/Repositories/ImageRepository.cs`
- Modify: `ScanService.cs` (+ `IScanService.cs`), plus the worker startup that calls `RequeueStalledEnrichmentAsync` (find it with `tokensave_callers` on that method)
- Test: `tests/PictureManager.Application.Tests/Scanning/ImageEnrichmentServiceTests.cs` (extend), `ScanServiceRequeueTests.cs` (extend), `tests/PictureManager.Infrastructure.Tests/Persistence/Repositories/ImageRepositoryTests.cs` (extend)

**Interfaces:**
- Consumes: `IPerceptualHasher` (Task 1)
- Produces: `IImageRepository.GetIdsMissingPerceptualHashAsync(CancellationToken) : Task<IReadOnlyList<int>>`; `IScanService.RequeueMissingPerceptualHashAsync(CancellationToken) : Task<int>`

- [ ] **Step 1: Failing tests (Application, NSubstitute)**
  - `EnrichAsync_ValidImage_StoresPerceptualHash`: the hasher returns `"0123456789abcdef"`, so `image.PerceptualHash` equals it in the `UpdateAsync` argument.
  - `EnrichAsync_HasherReturnsNull_StoresEmptySentinel_AndStillIndexes`: the hasher returns null and the validator returns true, so `PerceptualHash == ""` and `IndexState == Indexed`.
  - `EnrichAsync_InvalidImage_SkipsHasher`: the validator returns false, so the hasher gets no calls and `PerceptualHash` stays null.
  - `RequeueMissingPerceptualHash_ActiveJob_DoesNothing` and `..._EnqueuesIdsUnderOneEnrichingJob`: mirror the existing two `RequeueStalledEnrichment` tests in `ScanServiceRequeueTests.cs` (lines 40–70) exactly, swapping the repository method.
  - **Rescan fills missing hashes:** add three tests in the existing ScanService folder-scan tests (same fixture style). Each runs a scan where the reconciler decides `Unchanged`:
    - `Scan_UnchangedIndexedImageWithoutPerceptualHash_IsEnqueuedAndCounted`: existing image with `IndexState = Indexed`, `PerceptualHash = null`. It gets `_enrichmentQueue.Enqueue(jobId, id)` and `FilesFound` includes it.
    - `Scan_UnchangedImageWithPerceptualHash_IsNotEnqueued`: `PerceptualHash = "0123456789abcdef"`. No enqueue, and `FilesFound` is unchanged.
    - `Scan_UnchangedImageWithEmptySentinel_IsNotEnqueued`: `PerceptualHash = ""` (undecodable), so it isn't retried on every rescan.
- [ ] **Step 2: Failing Infrastructure test** `GetIdsMissingPerceptualHashAsync_ReturnsIndexedVisibleWithNullHashOnly`: seed one each of Indexed+null (returned), Indexed+`""`, Indexed+hex, Pending+null, Invalid+null, and Indexed+null+missing. Only the first is returned. Copy the setup style of `GetPendingImageIdsAsync_ReturnsOnly…` (line 103).
- [ ] **Step 3: Run, expect FAIL.**
- [ ] **Step 4: Implement**
  - In `EnrichAsync`, after `isValid` is computed: `var perceptual = isValid ? await _perceptualHasher.ComputeAsync(physicalPath, exif.Orientation, cancellationToken) ?? "" : null;` then `image.PerceptualHash = perceptual;`. Add `IPerceptualHasher` to the constructor.
  - Optional optimisation (do it only if enrichment throughput measurably drops): `SkiaImageValidator` already decodes the full bitmap, so validation and hashing could share one decode. For example, the hasher's non-null result implies valid, and `IImageValidator` stays as the fallback for when the hasher isn't registered. Keep the two interfaces separate for now; measure first.
  - Repository: `_dbContext.Images.AsNoTracking().WhereVisible().Where(i => i.IndexState == IndexState.Indexed && i.PerceptualHash == null).OrderBy(i => i.Id).Select(i => i.Id).ToListAsync(ct)`. Check that `WhereVisible` also covers "active folder", which `GetPendingImageIdsAsync` applies separately. If it doesn't, copy that filter.
  - `ScanService.RequeueMissingPerceptualHashAsync`: an identical copy of `RequeueStalledEnrichmentAsync` (line 151) using the new repo method. Re-running full `EnrichAsync` is intentional: the decode dominates the cost anyway, and it keeps one code path.
  - Worker startup: call it right after `RequeueStalledEnrichmentAsync`, only when that returned 0, so the two don't compete for the "no active job" slot.
  - **Folder rescan** (`ScanService.cs`, the `case ReconcileAction.Unchanged:` branch at ~line 358). Replace the bare `break` with:

    ```csharp
    case ReconcileAction.Unchanged:
        // Unchanged files are normally not re-enriched. Exception: indexed before perceptual
        // hashing existed (PerceptualHash null; "" means tried-and-undecodable, don't retry).
        // When enqueued it must count toward FilesFound (see completion check note below).
        if (existingImage!.IndexState == IndexState.Indexed && existingImage.PerceptualHash is null)
        {
            _enrichmentQueue.Enqueue(scanJobId, existingImage.Id);
            filesFound++;
        }
        // Otherwise not enqueued, so it must not count toward FilesFound: FilesFound drives the
        // background service's FilesEnriched >= FilesFound completion check.
        break;
    ```

    So hashes get filled two ways: automatically at startup (the backfill job), and on demand when the user rescans a folder. Both end up in the same `EnrichAsync`, and once a hash is set (hex or `""`) neither path picks the image up again.
- [ ] **Step 5: Run, expect PASS.** Fix any existing `ImageEnrichmentService` tests whose constructor calls now need the extra substitute.
- [ ] **Step 6: Commit** `feat: store perceptual hash on enrichment and backfill indexed images`

---

### Task 3: Pure hash helpers and clustering

**Files:**
- Create: `src/PictureManager.Application/Duplicates/PerceptualHash.cs`, `SimilarityClusterer.cs`
- Test: `tests/PictureManager.Application.Tests/Duplicates/PerceptualHashTests.cs`, `SimilarityClustererTests.cs`

**Interfaces:**
- Produces:
  - `PerceptualHash.Parse(string? s) : ulong?` (null for null, `""` or malformed input)
  - `PerceptualHash.Distance(ulong a, ulong b) : int`
  - `PerceptualHash.IsDegenerate(ulong h) : bool`
  - `SimilarityClusterer.Cluster(IReadOnlyList<(int Id, ulong Hash)> items, int threshold) : IReadOnlyList<IReadOnlyList<int>>`, which returns only groups of size ≥ 2. Ids within a group are in ascending order, and groups are ordered by size descending, then by smallest id ascending.

- [ ] **Step 1: Failing tests**

```csharp
[Theory]
[InlineData(null)] [InlineData("")] [InlineData("xyz")] [InlineData("0123")]
public void Parse_Invalid_ReturnsNull(string? s) => PerceptualHash.Parse(s).Should().BeNull();

[Fact] public void Parse_Hex_RoundTrips() => PerceptualHash.Parse("00000000000000ff").Should().Be(0xFFUL);
[Fact] public void Distance_CountsDifferingBits() => PerceptualHash.Distance(0b1011, 0b0001).Should().Be(2);
[Theory] [InlineData(0UL)] [InlineData(ulong.MaxValue)] [InlineData(0b11UL)]
public void IsDegenerate_NearUniform(ulong h) => PerceptualHash.IsDegenerate(h).Should().BeTrue();
```

```csharp
[Fact]
public void Cluster_GroupsWithinThreshold_ExcludesFarAndSingletons()
{
    ulong baseHash = 0x0F0F_3C3C_5A5A_A5A5;
    var items = new List<(int, ulong)> {
        (1, baseHash), (2, baseHash ^ 0b111), (3, ~baseHash), (4, baseHash ^ (1UL << 63)) };
    SimilarityClusterer.Cluster(items, 6).Should().BeEquivalentTo(
        new[] { new[] { 1, 2, 4 } }, o => o.WithStrictOrdering());
}

[Fact]
public void Cluster_ChainsTransitively()
{
    ulong a = 0x0F0F_3C3C_5A5A_A5A5, b = a ^ 0x3F, c = b ^ (0x3FUL << 32); // a~b=6, b~c=6, a~c=12
    SimilarityClusterer.Cluster(new List<(int, ulong)> { (1, a), (2, b), (3, c) }, 6)
        .Should().ContainSingle().Which.Should().Equal(1, 2, 3);
}

[Fact]
public void Cluster_SkipsDegenerateHashes()
{
    SimilarityClusterer.Cluster(new List<(int, ulong)> { (1, 0UL), (2, 0UL), (3, 1UL) }, 6).Should().BeEmpty();
}

[Fact]
public void Cluster_FindsMatchesAcrossAllBands_Randomized()
{
    var rng = new Random(42);
    var items = new List<(int, ulong)>();
    for (var i = 0; i < 500; i++) items.Add((i, (ulong)rng.NextInt64() | 0x0101_0101_0101_0101));
    // brute-force oracle
    var expectedPairs = items.SelectMany(x => items.Where(y => y.Item1 > x.Item1
        && PerceptualHash.Distance(x.Item2, y.Item2) <= 6).Select(y => (x.Item1, y.Item1))).ToList();
    var groups = SimilarityClusterer.Cluster(items, 6);
    foreach (var (p, q) in expectedPairs)
        groups.Should().Contain(g => g.Contains(p) && g.Contains(q));
}
```

- [ ] **Step 2: Run, expect FAIL.**
- [ ] **Step 3: Implement**

```csharp
namespace PictureManager.Application.Duplicates;

public static class PerceptualHash
{
    public static ulong? Parse(string? s) =>
        s is { Length: 16 } && ulong.TryParse(s, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var v) ? v : null;
    public static int Distance(ulong a, ulong b) => BitOperations.PopCount(a ^ b);
    /// <summary>Flat/blank images hash to ~all-0 or ~all-1 bits and would all match each other.</summary>
    public static bool IsDegenerate(ulong h) => BitOperations.PopCount(h) is <= 2 or >= 62;
}
```

```csharp
namespace PictureManager.Application.Duplicates;

/// <summary>Union-find over Hamming distance. Candidates come from 8 one-byte bands: with threshold ≤ 7,
/// two hashes within the threshold must agree exactly on at least one band (pigeonhole).</summary>
public static class SimilarityClusterer
{
    public const int MaxThreshold = 7;

    public static IReadOnlyList<IReadOnlyList<int>> Cluster(IReadOnlyList<(int Id, ulong Hash)> items, int threshold)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(threshold, MaxThreshold);
        var list = items.Where(i => !PerceptualHash.IsDegenerate(i.Hash)).ToList();
        var parent = Enumerable.Range(0, list.Count).ToArray();
        int Find(int x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }

        for (var band = 0; band < 8; band++)
        {
            var buckets = new Dictionary<byte, List<int>>();
            for (var i = 0; i < list.Count; i++)
            {
                var key = (byte)(list[i].Hash >> (band * 8));
                if (!buckets.TryGetValue(key, out var b)) buckets[key] = b = [];
                b.Add(i);
            }
            foreach (var bucket in buckets.Values)
                for (var x = 0; x < bucket.Count; x++)
                    for (var y = x + 1; y < bucket.Count; y++)
                        if (Find(bucket[x]) != Find(bucket[y])
                            && PerceptualHash.Distance(list[bucket[x]].Hash, list[bucket[y]].Hash) <= threshold)
                            parent[Find(bucket[x])] = Find(bucket[y]);
        }

        return Enumerable.Range(0, list.Count)
            .GroupBy(Find)
            .Where(g => g.Count() > 1)
            .Select(g => (IReadOnlyList<int>)g.Select(i => list[i].Id).Order().ToList())
            .OrderByDescending(g => g.Count).ThenBy(g => g[0])
            .ToList();
    }
}
```

- [ ] **Step 4: Run, expect PASS.**
- [ ] **Step 5: Commit** `feat: perceptual hash helpers and band-indexed similarity clustering`

---

### Task 4: Similar-groups service, repository and endpoint

**Files:**
- Modify: `DuplicateModels.cs`, `IDuplicateService.cs`, `DuplicateService.cs`, `IImageQueryRepository.cs`, `ImageQueryRepository.cs`, `DuplicateEndpoints.cs`
- Test: `tests/PictureManager.Application.Tests/Duplicates/DuplicateServiceTests.cs` (extend), `tests/PictureManager.Infrastructure.Tests/.../ImageQueryRepositoryTests.cs` (extend), `tests/PictureManager.Api.Tests/Smoke/ApiSmokeTests.cs` (one smoke test)

**Interfaces:**
- Consumes: `SimilarityClusterer.Cluster`, `PerceptualHash.Parse` (Task 3)
- Produces:
  - `public sealed record PerceptualHashRow(int Id, string PerceptualHash);`
  - `public sealed record SimilarGroup(string Key, int Count, int MaxDistance, IReadOnlyList<DuplicateImageItem> Images);`. `Key` = `"s" + smallest id` (stable React key). Images are ordered by pixel area desc, then `FileSize` desc, then id, so the "best" copy comes first.
  - `public sealed record SimilarCursor([property: JsonPropertyName("o")] int Offset);`
  - `DuplicateImageItem` gains `long FileSize` as its last parameter. `ImageRow` may need `FileSize` too; add it via `ImageProjections.ToRow` and `GetDuplicateMembersAsync`.
  - `IImageQueryRepository.GetPerceptualHashesAsync(CancellationToken) : Task<IReadOnlyList<PerceptualHashRow>>`: visible rows where `PerceptualHash` is not null and not `""`.
  - `IImageQueryRepository.GetMembersByIdsAsync(IReadOnlyCollection<int> ids, CancellationToken) : Task<IReadOnlyList<DuplicateMemberRow>>`: the same projection as `GetDuplicateMembersAsync`, keyed by id.
  - `IDuplicateService.ListSimilarAsync(int? threshold, string? cursor, int? limit, CancellationToken) : Task<Result<PagedResult<SimilarGroup>>>`
  - `GET /duplicates/similar?threshold=&cursor=&limit=`

- [ ] **Step 1: Failing service tests**
  - `ListSimilarAsync_ThresholdOutOfRange_ReturnsInvalid` with `[InlineData(-1)]` and `[InlineData(8)]` gives `Result.Invalid("threshold", …)`.
  - `ListSimilarAsync_GroupsAndOrdersLargestFirst`: the repo returns three near hashes and one far one, and members of different sizes. Expect one group, with images ordered 4000×3000 → 1600×1200 → 800×600 and `MaxDistance` equal to the largest pairwise distance.
  - `ListSimilarAsync_PagesByOffset`: 3 groups with `limit: 2` gives 2 items and a cursor. The second call with the cursor gives 1 item and a null cursor.
  - `ListSimilarAsync_MalformedCursor_ReturnsInvalid`
- [ ] **Step 2: Failing repository test** `GetPerceptualHashesAsync_ExcludesNullEmptyAndHidden`.
- [ ] **Step 3: Run, expect FAIL.**
- [ ] **Step 4: Implement `ListSimilarAsync`:** validate the threshold (default 6, range 0–`SimilarityClusterer.MaxThreshold`), the limit (reuse `DefaultLimit`/`MaxLimit`) and the cursor (`CursorCodec.TryDecode<SimilarCursor>`). Then:
  1. `rows = await _images.GetPerceptualHashesAsync(ct)`, then parse them, dropping unparsable ones.
  2. `groups = SimilarityClusterer.Cluster(parsed, threshold)`, then `page = groups.Skip(offset).Take(limit)`.
  3. `members = await _images.GetMembersByIdsAsync(page.SelectMany(g => g).ToList(), ct)` and map each with the existing `ToItem`.
  4. `MaxDistance` is the max pairwise `Distance` within a group (groups are small).
  5. `nextCursor = offset + limit < groups.Count ? Encode(new SimilarCursor(offset + limit)) : null`.

  Offset paging over a recomputed clustering is acceptable here because the cluster order is deterministic. A rescan between pages can shift groups, which is the same trade-off a page refresh would have. Recomputing per request is O(n) in memory (100k images ≈ 1.6 MB of hashes), so no cache is needed in v1.
- [ ] **Step 5: Endpoint:** `user.MapGet("/duplicates/similar", ListSimilarAsync);` with the handler shape copied from the existing `ListAsync`, adding `int? threshold`.
- [ ] **Step 6: Smoke test:** `GET /api/.../duplicates/similar` returns 200 with an empty `items` list on the test DB, and `?threshold=99` returns 400.
- [ ] **Step 7: Run `dotnet test`, expect all PASS.**
- [ ] **Step 8: Commit** `feat: GET /duplicates/similar groups resized and re-encoded copies`

---

### Task 5: Frontend Exact / Similar toggle

**Files:**
- Modify: `web/src/api/types.ts`, `web/src/api/queries.ts`, `web/src/views/DuplicatesView.tsx`, `web/src/routing/urlState.ts` (if mode parsing lives there)
- Test: `web/src/views/DuplicatesView.test.tsx` (extend), `web/src/api/albumQueries.test.tsx` or `queries.test.tsx` (extend)

**Interfaces:**
- Consumes: `GET /duplicates/similar` (Task 4)
- Produces:
  - `type SimilarGroup = { key: string; count: number; maxDistance: number; images: ImageListItem[] }`
  - `useSimilarDuplicates(enabled: boolean)`, an infinite query following the `useDuplicates`/`pageQuery` pattern in `queries.ts:100-160`
  - `ImageListItem` gains `fileSize?: number`

- [ ] **Step 1: Failing tests** (MSW handlers, same style as the existing DuplicatesView tests):
  - `"switches to similar mode and shows the largest copy first with its resolution"`: click the "Similar" toggle. The URL gets `mode=similar`, the header reads "3 similar photos", and the first tile's caption includes `4000 × 3000`.
  - `"similar mode says when there are none"`: shows "No similar photos found."
  - `"a star change updates copies inside cached similar groups"`: extend the existing favourite-cache updater so it also patches the `['duplicates','similar']` query data, mirroring the test at `albumQueries.test.tsx:72`.
- [ ] **Step 2: Run `npm run test -- DuplicatesView`, expect FAIL.**
- [ ] **Step 3: Implement**
  - Add an MUI `ToggleButtonGroup` (Exact | Similar) to the header box in `DuplicatesView` (lines 129–136), bound to the `mode` search param.
  - Normalise both modes to `{ key, label, images }[]` before rendering. Exact uses `key: contentHash, label: "${count} copies"`; similar uses `label: "${count} similar photos"`. Everything else (selection, viewer, paging, scroll loading) stays shared, which keeps the diff small.
  - In similar mode, the tile caption is `${folderPath} · ${width} × ${height}`, and the first tile in each group gets a small "Largest" chip.
  - Subtitle copy in similar mode: "Photos that look the same, including resized or re-encoded copies. Delete extra copies on disk, then rescan."
  - Find the favourites cache updater in `web/src/api/favorites.ts` and add the similar query key alongside the duplicates one.
- [ ] **Step 4: Run `npm run test` and `npm run build`, expect PASS.**
- [ ] **Step 5: Commit** `feat: Exact/Similar toggle on the Duplicates view`

---

## Verification (end-to-end)

1. `dotnet test` and `npm run test` both pass.
2. Run the app (the `run` skill / docker). On startup the worker log shows a Scan job in `Enriching` covering N images (the backfill). After it finishes, `SELECT count(*) FROM "Images" WHERE "PerceptualHash" IS NULL AND "IndexState" = 'Indexed'` returns 0.
2b. Rescan path: set `"PerceptualHash" = NULL` for one folder's images in SQL, then rescan only that folder from the UI. The job's `FilesFound` equals that folder's image count, and afterwards the hashes are set again. A second rescan reports `FilesFound = 0`.
3. Put a test photo plus two derived copies into a scanned folder: one resized to 25% and saved as JPEG q60, one converted to PNG. Then rescan the folder.
4. In Duplicates, the **Exact** tab doesn't show them. The **Similar** tab shows one group of 3, with the original first and a "Largest" chip.
5. A folder of unrelated photos taken seconds apart (burst shots) may group together at threshold 6. Note how many such false positives there are on the real library, and adjust the default in `DuplicateService` if needed. That's the only tuning knob.

## Relationship to the planned face-recognition feature

The perceptual hash deliberately lives in **scan enrichment**, not in a future face-detection service:
- It's cheap and needed for every image; face detection is heavy, may lag or be disabled, and duplicate search must not depend on it.
- It's a file-content fact like `ContentHash` and EXIF, so it shares their invalidation lifecycle (New / Modified / Unchanged + requeue) for free.
- Face results get recomputed on model upgrades; the hash never does.

Guidance for the face feature: give it its own job, queue and `Image`-side state. To avoid a third full decode, run detection on the cached preview derivative from `IThumbnailService` (already oriented and downscaled) instead of the original file.

## Out of scope (v1)

- Crops, mirrored/flipped copies, heavy edits (dHash doesn't survive these).
- A user-adjustable threshold slider in the UI (the API already accepts `threshold`).
- Caching cluster results or a Postgres-side index (only needed past roughly 500k images).
