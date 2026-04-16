using System.Diagnostics;

namespace DnRelay.Utilities;

static class DotNetCliEnvironmentDefaults
{
    private const string CliUiLanguageKey = "DOTNET_CLI_UI_LANGUAGE";
    private const string DefaultCliUiLanguage = "en-US";

    public static void Apply(ProcessStartInfo startInfo, IReadOnlyDictionary<string, string> environmentVariables)
    {
        if (!environmentVariables.ContainsKey(CliUiLanguageKey))
        {
            startInfo.Environment[CliUiLanguageKey] = DefaultCliUiLanguage;
        }

        foreach (var pair in environmentVariables)
        {
            startInfo.Environment[pair.Key] = pair.Value;
        }
    }
}
