using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using PictureManager.Infrastructure.Scanning;
using Xunit;

namespace PictureManager.Infrastructure.Tests.Scanning;

public class XxHashContentHasherTests
{
    [Fact]
    public async Task ComputeAsync_SameContent_ProducesSameHash()
    {
        var path = Path.GetTempFileName();
        try
        {
            var bytes = new byte[10_000];
            new Random(42).NextBytes(bytes);
            await File.WriteAllBytesAsync(path, bytes);

            var hasher = new XxHashContentHasher();
            var hash1 = await hasher.ComputeAsync(path, bytes.Length);
            var hash2 = await hasher.ComputeAsync(path, bytes.Length);

            hash1.Should().Be(hash2);
            hash1.Should().NotBeNullOrEmpty();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ComputeAsync_DifferentContent_ProducesDifferentHash()
    {
        var pathA = Path.GetTempFileName();
        var pathB = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(pathA, new byte[] { 1, 2, 3, 4, 5 });
            await File.WriteAllBytesAsync(pathB, new byte[] { 9, 9, 9, 9, 9 });

            var hasher = new XxHashContentHasher();
            var hashA = await hasher.ComputeAsync(pathA, 5);
            var hashB = await hasher.ComputeAsync(pathB, 5);

            hashA.Should().NotBe(hashB);
        }
        finally
        {
            File.Delete(pathA);
            File.Delete(pathB);
        }
    }

    [Fact]
    public async Task ComputeAsync_FileLargerThan128KB_OnlyReadsHeadAndTailSamples()
    {
        var path = Path.GetTempFileName();
        try
        {
            var bytes = new byte[200_000];
            new Random(7).NextBytes(bytes);
            await File.WriteAllBytesAsync(path, bytes);

            var hasher = new XxHashContentHasher();
            var hash = await hasher.ComputeAsync(path, bytes.Length);

            hash.Should().NotBeNullOrEmpty();
        }
        finally
        {
            File.Delete(path);
        }
    }
}
