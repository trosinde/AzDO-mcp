## MODIFIED Requirements

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

### Requirement: Schema migration
On first read of a schema-1 `connections.json` the system SHALL migrate each old connection to a connection with one project (alias = old short name, write switch = old "Schreibzugriff zulassen", default flag kept), keep its PAT file, and write schema 3. On first read of a schema-2 file the system SHALL convert every stored right to Lesen/Schreiben ("Schreiben" → ja/ja, "nur Lesen" → ja/n/a except Work Items → ja/nein, so that no write becomes possible before the next discovery, "kein Zugriff" → nein/nein), keep a backup `connections.v2.bak.json` and write schema 3. Migration SHALL run once and never lose settings.

#### Scenario: Migrate one entry
- **WHEN** schema 1 holds connection SHOP with write access on and default flag set
- **THEN** schema 3 holds one connection with project alias SHOP, write switch on, default, and the PAT is still readable

#### Scenario: Migrate schema 2 rights
- **WHEN** schema 2 stores the PAT scope Code "nur Lesen" and Work Items "nur Lesen"
- **THEN** schema 3 stores Code Lesen "ja", Schreiben "n/a" and Work Items Lesen "ja", Schreiben "nein", and `connections.v2.bak.json` holds the old file
