using System;
using System.Collections.Generic;
using System.Linq;

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
