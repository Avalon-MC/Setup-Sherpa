# Setup-Sherpa

> Declarative Debian setup: write TOML manifests, Sherpa installs them in the right order, drops privileges per step, and hands over when an install needs you.

A small CLI that installs a set of **installation manifests** in dependency order, with stepped execution, per-step privilege reduction, and real interactive handover. Aimed at a normal user setting up a Debian box in an afternoon — not a configuration-management system (not Ansible).

## Why

The gap: Ansible et al. are overkill (a degree in systems engineering), and a pile of shell scripts has no dependency graph, no idempotency, and no way to hand an interactive install to a human. Setup-Sherpa sits between them: describe what you want on a box, in a file a normal person writes, and Sherpa figures out order, privilege, and when to hand you the keyboard.

## Quick start

```bash
# Run via sudo so user steps can drop privileges
sudo sherpa run ./portainer.toml
```

Sherpa loads the manifest (plus its dependencies), plans install order, and executes steps sequentially — root steps as root, user steps dropped to the invoking user.

## A manifest

```toml
# ~/setup/portainer.toml
name = "portainer"
depends = ["docker"]
workdir = "~/apps/portainer"        # manifest-level default for bash steps

[[step]]
type = "repo"
source = "https://download.docker.com/linux/debian"
keyring = "https://download.docker.com/linux/debian/gpg"
components = ["stable"]

[[step]]
type = "apt"
update = true
packages = ["docker-ce", "docker-ce-cli", "docker-compose-plugin"]

[[step]]
type = "compose"
project = "portainer"
file = "@url:https://downloads.portainer.io/ce-sts/portainer-compose.yaml"

[[step]]
type = "bash"
script = """
chmod 700 .
"""
```

## Step types

| Type | Privilege default | Does |
|---|---|---|
| `apt` | root | Install packages via apt-get; optional `update` |
| `repo` | root | Add a custom Debian repository (`.sources` file + gpg key) |
| `docker-run` | root | Deploy a container via `docker run` (raw `command` string) |
| `docker-volume` | root | Create a docker volume (raw `command` string) |
| `compose` | root | Deploy a compose project via `docker compose` |
| `bash` | user | Run a custom bash block (override to root if needed) |

## How it works

- **Dependency ordering** — each manifest declares `depends: [name]`; Sherpa builds a DAG, topologically sorts, and hard-fails on a cycle with a readable message.
- **Stepped execution** — sequential numbered steps, stop-on-failure. Each step is **idempotent** (apt → installed? repo → sources file exists? docker-run → container by `--name` running/stopped/absent? compose → `docker compose ls`?) so a crashed run is safely re-runnable.
- **Privilege reduction** — run via `sudo` once; Sherpa reads `SUDO_USER` and drops to the invoking user for user steps (via `setpriv`). One password at launch, and user steps genuinely run as you.
- **Interactive handling** — a step can declare `interactive: true`, and Sherpa also auto-detects a tty-stall and offers a takeover hint. Every interactive step runs on a pty so wizards and debconf prompts behave like a real terminal.
- **Reproducible commands** — `docker-run`/`docker-volume` take a raw `command` string tokenized deterministically (no globbing, no `$`, no shell operators). Same manifest in → same bytes to docker out, every run.

## Design decisions

The full rationale for every choice (stack, privilege model, interactive handling, TOML format, passthrough commands, `workdir`, compose URLs) is in [`DECISIONS.md`](DECISIONS.md). The master plan is in [`PLAN.md`](PLAN.md).

## Roadmap

- **Phase 5 (next)** — self-contained single-file publish; verify on a clean Debian box.
- **Phase 6 (later)** — Portainer API deployment as a second `IComposeDeployer` implementation (the seam is already in place; manifests don't change).

## License

MIT.
