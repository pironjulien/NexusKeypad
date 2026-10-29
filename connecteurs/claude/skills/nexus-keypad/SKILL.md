---
name: nexus-keypad
description: Diagnose, enable or disable Claude's NexusKeypad connection when asked about the Logitech keypad or its AI buttons.
---

# NexusKeypad

This connector reports live activity and exact usage to the keypad locally. One engine runs in Logitech Options+; no separate MCP server is needed.

Read-only diagnostic:

```powershell
& (Join-Path $env:LOCALAPPDATA 'NexusKeypad\hook\nexuskeypad-hook.exe') connecteur claude status
```

On the user's explicit request, replace `status` with `enable` or `disable`. The installer preserves third-party settings and avoids installing duplicate activity hooks. A pre-existing private `nexus-keypad@nexus` plugin is preserved and used as the connector instead of adding another one.

The diagnostic proves the files and enabled configuration. The pad also checks the current session. Respect any host confirmation or reload prompt. Never claim a microphone or conversation-opening action works merely because the connector is installed.
