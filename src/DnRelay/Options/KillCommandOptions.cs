using DnRelay.Parsing;

namespace DnRelay.Options;

sealed class KillCommandOptions
{
    public required string Selector { get; init; }
    public bool Json { get; init; }

    public static ParseOutcome<KillCommandOptions> Parse(string[] args)
    {
        if (args.Length == 0)
        {
            return ParseOutcome<KillCommandOptions>.Fail("Missing kill target. Use a process id from 'dnrelay stats' or '*'.");
        }

        string? selector = null;
        var json = false;

        foreach (var arg in args)
        {
            if (string.Equals(arg, "--json", StringComparison.OrdinalIgnoreCase))
            {
                json = true;
                continue;
            }

            if (selector is not null)
            {
                return ParseOutcome<KillCommandOptions>.Fail("Too many arguments for kill. Use a single process id from 'dnrelay stats' or '*'.");
            }

            selector = arg;
        }

        if (string.IsNullOrWhiteSpace(selector))
        {
            return ParseOutcome<KillCommandOptions>.Fail("Missing kill target. Use a process id from 'dnrelay stats' or '*'.");
        }

        return ParseOutcome<KillCommandOptions>.Ok(new KillCommandOptions
        {
            Selector = selector,
            Json = json
        });
    }
}
