# Spec Delta

## MODIFIED Requirements

### Requirement: System tools
`list_projects` SHALL list only configured projects with alias, connection name, server host, Azure DevOps project name, default flag, write switch, effective right per area, visibility and PAT expiry status, without secrets. `ping` SHALL check reachability and authentication of the connection of one project.

#### Scenario: List projects hides PATs
- **WHEN** `list_projects` is called
- **THEN** the result contains aliases, projects and rights but no PAT

#### Scenario: Only configured projects
- **WHEN** the collection has seven projects and three are configured
- **THEN** `list_projects` returns exactly the three configured projects
