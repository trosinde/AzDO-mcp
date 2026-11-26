## MODIFIED Requirements

### Requirement: System tools
`list_projects` SHALL list only configured projects with alias, connection name, server host, Azure DevOps project name, default flag, write switch, the effective right per area as `read` and `write` with the values "ja", "nein", "n/a" or "?", visibility and PAT expiry status, without secrets. `ping` SHALL check reachability and authentication of the connection of one project.

#### Scenario: List projects hides PATs
- **WHEN** `list_projects` is called
- **THEN** the result contains aliases, projects and rights but no PAT

#### Scenario: Only configured projects
- **WHEN** the collection has seven projects and three are configured
- **THEN** `list_projects` returns exactly the three configured projects

#### Scenario: Rights per column
- **WHEN** SHOP has Code Lesen "ja" and Code Schreiben "n/a"
- **THEN** its entry contains `"Code": { "read": "ja", "write": "n/a" }`
