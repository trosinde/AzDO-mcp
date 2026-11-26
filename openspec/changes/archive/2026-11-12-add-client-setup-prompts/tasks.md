# Tasks

## 1. Manager.Core

- [x] 1.1 `ClientSetupService.Prompt(kind)` for Claude Code, Claude Desktop, OpenCode and VS Code, built from the written entry objects, with backup/keep/valid-JSON/no-token rules; verified by `ClientPromptTests` (entry, path, `claude mcp list`, wrapper sentence, Desktop marked for an assistant with file access, no PAT/URL/project name).
- [x] 1.2 Test question with the default project's Kurzname; verified by `ClientPromptTests` (with and without projects).
- [x] 1.3 OpenCode wrapper check in `Detect` → `ClientState.Warning`, card "prüfen" with the hint; verified by `ClientSetupTests` (wrapper present / absent).
- [x] 1.4 `ClientCard.Prompt`, `TestQuestion`, action "Prompt kopieren"; verified by a view-model test (clipboard holds the OpenCode prompt).

## 2. Manager

- [x] 2.1 `ClientsView.xaml`: section "Prompt zum Einrichten" (prompt text, "Prompt kopieren"), test question, "prüfen" hint; verified by `dotnet build` and a screenshot of the page.

## 3. Docs and verification

- [x] 3.1 README client section mentions the prompts; `docs/ui-proposal/README.md` deviation note; verified by review of the diff.
- [x] 3.2 All tests green, `openspec validate --all --strict` green.

## Workflow follow-up

- Commit, archive with `/opsx:archive add-client-setup-prompts`, build and install.
