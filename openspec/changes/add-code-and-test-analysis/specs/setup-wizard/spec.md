## MODIFIED Requirements

### Requirement: Step Projekte
The step SHALL list all discovered projects with checkbox, name, the effective Lesen and Schreiben values for Work Items, Code, Pipelines, Test and Iterationen, and status ("neu", "bereits eingerichtet"), with filters "Alle Projekte", "Nur neue", "Bereits eingerichtet" and "Alle auswählen". New projects with at least one area whose Lesen is not "nein" SHALL be preselected; already configured projects SHALL be shown but not selectable.

#### Scenario: Preselection
- **WHEN** Archiv-2019 has Lesen "nein" in every area and Sandbox has Code Lesen "ja"
- **THEN** Sandbox is preselected and Archiv-2019 is not

### Requirement: Step per project
Each project step SHALL show the effective rights and let the user set the "Kurzname" (prefilled with the suggestion, rules shown below the field, preview "So sprichst du es an: „offene Bugs in <Kurzname>“"), "Standardprojekt", "Schreibschalter" and a mode per tool group (Work Items lesen, Work Items schreiben, Git, Pipelines, Test, Planung).

#### Scenario: Alias clash
- **WHEN** the Kurzname "SHOP" is already used by another project
- **THEN** "Weiter" is blocked with "Kurzname „SHOP“ wird bereits verwendet"

#### Scenario: Suggestion and preview
- **WHEN** the project step for "Demo Project One" opens
- **THEN** the field holds `DPO` and the preview reads "So sprichst du es an: „offene Bugs in DPO“"

#### Scenario: Invalid Kurzname blocks
- **WHEN** the user enters `Demo Project`
- **THEN** "Weiter" is blocked and the rule text is shown in red
