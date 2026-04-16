# DnRelay Best Practices

This file captures practical usage guidance for `dnrelay` itself.
It is a product-usage reference, not a repo-specific dogfooding guide.

## General

- Prefer `dotnet dnrelay ...` over raw `dotnet ...` for normal build, test, run, and bench work.
- Treat the text summary as the first stop and the log file as the follow-up source.
- Use `--json` when another tool or agent needs stable machine-readable output.
- Leave `DOTNET_CLI_UI_LANGUAGE` alone unless you have a specific reason to change it; `dnrelay` defaults child `dotnet` processes to English for more stable logs and summaries.

## Build

- Use `dotnet dnrelay build <target>` as the default way to compile in repos that use `dnrelay`.
- Let `dnrelay` handle locking; do not try to manually serialize builds yourself.
- Use `--logs-dir` or `dnrelay config logs <path>` when the repo wants logs under a custom folder such as `.artifacts\logs`.

## Test

- Prefer `dotnet dnrelay test <target>` over raw `dotnet test` so `.trx`, lock handling, timeout control, and compact summaries stay consistent.
- Use the summary first for `total`, `passed`, `failed`, and `skipped`, then open the `.trx` or full log if needed.

## Run

- In normal use, run `dotnet dnrelay run --project <path>` without `--no-build`.
- `dnrelay run` already performs a coordinated build phase when needed and then uses `dotnet run --no-build` internally.
- Pass top-level `--no-build` only when you explicitly want to suppress `dnrelay`'s build phase because you already know the outputs are current.
- Use `--raw` when you want the child process output to stream directly to the terminal instead of relying on the compact tail summary.

## Stats And Kill

- Use `dotnet dnrelay stats` to inspect active dnrelay processes and lock owners for the current repository.
- Use `dotnet dnrelay stats --json` when another agent needs machine-readable process and lock state.
- Use `dotnet dnrelay kill <id>` with a process id shown by `stats`.
- Use `dotnet dnrelay kill *` only when you intentionally want to clear every dnrelay-related process for that repository.
- Treat `alreadyGonePids` and repo-local stale-record cleanup as normal maintenance outcomes, not command failures.

## JSON

- Prefer `--json` when another agent, script, or tool is going to inspect the result programmatically.
- Treat the text summary as the human default and JSON as the stable machine-facing contract.
- For `build`, read `status`, `warnings`, `errors`, `topWarnings`, and `topErrors` first.
- For `test`, read `status`, `total`, `passed`, `failed`, `skipped`, `trx`, and `topFailures` first.
- For `run`, read `status`, `exitCode`, and `outputTail` first.
- For `bench`, read `status`, `reason`, `artifacts`, `selectionRequired`, `availableBenchmarks`, and `highlights` first.
- In multi-benchmark projects, prefer:
  - `dnrelay bench --json --list`
  - then `dnrelay bench --json --select <index>`
- Do not parse `outputTail` as a strict contract. It is a bounded convenience field, not the primary structured result.

## Bench

- Use `dnrelay bench --list` first when benchmark names are not already known.
- Prefer `--select <index>` for deterministic follow-up commands in multi-benchmark projects.
- Keep benchmark samples fast with `ShortRun`-style jobs so the command surface can be dogfooded frequently.

## Config

- Use `dotnet dnrelay config logs <path>` to create `.dnrelay\config.json` instead of hand-editing the file.
- Keep repo-local config and lock files together under `.dnrelay\`.
- If you do not configure anything, logs default to `.dnrelay\logs\`.
