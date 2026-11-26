# azdo-tools Specification

## Purpose
Defines the Azure DevOps operations the MCP server offers to AI clients, grouped as in the "Tools & Rechte" page.

## Requirements

### Requirement: Tool set and groups
The server SHALL provide 22 tools, each in exactly one of six groups (Work Items lesen, Work Items schreiben, Git, Test, Planung, System), and SHALL mark every tool of "Work Items schreiben" as writing.

#### Scenario: Group membership is fixed
- **WHEN** the tool catalogue is queried
- **THEN** the groups contain exactly: Work Items lesen = get_work_item, query_by_id, execute_wiql, query_work_items, get_work_items_with_relations, get_linked_work_items, get_work_item_revisions; Work Items schreiben = create_work_item, update_work_item, add_work_item_comment, update_work_item_comment, add_work_item_relation, add_work_item_attachment; Git = get_repositories, get_commits, get_pull_requests; Test = get_test_runs, get_test_results; Planung = get_iterations, get_iteration_capacity; System = list_projects, ping

### Requirement: Work item read tools
Read tools SHALL return work item data as JSON text: by ID (optionally selected fields, optionally with relations), by saved query ID, by WIQL, by simple filter (type, state, assigned to, area, iteration, top), linked items of an item, and the revision history of an item.

#### Scenario: Get work item
- **WHEN** `get_work_item` is called with `id=123`
- **THEN** the result contains the ID, type, title, state and requested fields of item 123

#### Scenario: WIQL with many results
- **WHEN** `execute_wiql` matches more items than the limit (default 200)
- **THEN** the first items up to the limit are returned with a note how many were omitted

### Requirement: Work item write tools
Write tools SHALL create items (Bug, Task, User Story, Feature, Epic and any other type of the project), update arbitrary fields including state and assignment, add and edit comments, add relations and add file attachments. Each SHALL return the resulting item ID and a link.

#### Scenario: Create work item
- **WHEN** `create_work_item` is called with type `Task` and a title
- **THEN** a Task is created and its ID and URL are returned

#### Scenario: Update with invalid field
- **WHEN** `update_work_item` sets an unknown field reference
- **THEN** the tool returns an error naming the field and nothing is changed

#### Scenario: Attachment size limit
- **WHEN** `add_work_item_attachment` gets a file larger than 60 MB
- **THEN** the tool returns an error before uploading

### Requirement: Git, test and planning read tools
The server SHALL list repositories, commits of a repository (optional branch, author, date range, top) and pull requests (optional status, repository), test runs and their results, iterations of a team and iteration capacity.

#### Scenario: Pull requests by status
- **WHEN** `get_pull_requests` is called with `status=active`
- **THEN** only active pull requests are returned

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

### Requirement: API compatibility
Tools SHALL work against Azure DevOps Services and Azure DevOps Server 2020 or newer using REST API versions supported by both; when an endpoint is unavailable on the server, the tool SHALL report "Auf diesem Server nicht verfügbar".

#### Scenario: Older on-prem server
- **WHEN** an endpoint returns 404 for the requested API version on an on-prem server
- **THEN** the tool result is an error saying the function is not available on this server

### Requirement: Work items stay in their project
Every read or write tool that takes work item IDs SHALL only return or change work items whose project is the selected project. A single ID in another project SHALL fail before any approval request or write; for several IDs, items of other projects SHALL be left out with a note. Linked items and the relation target of `add_work_item_relation` are not restricted.

#### Scenario: Work item by Kurzname
- **WHEN** the user writes "gib mir aus SHOP das Work Item 4711" and 4711 lies in SHOP
- **THEN** `get_work_item` with `project="SHOP"` and `id=4711` returns the item

#### Scenario: Item of another project
- **WHEN** `get_work_item` is called with `project="SHOP"` and `id=5000`, and 5000 lies in OPS
- **THEN** the call fails and the audit log records "abgelehnt"

#### Scenario: No write across projects
- **WHEN** `update_work_item` is called with `project="SHOP"` for an item in OPS, where writing is blocked
- **THEN** no approval window opens and nothing is changed in Azure DevOps

#### Scenario: Batch with foreign items
- **WHEN** `get_work_items_with_relations` gets IDs 1, 2 and 3 and item 3 lies in another project
- **THEN** items 1 and 2 are returned with the note "Nicht in SHOP, ausgelassen: 3"

### Requirement: Foreign work item message
The error for a work item of another project SHALL read "Work Item <ID> gehört nicht zu <Kurzname>" and SHALL add "– es liegt in <Kurzname>" only when the owning project is configured; an unconfigured project SHALL NOT be named.

#### Scenario: Owner configured
- **WHEN** item 5000 lies in the configured project OPS and is requested with `project="SHOP"`
- **THEN** the message is "Work Item 5000 gehört nicht zu SHOP – es liegt in OPS"

#### Scenario: Owner not configured
- **WHEN** item 5000 lies in a project that is not configured
- **THEN** the message is "Work Item 5000 gehört nicht zu SHOP"
