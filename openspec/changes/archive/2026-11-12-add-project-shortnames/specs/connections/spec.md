## MODIFIED Requirements

### Requirement: Connection definition
A connection SHALL consist of a unique name, a server type (Azure DevOps Services or Azure DevOps Server on-prem), a collection URL, one PAT, an optional PAT expiry date, a reminder lead time, the PAT scope from the last test and a list of projects. Each project SHALL have the Azure DevOps project ID and name, a Kurzname unique across all connections (case-insensitive), a write switch, the effective rights per area and a visibility flag. Exactly one project across all connections SHALL be the default.

#### Scenario: Short names are unique
- **WHEN** a project Kurzname `shop` is saved while another project uses `SHOP`
- **THEN** saving is rejected with "Kurzname „SHOP“ wird bereits verwendet"

#### Scenario: Exactly one default
- **WHEN** project B is marked as default while A was default
- **THEN** A loses the default flag and B is the only default

#### Scenario: First connection becomes default
- **WHEN** the first project is saved
- **THEN** it is marked as default

#### Scenario: One PAT per collection
- **WHEN** a connection has three projects
- **THEN** one encrypted PAT is stored for the connection and used for all three

## ADDED Requirements

### Requirement: Kurzname rules
A new or edited Kurzname SHALL have 2 to 10 characters from letters, digits and `-`, start with a letter, not be one of `PR`, `BUG`, `ID`, `TEST`, `TASK`, `ALL`, and be stored in capitals. A stored Kurzname that does not meet these rules SHALL stay valid until it is edited.

#### Scenario: Stored in capitals
- **WHEN** the user enters `ops`
- **THEN** the project is saved with Kurzname `OPS` and `project="ops"` still finds it

#### Scenario: Invalid Kurzname
- **WHEN** the user enters `Demo Project` or `X` or `test`
- **THEN** saving is rejected with "Kurzname: 2–10 Zeichen, Buchstaben, Ziffern und -, beginnt mit Buchstabe" or, for `test`, "„TEST“ ist reserviert"

#### Scenario: Legacy alias kept
- **WHEN** a project migrated from v1 has the alias `Demo_Project_One`
- **THEN** it keeps working as Kurzname until the user edits it

### Requirement: Kurzname suggestion
When a project is set up, the system SHALL suggest a Kurzname that meets the rules, in capitals: the initials of a name with several words (numbers kept whole), otherwise the word itself when it has up to 8 characters, else its first five letters; on a clash a digit SHALL be appended.

#### Scenario: Several words
- **WHEN** the project is called "Demo Project One"
- **THEN** the suggestion is `DPO`

#### Scenario: Short word kept
- **WHEN** the project is called "Sandbox"
- **THEN** the suggestion is `SANDBOX`

#### Scenario: One word with clash
- **WHEN** the project is called "Ops" and `OPS` is already used
- **THEN** the suggestion is `OPS2`
