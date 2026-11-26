# Tasks

## 1. Velopack integration

- [x] 1.1 Add pinned `Velopack` to Manager and `vpk` to the local tool manifest `dotnet-tools.json`; verify `dotnet tool restore` and build succeed
- [x] 1.2 `Program.cs` bootstrap with install/uninstall/update hooks; `Updater.cs` with feed config, portable detection and footer status; verify `UpdaterStatusTests` (Manager.Core part) and manual start

## 2. Build script

- [x] 2.1 `build/build.ps1 -Version` (restore, build, test, publish both exes self-contained win-x64, `vpk pack`, portable zip); verify `.\build\build.ps1 -Version 2.0.0` produces both artefacts and fails on a broken test
- [x] 2.2 Document install, portable use, SmartScreen and AppLocker notes in `README.md`; verify steps match the script output

## 3. Verification

- [x] 3.1 Install `AzdoMcp-win-Setup.exe` as standard user; verify no UAC, Manager in tray, `%LOCALAPPDATA%\AzdoMcp\current\azdo-mcp.exe` exists
- [x] 3.2 Unpack portable zip to a user folder and start; verify Manager runs and shows "Portable"
- [x] 3.3 Uninstall and reinstall; verify connections are kept
