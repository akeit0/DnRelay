namespace DnRelay.Execution;

sealed class BuildLockSet : IAsyncDisposable, IDisposable
{
    private readonly List<BuildLock> locks;
    public TimeSpan TotalWaitDuration { get; }

    private BuildLockSet(List<BuildLock> locks)
    {
        this.locks = locks;
        TotalWaitDuration = TimeSpan.FromTicks(locks.Sum(static item => item.WaitDuration.Ticks));
    }

    public static async Task<BuildLockSet> AcquireAsync(
        IReadOnlyList<string> lockPaths,
        string command,
        string targetPath,
        DateTimeOffset startTime,
        CancellationToken cancellationToken,
        string lockDisplayName)
    {
        var acquiredLocks = new List<BuildLock>(lockPaths.Count);
        try
        {
            foreach (var lockPath in lockPaths.OrderBy(static path => path, StringComparer.Ordinal))
            {
                acquiredLocks.Add(await BuildLock.AcquireAsync(lockPath, command, targetPath, startTime, cancellationToken, lockDisplayName));
            }

            return new BuildLockSet(acquiredLocks);
        }
        catch
        {
            for (var index = acquiredLocks.Count - 1; index >= 0; index--)
            {
                acquiredLocks[index].Dispose();
            }

            throw;
        }
    }

    public void Dispose()
    {
        for (var index = locks.Count - 1; index >= 0; index--)
        {
            locks[index].Dispose();
        }
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
