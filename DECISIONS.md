# Decisions

Locked design decisions for the Linux Fresh-Install Setup Tool, with rationale.
Decisions are made with the user in conversation and recorded here as the
authoritative record of *why*.

## D1 — Stack: C#/.NET, self-contained single-file binary

- **Choice:** C#/.NET. Publish as a self-contained single file:
  `dotnet publish -r linux-x64 --self-contained true /p:PublishSingleFile=true`.
- **Why:** the user is a C#/.NET dev with a strong maintainability bar — the
  tool must be something he can debug and maintain himself. Self-contained
  single-file means it runs as-is on a fresh Debian box with zero runtime
  install, satisfying "a normal user just runs it."
- **Note:** self-contained is fat (~70–90MB trimmed) — acceptable for a local
  tool. NativeAOT is a possible later leaner path but deferred; not worth the
  library-compat risk now.

## D2 — Privilege: single elevation + drop (Option B)

- **Choice:** the user runs `sudo setup run ./manifest.toml`. The tool detects it
  is root, reads `SUDO_USER` to learn the real user (uid/gid/home), runs root
  steps as root, and drops to the real user for user steps.
- **Why:** one password at launch instead of mid-flow (per-command sudo is
  awkward with interactive steps). Secure: user steps genuinely run as the
  invoking user, so a buggy config script can't touch system files.
- **Mechanics:** `SUDO_USER` env gives the real user; setuid/setgid + restored
  `$HOME`/`$USER` for user steps; elevate back for the next root step.
- **Guards:** fail fast with "run me with sudo" if not root; if there are zero
  root steps, tell the user sudo isn't needed.

## D3 — Interactive: declared + auto-detect, pty relay everywhere (Option C)

- **Choice:** a step can declare `interactive: true` (predictable path), AND the
  tool auto-detects a tty-stall and offers takeover (rescues the debconf
  surprise). Every step runs on a pseudo-terminal (pty); the tool is a
  transparent relay between the user's terminal and the child process.
- **Why declared + auto-detect:** a normal user won't always know a step is
  interactive (apt debconf prompts surprise people). Declared is reliable when
  known; auto-detect catches the surprises. pty-everywhere is required for the
  auto-detect to work AND for apt's own UI (progress bars, whiptail) to render
  correctly.
- **Why pty:** a child on a pipe is not on a terminal (`isatty` false) — wizards
  degrade and keyboard input breaks. On a pty, the child sees a real terminal;
  the tool relays bytes and can watch every byte the child writes.
- **Resume:** "done" = the interactive child process exits. The tool then moves
  to the next step on its own. No extra signal from the user.
- **Scope:** always local sudo'd terminal — so the "no terminal attached" case
  is a two-line fail-fast check, not a feature.

## D4 — Format: TOML (Tomlyn)

- **Choice:** TOML via the Tomlyn library.
- **Why:** YAML is rejected (parsers, spec, no `System.Yaml` in the BCL,
  YamlDotNet errors that don't point at the offending line). TOML eliminates the
  two worst config footguns for a normal user: no indentation-as-structure (a
  user literally cannot break the file with whitespace) and no Norway-problem
  scalar coercion (`yes`/`on`/`1.0` stay strings). Native `#` comments (how you
  document steps), native `"""` multi-line strings for bash blocks, and Tomlyn
  gives line/column errors for actionable diagnostics.
- **Quirk:** `[[step]]` array-of-tables is the one unusual bit — repeat
  `[[step]]` to add a step. Documented, stable, standard.
- **Rejected:** YAML (above), JSONC (`System.Text.Json` is rock-solid but
  quote-heavy and no native multi-line strings for bash blocks).

## D5 — Passthrough command model with a deterministic tokenizer

- **Choice:** `docker-run`, `docker-volume`, and `compose` take a raw `command`
  string, tokenized by a deterministic C# tokenizer — no shell expansion, no
  globbing, no `$`, no shell operators. The author writes the docker invocation
  they already know, every flag is supported (no schema ceiling), and identical
  input yields identical args every run.
- **Why raw command, not decomposed fields:** a field-by-field schema forces the
  author to re-type the invocation in a different structure than docker itself,
  and any flag not anticipated (`--network`, `--env-file`, `--mount`, health
  checks) silently becomes unsupported. Passthrough removes the ceiling.
- **Why a C# tokenizer, not `bash -c`:** `bash -c` is a reproducibility
  liability for a setup tool — globbing expands `*` against run-time cwd, `$`
  resolves against run-time environment, and `;`/`&&`/`|` are live. A
  deterministic tokenizer splits on whitespace + quotes only; everything else is
  literal. Same manifest in → same bytes to docker out, every run.
- **Line between step types:** shell-adjacent types (`docker-run`,
  `docker-volume`, `compose`) are quoted-literal via the tokenizer; `bash` is a
  real script and goes through a shell. Consequence: no `$HOME` / `$(...)` in
  docker commands — an explicit variable mechanism may come later.
- **Volume creation** is its own step type `docker-volume`, not a field on
  `docker-run`. Docker auto-creates named volumes on first reference, so
  `docker-volume create` is only needed when creation must be explicit.

## Open / deferred

- **`only_if` hook for bash idempotency** — bash steps have no automatic
  idempotency check; a declarative guard may come later.
- **NativeAOT** — leaner publish, deferred (see D1).
- **Portainer deployer** — later second `IComposeDeployer` impl; interface now,
  impl later.
