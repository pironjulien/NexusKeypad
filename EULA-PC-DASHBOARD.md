# End User License Agreement

**PC Dashboard** (plugin `PcDashboard`, project "NexusKeypad") · Effective October 25, 2026

This End User License Agreement ("EULA") is between you and **Julien Piron** ("the Developer"), the author and licensor
of the PC Dashboard plugin for the Logi Plugin Service ("the Plugin"), whether you obtain it from the Logitech
Marketplace, from [GitHub](https://github.com/pironjulien/NexusKeypad) or elsewhere. By installing or using
the Plugin you accept this EULA. If you do not accept it, do not install or use the Plugin.

## 1. Licensor and Logitech

The Developer, not Logitech, licenses the Plugin to you. **Logitech Europe S.A. and its affiliates are not a party to
this EULA** and have no responsibility or liability to you in connection with the Plugin, its content, its support or
your use of it. Any claim about the Plugin is to be made to the Developer only.

## 2. License

The Plugin is open source software, licensed under the [MIT License](https://github.com/pironjulien/NexusKeypad/blob/main/LICENSE),
reproduced below. You may use, copy, modify and redistribute it under the terms of that license. Nothing in this EULA
restricts the rights the MIT License gives you.

> Copyright (c) 2026 Julien Piron
>
> Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated
> documentation files (the "Software"), to deal in the Software without restriction, including without limitation the
> rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit
> persons to whom the Software is furnished to do so, subject to the following conditions:
>
> The above copyright notice and this permission notice shall be included in all copies or substantial portions of the
> Software.
>
> THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE
> WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
> COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
> OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

## 3. What the Plugin does on your computer

The Plugin runs inside the Logi Plugin Service on your Windows PC. It shows the sound level, unread Gmail and Messages
counts, the date and time, the processor and memory load, and does only what the key you press says:

- **Volume down / up, Mute**: the Windows volume keys, and mute or unmute of the default audio output.
- **Gmail, Calendar, Messages, Website**: opens that page in front (its window if already open, otherwise a Google
  Chrome app window, otherwise your default browser); a second press while it is in front closes that window.
- **PC health**: opens Windows Task Manager; a second press while it is in front closes it.
- **Store and Windows Update**: one press updates your PC the way you would by hand. The Plugin opens the Microsoft
  Store "Downloads and updates" page and the Settings "Windows Update" page behind your other windows, clicks their own
  **check for updates** and **update all / download and install all** buttons when they appear, then minimizes them; the
  key animates until both are done and shows the result. It uses no administrator rights, changes no setting, and
  **never clicks restart**: Windows may still restart on its own schedule, according to your Windows Update settings.
  If Windows has suspended the Store app, the Plugin resumes it through the documented Windows interface used by
  debuggers (`IPackageDebugSettings::Resume`), without any persistent change. The buttons are recognized in English
  and French Windows; in another language this key does nothing.

The Plugin **never sends a message, makes a purchase, installs software of its own or submits anything on your
behalf**. What it reads and writes is described in the
[Privacy Policy](https://github.com/pironjulien/NexusKeypad/blob/main/PRIVACY-PC-DASHBOARD.md).

## 4. Third-party products and trademarks

The Plugin works with third-party products that you obtain and use under their own terms. The Developer does not
provide, control or support them, and is not responsible for their availability, their changes or their content.
Gmail, Google Calendar, Google Messages and Google Chrome are trademarks of Google LLC; Microsoft Store, Windows and
Windows Update of Microsoft Corporation; Android of Google LLC; Logitech, Logi and MX of Logitech. The Plugin is an
independent project, **not affiliated with, endorsed or sponsored by** any of them; their names and the pixel-art
pictures drawn by the Plugin only identify the product each key refers to.

## 5. No warranty

The Plugin is provided free of charge, "as is" and "as available", without warranty of any kind, as stated in the MIT
License above. The third-party products it works with change often; a new version may stop a feature from working.

## 6. Limitation of liability

To the fullest extent permitted by applicable law, the Developer is not liable for any indirect, incidental, special
or consequential damages, or for any loss of data, profits or business, arising from the use of or inability to use
the Plugin, including updates installed by Windows or the Microsoft Store after a press of the update key. Nothing in
this EULA limits liability that cannot be limited under applicable law, including your statutory rights as a consumer.

## 7. Updates and support

Updates are published on the Logitech Marketplace and on GitHub. Support and bug reports:
[GitHub issues](https://github.com/pironjulien/NexusKeypad/issues). Security issues: see the
[security policy](https://github.com/pironjulien/NexusKeypad/blob/main/SECURITY.md). Support is offered on a
best-effort basis, without any guaranteed response time.

## 8. Termination

This EULA ends when you uninstall the Plugin and delete its files. Your rights under the MIT License are not affected.

## 9. Governing law

This EULA is governed by French law, without prejudice to the mandatory consumer protection rules of the country where
you live.

## 10. Changes

The Developer may update this EULA; the current version is kept at
https://github.com/pironjulien/NexusKeypad/blob/main/EULA-PC-DASHBOARD.md with its effective date.
