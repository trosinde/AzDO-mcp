## MODIFIED Requirements

### Requirement: Tool set and groups
The server SHALL provide 31 tools, each in exactly one of seven groups (Work Items lesen, Work Items schreiben, Git, Pipelines, Test, Planung, System), and SHALL mark every tool of "Work Items schreiben" as writing.

#### Scenario: Group membership is fixed
- **WHEN** the tool catalogue is queried
- **THEN** the groups contain exactly: Work Items lesen = get_work_item, query_by_id, execute_wiql, query_work_items, get_work_items_with_relations, get_linked_work_items, get_work_item_revisions, trace_links; Work Items schreiben = create_work_item, update_work_item, add_work_item_comment, update_work_item_comment, add_work_item_relation, add_work_item_attachment; Git = get_repositories, get_commits, get_branches, get_pull_requests, get_pull_request, analyze_pull_requests; Pipelines = get_pipelines, get_builds, get_releases; Test = get_test_plans, get_test_runs, get_test_results, analyze_test_results; Planung = get_iterations, get_iteration_capacity; System = list_projects, ping

### Requirement: Git, test and planning read tools
The server SHALL list repositories, commits of a repository (optional branch, author, date range, top), branches, pull requests, pipelines, builds, releases, test plans, test runs and their results, iterations of a team and iteration capacity. None of these tools SHALL change data in Azure DevOps.

#### Scenario: Pull requests by status
- **WHEN** `get_pull_requests` is called with `status=active`
- **THEN** only active pull requests are returned

## ADDED Requirements

### Requirement: Compact object output
Git, Pipelines, Test, trace and analysis tools SHALL return fixed field sets, SHALL return a person as `{ name, login }` and SHALL NOT return avatar URLs, `_links` blocks or identity descriptors. Each object SHALL keep one `webUrl` where Azure DevOps offers one.

#### Scenario: No identity noise
- **WHEN** `get_pull_requests` returns a pull request with two reviewers
- **THEN** each reviewer has `name`, `login` and vote fields and no avatar or identity URL

### Requirement: Complete lists within limits
List tools SHALL page internally until the filter is exhausted or `top` is reached, SHALL apply filters in the Azure DevOps request where the API supports them, and SHALL report `processed` and `truncated`. Date filters SHALL be `from` and `to` in ISO 8601.

#### Scenario: Truncated list
- **WHEN** 1400 pull requests match and `top=1000`
- **THEN** 1000 are returned with `truncated: true`

#### Scenario: Filter in the request
- **WHEN** `get_builds` is called with a pipeline and `from`
- **THEN** the build request carries the definition and minimum time and no older builds are downloaded

### Requirement: Branch overview
`get_branches` SHALL list the branches of a repository with name, last commit (ID, date, author), ahead and behind counts against the default branch, a default flag and the ID of an active pull request from it. Optional filters: name prefix, `staleDays`, `withoutPullRequest`.

#### Scenario: Stale branches without pull request
- **WHEN** `get_branches` is called with `staleDays=30` and `withoutPullRequest=true`
- **THEN** only branches whose last commit is older than 30 days and that have no active pull request are returned

### Requirement: Pull request list
`get_pull_requests` SHALL accept status, repository, target branch, author, reviewer and a period (`from`, `to`; close date for completed and abandoned, creation date otherwise; PRs created over 180 days before `from` are not considered), default `top` 100, at most 1000. Each entry SHALL contain ID, repository, title, author, status, draft, source and target branch, created, closed, merge status, labels, `cycleTimeHours` and `webUrl`.

#### Scenario: Completed in a period
- **WHEN** `get_pull_requests` is called with `status=completed`, `from=2026-01-01`, `to=2026-03-31`
- **THEN** exactly the pull requests closed in that period are returned, across all result pages

### Requirement: Pull request includes
`include` SHALL accept `reviewers`, `threads`, `iterations`, `files` and `workItems`. Details SHALL be loaded for at most 200 pull requests per call; beyond that the result SHALL say which were not expanded. `get_pull_request` SHALL return one pull request by ID with all includes and its description.

#### Scenario: Include limit
- **WHEN** 350 pull requests match and `include=threads`
- **THEN** the first 200 contain threads and the result notes "Details für 150 PRs nicht geladen"

### Requirement: Reviewer fields
Each reviewer SHALL contain the person, `required`, `declined`, `vote` and `voteText` ("approved" 10, "approved with suggestions" 5, "no vote" 0, "waiting" −5, "rejected" −10), `votedAt` from the latest vote event of that reviewer when present, and `firstResponseHours` from creation to the reviewer's first comment or vote.

#### Scenario: Vote time from the vote event
- **WHEN** reviewer Max Muster voted 10 two hours after creation and never commented
- **THEN** the reviewer entry has `voteText: "approved"`, a `votedAt` and `firstResponseHours: 2`

### Requirement: Comment threads
Each thread SHALL contain ID, status (active, fixed, wontFix, closed, byDesign, pending), `kind` ("text" or "system"), file path and line range when it refers to code, and its comments with ID, parent ID, author, published date and text (at most 4000 characters). System threads SHALL be counted but not listed unless `includeSystem=true`.

#### Scenario: Review comments only
- **WHEN** a pull request has 4 review threads and 6 system threads
- **THEN** `threads` lists the 4 review threads and `systemThreads` is 6

### Requirement: Iterations and files
`iterations` SHALL list number, author and date per push. `files` SHALL list each changed path with its change type across the whole pull request, obtained in one request against the pull request base. Line counts SHALL NOT be reported.

#### Scenario: Files across pushes
- **WHEN** a pull request has three pushes touching `a.cs`, `b.cs` and `a.cs` again
- **THEN** `files` lists `a.cs` and `b.cs` once each

### Requirement: Pipelines, builds and releases
`get_pipelines` SHALL list build and release definitions (ID, name, kind, path). `get_builds` SHALL accept pipeline, branch, tags, result and period and return ID, number, pipeline, branch, tags, status, result, start, finish and reason. `get_releases` SHALL accept definition and period and return ID, name, definition, created and stages with name and status.

#### Scenario: Builds by tag
- **WHEN** `get_builds` is called with pipeline 305 and `tags=nightly`
- **THEN** only builds of pipeline 305 carrying the tag `nightly` are returned, newest first

#### Scenario: Release management missing
- **WHEN** the server has no release endpoints
- **THEN** `get_releases` returns "Auf diesem Server nicht verfügbar" and `get_pipelines` lists build definitions only

### Requirement: Test plans and runs
`get_test_plans` SHALL list plans with ID, name, state and root suite. `get_test_runs` SHALL return runs newest first and accept period (default 30 days), plan, build, release, release definition, pipeline, branch, run name (contains) and state. Each run SHALL contain ID, name, state, automated, start, end, counts per outcome, plan, build (ID, number, pipeline, branch) and release (ID, name, stage).

#### Scenario: Latest runs first
- **WHEN** `get_test_runs` is called without filters
- **THEN** the runs of the last 30 days are returned with the most recent start first

#### Scenario: Long period
- **WHEN** the period is longer than the server allows in one query
- **THEN** the tool splits it into several queries and returns one merged, de-duplicated list

### Requirement: Test results selection
`get_test_results` SHALL accept exactly one of `runId`, `buildId`, `releaseId` or `planId` (with period), an outcome filter and `latest` (`testCase+configuration` or `none`, default `none`), and SHALL page until `top` (default 500, at most 5000). It SHALL start with counts per outcome over the whole selection.

#### Scenario: Current state of a plan
- **WHEN** `get_test_results` is called with `planId=2691`, `from` 14 days ago and `latest=testCase+configuration`
- **THEN** each test case and configuration appears once with its most recent result

#### Scenario: Ambiguous selection
- **WHEN** both `runId` and `releaseId` are given
- **THEN** the tool returns an error that exactly one of them is allowed

### Requirement: Test result content
Each test result SHALL contain ID, run, test case (ID, title), configuration (ID, name), test point ID, automated test name, outcome, priority, owner, duration, start, completion, `failingSince`, comment, computer name, build and release reference, the error message shortened to 1000 characters, and the IDs of linked bugs and work items.

#### Scenario: Linked bug
- **WHEN** a failed result has bug 812 associated
- **THEN** its entry contains `bugs: [812]` and the shortened error message

### Requirement: Link tracing
`trace_links` SHALL start from work items (IDs or WIQL), a pull request, a commit or a test run, follow the requested kinds (children, parent, pullRequests, commits, branches, builds, testCases, testResults, bugs) up to `depth` 1 or 2, and return nodes (kind, ID, title or name, state or outcome), edges (from, to, kind, source "link" or "text") and one summary line per start item.

#### Scenario: From story to code and tests
- **WHEN** `trace_links` starts at story 4711 with `include=pullRequests,testCases,testResults`
- **THEN** the result contains its pull requests, its test cases via "Tested By" with their latest results and a summary such as "2 PRs (1 gemergt) · 5 Tests (1 rot)"

#### Scenario: From pull request to work items
- **WHEN** `trace_links` starts at pull request 88
- **THEN** the result contains the work items linked to pull request 88

### Requirement: References in text
With `refPattern` (a regular expression, at most 200 characters, evaluated with a timeout), `trace_links` SHALL also match that pattern in commit messages, PR titles, branch names and automated test names and add edges with source "text" between objects sharing a match.

#### Scenario: Requirement ID in commits
- **WHEN** `refPattern=REQ-\d+` and three commits and one test name contain `REQ-020`
- **THEN** the result has a node `REQ-020` with four "text" edges

### Requirement: Link tracing limits
`trace_links` SHALL resolve only objects of the selected project, SHALL count links into other projects without loading them, and SHALL stop at 500 nodes with `truncated: true`.

#### Scenario: Link into another project
- **WHEN** story 4711 links to a pull request in a project that is not the selected one
- **THEN** that pull request is not loaded and the summary states "1 Verknüpfung in andere Projekte"

### Requirement: Pull request analysis
`analyze_pull_requests` SHALL take the filters of `get_pull_requests`, `exclude` (title pattern, authors), `groupBy` (none, author, reviewer, repository, targetBranch, month, week, threadStatus, file) and `metrics`, and return one row per group plus a total row. People SHALL be grouped by login.

#### Scenario: Developer ranking
- **WHEN** `analyze_pull_requests` runs with `groupBy=author` and `metrics=count,cycleTime,commentsPerPr`
- **THEN** each author has one row with PR count, average, median and p90 cycle time in hours and comments per PR

#### Scenario: Test PRs excluded
- **WHEN** `exclude.titlePattern` is `^\[TEST\]`
- **THEN** pull requests whose title starts with "[TEST]" are not counted and the result reports how many were excluded

### Requirement: Pull request metrics
`metrics` SHALL accept count, completed, abandoned, cycleTime, iterations, files, comments, commentsPerPr, commentsPerFile, threadStatus, reviewersPerPr, firstResponse, approvalRate and rejectionRate. Comment metrics SHALL count text comments only. Cycle time SHALL use completed pull requests only. Rates SHALL be shares of reviewed pull requests with vote ≥ 5 or ≤ −5.

#### Scenario: Comments per file
- **WHEN** an author has 2 pull requests with 10 text comments across 4 changed files
- **THEN** commentsPerPr is 5 and commentsPerFile is 2.5

### Requirement: Test result analysis
`analyze_test_results` SHALL take the selection of `get_test_results` plus release definition, pipeline, branch and tags, apply `latest` (default `testCase+configuration` per group), `groupBy` (none, build, release, stage, run, outcome, testCase, computerName, day, week) and return total, passed, failed (Failed and Aborted), notExecuted, passRate and average duration per group.

#### Scenario: Results per build
- **WHEN** `analyze_test_results` runs for release definition 10, branch `master`, the last 30 days, `groupBy=build`
- **THEN** each build has one row counting the latest result per test case and configuration in that build

### Requirement: Failure trends
`analyze_test_results` SHALL accept the metrics `failingSince` (per failed test case, the start of its current failure streak) and `newFailures` (test cases failed in a group that passed in the previous group of the same order).

#### Scenario: New failure
- **WHEN** test case 501 passed in build 20261101.1 and failed in build 20261102.1
- **THEN** the row of build 20261102.1 lists 501 under newFailures
