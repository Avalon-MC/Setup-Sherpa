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
dotnet test tests/SetupSherpa.Tests    # full suite (xunit)
dotnet test tests/SetupSherpa.Tests --filter "FullyQualifiedName~ClassName"   # one class
dotnet run --project src/SetupSherpa.Cli -- run <dir-or-file>   # install
dotnet run --project src/SetupSherpa.Cli -- plan <dir-or-file>  # print order only, no install, no .sherpa
```

- `SetupSherpa.slnx` wires `src/SetupSherpa.Cli`, `src/SetupSherpa.Core`, `tests/SetupSherpa.Tests`.
- The CLI is exercised end-to-end with safe bash-only manifests under `examples/`
  (e.g. `dotnet run --project src/SetupSherpa.Cli -- run examples/deps`).

## Layout

- `src/SetupSherpa.Core/` — the engine, split by feature folder: `Manifest/`
  (model + TOML loader), `Planning/` (dependency DAG + topo sort), `Execution/`
  (executors, process runner, privilege, interactive watchdog, orchestrator),
  `State/` (`.sherpa`).
- `src/SetupSherpa.Cli/` — thin `Program.cs` (arg parsing, wiring).
- `tests/SetupSherpa.Tests/` — xunit. Executor/orchestrator tests use a `FakeRunner`
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
- **Manifest schema**: top-level `name` (required, unique), `depends`, `workdir`,
  `installOrder` (-100..+100, higher installs closer to first, never overrides a
  dependency), then `[[step]]` with a `type` (apt, repo, docker-run, docker-volume,
  compose, bash). **`name` may differ from the filename** — dependencies resolve by
  the manifest's `name` field (scanned from every .toml in the directory), not by
  filename. `docker-run`/`docker-volume` take a
  raw `command` string tokenized by `CommandTokenizer` (no shell expansion — D5).
- **`.env` expansion**: `docker-run`/`docker-volume` steps may list `expansionTokens`
  (bare `.env` key names). Sherpa substitutes `$VAR`/`${VAR}` in the raw command
  from a single `.env` in the run target dir (created blank if missing, never
  committed — see `.gitignore`). Substitution happens BEFORE tokenization; unlisted
  `$VAR` stays literal; a listed-but-missing token throws. Compose passes `--env-file`
  (YAML interpolation only). Bash is untouched (`$VAR` is shell there). Values are
  redacted from any surfaced command/error.
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
- **Binary name**: the CLI's assembly is named `sherpa` (via `<AssemblyName>sherpa</AssemblyName>`
  in `SetupSherpa.Cli.csproj`), so the invoked command is `sherpa` (e.g. `sudo sherpa run <dir>`).
  The product is Setup-Sherpa; the namespace/dirs are `SetupSherpa.*`.
- **Don't run real docker/apt on the dev box** — Bazzite/immutable, and the executors
  need a real Debian target (Phase 5). Tests must use the fakes.
- **`configurations/CorgiPhoenix` is a submodule** — its contents are not part of this
  repo's tree; commit the gitlink, not the files.
