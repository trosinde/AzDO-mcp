# Design

## Context

Builds on Core (connections, audit log) and Manager.Core (pipe server, approvals). The WPF project can only be built and run on Windows; logic must sit in Manager.Core so it is tested on Linux.

## Goals / Non-Goals

**Goals:** match docs/ui-proposal (layout, texts, colours, footnotes); keep views thin (MVVM).

**Non-Goals:** dark theme, localisation beyond German, client support beyond the four listed.

## Decisions

**Framework.** WPF on net10.0-windows with the built-in Fluent theme (`ThemeMode="Light"`, .NET 9+; WPF0001 suppressed) instead of a third-party UI library – no new packages. Tray via `System.Windows.Forms.NotifyIcon` (`UseWindowsForms`), context menu as a WPF popup styled like the artboard. MVVM without a toolkit: view models live in Manager.Core with a small own `ObservableObject` / `RelayCommand` / `AsyncCommand` (no package at all). UI thread marshalling via `IUiDispatcher`, dialogs via `IDialogs`, registry via `IRunKey` – all faked in tests.

**Artboard → view mapping** (kept in `docs/ui-proposal/README.md`): Main → `Views/OverviewPage.xaml`, Verbindung → `ConnectionsPage.xaml`, Clients → `ClientsPage.xaml`, Tools → `ToolsPage.xaml`, Protokoll → `LogPage.xaml`, Tray → `Tray/TrayMenuWindow.xaml` + `Tray/ApprovalWindow.xaml`.

**Status colours** as shared resource dictionary `Themes/Theme.xaml` with the hex values from the spec.

**Approval window.** Own topmost borderless WPF window bottom-right above the taskbar instead of Windows toast notifications – toasts with buttons need an AUMID/COM activator registration that unpackaged apps without installer hooks handle poorly. Alternative kept for later.

**Single instance.** Named mutex `Local\AzdoMcp-Manager` plus a pipe message `show` to the running instance (reuses the approval pipe).

**Layout at 1120 px.** Where the artboard is too tight for real WPF text metrics, the view keeps content and texts but rearranges (see `docs/ui-proposal/README.md`, „Bewusste Abweichungen“).

**Data root for approvals UI.** The Manager uses the same `%LOCALAPPDATA%\AzdoMcpData\` as the server; `AZDOMCP_DATA` / `AZDOMCP_PIPE` override both for demos and tests.

**Client config writers.** In Manager.Core; JSONC handled with `System.Text.Json` (`JsonCommentHandling.Skip`) for reading and a text-level insertion for OpenCode to keep comments.

## Risks / Trade-offs

- [Fluent theme in WPF is still marked experimental] → fallback to classic styles via own resource dictionary if it breaks.
- [Claude Code config format changes] → use the `claude mcp add` CLI instead of writing its file.
