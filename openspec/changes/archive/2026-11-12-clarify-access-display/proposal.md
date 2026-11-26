# Proposal

## Why

With a full-access PAT, every project shows "nur Lesen" for Code, Test and Iterationen. The only reason is that writing in those areas cannot be checked without really writing. The display presents an untested value as a restriction, so users doubt their PAT and their rights. A value that was not checked must say so ("n/a"), and only checked facts may look like a restriction.

## What Changes

- **Two values per area:** each area (Work Items, Code, Test, Iterationen) gets two separate values, Lesen and Schreiben. Each holds one of four check results:
  - "ja": checked, allowed;
  - "nein": checked, denied;
  - "n/a": cannot be checked without really writing;
  - "?": not checked yet.

  This replaces the single level "Schreiben / nur Lesen / kein Zugriff".
- **PAT scope:** Work Items Schreiben is checked with the existing `validateOnly` probe and gives ja/nein. Code, Test and Iterationen Schreiben are "n/a". Lesen comes from the read probes. A probe that fails for another reason, such as 404 or 400, gives "n/a" instead of a guessed level.
- **Project rights:** values the Security API answers are ja/nein. A permission the server version does not offer is "n/a". In the fallback with read probes, Lesen is ja/nein and Schreiben is "n/a".
- **Effective right:** computed per area and column from PAT and project right:
  - "nein" if either side is "nein";
  - "?" if either side is "?";
  - "n/a" if either side is "n/a";
  - otherwise "ja".
- **Enforcement:** only a checked "nein" restricts. Lesen "nein" blocks the group of that area. Work Items Schreiben "nein" blocks the write tools. "n/a" and "?" do not block, and Azure DevOps itself still refuses a write without the right. The Schreibschalter can be switched on unless Work Items Schreiben is "nein".
- **Status text:** the status names the reason when a project only reads: "nur Lesen · PAT", "nur Lesen · Projektrecht", "nur Lesen · Schreibschalter aus" or "nur Lesen · Schreib-Tools gesperrt". When writing is possible but untested, the status says "Schreibrecht nicht geprüft".
- **Neu einlesen:** a change is reported only when a value becomes "nein" or stops being "nein", for example ja → nein. Moves between ja, n/a and ? are refreshed silently.
- **UI:** one grid with "Lesen" and "Schreiben" per area on these pages:
  - PAT-Scope panel;
  - Übersicht project rows;
  - wizard steps "Projekte" and per project;
  - "Neu einlesen";
  - "Effektives Recht" in "Tools & Rechte".

  "n/a" and "?" are shown grey, with a tooltip that explains them.
- **Server and CLI:** `list_projects` and `azdo-mcp discover` report Lesen and Schreiben per area.
- **BREAKING (storage):** `connections.json` moves to schema 3 with two values per area, and schema 2 is migrated once with a backup:
  - "Schreiben" → ja/ja;
  - "nur Lesen" → ja/n/a, except Work Items → ja/nein (safe side: no write becomes possible until the next "Neu einlesen");
  - "kein Zugriff" → nein/nein.

## Capabilities

### New Capabilities
None.

### Modified Capabilities
- `project-discovery`: check values ja/nein/n/a/?, PAT scope per column, project rights per column, effective right per column, rescan reports only changes to or from "nein", discover output.
- `connections`: connection test reports Lesen/Schreiben per area; schema 3 migration.
- `tool-permissions`: only a checked "nein" restricts; the Schreibschalter can be switched on unless Work Items Schreiben is "nein".
- `setup-wizard`: grid in the steps "Projekte" and per project, preselection, Schreibschalter rule, rescan view.
- `manager-ui`: grey status for "n/a" and "?", Übersicht project table with grid and reason, Verbindungstest panel, effective right column in "Tools & Rechte".
- `azdo-tools`: `list_projects` reports Lesen and Schreiben per area.

## Impact

- **Core:**
  - `Connections/Connection.cs`: access model; `AccessLevel` is replaced by two check values per area.
  - `ConnectionStore`: schema 3 and the v2 → v3 migration.
  - `ConnectionTester`.
  - `Discovery/ProjectDiscovery.cs`: probes and Security API mapping.
  - `DiscoveryMerger`: diff rule.
  - `Permissions/Permissions.cs`: caps.
  - `Tools/OtherTools.cs`: `list_projects`.
- **Server:** `DiscoverCommand` table.
- **Manager.Core:** Overview, Connections, SetupWizard, ProjectRescan and Permissions view models, plus a shared status/reason helper.
- **Manager:** the views of those pages; a grey pill style in `Theme.xaml`.
- **Tests:** discovery, merger, migration, permissions and the Manager view models.
- **Data:** a one-time migration of `connections.json` with the backup `connections.v2.bak.json`.
- **Dependencies:** no new packages.
