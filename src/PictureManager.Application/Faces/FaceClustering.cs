using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Application.Repositories;

namespace PictureManager.Application.Faces;

/// <summary>Pure clustering rules, unit-testable without a database.</summary>
public static class FaceClustering
{
    /// <summary>
    /// The person that wins among the neighbours within maxDistance: at least minVotes of them, and strictly more
    /// than half of those within distance (a tie is no decision). excludedPersonId (the one the user rejected for this
    /// face) never wins or votes. Null when nobody qualifies.
    /// </summary>
    public static int? MajorityPerson(IReadOnlyList<FaceNeighbor> neighbors, float maxDistance, int minVotes, int? excludedPersonId = null)
    {
        var within = neighbors.Where(n => n.PersonId is not null && n.PersonId != excludedPersonId && n.Distance <= maxDistance).ToList();
        if (within.Count == 0)
            return null;

        var best = within.GroupBy(n => n.PersonId!.Value).OrderByDescending(g => g.Count()).First();
        return best.Count() >= minVotes && best.Count() * 2 > within.Count ? best.Key : null;
    }

    /// <summary>
    /// DBSCAN seeded from seedIds. neighbors(id) returns ids within eps; clusters start only at seeds but expand into
    /// any id it returns (seed or not), so a new face can group with older faces. A point is core when it has
    /// ≥ minPoints − 1 neighbours (so the cluster, including itself, reaches minPoints). Clusters smaller than
    /// minPoints are dropped as noise. Every cluster contains at least one seed.
    /// </summary>
    public static async Task<List<List<int>>> DbscanAsync(
        IReadOnlyList<int> seedIds, Func<int, Task<IReadOnlyList<int>>> neighbors, int minPoints, CancellationToken cancellationToken)
    {
        var visited = new HashSet<int>();
        var clustered = new HashSet<int>();
        var clusters = new List<List<int>>();

        foreach (var id in seedIds)
        {
            if (!visited.Add(id))
                continue;

            var seeds = (await neighbors(id)).ToList();
            if (seeds.Count + 1 < minPoints)
                continue;

            var cluster = new List<int> { id };
            clustered.Add(id);
            var queue = new Queue<int>(seeds);
            while (queue.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var current = queue.Dequeue();
                if (clustered.Add(current))
                    cluster.Add(current);
                if (!visited.Add(current))
                    continue;

                var next = await neighbors(current);
                if (next.Count + 1 >= minPoints)
                    foreach (var n in next.Where(n => !clustered.Contains(n)))
                        queue.Enqueue(n);
            }

            if (cluster.Count >= minPoints)
                clusters.Add(cluster);
        }

        return clusters;
    }
}
