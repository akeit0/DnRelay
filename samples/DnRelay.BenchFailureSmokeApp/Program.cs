using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;

BenchmarkSwitcher.FromAssembly(typeof(ProgramMarker).Assembly).Run(args);

internal sealed class ProgramMarker;

[ShortRunJob]
public class ThrowingBenchmarks
{
    [Benchmark]
    public int ThrowImmediately()
        => throw new InvalidOperationException("intentional benchmark failure");
}
