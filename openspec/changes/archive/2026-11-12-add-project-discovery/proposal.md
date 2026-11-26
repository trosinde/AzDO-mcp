# Proposal

## Why

Today every Azure DevOps project needs its own connection with its own copy of the PAT, set up by hand one at a time. Users want to enter collection URL and PAT once, see which projects that PAT can reach and with which rights, and set the projects up in one guided pass – while keeping per-project rules.

## What Changes

- **BREAKING (data)**: a connection becomes *one collection + one PAT*; projects live underneath it. `connections.json` moves to schema v2; existing v1 entries are migrated automatically (one connection with one project each, settings kept).
- Project discovery: verify login (`connectionData`), list all visible projects with paging, determine the PAT scope per area (Work Items, Code, Test, Iterationen) by read probes plus the existing non-persisting `validateOnly` write probe, read the user's rights per project via the Security API, and compute the effective right per area = minimum of PAT scope and project right.
- Setup wizard in the Manager: Verbindung → Projekte auswählen → one step per selected project (alias, Standardprojekt, Schreibschalter, mode per tool group) → Zusammenfassung → Übernehmen. Defaults: read groups Erlaubt, write groups Nachfragen, Schreibschalter aus.
- "Neu einlesen": diff of new / no longer visible / changed projects; new ones go through the wizard steps, missing ones are marked (never deleted), fewer rights lock immediately, more rights never switch writing on.
- Permissions get four levels – Gruppe → Tool → Verbindung → Projekt; the deepest set level wins; Schreibschalter and effective right always cap. "Tools & Rechte" shows this hierarchy.
- Server: tools select a *project* by alias; `list_projects` lists configured projects with effective rights; unknown project stays an error; new CLI `azdo-mcp discover <verbindung> [--apply]`.
- v1 import groups entries per collection and PAT.

## Capabilities

### New Capabilities
- `project-discovery`: discovery of projects, PAT scope and effective rights; rescan diff; merge rules; CLI.
- `setup-wizard`: the guided setup and the rescan flow in the Manager.

### Modified Capabilities
- `connections`: connection = collection + PAT with projects; v2 schema and migration; connection test reports PAT scope; v1 import per collection.
- `tool-permissions`: four-level hierarchy, per-project write switch and effective-right caps replace per-connection exceptions; defaults for new projects.
- `mcp-server`: `project` selects a configured project by alias.
- `azdo-tools`: `list_projects` returns configured projects with effective rights.
- `manager-ui`: Übersicht, Verbindungen & PATs and Tools & Rechte follow the changed artboards.

## Impact

- Core: `Connections` (model v2, store, migration, importer, tester), new `Discovery`, `Permissions` (hierarchy), `ToolExecutor`, system tools.
- Server: project selection, `discover` command.
- Manager.Core/Manager: new wizard and rescan view models and views, changed Overview/Connections/Permissions views; views renamed to `*View` (OverviewView, ConnectionsView, ProjectDiscoveryView, ProjectRescanView, PermissionsView, ClientsView, LogView).
- No new NuGet packages.
