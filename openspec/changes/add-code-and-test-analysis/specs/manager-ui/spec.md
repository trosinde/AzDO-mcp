## MODIFIED Requirements

### Requirement: Übersicht project table
The table "Verbindung / Projekt" SHALL show PAT expiry and status per connection and, per project below it, the effective Lesen and Schreiben values for Work Items, Code, Pipelines, Test and Iterationen, and a status text.

#### Scenario: Full-access PAT
- **WHEN** a project can write work items and the PAT has full access
- **THEN** Code, Pipelines, Test and Iterationen show Lesen "ja" and Schreiben "n/a", and no cell is yellow

### Requirement: Verbindungstest panel
The panel "Verbindungstest" SHALL show "Verbindung testen", the test results, "PAT-Scope (gilt für alle Projekte)" as a grid with the columns "Lesen" and "Schreiben" per area, the footnote "Schreiben wird nur für Work Items geprüft (Probe ohne Anlegen); für Code, Pipelines, Test und Iterationen ist das ohne echtes Schreiben nicht möglich (n/a)." and "Projekte erkennen".

#### Scenario: Projekte erkennen
- **WHEN** the user clicks "Projekte erkennen" for a saved connection
- **THEN** the setup wizard opens at the step "Projekte"

#### Scenario: Scope grid
- **WHEN** the test ran with a full-access PAT
- **THEN** the grid shows Work Items "ja"/"ja" and Code, Pipelines, Test, Iterationen "ja"/"n/a"
