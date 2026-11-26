## MODIFIED Requirements

### Requirement: Connection test
The system SHALL test a connection on request and report: server reachable and API version, authenticated user, PAT expiry warning, and the PAT scope per area (Work Items, Code, Pipelines, Test, Iterationen) as Lesen and Schreiben check values ("ja", "nein", "n/a"). The Work Items write scope SHALL be probed only with `validateOnly=true`. The test SHALL NOT need a project to be selected.

#### Scenario: All scopes present
- **WHEN** the test runs with a valid PAT that has all scopes
- **THEN** login and server report "OK", Work Items shows Lesen "ja" and Schreiben "ja", and Code, Pipelines, Test and Iterationen show Lesen "ja" and Schreiben "n/a"

#### Scenario: Missing scope
- **WHEN** the PAT lacks the Test Management scope
- **THEN** the area "Test" shows Lesen "nein" and test tools are blocked for all projects of the connection

#### Scenario: Missing build scope
- **WHEN** the PAT lacks the Build scope
- **THEN** the area "Pipelines" shows Lesen "nein" and the Pipelines tools are blocked for all projects of the connection

#### Scenario: Server unreachable
- **WHEN** the collection URL cannot be reached within 15 seconds
- **THEN** the test reports "Server nicht erreichbar" and does not reveal the PAT
