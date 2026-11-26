# Proposal

## Why

Projects already have an alias, but it does not help in daily prompts. Three things are missing:
- It cannot be changed after setup.
- It has no rules, so it often equals the long project name.
- The AI only learns the aliases if it calls `list_projects` on its own.

A prompt like "Zeig die offenen Bugs in SHOP" should therefore work without saying that SHOP is an Azure DevOps project.

## What Changes

- **Name:** "Alias" becomes "Kurzname" in all UI texts and tool descriptions. The stored field and its uniqueness across all connections stay as they are.
- **Rules for new or edited Kurznamen:**
  - 2–10 characters: letters, digits and `-`, starting with a letter;
  - unique without regard to case, stored in capitals;
  - not one of the reserved words `PR`, `BUG`, `ID`, `TEST`, `TASK`, `ALL`.

  Existing aliases that break the rules stay valid until they are edited. The Manager suggests a name that meets the rules.
- **Suggestion when setting up a project:**
  - a name with several words gives its initials, e.g. "Demo Project One" → `DPO`;
  - a single word up to 8 characters is kept ("Sandbox" → `SANDBOX`), a longer one gives its first 5 letters ("Infrastructure" → `INFRA`); numbers stay whole in initials ("Archiv-2019" → `A2019`);
  - on a clash a digit is added.
- **Server instructions:** the instructions sent at `initialize` list every visible configured project as `Kurzname = Projektname (Verbindung)`, name the default project, and tell the AI that naming a Kurzname means that Azure DevOps project, so it should use these tools with `project="<Kurzname>"` without asking.
- **Example in the instructions:** they include the example „gib mir aus SHOP das Work Item 4711“ → `get_work_item` with `project="SHOP"`, `id=4711`.
- **Work items stay in their project:** tools that take work item IDs check that the item lies in the selected project. Today `project="SHOP"` with the ID of an OPS item would read or even change it, bypassing OPS's settings.
  - A single ID in another project fails before approval and before any write: "Work Item 5000 gehört nicht zu SHOP – es liegt in OPS". An unconfigured project is not named.
  - Several IDs: items of other projects are left out, with a note.
- **Tool parameter:** the `project` parameter lists the current Kurznamen in its description and as allowed values. When Kurznamen change, the server sends `notifications/tools/list_changed`.
- **Wizard, project step:**
  - the field is labelled "Kurzname", with the suggestion and the rules;
  - a preview reads "So sprichst du es an: „offene Bugs in DPO“".
- **Verbindungen:** the Kurzname can be edited per project in the project list of the selected connection. Uniqueness is checked at once. A rename warns: "Prompts mit dem alten Kurznamen funktionieren danach nicht mehr."

## Capabilities

### New Capabilities
None.

### Modified Capabilities
- `connections`: Kurzname rules in the connection definition.
- `mcp-server`: Kurznamen in the server instructions and in the `project` parameter; tool list update when Kurznamen change.
- `azdo-tools`: work items stay in their project.
- `setup-wizard`: Kurzname field with suggestion and preview in the project step.
- `manager-ui`: Kurzname editable in the connection editor.

## Impact

- **Core:**
  - `ConnectionStore`: validation, reserved words, `SuggestAlias`, a rename API;
  - `ToolDefinition`: the `project` parameter text and enum;
  - `ToolExecutor`: error texts say "Kurzname";
  - the work item tools: project check of each ID.
- **Server:** `McpSetup` builds the instructions from the store, and the reload watcher sends `tools/list_changed`.
- **Manager.Core:** `SetupWizardViewModel` (`ProjectSetup`), `ConnectionsViewModel` (editable project items).
- **Manager:** `ProjectDiscoveryView`, `ConnectionsView`.
- **Tests:** store rules, suggestion, instructions text, the parameter enum, list change, wizard preview, rename in Verbindungen.
- **Data:** no data migration; no new packages.
