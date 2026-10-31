# NexusKeypad connectors

One connector folder for the three hosts, one shared native program and one Logitech runtime. No new MCP server is introduced.

This folder is also the `nexus-keypad` plugin of NexusSystem's catalog for Claude Code and Codex (git-subdir, pinned at
the release tag, same version as the product): nothing in it depends on this PC. Hooks reach the shared program at its
stable place, `%LOCALAPPDATA%\NexusKeypad\hook\nexuskeypad-hook.exe`.

- Shared: `skills/nexus-keypad/SKILL.md` (one guide), `assets/`, `LICENSE`.
- Claude: `.claude-plugin/plugin.json`, `hooks/hooks.json` (lifecycle hooks), `hooks/register.tsx` and `hooks/quota.ts`
  (modules: the Nexus Relay line under the prompt, the exact usage for the pad), `types/`, `tsconfig.json`.
- Codex: `.codex-plugin/plugin.json`, its lifecycle hooks inline (`cmd.exe` expands `%LOCALAPPDATA%`).
- Antigravity: root `plugin.json` + `hooks.json`; installation renders the program path in that `hooks.json` and
  migrates only the former NexusKeypad global handlers.

When an AI app has NexusSystem's copy (`nexus-keypad@nexus-plugins`) installed, the connector adopts it in place: no
second identity, no local catalog. Otherwise installation copies the same files locally (`Connecteurs.PlanConnecteur`):
Claude through its official plugin manager and a local `nexuskeypad` catalog, Codex through the personal catalog.

The build copies this directory next to `nexuskeypad-hook.exe`.

The helper accepts `connecteur <claude|codex|antigravity> <status|enable|disable>`. `status` is read-only. Its `Pret` state is a configuration proof, not an end-to-end app proof: the keypad adds session/port checks and rejects unreadable profiles.

Disabling a host plugin gates observation in the pad and the native completion notes of Claude and Antigravity, including callbacks already loaded by a running process. No Antigravity global hook is installed by the connector. Former Python sound hooks become silent after migration to prevent duplicate notes.

Codex's native hooks are passive, short-lived commands and require the host's review and trust before execution. Installation never fabricates trust or bypasses this approval. Hook files and plugin activation prove configuration only; logs preserve normal activity tracking while review is pending. Hooks add immediate parent-turn events without replacing asynchronous-question and terminal-state handling in the logs, or adding a second completion sound.
