# Design

## Context

MCP clients spawn `azdo-mcp.exe` themselves over stdio, often several instances at once (one per client/session). The Manager is a separate long-running tray process. Approvals therefore need IPC from many short-lived servers to one UI process.

## Goals / Non-Goals

**Goals:** deterministic permission evaluation in Core (unit-testable); IPC that works without admin and without network ports.

**Non-Goals:** the toast/dialog UI itself (→ `add-tray-manager`); remembering approvals ("immer erlauben") – not in v2.0.

## Decisions

**Permission store.** `%LOCALAPPDATA%\AzdoMcpData\permissions.json`: `{ groups: {name: mode}, tools: {name: mode}, connectionBlocks: {short: [group]} }`. Write switch and scope gaps live on the connection (from `add-mcp-core`). Evaluation is a pure function `Evaluate(tool, connection, permissions) → mode + reason`.

**Pipe.** Name `AzdoMcp-<SID>`, created by the Manager with `PipeOptions.CurrentUserOnly` (on Windows .NET then sets an ACL that only admits the creating user; no separate ACL code). `PipeOptions.CurrentUserOnly` on the client as well, so a pipe squatted by another user is refused. Messages are length-prefixed UTF-8 JSON (`register`, `heartbeat`, `approve-request`, `approve-response`, `pause`, `resume`, `reload`). Alternative: local HTTP – rejected (port, firewall prompts, other users on terminal servers).

**Where the code lives.** Message types, framing and the server-side channel (pipe client) are in `AzdoMcp.Core/Ipc` because `azdo-mcp.exe` must not depend on Manager.Core; the pipe host, instance registry and approval queue are in `AzdoMcp.Manager.Core`. Transports sit behind `IPipeClientTransport` / `IPipeServerTransport`; tests use an in-memory pair.

**Who is server.** The Manager hosts the pipe server; each MCP server instance is a client that connects lazily and retries every 5 s in the background. Requests carry a GUID; the server waits on a `TaskCompletionSource` with a 2-minute timeout (configurable, so tests use milliseconds instead of a fake clock; no extra package).

**Change summary.** Built in Core from the tool arguments (field names → German labels for common fields, else reference names). Field values appear only in the approval request, never in the audit log.

**list_changed.** After a reload the server compares the filtered tool set and sends `notifications/tools/list_changed` when it differs.

## Risks / Trade-offs

- [Clients that ignore `list_changed`] → blocked tools are still rejected at call time.
- [Pipe ACL APIs are Windows-only] → pipe code lives behind an interface; Linux tests use an in-memory transport.
- [User away → AI waits 2 minutes] → accepted; matches the UI footnote.
