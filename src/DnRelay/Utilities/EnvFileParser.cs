using DnRelay.Parsing;

namespace DnRelay.Utilities;

static class EnvFileParser
{
    public static EnvFileParseOutcome Parse(string path)
    {
        var environmentVariables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lines = File.ReadAllLines(path);

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
            {
                return EnvFileParseOutcome.Fail($"Invalid env file entry at {path}:{index + 1}");
            }

            var name = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim();
            environmentVariables[name] = TrimOptionalQuotes(value);
        }

        return EnvFileParseOutcome.Ok(environmentVariables);
    }

    private static string TrimOptionalQuotes(string value)
    {
        if (value.Length >= 2)
        {
            if ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\''))
            {
                return value[1..^1];
            }
        }

        return value;
    }
}
