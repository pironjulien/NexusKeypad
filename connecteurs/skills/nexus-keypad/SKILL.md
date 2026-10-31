---
name: nexus-keypad
description: Diagnose, enable or disable this AI app's NexusKeypad connection when the user asks about the Logitech keypad, its AI buttons or connection state.
---

# NexusKeypad

One keypad engine runs in Logitech Options+. This connector reports this AI app's live activity to the keypad
locally (and exact usage for Claude); it adds no second pad engine and no MCP server.

Read-only diagnostic, with this app's name (`claude`, `codex` or `antigravity`):

```powershell
& (Join-Path $env:LOCALAPPDATA 'NexusKeypad\hook\nexuskeypad-hook.exe') connecteur <app> status
```

On the user's explicit request, replace `status` with `enable` or `disable`. The installer preserves other plugins,
hooks and settings and never installs duplicate activity hooks. Disabling the connector stops activity reporting
and its completion note, including callbacks already loaded by a running app; the keypad then shows its setup icon.

`Pret` proves the connector files and their activation only, not that the running app has loaded them; the keypad
verifies the app connection separately. Respect the host's permission and reload flow and never interrupt a working
conversation. Never claim a microphone, usage view or conversation-opening action works from file presence alone.

Per app:
- Claude: a pre-existing complete `nexus-keypad@nexus` (private) or `nexus-keypad@nexus-plugins` (NexusSystem
  catalog) plugin is preserved and used as the connector instead of adding another one.
- Codex: the bundled lifecycle hooks run only after the user reviews and trusts them in Codex; never create trust
  entries or bypass that review. Until then, and in any case, Codex's session logs remain the source for history,
  names, quotas, asynchronous questions and conversation details; child events, concurrent tools and late callbacks
  must not mark a busy parent conversation finished. The completion sound stays Codex's own.
- Antigravity: activity hooks live in the plugin's root `hooks.json`; its Stop event plays the bundled completion
  note through the shared native program. The installer resolves the program path and migrates only former
  NexusKeypad global handlers. Use `disable` before removing the connector.
