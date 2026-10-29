# Privacy Policy

**AI Coding Agents** (plugin `TrinitePad`, project "NexusKeypad") and its AI connectors · Updated October 29, 2026

**The Plugin processes data locally and sends none of its session files, images or diagnostics to the Developer.**
It has no account, analytics, telemetry, advertising or Developer-operated server. Actions that paste data or start
voice features in another application follow that application's own processing and privacy policy.

The controller of this policy is **Julien Piron** ("the Developer"), author of the Plugin. Logitech's own processing
(your Logitech account, the Marketplace, Logi Options+) is covered by
[Logitech's privacy policy](https://www.logitech.com/legal/product-privacy-policy.html), not by this one.

## What the Plugin reads, on your computer only

The Plugin reads data only to draw the keypad keys and to perform the action of a key you press:

- **AI agent sessions**: conversation titles, states and timestamps from the local files of the apps you use
  (Claude Desktop: `%LOCALAPPDATA%\Claude-Data\…\claude-code-sessions` and its `config.json` to know the signed-in
  account; Codex: `~/.codex` session index and logs; Antigravity: its sidebar, read through its local debugging port),
  and the states written by the optional NexusKeypad plugin hooks in `~/.nexus/trinite`. Session identifiers, titles,
  states, event types and timestamps are used. Hook payloads may contain tool arguments or the last answer of a turn;
  the helper uses these to associate activity and distinguish a question from a finished turn, without storing the
  prompt, tool arguments or answer text in its activity files or callback diagnostics.
- **Usage quotas**: the percentages and reset times shown by each app (Claude's usage module, local desktop usage
  history or "Usage" button; Codex logs or a local codex-lb on `127.0.0.1:2455`; Antigravity's visible *View Usage*
  panel or *Settings › Models* page). Claude's displayed session identifies the account/organization so that quotas
  from different accounts are not mixed.
- **Windows**: window titles and buttons of these apps, through Windows UI Automation, to find the app in front and
  the buttons a key press uses.
- **Screen** (Screen capture key, only when you press it): one capture of your screen.
- **Microphone**: the Plugin never records audio itself. The microphone key uses Claude dictation, Codex dictation
  when available, an Antigravity voice memo, or Windows voice typing. If Codex's dictation control is unavailable,
  the fallback is Codex's native voice chat. That mode follows Codex's own conversation and transmission behavior;
  it is not limited to leaving a transcription in an unsent text field. The app or Windows handles the audio under
  its own privacy policy.
- **Phone** (only if an Android phone is reachable over adb, USB or wireless debugging, and only while the Plugin
  runs): to show the phone's latest photo on the Phone photo key, the Plugin watches the phone's camera and screenshot
  folders (`DCIM/Camera`, `DCIM/Screenshots`, `Pictures/Screenshots`) and copies each new photo or screenshot to your
  PC as soon as it is taken, without a key press. Pressing the key only pastes it. Without adb or a phone, nothing is
  read. Wireless debugging uses adb on your local network; the optional pairing note
  `~/.nexus/nexus-android/wifi.json` is read if present.

## What the Plugin writes, on your computer only

- `~/.nexus/trinite`: the last 5 screen captures and the last 5 phone photos with their reduced copies (older ones are
  deleted), per-session hook activity under `claude`, `codex` and `antigravity`, and Claude usage percentages/reset
  times with a session identifier in `quota-claude.json`. Codex callback diagnostics under `diagnostics/codex` keep
  the event type, time and publication result, without prompt, answer text, tool arguments or transcript path.
- **AI connections, only after setup is approved**: one NexusKeypad connector per installed AI, each with its skill,
  hooks and activation setting. The shared native helper is copied to
  `%LOCALAPPDATA%\NexusKeypad\hook\nexuskeypad-hook.exe`, with bundled connector templates and Claude's usage module
  beside it. Activation receipts in `%LOCALAPPDATA%\NexusKeypad\connecteurs\etats\<agent>.json` record the plugin
  identity, activation, setup time and local file paths/hashes used to verify that installation.
- **Claude connector**: its local marketplace and plugin are under
  `%LOCALAPPDATA%\NexusKeypad\connecteurs\claude`; the official plugin manager bundled with Claude Desktop registers
  and installs `nexus-keypad@nexuskeypad`, with its declaration and activation in `~/.claude/settings.json`.
  Repair can reinstall this connector through that manager. If a complete existing private NexusKeypad plugin is already installed, its identity
  and package are preserved instead of adding a competing plugin. Claude manages its own plugin cache and registry.
  If `CLAUDE_CONFIG_DIR` is set, that directory replaces `~/.claude`.
- **Codex connector**: plugin files in `~/.codex/plugins/nexus-keypad`, the installed copy in
  `~/.codex/plugins/cache/<catalog>/nexus-keypad/<version>`, an entry in the local catalogue
  `~/.agents/plugins/marketplace.json`, and its activation in `~/.codex/config.toml`. Installation never creates hook
  trust: new or modified hooks remain subject to Codex's own review. Existing session-log tracking remains available.
- **Antigravity connector**: plugin files, including its own `hooks.json`, in
  `~/.gemini/config/plugins/nexus-keypad`, enabled in `~/.gemini/config/config.json`. Only former NexusKeypad global
  handlers are migrated out of `~/.gemini/config/hooks.json`; hooks belonging to other tools are preserved.
- The per-AI NexusKeypad actions in Logi Options+ enable or disconnect these integrations. A disabled integration
  stops its activity observation and, for Claude and Antigravity, its native completion note. Other plugins and
  unrelated settings are preserved.
- `%LOCALAPPDATA%\NexusKeypad\quota-antigravity.json`: the last Antigravity quota read.
- `~/.nexus/status/keypad.json`: the Plugin's version and update status for the Nexus app. The complete edition may
  also keep update preferences and temporary update files under `%LOCALAPPDATA%\NexusKeypad`.
- The plugin log of the Logi Plugin Service (`…\LogiPluginService\Logs\plugin_logs\TrinitePad.log`): technical events,
  which can include conversation titles and file names. It stays on your computer.
- The Windows clipboard: Screen capture and Phone photo put the image in the clipboard before pasting it.

## Network

The Plugin connects to `127.0.0.1` for the apps' local debugging ports and codex-lb when present, and to your phone
through adb when reachable (see above). When a key opens a page, your browser connects to that page. The complete
NexusKeypad edition, distributed outside the Marketplace, also contacts GitHub to check for and download signed
updates; Marketplace editions are updated through Logitech. The Plugin has no analytics or data-collection endpoint
and sends none of its session files, images or local diagnostics to the Developer.

Screen capture and Phone photo paste an image without pressing Send. Dictation or voice recording is performed by
the selected app or Windows; Codex voice chat can transmit as part of its native voice conversation. What those apps
do with text, images or audio is governed by their own privacy policies.

## Your data, your control

The local files remain under your control. For the complete NexusKeypad edition, run the downloaded official
installer as `pwsh -File .\get.ps1 -Remove`. It disconnects the integrations it installed, removes the Logi plugin
and its assigned keys, and preserves the keypad profile backup. Connector/plugin caches and local usage or image
files may remain: after disconnection, delete `%LOCALAPPDATA%\NexusKeypad` and `~/.nexus/trinite` if no longer needed,
and remove the disabled NexusKeypad plugin from each host if you want to clear its host-managed cache too.
For a Marketplace edition, disconnect each AI with that edition's per-AI NexusKeypad actions before uninstalling
the Logi plugin. Avoid removing unrelated plugins or settings. Since the Developer receives no personal data from
the Plugin, there is no server-side copy for the Developer to correct or delete.

## Children

The Plugin is a developer tool and is not directed at children.

## Changes and contact

The current version of this policy is kept at
https://github.com/pironjulien/NexusKeypad/blob/main/PRIVACY.md with its effective date. Questions:
[GitHub issues](https://github.com/pironjulien/NexusKeypad/issues).
