# AzDO MCP Manager v2

MCP server for Azure DevOps (Server on-prem and Services) in .NET 10 plus a WPF tray app
that manages connections/PATs, tool permissions and approvals. Windows only.

## Hard rules

- No admin rights, no UAC. Never write outside the user profile
  (`%LOCALAPPDATA%`, `%APPDATA%`, `%USERPROFILE%`, `HKCU`).
- User data lives in `%LOCALAPPDATA%\AzdoMcpData\` – never in the program folder
  (`%LOCALAPPDATA%\AzdoMcp\` is the installer root and is deleted on uninstall).
- PATs only DPAPI-encrypted (CurrentUser). Never in plain text on disk, in logs, in tool
  results, in client configs, in tests or in commits. Error texts go through `SecretScrubber`.
- No real PATs, server URLs, names or customer data in code, tests, docs or commits.
  Use placeholders: `https://azdo.example.local/DefaultCollection`, "Max Muster".
- No new NuGet packages in `AzdoMcp.Core`, `AzdoMcp.Server`, `AzdoMcp.Manager.Core`
  without asking the owner. Versions are pinned centrally in `Directory.Packages.props`.
- stdout of `azdo-mcp.exe` belongs to the MCP transport – diagnostics only to stderr.
- Audit log: metadata only (no field values, comment texts, attachment contents).
- Discovery never writes to Azure DevOps; the only non-read request is the `validateOnly` work item probe.
- A connection is one collection + one PAT; tools address projects by alias. Discovery and rescan never
  overwrite existing project settings.
- If a rule here conflicts with a spec under `openspec/`: stop and ask.

## Layout

| Path | What |
|---|---|
| `src/AzdoMcp.Core` | connections, DPAPI secrets, REST client, 31 tools, audit log (net10.0) |
| `src/AzdoMcp.Server` | `azdo-mcp.exe --stdio` and `discover` CLI, MCP SDK glue (net10.0) |
| `src/AzdoMcp.Manager.Core` | view models, wizard, pipe host, client setup (net10.0, testable) |
| `src/AzdoMcp.Manager` | WPF tray app, views `*View.xaml` (net10.0-windows) |
| `tests/AzdoMcp.Tests` | xUnit v3, runs as an executable, fakes for HTTP and DPAPI |
| `openspec/` | specs and changes (spec-driven); read before changing behaviour |
| `docs/ui-proposal/` | UI artboards (German texts are authoritative) |

## Commands

```powershell
dotnet build AzdoMcp.sln
dotnet run --project tests/AzdoMcp.Tests          # all tests
openspec validate --all --strict
```

From WSL use the Windows SDK: `/mnt/c/Users/<user>/AppData/Local/Microsoft/dotnet/dotnet.exe`.
Set `AZDOMCP_DATA` to a temp folder for manual server runs so the real profile stays untouched.

## Conventions

- C# latest, nullable on, warnings are errors.
- User-facing strings in German (as in `docs/ui-proposal`), code and commits in English.
- Small thematic commits, English messages.
