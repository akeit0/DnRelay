# DnRelay Agent Guide

## Goal

`dnrelay` is a local-first wrapper around `dotnet` for AI agents and humans who need:

- compact, structured output instead of raw `dotnet` logs
- project-graph-aware coordination with repo-wide fallback to avoid parallel build/test file-lock issues
- explicit timeout control
- per-process environment injection
- durable full logs written to files
- progress reporting for long-running commands, especially benchmarks

The immediate objective is not to replace all of `dotnet`. The immediate objective is to make `build`, `test`, `run`, and later `bench` safe to use from an AI agent without wasting context or deadlocking the repo.

## Product Principles

- Dogfood the harness. When working in the DnRelay repo, prefer `dotnet dnrelay ...` over raw `dotnet ...` commands whenever the installed local tool version is sufficient for the task.
- Prefer `dotnet dnrelay build` over raw `dotnet build` by default so lock behavior, summaries, timeout handling, and log output are continuously exercised in real work.
- Prefer `dotnet dnrelay run --project <path>` over manually separating build and run when the real goal is to execute the project.
- Use the source-built harness entrypoint only when the installed local tool is stale and the behavior under test depends on unrefreshed code.
- Use raw `dotnet` only for bootstrap or break-glass recovery when `dnrelay` itself is too broken to use.
- Human-readable first, machine-readable second. Default output should be short enough for an agent to consume directly.
- Always preserve the full underlying `dotnet` output in a log file.
- Prefer deterministic summaries over streaming raw stdout.
- Coordinate conservatively by default; never assume the caller serialized access.
- Expose explicit escape hatches, but make the safe path the default.
- Keep the command surface stable so the implementation can later move from C# to NativeAOT, Rust, or Go.
- Treat dogfooding friction as product input. If this project gets stuck on packaging, locking, logging, or agent ergonomics, treat that as a signal to improve the harness rather than incidental project noise.
- Keep generic usage guidance and repo-specific dogfooding guidance separate. Product-facing guidance belongs in `BEST_PRACTICES.md`; repo-specific validation loops and self-lock notes belong in `DOGFOODING.md`.

## Current Status

Implemented command surface:

- `config`
- `build`
- `test`
- `run`
- `bench`

Separate companion development tool:

- `dnrelay-tool`
- `dnrelay-tool tool-refresh`

Implemented shared features:

- `--timeout`
- `--logs-dir`
- `--env`
- `--env-file`
- `--json`
- source-generated regex and JSON serialization for NativeAOT compatibility
- root help plus per-command `--help`
- repo-local full logs
- repo-local config via `.dnrelay\config.json`
- default harness logs under `.dnrelay\logs\`
- auto-generated `.dnrelay\.gitignore` for generated `logs/` and `locks/`
- local tool packaging and dogfooding loop
- immediate text-mode `<COMMAND> STARTED` output with resolved target and log path
- UTF-8 log writing plus UTF-8 child output decoding by default
- lock scope and lock wait stats in text summaries and logs

Current command behavior:

- `build` resolves a project/solution build lock scope when possible, otherwise falls back to a repo-wide build lock, and emits compact warning/error summaries.
- `test` uses the same build lock strategy, writes `.trx`, and summarizes counts from structured test results.
- `run` uses the same build lock strategy only for the build phase when needed, then executes with `--no-build`.
- `bench` uses the same build lock strategy for its build phase, then executes BenchmarkDotNet under a narrower project-scoped bench lock.
- `dnrelay-tool tool-refresh` is intentionally outside the main wrapper surface and remains available as a separate developer-oriented local tool.
- `dotnet publish -c Release -r win-x64 /p:PublishAot=true` is currently expected to succeed.

## Current TODOs From Dogfooding

- Expand example coverage in `README.md` as new real-world invocation patterns appear.
- Improve `bench --json` and text summaries only if dogfooding shows missing information; keep BenchmarkDotNet details in its artifacts rather than copying too much into the main summary.
- Revisit ordered workflow support for `dotnet new` then `dotnet sln add` only if repeated agent usage shows that a simple normal failure is not sufficient.
- Preserve the distinction between harness logs and BenchmarkDotNet artifacts. The harness log is the parent-process view; `BenchmarkDotNet.Artifacts` is the authoritative detailed benchmark output.
- Keep benchmark dogfooding fast. The in-repo benchmark sample should prefer `ShortRun`-style jobs and multiple benchmark classes so `bench` can be exercised through filters, assembly switcher flows, and compact summary extraction without long benchmark cycles.
- Prefer non-interactive benchmark selection. If a benchmark assembly exposes multiple candidates, `bench` should list them and exit unless the caller passed `--select` or `--filter`.
- Make benchmark discovery intuitive for agents. Provide stable numeric indices in list output and allow `--select <index>` as the most direct follow-up command.
- Keep benchmark coverage broad. Maintain sample benchmark projects for at least these cases: a single-benchmark project that runs directly, a multi-benchmark project that requires selection, and a benchmark project that fails by throwing.
- Keep build coverage broad. Maintain sample build projects for at least these cases: a project that succeeds with warnings and a project that fails with compile errors.

## Initial Command Surface

Implement these commands first:

```text
dnrelay build <path>
dnrelay test <path>
dnrelay run [--project <path>]
dnrelay bench [--project <path>]
```

Implement these cross-cutting options from the beginning:

```text
--timeout <duration>
--env KEY=VALUE
--env-file <path>
--json
--verbosity <quiet|normal|detailed>
```

Notes:

- `<path>` may be `.sln`, `.slnx`, `.csproj`, or a directory that resolves to one project/solution.
- `--json` should emit one final structured result object, not raw event streams.
- `--verbosity detailed` may expose more progress, but still must not dump unbounded child output.

## Recommended Implementation Order

### Phase 0: CLI skeleton

Build a single C# console app that can be installed as a local tool and that has:

- command parsing
- path resolution
- process launching
- environment injection
- timeout handling
- log file creation
- normalized result model

Exit criterion:

- `dnrelay build <target>` can launch `dotnet build` and return a small result summary.

### Phase 1: `build`

`build` is the first priority because it is:

- the most common agent action
- the main source of file-lock pain
- the easiest place to validate summary output

Requirements:

- acquire repository build lock before invoking `dotnet build`
- capture stdout/stderr to log file
- parse success/failure, warning count, error count, duration
- print a compact summary
- print waiting status while another conflicting operation holds the lock

Default summary shape:

```text
BUILD SUCCEEDED
project: src/App/App.csproj
duration: 4.2s
warnings: 2
errors: 0
log: logs/build-20260411-101530.txt
top warnings:
- CS8618: Non-nullable property ...
- NU1903: Package ...
```

### Phase 2: `test`

`test` is second because agents use it immediately after `build`, and it needs the same process, lock, env, and timeout infrastructure.

Requirements:

- reuse the same repository coordination layer
- summarize passed/failed/skipped counts
- surface failed test names concisely
- optionally capture blame or trx artifacts later, but not in v1

The first version should favor concise summaries over deep test reporting.

### Phase 3: `run`

`run` is third because its main complexity is runtime control rather than parsing compiler output.

Requirements:

- support `--project`
- support `--env` and `--env-file`
- support timeout
- by default, stream only bounded progress or last-N output context
- write complete child output to a log file

For v1, `run` should optimize for controlled execution, not interactive TTY fidelity.

### Phase 4: `bench`

`bench` comes after the above infrastructure exists. BenchmarkDotNet is noisy and long-running, so it should reuse as much of the shared execution model as possible.

Requirements:

- suppress most raw BenchmarkDotNet output from the main console
- emit heartbeat/progress messages so the caller knows it is still active
- highlight stage transitions and actual-run progress when detectable
- summarize final benchmark results in a compact table or JSON
- link to full artifacts/logs for detailed investigation

If BenchmarkDotNet parsing is expensive, start with coarse progress and artifact pointers, then iterate.

## Why This Order

Prioritize shared infrastructure before command breadth.

`build` and `test` validate almost every foundational concern:

- lock management
- timeout behavior
- env propagation
- summary formatting
- log persistence
- exit code mapping

Once those are solid, `run` is mostly execution control, and `bench` becomes a specialized adapter instead of a blocker for the rest of the tool.

## Architecture Guidance

Keep the implementation split into these layers from the start:

1. CLI layer
2. command planner
3. execution/locking layer
4. child process adapter for `dotnet`
5. result summarizer
6. JSON/text formatter

This separation matters because the final frontend command contract should survive a rewrite in NativeAOT, Rust, or Go.

Suggested internal abstractions:

- `CommandRequest`
- `ExecutionOptions`
- `LockScope`
- `ProcessResult`
- `CommandSummary`
- `ILogStore`
- `IRepoLockManager`

Avoid spreading `System.CommandLine` or raw process APIs across business logic.

## Locking Strategy

Use a lock file in the repository root. The first version does not need a distributed lock service.

Minimum requirements:

- discover repository root from the target path
- create a well-known lock file path under the repo root
- serialize conflicting operations using exclusive file access
- record owner metadata in the lock file: pid, command, target, start time
- when waiting, print short status lines describing which operation is being waited on

Current policy:

- `build` and `test` resolve a lock scope from `.csproj`, `.sln`, `.slnx`, and recursive `ProjectReference` closure when possible
- if resolution fails, they fall back to one repo-wide build lock
- `run` uses the same build lock scope only for its build phase when a build is needed
- `bench` follows the same build-phase pattern, then takes an additional project-scoped bench execution lock to protect `BenchmarkDotNet.Artifacts` and same-project benchmark outputs
- repo-local settings and lock files should live together under `.dnrelay\`, with lock files under `.dnrelay\locks\`

Reason:

- overlapping project graphs still serialize safely
- unrelated projects can now build and test in parallel
- long-lived `run` and `bench` processes do not hold the build lock after their build phase

Dogfooding update:

- keep the graph lock conservative. Resolve only what is clearly inferable from project and solution metadata; do not try to model arbitrary runtime resource conflicts.
- Only lock what can be inferred from the project/solution/build graph. Runtime conflicts that are not visible from project metadata should fail normally and be surfaced through compact summaries plus full logs.
- Failure from ordered workflows such as `dotnet sln add` against a project that does not exist yet is acceptable normal behavior unless dogfooding proves a stronger orchestration layer is worth the extra complexity.

## Timeout And Cancellation

Every command should support a harness-level timeout.

Requirements:

- kill the child process tree on timeout
- mark the summary as timed out
- keep the partial log
- return a distinct non-zero exit code for timeout

Future improvement:

- support soft cancellation first, then hard kill after grace period

## Environment Injection

Support both:

- repeated `--env KEY=VALUE`
- `--env-file <path>`

Rules:

- explicit `--env` overrides values from `--env-file`
- inherit parent environment unless the user later asks for a clean mode
- include effective environment keys in verbose logs, but never print obvious secrets in the default summary

## Repo-Local Config

Use `.dnrelay\config.json` for repo-local defaults that should not require repeating CLI flags.

Current supported key:

- `logsDir`

Rules:

- `--logs-dir` overrides config
- relative config paths are resolved from the repository root
- if config is missing or invalid, fall back to built-in defaults rather than failing the command

## Output Contract

Default text output must stay compact and stable.

Every command should at minimum report:

- final status
- resolved target
- duration
- key counts or run outcome
- log path
- lock scope and wait time when coordination happened

When useful, include a short `top warnings`, `top errors`, or `top failures` section capped to a small fixed number.

JSON output should expose the same data model as the text summary, including:

- command kind
- target
- status
- duration
- exit code
- timed out flag
- warning/error/test counts where applicable
- log path
- selected highlights

## Logging

Store full child output under a repo-local `logs/` directory.

Naming convention:

```text
logs/<command>-yyyyMMdd-HHmmss.txt
```

The summary should always point to the log path. Agents should rely on the summary first and inspect logs only when needed.

## Packaging Strategy

Near term:

- keep the tool usable as a .NET local tool
- dogfood in real repositories immediately
- keep dependencies light
- keep `README.md` and command help aligned with actual dogfooding workflows so agents do not rely on `dotnet run` for steady-state use

Medium term:

- trim-friendly design
- avoid reflection-heavy libraries where possible
- measure NativeAOT viability once command surface stabilizes

Long term:

- if startup time, binary size, or AOT constraints remain poor, keep the CLI contract and port the execution core to Rust or Go

The command contract is the product. The implementation language is replaceable.

## Non-Goals For V1

Do not block the first release on these:

- fine-grained project dependency locking
- rich BenchmarkDotNet statistical rendering
- IDE/debugger integration
- remote execution
- daemon/background service architecture
- interactive terminal emulation for `run`

## Current Milestone

Current usable milestone:

1. local tool installable from this repo
2. `build`, `test`, `run`, and `bench` implemented end-to-end
3. project-graph-aware build lock with repo-wide fallback in place
4. project-scoped bench execution lock in place
5. `--timeout`, `--env`, `--env-file`, and `--json` working
6. compact text summaries plus per-command help
7. full log file written for every run

Next milestone should focus on refining summaries, examples, and any workflow gaps that appear during continued dogfooding rather than adding broad new command surface immediately.

## Guidance For Future Agents

- Preserve the external CLI contract unless there is a clear product reason to change it.
- Prefer expanding the shared execution infrastructure over adding ad hoc command-specific logic.
- When forced to choose, protect output compactness and repository safety over feature completeness.
- Before adding more parsing, ensure the raw log is always sufficient to debug unexpected failures.
- Any new long-running command must define both a summary contract and a progress contract.
