using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Application.Scanning;
using SkiaSharp;

namespace PictureManager.Infrastructure.Scanning;

public sealed class SkiaImageValidator : IImageValidator
{
    public Task<bool> IsValidAsync(string filePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var stream = File.OpenRead(filePath);
            using var bitmap = SKBitmap.Decode(stream);
            return Task.FromResult(bitmap is not null);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException)
        {
            return Task.FromResult(false);
        }
    }
}
