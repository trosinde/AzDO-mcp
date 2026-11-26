# Design

## Context

Greenfield .NET rewrite of the v1 TypeScript server (see proposal.md – Why). Constraints: Windows only at runtime, no admin rights, everything under the user profile. Core logic must stay testable on Linux (CI/WSL), so Windows-only APIs sit behind interfaces.

## Goals / Non-Goals

**Goals:**
- One self-contained `azdo-mcp.exe`; no Node.js, no global .NET runtime required.
- Core library shared by Server and Manager (same connection store, same tool catalogue).
- Unit tests without a real Azure DevOps (HTTP handler fakes).

**Non-Goals:**
- Permissions and approvals (→ `add-tool-permissions`), UI (→ `add-tray-manager`), installer (→ `add-windows-packaging`).
- HTTP/SSE transport; only stdio.
- Entra ID / OAuth login; PAT only.

## Decisions

**Solution layout.** `src/AzdoMcp.Core` (net10.0), `src/AzdoMcp.Server` (net10.0, AssemblyName `azdo-mcp`), `tests/AzdoMcp.Tests` (xUnit v3 – runs as an executable, so `dotnet run --project tests/AzdoMcp.Tests` works). Alternative: one project – rejected, the Manager needs Core without the MCP host.

**MCP SDK.** Official `ModelContextProtocol` C# SDK used directly (`McpServer.Create` + `StdioServerTransport`, low-level `ListTools`/`CallTool` handlers) – no generic host, so no extra hosting package. Alternative: hand-written JSON-RPC – rejected, protocol drift risk. Tools are registered from a Core `ToolCatalog` (name, group, isWrite, schema, handler) rather than attributes, so permissions can filter the list later.

**Storage layout** under `%LOCALAPPDATA%\AzdoMcpData\`:
- `connections.json` – connection metadata, no secrets.
- `secrets\<connection-id>.bin` – DPAPI blob (CurrentUser, with an app-specific entropy).
- `logs\audit-YYYYMMDD.jsonl` – one JSON object per line.
The data root is deliberately *not* `%LOCALAPPDATA%\AzdoMcp\`: that is the Velopack install root, which is deleted on uninstall (see `add-windows-packaging` – user data must survive). Writes go through write-to-temp + atomic `File.Move(overwrite)` under a named mutex so server and Manager never see half files. The data root is injectable (tests use a temp dir).

**Secret protection.** `ISecretProtector` with `DpapiSecretProtector` (Windows, `System.Security.Cryptography.ProtectedData`) and a test fake. On non-Windows the real protector throws – the product does not run there.

**REST client.** `HttpClient` with Basic auth (`:PAT`), API version `6.0` for on-prem compatibility (Azure DevOps Server 2020 = 6.0) and `7.0`/preview only where v1 needed it, with a 404/400-version fallback to the "nicht verfügbar" error. Timeout 120 s per call, 15 s for the connection test. Services vs. Server differ only in base URL handling.

**Reload.** A cheap mtime/size check of `connections.json` before every call; no `FileSystemWatcher` (unreliable on redirected profiles). `add-tool-permissions` adds a watcher only to emit `list_changed`.

**Audit log concurrency.** Append with `FileShare.ReadWrite` and a named mutex `Local\AzdoMcp-audit` around each line write.

**v1 import.** Reads the v1 schema (`projects.<name>.baseUrl`, `pat`), splits `baseUrl` into collection URL + project, stores all entries in one transaction (write new store to temp, replace), then deletes the v1 file. Detection paths: `%USERPROFILE%\.azdo-mcp\config.json`, `%USERPROFILE%\.local\share\mcp-azure-devops\config.json` (v1 install.sh default) and a user-chosen file.

## Risks / Trade-offs

- [DPAPI blob lost when Windows profile is reset] → user must re-enter PATs; status shows "PAT nicht lesbar – bitte ersetzen".
- [API 6.0 lacks some newer fields] → acceptable; v1 used the same versions.
- [Self-contained exe ~70 MB] → accepted for "no runtime needed"; trimming is not used (reflection in JSON/MCP).

## Migration Plan

v1 keeps working until the user imports. Import deletes the v1 plain-text file; MCP clients are re-pointed via the Manager's client page.

## Open Questions

- Exact v1 install locations on users' machines (more detection paths can be added without spec change).
