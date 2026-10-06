using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using PictureManager.Application.Faces;
using PictureManager.Application.Thumbnails;
using PictureManager.Infrastructure.Imaging;
using SkiaSharp;

namespace PictureManager.Infrastructure.Faces;

/// <summary>
/// InsightFace buffalo_l in-process: SCRFD-10G detection, five-point alignment, ArcFace R50 embedding. Models load
/// lazily on first use, so a missing model folder only fails face jobs, never startup.
/// </summary>
public sealed class OnnxFaceAnalyzer : IFaceAnalyzer, IDisposable
{
    private const string DetectorFile = "det_10g.onnx";
    private const string RecognizerFile = "w600k_r50.onnx";
    private const int MaxDecodeSide = 1600;
    private const float NmsThreshold = 0.4f;
    private static readonly SKSamplingOptions Sampling = new(SKFilterMode.Linear, SKMipmapMode.Linear);

    private readonly FaceRecognitionOptions _options;
    private readonly SemaphoreSlim _inference;

    // Assigned only after a successful load: a failed load (models missing or corrupt) is retried on the next
    // call, so models dropped in later are picked up without a restart.
    private readonly Lock _loadLock = new();
    private volatile Sessions? _sessions;

    public OnnxFaceAnalyzer(FaceRecognitionOptions options)
    {
        _options = options;
        _inference = new SemaphoreSlim(Math.Max(1, options.InferenceConcurrency));
    }

    public FaceModelDescriptor Model => GetSessions().Descriptor;

    public async Task<FaceAnalysisResult?> AnalyzeAsync(
        string imagePath, int? orientation, FaceDetectionPreset preset = FaceDetectionPreset.Fast, CancellationToken cancellationToken = default)
    {
        var sessions = GetSessions();
        var settings = _options.For(preset);
        cancellationToken.ThrowIfCancellationRequested();

        // Decoding is NAS I/O + CPU and runs outside the inference gate, so reads overlap model execution.
        using var decoded = SkiaBitmapOps.DecodeDownsampled(imagePath, MaxDecodeSide);
        if (decoded is null)
            return null;
        using var image = SkiaBitmapOps.ApplyOrientation(decoded, ThumbnailResizeCalculator.NormalizeOrientation(orientation));

        await _inference.WaitAsync(cancellationToken);
        try
        {
            var detections = Detect(sessions.Detector, image, settings.DetectorInputSize)
                .Where(d => Math.Min(d.X2 - d.X1, d.Y2 - d.Y1) >= settings.MinFaceSizePx)
                .ToList();
            if (detections.Count == 0)
                return new FaceAnalysisResult(Array.Empty<DetectedFace>());

            var aligned = detections.Select(d => FaceAligner.Warp(image, d.Landmarks)).ToList();
            try
            {
                var embeddings = Embed(sessions.Recognizer, aligned);
                var faces = new List<DetectedFace>(detections.Count);
                for (var i = 0; i < detections.Count; i++)
                {
                    var d = detections[i];
                    var x1 = Math.Clamp(d.X1 / image.Width, 0f, 1f);
                    var y1 = Math.Clamp(d.Y1 / image.Height, 0f, 1f);
                    var x2 = Math.Clamp(d.X2 / image.Width, 0f, 1f);
                    var y2 = Math.Clamp(d.Y2 / image.Height, 0f, 1f);
                    var quality = FaceQuality.Compute(d.Score, Math.Min(d.X2 - d.X1, d.Y2 - d.Y1), FaceQuality.LaplacianVariance(aligned[i]));
                    faces.Add(new DetectedFace(x1, y1, x2 - x1, y2 - y1, d.Score, quality, embeddings[i]));
                }
                return new FaceAnalysisResult(faces);
            }
            finally
            {
                foreach (var crop in aligned)
                    crop.Dispose();
            }
        }
        finally
        {
            _inference.Release();
        }
    }

    private List<RawDetection> Detect(InferenceSession detector, SKBitmap image, int size)
    {
        // Letterbox to the top-left of a 640² canvas, keeping the aspect ratio (as InsightFace does).
        var scale = (float)size / Math.Max(image.Width, image.Height);
        using var input = new SKBitmap(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(input))
        {
            canvas.Clear(SKColors.Black);
            using var source = SKImage.FromBitmap(image);
            canvas.DrawImage(source, new SKRect(0, 0, image.Width * scale, image.Height * scale), Sampling);
        }

        var tensor = ToTensor(new[] { input }, size, mean: 127.5f, std: 128f);
        using var results = detector.Run(new[] { NamedOnnxValue.CreateFromTensor(detector.InputMetadata.Keys.First(), tensor) });

        // det_10g output order: scores (strides 8, 16, 32), then boxes, then landmarks.
        var outputs = results.Select(r => r.AsEnumerable<float>().ToArray()).ToList();
        var raw = ScrfdDecoder.Decode(size, outputs.GetRange(0, 3), outputs.GetRange(3, 3), outputs.GetRange(6, 3), _options.DetectionThreshold);
        return ScrfdDecoder.NonMaxSuppression(raw, NmsThreshold).Select(d => d.Scale(1 / scale)).ToList();
    }

    private static List<float[]> Embed(InferenceSession recognizer, IReadOnlyList<SKBitmap> aligned)
    {
        var tensor = ToTensor(aligned, FaceAligner.Size, mean: 127.5f, std: 127.5f);
        using var results = recognizer.Run(new[] { NamedOnnxValue.CreateFromTensor(recognizer.InputMetadata.Keys.First(), tensor) });
        var flat = results.First().AsEnumerable<float>().ToArray();
        var dimensions = flat.Length / aligned.Count;

        return Enumerable.Range(0, aligned.Count).Select(i =>
        {
            var vector = flat.AsSpan(i * dimensions, dimensions).ToArray();
            var norm = (float)Math.Sqrt(vector.Sum(v => v * v));
            for (var j = 0; j < vector.Length; j++)
                vector[j] /= norm;
            return vector;
        }).ToList();
    }

    /// <summary>NCHW float tensor, RGB, (pixel − mean) / std. Bitmaps must be Rgba8888 and size×size.</summary>
    private static DenseTensor<float> ToTensor(IReadOnlyList<SKBitmap> bitmaps, int size, float mean, float std)
    {
        var tensor = new DenseTensor<float>(new[] { bitmaps.Count, 3, size, size });
        for (var n = 0; n < bitmaps.Count; n++)
        {
            var pixels = bitmaps[n].GetPixelSpan();
            var row = bitmaps[n].RowBytes;
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    var i = y * row + x * 4;
                    tensor[n, 0, y, x] = (pixels[i] - mean) / std;
                    tensor[n, 1, y, x] = (pixels[i + 1] - mean) / std;
                    tensor[n, 2, y, x] = (pixels[i + 2] - mean) / std;
                }
        }
        return tensor;
    }

    private Sessions GetSessions()
    {
        if (_sessions is { } loaded)
            return loaded;
        lock (_loadLock)
            return _sessions ??= LoadSessions();
    }

    private Sessions LoadSessions()
    {
        var detectorPath = Path.Combine(_options.ModelDirectory, DetectorFile);
        var recognizerPath = Path.Combine(_options.ModelDirectory, RecognizerFile);
        if (!File.Exists(detectorPath) || !File.Exists(recognizerPath))
            throw new FaceModelUnavailableException(
                $"Face recognition models not found: expected {DetectorFile} and {RecognizerFile} in '{_options.ModelDirectory}'. " +
                "Run tools/download-face-models.ps1 (development) or rebuild the Docker image.");

        using var sessionOptions = new SessionOptions
        {
            // Parallelism comes from processing several images at once, not from threads inside one run.
            IntraOpNumThreads = 1,
            InterOpNumThreads = 1,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            // w600k_r50 declares its output as [1,512] although the batch input is dynamic, so every batched run
            // logs a shape-mismatch warning. The batched output is correct ([N,512]), so only errors are logged.
            LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR
        };

        using var sha = SHA256.Create();
        foreach (var path in new[] { detectorPath, recognizerPath })
        {
            var bytes = File.ReadAllBytes(path);
            sha.TransformBlock(bytes, 0, bytes.Length, null, 0);
        }
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        var hash = Convert.ToHexStringLower(sha.Hash!);

        var detector = OpenSession(detectorPath, sessionOptions);
        try
        {
            var recognizer = OpenSession(recognizerPath, sessionOptions);
            return new Sessions(detector, recognizer, new FaceModelDescriptor("insightface-buffalo_l", "det_10g+w600k_r50", 512, hash));
        }
        catch
        {
            detector.Dispose();
            throw;
        }
    }

    /// <summary>A corrupt or incompatible model fails the job (FaceModelUnavailableException), not each image.</summary>
    private static InferenceSession OpenSession(string path, SessionOptions sessionOptions)
    {
        try
        {
            return new InferenceSession(path, sessionOptions);
        }
        catch (Exception ex) when (ex is OnnxRuntimeException or DllNotFoundException or EntryPointNotFoundException or TypeInitializationException)
        {
            throw new FaceModelUnavailableException(
                $"Face recognition model could not be loaded: {Path.GetFileName(path)} in '{Path.GetDirectoryName(path)}' ({ex.Message}). " +
                "Re-run tools/download-face-models.ps1 (development) or rebuild the Docker image.", ex);
        }
    }

    public void Dispose()
    {
        lock (_loadLock)
        {
            _sessions?.Detector.Dispose();
            _sessions?.Recognizer.Dispose();
            _sessions = null;
        }
        _inference.Dispose();
    }

    private sealed record Sessions(InferenceSession Detector, InferenceSession Recognizer, FaceModelDescriptor Descriptor);
}
