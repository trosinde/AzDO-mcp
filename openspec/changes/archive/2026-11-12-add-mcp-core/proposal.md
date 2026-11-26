# Proposal

## Why

The current Azure DevOps MCP server (TypeScript, v1) keeps PATs in plain text in `config.json`, has no notion of read vs. write permissions and needs Node.js on every machine. v2 rebuilds the server core in .NET 10 as a single self-contained `azdo-mcp.exe` that MCP clients start over stdio, with PATs protected by Windows DPAPI.

## What Changes

- New .NET 10 solution `AzdoMcp.sln` with `AzdoMcp.Core` (connections, secret storage, Azure DevOps REST client, tool implementations, audit log), `AzdoMcp.Server` (`azdo-mcp.exe --stdio`) and `tests/AzdoMcp.Tests`.
- MCP server over stdio exposing the 22 tools of v1 with the same names and a `project` parameter that selects a connection by short name.
- Connections stored per user under `%LOCALAPPDATA%\AzdoMcpData\`; PATs encrypted with DPAPI (CurrentUser), never shown again after saving.
- Support for Azure DevOps Services (`dev.azure.com`) and Azure DevOps Server (on-prem collections).
- Import of the v1 `config.json` (plain-text PATs) into encrypted storage, deleting the plain-text file afterwards.
- Audit log of every tool call (metadata only, no PATs, no field contents).
- **BREAKING** vs. v1: configuration format and location change; v1 `config.json` is imported, not read at runtime.

## Capabilities

### New Capabilities
- `mcp-server`: stdio MCP protocol handling, tool listing, connection selection, error reporting.
- `connections`: connection definitions, default connection, DPAPI-protected PAT storage, connection test, v1 import.
- `azdo-tools`: behaviour of the Azure DevOps tools (work items, Git, test, planning, system).
- `audit-log`: per-call protocol records, retention and export.

### Modified Capabilities
- none

## Impact

- New code: `src/AzdoMcp.Core`, `src/AzdoMcp.Server`, `tests/AzdoMcp.Tests`.
- Dependencies (to be confirmed by owner): `ModelContextProtocol` (official C# MCP SDK) in Server, `System.Security.Cryptography.ProtectedData` in Core, xUnit v3 in tests.
- v1 TypeScript server is replaced; MCP clients must be re-pointed to `azdo-mcp.exe` (see `add-tray-manager`).
