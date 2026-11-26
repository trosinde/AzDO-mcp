# Tasks

## 1. Data model v2

- [x] 1.1 Connection/ProjectEntry/AccessLevel model, schema-2 store with alias uniqueness and single default; verify `ConnectionStoreV2Tests`
- [x] 1.2 Migration schema 1 → 2 with backup `connections.v1.bak.json`; verify `SchemaMigrationTests` (all v1 fields, PAT still readable, runs once)
- [x] 1.3 v1 import grouped per collection + PAT; verify `LegacyImportTests` (3 entries one PAT → 1 connection, 3 projects, PAT stored once)

## 2. Discovery

- [x] 2.1 Fake Azure DevOps with 3 projects (write / read-only / no access), paging, security namespaces; verify it serves the discovery calls in tests
- [x] 2.2 Discovery service: login, paged projects, PAT scope probes (only `validateOnly` as write), Security-API rights with name-resolved bits, read-probe fallback, effective = min; verify `ProjectDiscoveryTests`
- [x] 2.3 Merger and diff (new / missing / changed / unchanged, never overwrite settings, more rights never enable writing); verify `DiscoveryMergerTests`

## 3. Permissions and server

- [x] 3.1 Permission hierarchy v2 (project/connection × tool/group), caps (visibility, effective right, write switch), migration of `connectionBlocks`; verify `PermissionHierarchyTests`
- [x] 3.2 Project resolution by alias, `list_projects` with rights, unknown alias error, tool listing per project; adapt existing tests; verify `ToolExecutorTests`, `ToolListingTests`
- [x] 3.3 `azdo-mcp discover <verbindung> [--apply]`; verify `DiscoverCommandTests` (dry run unchanged, apply merges, unknown name exit 1, no PAT in output)
- [x] 3.4 PAT never in log, `connections.json` or CLI output after discovery; verify scan test

## 4. Manager

- [x] 4.1 `SetupWizardViewModel` (steps, test gate, preselection, project steps with defaults/locks/alias check, skip, summary, atomic apply, only changed modes stored); verify `SetupWizardTests`
- [x] 4.2 `ProjectRescanViewModel` (diff groups, remove on request, hand-off of new projects to the wizard); verify `ProjectRescanTests`
- [x] 4.3 Adapt Overview, Connections and Permissions view models to the new model and hierarchy; verify existing Manager tests updated
- [x] 4.4 Rename views to the `*View` mapping, add `ProjectDiscoveryView` and `ProjectRescanView`, rebuild `OverviewView`, `ConnectionsView`, `PermissionsView` after artboards 1, 2, 4, 7, 8; verify build and screenshots against the artboards
- [x] 4.5 Update `docs/ui-proposal` copies (placeholders only) and the mapping README; verify every artboard is mapped

## 5. Wrap-up

- [x] 5.1 README/CLAUDE.md: connection = collection + PAT, wizard, `discover` command; verify text matches behaviour
- [x] 5.2 All tests green, `openspec validate --all --strict` green, build script green; commit in thematic commits
