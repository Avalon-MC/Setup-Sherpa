# AGENTS.md — Setup-Sherpa

Setup-Sherpa is a C#/.NET CLI that installs a set of TOML **installation manifests**
in dependency order, with per-step privilege reduction and interactive handover. A
normal user points it at a directory of manifests; it installs them, tracking what's
done in a `.sherpa` state file. Targets Debian-based Linux. The manifest format and
design rationale are in `PLAN.md` and `DECISIONS.md`.

## Dev environment

- .NET SDK 10 (net10.0). `ImplicitUsings` and `Nullable` are enabled everywhere.
- TOML via **Tomlyn 2.10.1** — used for both loading manifests and serializing the
  `.sherpa` state. Do not upgrade to a newer major without re-probing the API
  (2.x uses `TomlSerializer.Deserialize<T>`, not the old `Toml.ToModel`).
- No linter, no formatter config. Build emits no warnings — keep it that way.

## Build & test

```bash
dotnet build                          # whole solution
dotnet test tests/SetupTool.Tests     # full suite (xunit)
dotnet test tests/SetupTool.Tests --filter "FullyQualifiedName~ClassName"   # one class
dotnet run --project src/SetupTool.Cli -- run <dir-or-file>   # run the CLI
```

- `SetupTool.slnx` wires `src/SetupTool.Cli`, `src/SetupTool.Core`, `tests/SetupTool.Tests`.
- The CLI is exercised end-to-end with safe bash-only manifests under `examples/`
  (e.g. `dotnet run --project src/SetupTool.Cli -- run examples/deps`).

## Layout

- `src/SetupTool.Core/` — the engine, split by feature folder: `Manifest/`
  (model + TOML loader), `Planning/` (dependency DAG + topo sort), `Execution/`
  (executors, process runner, privilege, interactive watchdog, orchestrator),
  `State/` (`.sherpa`).
- `src/SetupTool.Cli/` — thin `Program.cs` (arg parsing, wiring).
- `tests/SetupTool.Tests/` — xunit. Executor/orchestrator tests use a `FakeRunner`
  (implements `IProcessRunner`, records `ProcessSpec`, returns scripted responses)
  and `FakeDownloader` via the `TestContext.Make` helper — **never** hit real docker/apt.
- `examples/` — reference manifests. `configurations/` is a git submodule (CorgiPhoenixDeploy).

## Conventions

- **Executors**: implement `IStepExecutor` (a `StepType Type` + `ExecuteAsync(StepContext, CancellationToken)`).
  Register in the `executors` array in `Program.cs`/`Orchestrator` constructor.
  Each executor is idempotent: it checks "already present" before running.
- **Errors**: throw typed exceptions — `ManifestException` (load/schema),
  `PlanException` (dependency graph), `StepFailedException` (a step's process exited nonzero).
  Catch them in `Program.Main`; print `✗ <message>`, return nonzero.
- **Manifest schema**: `[[step]]` with a `type` (apt, repo, docker-run, docker-volume,
  compose, bash) plus `name`, `depends`, `workdir`. `docker-run`/`docker-volume` take a
  raw `command` string tokenized by `CommandTokenizer` (no shell expansion — D5).
- **Step types** use `StepTypes.TryParse` (kebab-case names like `docker-run`; a plain
  `Enum.TryParse` fails on the hyphen).
- **Commits**: one descriptive past-tense line ("Add .sherpa state: …", "Phase 2: …").
  Commits are authored as Hermes: `git -c user.name="Hermes" -c user.email="hermes@localhost" commit`.

## Pitfalls

- **`.sherpa` is always-skip-if-marked** by design — no content hash, no `--force`.
  Once a manifest is marked installed, editing it will NOT re-run it. To force a
  re-run, delete its entry (or the whole `.sherpa` file). Manifests are built once
  from tested commands and are never meant to re-run; per-step idempotency exists
  only to make a failed-midway retry (and overlapping steps in new manifests) cheap.
- **Interactive watchdog** thresholds live in `ProcessRunner.RunInteractiveAsync`
  (8s stall / 30s rearm). Interactive steps need a real terminal; they run under
  `script` (a pty) with stdout redirected for relay. Don't change the pty approach
  casually — it's what makes debconf/wizard prompts work.
- **Binary name mismatch (open)**: the project/assembly is `SetupTool.*` and
  `setuptool` in some code, but the README and `PrintUsage` say `sherpa`. Aligning
  the binary name to `sherpa` is deferred to the publish phase. Don't "fix" it piecemeal.
- **Don't run real docker/apt on the dev box** — Bazzite/immutable, and the executors
  need a real Debian target (Phase 5). Tests must use the fakes.
- **`configurations/CorgiPhoenix` is a submodule** — its contents are not part of this
  repo's tree; commit the gitlink, not the files.
