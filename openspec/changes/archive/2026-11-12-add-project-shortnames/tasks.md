# Tasks

## 1. Core

- [x] 1.1 `ShortName.Validate` / `ShortName.Suggest` (2–10 chars, letter first, letters/digits/-, reserved words, capitals, initials (numbers whole), short words whole or first five letters, digit on clash); verified by new `ShortNameTests` covering every scenario of "Kurzname rules" and "Kurzname suggestion".
- [x] 1.2 `ConnectionStore.Save` applies the strict rules only to new or changed Kurznamen, stores capitals and keeps legacy aliases; error texts "Kurzname „X“ wird bereits verwendet" / rule text / "„TEST“ ist reserviert"; verified by `ConnectionStoreTests` (stored in capitals, invalid, legacy kept, uniqueness).
- [x] 1.3 `ToolExecutor` unknown-project error reads "Unbekannter Kurzname „X“. Eingerichtet: …"; verified by the existing unknown-project test, updated.

- [x] 1.4 Project check for work item IDs (pre-check before approval and write, collection-level GET of `System.TeamProject`, Kurzname of a configured owner only, batch filtering with note "Nicht in SHOP, ausgelassen: …"); verified by new `WorkItemProjectCheckTests` with FakeAzdo for every scenario of "Work items stay in their project", including no approval request for a foreign write.

## 2. Server

- [x] 2.1 `project` parameter with enum and description built from the visible Kurznamen; Kurznamen in the `ToolListing` fingerprint; verified by a test on `tools/list` output (enum contains SHOP and OPS) and a `ToolListMonitor` test (rename triggers `list_changed`).
- [x] 2.2 Instructions built from the store at `initialize` (projects, default, fixed sentence with the example „gib mir aus SHOP das Work Item 4711“, no PAT or URL, hidden projects left out, text when no project exists); verified by `McpSetupTests` for each scenario of "Kurznamen in server instructions".

## 3. Manager

- [x] 3.1 Wizard project step: field "Kurzname" with suggestion, rule text, live error, preview "So sprichst du es an: „offene Bugs in X“"; "Weiter" blocked on error; verified by `SetupWizardTests` (suggestion DPO, invalid blocks, clash).
- [x] 3.2 Verbindungen: editable Kurzname per project with live check, legacy hint with suggestion, confirmation before saving a rename, permissions stay attached; verified by `ConnectionsViewModelTests` (rename, cancel, legacy hint) and `dotnet build`.
- [x] 3.3 Rename "Alias" to "Kurzname" in all UI texts and tool descriptions; verified by `grep -rn "Alias" src/AzdoMcp.Manager src/AzdoMcp.Manager.Core` showing no user-facing hits.

## 4. Docs and verification

- [x] 4.1 README section "Kurznamen" with an example prompt and the rules; `docs/ui-proposal/README.md` deviation note; verified by review of the diff.
- [x] 4.2 Full check: all tests green, `openspec validate --all --strict` green, and a manual check: after a rename in the Manager, a running Claude Code session sees the new Kurzname, and the prompts "Zeig die offenen Bugs in DPO" and "gib mir aus DPO das Work Item 1" work without naming Azure DevOps (fake server).

## Workflow follow-up

- Implement after `clarify-access-display`; both touch the wizard project step, so apply them one after the other.
- Commit thematically, archive with `/opsx:archive add-project-shortnames`.
