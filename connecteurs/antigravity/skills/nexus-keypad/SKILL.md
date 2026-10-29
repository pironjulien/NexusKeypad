---
name: nexus-keypad
description: Diagnose, enable or disable Antigravity's NexusKeypad connection when the user asks about the Logitech keypad or its AI buttons.
---

# NexusKeypad

One keypad engine runs in Logitech Options+. This plugin is Antigravity's setup and diagnostic entrypoint, with no second MCP server.

Read-only diagnostic:

```powershell
& (Join-Path $env:LOCALAPPDATA 'NexusKeypad\hook\nexuskeypad-hook.exe') connecteur antigravity status
```

Replace `status` with `enable` or `disable` only on the user's explicit request. Activity hooks live in this plugin's root `hooks.json`; its Stop event also plays the bundled completion note through the shared native program. The installer resolves the program path and migrates only former NexusKeypad global handlers, preserving other hooks. Turning this plugin off stops both activity reporting and its note, including callbacks already loaded by a running app. Use `disable` before removing the connector.

A configuration diagnostic does not prove that Antigravity has loaded the hooks or its local debugging connection. The keypad verifies the app connection separately. Respect the host's permission and reload flow; do not interrupt working conversations. Do not promise all features work from the presence of plugin.json.
