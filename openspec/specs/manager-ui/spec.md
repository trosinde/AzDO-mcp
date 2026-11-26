# manager-ui Specification

## Purpose
Defines the main window of the AzDO MCP Manager: navigation, the five pages and the shared status colours, following docs/ui-proposal.

## Requirements

### Requirement: Window and navigation
The main window SHALL show the title "AzDO MCP Manager", a left navigation with "Übersicht", "Verbindungen & PATs", "MCP-Clients", "Tools & Rechte", "Protokoll", and in the navigation footer "Benutzer-Installation · ohne Admin" and "v<Version> · Updates: <Status>".

#### Scenario: Navigate
- **WHEN** the user clicks "Protokoll"
- **THEN** the Protokoll page is shown and its navigation entry is marked as current

### Requirement: Status colours
Status SHALL use three colours consistently on every page and in the tray: green (#107C10 / pill #DFF6DD) for OK, yellow (#E3B100 / pill #FFF4CE) for warnings such as "in N Tagen" or "PAT erneuern", red (#C42B1C / pill #FDE7E9) for "abgelaufen", 401, 403. Colour SHALL never be the only carrier: each pill also has a text.

#### Scenario: Expired PAT everywhere red
- **WHEN** connection OPS has an expired PAT
- **THEN** its pill is red with text "abgelaufen" on Übersicht, in the connection list and its tray dot is red

### Requirement: Right value colours
Rights SHALL be shown green for "ja", red for "nein" and neutral grey (#4D4D4D / pill #F0F0F0) for "n/a" and "?"; yellow SHALL NOT be used for a single right value. "n/a" and "?" SHALL have a tooltip ("nicht prüfbar ohne echtes Schreiben", "noch nicht geprüft – Neu einlesen").

#### Scenario: Untestable value grey
- **WHEN** Code Schreiben is "n/a"
- **THEN** the cell shows a grey pill "n/a" with the tooltip "nicht prüfbar ohne echtes Schreiben"

### Requirement: Übersicht page
The page SHALL show the server status pill ("MCP-Server läuft" or "MCP-Server pausiert"), the buttons "Neu starten", "Stoppen" and "+ Verbindung", four figures (Verbindungen with project count and expiry hint, Tool-Aufrufe heute with writes, Fehler (24 h), Offene Freigaben with link), the project table, footnotes ¹ ² as in the UI proposal, and the client list with "aktiv"/"offen".

#### Scenario: Figures from log
- **WHEN** 12 calls were logged today, 3 of them writing
- **THEN** "Tool-Aufrufe heute" shows 12 and "davon 3 schreibend"

#### Scenario: Migration banner
- **WHEN** a v1 config with plain-text PATs is detected
- **THEN** a red banner "N PATs im Klartext gefunden" with "Später" and "Importieren" is shown; "Importieren" runs the v1 import grouped per collection

### Requirement: Übersicht project table
The table "Verbindung / Projekt" SHALL show PAT expiry and status per connection and, per project below it, the effective Lesen and Schreiben values for Work Items, Code, Test and Iterationen, and a status text.

#### Scenario: Full-access PAT
- **WHEN** a project can write work items and the PAT has full access
- **THEN** Code, Test and Iterationen show Lesen "ja" and Schreiben "n/a", and no cell is yellow

### Requirement: Project status text
The project status SHALL be "schreibt direkt", "schreibt mit Freigabe", "nur Lesen · <Grund>" (Grund: "PAT", "Projektrecht", "Schreibschalter aus" or "Schreib-Tools gesperrt"), "kein Zugriff" or "nicht mehr sichtbar". When writing is possible but Work Items Schreiben is "n/a" or "?", it SHALL add "· Schreibrecht nicht geprüft". A tooltip SHALL show how it was derived (PAT, Projektrecht, Schreibschalter, Modus).

#### Scenario: Project rows
- **WHEN** OPS's Work Items Schreiben is "nein" because the user may not edit work items there
- **THEN** its row shows a red Work Items Schreiben cell and "nur Lesen · Projektrecht"

#### Scenario: Write switch is the reason
- **WHEN** SHOP's Work Items Schreiben is "ja" and its write switch is off
- **THEN** its status is "nur Lesen · Schreibschalter aus"

### Requirement: Verbindungen & PATs page
The page SHALL show the hint "Eine Verbindung = eine Collection mit einem PAT. Die Projekte darin richtest du mit «Projekte erkennen» ein.", the connection list with projects indented below each connection, and "+ Neue Verbindung" (starts the setup wizard).

#### Scenario: Projects below their connection
- **WHEN** connection tfs has the projects SHOP and OPS
- **THEN** the list shows tfs with SHOP and OPS indented below it

#### Scenario: New connection
- **WHEN** the user clicks "+ Neue Verbindung"
- **THEN** the setup wizard opens at the step "Verbindung"

### Requirement: Connection editor
For the selected connection the page SHALL show server type, "Name der Verbindung", Collection-URL, PAT field with "Ersetzen" and "PAT erstellen ↗", "PAT gültig bis", "Erinnerung", the box "Projekte: N eingerichtet" with the last discovery date and "Neu einlesen" and one editable "Kurzname" per project, "Standardprojekt", and the buttons "Entfernen", "Verwerfen", "Speichern".

#### Scenario: PAT hidden after save
- **WHEN** a saved connection is opened
- **THEN** the PAT field shows only dots and cannot reveal the value

#### Scenario: PAT erstellen
- **WHEN** the user clicks "PAT erstellen ↗"
- **THEN** the browser opens the token page of the entered server (`<collection>/_usersSettings/tokens`)

#### Scenario: Unsaved changes
- **WHEN** the user navigates away with unsaved edits
- **THEN** the app asks whether to save or discard

### Requirement: Kurzname editing
A changed Kurzname SHALL be checked at once against the rules and other projects, and saving a rename SHALL first warn "Prompts mit dem alten Kurznamen funktionieren danach nicht mehr."

#### Scenario: Rename Kurzname
- **WHEN** the user changes OPS to `INF`, clicks "Speichern" and confirms the warning
- **THEN** the project is saved as `INF`, its permission settings stay attached, and running servers list `INF`

#### Scenario: Legacy alias hint
- **WHEN** a project's stored Kurzname `Demo_Project_One` breaks the rules
- **THEN** the field shows the hint "entspricht nicht den Kurznamen-Regeln – Vorschlag: DPO"

### Requirement: Verbindungstest panel
The panel "Verbindungstest" SHALL show "Verbindung testen", the test results, "PAT-Scope (gilt für alle Projekte)" as a grid with the columns "Lesen" and "Schreiben" per area, the footnote "Schreiben wird nur für Work Items geprüft (Probe ohne Anlegen); für Code, Test und Iterationen ist das ohne echtes Schreiben nicht möglich (n/a)." and "Projekte erkennen".

#### Scenario: Projekte erkennen
- **WHEN** the user clicks "Projekte erkennen" for a saved connection
- **THEN** the setup wizard opens at the step "Projekte"

#### Scenario: Scope grid
- **WHEN** the test ran with a full-access PAT
- **THEN** the grid shows Work Items "ja"/"ja" and Code, Test, Iterationen "ja"/"n/a"

### Requirement: Tools & Rechte page
The page SHALL show the subtitle "Ebenen: Gruppe → Tool → Verbindung → Projekt. Die tiefste gesetzte Ebene gilt; Schreibschalter und effektives Recht begrenzen immer.", an expandable tree with the columns Ebene, Modus¹, Effektives Recht², Hinweis; a three-way switch Erlaubt/Nachfragen/Gesperrt per row, shown faded when inherited; per project the Schreibschalter; hints "wie Gruppe" / "wie Tool" / "wie Verbindung" / "Ausnahme"; and the footnotes ¹ ² ³ as in the UI proposal.

#### Scenario: Toggle connection chip
- **WHEN** the user sets "Gesperrt" on the row of connection OPS under "Work Items schreiben" and saves
- **THEN** the write tools are blocked for every project of OPS and the row shows "Ausnahme"

#### Scenario: Set a project exception
- **WHEN** the user sets "Erlaubt" for `update_work_item` on project Musterlab and saves
- **THEN** the row shows "Ausnahme" and calls in Musterlab no longer ask

#### Scenario: Inherited mode faded
- **WHEN** project SHOP has no own mode for `update_work_item`
- **THEN** its switch shows the inherited mode faded and the hint "wie Verbindung"

#### Scenario: Footnote text
- **WHEN** the page is shown
- **THEN** footnote ¹ reads "Erlaubt = KI führt sofort aus · Nachfragen = Freigabe-Fenster mit Erlauben/Ablehnen, ohne Antwort nach 2 min abgelehnt · Gesperrt = Tool wird nicht angeboten. Blasser Modus = geerbt von der Ebene darüber."

### Requirement: Effective right on project rows
On project rows of "Tools & Rechte", "Effektives Recht" SHALL show the value the group needs (Lesen of its area for read groups, Work Items Schreiben for write groups) and, when it is "nein", the reason in the hint.

#### Scenario: Effective right of a write group
- **WHEN** OPS's Work Items Schreiben is "nein" because of the PAT
- **THEN** OPS's row under "Work Items schreiben" shows a red "nein" and the hint "PAT ohne Schreibrecht"

### Requirement: Protokoll page
The page SHALL list audit records newest first with Zeit, Client, Tool, Verb., Dauer, Ergebnis, filters "Alle Verbindungen" and "Alle Ergebnisse", "Exportieren", and a "Freigabe nötig" panel for the selected pending request with remaining time, change summary, "Erlauben" and "Ablehnen".

#### Scenario: Live update
- **WHEN** a new call is logged while the page is open
- **THEN** it appears at the top within 2 seconds

### Requirement: MCP-Clients page
The page SHALL show "MCP-Clients einbinden", the note that the client starts `azdo-mcp.exe --stdio` and PATs stay in the user profile, one card per supported client with status "eingerichtet"/"offen"/"prüfen", its entry, its actions, the section "Prompt zum Einrichten" with the prompt and "Prompt kopieren", and the test question, and the program path with the note that it stays the same after updates.

#### Scenario: Path shown
- **WHEN** the app runs from the installed location
- **THEN** the path `%LOCALAPPDATA%\AzdoMcp\current\azdo-mcp.exe` is shown with a copy button

#### Scenario: Copy prompt
- **WHEN** the user clicks "Prompt kopieren" on the OpenCode card
- **THEN** the OpenCode setup prompt is on the clipboard
