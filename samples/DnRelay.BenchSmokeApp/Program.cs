using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;

BenchmarkSwitcher.FromAssembly(typeof(ProgramMarker).Assembly).Run(args);

internal sealed class ProgramMarker;

[MemoryDiagnoser]
[ShortRunJob]
public class ParserBenchmarks
{
    private readonly string _csvInput = string.Join(',', Enumerable.Range(1, 100));
    private readonly string _pipeInput = string.Join('|', Enumerable.Range(1, 100));

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

    [Benchmark]
    public int ParsePipeSeparated()
    {
        var sum = 0;
        foreach (var part in _pipeInput.Split('|'))
        {
            sum += int.Parse(part);
        }

        return sum;
    }
}

[MemoryDiagnoser]
[ShortRunJob]
public class FormattingBenchmarks
{
    private readonly int[] _values = Enumerable.Range(1, 32).ToArray();

    [Benchmark]
    public string JoinWithStringJoin()
        => string.Join(',', _values);

    [Benchmark]
    public string JoinWithStringBuilder()
    {
        var builder = new StringBuilder();
        for (var index = 0; index < _values.Length; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            builder.Append(_values[index]);
        }

        return builder.ToString();
    }
}
