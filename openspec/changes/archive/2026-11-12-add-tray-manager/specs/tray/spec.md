# Spec Delta

## Purpose

Defines the tray icon as the entry point of the Manager: status at a glance, quick actions, reminders and approval notifications.

## ADDED Requirements

### Requirement: Tray icon and status
The Manager SHALL show an icon in the notification area with a status dot: green when all connections are OK, yellow when a PAT expires within its lead time or an approval is pending, red when a PAT is expired. Double-click SHALL open the main window.

#### Scenario: Pending approval
- **WHEN** an approval is pending
- **THEN** the tray dot is yellow

### Requirement: Tray menu
The context menu SHALL contain: header "AzDO MCP Manager" with "Server läuft · N Clients aktiv" (or "pausiert"), one entry per connection with status dot and expiry hint ("PAT in N Tagen", "PAT abgelaufen"), "Verwaltung öffnen", "Offene Freigaben (N)", "Server neu starten", "Autostart bei Anmeldung" with check mark, "Protokollordner öffnen", "Beenden".

#### Scenario: Connection entry
- **WHEN** the user clicks connection "SHOP" in the menu
- **THEN** the main window opens on "Verbindungen & PATs" with SHOP selected

#### Scenario: Beenden
- **WHEN** the user clicks "Beenden"
- **THEN** the Manager exits; running server instances keep serving "Erlaubt" calls

### Requirement: Close to tray
Closing the main window SHALL hide it to the tray; the Manager keeps running. Only "Beenden" exits.

#### Scenario: Close button
- **WHEN** the user clicks the window's close button
- **THEN** the window disappears and the tray icon remains

### Requirement: Single instance
Starting the Manager a second time SHALL bring the running instance's window to the front instead of starting a second process.

#### Scenario: Second start
- **WHEN** the Manager runs and the user starts it again
- **THEN** the existing window is shown and the new process exits

### Requirement: Autostart without admin
"Autostart bei Anmeldung" SHALL be stored in `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` and start the Manager minimised to the tray. It SHALL be on by default after installation.

#### Scenario: Toggle autostart
- **WHEN** the user unticks "Autostart bei Anmeldung"
- **THEN** the HKCU Run entry is removed

### Requirement: Approval notification
For each approval request the Manager SHALL show a notification window near the tray titled "<Client> möchte #<ID> in <Verbindung> ändern" (or "… <Tool> in <Verbindung> ausführen" without ID), the tool and change summary, and buttons "Erlauben", "Ablehnen", "Details". "Details" opens the Protokoll page with the request selected. The window SHALL close when the request is answered or times out.

#### Scenario: Approve from notification
- **WHEN** the user clicks "Erlauben" in the notification
- **THEN** the request is approved and the notification closes

#### Scenario: Several requests
- **WHEN** two requests are pending
- **THEN** both are reachable (stacked notifications or a counter leading to the Protokoll page)

### Requirement: PAT expiry reminder
Once per day the Manager SHALL show a tray balloon for each connection whose PAT expires within its lead time or has expired.

#### Scenario: Reminder
- **WHEN** SHOP's PAT expires in 4 days with lead time 7 days
- **THEN** a balloon "PAT für SHOP läuft in 4 Tagen ab" is shown at most once per day
