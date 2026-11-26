# Design

## Context

`ClientSetupService` in Manager.Core knows the paths (`ExePath`, `ClaudeDesktopConfig`, `OpenCodeConfig`, `VsCodeUserConfig`) and the entries it writes. `ClientsViewModel` builds one `ClientCard` per client with a snippet and actions. `Detect` reads the config with `JsoncEditor` and checks `mcp.azure-devops`.

## Goals / Non-Goals

**Goals:** prompts that work when pasted unchanged; one source for the entry, so the prompt and the one-click write can never differ.

**Non-Goals:** no automatic repair of foreign config content by the Manager; no prompts in English.

## Decisions

### 1. Prompts built from the same entry objects

`ClientSetupService.Prompt(ClientKind, string? kurzname)` serialises the same `JsonObject` that the Write method uses (compact, with escaped backslashes) into the prompt text.

*Alternative considered:* fixed text constants. They could drift from the written entry. Rejected.

### 2. Test question from the default project

`ClientsViewModel` takes the default project's Kurzname from `ConnectionStore.FindProject(null)`, or none if no project is set up.

### 3. Wrapper check in `Detect`

`ClientState` gets an optional `Warning`. For OpenCode, `mcp.servers` present → Warning text; the card shows "prüfen" (`StatusLevel.Warning`) and the hint.

## Risks / Trade-offs

- **[Assistant edits more than asked]** → Mitigation: the prompt asks for a backup and for keeping everything else; the Manager's one-click write stays available as the strict path.
- **[Path with user name in the prompt]** It is a local path, the user pastes it into their own tool → acceptable; no PAT or URL.
