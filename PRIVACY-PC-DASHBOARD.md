# Privacy Policy

**PC Dashboard** (plugin `PcDashboard`, project "NexusKeypad") · Effective October 25, 2026

**In short: the Plugin collects no personal data. Everything it reads stays on your computer; nothing is sent to the
Developer or to anyone else.** It has no account, no analytics, no telemetry, no advertising and no server.

The controller of this policy is **Julien Piron** ("the Developer"), author of the Plugin. Logitech's own processing
(your Logitech account, the Marketplace, Logi Options+) is covered by
[Logitech's privacy policy](https://www.logitech.com/legal/product-privacy-policy.html), not by this one.

## What the Plugin reads, on your computer only

The Plugin reads data only to draw the keypad keys and to perform the action of a key you press. Chrome window titles
and phone notifications are read only while its keys are shown on the keypad. Sound, processor and memory are also read
once a second while the Plugin runs, even when its keys are not shown: the Logi service does not tell a plugin which
page is shown, and a changed image is what wakes up a shown key whose picture had stopped changing (a muted key, for
instance).

- **Sound**: the volume level, the mute state and the current peak level of the default audio output (Windows audio
  API). Volume keys send the Windows volume keys; the mute key mutes or unmutes that output.
- **PC health**: the processor and memory load (Windows kernel counters).
- **Unread Gmail and Messages counts**: the **titles** of your Google Chrome windows and tabs (Windows UI Automation),
  to find Gmail, Google Calendar and Google Messages for web and read the unread number they show in parentheses
  (for example "Inbox (3)"). Only that number is used; titles are neither kept nor logged. If you use the optional
  [NexusBrowser](https://github.com/pironjulien/NexusBrowser) extension with its "Gmail unread for the pad" box ticked,
  the Gmail count it writes to `~/.nexus/nexus-browser/compteurs.json` is read too.
- **Phone notifications** (optional, only if an Android phone is reachable over adb, USB or wireless debugging): the
  **number** of pending notifications of the Gmail and Google Messages apps, read at most once a minute with
  `adb shell dumpsys notification`, filtered on the phone to those two apps. Only the two counts are used; notification
  texts are neither read by the Plugin, kept nor logged. Wireless debugging uses adb on your local network; the optional
  pairing note `~/.nexus/nexus-android/wifi.json` is read if present.
- **Store and Windows Update** (only when you press that key): the names of the buttons and the status texts of the
  Microsoft Store "Downloads and updates" page and of the Settings "Windows Update" page (Windows UI Automation), to
  click their own update buttons (see the [EULA](EULA-PC-DASHBOARD.md), section 3).
- **Screen and inactivity**: how long the PC has been idle and the Windows screen-off delay, so the keys stop being
  drawn when Windows turns the screen off.
- **Website key**: the web address (and optional window title) you type for that key in Logi Options+. Logi Options+
  stores it in your profile; the Plugin uses it only to open that address and find its window.
- **Addresses of Chrome app windows** (only when you press a Website key without a window title): the address of the
  page shown in each Google Chrome app window (Windows UI Automation), compared with the key's address to find the
  window that key opened, so a second press brings it back or closes it, and never another one. Addresses are read
  at the press, neither kept nor logged.

## What the Plugin writes, on your computer only

- The plugin log of the Logi Plugin Service (`…\LogiPluginService\Logs\plugin_logs\PcDashboard.log`): technical
  events, which can include the address of your website key. It stays on your computer.
- Nothing else: no file, no cache, no clipboard use.

## Network

The Plugin makes no network request itself, except to your phone through adb when a phone is connected. When you press
the Gmail, Calendar, Messages or Website key, that page opens in your browser (Google Chrome if installed, otherwise
your default browser), which then connects to that site as usual. **No data is sent to the Developer, to Logitech or to
any analytics service.**

## Your data, your control

All the data above stays under your control on your computer. To remove it: uninstall the Plugin from Logi Options+
(this also removes the website addresses from your profile) and delete its log file. Since the Developer receives no
data, there is nothing to request, correct or delete on the Developer's side.

## Children

The Plugin is not directed at children.

## Changes and contact

The current version of this policy is kept at
https://github.com/pironjulien/NexusKeypad/blob/main/PRIVACY-PC-DASHBOARD.md with its effective date.
Questions: [GitHub issues](https://github.com/pironjulien/NexusKeypad/issues).
