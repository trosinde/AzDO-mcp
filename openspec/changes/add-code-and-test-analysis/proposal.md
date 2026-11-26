# Proposal

## Why

The user wants to analyse code review and test activity as data and build KPIs and overviews from it, starting from work items or the other way round.

Two existing team applications do exactly that today. Each one has its own Azure DevOps client, its own sync and its own local database:
- **A test results dashboard (Python):**
  - **Reads:** test plans, test runs, results, builds with tags and branch, releases per definition, and the requirement ↔ test case links.
  - **Computes:** the latest result per test case and configuration, and counts per build and per release.
- **A pull request review analytics app (NestJS):**
  - **Reads:** pull requests, reviewers, iterations, changed files and comment threads.
  - **Computes:** cycle time, comments per PR or per file, thread status, reviewer activity and approval rate, and monthly trends per developer and per reviewer.

Both follow the same pattern: fetch a filtered set → expand details → derive fields → group and count. Both also share the same weaknesses:
- They load the full history and filter locally.
- They send one request per item.
- They join people by display name.
- They drop the vote timestamps.

The MCP server can provide this layer once, read-only, scoped by Kurzname. The AI client can then answer the same questions directly.

The current tools cannot. `get_pull_requests` has no reviewers, comments, close date or filters. There are no branch, build, release or test plan tools. `get_test_runs` returns the oldest runs first, without dates. Test results come back as megabytes of raw JSON.

## What Changes

The change is organised in two layers.

### Layer 1 – objects
Fixed, compact fields; filters applied on the Azure DevOps side; full paging; `include` for details.

**Git**
- `get_branches`: last commit, ahead/behind, active PR; filters prefix, `staleDays`, `withoutPullRequest`.
- `get_pull_requests`:
  - **Filters:** period, repository, target branch, author, reviewer, status.
  - **`include`:** reviewers, threads, iterations, files, workItems.
  - **Derived fields:** cycle time, vote text, vote time, first response.
- `get_pull_request`: one PR by ID with everything included.

**Pipelines** (new tool group and rights area)
- `get_pipelines`: build and release definitions.
- `get_builds`: filters pipeline, branch, tags, result, period.
- `get_releases`: filters definition, period; with stages.

**Test**
- `get_test_plans`.
- `get_test_runs`:
  - **Order:** newest first.
  - **Filters:** period, plan, build, release, release definition, pipeline, branch, run name, state.
  - **Details:** build and release reference.
- `get_test_results`:
  - **Selection:** by run, build, release or plan.
  - **`latest`:** only the latest result per test case and configuration.
  - **Fields:** configuration, test point, `failingSince`, comment, computer name, bugs, work items.

**Links**
- `trace_links`:
  - **Direction:** from work items, a PR, a commit or a test run, in both directions.
  - **Links followed:** PRs, commits, branches, builds, test cases, test results, bugs.
  - **`refPattern`:** an optional pattern that also matches IDs written in text, e.g. `REQ-\d+` in commit messages, PR titles, branch names and test names.

### Layer 2 – analysis
One tool per domain, of the form *filter → `groupBy` → `metrics`*.

**`analyze_pull_requests`** (group Git)
- **groupBy:** author, reviewer, repository, target branch, month, week, thread status, file.
- **metrics:**
  - count, completed, abandoned;
  - cycle time (avg, median, p90);
  - iterations, files, comments, comments per PR, comments per file;
  - threads per status, reviewers per PR;
  - first response (avg, median), approval rate, rejection rate.
- **`exclude`:** a title pattern and authors, e.g. test PRs and bots.

**`analyze_test_results`** (group Test)
- **Selection:** plan, release definition, pipeline, build or release.
- **`latest`:** test case + configuration by default.
- **groupBy:** build, release, stage, run, outcome, test case, computer name, day, week.
- **metrics:** total, passed, failed (Failed + Aborted), not executed, pass rate, average duration, failing since, new failures.

### Common
- **People:** identified by login, shown by name; grouping uses the login, so two display names of one login count as one person.
- **No noise:** no avatar, `_links` or descriptor fields.
- **Limits:** every result reports `processed` and `truncated`.
- **Text analysis stays in the AI client:** the server returns the comment text and does not rate it (e.g. severity or category).
- **Tool count:** rises from 22 to 31 in seven groups.

| Group | Tools |
|---|---|
| Work Items lesen | +1 `trace_links` |
| Git | +4 `get_branches`, `get_pull_request`, `analyze_pull_requests`; `get_pull_requests` extended |
| Pipelines (new) | `get_pipelines`, `get_builds`, `get_releases` |
| Test | +2 `get_test_plans`, `analyze_test_results`; runs and results extended |

### Pipelines area
- **New area:** a fifth rights area "Pipelines" (Build and Release read) alongside Work Items, Code, Test and Iterationen.
- **Discovery:** probes the PAT scope with a build read and reads the project right "View builds".
- **Stored data:** projects keep their stored rights; Pipelines is "?" until the next discovery, so no schema migration is needed.
- **UI:** the overview, connection test, wizard and "Tools & Rechte" show the new area or group.

## Non-goals

- No Git operations: no clone, diff, file content, merge, vote or comment.
- No writes of any kind.
- No rating of comment content.
- No line counts per PR. Azure DevOps reports changed files only.
- No local database or cache. Every call reads live data within the limits.

## Capabilities

### New Capabilities
None.

### Modified Capabilities
- `azdo-tools`: tool set and groups, the object tools, the analysis tools, link tracing, compact output.
- `tool-permissions`: the Pipelines group in the defaults; tools reading several areas.
- `connections`: the connection test reports the Pipelines scope.
- `project-discovery`: the Pipelines PAT probe and project right; the area list.
- `setup-wizard`: the Pipelines column and tool group.
- `manager-ui`: the Pipelines column in the overview and in the scope grid.

## Impact

- **Core:**
  - `ToolGroup.Pipelines` and `AccessArea.Pipelines`.
  - `Tools/GitTools.cs`, `PipelineTools.cs`, `TestTools.cs`, `TraceTools.cs` and `AnalysisTools.cs`, with shared helpers `Paging.cs`, `Shape.cs` and `Aggregate.cs`.
  - Discovery and the connection tester probe Pipelines.
  - `ServerInstructions` mentions the analysis tools.
- **Manager:**
  - The Pipelines column in the overview, the scope grid and the wizard.
  - The Pipelines group in "Tools & Rechte" and in the wizard project step.
- **Tests:**
  - FakeAzdo endpoints for every new request.
  - Tests for each tool, paging, the 7-day window, `latest`, `groupBy`/`metrics`, `exclude`, login grouping, foreign-project links, a missing area right, and the Pipelines discovery.
- **Docs:** the README tool list and analysis examples; CLAUDE.md "22 tools" → 31.
- **Dependencies:** no new packages.
