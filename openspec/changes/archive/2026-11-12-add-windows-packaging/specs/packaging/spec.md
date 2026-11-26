# Spec Delta

## Purpose

Defines how AzDO MCP Manager and azdo-mcp.exe are built, installed per user without admin rights, updated and removed.

## ADDED Requirements

### Requirement: Per-user installer without UAC
`AzdoMcp-win-Setup.exe` SHALL install for the current user only into `%LOCALAPPDATA%\AzdoMcp\`, SHALL NOT trigger a UAC prompt, SHALL create a Start-menu shortcut "AzDO MCP Manager" and SHALL start the Manager after installation.

#### Scenario: Install as standard user
- **WHEN** a standard user without admin rights runs the setup
- **THEN** installation completes without UAC prompt and the Manager is running in the tray

### Requirement: Stable executable path
After installation and after every update the server SHALL be reachable at `%LOCALAPPDATA%\AzdoMcp\current\azdo-mcp.exe`, so MCP client configs never need changing.

#### Scenario: Path survives update
- **WHEN** version 2.0.1 is applied over 2.0.0
- **THEN** `%LOCALAPPDATA%\AzdoMcp\current\azdo-mcp.exe` exists and reports version 2.0.1

### Requirement: Self-contained
Both executables SHALL be self-contained win-x64 builds that need no installed .NET runtime.

#### Scenario: Clean machine
- **WHEN** the setup runs on Windows without any .NET runtime
- **THEN** Manager and server start

### Requirement: Portable package
`AzdoMcp-<Version>-portable.zip` SHALL contain the Manager and `azdo-mcp.exe` and SHALL run after unpacking to any user-writable folder without installation. In portable mode the Manager SHALL show the real path on the MCP-Clients page, SHALL NOT offer updates and SHALL show "Portable" in the footer.

#### Scenario: Run from Downloads
- **WHEN** the zip is unpacked to `%USERPROFILE%\Downloads\AzdoMcp` and the Manager is started
- **THEN** it runs and the MCP-Clients page shows that folder's `azdo-mcp.exe`

### Requirement: User data separate from program
Connections, secrets, permissions and logs SHALL live outside the program folder (see `connections`), so updates and reinstalls keep them. Uninstall SHALL keep them unless the user deletes them explicitly.

#### Scenario: Reinstall keeps connections
- **WHEN** the user uninstalls and installs again
- **THEN** existing connections are still present

### Requirement: Updates
The installed Manager SHALL check for updates at start and on request, show the status in the footer ("Updates: aktuell", "Update verfügbar", "Updates: nicht konfiguriert"), and apply an update only after the user confirms. Running server instances SHALL be asked to exit gracefully before the swap.

#### Scenario: Update available
- **WHEN** a newer release is found
- **THEN** the footer shows "Update verfügbar" and clicking it offers "Jetzt aktualisieren"

### Requirement: Build script
`build\build.ps1 -Version <x.y.z>` SHALL restore, build, run the tests, publish both executables and produce `artifacts\releases\AzdoMcp-win-Setup.exe` and `artifacts\AzdoMcp-<x.y.z>-portable.zip`, failing with a non-zero exit code if any step fails.

#### Scenario: Failing test stops build
- **WHEN** a unit test fails
- **THEN** the script stops before packaging and exits non-zero
