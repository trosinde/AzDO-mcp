# Spec Delta

## Purpose

Lets the user control which Azure DevOps tools an AI client may run, and whether it must ask first, per tool group, tool and connection.

## ADDED Requirements

### Requirement: Permission modes
Each tool group and each write tool SHALL have one mode: "Erlaubt" (runs immediately), "Nachfragen" (requires approval per call) or "Gesperrt" (not offered to the AI). A tool without its own mode SHALL inherit its group's mode.

#### Scenario: Tool inherits group mode
- **WHEN** group "Git" is "Erlaubt" and `get_commits` has no own mode
- **THEN** `get_commits` runs without approval

#### Scenario: Tool overrides group
- **WHEN** group "Work Items schreiben" is "Nachfragen" and `add_work_item_comment` is "Erlaubt"
- **THEN** adding a comment runs without approval while `update_work_item` still asks

### Requirement: Safe defaults
Without a saved configuration, all read groups (Work Items lesen, Git, Test, Planung) SHALL be "Erlaubt" and all write tools "Nachfragen". The System group (list_projects, ping) SHALL always be "Erlaubt" and SHALL NOT be configurable.

#### Scenario: Fresh install
- **WHEN** no permission file exists
- **THEN** `create_work_item` requires approval and `get_work_item` does not

### Requirement: Per-connection exceptions
A tool group SHALL be blockable for single connections. A connection with "Schreibzugriff zulassen" switched off SHALL treat all write tools as "Gesperrt". A group whose PAT scope was reported missing by the last connection test SHALL be treated as "Gesperrt" for that connection.

#### Scenario: Write access off
- **WHEN** connection OPS has write access off and `update_work_item` is called with `project="OPS"`
- **THEN** the call is rejected with "Für OPS gesperrt" and nothing is sent to Azure DevOps

#### Scenario: Missing scope
- **WHEN** the last test of SHOP reported 403 for Test Management
- **THEN** `get_test_runs` with `project="SHOP"` is rejected with "403 Scope fehlt" without calling Azure DevOps

### Requirement: Effective mode order
The effective mode for a call SHALL be the most restrictive of: tool mode (or inherited group mode), connection exception, connection write switch, scope gap. Order of restrictiveness: Gesperrt > Nachfragen > Erlaubt.

#### Scenario: Most restrictive wins
- **WHEN** a tool is "Erlaubt" but blocked for the selected connection
- **THEN** the call is rejected

### Requirement: Filtered tool listing
`tools/list` SHALL omit tools that are "Gesperrt" for every configured connection. A tool blocked only for some connections SHALL be listed, and its description SHALL name the connections where it is unavailable.

#### Scenario: Group blocked globally
- **WHEN** group "Test" is "Gesperrt"
- **THEN** `get_test_runs` and `get_test_results` are absent from `tools/list`

#### Scenario: Listing updates
- **WHEN** the user blocks a group while a client is connected
- **THEN** the server sends `notifications/tools/list_changed`

### Requirement: Blocked call handling
A call to a tool that is "Gesperrt" for the selected connection SHALL be rejected with a tool error and logged with result "gesperrt".

#### Scenario: Call to unlisted tool
- **WHEN** a client calls a globally blocked tool by name
- **THEN** the result is an error "Tool gesperrt" and an audit record is written
