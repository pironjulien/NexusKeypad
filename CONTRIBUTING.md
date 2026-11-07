# Contributing

## Environment

- Windows 11, the .NET SDK pinned by `global.json`, PowerShell 7;
- Node.js on the PATH for the JavaScript tests (no npm dependency);
- Logi Options+ and its plugin service are optional: without them (as in the CI), the build uses the
  [LogiPluginApi](https://www.nuget.org/packages/LogiPluginApi) package.

## Rules

1. Open an issue or a pull request against `main`. A change is accepted only when `test.ps1` passes: the CI
   (`.github/workflows/tests.yml`) runs that same script on every push and pull request.
2. **Every new feature and every bug fix comes with its test**: C# changes in `tests/` (xUnit v3: `Logique` for the
   rules and every key drawn, `Connecteurs`, `Isole` for the launchers, `Hook`), the pad's JavaScript in
   `tests/*.cjs` (`node --test`). A fix starts with a test that fails without it.
3. The five plugin editions must compile, and the PC Dashboard edition holds nothing of the AI agents: no name, string
   or function that reads or drives their apps (`test.ps1` checks the built DLL).
4. Tests never touch the installed plugin: no link set, no reload. Checks on the real PC (clipboard, live apps) are
   explicit tests, run only on request (`dotnet test --project tests/Logique -- --explicit only`).
5. Never commit a secret, a token, a log or a build output: builds go to `~\.nexus\build\nexuskeypad`, outside the
   repository. Comments, commit messages and `CHANGELOG.md` entries (Keep a Changelog, under `[Unreleased]`) are
   written in English.

## Checks

```powershell
pwsh -File test.ps1    # the plugin editions, get.ps1 in a test folder, dotnet test, node --test; offline after restore
git diff --check
```

## Version

One version everywhere: `plugin/src/TrinitePadPlugin.csproj` sets it; the four `LoupedeckPackage.yaml`, the connector
manifests and `Connecteurs.Version` follow, and `CHANGELOG.md` has its `[X.Y.Z]` section. The `Release` workflow
(`.github/workflows/release.yml`) checks them, runs `test.ps1`, builds, attests and publishes a development build on
every push to `main`.
