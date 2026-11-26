# Spec Delta

## Purpose

Guides the user from collection URL and PAT to fully configured projects in one pass, one step per project, so rules can still be set per project.

## ADDED Requirements

### Requirement: Wizard steps
The Manager SHALL offer a setup wizard with the steps "Verbindung", "Projekte", one step "Projekt k von n" per selected project, and "Zusammenfassung". A step indicator SHALL show the current step; "Zurück" SHALL keep all inputs.

#### Scenario: Back keeps input
- **WHEN** the user goes from project step 2 back to step 1 and forward again
- **THEN** the values entered in both steps are unchanged

### Requirement: Step Verbindung
For a new connection the first step SHALL ask for server type, connection name, collection URL, PAT ("PAT erstellen ↗"), "PAT gültig bis" and "Erinnerung", and SHALL offer "Verbindung testen" showing login, API version and the PAT scope per area. "Weiter" SHALL be possible only after a successful test. For an existing connection the wizard SHALL start at "Projekte".

#### Scenario: Test required
- **WHEN** the connection test has not succeeded
- **THEN** "Weiter" is disabled

### Requirement: Step Projekte
The step SHALL list all discovered projects with checkbox, name, effective right for Work Items, Code and Test as Ampel pills, and status ("neu", "bereits eingerichtet"), with filters "Alle Projekte", "Nur neue", "Bereits eingerichtet" and "Alle auswählen". New projects with at least one readable area SHALL be preselected; already configured projects SHALL be shown but not selectable.

#### Scenario: Preselection
- **WHEN** Archiv-2019 has no access in any area and Sandbox can be read
- **THEN** Sandbox is preselected and Archiv-2019 is not

### Requirement: Step per project
Each project step SHALL show the effective rights and let the user set the alias for the AI (default: project name, unique across all connections), "Standardprojekt", "Schreibschalter" and a mode per tool group (Work Items lesen, Work Items schreiben, Git, Test, Planung).

#### Scenario: Alias clash
- **WHEN** the alias "SHOP" is already used by another project
- **THEN** "Weiter" is blocked with "Alias „SHOP“ wird bereits verwendet"

### Requirement: Project step defaults
In a project step, read groups SHALL start as Erlaubt, write groups as Nachfragen and the Schreibschalter off; the Schreibschalter SHALL be disabled unless Work Items is "Schreiben". Groups without effective access SHALL show "kein Zugriff" and be locked to Gesperrt.

#### Scenario: No write right
- **WHEN** a project's Work Items right is "nur Lesen"
- **THEN** the Schreibschalter is off and disabled with a hint why

#### Scenario: Locked group
- **WHEN** a project's Test right is "kein Zugriff"
- **THEN** the Test group shows "kein Zugriff" and cannot be set to Erlaubt

### Requirement: Skip remaining projects
"Übrige mit Voreinstellung" SHALL apply the defaults to all remaining project steps and go to the summary.

#### Scenario: Skip
- **WHEN** the user is at project 2 of 5 and clicks "Übrige mit Voreinstellung"
- **THEN** projects 3–5 get the defaults and the summary is shown

### Requirement: Summary and apply
The summary SHALL list every selected project with alias, effective rights, Schreibschalter and changed modes, and "Übernehmen (N Projekte)" SHALL store the connection (PAT once, encrypted), the projects and only those modes the user changed from the inherited value, in one atomic save. Running servers SHALL reload afterwards.

#### Scenario: Unchanged defaults are not frozen
- **WHEN** the user keeps all defaults for Sandbox and later changes the global mode of Git to Nachfragen
- **THEN** Git in Sandbox asks too

#### Scenario: PAT stored once
- **WHEN** three projects of one collection are applied
- **THEN** exactly one encrypted PAT file exists for that connection

### Requirement: Rescan view
"Neu einlesen" SHALL show the rescan diff in groups "Neu sichtbar" (checkbox = einrichten), "Nicht mehr sichtbar" (kept, with "Entfernen …"), "Rechte geändert (alt → neu)" and "Unverändert", and "Änderungen übernehmen" SHALL run the project steps for the checked new projects before applying.

#### Scenario: New project in rescan
- **WHEN** Musterlab-Docs is checked under "Neu sichtbar" and the user clicks "Änderungen übernehmen"
- **THEN** the project step for Musterlab-Docs is shown before anything is saved

#### Scenario: Remove only on request
- **WHEN** the user clicks "Entfernen …" for QA-Automation and confirms
- **THEN** only QA-Automation and its project settings are removed
