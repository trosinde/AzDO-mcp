# approval-flow Specification

## Purpose
Defines how a tool call in mode "Nachfragen" is shown to the user and approved or denied, and how server instances and the Manager talk to each other.

## Requirements

### Requirement: Local user-only channel
Server instances and the Manager SHALL communicate over a Named Pipe whose name contains the current user's SID and whose ACL allows only the current user. No network port SHALL be opened.

#### Scenario: Other user cannot connect
- **WHEN** a process of another Windows user tries to open the pipe
- **THEN** access is denied

### Requirement: Approval request
For a call in mode "Nachfragen" the server SHALL send the Manager a request with client name, tool, connection, work item ID (if any) and a short German change summary (for example "Status Active → Resolved, Priorität 2 → 1"), and SHALL wait for the answer before calling Azure DevOps.

#### Scenario: Approved
- **WHEN** the user clicks "Erlauben"
- **THEN** the call is executed and logged as OK (or its error)

#### Scenario: Denied
- **WHEN** the user clicks "Ablehnen"
- **THEN** the tool returns an error "Vom Benutzer abgelehnt" and the record result is "abgelehnt"

#### Scenario: Pending shown in log
- **WHEN** a request waits for an answer
- **THEN** the protocol shows the call with result "wartet auf Freigabe"

### Requirement: Approval timeout
A request without an answer SHALL be denied after 2 minutes.

#### Scenario: No answer
- **WHEN** 2 minutes pass without a decision
- **THEN** the call is denied with "Keine Antwort – abgelehnt" and the pending notification is withdrawn

### Requirement: Manager not running
If the Manager cannot be reached, calls in mode "Nachfragen" SHALL be denied immediately with a message telling the user to start the AzDO MCP Manager. Calls in mode "Erlaubt" SHALL work without the Manager.

#### Scenario: Manager closed
- **WHEN** the Manager is not running and `update_work_item` (Nachfragen) is called
- **THEN** the call is denied with "AzDO MCP Manager nicht gestartet – Freigabe nicht möglich"

### Requirement: Instance registration
Each server instance SHALL register with the Manager (client name, process ID, start time, last call) when the Manager is reachable and re-register when the Manager starts later.

#### Scenario: Manager started after client
- **WHEN** the Manager starts while two server instances run
- **THEN** within 10 seconds the Manager shows both instances as active clients

### Requirement: Pause and reload
The Manager SHALL be able to pause all server instances ("Stoppen") – every tool call is then rejected with "Vom Benutzer pausiert" – resume them, and make them reload configuration ("Neu starten").

#### Scenario: Paused
- **WHEN** the user clicks "Stoppen" and a client calls `get_work_item`
- **THEN** the call is rejected with "Vom Benutzer pausiert" until the user resumes
