var delayMs = 0;
if (args.Length > 0 && int.TryParse(args[0], out var parsedDelayMs))
{
    delayMs = parsedDelayMs;
}

Console.WriteLine($"env DOTNET_HARNESS_RUN_SMOKE={Environment.GetEnvironmentVariable("DOTNET_HARNESS_RUN_SMOKE") ?? "<null>"}");
Console.WriteLine($"args={string.Join(",", args)}");

if (delayMs > 0)
{
    Console.WriteLine($"sleeping {delayMs}ms");
    await Task.Delay(delayMs);
}

Console.WriteLine("run smoke app done");
