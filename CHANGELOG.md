# Changelog

All notable changes to this project are documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

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
