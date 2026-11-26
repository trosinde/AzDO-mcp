# mcp-server Specification

## Purpose
Lets MCP clients (Claude Code, Claude Desktop, OpenCode, VS Code/Copilot) talk to Azure DevOps through one local executable started over stdio.

## Requirements

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
Every Azure DevOps tool SHALL accept an optional `project` argument holding a project Kurzname. Its description SHALL list the configured Kurznamen with project names and its schema SHALL list them as allowed values. Without it, the default project SHALL be used. Matching SHALL be case-insensitive and SHALL consider only configured projects; there is no fallback to unconfigured projects of a collection.

#### Scenario: Explicit connection
- **WHEN** a tool is called with `project="shop"` and a project with Kurzname `SHOP` is configured
- **THEN** the call runs against that project with its connection's PAT

#### Scenario: Default connection
- **WHEN** a tool is called without `project`
- **THEN** the call runs against the project marked as default

#### Scenario: Unknown connection
- **WHEN** a tool is called with a `project` that matches no configured Kurzname, even if such a project exists in the collection
- **THEN** the call fails with "Unbekannter Kurzname „X“. Eingerichtet: …" listing the configured Kurznamen

#### Scenario: Kurznamen in the schema
- **WHEN** SHOP and OPS are configured and a client calls `tools/list`
- **THEN** the `project` parameter of `get_work_item` lists `SHOP` and `OPS` as allowed values

### Requirement: Error reporting
Failures of a tool call SHALL be returned as MCP tool results with `isError: true` and a short German message; they SHALL NOT crash the server. HTTP 401 SHALL be reported as "401 PAT abgelaufen oder ungültig", 403 as "403 Scope fehlt" plus the missing area if known.

#### Scenario: Expired PAT
- **WHEN** Azure DevOps answers 401
- **THEN** the tool result is an error containing "401" and the server keeps serving further calls

#### Scenario: Secrets never echoed
- **WHEN** any error occurs
- **THEN** the error text contains no PAT, no Authorization header and no encrypted blob

### Requirement: Configuration reload
The server SHALL pick up changes to connections and permissions without a restart of the client. When the set of Kurznamen changes, the server SHALL send `notifications/tools/list_changed`.

#### Scenario: Connection added while running
- **WHEN** a connection is added through the Manager while a server instance runs
- **THEN** the next tool call can use the new connection without restarting the server process

#### Scenario: Kurzname renamed while running
- **WHEN** OPS is renamed to `INF` in the Manager while a client is connected
- **THEN** the server sends `notifications/tools/list_changed` and the next `tools/list` lists `INF`

### Requirement: Kurznamen in server instructions
The instructions the server sends at `initialize` SHALL list every visible configured project as "Kurzname = Projektname (Verbindung)", name the default project, and tell the AI that a named Kurzname means that Azure DevOps project, with an example using the first configured Kurzname. Without configured projects they SHALL say that no project is set up yet. They SHALL contain no PAT and no collection URL.

#### Scenario: Fixed guidance sentence
- **WHEN** SHOP is the first configured project
- **THEN** the instructions contain "Nennt der Nutzer einen dieser Kurznamen, ist dieses Azure-DevOps-Projekt gemeint: die Tools direkt mit project=\"<Kurzname>\" aufrufen, ohne nachzufragen. Beispiel: „gib mir aus SHOP das Work Item 4711“ → get_work_item mit project=\"SHOP\", id=4711."

#### Scenario: Instructions list projects
- **WHEN** SHOP ("Demo Project One", Verbindung tfs, Standard) and OPS are configured
- **THEN** the instructions contain "SHOP = Demo Project One (tfs)", "OPS = …" and "Standardprojekt: SHOP"

#### Scenario: Work item request understood
- **WHEN** the user writes "gib mir aus SHOP das workitem 4711" in a connected client
- **THEN** the instructions tell the AI to call `get_work_item` with `project="SHOP"` and `id=4711` without asking which system is meant

#### Scenario: Hidden project left out
- **WHEN** QA-Automation is marked not visible
- **THEN** the instructions do not list it
