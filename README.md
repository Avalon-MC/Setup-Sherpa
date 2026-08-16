# Setup-Sherpa

> Setup-Sherpa installs software on a fresh Linux system from simple TOML manifests — it works out the right install order, runs each step with the least privilege it needs, and hands over to you when an install needs a human.

A small CLI that installs a set of **installation manifests** in dependency order, with stepped execution, per-step privilege reduction, and real interactive handover. Aimed at a normal user setting up a fresh Linux box in an afternoon (Debian-based systems only for now) — not a configuration-management system (not Ansible).

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
| `repo` | root | Add a custom package repository (`.sources` file + gpg key) |
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

## License

MIT.
