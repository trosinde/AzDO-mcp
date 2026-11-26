# Tasks

## 1. Access model and storage

- [x] 1.1 Replace `AccessLevel` with `Check` / `AreaAccess` / `AreaRights` (two columns per area), `Combine` and German `Display()` ("ja", "nein", "n/a", "?"); verified by new unit tests for every `Combine` row of design decision 1.
- [x] 1.2 Store `connections.json` as schema 3 and migrate schema 2 with backup `connections.v2.bak.json` (Write→ja/ja, Read→ja/n/a, WI Read→ja/nein, None→nein/nein); v1 goes straight to schema 3; verified by `SchemaMigrationTests` (schema 2 rights, backup, runs once, v1 → v3).

## 2. Discovery

- [x] 2.1 `PatScopeProbe` per the mapping table (Lesen from probes, other failures → n/a, WI Schreiben from `validateOnly`, other Schreiben → n/a); verified by `ProjectDiscoveryTests` with FakeAzdo full-access, read-only and 404 cases.
- [x] 2.2 `ProjectRightsReader`: Security API bits → ja/nein, missing action → n/a, Iterationen Schreiben n/a, fallback Schreiben n/a; verified by `ProjectDiscoveryTests` (security API on/off, contribute denied).
- [x] 2.3 `ConnectionTester` reports and stores the new PAT scope; verified by `ConnectionTesterTests`.
- [x] 2.4 `DiscoveryMerger.Diff` reports only changes to or from "nein" per column; `Apply` stores all values; verified by `DiscoveryMergerTests` ("?"→"n/a" unchanged, ja→nein reported, nein→ja keeps switch off).

## 3. Enforcement and status

- [x] 3.1 `PermissionEvaluator`: block only on Lesen "nein" / WI Schreiben "nein"; n/a and ? pass; verified by `PermissionHierarchyTests` and `BlockedCallTests` (new case "Untestable write does not block").
- [x] 3.2 Core `ProjectAccessStatus` (kind, reason PAT / Projektrecht / Schreibschalter aus / Schreib-Tools gesperrt, untested flag, tooltip lines); verified by unit tests for each reason and for "Schreibrecht nicht geprüft".

## 4. Server and CLI

- [x] 4.1 `list_projects` returns `effectiveRights: { area: { read, write } }`; verified by a tool test asserting `"Code": { "read": "ja", "write": "n/a" }` and no PAT in the output.
- [x] 4.2 `azdo-mcp discover` prints Lesen/Schreiben per area and the status from `ProjectAccessStatus`; verified by `DiscoverCommandTests`.

## 5. Manager

- [x] 5.1 `RightCell` view model and `RightsGrid` templates (compact and full) plus grey `PillNeutral` with tooltips in `Theme.xaml`; verified by `dotnet build` and a view-model test for cell texts and tooltips.
- [x] 5.2 Übersicht project rows with grid and status/reason/tooltip from `ProjectAccessStatus`; verified by `OverviewViewModelTests` (Projektrecht, Schreibschalter aus, full-access PAT has no yellow cell).
- [x] 5.3 Verbindungstest panel: PAT-Scope grid "Lesen"/"Schreiben" and the footnote text from the spec; verified by `ConnectionsViewModelTests`.
- [x] 5.4 Wizard: step "Projekte" grid and preselection (any Lesen not "nein"); project step full grid, Schreibschalter enabled unless WI Schreiben "nein" with reason or n/a hint; verified by `SetupWizardTests`.
- [x] 5.5 "Neu einlesen": grid for new rows, changed values as "<Bereich> <Lesen|Schreiben>: alt → neu"; verified by `ProjectRescanTests`.
- [x] 5.6 "Tools & Rechte": project rows show the value the group needs and the "nein" reason in the hint; verified by `PermissionsViewModelTests`.

## 6. Docs and verification

- [x] 6.1 Update `docs/ui-proposal/README.md` (deviation: Lesen/Schreiben grid, grey n/a) and README (rights section, schema 3, hint to run "Neu einlesen" after the update); verified by review of the diff.
- [x] 6.2 Full check: `dotnet run --project tests/AzdoMcp.Tests` green, `openspec validate --all --strict` green, `build.ps1 -Version 2.2.0` green, and a manual look at Übersicht, Verbindungen, wizard, Neu einlesen and Tools & Rechte against a local fake server with a full-access and a read-only PAT (screenshots of app windows only).

## Workflow follow-up

- Commit thematically, then archive with `/opsx:archive clarify-access-display`.
- Install 2.2.0 and run "Neu einlesen" against the real server.
