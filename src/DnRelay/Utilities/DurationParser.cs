using System.Text.RegularExpressions;

namespace DnRelay.Utilities;

static partial class DurationParser
{
    [GeneratedRegex("^(?<value>[0-9]+(?:\\.[0-9]+)?)(?<unit>ms|s|m|h)?$", RegexOptions.CultureInvariant)]
    private static partial Regex DurationPattern();

    public static bool TryParse(string text, out TimeSpan? timeout)
    {
        timeout = null;

        if (TimeSpan.TryParse(text, out var value))
        {
            timeout = value;
            return true;
        }

        var match = DurationPattern().Match(text);
        if (!match.Success)
        {
            return false;
        }

        var numericValue = double.Parse(match.Groups["value"].Value);
        var unit = match.Groups["unit"].Success ? match.Groups["unit"].Value : "s";
        timeout = unit switch
        {
            "ms" => TimeSpan.FromMilliseconds(numericValue),
            "s" => TimeSpan.FromSeconds(numericValue),
            "m" => TimeSpan.FromMinutes(numericValue),
            "h" => TimeSpan.FromHours(numericValue),
            _ => null
        };

        return timeout is not null;
    }
}
