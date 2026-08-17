# Setup-Sherpa

> Setup-Sherpa installs software on a fresh Linux system from simple TOML manifests — it works out the right install order, runs each step with the least privilege it needs, and hands over to you when an install needs a human.

Setup-Sherpa is a small command-line tool that sets up a fresh Linux machine from a folder of simple TOML files. Each file describes one thing to install — a package, a Docker container, a compose project, or a script — and Sherpa figures out the right order, runs each step with the least privilege it needs, and pauses to let you handle anything that needs a human.

It's built for a normal person setting up a machine in an afternoon, not for a systems engineer. (Debian-based systems only for now.)

## Quick start

Put your manifests in a folder, then run:

```bash
sudo sherpa run ./setup
```

Sherpa reads every `.toml` file in the folder (including up to two levels of
sub-folders), works out the install order, and runs the steps one at a time —
root steps as root, user steps as you.

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
| `wait` | you | Print a message and pause until you press Enter |
| `env-input` | you | Prompt for a value and store it in `.env` |
| `copy` | you | Copy a file or folder into place |
| `extract` | you | Unpack a `.tar.gz` archive into a folder |
| `systemd` | root | Install a systemd unit (and optionally enable/start it) |

## How it works

- **Right order** — each manifest can say what it depends on. Sherpa builds the dependency graph and installs things in the right sequence, and stops with a clear message if two things depend on each other.
- **Safe to re-run** — every step checks whether it's already done before running. Install a package that's already there, and Sherpa skips it. If a run fails partway, you can just run it again and it picks up where it left off.
- **Least privilege** — run Sherpa with `sudo` once. Root steps run as root; user steps drop back to your account, so a script that only needs your own files never runs with root powers.
- **Hands over when needed** — some installs need a person (a wizard, a license prompt). Mark a step `interactive: true`, or let Sherpa notice when a step is waiting for input — it hands you the keyboard and continues when you're done.
- **Predictable commands** — Docker commands are written exactly as you'd type them, and Sherpa runs them without a shell in between, so nothing gets expanded or reinterpreted. Same manifest in, same result out.

## `.env` secrets

A single `.env` next to your manifests holds values for steps. Reference them
with `expansionTokens` on a `docker-run`, `docker-volume`, or `bash` step:

```toml
[[step]]
type = "docker-run"
expansionTokens = ["MSSQL_SA_PASSWORD"]
command = "-d -e MSSQL_SA_PASSWORD=$MSSQL_SA_PASSWORD --name sql1 mcr/sql:latest"
```

Sherpa substitutes `$VAR`/`${VAR}` for the listed tokens from `.env` (values are
shell-quoted for `bash` steps), never commits it (see `.gitignore`), and hides
the values from any output. Compose steps get `.env` via `--env-file` for YAML
interpolation when the file exists. Unlisted `$VAR` is left literal; a
listed-but-missing token stops the run. An `env-input` step writes a value you
type into `.env` so later steps can use it.

## Design decisions

The full rationale for every choice (stack, privilege model, interactive handling, TOML format, passthrough commands, `workdir`, compose URLs) is in [`DECISIONS.md`](DECISIONS.md). The master plan is in [`PLAN.md`](PLAN.md).

## License

MIT.

## Manifest schema

A manifest is a single `.toml` file. Its `name` may differ from the filename; `depends` resolves by the `name` field.

| Field | Type | Required | Notes |
|---|---|---|---|
| `name` | string | yes | Unique; other manifests reference it via `depends` |
| `depends` | array of strings | no | Manifests that must install first |
| `workdir` | string | no | Default working dir for steps; resolved per the step `workdir` rules |
| `installOrder` | int (-100..+100) | no | Higher installs closer to first; never overrides a `depends` edge |
| `[[step]]` | array of tables | yes (≥1) | The install steps, in order |

## Step schema

Each `[[step]]` has a shared core plus type-specific fields. The `type` is always required.

| Field | Type | Required | Notes |
|---|---|---|---|
| `type` | string | yes | `apt`, `repo`, `docker-run`, `docker-volume`, `compose`, `bash`, `wait`, `env-input`, `copy`, `extract`, `systemd` |
| `privilege` | string | no | `root` or `user`. Defaults per type: `bash`/`wait`/`env-input`/`copy`/`extract` → `user`; `apt`/`repo`/`docker-run`/`docker-volume`/`compose`/`systemd` → `root` |
| `workdir` | string | no | Overrides the manifest `workdir`. `~/...` → user's home; absolute → `mkdir -p` on demand; relative → resolved against the manifest's directory |
| `interactive` | bool | no | Declares the step needs a human at the terminal |

**Per-type fields:**

| `type` | Fields |
|---|---|
| `apt` | `packages` (array, required), `update` (bool) |
| `repo` | `source` (string, required), `keyring` (string), `suite` (string, default `$VERSION_CODENAME`), `components` (array, default `["main"]`), `architectures` (string), `repo_name` (string) |
| `docker-run` | `command` (string, required) — raw docker args, no shell expansion; `expansionTokens` (array) — `.env` keys to substitute into the command |
| `docker-volume` | `command` (string, required) — raw docker args; `expansionTokens` (array) — `.env` keys to substitute |
| `compose` | `project` (string, required), `file` (string, required — path or `@url:https://...`) |
| `bash` | `script` (string, required); `expansionTokens` (array) — `.env` keys to substitute (shell-quoted) |
| `wait` | `message` (string, required) |
| `env-input` | `variable` (string, required), `secret` (bool, default `false` — set `true` to suppress echo) |
| `copy` | `src` (string, required — relative to the manifest's folder), `dest` (string, required) |
| `extract` | `archive` (string, required — relative to the manifest's folder), `dest` (string, required) |
| `systemd` | `unit` (string, required — path to the `.service` file), `name` (string, service name), `enable` (bool), `start` (bool) |
