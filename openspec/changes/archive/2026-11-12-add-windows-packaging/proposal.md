# Proposal

## Why

Users without admin rights must be able to install, update and remove the tool themselves, and MCP client configs must keep working across updates. Some machines forbid installers, so a portable variant is needed too.

## What Changes

- Velopack-based per-user installer `AzdoMcp-win-Setup.exe` (no UAC) installing to `%LOCALAPPDATA%\AzdoMcp\` with the stable path `current\azdo-mcp.exe`.
- Portable zip `AzdoMcp-<Version>-portable.zip` that runs without installation.
- In-app update check and apply, shown in the navigation footer ("Updates: aktuell").
- Build script `build\build.ps1 -Version <x.y.z>` producing both artefacts under `artifacts\`.
- Uninstall via "Apps & Features" (per user) that keeps the user's data unless they choose otherwise.

## Capabilities

### New Capabilities
- `packaging`: installer, portable package, stable paths, updates, uninstall, build script.

### Modified Capabilities
- none

## Impact

- `build/build.ps1`, Velopack package (`Velopack` NuGet in Manager only) and `vpk` dotnet tool (local tool manifest).
- `Program.cs` of Manager gets the Velopack bootstrap; `Updater.cs` wraps update checks.
- Release artefacts are not committed (`artifacts/` ignored).
