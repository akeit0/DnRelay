using System.Text.Json.Serialization;
using DnRelay.Models;

namespace DnRelay.Serialization;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(BuildCommandJsonPayload))]
[JsonSerializable(typeof(TestCommandJsonPayload))]
[JsonSerializable(typeof(RunCommandJsonPayload))]
[JsonSerializable(typeof(BenchCommandJsonPayload))]
[JsonSerializable(typeof(StatsCommandJsonPayload))]
[JsonSerializable(typeof(KillCommandJsonPayload))]
[JsonSerializable(typeof(StatsProcessJsonEntry))]
[JsonSerializable(typeof(StatsLockJsonEntry))]
[JsonSerializable(typeof(LockMetadata))]
[JsonSerializable(typeof(DnRelayConfig))]
[JsonSerializable(typeof(TrackedProcessMetadata))]
internal sealed partial class DnRelayJsonContext : JsonSerializerContext
{
}
