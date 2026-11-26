## MODIFIED Requirements

### Requirement: Safe defaults
Without a saved configuration, all read groups (Work Items lesen, Git, Pipelines, Test, Planung) SHALL be "Erlaubt" and all write tools "Nachfragen". New projects SHALL start with the write switch off. The System group (list_projects, ping) SHALL always be "Erlaubt" and SHALL NOT be configurable.

#### Scenario: Fresh install
- **WHEN** no permission file exists
- **THEN** `create_work_item` requires approval and `get_work_item` does not

#### Scenario: New project cannot write
- **WHEN** a project was just set up with defaults
- **THEN** write tools for it are blocked until its write switch is turned on

#### Scenario: Existing configuration gains Pipelines
- **WHEN** a saved permission file has no entry for the Pipelines group
- **THEN** the Pipelines group is "Erlaubt"

## ADDED Requirements

### Requirement: Tools reading several areas
A tool that reads data of other areas than its own group (`trace_links`, `get_pull_requests` with `include=workItems`, `get_test_runs`, `get_test_results` and `analyze_test_results` with the `tags` filter) SHALL be governed by the mode of its own group and SHALL leave out the data of every other area whose effective Lesen value is "nein". The result SHALL name each left-out area in `notes`.

#### Scenario: No test read right
- **WHEN** `trace_links` with `include=testResults` runs in a project whose Test Lesen is "nein"
- **THEN** the result has no test nodes and `notes` contains "Test: kein Lesezugriff – ausgelassen"

#### Scenario: Tags filter without Pipelines right
- **WHEN** `get_test_runs` is called with `tags=nightly` in a project whose Pipelines Lesen is "nein"
- **THEN** the call returns an error that the tags filter needs Pipelines Lesen

#### Scenario: Untested right does not restrict
- **WHEN** Pipelines Lesen is "?" in the project
- **THEN** `get_test_runs` with a tags filter still runs
