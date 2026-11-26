## ADDED Requirements

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
