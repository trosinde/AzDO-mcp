# Design

## Context

- **Store:** `ProjectEntry.Alias` is validated by `ConnectionStore` with `^[A-Za-z0-9_.\-]{1,40}$` and is unique across connections. `SuggestAlias` sanitizes the project name and appends `-2`.
- **Server instructions:** `McpSetup.CreateOptions` sets a static `ServerInstructions` string.
- **Tool list monitor:** `ToolListMonitor` watches the data folder and sends `tools/list_changed` when the fingerprint of the visible tool set changes. The fingerprint comes from `ToolListing`.
- **Permissions:** permission overrides refer to the project `Key`, not to the alias, so a rename keeps them.

## Goals / Non-Goals

**Goals:**
- The AI knows the Kurznamen from the first message.
- A Kurzname can be edited safely at any time.

**Non-Goals:**
- No work item or PR notation such as `KURZNAME#ID` or `KURZNAME!ID`.
- No writing into client files such as `CLAUDE.md` or `AGENTS.md`.
- No rename of the stored JSON field `alias` and no data migration.

## Decisions

### 1. Rules in one place

`ShortName.Validate(string, isLegacy)` and `ShortName.Suggest(name, reserved)` are static helpers in Core.

`ConnectionStore.Save` checks the strict rules only for a project whose Kurzname is new or changed compared with the stored entry. Unchanged legacy aliases still pass the old pattern.

The value is stored in capitals at save time.

*Alternative considered:* force all legacy aliases to the new rules at load. That would break prompts and settings without asking. Rejected.

### 2. Instructions built per session

`CreateOptions` gets a `Func<string>` that builds the instructions from `ConnectionStore.AllProjects()` (visible only) at `initialize`. `initialize` is the only moment MCP transfers them.

Later changes reach running sessions through the tool schema instead, see decision 3.

The text is German and has a fixed format:
- one line per project: `Kurzname = Projektname (Verbindung)`;
- the line `Standardprojekt: X`;
- the fixed sentence from the spec.

### 3. Kurznamen in the `project` parameter

`ToolDefinition` builds the `project` schema at listing time:
- `enum` holds the visible Kurznamen;
- `description` reads "Kurzname des Azure-DevOps-Projekts: SHOP = Demo Project One, OPS = …. Ohne Angabe: Standardprojekt SHOP."

The `ToolListing` fingerprint includes the Kurznamen, so `ToolListMonitor` sends `list_changed` on a rename, an add or a removal without further code.

Matching stays case-insensitive in `ToolExecutor`. Clients that enforce `enum` strictly get capitals, which the AI copies from the schema.

### 4. Editing in Verbindungen

`ConnectionsViewModel` turns `ProjectListItem` into an editable `ProjectItemViewModel` with:
- `ShortName`;
- `Error` for the live check against the rules and against all other projects;
- `LegacyHint`.

"Speichern" asks via `IDialogs.Confirm` before a rename. It then saves through `ConnectionStore.Save`; the `Key` is unchanged, so the permissions stay attached.

### 5. Wizard

`ProjectSetup.Alias` is renamed to `ShortName`. It is prefilled with `ShortName.Suggest`, using the Kurznamen of all projects plus the other wizard selections as reserved. It gets a live `Error` and a `Preview` text.

### 6. Project check for work item IDs

The check runs as a pre-check in `ToolExecutor`, before the approval request and before the tool's own request.
- **Tools with an `id`:** one GET `_apis/wit/workitems/{id}?fields=System.TeamProject` at collection level, compared case-insensitively with the project name of the selected `ProjectRef`.
- **Batch tools:** the batch response already contains `System.TeamProject`. They filter after the read, and a write batch does not exist.
- **Wrong project:** the error names the owning project's Kurzname only if `ConnectionStore` has a configured project with that name in the same connection. The audit result is "abgelehnt".

*Alternative considered:* rely on the project-scoped URL `{project}/_apis/wit/workitems/{id}`. Azure DevOps does not reliably refuse items of other projects on that route. Rejected.

## Risks / Trade-offs

- **[One extra GET per write call]** A small delay. → Accepted: writes are rare, and the GET is cheap.

- **[Client ignores instructions]** Some clients do not pass server instructions to the model. → Mitigation: the `project` parameter description carries the same table.
- **[Strict enum and legacy aliases]** A legacy alias with lowercase letters sits in the enum unchanged. → Mitigation: the enum lists aliases exactly as stored, and matching ignores case.
- **[Rename breaks saved prompts]** → Mitigation: the confirmation dialog warns; nothing else references the alias.
- **[Many projects]** A long instruction text. → Mitigation: one short line per project. No limit is needed for typical counts below 50.
