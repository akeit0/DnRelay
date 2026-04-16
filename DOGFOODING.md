# DnRelay Dogfooding Notes

This file is specific to developing `dnrelay` itself inside this repository.
It is intentionally separate from [BEST_PRACTICES.md](BEST_PRACTICES.md), which is the generic product-facing guidance.

## Core Rule

- Prefer `dotnet dnrelay ...` over raw `dotnet ...` whenever the installed local tool is good enough for the task.
- Prefer `dotnet dnrelay build` over raw `dotnet build` by default.
- Prefer `dotnet dnrelay run --project <path>` over manually doing build-then-run in two separate commands.
- Use raw `dotnet` only for bootstrap or break-glass recovery when `dnrelay` itself is too stale or broken to use.
- Leave repo-local generated state under `.dnrelay\`, including default logs under `.dnrelay\logs\` and lock files under `.dnrelay\locks\`.

## Self-Lock Conditions

- The most common self-lock is: run a source-built `DnRelay.dll` or `DnRelay.exe`, then rebuild the same output directory with raw `dotnet build`.
- Another common self-lock is: keep a Debug or Release apphost running from `bin\<Configuration>\...`, then pack, rebuild, or update that same project into the same output path.
- In normal repo work this should be rare, because build and run should usually go through `dnrelay`.

## Recommended Validation Loop

- If the installed tool is sufficient, use `dotnet dnrelay ...` directly.
- If you need build-then-run behavior, prefer `dotnet dnrelay run --project <path>` rather than a separate `build` followed by direct `dotnet run`.
- If you need freshly edited but not yet refreshed behavior:
  - bootstrap once with raw `dotnet build` only if needed
  - execute via `dotnet <path-to-DnRelay.dll> ...`
  - avoid rebuilding the same output path while that DLL or apphost is still running
- As soon as practical, refresh the local tool and return to `dotnet dnrelay ...`.

## Run

- For normal execution, use `dotnet dnrelay run --project <path>`.
- Do not add top-level `--no-build` unless you explicitly want to skip `dnrelay`'s build phase.
- `dnrelay run` already does the coordinated build-then-run split internally.
- That makes `dnrelay run` the default choice even when your intent is effectively "build first, then run".

## Cleanup

- If a repo-local `testhost`, benchmark process, or long-running run process is left behind, use `dotnet dnrelay stats` first.
- Prefer `dotnet dnrelay kill <id>` over external task-kill commands so the cleanup path is dogfooded too.
- Use `dotnet dnrelay stats --json` when validating the machine-facing process-management contract.

## JSON

- Use `--json` when validating machine-facing contracts.
- Prefer checking stable fields first:
  - `build`: `status`, `warnings`, `errors`
  - `test`: `status`, `total`, `passed`, `failed`, `skipped`
  - `run`: `status`, `exitCode`
  - `bench`: `status`, `reason`, `artifacts`, `selectionRequired`
- Do not treat `outputTail` as a strict contract.
