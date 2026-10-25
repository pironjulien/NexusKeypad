# Changelog

All notable changes to this project are documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Added
- Gmail unread count from [NexusBrowser](https://github.com/pironjulien/NexusBrowser) (its opt-in "Gmail unread for the
  pad" box) when Gmail is not open in Chrome: `~/.nexus/nexus-browser/compteurs.json`, used if under 3 minutes old.
- README: "Better with the Nexus family, fine on its own", what each part adds and what replaces it when it is missing.
- `pad.py capture 2`: picture of page 2, read and drawn fresh even while it is hidden.

### Fixed
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
