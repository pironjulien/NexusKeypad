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
- No Gmail badge when Gmail is open in a Chrome tab: tab titles (Chrome's tab strip, read through UI Automation even
  with the window behind) count like window titles; read every 3 s, off the animation thread.
- Updates key: it only searched, then the Store and Windows Update offered their updates without installing them. It
  now installs everything: search, then "Update all" (Store) and "Download & install all" (Windows Update) pressed
  through UI Automation, windows shown behind the others (minimised, they expose no buttons) then minimised again;
  never a restart; the key animates until it is done.
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
