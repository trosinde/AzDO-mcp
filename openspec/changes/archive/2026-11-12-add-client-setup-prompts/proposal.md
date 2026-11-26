# Proposal

## Why

The one-click entries do not always lead to a working setup. OpenCode, for example, failed because its config file already held an invalid `servers` wrapper under `mcp`, and the card still showed "eingerichtet". The user wants a ready-made prompt per client to paste into the AI tool. The tool then sets up the server itself and can fix such problems in the file on the way.

## What Changes

- Every client card on "MCP-Clients einbinden" gets a section "Prompt zum Einrichten", with the prompt text and a "Prompt kopieren" button.
- The prompt names the server name `azure-devops`, the program path and the exact entry for that client. It also says to back up the file first, keep all other content and write valid JSON.
  - **Claude Code:** the prompt is for Claude Code itself (run `claude mcp add`, check with `claude mcp list`).
  - **Claude Desktop:** the prompt is for an assistant with file access, e.g. Claude Code, because Claude Desktop cannot edit its own config.
  - **OpenCode:** the prompt is for OpenCode itself. It also says to move entries out of a `servers` wrapper.
  - **VS Code / Copilot:** the prompt is for Copilot agent mode and edits the user `mcp.json`.
- Below every prompt there is a test question for after the restart: „Welche Azure-DevOps-Projekte kennst du?“. Once projects are set up there is also „Zeig die offenen Bugs in <Kurzname>“.
- Prompts contain no PAT, no collection URL and no project names, only the Kurzname in the test question.
- The OpenCode card shows "prüfen" in yellow when `mcp` contains a `servers` key, with the hint „„servers“ unter „mcp“ ist kein gültiger Eintrag – Prompt nutzen oder Datei korrigieren“.
- The one-click buttons stay.

## Capabilities

### New Capabilities
None.

### Modified Capabilities
- `client-setup`: setup prompt per client, the content rules for prompts, the OpenCode wrapper check.
- `manager-ui`: the MCP-Clients page shows the prompt section and the test question.

## Impact

- **Manager.Core:**
  - `Clients/ClientSetup.cs` (prompt builder, wrapper check);
  - `ViewModels/ClientsViewModel.cs` (`ClientCard.Prompt`, `TestQuestion`, copy command).
- **Manager:** `Views/ClientsView.xaml`.
- **Tests:** prompt content per client (path, entry, no secrets), test question with Kurzname, wrapper detection.
- **Dependencies:** no new packages.
