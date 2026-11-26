# Spec Delta

## Purpose

Finds all Azure DevOps projects a PAT can reach in one collection, determines what the MCP server may do in each, and keeps that picture up to date without ever overwriting the user's settings.

## ADDED Requirements

### Requirement: Login check
Discovery SHALL start with `GET {collection}/_apis/connectionData` and report the authenticated user's ID and display name. A 401 SHALL stop discovery with "401 PAT abgelaufen oder ungültig".

#### Scenario: Valid PAT
- **WHEN** discovery runs with a valid PAT
- **THEN** the result names the signed-in user

#### Scenario: Expired PAT
- **WHEN** `connectionData` answers 401
- **THEN** discovery stops with the 401 message and no project is listed

### Requirement: Project listing with paging
Discovery SHALL list every project visible to the PAT via `GET {collection}/_apis/projects`, following `continuationToken` (response header `x-ms-continuationtoken`) until no further page exists.

#### Scenario: Three pages
- **WHEN** the server returns projects in three pages
- **THEN** all projects of all pages are listed exactly once

### Requirement: PAT scope per area
Discovery SHALL determine, for the whole collection, the PAT scope per area Work Items, Code, Test and Iterationen as "Schreiben", "nur Lesen" or "kein Zugriff", using read requests and, for Work Items writing, a create request with `validateOnly=true` that persists nothing. No other write request SHALL be sent.

#### Scenario: Read-only PAT
- **WHEN** reads succeed but the `validateOnly` create answers 403
- **THEN** Work Items scope is "nur Lesen"

#### Scenario: No write request against data
- **WHEN** discovery runs
- **THEN** the only non-GET work item request carries `validateOnly=true`

### Requirement: Project rights
Discovery SHALL read the signed-in user's rights per project through the Security API (`_apis/securitynamespaces`, `_apis/permissions`) for: work items read/edit (area root), Git read/contribute, test results view and test plan management. Permission bits SHALL be resolved by action name from the namespace list. When the Security API is unavailable, per-project read probes SHALL be used instead.

#### Scenario: Permissions resolved by name
- **WHEN** the namespace list returns action "WORK_ITEM_WRITE" with bit 32
- **THEN** that bit is used for the work item edit check

#### Scenario: Security API forbidden
- **WHEN** `_apis/permissions` answers 403
- **THEN** rights fall back to read probes per project and are at most "nur Lesen" for areas without a write probe

### Requirement: Effective right
The effective right per project and area SHALL be the lower of PAT scope and project right (kein Zugriff < nur Lesen < Schreiben).

#### Scenario: PAT limits
- **WHEN** the project grants Git contribute but the PAT scope for Code is "nur Lesen"
- **THEN** the effective Code right is "nur Lesen"

#### Scenario: Project limits
- **WHEN** the PAT can write work items but the user may only read work items in OPS
- **THEN** the effective Work Items right in OPS is "nur Lesen"

### Requirement: Merge without overwriting
Applying a discovery SHALL add selected new projects with defaults, update stored effective rights and visibility of existing projects, and SHALL NEVER change an existing project's alias, default flag, write switch or permission settings.

#### Scenario: Existing project kept
- **WHEN** SHOP is already configured with write switch on and a new discovery is applied
- **THEN** SHOP keeps its alias, write switch and modes; only its stored rights are refreshed

### Requirement: Rescan diff
A rescan SHALL compare with the stored projects and report new projects, projects no longer visible, projects with changed rights (old → new per area) and unchanged projects. Projects no longer visible SHALL be marked and kept, never deleted automatically.

#### Scenario: Project disappeared
- **WHEN** QA-Automation is configured but no longer returned
- **THEN** it is reported under "Nicht mehr sichtbar", stays configured, and its tools answer "kein Zugriff"

#### Scenario: More rights never enable writing
- **WHEN** OPS's Work Items right changes from "nur Lesen" to "Schreiben"
- **THEN** the change is reported and OPS's write switch stays off

#### Scenario: Fewer rights lock immediately
- **WHEN** Musterlab's Test right drops to "kein Zugriff" and the rescan is applied
- **THEN** test tools are blocked for Musterlab

### Requirement: Discover command
`azdo-mcp discover <verbindung>` SHALL print the discovery result (project, rights per area, status) for the named connection without changing anything; with `--apply` it SHALL apply it with the merge rules and add all new projects with defaults. It SHALL exit with 0 on success, 1 on errors and never print the PAT.

#### Scenario: Dry run
- **WHEN** `azdo-mcp discover tfs` runs
- **THEN** the table is printed and `connections.json` is unchanged

#### Scenario: Unknown connection
- **WHEN** the named connection does not exist
- **THEN** the command lists the known connection names and exits with 1

### Requirement: Secrets stay secret
Discovery results, logs, the CLI output and `connections.json` SHALL NOT contain the PAT.

#### Scenario: Scan for PAT
- **WHEN** discovery was applied and the CLI was run
- **THEN** neither the data folder nor the CLI output contains the PAT
