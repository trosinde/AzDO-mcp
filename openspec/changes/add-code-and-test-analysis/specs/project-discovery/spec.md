## MODIFIED Requirements

### Requirement: PAT scope per area
Discovery SHALL determine, for the whole collection, the PAT scope per area as Lesen and Schreiben check values. Lesen SHALL come from read requests (for Pipelines: a build list with one entry): success is "ja", 401/403 is "nein", any other failure is "n/a". Work Items Schreiben SHALL come from a create request with `validateOnly=true` that persists nothing: 401/403 is "nein", otherwise "ja". Other Schreiben values SHALL be "n/a". No other write request SHALL be sent.

#### Scenario: Read-only PAT
- **WHEN** reads succeed but the `validateOnly` create answers 403
- **THEN** Work Items is Lesen "ja", Schreiben "nein"

#### Scenario: Full-access PAT
- **WHEN** all reads and the `validateOnly` create succeed
- **THEN** Work Items is "ja"/"ja" and Code, Pipelines, Test and Iterationen are Lesen "ja", Schreiben "n/a"

#### Scenario: Probe where the user has rights
- **WHEN** the first project in the collection gives the user no work item access and another one does
- **THEN** the PAT scope is probed in the other project, so a missing project right is not reported as a PAT limit

#### Scenario: No write request against data
- **WHEN** discovery runs
- **THEN** the only non-GET work item request carries `validateOnly=true`

### Requirement: Project rights
Discovery SHALL read the signed-in user's rights per project through the Security API (`_apis/securitynamespaces`, `_apis/permissions`) for: work items read/edit (area root), Git read/contribute, builds view, test results view and test plan management. Permission bits SHALL be resolved by action name from the namespace list. When the Security API is unavailable, per-project read probes SHALL be used instead.

#### Scenario: Permissions resolved by name
- **WHEN** the namespace list returns action "WORK_ITEM_WRITE" with bit 32
- **THEN** that bit is used for the work item edit check

#### Scenario: Builds view denied
- **WHEN** the Build namespace answers ViewBuilds = false for SHOP
- **THEN** SHOP's project right for Pipelines Lesen is "nein"

### Requirement: Check values
Every right SHALL be stored and shown per area (Work Items, Code, Pipelines, Test, Iterationen) as two separate values, Lesen and Schreiben, each with exactly one check value: "ja" (checked, allowed), "nein" (checked, denied), "n/a" (cannot be checked without really writing) or "?" (not checked yet). No value SHALL be shown as a restriction unless it was checked and denied.

#### Scenario: Untestable write is not a restriction
- **WHEN** writing in Code cannot be checked without a real write
- **THEN** Code Schreiben is "n/a" and not "nein" or "nur Lesen"

#### Scenario: Never checked
- **WHEN** a migrated project was never discovered
- **THEN** its values are "?" until the next discovery

#### Scenario: New area on existing data
- **WHEN** a project was discovered before the Pipelines area existed
- **THEN** its Pipelines values are "?" and all other stored values are unchanged until the next discovery
