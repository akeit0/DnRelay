using DnRelay.Parsing;

namespace DnRelay.Options;

sealed class StatsCommandOptions
{
    public bool Json { get; init; }

    public static ParseOutcome<StatsCommandOptions> Parse(string[] args)
    {
        var json = false;

        foreach (var arg in args)
        {
            if (string.Equals(arg, "--json", StringComparison.OrdinalIgnoreCase))
            {
                json = true;
                continue;
            }

            return ParseOutcome<StatsCommandOptions>.Fail($"Unsupported stats option: {arg}");
        }

        return ParseOutcome<StatsCommandOptions>.Ok(new StatsCommandOptions
        {
            Json = json
        });
    }
}
