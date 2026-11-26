# Spec Delta

## Purpose

Defines the main window of the AzDO MCP Manager: navigation, the five pages and the shared status colours, following docs/ui-proposal.

## ADDED Requirements

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

### Requirement: Übersicht page
The page SHALL show the server status pill ("MCP-Server läuft" or "MCP-Server pausiert"), the buttons "Neu starten", "Stoppen" and "+ Verbindung", four figures (Verbindungen with expiry hint, Tool-Aufrufe heute with writes, Fehler (24 h), Offene Freigaben with link), the connection table (Kurzname, Server · Projekt, PAT läuft ab, Status, test button) and the client list with "aktiv"/"offen".

#### Scenario: Figures from log
- **WHEN** 12 calls were logged today, 3 of them writing
- **THEN** "Tool-Aufrufe heute" shows 12 and "davon 3 schreibend"

#### Scenario: Migration banner
- **WHEN** a v1 config with plain-text PATs is detected
- **THEN** a red banner "N PATs im Klartext gefunden" with "Später" and "Importieren" is shown; "Importieren" runs the v1 import

### Requirement: Verbindungen & PATs page
The page SHALL show the connection list with status pills and "+ Neue Verbindung", and for the selected connection: server type radio buttons, Kurzname (hint `Die KI nutzt: project="<Kurzname>"`), Projekt (from the connection test), Collection-URL, PAT field with "Ersetzen" and "PAT erstellen ↗", "PAT gültig bis", "Erinnerung", "Standard-Verbindung", "Schreibzugriff zulassen", the buttons "Entfernen", "Verwerfen", "Speichern", and the connection test panel with "Testen" and per-area results.

#### Scenario: PAT hidden after save
- **WHEN** a saved connection is opened
- **THEN** the PAT field shows only dots and cannot reveal the value

#### Scenario: PAT erstellen
- **WHEN** the user clicks "PAT erstellen ↗"
- **THEN** the browser opens the token page of the entered server (`<collection>/_usersSettings/tokens`)

#### Scenario: Unsaved changes
- **WHEN** the user navigates away with unsaved edits
- **THEN** the app asks whether to save or discard

### Requirement: Tools & Rechte page
The page SHALL show the subtitle "Was die KI darf – pro Tool-Gruppe, Ausnahmen pro Verbindung.", a table with Gruppe, Tools, Modus¹, Verbindungen, a three-way switch Erlaubt/Nachfragen/Gesperrt (green/yellow/red when active), sub-rows for the write tools, connection chips (struck-through red when blocked or scope missing, toggled by click), and the footnotes ¹ and ² as in the UI proposal.

#### Scenario: Toggle connection chip
- **WHEN** the user clicks chip "OPS" in row "Work Items schreiben" and saves
- **THEN** the chip is struck through and write tools are blocked for OPS

#### Scenario: Footnote text
- **WHEN** the page is shown
- **THEN** footnote ¹ reads "Erlaubt = KI führt sofort aus · Nachfragen = Windows-Benachrichtigung mit Erlauben/Ablehnen, ohne Antwort nach 2 min abgelehnt · Gesperrt = Tool wird der KI gar nicht angeboten."

### Requirement: Protokoll page
The page SHALL list audit records newest first with Zeit, Client, Tool, Verb., Dauer, Ergebnis, filters "Alle Verbindungen" and "Alle Ergebnisse", "Exportieren", and a "Freigabe nötig" panel for the selected pending request with remaining time, change summary, "Erlauben" and "Ablehnen".

#### Scenario: Live update
- **WHEN** a new call is logged while the page is open
- **THEN** it appears at the top within 2 seconds

### Requirement: MCP-Clients page
The page SHALL show "MCP-Clients einbinden", the note that the client starts `azdo-mcp.exe --stdio` and PATs stay in the user profile, one card per supported client with status "eingerichtet"/"offen" and its actions, and the program path with the note that it stays the same after updates.

#### Scenario: Path shown
- **WHEN** the app runs from the installed location
- **THEN** the path `%LOCALAPPDATA%\AzdoMcp\current\azdo-mcp.exe` is shown with a copy button
