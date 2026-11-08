# Development instructions

## Pitfalls

- Frame rate cost lives in the Logi plugin service (each image sent is processed there), not in our rendering: images
  already have the pad size (116 px) and an image whose pixels did not change is never encoded or sent. Above 12 frames
  per second nothing more is visible on these small pixel arts, while the service saturates near 65 images/s.
  `outils\pad.ps1 cadence [1-60]` changes the base rate live and counts the images sent.
- Logi Options+ started from an AI desktop app (a packaged MSIX app such as Claude) stays bound to that app's job and
  dies when the app restarts, even with `CREATE_BREAKAWAY_FROM_JOB`: the pad goes black. Always start it through WMI
  (`outils\relancer_logi.ps1`, `Start-Logi` in get.ps1, the hook's `Lancer`), so its parent is WmiPrvSE, never the app.
- Never close an AI app with `taskkill /T`: the process tree can include the Logi agent restarted from it. After any
  forced restart of an AI app, check `LogiPluginService` and the end of `NexusKeypad.log`.
- The pad sleeps on the `GoToSleep` order the Options+ agent sends when the screen turns off; a service restarted
  after that order misses it and the pad stays on. The plugin stops drawing while the screen is off (`Moteur.cs`), so
  the image freezes. Keep the pad awake through Logi or the plugin, never through Windows power settings or DDC/CI.
- When moving or rewriting a Claude plugin (status line, connector), never bring back a Nexus Relay line under the
  prompt (`ui.render` SessionMode, `$.ui.status`): it was dropped in 1.8.25 and came back with three migrations.
