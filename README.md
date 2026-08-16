# Setup-Sherpa

> Setup-Sherpa installs software on a fresh Linux system from simple TOML manifests — it works out the right install order, runs each step with the least privilege it needs, and hands over to you when an install needs a human.

Setup-Sherpa is a small command-line tool that sets up a fresh Linux machine from a folder of simple TOML files. Each file describes one thing to install — a package, a Docker container, a compose project, or a script — and Sherpa figures out the right order, runs each step with the least privilege it needs, and pauses to let you handle anything that needs a human.

It's built for a normal person setting up a machine in an afternoon, not for a systems engineer. (Debian-based systems only for now.)

## Quick start

Put your manifests in a folder, then run:

```bash
sudo sherpa run ./setup
```

Sherpa reads every `.toml` file in the folder, works out the install order, and runs the steps one at a time — root steps as root, user steps as you.

## A manifest

```toml
# ~/setup/portainer.toml
name = "portainer"
depends = ["docker"]              # install docker first
workdir = "~/apps/portainer"      # run bash steps from here

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

| Type | Runs as | What it does |
|---|---|---|
| `apt` | root | Install packages with apt-get |
| `repo` | root | Add a package repository (`.sources` file + gpg key) |
| `docker-run` | root | Deploy a container with `docker run` |
| `docker-volume` | root | Create a Docker volume |
| `compose` | root | Deploy a compose project with `docker compose` |
| `bash` | you | Run a custom bash script |

## How it works

- **Right order** — each manifest can say what it depends on. Sherpa builds the dependency graph and installs things in the right sequence, and stops with a clear message if two things depend on each other.
- **Safe to re-run** — every step checks whether it's already done before running. Install a package that's already there, and Sherpa skips it. If a run fails partway, you can just run it again and it picks up where it left off.
- **Least privilege** — run Sherpa with `sudo` once. Root steps run as root; user steps drop back to your account, so a script that only needs your own files never runs with root powers.
- **Hands over when needed** — some installs need a person (a wizard, a license prompt). Mark a step `interactive: true`, or let Sherpa notice when a step is waiting for input — it hands you the keyboard and continues when you're done.
- **Predictable commands** — Docker commands are written exactly as you'd type them, and Sherpa runs them without a shell in between, so nothing gets expanded or reinterpreted. Same manifest in, same result out.

## Design decisions

The full rationale for every choice (stack, privilege model, interactive handling, TOML format, passthrough commands, `workdir`, compose URLs) is in [`DECISIONS.md`](DECISIONS.md). The master plan is in [`PLAN.md`](PLAN.md).

## License

MIT.
