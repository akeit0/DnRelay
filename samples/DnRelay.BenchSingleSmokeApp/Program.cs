using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;

BenchmarkSwitcher.FromAssembly(typeof(ProgramMarker).Assembly).Run(args);

internal sealed class ProgramMarker;

[MemoryDiagnoser]
[ShortRunJob]
public class SingleParserBenchmark
{
    private readonly string _csvInput = string.Join(',', Enumerable.Range(1, 64));

    [Benchmark]
    public int ParseCsv()
    {
        var sum = 0;
        foreach (var part in _csvInput.Split(','))
        {
            sum += int.Parse(part);
        }

        return sum;
    }
}
