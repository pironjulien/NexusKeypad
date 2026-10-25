# Privacy Policy

**AI Coding Agents + PC** (plugin `AiAgentsPc`, project "NexusKeypad") · Effective October 25, 2026

**In short: the Plugin collects no personal data. Everything it reads stays on your computer; nothing is sent to the
Developer or to anyone else.** It has no account, no analytics, no telemetry, no advertising and no server.

AI Coding Agents + PC is the complete edition of the project, page 1 and page 2 in one plugin. It reads and writes
exactly what the two single-page plugins describe, on your computer only:

- page 1 (AI agents, quotas, screen capture, dictation, and the phone photo: with a phone reachable over adb, each new
  photo or screenshot is copied from the phone as soon as it is taken, to show it on the key): the
  [AI Coding Agents Privacy Policy](https://github.com/pironjulien/NexusKeypad/blob/main/PRIVACY.md);
- page 2 (sound, unread Gmail and Messages counts from Chrome window titles or phone notifications, PC health, Store
  and Windows Update): the
  [PC Dashboard Privacy Policy](https://github.com/pironjulien/NexusKeypad/blob/main/PRIVACY-PC-DASHBOARD.md).

Differences: at each press of its Website key, the addresses of the pages shown in Google Chrome app windows are read
(Windows UI Automation) to find the window that key opened, neither kept nor logged; its log file is
`…\LogiPluginService\Logs\plugin_logs\AiAgentsPc.log`; its Website key has no address to
set and opens the project's page on GitHub; page 2 is only read while it is shown on the keypad.

The controller is **Julien Piron** ("the Developer"). Logitech's own processing is covered by
[Logitech's privacy policy](https://www.logitech.com/legal/product-privacy-policy.html). To remove all data: uninstall
the Plugin from Logi Options+, then delete `~/.nexus/trinite`, `%LOCALAPPDATA%\NexusKeypad` and its log file.

The current version is kept at https://github.com/pironjulien/NexusKeypad/blob/main/PRIVACY-AI-AGENTS-PC.md.
Questions: [GitHub issues](https://github.com/pironjulien/NexusKeypad/issues).
