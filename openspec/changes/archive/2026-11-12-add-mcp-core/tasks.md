# Tasks

## 1. Solution setup

- [x] 1.1 Create `AzdoMcp.sln`, `Directory.Build.props` (net10.0, nullable, warnings as errors), `src/AzdoMcp.Core`, `src/AzdoMcp.Server` (AssemblyName `azdo-mcp`), `tests/AzdoMcp.Tests` (xUnit v3); verify `dotnet build AzdoMcp.sln` succeeds
- [x] 1.2 Add `CLAUDE.md` with hard rules and commands, `.gitignore`, `README.md`; verify `dotnet run --project tests/AzdoMcp.Tests` runs (0 tests OK)

## 2. Connections and secrets

- [x] 2.1 Connection model, validation (unique short name, single default, first = default) and JSON store with atomic writes; verify tests `ConnectionStoreTests`
- [x] 2.2 `ISecretProtector`, DPAPI implementation and fake; PAT never in `connections.json`; verify test that scans the data root for the PAT string
- [x] 2.3 Expiry classification green/yellow/red; verify `ExpiryStatusTests` (4 days / 7 days lead → yellow "in 4 Tagen")
- [x] 2.4 v1 import (all-or-nothing, suffix on clash, delete v1 file); verify `LegacyImportTests` incl. failure keeps v1 file
- [x] 2.5 Connection test per area with project list, 15 s timeout; verify `ConnectionTesterTests` with fake HTTP handler (OK, 403 test scope, unreachable)

## 3. Azure DevOps client and tools

- [x] 3.1 REST client (Basic auth, API versions, 401/403/404 mapping to German messages, secrets scrubbed); verify `AzdoClientErrorTests`
- [x] 3.2 Tool catalogue with 22 tools, groups and write flags; verify `ToolCatalogTests` (22 tools, six groups)
- [x] 3.3 Work item read tools incl. WIQL limit note; verify tests with recorded fake responses
- [x] 3.4 Work item write tools incl. 60 MB attachment guard; verify tests
- [x] 3.5 Git, test, planning and system tools; verify tests (`list_projects` contains no PAT)

## 4. MCP server

- [x] 4.1 Stdio host with MCP SDK, `--stdio` argument handling, stderr-only diagnostics; verify `ServerProtocolTests` (initialize, tools/list over an in-memory stream)
- [x] 4.2 Connection selection (`project`, default, case-insensitive, unknown → list of names); verify tests
- [x] 4.3 Config reload (mtime check before each call, see design); verify test that adds a connection between two calls

## 5. Audit log

- [x] 5.1 JSONL writer with mutex, client name from `clientInfo`, no content fields; verify `AuditLogTests` (comment text absent, parallel writers)
- [x] 5.2 30-day retention and CSV export; verify tests

## 6. Integration

- [x] 6.1 Manual smoke test: start `azdo-mcp.exe --stdio`, send initialize + tools/list from a script; verify 22 tools listed
