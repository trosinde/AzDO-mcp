# Spec Delta

## Purpose

Lets the user register azdo-mcp.exe in supported MCP clients with one click, without putting any PAT into a client configuration.

## ADDED Requirements

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
