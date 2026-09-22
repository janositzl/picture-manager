using System;
using System.IO;
using System.IO.Hashing;
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Application.Scanning;

namespace PictureManager.Infrastructure.Scanning;

public sealed class XxHashContentHasher : IContentHasher
{
    private const int SampleSize = 64 * 1024;

    public async Task<string> ComputeAsync(string filePath, long fileSize, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true);

        var hasher = new XxHash64();
        hasher.Append(BitConverter.GetBytes(fileSize));

        var headSize = (int)Math.Min(SampleSize, fileSize);
        var headBuffer = new byte[headSize];
        await ReadExactAsync(stream, headBuffer, cancellationToken);
        hasher.Append(headBuffer);

        if (fileSize > SampleSize)
        {
            var tailSize = (int)Math.Min(SampleSize, fileSize - headSize);
            stream.Seek(-tailSize, SeekOrigin.End);
            var tailBuffer = new byte[tailSize];
            await ReadExactAsync(stream, tailBuffer, cancellationToken);
            hasher.Append(tailBuffer);
        }

        return Convert.ToHexString(hasher.GetCurrentHash());
    }

    private static async Task ReadExactAsync(FileStream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), cancellationToken);
            if (read == 0)
                break;
            offset += read;
        }
    }
}
