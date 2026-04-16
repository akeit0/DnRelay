namespace DnRelay.Parsing;

readonly record struct EnvFileParseOutcome(bool Success, IReadOnlyDictionary<string, string>? EnvironmentVariables, string? ErrorMessage)
{
    public static EnvFileParseOutcome Ok(IReadOnlyDictionary<string, string> environmentVariables) => new(true, environmentVariables, null);
    public static EnvFileParseOutcome Fail(string message) => new(false, null, message);
}
