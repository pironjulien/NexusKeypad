# Changelog

All notable changes to this project are documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

## [1.5.0] - 2026-10-27

### Added
- CodeQL code scanning (`.github/workflows/codeql.yml`): C# built for real on Windows (plugin with its hook, preview,
  rule checker; Logi API from the `LogiPluginApi` NuGet package, no Logi service needed) and the GitHub Actions
  workflows, on every push, pull request and every Wednesday.

### Changed
- Photo and screenshot pasted into an AI: up to 2000 px and 2.56 MP (was 1568 px and 1.15 MP), lossless WebP while it
  fits 500 KB, then quality 90, 80, 70.
- Dependabot NuGet reads the project folders (`/plugin/src`, `/apercu`, `/verifier`) instead of the root, which holds
  no project or solution; updates grouped in one pull request.
- `LoupedeckPackage.yaml` of the four editions: the "Loupedeck CT, Live and Live S" comment now names the MX Creative
  Console (`LogitechCreativeFamily`). The internal `TrinitePad` name of the AI Coding Agents edition is documented in
  its package: kept on purpose, renaming it would break the identity of installed copies. Comment only, no effect on
  the plugin.

### Fixed
- Hook build (`TrinitePadPlugin.csproj`): the output folder is passed to `dotnet publish -o` without a trailing `\`;
  `\"` escaped the quote and glued any appended argument to the path (the build failed under CodeQL).

## [1.4.4] - 2026-10-27 — Sharper icon

### Changed
- Icon of the four editions (`metadata/Icon256x256.png`) rebuilt from the high-definition kit: the julienpiron.fr medallion rebuilt at 600 px in the icon kit, sharp up to 512 px instead of a 240 px picture enlarged.

## [1.4.3] - 2026-10-26 — Nexus family icon

### Changed
- Nexus family icon: the keypad pictogram in the julienpiron.fr medallion, the same on the site and in every Nexus
  product. It replaces the three characters (AI Coding Agents, AI Coding Agents + PC, NexusKeypad) and the four keys
  (PC Dashboard) in each edition's `metadata/Icon256x256.png`, drawn in the central 192 px on a transparent background
  (`dotnet run --project apercu -- icone <nexuskeypad-mark-256.png> <png>`; `icone-poste` removed). Key templates
  (`DefaultIconTemplate.ict`) are unchanged.

### Fixed
- `test.ps1` failed in this repository: it ran a test of a private, unpublished part. That test is no longer
  published.

### Added
- Preview tool: `sprites <folder>` mode, exporting the characters in every state, the relay messengers and the
  situation boards as 30 fps PNG sequences with `index.json` (for videos). The shipped plugin does not change.

### Removed
- `etat/etat.py` and `outils/installer_hooks.py`: replaced by the native hook `nexuskeypad-hook.exe`, whose rules are
  kept case by case by `tests/test_hook.py`.

## [1.4.2] - 2026-10-26 — Antigravity quota back, get.ps1 attached to the release

### Fixed
- Antigravity's quota stayed empty when a "Conversation Archived … Undo" notification was on screen (it is a dialog
  too): the reading now targets the settings window that has the Models tab.

### Added
- The release also carries `get.ps1` (in `SHA256SUMS.txt`, attested): install from a downloaded, checked file
  instead of piping into `iex` (README).

## [1.4.1] - 2026-10-26 — PC Dashboard without any AI agents code

### Fixed
- PC Dashboard 1.4.0 still carried a few AI agents functions (unused there): removed. The tests now check that its DLL
  names no AI app.

### Changed
- The keys' group in Logi Options+ is now called "Trinity" in English ("Trinité" in French).

## [1.4.0] - 2026-10-26 — The complete edition updates itself

### Added
- The complete edition updates itself once a day (SHA-256 checked against the release, swapped in only while the
  keypad is idle, previous version put back on failure); `{"miseAJourAuto": false}` turns it off. State for the Nexus
  app in `~/.nexus/status/keypad.json`, also written by the Marketplace editions.

### Changed
- PC Dashboard is now built only from what the PC page uses: no code that reads the AI apps ships in it. No Marketplace
  edition contains the network code of the self-update. Code comments are neutral ("the user").
- The preview tool also draws all 18 keys at a glance per situation (`pad-<situation>.png`).

### Security
- Safety catches against touching a real setup by mistake: `get.ps1` refuses a local test package without a test
  folder unless `-Real`; the development build installs into Logi only with `-p:Reel=true`; `placer_touches.py`,
  `installer_hooks.py` and the acting commands of `pad.py` need `--reel`.

### Fixed
- `get.ps1` reads the latest version from the `releases/latest` redirect instead of the GitHub API (its anonymous limit
  of 60 requests an hour per IP broke on shared IPs); the API stays as a fallback.

## [1.3.0] - 2026-10-25 — The complete NexusKeypad in one command

### Added
- The complete edition outside the Logi Marketplace: `irm https://raw.githubusercontent.com/pironjulien/NexusKeypad/main/get.ps1 | iex`
  (no administrator rights) installs the `NexusKeypad` plugin, both pages, everything, from the latest release (SHA-256
  checked), and offers to place the 18 keys (profile backed up first). `-Remove` takes it all away. It has its own
  plugin name, distinct from the Marketplace editions. Non-interactive mode for the Nexus pack (`NEXUS_PACK=1`).
- The release now carries four packages: the three Marketplace editions and `NexusKeypad`.
- Claude's exact usage: the `nexuskeypad-quota` Claude Code module (`claude-quota/`) writes the 5-hour and weekly
  windows to `~/.nexus/trinite/quota-claude.json`. It ships in the package and is set up and removed with the live
  states (same Yes), through a local marketplace declared in `~/.claude/settings.json`.

### Changed
- AI Coding Agents + PC: its Website key is set in Logi Options+ like PC Dashboard's (the **Website** action, no
  website by default); Key 1 to Key 17 for the other keys.

### Fixed
- Codex key: headless `codex exec` runs (started by scripts or tools) were counted as app chats; they have no row in
  the app's sidebar, so a "Chat Codex" alert blinked and could never be opened. They are now ignored.

## [1.2.0] - 2026-10-25 — NexusKeypad: live states without Python, one version for everything

### Added
- Live states without Python: `nexuskeypad-hook.exe`, a native program (Native AOT, no .NET runtime to install) shipped
  in the plugin package, writes the hook states with the same rules as `etat/etat.py` (compared case by case in the
  tests). The plugin asks once, at first start, whether to turn live states on (default: No); the *Live states* action
  of the AI agents group sets them up or removes them, always after a Yes. Nothing changes on No or on any error.
- Release workflow: on a `vX.Y.Z` tag, the three packages are built, checked by the Logi Plugin Tool, attested (build
  provenance) and published with `SHA256SUMS.txt` as the "NexusKeypad X.Y.Z" release.
- Animations: waiting and done states of their own for Codex (tilted head, terminal typing "?"; check mark, hops and
  confetti) and Antigravity (radar waves; landing and fireworks); new states for all three: permission requested
  (padlock), error (crossed eyes, bolt, shake, red ring; Claude through the new `StopFailure` hook), usage limit
  (faded, empty battery), listening during a dictation, waking up while an app the pad launched starts. The relay is
  no longer a line of tiny Clawds: between Claude and the peer, at mid-height, two lanes of chevrons (orange towards
  the peer, blue or cyan back to Claude).
- Second Logitech Marketplace plugin, "PC Dashboard" 1.0.0: page 2 alone (`-p:Edition=Poste`), as its own plugin
  `PcDashboard`, without the agents, the phone photo or the test pipe; Chrome and the phone are only read while its keys
  are shown. Its website key is a "Website" action whose address you type in Options+ (no website by default).
  `outils/paquet_marketplace.ps1 -Edition Poste` packs and checks `PcDashboard_1_0_0.lplug4`. Its own
  [EULA](EULA-PC-DASHBOARD.md) and [Privacy Policy](PRIVACY-PC-DASHBOARD.md).
- Third Logitech Marketplace plugin, "AI Coding Agents + PC" 1.1.0: both pages in one plugin (`-p:Edition=Complete`,
  plugin `AiAgentsPc`), without the test pipe; its website key opens the project page.
  `outils/paquet_marketplace.ps1 -Edition Complete` packs and checks `AiAgentsPc_1_1_0.lplug4`. Its
  [EULA](EULA-AI-AGENTS-PC.md) and [Privacy Policy](PRIVACY-AI-AGENTS-PC.md).
- Logitech Marketplace edition, "AI Coding Agents" 1.1.0: the agents page alone (page 1), without page 2 and without
  the local test pipe (`-p:Edition=Marketplace`), built without debug symbols. `outils/paquet_marketplace.ps1` builds
  it, packs `TrinitePad_1_1_0.lplug4` and checks it with the official Logi Plugin Tool; `test.ps1` builds it too.
- [EULA](EULA.md) and [Privacy Policy](PRIVACY.md): the developer is the only licensor, Logitech is not a party; no
  personal data is collected, everything stays on the PC.
- Plugin icon: our three characters (Codex and Antigravity behind, Clawd in front), drawn by the plugin's own code
  (`dotnet run --project apercu -- icone <png>`).
- README: Marketplace installation, and the hooks and the Antigravity debugging port in "Better with the Nexus
  family" (what each adds, what remains without it).
- Action names in English, with a French translation (`package/localization/TrinitePad_fr-FR.xliff`).
- English labels for Claude (dictation, usage button, conversation title) and Codex (voice chat, dictation), taken from
  the apps' own language files: these presses now work with the apps in English too.
- Gmail unread count from [NexusBrowser](https://github.com/pironjulien/NexusBrowser) (its opt-in "Gmail unread for the
  pad" box) when Gmail is not open in Chrome: `~/.nexus/nexus-browser/compteurs.json`, used if under 3 minutes old.
- README: "Better with the Nexus family, fine on its own", what each part adds and what replaces it when it is missing.
- `pad.py capture 2`: picture of page 2, read and drawn fresh even while it is hidden.

### Changed
- Marketplace display names carry the NexusKeypad brand from the next submitted version: "NexusKeypad – AI Coding
  Agents", "NexusKeypad – PC Dashboard" and "NexusKeypad – AI Coding Agents + PC" (plugin identifiers unchanged).
- The repository is now `pironjulien/NexusKeypad` (formerly `mx-keypad-ai-agents`; GitHub redirects the old
  address). Links in the README, EULAs, privacy policies and Marketplace metadata follow.
- Agent heads: one persistent state sign above the head, the same for all three (working: cyan spinner; question:
  amber "?"; permission: padlock; done: green check; error: red triangle; usage limit: empty barred gauge; closed: grey
  silhouette and a switched-off power sign; waking up: grey spinner; listening: microphone, red recording dot and cyan
  waves). Working: stars in each agent's colour rise behind it; done: sunglasses and fireworks
  for all three. Celebrations and shakes play only when the state starts. The relay: messengers go back and forth; a tiny
  Clawd walks to the agent working for Claude and gives it an electric shock, then a tiny Codex or Antigravity walks
  back and shocks Claude. `apercu animations <folder>` renders every state frame by frame.
- Updates key: Windows Update opens on the left half of the screen and the Store on the right half, in view (instead
  of behind your windows, then minimized); up to date, each window closes by itself; installing or unproven, it stays.

### Fixed
- Head ring: only the keypad's outer corner is strongly rounded (top left of the left key, top right of the right
  key); every other corner shares the common radius. Clawd's done hop now lifts its legs too.
- Quota gauges sat 1 to 2 px above their icons (the calendar ended below its bar): centred.
- Hooks: `PostToolUseFailure` and `StopFailure` (official `error` field); a denied permission no longer leaves the
  padlock stuck.
- Claude key, the view bounced (bottom, up, down…): the conversation already shown was reopened through its link and
  Claude reloaded it; the 100 % fallback scroll then jumped to the middle of a loaded part. Already shown, it is only
  brought to the front; only Claude's own "Scroll to bottom" button scrolls it down.
- Claude key, conversation still in the middle (Antigravity then Claude): Claude keeps only part of the thread
  loaded and UI Automation reported 100 % at the end of that part. The pad first presses Claude's own "Scroll to
  bottom" button, which loads the real end.
- Claude key: the opened conversation stayed in the middle. A pass with nothing to scroll yet (conversation still
  loading) stopped the next passes since the re-check added just before; only "Claude is no longer in front" stops
  them now.
- Review follow-up: a conversation scroll is tied to the press that started it (a gesture still running no longer
  revives it) and re-checked, target window in front, right before scrolling; both update windows share the screen of
  the press; an up-to-date window is closed only if it still shows its up-to-date page and the plugin is not stopping.
- Messages key: a second press opened another window instead of closing its own. Google Messages writes "Google
  Messages for web" with non-breaking spaces (U+00A0), so its window was never recognised; titles are now compared
  with unified spaces.
- Agent keys: Claude came back to the front 1 to 2 s after pressing Codex or Antigravity right after Claude's key. The
  scroll of its conversation to the end (UI Automation, up to ~4 s after the press) brought its window forward. It now
  scrolls only while Claude is still in front, and any key that brings another window cancels pending scrolls at once.
- Review by Codex and Antigravity of the Marketplace editions (October 25), sent again to Logitech as AI Coding Agents
  1.1.1, PC Dashboard 1.0.1 and AI Coding Agents + PC 1.1.1:
  - website key without a window title: its window is a Chrome app window whose page is under the requested address
    (same scheme, host and port, path at or below the address's path segment by segment: two apps on localhost:3000 and
    localhost:5173, or two GitHub repositories, are told apart; a query or fragment in the address must match exactly),
    the page address being read again (UI Automation) at each press; a window that fails this check just before the
    key acts is neither closed nor brought back, the requested page is opened instead; the privacy policies declare
    this reading; it looked for a
    window whose title starts with the site's host name, which a page title almost never does (6 s wait and one more
    window at each press). With a title given, its window must still start with it (no Gmail false positive);
  - PC Dashboard no longer opens any agent file (not even the kept Antigravity quota). With its keys not requested by
    the keypad, it only reads sound, processor and memory once a second (no Chrome, no phone): that is what wakes up a
    shown key whose image had stopped changing; the privacy policy now says so;
  - the Marketplace edition has its own intermediate build folder, like the other two;
  - French action names of the quota keys: "Quota de Codex" instead of "Codex n°2";
  - privacy policy: with a phone reachable over adb, new photos are copied as soon as they are taken (to show them on
    the key), not only when the photo key is pressed.
- Review by Codex and Antigravity (October 25):
  - updates key: a real outcome (started, up to date, unknown, failed, cancelled); green check only when both are
    started or up to date, otherwise an amber "!"; slower polling after 10 s; stopped with the plugin; a minimised
    Store suspended by Windows is resumed (`IPackageDebugSettings.Resume`) before its buttons are read; a greyed-out
    button (Windows Update while it searches) is no longer pressed (the press raised an error and stopped everything);
  - shown page: the one whose images the keypad last asked for (116 px); the hidden page is no longer read;
  - Capture and Photo are pasted only into the AI targeted at the press (nothing if you switched away);
  - CDP clicks and window closes are checked (logged when they fail);
  - Gmail and Messages are recognised by their exact title ("… - Gmail", "Google Messages for web"): a tab that
    mentions Gmail no longer lights a false badge; Chrome's tab strip is read only when needed;
  - NexusBrowser unread count: one entry per Chrome profile (added up), 3-minute expiry checked when used;
  - Antigravity quota kept with its reset times ("refresh in 4 days, 23 hours"): each window goes back to 0 % at its
    own time, no longer 5 h or 7 days after the reading;
  - `installer_hooks.py`: Antigravity alone works (missing Claude settings are created); paths with spaces or
    apostrophes (single-quoted for PowerShell, Windows short name for Antigravity);
  - `essais_reels.py`: a Claude dictation started by a test is stopped during cleanup.
- Second review (October 25):
  - paste: the AI is read from the very window checked in front before Ctrl+V (switch to another AI between two checks
    and nothing is pasted);
  - Store "up to date" only with proof: its search button enabled again, nothing offered, no error message (otherwise
    unknown); resuming the suspended app is retried until a button answers;
  - Antigravity quota kept per model group: a group that resets leaves the gauge to the next one, and resets also apply
    in memory, without waiting 5 h or a new reading;
  - NexusBrowser Gmail total dated by its oldest source (no count kept past 3 minutes);
  - native clipboard (Win32 API: PNG, CF_DIB, CF_HDROP) instead of a PowerShell kept open (60 to 80 MB); live test:
    `dotnet run --project verifier -- presse-papiers`;
  - plugin shutdown: an error in the updates task is logged, never thrown;
  - older Antigravity quota cache: its reset times still apply; resuming the Store is scheduled from the previous try
    (a slow poll no longer skips it).
- No Gmail badge when Gmail is open in a Chrome tab: tab titles (Chrome's tab strip, read through UI Automation even
  with the window behind) count like window titles; read every 3 s, off the animation thread.
- Updates key: it only searched, then the Store and Windows Update offered their updates without installing them. It
  now installs everything: search, then "Update all" (Store) and "Download & install all" (Windows Update) pressed
  through UI Automation, windows shown behind the others (minimised, they expose no buttons) then minimised again;
  never presses *Restart*; the key animates until it is done.
- PC health: the second press closes Task Manager (it refuses the close message even without elevation, so it is
  ended).
- Second press on Gmail, Calendar, Messages, the site, PC health or the Codex quota key closes its window when it is in
  front. Task Manager is started without elevation (`__COMPAT_LAYER=RunAsInvoker`): elevated, Windows would not let
  the pad close or minimise it.
- False pending mail: Gmail only counts the unread mails shown in its window title on the PC (its phone notifications
  stay after mails are read on the PC); Messages no longer counts the permanent notifications of its web service.
- Codex quota key: the Codex LB window, now titled "Codex LB - 127.0.0.1", was no longer recognised and each press
  opened a new one.
- Site key: it could bring Gmail forward, whose window title contains the account address; the site window is now
  recognised when its title starts with the site name.
- Antigravity quota no longer goes blank after 5 hours while Antigravity is closed: nothing was used, so the 5-hour
  gauge is shown full and the weekly one is kept for up to 7 days (Antigravity open: unchanged).

## [1.0.0] - 2026-10-24 — First public release

### Added
- Page 1: Codex, Claude and Antigravity as animated pixel-art characters showing the state of their most urgent
  conversation (idle, working, waiting for you, done, asleep), with blinking alerts until the conversation is opened.
  A press opens that conversation (full screen, scrolled to the end), then cycles through the others.
- Quota gauges for each agent (5-hour window and week, what is left) and the number of open conversations; a press
  opens the app's own usage panel.
- Screen capture, phone photo and dictation keys that paste or dictate into the agent in front, never sending.
- Without an Android phone, the photo key is an animated clock with the date; a press opens the Windows notification
  center and calendar.
- Page 2: volume down, mute and volume up; Gmail, Calendar and Messages, opened in front in their installed Chrome
  app or a Chrome app window, with unread counts from their window title (or the phone's notifications); PC health;
  one key that starts both the Windows Update and Microsoft Store update checks; a website key configured in
  `~/.nexus/trinite/site.json`.
- Codex is started with its debugging port (9223) when the plugin launches it; a local codex-lb is used for its quota
  when one runs.
- `test.ps1`: plugin build, rendering of every key, logic and hook rules; builds against the LogiPluginApi NuGet
  package when the Logi service is not installed (CI).
