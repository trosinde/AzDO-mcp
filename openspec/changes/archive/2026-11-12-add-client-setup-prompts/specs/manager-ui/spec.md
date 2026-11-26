## MODIFIED Requirements

### Requirement: MCP-Clients page
The page SHALL show "MCP-Clients einbinden", the note that the client starts `azdo-mcp.exe --stdio` and PATs stay in the user profile, one card per supported client with status "eingerichtet"/"offen"/"prüfen", its entry, its actions, the section "Prompt zum Einrichten" with the prompt and "Prompt kopieren", and the test question, and the program path with the note that it stays the same after updates.

#### Scenario: Path shown
- **WHEN** the app runs from the installed location
- **THEN** the path `%LOCALAPPDATA%\AzdoMcp\current\azdo-mcp.exe` is shown with a copy button

#### Scenario: Copy prompt
- **WHEN** the user clicks "Prompt kopieren" on the OpenCode card
- **THEN** the OpenCode setup prompt is on the clipboard
