## MODIFIED Requirements

### Requirement: Connection selection
Every Azure DevOps tool SHALL accept an optional `project` argument holding a project Kurzname. Its description SHALL list the configured Kurznamen with project names and its schema SHALL list them as allowed values. Without it, the default project SHALL be used. Matching SHALL be case-insensitive and SHALL consider only configured projects; there is no fallback to unconfigured projects of a collection.

#### Scenario: Explicit connection
- **WHEN** a tool is called with `project="shop"` and a project with Kurzname `SHOP` is configured
- **THEN** the call runs against that project with its connection's PAT

#### Scenario: Default connection
- **WHEN** a tool is called without `project`
- **THEN** the call runs against the project marked as default

#### Scenario: Unknown connection
- **WHEN** a tool is called with a `project` that matches no configured Kurzname, even if such a project exists in the collection
- **THEN** the call fails with "Unbekannter Kurzname „X“. Eingerichtet: …" listing the configured Kurznamen

#### Scenario: Kurznamen in the schema
- **WHEN** SHOP and OPS are configured and a client calls `tools/list`
- **THEN** the `project` parameter of `get_work_item` lists `SHOP` and `OPS` as allowed values

### Requirement: Configuration reload
The server SHALL pick up changes to connections and permissions without a restart of the client. When the set of Kurznamen changes, the server SHALL send `notifications/tools/list_changed`.

#### Scenario: Connection added while running
- **WHEN** a connection is added through the Manager while a server instance runs
- **THEN** the next tool call can use the new connection without restarting the server process

#### Scenario: Kurzname renamed while running
- **WHEN** OPS is renamed to `INF` in the Manager while a client is connected
- **THEN** the server sends `notifications/tools/list_changed` and the next `tools/list` lists `INF`

## ADDED Requirements

### Requirement: Kurznamen in server instructions
The instructions the server sends at `initialize` SHALL list every visible configured project as "Kurzname = Projektname (Verbindung)", name the default project, and tell the AI that a named Kurzname means that Azure DevOps project, with an example using the first configured Kurzname. Without configured projects they SHALL say that no project is set up yet. They SHALL contain no PAT and no collection URL.

#### Scenario: Fixed guidance sentence
- **WHEN** SHOP is the first configured project
- **THEN** the instructions contain "Nennt der Nutzer einen dieser Kurznamen, ist dieses Azure-DevOps-Projekt gemeint: die Tools direkt mit project=\"<Kurzname>\" aufrufen, ohne nachzufragen. Beispiel: „gib mir aus SHOP das Work Item 4711“ → get_work_item mit project=\"SHOP\", id=4711."

#### Scenario: Instructions list projects
- **WHEN** SHOP ("Demo Project One", Verbindung tfs, Standard) and OPS are configured
- **THEN** the instructions contain "SHOP = Demo Project One (tfs)", "OPS = …" and "Standardprojekt: SHOP"

#### Scenario: Work item request understood
- **WHEN** the user writes "gib mir aus SHOP das workitem 4711" in a connected client
- **THEN** the instructions tell the AI to call `get_work_item` with `project="SHOP"` and `id=4711` without asking which system is meant

#### Scenario: Hidden project left out
- **WHEN** QA-Automation is marked not visible
- **THEN** the instructions do not list it
