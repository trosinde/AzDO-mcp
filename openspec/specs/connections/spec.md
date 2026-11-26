# connections Specification

## Purpose
Defines how Azure DevOps connections (server, collection, project, PAT) are stored per user, protected and tested.

## Requirements

### Requirement: Connection definition
A connection SHALL consist of a unique name, a server type (Azure DevOps Services or Azure DevOps Server on-prem), a collection URL, one PAT, an optional PAT expiry date, a reminder lead time, the PAT scope from the last test and a list of projects. Exactly one project across all connections SHALL be the default.

#### Scenario: Exactly one default
- **WHEN** project B is marked as default while A was default
- **THEN** A loses the default flag and B is the only default

#### Scenario: First connection becomes default
- **WHEN** the first project is saved
- **THEN** it is marked as default

#### Scenario: One PAT per collection
- **WHEN** a connection has three projects
- **THEN** one encrypted PAT is stored for the connection and used for all three

### Requirement: Project definition
Each project SHALL have the Azure DevOps project ID and name, a Kurzname unique across all connections (case-insensitive), a write switch, the effective rights per area and a visibility flag.

#### Scenario: Short names are unique
- **WHEN** a project Kurzname `shop` is saved while another project uses `SHOP`
- **THEN** saving is rejected with "Kurzname „SHOP“ wird bereits verwendet"

### Requirement: Per-user storage location
Connections SHALL be stored under `%LOCALAPPDATA%\AzdoMcpData\` of the current user. No file SHALL be written outside the user profile and no admin rights SHALL be required.

#### Scenario: Fresh profile
- **WHEN** the server or Manager runs for the first time
- **THEN** the folder `%LOCALAPPDATA%\AzdoMcpData\` is created with normal user rights

### Requirement: PAT protection
PATs SHALL be stored only encrypted with Windows DPAPI in CurrentUser scope. The connection file SHALL contain no PAT. After saving, a PAT SHALL NOT be displayable again; it can only be replaced.

#### Scenario: Stored PAT is not readable as text
- **WHEN** a connection with a PAT is saved
- **THEN** no file under `%LOCALAPPDATA%\AzdoMcpData\` contains the PAT in plain text

#### Scenario: Other user cannot decrypt
- **WHEN** the encrypted PAT is copied to another Windows account
- **THEN** it cannot be decrypted there and the connection reports "PAT nicht lesbar – bitte ersetzen"

#### Scenario: Replace PAT
- **WHEN** the user chooses "Ersetzen" and enters a new PAT
- **THEN** the old encrypted PAT is overwritten and the expiry date can be updated

### Requirement: Connection test
The system SHALL test a connection on request and report: server reachable and API version, authenticated user, PAT expiry warning, and the PAT scope per area (Work Items, Code, Test, Iterationen) as Lesen and Schreiben check values ("ja", "nein", "n/a"). The Work Items write scope SHALL be probed only with `validateOnly=true`. The test SHALL NOT need a project to be selected.

#### Scenario: All scopes present
- **WHEN** the test runs with a valid PAT that has all scopes
- **THEN** login and server report "OK", Work Items shows Lesen "ja" and Schreiben "ja", and Code, Test and Iterationen show Lesen "ja" and Schreiben "n/a"

#### Scenario: Missing scope
- **WHEN** the PAT lacks the Test Management scope
- **THEN** the area "Test" shows Lesen "nein" and test tools are blocked for all projects of the connection

#### Scenario: Server unreachable
- **WHEN** the collection URL cannot be reached within 15 seconds
- **THEN** the test reports "Server nicht erreichbar" and does not reveal the PAT

### Requirement: PAT expiry status
The system SHALL classify each connection by PAT expiry: green when more than the reminder lead time remains, yellow ("in N Tagen", "PAT erneuern") within the lead time, red ("abgelaufen") after expiry or after a 401 from the last call or test.

#### Scenario: Expiring soon
- **WHEN** the PAT expires in 4 days and the lead time is 7 days
- **THEN** the status is yellow with "in 4 Tagen"

#### Scenario: Expired
- **WHEN** the expiry date has passed
- **THEN** the status is red with "abgelaufen"

### Requirement: Import of v1 configuration
The system SHALL detect a v1 `config.json` with plain-text PATs, offer to import it, and create one connection per collection and PAT with one project per v1 entry (alias = v1 name), encrypt each PAT once and then delete the plain-text file. The import SHALL NOT proceed partially.

#### Scenario: Successful import
- **WHEN** the user imports a v1 file with three entries for one collection and one PAT
- **THEN** one connection with three projects exists, the PAT is encrypted once, and the v1 file no longer exists

#### Scenario: Import fails
- **WHEN** one entry cannot be stored
- **THEN** nothing from that file is kept and the v1 file is left untouched

#### Scenario: Name clash
- **WHEN** an imported alias already exists
- **THEN** the imported project gets a suffix (`SHOP-2`) and the user is told

### Requirement: Schema migration
On first read of a schema-1 `connections.json` the system SHALL migrate each old connection to a connection with one project (alias = old short name, write switch = old "Schreibzugriff zulassen", default flag kept), keep its PAT file, and write schema 3. Migration SHALL run once and never lose settings.

#### Scenario: Migrate one entry
- **WHEN** schema 1 holds connection SHOP with write access on and default flag set
- **THEN** schema 3 holds one connection with project alias SHOP, write switch on, default, and the PAT is still readable

### Requirement: Rights migration from schema 2
On first read of a schema-2 file the system SHALL convert every stored right to Lesen/Schreiben ("Schreiben" → ja/ja, "nur Lesen" → ja/n/a except Work Items → ja/nein, so that no write becomes possible before the next discovery, "kein Zugriff" → nein/nein), keep a backup `connections.v2.bak.json` and write schema 3, once.

#### Scenario: Migrate schema 2 rights
- **WHEN** schema 2 stores the PAT scope Code "nur Lesen" and Work Items "nur Lesen"
- **THEN** schema 3 stores Code Lesen "ja", Schreiben "n/a" and Work Items Lesen "ja", Schreiben "nein", and `connections.v2.bak.json` holds the old file

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
