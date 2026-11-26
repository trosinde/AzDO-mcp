# Design

## Context

Rights are currently one `AccessLevel` per area (`None < Read < Write`). They are stored in `connections.json` schema 2 as `AreaRights.Levels`, and a missing key means "unknown". Several things only produce `Read`:
- `PatScopeProbe` for Code, Test and Iterationen;
- the per-project read-probe fallback;
- probe failures other than 401/403.

`Access.Min` combines PAT scope and project right. The model is used throughout the code:
- `PermissionEvaluator` caps groups at `None` and write tools below `Write`;
- `DiscoveryMerger.Diff` compares levels;
- `OverviewViewModel.ProjectStatus` derives the status text;
- `list_projects` and `DiscoverCommand` print `Display()`.

## Goals / Non-Goals

**Goals:**
- A single access model in which a value that was not checked can never look like a denial.
- One place in Core that derives the status text and reason. The Manager pages, the CLI and `list_projects` all use it.

**Non-Goals:**
- No new probes and no write request beyond the existing `validateOnly` create.
- No change to the permission hierarchy, the modes or the wizard flow.

## Decisions

### 1. Replace `AccessLevel` with two check values per area

The new types are:
- `enum Check { Yes, No, NotTestable }`, with `null` meaning "?";
- `record AreaAccess(Check? Read, Check? Write)`;
- `AreaRights` becomes `Dictionary<AccessArea, AreaAccess>`.

`AreaRights.Combine(pat, project)` applies the rule per column: No beats ?, ? beats n/a, n/a beats Yes. The German texts come from `Check?.Display()`: "ja", "nein", "n/a", "?".

*Alternative considered:* keep `AccessLevel` and add a separate "write tested" flag. That leaves two sources of truth and still invites showing "nur Lesen". Rejected.

### 2. Probe and Security API mapping

| Source | Lesen | Schreiben |
|---|---|---|
| PAT: WI | WIQL: Ok→Yes, 401/403→No, other→NotTestable | `validateOnly`: 401/403→No, else Yes (only if Lesen is Yes) |
| PAT: Code/Test/Iter | GET probe as above | NotTestable |
| Security API: WI/Code/Test | read bit → Yes/No; action missing → NotTestable | write bit → Yes/No; action missing → NotTestable |
| Security API: Iter | GENERIC_READ ∧ WI read | NotTestable |
| Fallback probes | GET probe as above | NotTestable |

If WI Lesen is No, WI Schreiben is also No, because a PAT that cannot read work items cannot write them either.

### 3. Caps only on a checked "nein"

`PermissionEvaluator` behaves as follows:
- `Read == No` gives "403 Scope fehlt";
- for write tools, `WorkItems.Write == No` gives "(nur Lesen)";
- `NotTestable` and `null` pass, and Azure DevOps still answers 403 if the right is really missing.

The Schreibschalter in the wizard and in "Tools & Rechte" is enabled unless `WorkItems.Write == No`.

*Alternative considered:* treat n/a as blocking. Without the Security API, nobody could ever write. Rejected.

### 4. One status helper in Core

`ProjectAccessStatus.For(ProjectRef, PermissionSettings)` returns:
- `Kind`: Direkt, MitFreigabe, NurLesen, KeinZugriff or NichtSichtbar;
- `Reason`: PAT, Projektrecht, Schreibschalter or ToolsGesperrt;
- `Untested`: true when WI Schreiben is n/a or ?;
- the text lines for the tooltip.

Reasons are checked in order:
1. PAT WI Schreiben No → PAT;
2. project WI Schreiben No → Projektrecht;
3. switch off → Schreibschalter;
4. all write tools Gesperrt → ToolsGesperrt.

The Overview, the wizard summary, the Permissions hints and `discover` use it.

### 5. Diff rule

`DiscoveryMerger.Diff` reports an `AreaChange(Area, Column, Old, New)` only when `(Old == No) != (New == No)`. The merge still stores all new values.

### 6. Storage: schema 3

`AreaRights` is stored as `{ "WorkItems": { "read": "Yes", "write": "No" } }`. A missing area or column means "?".

`ConnectionStore.ReadOrMigrate` handles the versions as follows:
- **Schema 1:** v1 → v3 goes through the existing `FromV1`, then the v2 → v3 conversion.
- **Schema 2:** under the existing mutex it writes `connections.v2.bak.json`, converts, then writes schema 3. The conversion is:
  - Write → Yes/Yes;
  - Read → Yes/NotTestable, except WorkItems → Yes/No;
  - None → No/No.

### 7. UI

A shared DataTemplate `RightsGrid` in `Theme.xaml` takes a list of `(Area, Read, Write)` cells and comes in two sizes:
- **Compact:** "L"/"S" mini pills under the area headers WI · Code · Test · Iter, for the Übersicht, the wizard "Projekte" step and the "Neu sichtbar" rows.
- **Full:** a table with the columns "Lesen" and "Schreiben", for the PAT-Scope panel and the wizard project step.

Pill styles:
- ja → green;
- nein → red;
- n/a and ? → the existing neutral grey (`NeutralBg`/`NeutralFg`) with a tooltip.

`RightPill`/`AccessPill` in `OverviewViewModel` are replaced by a `RightCell` view model.

## Risks / Trade-offs

- **[Writes now possible on n/a]** Without the Security API, a project with the write switch on can now reach Azure DevOps with write calls that were blocked before. → Mitigation: the write switch stays off by default, write tools default to Nachfragen, and AzDO enforces its own rights. The status shows "Schreibrecht nicht geprüft".
- **[Migration loses detail]** Schema 2 does not record whether a "nur Lesen" came from a check. → Mitigation: Work Items maps to the safe side (nein), the other areas have no write tools, and the Übersicht footnote suggests "Neu einlesen" after the update.
- **[Width at 1120 px]** Eight cells per project row. → Mitigation: compact 28 px cells; the status text column wraps.
- **[Older Manager on schema 3]** Manager 2.1.0 cannot read schema 3. → Mitigation: the backup `connections.v2.bak.json` allows a downgrade; mention this in the release notes.

## Migration Plan

1. Ship the change as 2.2.0. The first start migrates `connections.json` to schema 3 and keeps `connections.v2.bak.json`.
2. Tell the user to run "Neu einlesen" once per connection, which replaces migrated values with checked ones.
3. Rollback: reinstall 2.1.0 and copy `connections.v2.bak.json` back to `connections.json`.
