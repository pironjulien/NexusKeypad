# NexusKeypad

**Antigravity's live state on your Logitech MX Creative Keypad.** The keypad shows when Antigravity is thinking,
using a tool or done, and plays a short note when a task ends. The skill diagnoses, enables or disables the
connection on request.

## Requirements

Windows 10 or 11, Logi Options+ and a Logitech MX Creative Keypad with the NexusKeypad plugin (Logi Marketplace or
[releases](https://github.com/pironjulien/NexusKeypad/releases)). This plugin's hooks run the small helper the
NexusKeypad plugin installs in `%LOCALAPPDATA%\NexusKeypad\hook`; without it they do nothing. It adds no MCP server.

## Privacy

The helper only writes Antigravity's state to `%USERPROFILE%\.nexus\trinite` on this PC, for the keypad to read.
Nothing is sent anywhere: no account, no telemetry. Details:
[privacy](https://github.com/pironjulien/NexusKeypad/blob/main/PRIVACY.md).

## Links

- Source and releases: [github.com/pironjulien/NexusKeypad](https://github.com/pironjulien/NexusKeypad) (MIT)
- Support: [GitHub issues](https://github.com/pironjulien/NexusKeypad/issues)
