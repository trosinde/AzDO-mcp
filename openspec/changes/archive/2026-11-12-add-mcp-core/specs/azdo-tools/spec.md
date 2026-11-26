# Spec Delta

## Purpose

Defines the Azure DevOps operations the MCP server offers to AI clients, grouped as in the "Tools & Rechte" page.

## ADDED Requirements

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
`list_projects` SHALL list the configured connections (short name, server host, project, default flag, expiry status) without secrets. `ping` SHALL check reachability and authentication of one connection.

#### Scenario: List projects hides PATs
- **WHEN** `list_projects` is called
- **THEN** the result contains short names and projects but no PAT

### Requirement: API compatibility
Tools SHALL work against Azure DevOps Services and Azure DevOps Server 2020 or newer using REST API versions supported by both; when an endpoint is unavailable on the server, the tool SHALL report "Auf diesem Server nicht verfügbar".

#### Scenario: Older on-prem server
- **WHEN** an endpoint returns 404 for the requested API version on an on-prem server
- **THEN** the tool result is an error saying the function is not available on this server
