# Spec Delta

## MODIFIED Requirements

### Requirement: Connection definition
A connection SHALL consist of a unique name, a server type (Azure DevOps Services or Azure DevOps Server on-prem), a collection URL, one PAT, an optional PAT expiry date, a reminder lead time, the PAT scope from the last test and a list of projects. Each project SHALL have the Azure DevOps project ID and name, an alias unique across all connections, a write switch, the effective rights per area and a visibility flag. Exactly one project across all connections SHALL be the default.

#### Scenario: Short names are unique
- **WHEN** a project alias `shop` is saved while another project uses `SHOP`
- **THEN** saving is rejected with a message that the alias is already used

#### Scenario: Exactly one default
- **WHEN** project B is marked as default while A was default
- **THEN** A loses the default flag and B is the only default

#### Scenario: First connection becomes default
- **WHEN** the first project is saved
- **THEN** it is marked as default

#### Scenario: One PAT per collection
- **WHEN** a connection has three projects
- **THEN** one encrypted PAT is stored for the connection and used for all three

### Requirement: Connection test
The system SHALL test a connection on request and report: server reachable and API version, authenticated user, PAT expiry warning, and the PAT scope per area (Work Items, Code, Test, Iterationen) as Schreiben / nur Lesen / kein Zugriff. The Work Items write scope SHALL be probed only with `validateOnly=true`. The test SHALL NOT need a project to be selected.

#### Scenario: All scopes present
- **WHEN** the test runs with a valid PAT that has all scopes
- **THEN** login and server report "OK" and every area shows its scope

#### Scenario: Missing scope
- **WHEN** the PAT lacks the Test Management scope
- **THEN** the area "Test" shows "kein Zugriff" and test tools are blocked for all projects of the connection

#### Scenario: Server unreachable
- **WHEN** the collection URL cannot be reached within 15 seconds
- **THEN** the test reports "Server nicht erreichbar" and does not reveal the PAT

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

## ADDED Requirements

### Requirement: Schema migration
On first read of a schema-1 `connections.json` the system SHALL migrate each old connection to a connection with one project (alias = old short name, write switch = old "Schreibzugriff zulassen", default flag kept), keep its PAT file, and write schema 2. Migration SHALL run once and never lose settings.

#### Scenario: Migrate one entry
- **WHEN** schema 1 holds connection SHOP with write access on and default flag set
- **THEN** schema 2 holds one connection with project alias SHOP, write switch on, default, and the PAT is still readable
