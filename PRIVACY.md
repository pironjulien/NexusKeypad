# Privacy Policy

**AI Coding Agents** (plugin `TrinitePad`, project "MX Keypad AI Agents") · Effective October 25, 2026

**In short: the Plugin collects no personal data. Everything it reads stays on your computer; nothing is sent to the
Developer or to anyone else.** It has no account, no analytics, no telemetry, no advertising and no server.

The controller of this policy is **Julien Piron** ("the Developer"), author of the Plugin. Logitech's own processing
(your Logitech account, the Marketplace, Logi Options+) is covered by
[Logitech's privacy policy](https://www.logitech.com/legal/product-privacy-policy.html), not by this one.

## What the Plugin reads, on your computer only

The Plugin reads data only to draw the keypad keys and to perform the action of a key you press:

- **AI agent sessions**: conversation titles, states and timestamps from the local files of the apps you use
  (Claude Desktop: `%LOCALAPPDATA%\Claude-Data\…\claude-code-sessions` and its `config.json` to know the signed-in
  account; Codex: `~/.codex` session index and logs; Antigravity: its sidebar, read through its local debugging port),
  and the states written by the optional hooks in `~/.nexus/trinite`. Only titles, states, event types and timestamps
  are used; message contents are neither kept nor logged (the optional hook script looks at the last answer of a turn
  only to tell a question from a finished turn, and does not store it).
- **Usage quotas**: the percentages shown by each app (Claude's "Usage" button, Codex logs or a local codex-lb on
  `127.0.0.1:2455`, Antigravity's *Settings › Models* page).
- **Windows**: window titles and buttons of these apps, through Windows UI Automation, to find the app in front and
  the buttons a key press uses.
- **Screen** (Screen capture key, only when you press it): one capture of your screen.
- **Microphone**: the Plugin never records audio. The Dictation key presses the dictation button of the app in front
  (or starts Windows voice typing); that app or Windows does the listening.
- **Phone** (only if an Android phone is reachable over adb, USB or wireless debugging, and only while the Plugin
  runs): to show the phone's latest photo on the Phone photo key, the Plugin watches the phone's camera and screenshot
  folders (`DCIM/Camera`, `DCIM/Screenshots`, `Pictures/Screenshots`) and copies each new photo or screenshot to your
  PC as soon as it is taken, without a key press. Pressing the key only pastes it. Without adb or a phone, nothing is
  read. Wireless debugging uses adb on your local network; the optional pairing note
  `~/.nexus/nexus-android/wifi.json` is read if present.

## What the Plugin writes, on your computer only

- `~/.nexus/trinite`: the last 5 screen captures and the last 5 phone photos with their reduced copies (older ones are
  deleted), and the hook states if you installed the hooks.
- `%LOCALAPPDATA%\NexusKeypad\quota-antigravity.json`: the last Antigravity quota read.
- The plugin log of the Logi Plugin Service (`…\LogiPluginService\Logs\plugin_logs\TrinitePad.log`): technical events,
  which can include conversation titles and file names. It stays on your computer.
- The Windows clipboard: Screen capture and Phone photo put the image in the clipboard before pasting it.

## Network

The Plugin makes no network request except: to `127.0.0.1` (the apps' local debugging ports, and codex-lb when you
run it), to your phone through adb when one is reachable (see above), and, when you press a key that opens a page (for example
a usage page), to that page in your browser. **No data is sent to the Developer, to Logitech or to any analytics
service.** Pasted images and dictated text always stay unsent in the app's field: sending them is your decision, and
what the AI apps then do with them is governed by their own privacy policies.

## Your data, your control

All the data above stays under your control on your computer. To remove it: uninstall the Plugin from Logi Options+,
then delete `~/.nexus/trinite` and `%LOCALAPPDATA%\NexusKeypad` (and, if you installed them, the hooks that call
`etat.py` from `~/.claude/settings.json` and the `trinite-pad` entry of `~/.gemini/config/hooks.json`). Since the
Developer receives no data, there is nothing to request, correct or delete on the Developer's side.

## Children

The Plugin is a developer tool and is not directed at children.

## Changes and contact

The current version of this policy is kept at
https://github.com/pironjulien/NexusKeypad/blob/main/PRIVACY.md with its effective date. Questions:
[GitHub issues](https://github.com/pironjulien/NexusKeypad/issues).
