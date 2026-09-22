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

    [Fact]
    public async Task ComputeAsync_FilesDifferingOnlyInMiddleRegion_ProduceSameHash()
    {
        var pathA = Path.GetTempFileName();
        var pathB = Path.GetTempFileName();
        try
        {
            var length = 200_000;
            var bytesA = new byte[length];
            new Random(11).NextBytes(bytesA);
            var bytesB = (byte[])bytesA.Clone();

            // Mutate only the middle region (strictly between the 64KB head and 64KB tail windows)
            for (var i = 70_000; i < 130_000; i++)
            {
                bytesB[i] = (byte)(bytesB[i] ^ 0xFF);
            }

            await File.WriteAllBytesAsync(pathA, bytesA);
            await File.WriteAllBytesAsync(pathB, bytesB);

            var hasher = new XxHashContentHasher();
            var hashA = await hasher.ComputeAsync(pathA, length);
            var hashB = await hasher.ComputeAsync(pathB, length);

            hashA.Should().Be(hashB);
        }
        finally
        {
            File.Delete(pathA);
            File.Delete(pathB);
        }
    }
}
