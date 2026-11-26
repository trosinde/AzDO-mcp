# Design

## Context

Corporate Windows machines: no admin, possibly AppLocker/WDAC rules for `%LOCALAPPDATA%`. Two executables (Manager GUI, stdio server) must ship together.

## Goals / Non-Goals

**Goals:** per-user install, stable server path, portable fallback, one build command.

**Non-Goals:** MSI/MSIX, code signing (no certificate yet), machine-wide install, ARM64.

## Decisions

**Velopack** 1.2.161 (current stable; pinned in `Directory.Packages.props` and `dotnet-tools.json`, replacing the earlier 0.0.1298 pin). It installs per user into `%LOCALAPPDATA%\<PackId>\current\` and keeps that path across updates – exactly the stable-path requirement. Alternatives: Squirrel.Windows (unmaintained), MSIX (needs signing, sandboxed paths), Inno Setup per-user (no delta updates).

**Pack layout.** PackId `AzdoMcp`, main exe `AzdoMcp.Manager.exe`; `azdo-mcp.exe` published into the same folder. Both self-contained win-x64, single-folder (not single-file, faster start).

**Bootstrap.** `VelopackApp.Build()` first in `Program.Main` of the Manager with hooks: after install → set autostart; before uninstall → remove HKCU Run entry. Server instances are asked to exit by the running Manager before it applies an update (see below), not in a Velopack hook.

**Update source.** Configurable feed (`update.json` in the data root: URL of a Velopack release folder or GitHub repo). Default: none → "Updates: nicht konfiguriert". Avoids hard-coding an internal server URL in the repo.

**Portable detection.** Velopack's `UpdateManager.IsInstalled == false` (or `IsPortable`) → portable mode. The portable zip is the plain publish folder (no `Update.exe`), not Velopack's own portable package, so it never self-updates.

**Graceful server exit.** New pipe message `exit`: before applying an update the Manager asks all `azdo-mcp.exe` instances to stop (5 s) and aborts with a message if some keep running, because Velopack cannot replace a locked exe.

**build.ps1.** PowerShell 5.1-compatible, uses `dotnet` from PATH or `%LOCALAPPDATA%\Microsoft\dotnet`, `dotnet tool restore` for `vpk`, writes only under `artifacts\`.

## Risks / Trade-offs

- [AppLocker blocks executables under %LOCALAPPDATA%] → portable zip into an allowed folder; clarify with IT (open).
- [Unsigned exe triggers SmartScreen] → document "Weitere Informationen → Trotzdem ausführen"; signing later.
- [Velopack API changes between versions] → pin version, wrap calls in `Updater.cs`.

## Open Questions

- AppLocker/WDAC policy on target machines (affects only deployment advice, not the build).
- Final update feed location.
