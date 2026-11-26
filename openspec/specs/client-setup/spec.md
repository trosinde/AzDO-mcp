# client-setup Specification

## Purpose
Lets the user register azdo-mcp.exe in supported MCP clients with one click, without putting any PAT into a client configuration.

## Requirements

### Requirement: Supported clients
The Manager SHALL support Claude Code, Claude Desktop, OpenCode and VS Code/GitHub Copilot, detect whether each is already set up for `azdo-mcp` and show "eingerichtet" or "offen".

#### Scenario: Detect existing entry
- **WHEN** `claude_desktop_config.json` contains a server entry pointing to `azdo-mcp.exe`
- **THEN** Claude Desktop shows "eingerichtet"

### Requirement: Server entry content
Every written entry SHALL use the server name `azure-devops`, the command `%LOCALAPPDATA%\AzdoMcp\current\azdo-mcp.exe` (expanded) and the argument `--stdio`, and SHALL contain no PAT, URL or environment secret.

#### Scenario: No secrets in client config
- **WHEN** an entry is written for any client
- **THEN** the client file contains no PAT and no collection URL

### Requirement: Claude Code
For Claude Code the Manager SHALL offer "Befehl kopieren" (`claude mcp add --scope user azure-devops -- "<exe>" --stdio`) and "Ausführen", which runs the command if the `claude` CLI is found.

#### Scenario: CLI missing
- **WHEN** "Ausführen" is clicked and `claude` is not on PATH
- **THEN** the Manager says the CLI was not found and the command stays copyable

### Requirement: File-based clients
For Claude Desktop (`%APPDATA%\Claude\claude_desktop_config.json`), OpenCode (`%USERPROFILE%\.config\opencode\opencode.jsonc`) and VS Code (user `%APPDATA%\Code\User\mcp.json` or workspace `.vscode\mcp.json`) the Manager SHALL add or update only its own entry and keep all other content.

#### Scenario: Keep other servers
- **WHEN** the Claude Desktop config already lists other MCP servers
- **THEN** after writing they are unchanged and `azure-devops` is added

#### Scenario: JSONC comments kept
- **WHEN** `opencode.jsonc` contains comments
- **THEN** the comments are still present after writing

### Requirement: Backup before writing
Before changing a client file the Manager SHALL save a copy next to it named `<file>.azdo-mcp-backup-<yyyyMMddHHmmss>`.

#### Scenario: Backup created
- **WHEN** an entry is written to an existing file
- **THEN** a backup with the original content exists

#### Scenario: Invalid client file
- **WHEN** the client file is not valid JSON
- **THEN** the Manager does not write and reports the parse error

### Requirement: Setup prompt per client
For every supported client the Manager SHALL provide a setup prompt in German that a user can paste into an AI assistant. It SHALL name the server name `azure-devops`, the expanded program path, the client's config location and the exact entry, and SHALL tell the assistant to back up the file first, keep all other content and write valid JSON.

#### Scenario: Claude Code prompt
- **WHEN** the Claude Code prompt is shown
- **THEN** it asks to run `claude mcp add --scope user azure-devops -- "<exe>" --stdio` and to check the result with `claude mcp list`

#### Scenario: OpenCode prompt fixes a wrapper
- **WHEN** the OpenCode prompt is shown
- **THEN** it names the entry `"azure-devops": { "type": "local", "command": ["<exe>", "--stdio"], "enabled": true }` directly under `mcp` and asks to move entries out of a `servers` key under `mcp`

#### Scenario: Claude Desktop prompt for an assistant with file access
- **WHEN** the Claude Desktop prompt is shown
- **THEN** it is marked "für einen Assistenten mit Dateizugriff (z. B. Claude Code)" and names `claude_desktop_config.json` with the `mcpServers` entry

### Requirement: Prompt content rules
A setup prompt SHALL contain no PAT, no collection URL and no project name, and SHALL tell the assistant not to add or change tokens or headers.

#### Scenario: No secrets in prompts
- **WHEN** connections with PATs are configured
- **THEN** none of the prompts contains a PAT, a collection URL or a project name

### Requirement: Test question
Each client card SHALL show a test question for after the client restart: „Welche Azure-DevOps-Projekte kennst du?“ and, when projects are set up, „Zeig die offenen Bugs in <Kurzname>“ with the default project's Kurzname.

#### Scenario: Test question with Kurzname
- **WHEN** SHOP is the default project
- **THEN** the test question includes „Zeig die offenen Bugs in SHOP“

### Requirement: OpenCode wrapper check
When the OpenCode config holds a `servers` key directly under `mcp`, the OpenCode card SHALL show "prüfen" (yellow) with the hint „„servers“ unter „mcp“ ist kein gültiger Eintrag – Prompt nutzen oder Datei korrigieren“, even if an `azure-devops` entry exists.

#### Scenario: Wrapper detected
- **WHEN** `opencode.json` contains `"mcp": { "azure-devops": { … }, "servers": { … } }`
- **THEN** the OpenCode card shows "prüfen" and the hint
