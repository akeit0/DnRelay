# DnRelay

`dnrelay` is a compact wrapper around `dotnet` for AI agents and humans.

It keeps the full child output in repo-local logs, but prints a short stable summary by default. It also adds repo-aware locking, timeout control, environment injection, and tighter handling for noisy commands like tests and benchmarks.

By default, `dnrelay` asks the .NET CLI to emit English UI text via `DOTNET_CLI_UI_LANGUAGE=en-US` so summaries and saved logs stay stable across machines and operating systems. If you need a different language, override it with `--env DOTNET_CLI_UI_LANGUAGE=<value>`.

Additional guidance:

- generic usage guidance: [BEST_PRACTICES.md](/C:/Users/akito/DotNetHarness/BEST_PRACTICES.md)
- repo-specific dogfooding notes: [DOGFOODING.md](/C:/Users/akito/DotNetHarness/DOGFOODING.md)

## Quick Start
```
dnx dnrelay
```
## Status

Current commands:

- `build`
- `test`
- `run`
- `bench`
- `stats`
- `kill`
- `config`

Companion development tool:

- `dnrelay-tool`

Current design goals:

- short default output
- full logs on disk
- explicit timeout control
- `--env` and `--env-file`
- conservative locking where project metadata makes conflicts inferable
- practical dogfooding as a local .NET tool
- NativeAOT-friendly implementation choices

## NativeAOT

The current implementation is written to stay friendly to NativeAOT:

- regex uses `GeneratedRegexAttribute`
- JSON output and lock metadata serialization use source-generated `System.Text.Json` metadata

Current validation command:

```powershell
dotnet publish src\DnRelay\DnRelay.csproj -c Release -r win-x64 /p:PublishAot=true
```

## Install

Install `dnrelay` as a global .NET tool:

```powershell
dotnet tool install --global DnRelay
```

After global install, use the short command directly:

```powershell
dnrelay --help
```

NuGet package:

- [DnRelay](https://www.nuget.org/packages/DnRelay)

Update:

```powershell
dotnet tool update --global DnRelay
```

Uninstall:

```powershell
dotnet tool uninstall --global DnRelay
```

For repository-local pinning instead of global install:

```powershell
dotnet new tool-manifest
dotnet tool install --local DnRelay
dotnet dnrelay --help
```

## Companion Tool

`dnrelay-tool` is a separate development helper, not part of the main execution wrapper surface.

Current companion command:

- `tool-refresh`

Install:

```powershell
dotnet tool install --global DnRelay.Tool
```

Package:

- [DnRelay.Tool](https://www.nuget.org/packages/DnRelay.Tool)

Example:

```powershell
dnrelay-tool tool-refresh
dnrelay-tool tool-refresh --bump minor
dnrelay-tool tool-refresh --version 2.0.0-rc.1
dnrelay-tool tool-refresh src\DnRelay.Tool\DnRelay.Tool.csproj
```

`tool-refresh`:

- reads version from either `<Version>` or `<VersionPrefix>` plus optional `<VersionSuffix>`
- defaults to `patch`, or uses `<ToolRefreshBump>` when present in the target project
- accepts `--bump patch|minor|major`
- accepts `--version <semver>` for an explicit target version
- updates the local tool if it is already installed, or installs it into the local manifest if not

## Commands

### config

```powershell
dnrelay config logs .artifacts\logs
dnrelay config logs .artifacts\logs --force
```

`config logs` writes `.dnrelay\config.json` with a `logsDir` entry for the current repository.

### build

```powershell
dnrelay build src\App\App.csproj
dnrelay build --target src\App\App.csproj
dnrelay build src\App\App.csproj --timeout 30s
dnrelay build src\App.slnx --json
dnrelay build src\App\App.csproj --env ContinuousIntegrationBuild=true
dnrelay build src\App\App.csproj --env-file .env.build
dnrelay build src\App\App.csproj --logs-dir .artifacts\logs
dnrelay build src\App\App.csproj -- --no-restore
dnrelay build src\App\App.csproj -- -c Release -p:TreatWarningsAsErrors=true
dnrelay build samples\DnRelay.BuildWarningSmokeApp\DnRelay.BuildWarningSmokeApp.csproj
dnrelay build samples\DnRelay.BuildErrorSmokeApp\DnRelay.BuildErrorSmokeApp.csproj
```

Summary includes:

- status
- target
- duration
- warning count
- error count
- log path
- lock scope summary
- lock wait duration

Text mode also prints an immediate `BUILD STARTED` block with the target and log path before lock wait or execution begins.

Build smoke samples in this repo:

- `samples\DnRelay.BuildWarningSmokeApp\DnRelay.BuildWarningSmokeApp.csproj`
  - succeeds with a nullable warning so `warnings:` and `top warnings:` can be dogfooded
- `samples\DnRelay.BuildErrorSmokeApp\DnRelay.BuildErrorSmokeApp.csproj`
  - fails with a compile error so `errors:` and `top errors:` can be dogfooded

### test

```powershell
dnrelay test tests\App.Tests\App.Tests.csproj
dnrelay test --target tests\App.Tests\App.Tests.csproj
dnrelay test tests\App.Tests\App.Tests.csproj --env ASPNETCORE_ENVIRONMENT=Development
dnrelay test tests\App.Tests\App.Tests.csproj --env-file .env.test
dnrelay test tests\App.Tests\App.Tests.csproj --logs-dir .artifacts\logs
dnrelay test tests\App.Tests\App.Tests.csproj --timeout 2m
dnrelay test tests\App.Tests\App.Tests.csproj --json
dnrelay test tests\App.Tests\App.Tests.csproj -- --filter MyTest
dnrelay test tests\App.Tests\App.Tests.csproj -- --filter Category=Smoke
dnrelay test tests\App.Tests\App.Tests.csproj -- --logger "console;verbosity=minimal"
```

`test` writes a full log and a `.trx` file, then summarizes:

- total
- passed
- failed
- skipped
- errors
- top failures
- log path
- trx path
- lock scope summary
- lock wait duration

Text mode also prints an immediate `TEST STARTED` block with the target and log path before lock wait or execution begins.

### run

```powershell
dnrelay run --project src\App\App.csproj
dnrelay run --target src\App\App.csproj
dnrelay run --project src\App\App.csproj --timeout 45s
dnrelay run --project src\App\App.csproj --logs-dir .artifacts\logs
dnrelay run --project src\App\App.csproj --env-file .env.local
dnrelay run --project src\App\App.csproj --env ASPNETCORE_ENVIRONMENT=Development
dnrelay run --project src\App\App.csproj --json
dnrelay run --project src\App\App.csproj --no-build
dnrelay run --project src\App\App.csproj --raw
dnrelay run --project src\App\App.csproj -- --urls http://localhost:5007
dnrelay run --project samples\DnRelay.RunSmokeApp\DnRelay.RunSmokeApp.csproj -- 1000
```

`run` builds first under a project-graph-aware build lock when needed, then executes with `--no-build`. This avoids holding the build lock across the whole process.

In normal use, do not pass `--no-build`.
`dnrelay run` already decides whether it needs a coordinated build phase, then invokes `dotnet run --no-build` internally for the actual execution step.
Pass top-level `--no-build` only when you intentionally want to skip `dnrelay`'s build phase because you already know the outputs are up to date.

Summary includes:

- status
- target
- duration
- exit code
- log path
- optional raw output streaming
- bounded output tail

Text mode also prints an immediate `RUN STARTED` block with the target and log path before build coordination or execution begins.

### kill

```powershell
dnrelay stats
dnrelay stats --json
dnrelay kill 12345
dnrelay kill 12345 --json
dnrelay kill *
```

`kill` is a repo-local cleanup command for lingering dnrelay-related processes.

Use `dnrelay stats` first to see active process ids and lock owners for the current repository.

`kill` accepts:

- a process id shown by `dnrelay stats`
- `*` to kill every dnrelay-related process for the repository

This is mainly useful for cases such as:

- leftover `testhost` processes after `test`
- long-running `run` processes you want to stop quickly
- stale benchmark processes during dogfooding

### stats

```powershell
dnrelay stats
dnrelay stats --json
```

`stats` shows:

- active tracked dnrelay processes for the repository
- active lock owners under `.dnrelay\locks\`
- process id, command, target, and start time
- automatic cleanup of stale `.dnrelay\pids\*.json` and `.dnrelay\locks\*.json` records when their owner pid is already gone

This is the primary way to discover ids for `dnrelay kill`.

### bench

```powershell
dnrelay bench --project perf\Benchmarks\Benchmarks.csproj
dnrelay bench --target perf\Benchmarks\Benchmarks.csproj
dnrelay bench --project perf\Benchmarks\Benchmarks.csproj -- --filter *Parser*
dnrelay bench --project perf\Benchmarks\Benchmarks.csproj --json -- --filter *Parser*
dnrelay bench --project perf\Benchmarks\Benchmarks.csproj -c Release
dnrelay bench --project perf\Benchmarks\Benchmarks.csproj --timeout 10m
dnrelay bench --project perf\Benchmarks\Benchmarks.csproj --logs-dir .artifacts\logs
dnrelay bench --project perf\Benchmarks\Benchmarks.csproj --env DOTNET_TieredPGO=1
dnrelay bench --project perf\Benchmarks\Benchmarks.csproj -- --job short --filter *Parser*
dnrelay bench --project samples\DnRelay.BenchSingleSmokeApp\DnRelay.BenchSingleSmokeApp.csproj
dnrelay bench --project samples\DnRelay.BenchSmokeApp\DnRelay.BenchSmokeApp.csproj
dnrelay bench --project samples\DnRelay.BenchSmokeApp\DnRelay.BenchSmokeApp.csproj --list
dnrelay bench --project samples\DnRelay.BenchSmokeApp\DnRelay.BenchSmokeApp.csproj --select 2
dnrelay bench --project samples\DnRelay.BenchSmokeApp\DnRelay.BenchSmokeApp.csproj --select ParserBenchmarks
dnrelay bench --project samples\DnRelay.BenchSmokeApp\DnRelay.BenchSmokeApp.csproj -- --filter *Parser*
dnrelay bench --project samples\DnRelay.BenchFailureSmokeApp\DnRelay.BenchFailureSmokeApp.csproj
```

`bench` is intended for BenchmarkDotNet-style projects. It:

1. builds under a project-graph-aware build lock
2. releases that lock
3. runs the benchmark under a narrower project-scoped bench lock

This keeps build coordination conservative without blocking unrelated repo work for the full benchmark duration.

Summary includes:

- status
- target
- duration
- exit code
- log path
- BenchmarkDotNet artifacts path
- compact highlights
- bounded output tail
- build lock scope summary
- build lock wait duration

Text mode also prints an immediate `BENCH STARTED` block with the target and log path before build coordination begins.

The full details still live in `BenchmarkDotNet.Artifacts`.

The sample benchmark projects in this repo cover three dogfooding patterns:

- `DnRelay.BenchSingleSmokeApp`: one benchmark, runs directly without selection
- `DnRelay.BenchSmokeApp`: multiple benchmarks, exercises `--list`, `--select`, and `--filter`
- `DnRelay.BenchFailureSmokeApp`: throwing benchmark, exercises benchmark failure reporting

All of them use `ShortRunJob` so iteration stays fast.

If multiple benchmarks are discovered and you did not pass `--select` or `--filter`, the harness prints the benchmark list and exits instead of entering interactive selection.
Use `--list` to inspect candidates, `--select <index>` to run one benchmark by number, or `--select <name>` / `--filter` for name-based selection.

### bench output format

`bench` has three main output shapes:

1. list output
2. selection-required output
3. progress plus final summary when execution starts

Example for list mode:

```text
BENCH STARTED
target: samples\DnRelay.BenchSmokeApp\DnRelay.BenchSmokeApp.csproj
log: .dnrelay\logs\bench-20260412-121957-221-pid188612.txt
BENCH LIST
project: samples\DnRelay.BenchSmokeApp\DnRelay.BenchSmokeApp.csproj
benchmarks:
- [0] FormattingBenchmarks.JoinWithStringJoin
- [1] FormattingBenchmarks.JoinWithStringBuilder
- [2] ParserBenchmarks.ParseCsv
- [3] ParserBenchmarks.ParsePipeSeparated
```

Example for selection-required mode:

```text
BENCH STARTED
target: samples\DnRelay.BenchSmokeApp\DnRelay.BenchSmokeApp.csproj
log: .dnrelay\logs\bench-20260412-121957-226-pid193604.txt
BENCH SELECTION REQUIRED
project: samples\DnRelay.BenchSmokeApp\DnRelay.BenchSmokeApp.csproj
benchmarks:
- [0] FormattingBenchmarks.JoinWithStringJoin
- [1] FormattingBenchmarks.JoinWithStringBuilder
- [2] ParserBenchmarks.ParseCsv
- [3] ParserBenchmarks.ParsePipeSeparated
next:
- dnrelay bench --project samples\DnRelay.BenchSmokeApp\DnRelay.BenchSmokeApp.csproj --select 0
- dnrelay bench --project samples\DnRelay.BenchSmokeApp\DnRelay.BenchSmokeApp.csproj --select FormattingBenchmarks.JoinWithStringJoin
- dnrelay bench --project samples\DnRelay.BenchSmokeApp\DnRelay.BenchSmokeApp.csproj -- --filter *SomeBenchmark*
```

Example for execution mode:

```text
BENCH STARTED
target: samples\DnRelay.BenchSmokeApp\DnRelay.BenchSmokeApp.csproj
log: .dnrelay\logs\bench-20260412-024109-141-pid213648.txt
benchmark running... 15s
BENCH SUCCEEDED
project: samples\DnRelay.BenchSmokeApp\DnRelay.BenchSmokeApp.csproj
duration: 29.1s
exit code: 0
log: .dnrelay\logs\bench-20260412-024109-141-pid213648.txt
artifacts: samples\DnRelay.BenchSmokeApp\BenchmarkDotNet.Artifacts\results
build lock: samples\DnRelay.BenchSmokeApp\DnRelay.BenchSmokeApp.csproj
build lock wait: 0.0s
highlights:
- | Method   | Mean     | Error   | StdDev  |
- | ParseCsv | 885.7 ns | 3.42 ns | 3.20 ns |
```

Field meanings:

- `benchmark running... <seconds>s`
  - heartbeat only
  - indicates the benchmark process is still alive
  - not part of the final summary object
- `<COMMAND> STARTED`
  - immediate text-mode acknowledgement that the harness accepted the command
  - emitted before lock wait and before child process execution
  - includes the resolved target path and harness log path
  - suppressed in `--json` mode so the final JSON object remains the only stdout payload
- `BENCH LIST`
  - printed when `--list` is used
  - shows the discovered benchmark methods with stable numeric indices
- `BENCH SELECTION REQUIRED`
  - printed when multiple benchmarks are discovered and no `--select` or `--filter` was provided
  - intended to be the default agent-friendly flow for multi-benchmark assemblies
  - includes concrete next-command examples instead of entering interactive selection
- `BENCH SUCCEEDED` / `BENCH FAILED` / `BENCH TIMED OUT`
  - final harness status
- `reason`
  - optional failure-focused one-line diagnosis
  - appears when the harness can identify a benchmark issue such as an exception or invalid benchmark result set
- `project`
  - resolved benchmark project path relative to the repo root
- `duration`
  - total harness-observed wall-clock duration, including build phase and benchmark execution
- `exit code`
  - final process exit code returned by the harness
- `log`
  - full harness log path
- `artifacts`
  - BenchmarkDotNet results directory
  - this is the primary pointer for detailed benchmark reports such as `.csv`, `.md`, and `.html`
- `build lock`
  - the build-phase lock scope used before BenchmarkDotNet execution begins
- `build lock wait`
  - time spent waiting for the build-phase lock
- `highlights`
  - selected lines promoted from BenchmarkDotNet output
  - intended to give a compact human/agent-readable result without dumping the full log
  - successful runs prefer the final summary table rows only
  - if the table cannot be extracted, dnrelay falls back to the broader final summary section
  - failed runs may instead prefer exception and export lines
- `output tail`
  - bounded last lines from the benchmark output
  - primarily useful for failures and unusual final-context clues
  - successful runs may omit it entirely if the tail adds no value
  - not intended to be a stable machine-readable contract

### JSON Contract

All `--json` modes emit exactly one final JSON object to stdout.

Shared fields:

- `command`
- `status`
- `project`
- `duration`
- `exitCode`
- `timedOut`
- `log`

`build --json` adds:

- `warnings`
- `errors`
- `topWarnings`
- `topErrors`

`test --json` adds:

- `total`
- `passed`
- `failed`
- `skipped`
- `errors`
- `trx`
- `topFailures`
- `topErrors`

`run --json` adds:

- `outputTail`

`bench --json` adds:

- `artifacts`
- `reason`
- `selectionRequired`
- `availableBenchmarks`
- `highlights`
- `outputTail`

`stats --json` adds:

- `repo`
- `processes`
- `locks`
- `removedStaleProcessIds`
- `removedStaleLocks`

Each `processes[]` entry includes:

- `id`
- `command`
- `target`
- `started`

Each `locks[]` entry includes:

- `name`
- `ownerId`
- `state`
- `command`
- `target`
- `started`

`kill --json` adds:

- `selector`
- `repo`
- `matchedPids`
- `killedPids`
- `alreadyGonePids`
- `failedPids`
- `removedStaleProcessIds`
- `removedStaleLocks`

Kill-specific `status` values:

- `completed`
- `no_match`
- `failed`

Bench-specific `status` values:

- `listed`
- `selection_required`
- `succeeded`
- `failed`
- `timed_out`

What is intentionally not in the main summary:

- full statistical detail such as histogram, quartiles, skewness, kurtosis, and confidence interval breakdown
- full environment dump
- full BenchmarkDotNet transcript

Those remain in:

- `log`
- `artifacts`

Practical interpretation:

- use `--list` first when you do not know the benchmark names
- use `--select <index>` for the most deterministic follow-up command
- read the summary first
- use `highlights` for quick result inspection
- use `output tail` for quick failure/context clues
- open `log` or `BenchmarkDotNet.Artifacts` when you need the full BenchmarkDotNet output contract

## Common Options

Supported across the main commands:

- `--json`
- `--timeout <duration>`
- `--logs-dir <path>`
- `--env KEY=VALUE`
- `--env-file <path>`

Examples:

```powershell
dnrelay build src\App\App.csproj --timeout 30s
dnrelay build src\App\App.csproj --logs-dir .artifacts\logs
dnrelay test tests\App.Tests\App.Tests.csproj --env ASPNETCORE_ENVIRONMENT=Development
dnrelay run --project src\App\App.csproj --env-file .env.local
```

## Logging And Artifacts

Harness logs go under:

```text
.dnrelay/logs/
```

Test result artifacts go under:

```text
artifacts/test-results/
```

BenchmarkDotNet keeps its own detailed artifacts under the benchmark project, typically:

```text
<benchmark-project-dir>/BenchmarkDotNet.Artifacts/
```

The harness summary is the primary output. Logs and artifacts are for follow-up investigation.

Logs are written as UTF-8 without BOM, and child process stdout/stderr is decoded as UTF-8 by default so common Windows `dotnet` output stays readable in the saved transcript.
`dnrelay` also defaults `DOTNET_CLI_UI_LANGUAGE` to `en-US` for its child `dotnet` processes so CLI and MSBuild output are consistently English unless you override that environment variable explicitly.

You can override the harness log directory per command with `--logs-dir <path>`.
If the path is relative, it is resolved from the repository root.

You can also generate a repo-local default with:

```powershell
dnrelay config logs .artifacts\logs
```

Or write `.dnrelay\config.json` directly:

```json
{
  "logsDir": ".artifacts/logs"
}
```

Resolution order:

- `--logs-dir`
- `.dnrelay\config.json` `logsDir`
- default `.dnrelay\logs\`

## Locking Model

Current locking policy:

- `build` and `test` resolve a project/solution lock scope from `.csproj`, `.sln`, or `.slnx`
- if that scope cannot be resolved safely, they fall back to a repo-wide build lock
- `run` uses the same build lock scope only for the build phase when needed
- `bench` uses the same build lock scope for the build phase, then a project-scoped bench lock for the benchmark run

Lock files and repo-local config live under:

```text
.dnrelay/
```

In practice that means:

- `.dnrelay\config.json` for repo-local defaults
- `.dnrelay\.gitignore` auto-generated to ignore `logs/`, `locks/`, and `pids/`
- `.dnrelay\logs\` for default harness logs
- `.dnrelay\locks\` for build and bench coordination files
- `.dnrelay\pids\` for tracked child process metadata used by `kill`

The intended rule is:

- lock what can be inferred from project/solution/build metadata
- do not try to infer arbitrary runtime resource conflicts such as ports, databases, or external services

If a runtime conflict is outside the project graph, normal failure plus compact summary is acceptable.

## Help

Root help:

```powershell
dnrelay --help
dnrelay-tool --help
```

Per-command help:

```powershell
dnrelay build --help
dnrelay test --help
dnrelay run --help
dnrelay bench --help
dnrelay stats --help
dnrelay kill --help
dnrelay-tool tool-refresh --help
```
## LICENSE
MIT
