# Linux Fresh-Install Setup Tool

A small CLI that installs a set of **installation manifests** in dependency order,
with stepped execution, per-step privilege reduction, and real interactive
handover. Aimed at a normal user setting up a Debian box in an afternoon —
not a configuration-management system (not Ansible).

## Why this exists

The gap: Ansible et al. are overkill (a degree in systems engineering), and a
pile of shell scripts has no dependency graph, no idempotency, and no way to
hand an interactive install to a human. This tool sits between them: describe
what you want on a box, in a file a normal person writes, and the tool figures
out order, privilege, and when to hand you the keyboard.

## Goals (v1)

- **Manifests** — declarative, TOML, one manifest = one unit of install.
- **Dependency ordering** — `depends` between manifests; topological sort;
  readable hard-fail on a cycle.
- **Stepped execution** — sequential numbered steps, stop-on-failure, idempotent
  so a crashed run is safely re-runnable.
- **Privilege reduction** — run via `sudo` once; tool drops to the invoking user
  (`SUDO_USER`) for user-level steps.
- **Interactive handling** — hand a step over to the human and resume when it
  finishes; pty relay makes interactive installs actually work.
- **Idempotency per step type** — skip if already present; re-runs are safe.
- **Self-contained binary** — single file that runs on a fresh Debian box, zero
  runtime install.

## Step types (v1)

| Type | Privilege default | Does |
|---|---|---|
| `apt` | root | Install packages via apt-get; optional `update` |
| `repo` | root | Add a custom Debian repository (`.sources` file + gpg key) |
| `docker-run` | root | Deploy a container via `docker run` |
| `compose` | root | Deploy a compose project via `docker compose` |
| `bash` | user | Run a custom bash block (override to root if needed) |

## Out of scope (v1)

- Version-level dependency handling (only "X depends on Y").
- Non-Debian targets (Debian-based only for now).
- Portainer deployment (a later expansion — see below).
- Headless/CI operation (this tool is always run from a local sudo'd terminal).

## Portainer expansion (later)

Deploying compose via the Portainer API is the same conceptual step as local
compose ("deploy this compose project") with a different backing. Model the
deployer as `IComposeDeployer` now; Portainer is a second implementation.
Manifests do not change. **Build the interface now, not the Portainer impl.**

## Doc map

| Doc | Contents |
|---|---|
| `PLAN.md` | This file — master plan, source of truth, entry point |
| `DECISIONS.md` | Locked decisions with rationale |
| `examples/` | Sample manifests (normal + interactive) |

## Phase breakdown

1. **Core engine** — manifest load (TOML), schema validation, dependency DAG +
   topological sort, cycle detection, ordered step model.
2. **Executors** — `apt`, `repo`, `docker-run`, `compose` (behind
   `IComposeDeployer`), `bash`; each with idempotency checks.
3. **Privilege + run model** — sudo detection, `SUDO_USER` drop, pty relay,
   sequential numbered run, stop-on-failure.
4. **Interactive** — declared `interactive: true` + auto-detect tty-stall,
   takeover prompt, transparent relay during interactive steps.
5. **Publish** — self-contained single-file publish; verify on a clean Debian box.
6. **Portainer** (later) — second `IComposeDeployer` impl against the Portainer API.
