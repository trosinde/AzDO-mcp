## ADDED Requirements

### Requirement: Check values
Every right SHALL be stored and shown per area (Work Items, Code, Test, Iterationen) as two separate values, Lesen and Schreiben, each with exactly one check value: "ja" (checked, allowed), "nein" (checked, denied), "n/a" (cannot be checked without really writing) or "?" (not checked yet). No value SHALL be shown as a restriction unless it was checked and denied.

#### Scenario: Untestable write is not a restriction
- **WHEN** writing in Code cannot be checked without a real write
- **THEN** Code Schreiben is "n/a" and not "nein" or "nur Lesen"

#### Scenario: Never checked
- **WHEN** a migrated project was never discovered
- **THEN** its values are "?" until the next discovery

## MODIFIED Requirements

### Requirement: PAT scope per area
Discovery SHALL determine, for the whole collection, the PAT scope per area as Lesen and Schreiben check values. Lesen SHALL come from read requests: success is "ja", 401/403 is "nein", any other failure is "n/a". Work Items Schreiben SHALL come from a create request with `validateOnly=true` that persists nothing: 401/403 is "nein", otherwise "ja". Schreiben for Code, Test and Iterationen SHALL be "n/a". No other write request SHALL be sent.

#### Scenario: Read-only PAT
- **WHEN** reads succeed but the `validateOnly` create answers 403
- **THEN** Work Items is Lesen "ja", Schreiben "nein"

#### Scenario: Full-access PAT
- **WHEN** all reads and the `validateOnly` create succeed
- **THEN** Work Items is "ja"/"ja" and Code, Test and Iterationen are Lesen "ja", Schreiben "n/a"

#### Scenario: No write request against data
- **WHEN** discovery runs
- **THEN** the only non-GET work item request carries `validateOnly=true`

### Requirement: Project rights
Discovery SHALL read the signed-in user's rights per project through the Security API (`_apis/securitynamespaces`, `_apis/permissions`) for: work items read/edit (area root), Git read/contribute, test results view and test plan management. Permission bits SHALL be resolved by action name from the namespace list. An answered permission SHALL give "ja" or "nein"; a permission the server does not offer, and Iterationen Schreiben, SHALL be "n/a". When the Security API is unavailable, per-project read probes SHALL be used instead, giving Lesen "ja"/"nein" and Schreiben "n/a".

#### Scenario: Permissions resolved by name
- **WHEN** the namespace list returns action "WORK_ITEM_WRITE" with bit 32
- **THEN** that bit is used for the work item edit check

#### Scenario: Contribute denied
- **WHEN** the Security API answers GenericContribute = false for SHOP
- **THEN** SHOP's project right for Code Schreiben is "nein"

#### Scenario: Security API forbidden
- **WHEN** `_apis/permissions` answers 403
- **THEN** rights fall back to read probes per project and every Schreiben value is "n/a"

### Requirement: Effective right
The effective right per project, area and column (Lesen, Schreiben) SHALL combine PAT scope and project right: "nein" if either is "nein"; otherwise "?" if either is "?"; otherwise "n/a" if either is "n/a"; otherwise "ja".

#### Scenario: PAT limits
- **WHEN** the project grants Git contribute and the PAT's Code Schreiben is "n/a"
- **THEN** the effective Code Schreiben is "n/a"

#### Scenario: Project limits
- **WHEN** the PAT can write work items but the user may not edit work items in OPS
- **THEN** the effective Work Items Schreiben in OPS is "nein"

#### Scenario: Denied beats untestable
- **WHEN** the PAT's Code Schreiben is "n/a" and the project right for Code Schreiben is "nein"
- **THEN** the effective Code Schreiben is "nein"

### Requirement: Rescan diff
A rescan SHALL compare with the stored projects and report new projects, projects no longer visible, projects with changed rights and unchanged projects. A right SHALL count as changed only when a value becomes "nein" or stops being "nein" (shown as "<Bereich> <Lesen|Schreiben>: alt → neu"); other changes between "ja", "n/a" and "?" SHALL be stored silently. Projects no longer visible SHALL be marked and kept, never deleted automatically.

#### Scenario: Project disappeared
- **WHEN** QA-Automation is configured but no longer returned
- **THEN** it is reported under "Nicht mehr sichtbar", stays configured, and its tools answer "kein Zugriff"

#### Scenario: Untestable change not reported
- **WHEN** SHOP's Code Schreiben changes from "?" to "n/a"
- **THEN** SHOP is listed as unchanged and the new value is stored on apply

#### Scenario: More rights never enable writing
- **WHEN** OPS's Work Items Schreiben changes from "nein" to "ja"
- **THEN** the change is reported and OPS's write switch stays off

#### Scenario: Fewer rights lock immediately
- **WHEN** Musterlab's Test Lesen drops to "nein" and the rescan is applied
- **THEN** test tools are blocked for Musterlab

### Requirement: Discover command
`azdo-mcp discover <verbindung>` SHALL print the discovery result (project, Lesen/Schreiben per area, status) for the named connection without changing anything; with `--apply` it SHALL apply it with the merge rules and add all new projects with defaults. It SHALL exit with 0 on success, 1 on errors and never print the PAT.

#### Scenario: Dry run
- **WHEN** `azdo-mcp discover tfs` runs
- **THEN** the table with Lesen/Schreiben per area is printed and `connections.json` is unchanged

#### Scenario: Unknown connection
- **WHEN** the named connection does not exist
- **THEN** the command lists the known connection names and exits with 1
