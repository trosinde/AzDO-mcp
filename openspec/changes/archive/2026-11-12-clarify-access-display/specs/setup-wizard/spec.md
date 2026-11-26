## MODIFIED Requirements

### Requirement: Step Projekte
The step SHALL list all discovered projects with checkbox, name, the effective Lesen and Schreiben values for Work Items, Code, Test and Iterationen, and status ("neu", "bereits eingerichtet"), with filters "Alle Projekte", "Nur neue", "Bereits eingerichtet" and "Alle auswählen". New projects with at least one area whose Lesen is not "nein" SHALL be preselected; already configured projects SHALL be shown but not selectable.

#### Scenario: Preselection
- **WHEN** Archiv-2019 has Lesen "nein" in every area and Sandbox has Code Lesen "ja"
- **THEN** Sandbox is preselected and Archiv-2019 is not

### Requirement: Project step defaults
In a project step, read groups SHALL start as Erlaubt, write groups as Nachfragen and the Schreibschalter off. The Schreibschalter SHALL be disabled only when the effective Work Items Schreiben is "nein", with the reason ("PAT ohne Schreibrecht" or "kein Projektrecht zum Bearbeiten"); when it is "n/a" or "?" it SHALL be enabled with the hint "Schreibrecht nicht prüfbar – Azure DevOps entscheidet beim Schreiben". Groups whose area has Lesen "nein" SHALL show "kein Zugriff" and be locked to Gesperrt.

#### Scenario: No write right
- **WHEN** a project's Work Items Schreiben is "nein" because of the PAT
- **THEN** the Schreibschalter is off and disabled with the hint "PAT ohne Schreibrecht"

#### Scenario: Write right not testable
- **WHEN** a project's Work Items Schreiben is "n/a"
- **THEN** the Schreibschalter can be switched on and shows "Schreibrecht nicht prüfbar – Azure DevOps entscheidet beim Schreiben"

#### Scenario: Locked group
- **WHEN** a project's Test Lesen is "nein"
- **THEN** the Test group shows "kein Zugriff" and cannot be set to Erlaubt

### Requirement: Rescan view
"Neu einlesen" SHALL show the rescan diff in groups "Neu sichtbar" (checkbox = einrichten, with Lesen/Schreiben per area), "Nicht mehr sichtbar" (kept, with "Entfernen …"), "Rechte geändert (alt → neu)" listing each changed value as "<Bereich> <Lesen|Schreiben>: alt → neu", and "Unverändert", and "Änderungen übernehmen" SHALL run the project steps for the checked new projects before applying.

#### Scenario: New project in rescan
- **WHEN** Musterlab-Docs is checked under "Neu sichtbar" and the user clicks "Änderungen übernehmen"
- **THEN** the project step for Musterlab-Docs is shown before anything is saved

#### Scenario: Changed value shown per column
- **WHEN** OPS's Work Items Schreiben changed from "ja" to "nein"
- **THEN** "Rechte geändert" lists OPS with "Work Items Schreiben: ja → nein"

#### Scenario: Remove only on request
- **WHEN** the user clicks "Entfernen …" for QA-Automation and confirms
- **THEN** only QA-Automation and its project settings are removed
