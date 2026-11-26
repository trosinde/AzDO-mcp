# Tasks

## 1. Permission model

- [x] 1.1 Modes, defaults, permissions store and `Evaluate` (inheritance, per-connection blocks, write switch, scope gaps, most restrictive wins); verify `PermissionEvaluatorTests` covering every spec scenario
- [x] 1.2 Filter `tools/list` and annotate partially blocked tools; send `list_changed` on difference; verify `ToolListingTests`
- [x] 1.3 Reject blocked calls with "Tool gesperrt" / "Für X gesperrt" / "403 Scope fehlt" and audit result "gesperrt"; verify tests

## 2. IPC

- [x] 2.1 Create `src/AzdoMcp.Manager.Core`; message types and length-prefixed JSON framing (in Core/Ipc, shared with the server); verify `PipeFramingTests`
- [x] 2.2 Windows pipe server (SID name, `CurrentUserOnly`) and client with retry, behind an interface plus in-memory transport; verify round-trip test on in-memory transport
- [x] 2.3 Registration and heartbeat, active-client list; verify `InstanceRegistryTests`

## 3. Approval flow

- [x] 3.1 Approval request with change summary, wait with 2-minute timeout, "wartet auf Freigabe" record; verify `ApprovalFlowTests` (approve, deny, timeout with a short configured timeout)
- [x] 3.2 Manager unreachable → immediate denial message; verify test
- [x] 3.3 Pause / resume / reload commands; verify test that a paused server rejects calls
