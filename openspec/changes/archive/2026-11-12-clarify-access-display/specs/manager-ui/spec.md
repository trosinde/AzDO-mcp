## MODIFIED Requirements

### Requirement: Status colours
Status SHALL use three colours consistently on every page and in the tray: green (#107C10 / pill #DFF6DD) for OK, yellow (#E3B100 / pill #FFF4CE) for warnings such as "in N Tagen" or "PAT erneuern", red (#C42B1C / pill #FDE7E9) for "abgelaufen", 401, 403. Rights SHALL be shown green for "ja", red for "nein" and neutral grey (#4D4D4D / pill #F0F0F0) for "n/a" and "?"; yellow SHALL NOT be used for a single right value. Colour SHALL never be the only carrier: each pill also has a text, and "n/a" and "?" have a tooltip ("nicht prüfbar ohne echtes Schreiben", "noch nicht geprüft – Neu einlesen").

#### Scenario: Expired PAT everywhere red
- **WHEN** connection OPS has an expired PAT
- **THEN** its pill is red with text "abgelaufen" on Übersicht, in the connection list and its tray dot is red

#### Scenario: Untestable value grey
- **WHEN** Code Schreiben is "n/a"
- **THEN** the cell shows a grey pill "n/a" with the tooltip "nicht prüfbar ohne echtes Schreiben"

### Requirement: Übersicht project table
The table "Verbindung / Projekt" SHALL show PAT expiry and status per connection and, per project below it, the effective Lesen and Schreiben values for Work Items, Code, Test and Iterationen, and a status text: "schreibt direkt", "schreibt mit Freigabe", "nur Lesen · <Grund>" with the reason "PAT", "Projektrecht", "Schreibschalter aus" or "Schreib-Tools gesperrt", "kein Zugriff" or "nicht mehr sichtbar". When writing is possible but Work Items Schreiben is "n/a" or "?", the status SHALL add "· Schreibrecht nicht geprüft". A tooltip on the status SHALL show how it was derived (PAT, Projektrecht, Schreibschalter, Modus).

#### Scenario: Project rows
- **WHEN** OPS's Work Items Schreiben is "nein" because the user may not edit work items there
- **THEN** its row shows a red Work Items Schreiben cell and "nur Lesen · Projektrecht"

#### Scenario: Write switch is the reason
- **WHEN** SHOP's Work Items Schreiben is "ja" and its write switch is off
- **THEN** its status is "nur Lesen · Schreibschalter aus"

#### Scenario: Full-access PAT
- **WHEN** a project can write work items and the PAT has full access
- **THEN** Code, Test and Iterationen show Lesen "ja" and Schreiben "n/a", and no cell is yellow

### Requirement: Verbindungstest panel
The panel "Verbindungstest" SHALL show "Verbindung testen", the test results, "PAT-Scope (gilt für alle Projekte)" as a grid with the columns "Lesen" and "Schreiben" per area, the footnote "Schreiben wird nur für Work Items geprüft (Probe ohne Anlegen); für Code, Test und Iterationen ist das ohne echtes Schreiben nicht möglich (n/a)." and "Projekte erkennen".

#### Scenario: Projekte erkennen
- **WHEN** the user clicks "Projekte erkennen" for a saved connection
- **THEN** the setup wizard opens at the step "Projekte"

#### Scenario: Scope grid
- **WHEN** the test ran with a full-access PAT
- **THEN** the grid shows Work Items "ja"/"ja" and Code, Test, Iterationen "ja"/"n/a"

### Requirement: Tools & Rechte page
The page SHALL show the subtitle "Ebenen: Gruppe → Tool → Verbindung → Projekt. Die tiefste gesetzte Ebene gilt; Schreibschalter und effektives Recht begrenzen immer.", an expandable tree with the columns Ebene, Modus¹, Effektives Recht², Hinweis; a three-way switch Erlaubt/Nachfragen/Gesperrt per row, shown faded when inherited; per project the Schreibschalter; hints "wie Gruppe" / "wie Tool" / "wie Verbindung" / "Ausnahme"; and the footnotes ¹ ² ³ as in the UI proposal. On project rows "Effektives Recht" SHALL show the value the group needs (Lesen of its area for read groups, Work Items Schreiben for write groups) and, when it is "nein", the reason in the hint.

#### Scenario: Toggle connection chip
- **WHEN** the user sets "Gesperrt" on the row of connection OPS under "Work Items schreiben" and saves
- **THEN** the write tools are blocked for every project of OPS and the row shows "Ausnahme"

#### Scenario: Set a project exception
- **WHEN** the user sets "Erlaubt" for `update_work_item` on project Musterlab and saves
- **THEN** the row shows "Ausnahme" and calls in Musterlab no longer ask

#### Scenario: Inherited mode faded
- **WHEN** project SHOP has no own mode for `update_work_item`
- **THEN** its switch shows the inherited mode faded and the hint "wie Verbindung"

#### Scenario: Effective right of a write group
- **WHEN** OPS's Work Items Schreiben is "nein" because of the PAT
- **THEN** OPS's row under "Work Items schreiben" shows a red "nein" and the hint "PAT ohne Schreibrecht"

#### Scenario: Footnote text
- **WHEN** the page is shown
- **THEN** footnote ¹ reads "Erlaubt = KI führt sofort aus · Nachfragen = Freigabe-Fenster mit Erlauben/Ablehnen, ohne Antwort nach 2 min abgelehnt · Gesperrt = Tool wird nicht angeboten. Blasser Modus = geerbt von der Ebene darüber."
