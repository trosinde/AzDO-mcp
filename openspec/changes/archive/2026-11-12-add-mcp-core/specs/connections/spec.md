# Spec Delta

## Purpose

Defines how Azure DevOps connections (server, collection, project, PAT) are stored per user, protected and tested.

## ADDED Requirements

### Requirement: Connection definition
A connection SHALL consist of a unique short name (Kurzname), a server type (Azure DevOps Services or Azure DevOps Server on-prem), a collection URL, a project, a PAT, an optional PAT expiry date, a reminder lead time, a default flag and a write-access flag.

#### Scenario: Short names are unique
- **WHEN** a second connection with short name `shop` is saved while `SHOP` exists
- **THEN** saving is rejected with a message that the short name is already used

#### Scenario: Exactly one default
- **WHEN** connection B is marked as default while A was default
- **THEN** A loses the default flag and B is the only default

#### Scenario: First connection becomes default
- **WHEN** the first connection is saved
- **THEN** it is marked as default

### Requirement: Per-user storage location
Connections SHALL be stored under `%LOCALAPPDATA%\AzdoMcpData\` of the current user. No file SHALL be written outside the user profile and no admin rights SHALL be required.

#### Scenario: Fresh profile
- **WHEN** the server or Manager runs for the first time
- **THEN** the folder `%LOCALAPPDATA%\AzdoMcpData\` is created with normal user rights

### Requirement: PAT protection
PATs SHALL be stored only encrypted with Windows DPAPI in CurrentUser scope. The connection file SHALL contain no PAT. After saving, a PAT SHALL NOT be displayable again; it can only be replaced.

#### Scenario: Stored PAT is not readable as text
- **WHEN** a connection with a PAT is saved
- **THEN** no file under `%LOCALAPPDATA%\AzdoMcpData\` contains the PAT in plain text

#### Scenario: Other user cannot decrypt
- **WHEN** the encrypted PAT is copied to another Windows account
- **THEN** it cannot be decrypted there and the connection reports "PAT nicht lesbar – bitte ersetzen"

#### Scenario: Replace PAT
- **WHEN** the user chooses "Ersetzen" and enters a new PAT
- **THEN** the old encrypted PAT is overwritten and the expiry date can be updated

### Requirement: Connection test
The system SHALL test a connection on request and report, per area, whether it works: server reachable and API version, authenticated user, work items read/write, Git read, test management, and PAT expiry warning. The test SHALL return the list of projects of the collection.

#### Scenario: All scopes present
- **WHEN** the test runs with a valid PAT that has all scopes
- **THEN** every area reports "OK" and the project list is filled

#### Scenario: Missing scope
- **WHEN** the PAT lacks the Test Management scope
- **THEN** the area "Test Management" reports "403" with "Scope fehlt" and the connection records that test tools are unavailable for it

#### Scenario: Server unreachable
- **WHEN** the collection URL cannot be reached within 15 seconds
- **THEN** the test reports "Server nicht erreichbar" and does not reveal the PAT

### Requirement: PAT expiry status
The system SHALL classify each connection by PAT expiry: green when more than the reminder lead time remains, yellow ("in N Tagen", "PAT erneuern") within the lead time, red ("abgelaufen") after expiry or after a 401 from the last call or test.

#### Scenario: Expiring soon
- **WHEN** the PAT expires in 4 days and the lead time is 7 days
- **THEN** the status is yellow with "in 4 Tagen"

#### Scenario: Expired
- **WHEN** the expiry date has passed
- **THEN** the status is red with "abgelaufen"

### Requirement: Import of v1 configuration
The system SHALL detect a v1 `config.json` with plain-text PATs, offer to import it, store each project entry as a connection with an encrypted PAT and then delete the plain-text file. The import SHALL NOT proceed partially.

#### Scenario: Successful import
- **WHEN** the user imports a v1 file with three project entries
- **THEN** three connections exist, each PAT is encrypted, and the v1 file no longer exists

#### Scenario: Import fails
- **WHEN** one entry cannot be stored
- **THEN** no connection from that file is kept and the v1 file is left untouched

#### Scenario: Name clash
- **WHEN** an imported short name already exists
- **THEN** the imported connection gets a suffix (`SHOP-2`) and the user is told
