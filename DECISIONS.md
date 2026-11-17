# Decisions

The main choices behind Claude Usage Meter, what they cost, and the ones made when it was published.
Each entry is short on purpose: the context, the decision, and the consequence.

## Reading the usage

### 1. Only the official Claude Code CLI, never tokens or the API
**Context.** The numbers live behind the user's Claude sign-in. Some community widgets read the OAuth token from
`~/.claude/.credentials.json` and call the usage endpoint directly.
**Decision.** The meter runs exactly one command, `claude -p /usage --output-format text --no-session-persistence`,
and parses its text. It never reads credential files or tokens, never calls Anthropic's API, console or OAuth
endpoints, and never sends a message to a model.
**Consequence.** The meter stays inside what Claude Code itself does and needs no secrets of its own. Anthropic's
Consumer Terms don't allow subscription tokens to be used by other tools, and a model call would spend the very usage
being measured. The price is a dependency on the CLI being installed and signed in, and on the shape of its output.

### 2. Verify that `/usage` costs nothing before polling it
**Context.** A meter that polls every two minutes must not use up the plan it measures.
**Decision.** Before any polling code was written, `/usage` was run by hand with JSON output and checked: 0 turns,
0 input and output tokens, $0. It is a local command that prints the usage panel without starting a model turn.
**Consequence.** Polling is free. If a future CLI ever made `/usage` start a model turn, that would be a reason to
stop polling, not to work around it.

### 3. A generic parser instead of a fixed list of blocks
**Context.** Block names change: weekly limits are per model, and model names change.
**Decision.** Any line shaped `<label>: <n>% used · resets <when>` is a block. Reset times are parsed from the forms
the CLI prints (month and day, 12 or 24 hour time, an IANA zone in brackets, or relative "in 2h 14m"). When a reset
time can't be parsed, the raw text is shown instead of a countdown.
**Consequence.** A renamed or new block appears without an update. Lines in the CLI's "What's contributing to your
usage?" section (`83%  Start fresh...`) don't match, because they have no `label:` and no `used`; a test covers that.

### 4. Keep background polls out of running Claude Code sessions
**Context.** A process started from inside a Claude Code session inherits that session's identity and IPC handles
through environment variables (session id, messaging socket, IDE port). An early manual test showed that a `/usage`
run with those inherited can coincide with the parent session being interrupted. A plain run also fires the user's
hooks and connects their MCP servers.
**Decision.** The CLI is started with a clean environment: every `CLAUDE*`, `ANTHROPIC_*`, `VSCODE_*`, `MCP_*`,
`ELECTRON_*`, `TERM_PROGRAM*` and `GIT_ASKPASS` variable is removed unless the user set it persistently in Windows,
and `--safe-mode` is added so hooks, MCP servers, plugins and `CLAUDE.md` stay off. If a CLI version rejects
`--safe-mode`, the meter drops it automatically.
**Consequence.** Polls ran next to two sessions that were mid-turn without disturbing either, and got faster
(about 7 s down to about 2 s). User configuration such as `CLAUDE_CONFIG_DIR` still reaches the CLI.

### 5. Don't set `CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC`
**Context.** The first version set it for the child process to keep polls quiet. With it, `/usage` doesn't fetch the
limits itself: it prints them only if a normal Claude Code run fetched them a few minutes earlier, and otherwise
prints the panel without the limit lines. The numbers appeared while the user was active in Claude Code and
disappeared when they weren't.
**Decision.** Set the narrower `DISABLE_TELEMETRY`, `DISABLE_ERROR_REPORTING` and `DISABLE_AUTOUPDATER` instead.
A read that still comes back without limits is classified as **Incomplete**: one quiet retry after 3 seconds, then
the last numbers stay, dimmed.
**Consequence.** Reliable numbers without telemetry or update traffic from the polls. The meter itself still makes
no network calls.

## Showing it

### 6. Honest states over pretty numbers
**Context.** A meter that shows stale or invented numbers is worse than none.
**Decision.** Short failures (timeout, offline, an incomplete read) keep the last good numbers for up to 10 minutes,
visibly dimmed with "Couldn't refresh". After that, or immediately when the CLI is signed out or missing, the meter
shows `--` with a reason and an action (Sign in, Open settings, Try again).
**Consequence.** What you see is always either fresh, clearly marked as old, or clearly missing.

### 7. .NET 8 WPF, with its own thin Windows wrappers
**Context.** The app lives on the taskbar all day, so it must be light and feel native. Tauri (web UI) was the other
candidate.
**Decision.** WPF on .NET 8, with small P/Invoke wrappers for the tray icon (`Shell_NotifyIcon` v4, no WinForms),
DWM effects (acrylic, rounded corners) and taskbar geometry. Alerts use the tray balloon, which Windows 11 shows as a
native toast, instead of the WinRT toast API.
**Consequence.** Native look and behaviour, no WebView, and no Windows SDK projection (about 25 MB saved).

### 8. The strip is a topmost window over the taskbar, not an Explorer add-in
**Context.** Windows 11 has no supported API for taskbar widgets, and injecting into Explorer is fragile.
**Decision.** A borderless, non-activating, topmost tool window placed over the taskbar. It re-asserts topmost when
the foreground window changes, registers as an AppBar only to be told about full-screen apps, hides for full-screen
windows and auto-hide, and stores its position as a fraction of the taskbar per monitor.
**Consequence.** Nothing is injected into the shell. The known cost: on recent Windows 11 builds the taskbar's app
buttons draw above it, so the strip belongs on empty taskbar space.

### 9. Footprint over a smaller download
**Context.** The target was under 60 MB of RAM and near-zero CPU between refreshes.
**Decision.** Software rendering by default (39 vs 70 MB with GPU rendering, and it looks the same for this UI), a
working-set trim after each refresh and when the popup closes, and a single-file publish **without** compression.
**Consequence.** About 44 MB of private memory idle. The exe is larger (single-file compression would unpack every
assembly into memory and roughly double the RAM), but the zip is about 58 MB either way.

## Publishing it

### 10. Unofficial, and looks it
**Context.** The app shows Claude usage, but it is not an Anthropic product, and a familiar logo would suggest it is.
**Decision.** No Anthropic or Claude logos or artwork. The icon is an original teal progress ring, the accent colours
are neutral, and the README, the About window and the release notes say that the project is not affiliated with or
endorsed by Anthropic and that Claude is Anthropic's trademark.
**Consequence.** The name describes what the app works with; nothing else implies an official tool.

### 11. Made-up data in tests, screenshots and the GIF
**Context.** Real `/usage` output contains a person's usage, their time zone and which tools and MCP servers they use.
**Decision.** Test fixtures use made-up numbers and example names. Screenshots and the GIF come from the app's demo
mode (`--demo`, `--demo-cycle`), which never runs the CLI, and were cropped to the meter only.
**Consequence.** Nothing personal is in the repository, and the visuals are reproducible.

### 12. Published as one clean commit under the MIT licence
**Context.** The working repository's history contained early names, an earlier logo and real CLI output.
**Decision.** Publish the final tree as a single commit in a new repository, after a scan for secrets, email
addresses and local paths, under the MIT licence.
**Consequence.** Nothing from the old history can leak, at the cost of losing the commit-by-commit story.
