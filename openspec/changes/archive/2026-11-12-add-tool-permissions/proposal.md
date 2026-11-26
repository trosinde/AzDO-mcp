# Proposal

## Why

v1 lets any AI client run every tool, including writes, with the user's full PAT rights. Users need to decide what the AI may do: read freely, ask before changing work items, or not see a tool at all – per tool group, with exceptions per connection.

## What Changes

- Three modes per tool group and per tool: "Erlaubt", "Nachfragen", "Gesperrt".
- Per-connection exceptions, including the connection-level switch "Schreibzugriff zulassen" and scope gaps found by the connection test.
- Blocked tools are not offered to the AI (`tools/list` filtered).
- "Nachfragen" triggers an approval request to the Manager over a user-only Named Pipe; no answer within 2 minutes = denied.
- Server instances register with the Manager so it can show active clients and pause/reload them.

## Capabilities

### New Capabilities
- `tool-permissions`: modes, defaults, overrides, evaluation order, effect on tool listing and calls.
- `approval-flow`: request/answer protocol between server and Manager, timeout, behaviour when the Manager is not running, pause/reload.

### Modified Capabilities
- none (builds on `mcp-server`, `azdo-tools`, `audit-log` from `add-mcp-core`, which is not archived yet)

## Impact

- Core: permission model and store `permissions.json`, pipe protocol types.
- Server: filters `tools/list`, gates `tools/call`, pipe client.
- Manager.Core (new project `src/AzdoMcp.Manager.Core`, net10.0): pipe server and approval queue, usable from WPF.
- No new NuGet packages.
