# NexusKeypad connectors

One connector folder for the three hosts, one shared native program and one Logitech runtime. No new MCP server is introduced.

Shared: `skills/nexus-keypad/SKILL.md` (one guide) and `assets/`. Per host: Claude `.claude-plugin/` + `claude/` (hooks, usage module) + `types/`; Codex `.codex-plugin/` + `codex/hooks.json`; Antigravity root `plugin.json` + `hooks.json`. Installation maps each host's files to the layout that host expects (`Connecteurs.PlanConnecteur`).

- Claude: lifecycle hooks, the shared native completion note and the existing public usage module.
- Codex: setup/diagnostic skill, activation and native lifecycle hooks; the existing log reader remains available for history, quotas, conversation details and fallback.
- Antigravity: setup/diagnostic skill, activity hooks and completion note owned by the host plugin. Installation renders the native program path in its root `hooks.json` and migrates only the former NexusKeypad global handlers.

The build copies this directory next to `nexuskeypad-hook.exe`. It also supplies the existing public Claude quota module under `claude-quota/nexuskeypad-quota/hooks/register.ts`. Installation copies that file into the Claude connector; no source-tree or private OneDrive path is needed.

The helper accepts `connecteur <claude|codex|antigravity> <status|enable|disable>`. `status` is read-only. Its `Pret` state is a configuration proof, not an end-to-end app proof: the keypad adds session/port checks and rejects unreadable profiles.

Disabling a host plugin gates observation in the pad and the native completion notes of Claude and Antigravity, including callbacks already loaded by a running process. No Antigravity global hook is installed by the connector. Former Python sound hooks become silent after migration to prevent duplicate notes. Public and private Claude packages both use the same native note.

Codex's native hooks are passive, short-lived commands and require the host's review and trust before execution. Installation never fabricates trust or bypasses this approval. Hook files and plugin activation prove configuration only; logs preserve normal activity tracking while review is pending. Hooks add immediate parent-turn events without replacing asynchronous-question and terminal-state handling in the logs, or adding a second completion sound.

Claude's public connector and private full plugin both include the same diagnostic skill. The private plugin keeps its existing hooks, usage module and Relay interface; installation does not add a competing plugin identity. Its native plugin update must deliver the skill before the connector can attest the package.
