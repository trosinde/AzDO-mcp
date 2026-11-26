# Spec Delta

## MODIFIED Requirements

### Requirement: Übersicht page
The page SHALL show the server status pill ("MCP-Server läuft" or "MCP-Server pausiert"), the buttons "Neu starten", "Stoppen" and "+ Verbindung", four figures (Verbindungen with project count and expiry hint, Tool-Aufrufe heute with writes, Fehler (24 h), Offene Freigaben with link), the table "Verbindung / Projekt" with PAT expiry and status per connection and, per project below it, Ampel pills for WI · Code · Test and a status text ("schreibt direkt", "schreibt mit Freigabe", "nur Lesen", "nicht mehr sichtbar"), footnotes ¹ ² as in the UI proposal, and the client list with "aktiv"/"offen".

#### Scenario: Figures from log
- **WHEN** 12 calls were logged today, 3 of them writing
- **THEN** "Tool-Aufrufe heute" shows 12 and "davon 3 schreibend"

#### Scenario: Migration banner
- **WHEN** a v1 config with plain-text PATs is detected
- **THEN** a red banner "N PATs im Klartext gefunden" with "Später" and "Importieren" is shown; "Importieren" runs the v1 import grouped per collection

#### Scenario: Project rows
- **WHEN** OPS can only read work items
- **THEN** its row shows a yellow WI pill and "nur Lesen"

### Requirement: Verbindungen & PATs page
The page SHALL show the hint "Eine Verbindung = eine Collection mit einem PAT. Die Projekte darin richtest du mit «Projekte erkennen» ein.", the connection list with projects indented below each connection, "+ Neue Verbindung" (starts the setup wizard), and for the selected connection: server type, "Name der Verbindung", Collection-URL, PAT field with "Ersetzen" and "PAT erstellen ↗", "PAT gültig bis", "Erinnerung", the box "Projekte: N eingerichtet" with the last discovery date and "Neu einlesen", "Standardprojekt", the buttons "Entfernen", "Verwerfen", "Speichern", and the panel "Verbindungstest" with "Verbindung testen", results, "PAT-Scope (gilt für alle Projekte)" per area and "Projekte erkennen".

#### Scenario: PAT hidden after save
- **WHEN** a saved connection is opened
- **THEN** the PAT field shows only dots and cannot reveal the value

#### Scenario: PAT erstellen
- **WHEN** the user clicks "PAT erstellen ↗"
- **THEN** the browser opens the token page of the entered server (`<collection>/_usersSettings/tokens`)

#### Scenario: Unsaved changes
- **WHEN** the user navigates away with unsaved edits
- **THEN** the app asks whether to save or discard

#### Scenario: Projekte erkennen
- **WHEN** the user clicks "Projekte erkennen" for a saved connection
- **THEN** the setup wizard opens at the step "Projekte"

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
