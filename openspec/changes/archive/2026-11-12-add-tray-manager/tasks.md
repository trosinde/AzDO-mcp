# Tasks

## 1. Project and shell

- [x] 1.1 Create `src/AzdoMcp.Manager` (net10.0-windows, WPF, UseWindowsForms), add to solution; verify `dotnet build` on Windows
- [x] 1.2 Main window with navigation, footer texts, status colour resources; verify manual click through five entries
- [x] 1.3 Add `docs/ui-proposal/README.md` with artboard → view mapping; verify every artboard is mapped

## 2. View models (Manager.Core)

- [x] 2.1 Overview figures from audit log and connections; verify `OverviewViewModelTests` (12 calls, 3 writing)
- [x] 2.2 Connection editor (validation, PAT hidden, unsaved-changes prompt, token URL); verify tests
- [x] 2.3 Tools & Rechte grid model incl. chips and save; verify tests
- [x] 2.4 Protokoll list, filters, export, pending approvals; verify tests

## 3. Pages

- [x] 3.1 Übersicht incl. migration banner; verify manually against Main artboard
- [x] 3.2 Verbindungen & PATs incl. connection test panel; verify manually against Verbindung artboard
- [x] 3.3 Tools & Rechte incl. footnotes; verify manually against Tools artboard
- [x] 3.4 Protokoll incl. "Freigabe nötig" panel and live update; verify manually against Protokoll artboard
- [x] 3.5 MCP-Clients; verify manually against Clients artboard

## 4. Tray

- [ ] 4.1 NotifyIcon with status dot, menu as in Tray artboard, close-to-tray, single instance; verify manually
- [ ] 4.2 Autostart in HKCU Run; verify `AutostartTests` with fake registry and manual toggle
- [x] 4.3 Approval window (Erlauben/Ablehnen/Details, closes on timeout); verify manually with a test request
- [x] 4.4 Daily PAT reminder balloon; verify `ReminderSchedulerTests`

## 5. Client setup

- [x] 5.1 Writers for Claude Desktop, OpenCode (comments kept), VS Code user/workspace, backup and parse-error handling; verify `ClientConfigWriterTests`
- [ ] 5.2 Claude Code command copy/run with CLI detection; verify test for command text and manual run

## Workflow follow-up

- Archived on 2026-11-12 with 4.1, 4.2 and 5.2 still needing the owner's manual check
  (tray menu click, autostart toggle on the installed app, "Ausführen" for Claude Code).
