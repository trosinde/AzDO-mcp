## MODIFIED Requirements

### Requirement: Step per project
Each project step SHALL show the effective rights and let the user set the "Kurzname" (prefilled with the suggestion, rules shown below the field, preview "So sprichst du es an: „offene Bugs in <Kurzname>“"), "Standardprojekt", "Schreibschalter" and a mode per tool group (Work Items lesen, Work Items schreiben, Git, Test, Planung).

#### Scenario: Alias clash
- **WHEN** the Kurzname "SHOP" is already used by another project
- **THEN** "Weiter" is blocked with "Kurzname „SHOP“ wird bereits verwendet"

#### Scenario: Suggestion and preview
- **WHEN** the project step for "Demo Project One" opens
- **THEN** the field holds `DPO` and the preview reads "So sprichst du es an: „offene Bugs in DPO“"

#### Scenario: Invalid Kurzname blocks
- **WHEN** the user enters `Demo Project`
- **THEN** "Weiter" is blocked and the rule text is shown in red
