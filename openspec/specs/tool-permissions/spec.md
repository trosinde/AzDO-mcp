# tool-permissions Specification

## Purpose
Lets the user control which Azure DevOps tools an AI client may run, and whether it must ask first, per tool group, tool and connection.

## Requirements

### Requirement: Permission modes
Modes "Erlaubt", "Nachfragen" and "Gesperrt" SHALL be settable on four levels: Gruppe, Tool, Verbindung and Projekt; on the Verbindung and Projekt levels per group or per tool. The deepest level that has a mode SHALL apply, in this order (deepest first): project+tool, project+group, connection+tool, connection+group, tool, group, default.

#### Scenario: Tool inherits group mode
- **WHEN** group "Git" is "Erlaubt" and `get_commits` has no own mode
- **THEN** `get_commits` runs without approval

#### Scenario: Tool overrides group
- **WHEN** group "Work Items schreiben" is "Nachfragen" and `add_work_item_comment` is "Erlaubt"
- **THEN** adding a comment runs without approval while `update_work_item` still asks

#### Scenario: Project overrides tool
- **WHEN** `update_work_item` is "Nachfragen" and project Musterlab sets "Erlaubt" for it
- **THEN** `update_work_item` in Musterlab runs without approval and still asks in SHOP

### Requirement: Safe defaults
Without a saved configuration, all read groups (Work Items lesen, Git, Test, Planung) SHALL be "Erlaubt" and all write tools "Nachfragen". New projects SHALL start with the write switch off. The System group (list_projects, ping) SHALL always be "Erlaubt" and SHALL NOT be configurable.

#### Scenario: Fresh install
- **WHEN** no permission file exists
- **THEN** `create_work_item` requires approval and `get_work_item` does not

#### Scenario: New project cannot write
- **WHEN** a project was just set up with defaults
- **THEN** write tools for it are blocked until its write switch is turned on

### Requirement: Per-connection exceptions
A project with its write switch off SHALL treat all write tools as "Gesperrt". A tool group whose area has the effective Lesen value "nein" in a project SHALL be "Gesperrt" there; write tools SHALL be "Gesperrt" when the effective Work Items Schreiben value is "nein". The values "n/a" and "?" SHALL NOT restrict. A project marked not visible SHALL reject every call with "kein Zugriff".

#### Scenario: Write access off
- **WHEN** project OPS has its write switch off and `update_work_item` is called with `project="OPS"`
- **THEN** the call is rejected with "Für OPS gesperrt" and nothing is sent to Azure DevOps

#### Scenario: Missing scope
- **WHEN** SHOP's effective Test Lesen is "nein"
- **THEN** `get_test_runs` with `project="SHOP"` is rejected with "403 Scope fehlt" without calling Azure DevOps

#### Scenario: Read-only project
- **WHEN** OPS's Work Items Schreiben is "nein" and its write switch is on
- **THEN** write tools stay blocked for OPS

#### Scenario: Untestable write does not block
- **WHEN** Musterlab's Work Items Schreiben is "n/a" (Security API unavailable) and its write switch is on
- **THEN** write tools follow their mode for Musterlab and Azure DevOps decides on the request

### Requirement: Effective mode order
The effective mode for a call SHALL be the most restrictive of: the mode from the hierarchy, the project write switch, the effective right and visibility. Order of restrictiveness: Gesperrt > Nachfragen > Erlaubt. A mode can restrict the effective right, never extend it.

#### Scenario: Most restrictive wins
- **WHEN** a tool is "Erlaubt" but the project has no access for its area
- **THEN** the call is rejected

### Requirement: Filtered tool listing
`tools/list` SHALL omit tools that are "Gesperrt" for every configured project. A tool blocked only for some projects SHALL be listed, and its description SHALL name the projects where it is unavailable.

#### Scenario: Group blocked globally
- **WHEN** group "Test" is "Gesperrt"
- **THEN** `get_test_runs` and `get_test_results` are absent from `tools/list`

#### Scenario: Listing updates
- **WHEN** the user blocks a group while a client is connected
- **THEN** the server sends `notifications/tools/list_changed`

### Requirement: Blocked call handling
A call to a tool that is "Gesperrt" for the selected project SHALL be rejected with a tool error and logged with result "gesperrt".

#### Scenario: Call to unlisted tool
- **WHEN** a client calls a globally blocked tool by name
- **THEN** the result is an error "Tool gesperrt" and an audit record is written
