# audit-log Specification

## Purpose
Records every tool call so the user can see what an AI client did with their Azure DevOps access ("Protokoll" page).

## Requirements

### Requirement: Call records
Every tool call SHALL produce one record with time, client name (from MCP `clientInfo`), tool, connection short name, duration and result (OK, error code with short text, "abgelehnt", "wartet auf Freigabe"). Records SHALL be appended to a per-day file under `%LOCALAPPDATA%\AzdoMcpData\logs\`.

#### Scenario: Successful call logged
- **WHEN** Claude Code calls `get_work_item` on SHOP and it succeeds in 0.2 s
- **THEN** a record with client "Claude Code", tool `get_work_item`, connection `SHOP`, duration 0.2 s and result OK is written

#### Scenario: Concurrent servers
- **WHEN** two server instances log at the same time
- **THEN** both records are written completely and no line is interleaved

### Requirement: No sensitive content in logs
Records SHALL NOT contain PATs, Authorization headers, field values, comment texts or attachment contents. Work item IDs MAY be logged.

#### Scenario: Comment text not logged
- **WHEN** `add_work_item_comment` is called with a comment text
- **THEN** the record contains the work item ID but not the comment text

### Requirement: Retention
Log files older than 30 days SHALL be deleted automatically.

#### Scenario: Old file removed
- **WHEN** a server or the Manager starts and a 31-day-old log file exists
- **THEN** that file is deleted

### Requirement: Export
The system SHALL export the filtered records as CSV to a location the user chooses.

#### Scenario: Export filtered
- **WHEN** the user filters by connection SHOP and exports
- **THEN** the CSV contains only SHOP records with the visible columns
