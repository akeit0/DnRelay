namespace DnRelay.Tool.Options;

readonly record struct ParseOutcome<T>(bool Success, T? Options, string? ErrorMessage)
{
    public static ParseOutcome<T> Ok(T options) => new(true, options, null);
    public static ParseOutcome<T> Fail(string message) => new(false, default, message);
}
