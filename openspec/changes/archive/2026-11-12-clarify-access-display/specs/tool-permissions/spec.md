## MODIFIED Requirements

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
