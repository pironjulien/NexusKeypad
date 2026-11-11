# Changelog

All notable changes to this project are documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

## [1.9.7] - 2026-11-11

### Fixed
- The Antigravity key no longer shows "relaunch" while Antigravity is closed: background Node helpers started with
  Antigravity's executable (such as an AI relay sidecar) were counted as the open app by the connection check, while
  the head already ignored them. Both now share one rule.
- The connector program builds again with the .NET 10.0.15 runtime: its pinned native compiler moves to 10.0.15.

## [1.9.6] - 2026-11-09

### Changed
- The Claude key reaches every recent conversation again, whatever its folder: an idle conversation of another project
  was left out (only working, waiting or finished ones were shown), so a conversation started from another one, in
  another folder, could not be reached from the pad. Archived conversations stay out; idle ones still leave after 6 hours.

## [1.9.5] - 2026-11-09

### Fixed
- Updates no longer fail with "Logi plugin service did not stop in 30 seconds": when Logi Options+ restarts its
  plugin service right after a stop, sometimes twice, or a just-restarted service ignores the stop, the installer
  waits for it to finish starting and sends the stop again, then checks that nothing comes back for 5 seconds.

## [1.9.4] - 2026-11-09

### Fixed
- An AI key shows "working" as soon as one of its conversations works, even when another one waits in the background
  (a long command, background tasks or agents): "in the background" now shows only when none of them is thinking.

## [1.9.3] - 2026-11-08

### Fixed
- Codex gets back his "?" and padlock above the head: the question mark and blocked cursor in his terminal are too
  small to read on the pad. The three AIs now show the same sign for a question and for a permission.

## [1.9.2] - 2026-11-08

### Changed
- No sign above a head when the character already says the state: an app that is closed (grey, asleep, its "z") loses
  its power button, a finished turn (sunglasses, fireworks) its green check mark, an error (crossed eyes, red ring) its
  red triangle, and Codex keeps its question and its blocked cursor in his own terminal. The "?" and the padlock stay
  above Clawd and Antigravity, whose gestures alone do not tell a question from a permission, and the empty gauge
  stays above an AI that reached its usage limit.

## [1.9.1] - 2026-11-08

### Changed
- The background state is acted by each head instead of a wheel above it: Clawd waits, tapping his foot, his eyes
  following a cyan spark that runs back and forth on the ground; Codex takes his paws off the keyboard and watches a
  progress bar fill by itself in his terminal; Antigravity lands, engines off, and follows with her eyes her satellite,
  which keeps orbiting slowly.

## [1.9.0] - 2026-11-08

### Added
- A "background" state for the three AIs: the AI is not thinking, a process runs for it and it will resume on its own.
  Its head rests while a cyan wheel turns above it. Claude: a turn that ends while commands it started in the
  background are still running (read from its transcript: the launch, and the notification of their end) or while
  background sub-agents work, instead of "done" or "working". Claude and Antigravity: a tool that has been running for
  more than 15 seconds (a foreground sub-agent, which thinks, stays "working"); the hook now records the tool a
  `PreToolUse` started. Codex: a tool call that has had no output for more than 15 seconds in its journal (command, wait
  for its agents). Antigravity's end of turn with a sub-agent still working is "background" too.

### Changed
- Tests: FsCheck 3.6.0 (was 3.4.0). The preview tool (`apercu`) enforces the code-style rules at build time, like the
  plugin and the hook: the plugin's `.editorconfig` moves to the repository root so that it covers them too (it only
  covered `plugin/src` before). The changelog carries its comparison links.

### Fixed
- Tests: no test can show a Windows error box. The test process sets the error mode (no critical-error, crash or
  missing-file box) when it loads, and every process it starts inherits it; a test checks it in a child process. The
  child is checked for the two bits that are always inherited: native loading code briefly replaces the process mode
  with exactly those two (0x8001), which once made the check fail in CI.

## [1.8.49] - 2026-11-07

### Fixed
- Codex quota with codex-lb: the gauges are computed from each account (`/api/accounts`) instead of
  `/api/usage/summary`, which counted a Pro Lite account's 5-hour window as full even with its week almost spent.
  A blocked account (rate limited, quota exceeded) now counts as empty, and an available account's 5-hour credits never
  exceed what is left of its week (Pro Lite accounts have a weekly limit only).

## [1.8.48] - 2026-11-07

### Changed
- The Logi plugin and the native hook build with the recommended .NET analyzers and the build-time code style, every
  warning an error (as NexusWpp). The same gate runs locally and in CI: the StyleCop analyzers pulled in by the
  LogiPluginApi package (CI only, thousands of ignored warnings) are left out. Nullable reference types are checked in
  every file of the plugin and the hook: values that can be missing (unread quotas, closed apps, unreadable files,
  absent UI Automation elements) are declared so, and each use handles them.
- The tests, the key preview tool (apercu) and the Stream Deck edition build with the recommended .NET analyzers, every
  warning an error; the preview and the Stream Deck edition also check nullable reference types.
- Win32 text reads (window titles and classes, short paths, process image and package names) use character buffers
  instead of StringBuilder.
- Tomlyn 2.10.2.
- The release workflow uses NexusSystem's shared release workflows at v1.6.54.
- CodeQL runs from a workflow file (same languages and default queries) instead of the default setup, so OpenSSF
  Scorecard credits the static analysis.

### Fixed
- Dependabot's NuGet updates silently found nothing: the build folders were built from `USERPROFILE`, unset on its
  Linux runner. One root `Directory.Build.props` now takes the user profile from .NET for every project.
- Live states for Antigravity: when the hook's path has spaces and Windows gives it no short form, setting them up now
  fails and restores both settings files, instead of removing Antigravity's hooks while reporting them set.

## [1.8.47] - 2026-11-07

### Fixed
- At PC start-up, with the Claude app not launched yet, Claude's key could light up (head awake, quota shown): the
  Claude Code engine, also named `claude.exe`, run outside the app (NexusSystem's logon maintenance updating plugins, a
  terminal, another tool), was taken for the app. Only the Claude desktop app now counts as Claude being open.
- Claude's key no longer shows its "finished" ring while the app is closed: an unseen finish left in Claude's session
  files from the day before ringed the key at start-up. The ring comes back as soon as the app is running.

## [1.8.46] - 2026-11-06

### Changed
- The night sky while the PC screen is off, and the keypad kept lit, are back. Removing them in 1.8.45 did not stop a
  monitor on the processor's graphics from waking up with "No signal": the cause lies elsewhere.

## [1.8.45] - 2026-11-06

### Removed
- Night sky while the PC screen is off, and the restart of the Logi plugin service that kept the keypad lit. With a
  monitor on the processor's graphics (DisplayPort), the screen kept waking up with "No signal" and the keypad
  restarted with it. The keypad now turns off with the screen, as Logi Options+ does.

## [1.8.44] - 2026-11-06

### Fixed
- A Claude session whose title only begins with "NexusRelay" (work on the product, such as "NexusRelay : sujet Codex bloqué…") was shown as a relay exchange with Codex and animated the pad while no relay was running. A Claude conversation now counts as the relay's when it lives in the relay's folder (`.nexus\relay\NexusRelay`, or `claude-projet` before) or its title is the start of the relay's wake message.

## [1.8.43] - 2026-11-06

### Changed
- Images pasted into an agent (screen capture, phone photo) follow the shared Nexus ceiling raised to 3 MB (was 500 KB):
  measured on 2026-11-06, none of the three AI apps refuses an image of 6.8 MB. Still at most 2000 px and 2.56 Mpx,
  lossless WebP while it fits; above 3 MB, lossless WebP with 2, then 3 low bits per colour dropped, and only then lossy
  WebP at quality 90, 80, 70. A phone photo (JPEG) is now sent lossless too: reduced, it fits 3 MB.

## [1.8.42] - 2026-11-06

### Changed
- Which agent opened a NexusRelay conversation is read again only when a relay message is added (30 s at most). Every
  reading, once per second, listed and dated every relay message for each relay conversation (about 2 points of one
  core on 2026-11-06).

## [1.8.41] - 2026-11-06

### Changed
- A key image is encoded only when its pixels changed. Every animation tick drew each key, then encoded it for the Logi
  service even when nothing moved, and the encoding cost as much as the drawing. Measured on 2026-11-06 over 40 s at the
  same 27.7 images per second sent to the pad: the Logi service, its extension and the Options+ agent went from 74.5 %
  to 63 % of one core. The images already had the pad's own size (116 px).

## [1.8.40] - 2026-11-06

### Changed
- The heads of working agents (or of an agent calling the user) animate at 12 frames per second, the rate of NexusWpp;
  every other key stays at 4. A working head costs about 10 more points of one core than at 8; an idle head nothing.

## [1.8.39] - 2026-11-06

### Changed
- The heads of working agents (or of an agent calling the user) animate at 8 frames per second again; every other key
  stays at 4. An idle head does not change, so its rate costs nothing. Bench of 2026-11-06 (Logi plugin service, its
  extension and the Options+ agent, share of one core): 4 frames per second on every key about 58 %, 8 on every key
  about 97 %, saturation near 20 (about 65 images per second, 157 %); the GPU is never used.

### Added
- `outils\pad.ps1 cadence [1-60]`: changes the animation rate of the running plugin for a bench, without restarting
  the Logi service, and reports the number of images sent so far. A restarted plugin returns to its shipped rate.

## [1.8.38] - 2026-11-06

### Fixed
- Claude quota: the 5-hour gauge no longer swings between two values from one reading to the next (35 % then 51 %
  on 2026-11-06). Claude for Windows writes its usage history only every quarter of an hour, sometimes every hour, and
  that history came before the live Usage button. A history sample older than 2 minutes now gives way to the live
  reading while Claude is open; it stays the fallback when Claude is closed or the button cannot be read. An accepted
  Nexus Pad plugin publication keeps priority.

## [1.8.37] - 2026-11-06

### Changed
- One animation rate for every key: 4 frames per second. The heads of working agents ran at 8 frames per second and
  the other keys at 4; the volume key and the page 2 readings at 8. Animations follow real time, so their speed is
  unchanged. Every image sent to the pad is drawn, cropped and encoded by the Logi plugin service, which pays most of
  its cost: on 2026-11-06 the service, its extension and the Options+ agent went from 65.5 % to 58 % of one core over
  60 s with an agent at work (CPU profile: about a quarter of the service's time draws and encodes pad images, a tenth
  renders the keys in this plugin).

## [1.8.36] - 2026-11-05

### Fixed
- Clock key: the third press opens the notification centre again. Once closed, the panel stays the foreground window
  but hidden (cloaked), so 1.8.35 sent Escape to it instead of reopening it; only a visible panel is closed now.

## [1.8.35] - 2026-11-05

### Fixed
- Clock key (Photo without a phone): a second press now closes the Windows notification centre with Escape, as on a
  keyboard. The panel stayed in front, so the next keys (Gmail, Calendar, Messages) no longer recognised their own
  window as the one in front. The panel is recognised by its window, not by its title, which depends on the language.

## [1.8.34] - 2026-11-05

### Added
- Codex dictation without reading the screen: setting up the Codex connector adds a single-press dictation key
  (`globalDictationSingleTap` on F13, a key no keyboard has) to `~/.codex/keybindings.json` when the user has none;
  the keypad presses it to start and again to stop. A key the user chose, and the hold-to-dictate key, are never
  changed; removing the connector removes only that F13. Without a single-press key, the microphone button is used as
  before.

## [1.8.33] - 2026-11-05

### Added
- Official app shortcuts as fallback when the screen cannot be read:
  - Dictation: Claude's own dictation shortcut (Ctrl+D by default) when its microphone button is not found, and
    Antigravity's Toggle Voice Recording (Ctrl+M by default) when its button is not found; Antigravity's recording is
    also confirmed through UI Automation when its debugging port is closed.
  - Paste (screenshot, phone photo) without a debugging port: Codex's "focus main chat" (Alt+L) and Antigravity's
    Focus Input (Ctrl+L) put the cursor in the message box first.
  - Opening a Codex conversation that is waiting for you, when neither its link nor its port answers: Codex's "next
    chat needing your attention" (Ctrl+Alt+A).

### Changed
- Keys sent by the keypad carry the key's physical code, like a real keyboard, so shortcuts read by key position also
  work with AZERTY and other layouts.

## [1.8.32] - 2026-11-05

### Changed
- Seen and question states now come from each app's own signals, the guessed ones staying as fallback:
  - Claude: a finished turn is classified by the app itself (its end-of-turn summary: blocked = waiting for you,
    completed or review ready = done), matched to that very turn; the Stop hook's official `last_assistant_message`
    is read before the transcript.
  - Codex: a conversation leaving the app's unread list counts as seen, and "Mark as unread" lights the alert again.
  - Antigravity: archived conversations are no longer shown or counted, and renamed ones keep their new title.
- Seen conversations are kept on disk for 24 hours: restarting the plugin or Logi no longer lights finished turns
  you already read.

### Added
- Claude: the official `PermissionRequest` hook shows the padlock as soon as the approval box opens; MCP questions
  (`Elicitation`, `agent_needs_input` and `elicitation_dialog` notifications) show the question state.
- Antigravity: a turn ending in error (`terminationReason`, `error`) shows the error face, or the usage-limit face
  for quota and capacity errors.
- Antigravity started without its debugging port: conversations known from their hooks in the last 12 hours stay
  on the keypad (working, question, done).

## [1.8.31] - 2026-11-05

### Fixed
- An Antigravity conversation opened by the user no longer keeps its green "done" ring when Antigravity was started
  without its debugging port (Windows shortcut): the keypad also reads the last view Antigravity records itself
  (`annotations/<id>.pbtxt`).

## [1.8.30] - 2026-11-04

### Added
- Ready profile (`.lp5`) for each Logi Marketplace edition, attached to every release beside the packages and listed
  in `SHA256SUMS.txt`: import it in Logi Options+ (keypad, **+** on the profile bar) and every key is already in place.
  Logitech's marketplace team confirmed that universal plugins cannot ship a default profile and suggested sharing it
  separately; the plugins themselves are unchanged. Built by `outils/profil_marketplace.ps1`, reproducible byte for
  byte.

## [1.8.29] - 2026-11-04

### Fixed
- Relay conversations are still recognized since NexusRelay 1.13.2 writes its name without a space: titles starting
  with "NexusRelay" or "Nexus Relay" both count, and the default relay topic is "NexusRelay".

## [1.8.28] - 2026-11-04

### Changed
- Claude display name written NexusKeypad, without a space, like the repository, Codex and the NexusSystem catalog.

## [1.8.27] - 2026-11-04

### Changed
- Product icon now shows the julienpiron.fr medallion on a transparent background at every size (no dark square), with the knot badge at the bottom of the ring kept whole.
- Same `.gitattributes` as every Nexus repository (2026-11-04): LF in Git and on disk, CRLF only for `.cmd`/`.bat`; files stored with CRLF were renormalized once.

## [1.8.26] - 2026-11-03

### Added
- Antigravity Marketplace bundle `marketplace/antigravity` (passes `agy plugin validate --strict`), kept outside
  `connecteurs/` so the Logi package carries no second copy: the connector's manifest (now with the Marketplace
  fields), guide and logo, a README for the detail page, and a root `hooks.json` whose handlers run
  `scripts/hook.cmd`. A Marketplace copy cannot name the helper's path (Antigravity expands only `${PLUGIN_ROOT}`), so
  the script runs `%LOCALAPPDATA%\NexusKeypad\hook\nexuskeypad-hook.exe` when the NexusKeypad plugin installed it and
  answers `{}` otherwise.

## [1.8.25] - 2026-11-02

### Removed
- The Nexus Relay line under Claude's prompt (Codex and AGY dots, unread messages): the Claude connector now only
  writes Claude's exact usage for the keypad.

## [1.8.24] - 2026-11-02

### Fixed
- Relay courier animation on the keypad was hardcoded to Claude as the sole initiator, walking between Claude and Codex
  even when Antigravity initiated the exchange. The animation now supports all 6 directions across the Trinity: the
  messenger travels between the actual initiating agent (touch 0, 1, or 2) and the worker agent (touch 0, 1, or 2),
  carrying the initiator's sprite (Clawd, Codex cloud, or Antigravity arch) and returning with the worker's sprite.

## [1.8.23] - 2026-11-02

### Fixed
- Relay messengers still ran between Claude and Codex with no relay in use when the relay's Codex workspace is the
  user's own folder (such as Documents): every chat opened there counted as a relay wake. A Codex chat is now a relay
  wake only when the relay's own records list it (topic conversation or owned thread), or when it is opened in the
  relay's dedicated project folder.

## [1.8.22] - 2026-11-02

### Added
- Night sky while the PC screen is off: the keys form one starry sky with a crescent moon in the middle, an AI that
  works or calls the user stays awake on its key, the others sleep ("z").
- NexusKeypad edition: the keypad stays lit when the screen turns off. Logi Options+ puts it to sleep at that moment,
  with no setting to prevent it; a plugin service restarted afterwards keeps it lit without waking the screen, so the
  plugin restarts the service with Logi's official tool 3 s after the screen goes off (at most once a minute; the
  keyboard and mouse agent is not touched). Marketplace editions leave the keypad to Logi.

## [1.8.21] - 2026-11-02

### Changed
- On a very wide screen (more than φ² times wider than tall, such as a 5120×1440 32:9 monitor, two screens in one),
  an AI app opened or brought forward by the pad no longer fills the whole width: it takes the half opposite the window
  the user was working in (the right half when that window is full screen or elsewhere), and stays on the half it
  already occupies. 16:9 and 21:9 screens keep the maximized window.

### Fixed
- Relay messengers ran between Claude and Codex while no relay was in use: a Codex chat counted as a relay wake as soon
  as its header named the relay folder, and Codex lists that folder among the workspace roots of every chat. A chat is
  now a relay wake only when it is opened in the relay's Codex project folder, the relay's own rule.

## [1.8.20] - 2026-11-02

### Fixed
- Screenshot and Phone photo keys took 4 to 7 s before the image was pasted on a PC with a 5120×1440 screen: the pad
  wrote the full screen as PNG (0.9 s), encoded lossless WebP at maximum effort, then a second, reduced PNG, and did all
  of it for the photo at the key press. It now follows the shared Nexus image rule with a single file, the WebP: the
  screenshot is reduced in memory and written once (about 150 ms instead of 1.3 s), lossless WebP uses a faster effort
  (4 to 5 times quicker, 2 to 5 % larger), a phone photo (JPEG, already lossy) skips the lossless attempt that never
  fit 500 KB, and the photo is prepared as soon as it reaches the key, so the press only fills the clipboard (about
  140 ms instead of 2.6 s). The clipboard still offers the image (PNG, built in memory only, and CF_DIB) and the WebP
  file.

## [1.8.19] - 2026-11-02

### Changed
- The 18 keys go on their own **NexusKeypad** profile of the keypad, made active, instead of the user's profile: that
  profile is left as it is, and switching back to it in Logi Options+ is enough to leave the keypad's keys. `-Remove`
  makes the original profile active again and deletes the NexusKeypad profile. A profile edited by an earlier version
  is restored from its one-time backup (`ProfileInfo.before-nexuskeypad.json`).
- `get.ps1 -KeysOnly`: places the keys of the plugin already installed, nothing downloaded; nothing if already placed.

### Fixed
- Codex hooks waited at most 0.16 s for their session lock: eight parallel tool calls on a busy PC (or in CI) lost
  events, so the keypad missed a running call. The wait is now 0.618 s, still under the second the harness tolerates.

## [1.8.18] - 2026-11-02

### Fixed
- Placing the keys or installing refused to stop the Logi plugin service with "A process with a Logi plugin service
  name has an unexpected identity" when a service process was exiting: an exiting process no longer reports its path.
  It is now waited for and ignored once gone, so `get.ps1` places the 18 keys on a new PC instead of leaving the
  keypad's original keys.

## [1.8.17] - 2026-10-31

### Fixed
- A Claude turn stopped with Escape returns the keypad to idle within seconds instead of showing Claude at work for
  up to two hours: Escape sends no hook, so the keypad now also reads the turn edge Claude's app records
  (`lastTurnEdgeAt`) and treats a working state older than that edge as a finished turn.

### Changed
- Opening a Codex conversation from the keypad uses Codex's official link (`codex://threads/<id>`); the click on its
  row through the debugging port remains as a fallback when the link does not open it.

### Security
- CodeQL (GitHub default setup) and the OpenSSF Scorecard workflow run again on every push to main.

## [1.8.16] - 2026-10-31

### Fixed
- Antigravity closed to the notification area no longer shows the restart arrow: in the background it keeps only
  its splash page (a data: page), now read as a hidden window that the keypad reopens without restarting.

## [1.8.15] - 2026-10-31

### Fixed
- `get.ps1` no longer fails its first installation attempt: the Logi Options+ agent restarts a just-stopped plugin
  service once, about a second later, so the installer now waits for the stopped processes to exit, then stops a
  restarted service again (three stops at most) before copying.

## [1.8.14] - 2026-10-31

### Fixed
- When Claude or Codex updates NexusSystem's copy of the plugin itself (a new installed folder, the same enabled
  identity), the keypad re-attests it silently instead of showing the wrench and asking « Connect … to NexusKeypad? »
  again. Any other repair keeps its question.

## [1.8.13] - 2026-10-31

### Changed
- `connecteurs/` is now also NexusSystem's catalog plugin for Claude Code and Codex (pinned at the release tag, the
  product's version): Claude's hooks and modules in `hooks/`, Codex's hooks inline in its manifest, both reaching
  `%LOCALAPPDATA%\NexusKeypad\hook\nexuskeypad-hook.exe`, so nothing is rendered per PC for them.
- Codex: an installed `nexus-keypad@nexus-plugins` is adopted as the connector, as for Claude; the personal copy and
  its catalog entry are retired. Without NexusSystem's catalog, the connector still installs the same files locally.
- The connector version is the product's version.
- The Claude connector now carries the Nexus Relay line under the prompt and the end of background agents
  (`SubagentStop`), like NexusSystem's catalog copy.

### Fixed
- `get.ps1`: an update with the keys already on the keypad no longer asks to place them nor explains how.

## [1.8.12] - 2026-10-31

### Changed
- Release: a push whose version is not released yet becomes the stable release right after its development build
  (same files); "Run workflow" still promotes by hand.
- Updates go through `get.ps1` only (run by the user, the NexusSystem app or an AI); `get.ps1` checks the package
  against the release's `SHA256SUMS.txt` and its build provenance stays attested.

### Fixed
- An update no longer leaves the enabled connectors to repair (« Le connecteur doit être mis à jour ») : `get.ps1`
  refreshes, without asking, the connections already enabled; those turned off or never set up stay as they are.
- The connection questions (« Connect Claude to NexusKeypad? » and the others) open on their own thread: a dialog
  left open no longer freezes the keypad.
- A connector to repair or enable stays visible (the wrench or the unplugged plug in front of the character) while its
  AI works; activity used to hide it for as long as the AI kept working.

### Removed
- The offline release signature (`SHA256SUMS.txt.sig`, `outils/signer_release.ps1`, the `signed` job) and its check
  in `get.ps1`.
- The complete edition's self-update: the daily check, the pipe's `update` command, the folder swap of
  `nexuskeypad-hook.exe` and the `miseAJourAuto` setting. `~/.nexus/status/keypad.json` now only carries the loaded
  version for the Nexus app.
- CodeQL and OpenSSF Scorecard workflows and badge.

## [1.8.11] - 2026-10-31

### Fixed
- Claude's quota no longer disappears when you switch conversations: the quota published by any recent session of the
  connected account is accepted when it belongs to the same organisation as the displayed conversation (or, with no
  conversation displayed, to the account's only organisation among recent sessions). Other accounts and organisations
  are still refused.
- The pad no longer shows Claude as finished while background sub-agents it launched are still working: the hook tracks
  background `Agent`/`Task` calls per session, a `Stop` with agents still running keeps the pad on "working" without the
  end-of-turn note, and the last agent's `SubagentStop` ends the wait (the note plays at the end of the turn in which
  Claude reads their report). The Claude connector now subscribes to `SubagentStop`. Connector version 1.2.5.

## [1.8.10] - 2026-10-31

### Added
- `get.ps1` honours `NEXUS_RELEASE_TAG` (a stable `vX.Y.Z` or development `vX.Y.Z-dev.N` tag, set by NexusSystem's
  development channel), with the same signature and checksum checks.

### Changed
- Releases use two channels, with NexusSystem's shared release workflows: every push to `main` checks one version
  everywhere, runs `test.ps1`, builds the four `.lplug4` packages, `get.ps1` and `SHA256SUMS.txt`, and publishes the
  attested development pre-release `vX.Y.Z-dev.N`; "Run workflow" promotes a signed development build unchanged to
  the stable `vX.Y.Z`. The tag-triggered draft release is gone.
- `outils/signer_release.ps1` signs a development pre-release before promotion (its signature follows it into the
  stable release) and checks the provenance attested by the shared development workflow. Promotion refuses an
  unsigned build.

### Security
- The automatic update only accepts a stable `vX.Y.Z` tag from `releases/latest`, never a pre-release.

## [1.8.9] - 2026-10-31

### Changed
- One connector folder (`connecteurs/`) now serves Claude Code, Codex and Antigravity: one shared guide
  (`skills/nexus-keypad/SKILL.md`), one logo, plus each host's own manifest and hooks (`.claude-plugin` +
  `claude/`, `.codex-plugin` + `codex/`, root `plugin.json` + `hooks.json`). The former per-host copies
  (`connecteurs/claude`, `connecteurs/codex`, `connecteurs/antigravity`) are gone. Installed layouts are unchanged
  except the shared guide and Claude's logo, now at `assets/logo.png`. Connector version 1.2.4.

## [1.8.8] - 2026-10-30

### Fixed
- Claude's selected enabled catalog takes precedence over a disabled legacy catalog. Repair no longer reactivates the
  private connector after a switch to the public NexusSystem catalog or enables duplicate hooks.
- A selected plugin without its manager installation and complete cache reports pending, without changing settings
  or issuing a ready receipt. Once Claude has installed it, explicit repair adopts that identity while preserving all
  other choices; duplicate active identities are refused. The snapshot exposes a structured pending reason without
  exporting free-form details or claiming that an activity event was observed.
- Claude receipts attest the cache currently selected by its plugin manager. A versioned cache switch requires
  explicit adoption even if the previous complete cache remains on disk under the same plugin identity.

## [1.8.7] - 2026-10-30

### Fixed
- If restoring a failed plugin copy also fails, keep the verified backup and leave the plugin service stopped.
  Never load a partial package or discard its recovery files; the installer reports their location.

## [1.8.6] - 2026-10-30

### Fixed
- A failed package copy restores the complete previous plugin before restarting its service. A partial first
  installation is removed before restart. Isolated fault-injection checks cover both failures and prove that the
  service never reloads mixed or partial package files.

## [1.8.5] - 2026-10-30

### Fixed
- The installer started in Windows PowerShell 5.1 by PowerShell 7 prefers its own standard modules. `Get-FileHash`
  and `Unblock-File` no longer fail on inherited Core-only modules. User and third-party module paths are retained.
  An isolated regression runs the real installer preamble with Core modules first, verifies the checksum and download
  marker removal, and checks module precedence without installing or touching the pad.
- Installation, the restart helper and automatic updates share one service controller: Logitech's signed official
  tool cooperatively stops only the plugin service before files change, and WMI starts it in a headless console.
  The keyboard/mouse agent is never stopped. Missing components, wrong signatures or process paths, refused commands
  and unconfirmed stops refuse the copy; there is no process-kill fallback. A completed stop that fails after stopping
  recovers the verified service while preserving the error; an in-flight command never triggers a concurrent restart.
- Source installation refreshes the shared connector helper even when its previous receipt still reports ready.
  The signed update package carries its service controller beside the native updater, so every update uses the same
  signature, identity and bounded-wait checks.

## [1.8.4] - 2026-10-30

### Fixed
- The pad pipe test registers its server before starting the client and awaits asynchronous I/O with cancellation.
  On a busy CI runner, delayed server startup could miss the client's absence window and wait forever; this blocked
  release builds without a failed assertion. The client protocol and every test assertion remain unchanged.

## [1.8.3] - 2026-10-30

### Fixed
- Repairing or updating one AI connector no longer marks the other two as needing repair. The shared native helper,
  bundled templates and quota module have one runtime receipt; each AI keeps its own plugin receipt and setup time.
  Existing receipts migrate without rewriting another connector's activation. Missing or changed shared files still
  invalidate every affected connector, while damage to an individual plugin remains visible after a shared repair.

## [1.8.2] - 2026-10-30

### Fixed
- The complete Claude plugin installed from NexusSystem's public catalog (`nexus-keypad@nexus-plugins`) is recognised
  like the private one (`nexus-keypad@nexus`): enabled in place as the connector, without the pad's own hooks, quota mod
  or public identity set a second time.
- Codex: a `nexus-keypad` plugin enabled from the Nexus catalogs (`@nexus`, `@nexus-plugins`) cannot carry hooks with this
  PC's helper path; the connector keeps the only hooks (real path of `nexuskeypad-hook.exe`) and turns those copies off
  instead of doubling them. A copy enabled again is reported as needing repair.

### Added
- `get.ps1` sets up the connector of each AI app found (Claude, Codex, Antigravity: plugin and hooks with the installed
  helper's real path) right after installing, as the source installer does; a failure only leaves that key unplugged.

### Security
- No more `-EncodedCommand`: the update helper's WMI restart of Logi and the Antigravity signature check run a fixed,
  readable PowerShell command and pass their values in environment variables (antivirus heuristics flag encoded commands).
- Installation documented without `irm | iex`: the NexusSystem app (Microsoft Store), or the release's `get.ps1` downloaded,
  checked against the signed `SHA256SUMS.txt` and run with `-File`.
- The CI scans the whole history for secrets (gitleaks) and audits the workflows (zizmor) on every push and pull
  request, next to the tests.

### Changed
- Tests follow the C# standard: four xUnit v3 projects (`tests/Logique`, `tests/Connecteurs`, `tests/Isole`,
  `tests/Hook`) run by `dotnet test` on Microsoft.Testing.Platform, selected by a root `global.json`. They replace the
  `verifier` program, the ten `dotnet run` test programs and the Python `unittest` suite (147 tests, 9 of them
  explicit checks on the real PC that run only on request).
- The native hook's tests run the published Native AOT `nexuskeypad-hook.exe`, which the test build publishes itself.
- `test.ps1` is a short entry point: the five plugin editions, the `get.ps1` checks, `dotnet test`, `node --test`.
  The CI runs it unchanged; builds and test results stay in `~\.nexus\build\nexuskeypad\test`.
- `outils/pad.ps1` replaces `outils/pad.py`: the same one-line pipe client (waits while busy, plugin absent after 1 s,
  10 s at most, a command never replayed) and the same `--reel` guard before any real press or reading.

### Removed
- Python from the repository: `outils/pad.py`, `outils/placer_touches.py` (`nexuskeypad-hook.exe keys` does the
  same, as in `get.ps1`), `outils/verifier_parite.py` (`pad.ps1 snapshot` and `nexuskeypad-hook.exe connecteur <agent>
  status` give the same read-only data), the opt-in `tests/lanceurs/lifetime.py` and the Python tests. Building or
  testing from source no longer needs Python.

## [1.8.1] - 2026-10-30

### Changed
- Shared log in `%USERPROFILE%\.nexus\journal\keypad` instead of AppData, like every Nexus product: an MSIX app
  (Claude Desktop) redirects its AppData writes into a private copy other tools cannot see.
- A question that has been looked at (conversation opened from the pad or shown in the app) goes back to idle: the
  "?" clears as soon as the conversation is shown, like the done check mark, for Claude, Codex and Antigravity.
  Permission requests (padlock) stay until they are answered.

### Fixed
- Black pad after Claude restarts: Logi Options+ restarted from an AI app stayed tied to that app's package and died
  with it. Logi is now restarted through WMI only (parent: the WMI service), with no child-process fallback.
- Black pad after an install: copying the plugin while Logi was running triggered a reload, cut by the restart, and
  Logi quarantined the plugin. The installer now stops Logi before copying.
- Builds (plugin, hook, tests, packages) go to `~\.nexus\build\nexuskeypad` instead of AppData.
- Independent launches: AI apps, links and PWAs opened from the pad, and its update helper, go through the Explorer
  desktop broker, outside the caller's process tree and job. No child fallback after an uncertain launch; a failed
  Logi restart is reported instead of announcing a successful update.
- Antigravity quota: the model actually selected picks its quota family; an exhausted family no longer hides Gemini
  Flash. The shared cache keeps every group, its reset times and the selection; an old copy never overwrites newer usage.
- Antigravity navigation: one bounded passive wait before a single open, then the displayed conversation ID is
  checked; no more ten-click replays, and an archived conversation is recognised by its real view.
- Dictation: start confirmed by the active record button; stop confirmed separately from a failure or an unknown
  read, without a second click.
- Claude: the usage details use the button's `Invoke` click when Chromium exposes a `Collapse` that does nothing;
  microphone commands prefer `Invoke` and their effect is read back.
- Assembly and package versions aligned (1.8.1); logs no longer report the old 1.0.0 assembly version.

## [1.8.0] - 2026-10-29

### Security
- Verify the release manifest's ECDSA signature before extracting or executing a downloaded package during first
  installation. The public trust keys match those already used by the complete edition's automatic updater.

### Fixed
- Register and install Claude's public connector through Claude's own plugin commands, and verify its installed
  package before reporting readiness. The type contract now passes Claude's native plugin validation.
- Claude's idle character no longer shows a permanent connection progress bar while waiting for a future event.
  Diagnostics separately retain whether the first callback was observed; an unchanged enable preserves its receipt.
- Identify Claude's displayed conversation by its exact ID when filtering the conversation counter and workspace.
- Read both Antigravity usage layouts passively, with visible-text evidence and reset delays before or after values.
- Recognize both Antigravity usage-panel layouts and require visible limits and percentages before reporting success.
- Accept canonical local Windows transcript paths in Codex hooks while still rejecting network paths.
- Verify Claude conversations by their exact session ID, scope usage to the displayed organization and verify
  usage-panel changes before reporting success. Keypad-driver refusals and unknown results remain explicit.
- An Antigravity cancellation returns to idle without a completion note; active children still keep it busy.
- Bundle the official NexusKeypad branding with the AI connectors, including the Codex listing/composer images and
  the Antigravity logo, with installed assets covered by the connector file checks.
  The Claude connector includes its documented directory icon and image; current local Claude listings may ignore it.

### Added
- One NexusKeypad connection flow for Claude, Codex and Antigravity: a named connector per AI, targeted setup from
  its key, diagnostics and disabling, with one shared keypad engine and no extra MCP server.
- Bundled native Codex lifecycle hooks, subject to Codex's own review and trust. Existing log readers remain
  available for history, usage, asynchronous questions and fallback; no duplicate completion sound is added.
- Claude and Antigravity completion notes use the same native helper, with distinct notes and no Python dependency.
- Connection states use a plug, wrench, restart arrow, crossed eyes or sleeping face without covering Relay miniatures.
- Read-only JSON snapshots and parity reports distinguish installed configuration from native UI validation.
- Verified local app launch options, including Antigravity without editing shortcuts. A restart requires consent,
  respects active work and dictation, and never forces an app to close.

## [1.7.7] - 2026-10-28

### Fixed
- Codex state: unanswered question cards from previous turns no longer keep the key on "?" during a new task or
  after it finishes. Ongoing work takes priority over asynchronous cards; questions from the current turn remain
  visible when that turn finishes. Historical cards are neither deleted nor marked as answered.
- Codex state: aborting a turn with an asynchronous question returns the key to idle. The card remains in the
  history for a real later reply, without interfering with questions from subsequent turns.

## [1.7.6] - 2026-10-28

### Fixed
- Live states action: where a complete Claude plugin `nexus-keypad@nexus` is declared (live-state hooks, task-end note
  and quota in one plugin), the keypad turns that plugin on or off instead of adding its own hooks and quota module
  next to it, which would have run twice; it also counts it as "set up".

## [1.7.5] - 2026-10-27

### Fixed
- Automatic update: every downloaded package stayed in `%LOCALAPPDATA%\NexusKeypad\maj`, one per version; it is now
  deleted once unpacked and checked, with those of past versions.

## [1.7.4] - 2026-10-27

### Added
- Property-based tests (FsCheck) of the readers of other apps' files: Codex logs and Antigravity's quota page, random
  or damaged real input; never an exception, shares always between 0 and 1.
- Build provenance attached to every release (`NexusKeypad-X.Y.Z.intoto.jsonl`, the Sigstore bundle of the
  attestation), also attached to releases 1.6.2 to 1.7.3.

### Fixed
- Antigravity quota: a percentage above 100 (damaged text) gave a negative share; now ignored.
- Codex logs and Claude sessions: a string with an invalid escape stopped reading the states; now decoded as JSON.

## [1.7.3] - 2026-10-27

### Fixed
- Codex: questions asked without waiting (`request_user_input_async`) now show on its key until answered, even after
  the turn ends (Nexus Relay chats excepted).

## [1.7.2] - 2026-10-27

### Removed
- Quota pace signs (1.7.0, 1.7.1): the gauges already show what is left. The quota row is back to its 1.6.2 look.

## [1.7.1] - 2026-10-27

### Changed
- Quota pace: no more pink gauges. An animated sign next to the open-conversation count shows the usage projected at
  reset (busiest window): green thumb (under 80%), blue check (80–100%), orange hourglass (100–125%), red warning
  triangle (over 125%); no sign when it cannot be judged.

## [1.7.0] - 2026-10-27

### Added
- Burn rate: a 5-hour or weekly quota gauge turns pink when, at the current pace, that window will run out before it
  resets (share used ÷ share of the window elapsed above 100%). No warning rather than a false one: without a known
  reset time, in the first 10% of the window, or once the quota is already full. Reset times come from Claude's
  `nexuskeypad-quota` module, Codex's logs or, with Codex LB, each account (the pool runs out before the first account
  resets, at the sum of their rates), and each Antigravity model group.

## [1.6.2] - 2026-10-27

### Security
- Complete edition automatic update: the release's `SHA256SUMS.txt` must carry a valid offline signature
  (`SHA256SUMS.txt.sig`, ECDSA P-256), checked against the public key built into the plugin; the private key is never on
  GitHub, so a compromised repository cannot push an update. Releases are created as drafts and published once signed.

### Fixed
- After an update, the reloaded plugin keeps the `done` state in `~/.nexus/status/keypad.json` until the next check, and
  keeps the latest known version instead of `null`.

## [1.6.1] - 2026-10-27

### Fixed
- Complete edition (NexusKeypad) automatic update: the folder swap failed for real, because Logi Plugin Service keeps
  the plugin's DLLs open and does not unload it first. `nexuskeypad-hook swap` now, when the folder stays locked, stops
  `LogiPluginService`, `LogiPluginServiceExt` and `logioptionsplus_agent` for the swap (keypad idle, as `get.ps1` does),
  then restarts the agent detached, even on failure. Only the real Logi plugin folder triggers this.
- A failed swap now reports the version actually in place (the previous one, restored) instead of `null`, and a
  readable message instead of a raw resource key.

## [1.6.0] - 2026-10-27

### Added
- Complete edition (NexusKeypad): a local usage journal, one line per key press in
  `%LOCALAPPDATA%\NexusJournal\keypad\<ai>\<YYYY-MM>.jsonl` (key, AI app aimed at, duration, outcome, short fixed
  cause), never a conversation title, text or path; it never leaves the PC and `NEXUS_JOURNAL=0` turns it off. The
  Marketplace editions have none.

### Changed
- The plugin log no longer holds conversation titles (the "conversations" line and the warning of a key that could
  not open a conversation had them): only states, conversation counts and quotas remain.

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

[Unreleased]: https://github.com/pironjulien/NexusKeypad/compare/v1.9.6...HEAD
[1.9.6]: https://github.com/pironjulien/NexusKeypad/compare/v1.9.5...v1.9.6
[1.9.5]: https://github.com/pironjulien/NexusKeypad/compare/v1.9.4...v1.9.5
[1.9.4]: https://github.com/pironjulien/NexusKeypad/compare/v1.9.3...v1.9.4
[1.9.3]: https://github.com/pironjulien/NexusKeypad/compare/v1.9.2...v1.9.3
[1.9.2]: https://github.com/pironjulien/NexusKeypad/compare/v1.9.1...v1.9.2
[1.9.1]: https://github.com/pironjulien/NexusKeypad/compare/v1.9.0...v1.9.1
[1.9.0]: https://github.com/pironjulien/NexusKeypad/compare/v1.8.49...v1.9.0
[1.8.49]: https://github.com/pironjulien/NexusKeypad/compare/v1.8.48...v1.8.49
[1.8.48]: https://github.com/pironjulien/NexusKeypad/compare/v1.8.47...v1.8.48
[1.8.47]: https://github.com/pironjulien/NexusKeypad/compare/v1.8.46...v1.8.47
[1.8.46]: https://github.com/pironjulien/NexusKeypad/compare/v1.8.45...v1.8.46
[1.8.45]: https://github.com/pironjulien/NexusKeypad/compare/v1.8.44...v1.8.45
[1.8.44]: https://github.com/pironjulien/NexusKeypad/compare/v1.8.43...v1.8.44
[1.8.43]: https://github.com/pironjulien/NexusKeypad/compare/v1.8.42...v1.8.43
[1.8.42]: https://github.com/pironjulien/NexusKeypad/compare/v1.8.41...v1.8.42
[1.8.41]: https://github.com/pironjulien/NexusKeypad/compare/v1.8.40...v1.8.41
[1.8.40]: https://github.com/pironjulien/NexusKeypad/compare/v1.8.39...v1.8.40
[1.8.39]: https://github.com/pironjulien/NexusKeypad/compare/v1.8.38...v1.8.39
[1.8.38]: https://github.com/pironjulien/NexusKeypad/compare/v1.8.37...v1.8.38
[1.8.37]: https://github.com/pironjulien/NexusKeypad/compare/v1.8.36...v1.8.37
[1.8.36]: https://github.com/pironjulien/NexusKeypad/compare/v1.8.35...v1.8.36
[1.8.35]: https://github.com/pironjulien/NexusKeypad/compare/v1.8.34...v1.8.35
[1.8.34]: https://github.com/pironjulien/NexusKeypad/compare/v1.8.33...v1.8.34
[1.8.33]: https://github.com/pironjulien/NexusKeypad/compare/v1.8.32...v1.8.33
[1.8.32]: https://github.com/pironjulien/NexusKeypad/compare/v1.8.31...v1.8.32
[1.8.31]: https://github.com/pironjulien/NexusKeypad/compare/v1.8.30...v1.8.31
[1.8.30]: https://github.com/pironjulien/NexusKeypad/compare/v1.8.29...v1.8.30
[1.8.29]: https://github.com/pironjulien/NexusKeypad/compare/v1.8.28...v1.8.29
[1.8.28]: https://github.com/pironjulien/NexusKeypad/compare/v1.8.27...v1.8.28
[1.8.27]: https://github.com/pironjulien/NexusKeypad/compare/v1.8.26...v1.8.27
[1.8.26]: https://github.com/pironjulien/NexusKeypad/compare/v1.8.25...v1.8.26
[1.8.25]: https://github.com/pironjulien/NexusKeypad/compare/v1.8.24...v1.8.25
[1.8.24]: https://github.com/pironjulien/NexusKeypad/compare/v1.8.23...v1.8.24
[1.8.23]: https://github.com/pironjulien/NexusKeypad/compare/v1.8.22...v1.8.23
[1.8.22]: https://github.com/pironjulien/NexusKeypad/compare/v1.8.21...v1.8.22
[1.8.21]: https://github.com/pironjulien/NexusKeypad/compare/v1.8.20...v1.8.21
[1.8.20]: https://github.com/pironjulien/NexusKeypad/compare/v1.8.19...v1.8.20
[1.8.19]: https://github.com/pironjulien/NexusKeypad/compare/v1.8.18...v1.8.19
[1.8.18]: https://github.com/pironjulien/NexusKeypad/compare/v1.8.17...v1.8.18
[1.8.17]: https://github.com/pironjulien/NexusKeypad/compare/v1.8.16...v1.8.17
[1.8.16]: https://github.com/pironjulien/NexusKeypad/compare/v1.8.15...v1.8.16
[1.8.15]: https://github.com/pironjulien/NexusKeypad/compare/v1.8.14...v1.8.15
[1.8.14]: https://github.com/pironjulien/NexusKeypad/compare/v1.8.13...v1.8.14
[1.8.13]: https://github.com/pironjulien/NexusKeypad/compare/v1.8.12...v1.8.13
[1.8.12]: https://github.com/pironjulien/NexusKeypad/compare/v1.8.11...v1.8.12
[1.8.11]: https://github.com/pironjulien/NexusKeypad/compare/v1.8.10...v1.8.11
[1.8.10]: https://github.com/pironjulien/NexusKeypad/compare/v1.8.9...v1.8.10
[1.8.9]: https://github.com/pironjulien/NexusKeypad/compare/v1.8.8...v1.8.9
[1.8.8]: https://github.com/pironjulien/NexusKeypad/compare/v1.8.7...v1.8.8
[1.8.7]: https://github.com/pironjulien/NexusKeypad/compare/v1.8.6...v1.8.7
[1.8.6]: https://github.com/pironjulien/NexusKeypad/compare/v1.8.5...v1.8.6
[1.8.5]: https://github.com/pironjulien/NexusKeypad/compare/v1.8.4...v1.8.5
[1.8.4]: https://github.com/pironjulien/NexusKeypad/compare/v1.8.3...v1.8.4
[1.8.3]: https://github.com/pironjulien/NexusKeypad/compare/v1.8.2...v1.8.3
[1.8.2]: https://github.com/pironjulien/NexusKeypad/compare/v1.8.1...v1.8.2
[1.8.1]: https://github.com/pironjulien/NexusKeypad/compare/v1.8.0...v1.8.1
[1.8.0]: https://github.com/pironjulien/NexusKeypad/compare/v1.7.7...v1.8.0
[1.7.7]: https://github.com/pironjulien/NexusKeypad/compare/v1.7.6...v1.7.7
[1.7.6]: https://github.com/pironjulien/NexusKeypad/compare/v1.7.5...v1.7.6
[1.7.5]: https://github.com/pironjulien/NexusKeypad/compare/v1.7.4...v1.7.5
[1.7.4]: https://github.com/pironjulien/NexusKeypad/compare/v1.7.3...v1.7.4
[1.7.3]: https://github.com/pironjulien/NexusKeypad/compare/v1.7.2...v1.7.3
[1.7.2]: https://github.com/pironjulien/NexusKeypad/compare/v1.7.1...v1.7.2
[1.7.1]: https://github.com/pironjulien/NexusKeypad/compare/v1.7.0...v1.7.1
[1.7.0]: https://github.com/pironjulien/NexusKeypad/compare/v1.6.2...v1.7.0
[1.6.2]: https://github.com/pironjulien/NexusKeypad/compare/v1.6.1...v1.6.2
[1.6.1]: https://github.com/pironjulien/NexusKeypad/compare/v1.6.0...v1.6.1
[1.6.0]: https://github.com/pironjulien/NexusKeypad/compare/v1.5.0...v1.6.0
[1.5.0]: https://github.com/pironjulien/NexusKeypad/compare/v1.4.4...v1.5.0
[1.4.4]: https://github.com/pironjulien/NexusKeypad/compare/v1.4.3...v1.4.4
[1.4.3]: https://github.com/pironjulien/NexusKeypad/compare/v1.4.2...v1.4.3
[1.4.2]: https://github.com/pironjulien/NexusKeypad/compare/v1.4.1...v1.4.2
[1.4.1]: https://github.com/pironjulien/NexusKeypad/compare/v1.4.0...v1.4.1
[1.4.0]: https://github.com/pironjulien/NexusKeypad/compare/v1.3.0...v1.4.0
[1.3.0]: https://github.com/pironjulien/NexusKeypad/compare/v1.2.0...v1.3.0
[1.2.0]: https://github.com/pironjulien/NexusKeypad/compare/v1.0.0...v1.2.0
[1.0.0]: https://github.com/pironjulien/NexusKeypad/releases/tag/v1.0.0
