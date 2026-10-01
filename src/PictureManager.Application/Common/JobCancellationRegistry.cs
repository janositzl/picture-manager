using System;
using System.Collections.Concurrent;
using System.Threading;

namespace PictureManager.Application.Common;

public sealed class JobCancellationRegistry : IJobCancellationRegistry
{
    private readonly ConcurrentDictionary<int, CancellationTokenSource> _sources = new();

    public CancellationToken Register(int jobId) =>
        _sources.GetOrAdd(jobId, _ => new CancellationTokenSource()).Token;

    public bool Cancel(int jobId)
    {
        if (!_sources.TryGetValue(jobId, out var source))
            return false;

        try
        {
            source.Cancel();
            return true;
        }
        catch (ObjectDisposedException)
        {
            // Released (job finished) between the lookup and the cancel.
            return false;
        }
    }

    public void Release(int jobId)
    {
        if (_sources.TryRemove(jobId, out var source))
            source.Dispose();
    }
}
