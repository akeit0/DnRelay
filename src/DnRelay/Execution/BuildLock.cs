using System.Text.Json;
using DnRelay.Models;
using DnRelay.Serialization;

namespace DnRelay.Execution;

sealed class BuildLock : IAsyncDisposable, IDisposable
{
    private readonly FileStream stream;
    private readonly string lockPath;
    private readonly string metadataPath;
    public TimeSpan WaitDuration { get; }

    private BuildLock(FileStream stream, string lockPath, string metadataPath, TimeSpan waitDuration)
    {
        this.stream = stream;
        this.lockPath = lockPath;
        this.metadataPath = metadataPath;
        WaitDuration = waitDuration;
    }

    public static async Task<BuildLock> AcquireAsync(string lockPath, string command, string targetPath, DateTimeOffset startTime, CancellationToken cancellationToken, string lockDisplayName = "build lock")
    {
        var metadataPath = $"{lockPath}.json";
        var waitingSince = DateTimeOffset.Now;
        var lastStatusAt = DateTimeOffset.MinValue;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var metadata = new LockMetadata(Environment.ProcessId, command, targetPath, startTime);
                var stream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                stream.SetLength(0);
                await JsonSerializer.SerializeAsync(stream, metadata, DnRelayJsonContext.Default.LockMetadata, cancellationToken: cancellationToken);
                await stream.FlushAsync(cancellationToken);
                await File.WriteAllTextAsync(
                    metadataPath,
                    JsonSerializer.Serialize(metadata, DnRelayJsonContext.Default.LockMetadata),
                    cancellationToken);
                return new BuildLock(stream, lockPath, metadataPath, DateTimeOffset.Now - waitingSince);
            }
            catch (IOException)
            {
                if (DateTimeOffset.Now - lastStatusAt >= TimeSpan.FromSeconds(2))
                {
                    lastStatusAt = DateTimeOffset.Now;
                    var owner = await TryReadOwnerAsync(metadataPath, cancellationToken);
                    if (owner is null)
                    {
                        Console.WriteLine($"waiting for {lockDisplayName}...");
                    }
                    else
                    {
                        var waited = DateTimeOffset.Now - waitingSince;
                        Console.WriteLine($"waiting for {lockDisplayName} ({waited.TotalSeconds:F0}s): pid={owner.Pid} command={owner.Command} target={owner.Target}");
                    }
                }

                await Task.Delay(500, cancellationToken);
            }
        }
    }

    public void Dispose()
    {
        stream.Dispose();
        TryDeleteMetadata();
    }

    public ValueTask DisposeAsync()
    {
        stream.Dispose();
        TryDeleteMetadata();
        return ValueTask.CompletedTask;
    }

    private void TryDeleteMetadata()
    {
        try
        {
            if (File.Exists(metadataPath))
            {
                File.Delete(metadataPath);
            }
        }
        catch
        {
        }

        try
        {
            if (File.Exists(lockPath))
            {
                File.Delete(lockPath);
            }
        }
        catch
        {
        }
    }

    private static async Task<LockMetadata?> TryReadOwnerAsync(string metadataPath, CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(metadataPath))
            {
                return null;
            }

            await using var stream = File.OpenRead(metadataPath);
            return await JsonSerializer.DeserializeAsync(stream, DnRelayJsonContext.Default.LockMetadata, cancellationToken: cancellationToken);
        }
        catch
        {
            return null;
        }
    }
}
