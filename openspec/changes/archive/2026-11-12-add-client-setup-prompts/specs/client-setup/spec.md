## ADDED Requirements

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
