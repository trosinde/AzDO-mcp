# Proposal

## Why

Connections, PATs, permissions and approvals need a place a non-developer can manage without editing JSON. A tray app also gives a home to approval requests and PAT-expiry reminders.

## What Changes

- WPF app `AzdoMcp.Manager` (Windows 11 Fluent look) with five pages as in `docs/ui-proposal/`: Übersicht, Verbindungen & PATs, MCP-Clients, Tools & Rechte, Protokoll.
- Tray icon with status, quick actions, autostart and approval notifications ("Freigabe-Fenster").
- One-click registration of `azdo-mcp.exe` in Claude Code, Claude Desktop, OpenCode and VS Code/GitHub Copilot, with backup of the client config.
- PAT expiry reminders and v1 migration banner.

## Capabilities

### New Capabilities
- `manager-ui`: the five pages, their content, texts and status colours.
- `tray`: tray icon, menu, close-to-tray, autostart, notifications, single instance.
- `client-setup`: writing MCP client configurations.

### Modified Capabilities
- none

## Impact

- New project `src/AzdoMcp.Manager` (net10.0-windows, WPF, WinForms `NotifyIcon` for the tray).
- View models and client-config writers in `AzdoMcp.Manager.Core` (testable on Linux).
- Writes to `%APPDATA%\Claude\`, `%USERPROFILE%\.claude.json` (via `claude` CLI), `%USERPROFILE%\.config\opencode\`, `%APPDATA%\Code\User\`, and `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.
