## MODIFIED Requirements

### Requirement: Connection editor
For the selected connection the page SHALL show server type, "Name der Verbindung", Collection-URL, PAT field with "Ersetzen" and "PAT erstellen ↗", "PAT gültig bis", "Erinnerung", the box "Projekte: N eingerichtet" with the last discovery date and "Neu einlesen" and one editable "Kurzname" per project, "Standardprojekt", and the buttons "Entfernen", "Verwerfen", "Speichern". A changed Kurzname SHALL be checked at once against the rules and other projects, and saving a rename SHALL first warn "Prompts mit dem alten Kurznamen funktionieren danach nicht mehr."

#### Scenario: PAT hidden after save
- **WHEN** a saved connection is opened
- **THEN** the PAT field shows only dots and cannot reveal the value

#### Scenario: PAT erstellen
- **WHEN** the user clicks "PAT erstellen ↗"
- **THEN** the browser opens the token page of the entered server (`<collection>/_usersSettings/tokens`)

#### Scenario: Unsaved changes
- **WHEN** the user navigates away with unsaved edits
- **THEN** the app asks whether to save or discard

#### Scenario: Rename Kurzname
- **WHEN** the user changes OPS to `INF`, clicks "Speichern" and confirms the warning
- **THEN** the project is saved as `INF`, its permission settings stay attached, and running servers list `INF`

#### Scenario: Legacy alias hint
- **WHEN** a project's stored Kurzname `Demo_Project_One` breaks the rules
- **THEN** the field shows the hint "entspricht nicht den Kurznamen-Regeln – Vorschlag: DPO"
