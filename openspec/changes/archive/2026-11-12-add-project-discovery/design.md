# Design

## Context

Today a `Connection` is collection + project + PAT; permissions know groups, tools and per-connection group blocks (`openspec/specs/connections`, `tool-permissions`). The changed UI proposal (artboards 1, 2, 4, 7, 8) models *one connection per collection with projects underneath*. The owner asked for a guided wizard that walks through every selected project so rules stay per project.

## Goals / Non-Goals

**Goals:** one PAT per collection; reliable effective rights without writing data; settings never overwritten by discovery; per-project rules on every level.

**Non-Goals:** reading the PAT's scopes from Azure DevOps (no such API for on-prem/PAT auth); Git write tools; Build area (the artboards show Iterationen instead – the artboard is authoritative); renaming the data files (owner decision: keep `connections.json` and `secrets\<id>.bin`).

## Decisions

**Data model v2.** `connections.json` `{ version: 2, connections: [ { id, name, serverType, collectionUrl, patExpires, reminderDays, patScope: {area: level}, lastTested, lastAuthFailure, lastDiscovery, projects: [ { id (AzDO project GUID), name, alias, isDefault, allowWrite, rights: {area: level}, visible, addedAt } ] } ] }`. Areas: `WorkItems`, `Code`, `Test`, `Iterations`; levels `None < Read < Write`. PAT stays `secrets\<connectionId>.bin`. Migration from version 1 happens inside `ConnectionStore` on first read (under the config mutex), so server and Manager can never disagree. Alternative "separate projects.json" rejected – two files would need a cross-file transaction.

**Resolution key.** Tools resolve `project` by alias over all connections; the result is `(Connection, ProjectEntry)`. `AzdoClient` gets the project name from the entry. The rest of the tool code only uses `ProjectUrl`, so tools do not change.

**Discovery service (`Core/Discovery`).**
1. `connectionData` → user id/name.
2. `GET _apis/projects?$top=100&continuationToken=…&api-version=6.0`, token from header `x-ms-continuationtoken`.
3. PAT scope on the first visible project: WIQL `$top=1` (WI read), `workitems/$Task?validateOnly=true` (WI write – owner decision: keep, nothing is stored), `git/repositories` (Code read), `test/runs?$top=1` (Test read), `work/teamsettings/iterations` (Iterationen read). Code/Test/Iterationen write scope is not probed: no write tools exist for them, so "Schreiben" is shown only from project rights and capped to Read by the missing probe – displayed as "nur Lesen".
4. Project rights: `GET _apis/securitynamespaces` → resolve namespaces and bits *by action name*: CSS (`WORK_ITEM_READ`, `WORK_ITEM_WRITE`, token `vstfs:///Classification/Node/<root area id>` from `wit/classificationnodes/areas`), Git Repositories (`GenericRead`, `GenericContribute`, token `repoV2/<projectId>`), Project (`VIEW_TEST_RESULTS` / CSS `MANAGE_TEST_PLANS`). One `GET _apis/permissions/<ns>/<bit>?tokens=t1,t2,…&delimiter=,` per namespace/bit for *all* projects (batched by 50 tokens). On 401/403/404 of the Security API: per-project read probes (WIQL, repositories, test runs) → Read/None.
5. `Effective = Min(patScope, projectRight)`.

**Merge (`DiscoveryMerger`).** Pure function `(stored connection, discovery result, selection, per-project choices) → updated connection + permission overrides`. Never touches alias/default/allowWrite/modes of existing projects; refreshes `rights` and `visible`; adds new projects with `allowWrite=false`. `Diff` = new / missing / changed (per area old→new) / unchanged. Tested without I/O.

**Permission hierarchy.** `permissions.json` v2 adds `connections: {connId: {groups, tools}}` and `projects: {"connId/projectId": {groups, tools}}`; the old `connectionBlocks` migrate to connection-level group `Blocked`. `Evaluate(tool, connection, project, settings)` walks project.tools → project.groups → connection.tools → connection.groups → tools → groups → default, then applies caps: visibility, effective right of the tool group's area (WorkItemsRead/Write→WorkItems, Git→Code, Test→Test, Planning→Iterations), write switch, Write right for write tools. The wizard stores a project-level mode only when it differs from the inherited one.

**Wizard (Manager.Core `SetupWizardViewModel`).** One view model with `Steps` and `CurrentStep` (`Connection`, `Projects`, `Project[i]`, `Summary`); per-project state objects live in the VM until "Übernehmen" calls `DiscoveryMerger` and saves connection + permissions in one go (connections first under the config mutex, then permissions; on failure of the second the first is rolled back from the in-memory snapshot). Rescan (`ProjectRescanViewModel`) produces the diff and, for checked new projects, hands them to the wizard starting at the first project step.

**Views.** Renamed to the owner's mapping: `OverviewView`, `ConnectionsView`, `ProjectDiscoveryView` (the wizard; artboard "Erkennung" is its step "Projekte"), `ProjectRescanView`, `PermissionsView` (tree), `ClientsView`, `LogView`. The wizard's connection, project and summary steps have no artboard yet; they reuse the card, pill and segmented-switch styles of artboards 2, 4 and 7.

**CLI.** `azdo-mcp discover <name> [--apply]` in the Server project, sharing the Core discovery service; output is a plain table to stdout (stdio MCP mode is not active in this command).

## Risks / Trade-offs

- [Security API differs between AzDO Server versions] → bits by action name, fallback to read probes; unknown → "nur Lesen" at most.
- [`validateOnly` might be logged server-side as an attempt] → no data is created; documented in the UI hint.
- [Alias clashes across collections] → alias must be unique; the wizard proposes `Name-2` and blocks "Weiter" on clashes.
- [Schema migration bug loses settings] → migration test with every v1 field; v1 file backed up as `connections.v1.bak.json` before the first v2 write.

## Migration Plan

Automatic on first start of 2.1: back up, migrate connections (one project each) and `connectionBlocks`, write v2. No user action needed; "Neu einlesen" then fills rights and finds further projects.
