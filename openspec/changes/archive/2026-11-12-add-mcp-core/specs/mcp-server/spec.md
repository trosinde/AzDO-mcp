# Spec Delta

## Purpose

Lets MCP clients (Claude Code, Claude Desktop, OpenCode, VS Code/Copilot) talk to Azure DevOps through one local executable started over stdio.

## ADDED Requirements

### Requirement: Stdio transport
The server SHALL be started as `azdo-mcp.exe --stdio` and SHALL speak the MCP protocol (JSON-RPC 2.0) on stdin/stdout. Nothing other than protocol messages SHALL be written to stdout; diagnostics go to stderr or the log folder.

#### Scenario: Initialize handshake
- **WHEN** a client sends `initialize` followed by `notifications/initialized`
- **THEN** the server answers with its name `azdo-mcp`, its version and the `tools` capability

#### Scenario: No stdout noise
- **WHEN** the server starts with an empty or invalid configuration
- **THEN** stdout contains only valid JSON-RPC messages and the problem is reported on stderr and as a tool error on first call

#### Scenario: Unknown argument
- **WHEN** the executable is started without `--stdio`
- **THEN** it prints usage to stderr and exits with a non-zero code

### Requirement: Tool listing
The server SHALL answer `tools/list` with every available tool, each with a name, a German or English description and a JSON input schema. Tool names SHALL be identical to v1 so existing prompts keep working.

#### Scenario: List tools
- **WHEN** a client calls `tools/list` with at least one connection configured
- **THEN** the result contains the tools defined in the `azdo-tools` capability with their input schemas

### Requirement: Connection selection
Every Azure DevOps tool SHALL accept an optional `project` argument holding a connection short name. Without it, the default connection SHALL be used. Matching SHALL be case-insensitive.

#### Scenario: Explicit connection
- **WHEN** a tool is called with `project="shop"` and a connection `SHOP` exists
- **THEN** the call runs against connection `SHOP`

#### Scenario: Default connection
- **WHEN** a tool is called without `project`
- **THEN** the call runs against the connection marked as default

#### Scenario: Unknown connection
- **WHEN** a tool is called with a `project` that matches no connection
- **THEN** the call fails with a tool error naming the unknown short name and listing the known short names

### Requirement: Error reporting
Failures of a tool call SHALL be returned as MCP tool results with `isError: true` and a short German message; they SHALL NOT crash the server. HTTP 401 SHALL be reported as "401 PAT abgelaufen oder ungültig", 403 as "403 Scope fehlt" plus the missing area if known.

#### Scenario: Expired PAT
- **WHEN** Azure DevOps answers 401
- **THEN** the tool result is an error containing "401" and the server keeps serving further calls

#### Scenario: Secrets never echoed
- **WHEN** any error occurs
- **THEN** the error text contains no PAT, no Authorization header and no encrypted blob

### Requirement: Configuration reload
The server SHALL pick up changes to connections and permissions without a restart of the client.

#### Scenario: Connection added while running
- **WHEN** a connection is added through the Manager while a server instance runs
- **THEN** the next tool call can use the new connection without restarting the server process
