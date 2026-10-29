---
name: nexus-keypad
description: Diagnose, enable or disable this AI's NexusKeypad connection when the user asks about the Logitech keypad, its AI buttons or connection state.
---

# NexusKeypad

The keypad runs once in Logitech Options+. This connector gives Codex a supported setup and diagnostic entrypoint; it does not create another pad engine or MCP server.

For a read-only configuration diagnostic, run the shared installed program:

```powershell
& (Join-Path $env:LOCALAPPDATA 'NexusKeypad\hook\nexuskeypad-hook.exe') connecteur codex status
```

On an explicit user request to enable or disable this connection, replace `status` with `enable` or `disable`. These commands preserve other Codex plugins and settings. Disabling this plugin in Codex also makes the keypad show its setup icon rather than reading Codex activity.

`Pret` proves connector files and plugin activation only. This plugin bundles native lifecycle hooks for immediate activity; Codex requires the user to review and trust new or changed hooks before they run. Until then, session logs keep activity tracking available. Never create trust entries or bypass this native review. Installing a plugin does not prove its hooks are running.

Hooks supplement the existing session logs, which remain the source for history, names, quotas, asynchronous questions and conversation details. Child events, concurrent tools and late callbacks must not mark a busy parent conversation finished. The completion sound remains Codex's own notification sound.

The keypad also verifies the running app connection. Respect the host's reload flow; never interrupt a working conversation or claim a working microphone or usage view from file presence alone.
