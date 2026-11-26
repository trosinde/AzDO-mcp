# Design

## Context

Two team applications each run their own Azure DevOps sync plus a local database for analysis.
- **A test results dashboard (Python/pandas):**
  - **Reads:** test plans; all runs of a plan; releases per definition; all builds with tags and branch; results per run with work items; `TestedBy` links.
  - **Computes:** the latest result per test case × configuration; counts per build and per release; burnup; split by test equipment (computer name).
- **A pull request analytics app (NestJS/Postgres):**
  - **Reads:**
    - all PRs of the last N months, per repository;
    - per PR the iterations, the changes per iteration and the threads;
    - reviewers from the PR object.
  - **Computes:**
    - overview, ranking per developer and per reviewer;
    - cycle time, comments per PR and per file, thread status counts;
    - first response, approval and rejection rate, monthly trends.
  - **Restriction:** text comments only.

Observed weaknesses that the server must avoid:
- full-history downloads with local filtering;
- one request per run for build tags, one request per iteration for changes;
- people joined by display name;
- `reviewed_at` never filled although vote events carry dates;
- line-count columns that are never filled.

The current MCP tools return raw, noisy JSON with no expansion and no aggregation.

## Goals / Non-Goals

**Goals:**
- The same questions both apps answer, as single MCP calls.
- Filters applied in the Azure DevOps request where the API allows it.
- Bounded request volume.
- Compact output.

**Non-Goals:**
- Local caching or sync.
- Content rating; the AI client does it from the returned text.
- Git content.
- Writes.
- Line counts.

## Decisions

### 1. Two layers in Core

```
Tools/Shape.cs      person {name, login}, voteText, shorten+scrub, webUrl, strip noise
Tools/Paging.cs     $top/$skip and continuation-token paging up to a limit,
                    7-day window walk (newest first, early stop), processed/truncated
Tools/Expand.cs     bounded parallel expansion (4 at a time, cap per call)
Tools/Aggregate.cs  rows → groups → metrics (count, sum, avg, median, p90, rate),
                    plus a total row; deterministic order
GitTools.cs, PipelineTools.cs, TestTools.cs, TraceTools.cs   layer 1 (objects)
AnalysisTools.cs                                              layer 2 (uses layer 1 loaders)
```

- **Shared loaders:** layer 1 tools are thin wrappers around loaders (`LoadPullRequestsAsync(filter, include)`, `LoadTestResultsAsync(selection)`). Layer 2 calls the same loaders, so a metric always uses exactly the data `get_*` would show.
- **Limits:** the analysis tools use higher internal limits (2000 PRs, of which at most 500 get details; 20000 results) because they return only aggregates. They still report `processed` and `truncated`.

### 2. Endpoints

api-version 6.0 unless noted; works on Server 2020+ and Services.

| Data | Request | Server-side filters used |
|---|---|---|
| Branches | `git/repositories/{repo}/stats/branches` | – (one call) |
| PRs | `git/pullrequests` (project) or `git/repositories/{repo}/pullrequests` | `searchCriteria.status, targetRefName`; `creatorId`/`reviewerId` when the filter is an ID. The period is filtered locally while walking the newest-first list; the walk stops at PRs created 180 days before `from` (close-date periods) or before `from` (active). Date parameters on the request need api 7.1 and are not used. |
| PR by ID | `git/pullrequests/{id}` | – |
| Threads | `.../pullRequests/{id}/threads` | – |
| Iterations | `.../pullRequests/{id}/iterations` | – |
| Files | `.../pullRequests/{id}/iterations/{last}/changes?$compareTo=0` | one call instead of one per iteration |
| PR work items | `.../pullRequests/{id}/workitems` | – |
| Build definitions | `build/definitions` | `name`, `path` |
| Builds | `build/builds` | `definitions, branchName, tagFilters, resultFilter, minTime, maxTime, queryOrder=finishTimeDescending` |
| Release definitions / releases | `release/definitions`, `release/releases?$expand=environments` | `definitionId, minCreatedTime, maxCreatedTime` |
| Test plans | `testplan/plans` | – |
| Test runs | `test/runs` query API | `minLastUpdatedDate, maxLastUpdatedDate (≤ 7 days), planIds, buildIds, releaseIds, releaseDefIds, branchName, runTitle, state` |
| Results | `test/Runs/{id}/results?detailsToInclude=WorkItems&$top=200&$skip=n` | `outcomes` |
| Latest result per test case (trace) | `POST test/points` with `pointsFilter.testcaseIds` (a query, writes nothing) | – |

**Release host:**
- **On Services:** the release API sits on `vsrm.dev.azure.com/{org}`. The host is derived from the collection URL.
- **On Server:** the release API is on the collection URL itself.
- **No endpoint:** a 404 on the release endpoint gives "Auf diesem Server nicht verfügbar".

**Test runs by release definition, pipeline or branch:** the run query takes `releaseDefIds`, `buildDefIds` and `branchName` directly, so these filters need no build data. Tags are not a run filter. With `tags`, the tool first loads the matching builds (`get_builds` filters, which needs Pipelines Lesen), then queries runs with `buildIds` in chunks of 50. This replaces the dashboard's "all builds + all runs + tags per run".

**Results by release or plan:** these use the same run query over the period (default the last 30 days) and then load the results of every run found (at most 1000 runs, 20000 results).

### 3. Derived fields
- **`cycleTimeHours`:** `closedDate − creationDate`, for completed PRs only.
- **`votedAt`:** the `publishedDate` of the newest system thread whose properties have `CodeReviewThreadType = VoteUpdate` and whose `CodeReviewVotedByIdentity` matches the reviewer.
- **`firstResponseHours`:** from `creationDate` to the earliest of the reviewer's first text comment and `votedAt`.
- **`latest=testCase+configuration`:** the newest `startedDate` per key; ties are broken by the higher result ID. Same rule as the dashboard.
- **`failingSince`:** taken from the result field when present. Otherwise the analysis computes it from the ordered groups.
- **System threads:** every comment has `commentType = system`, or the thread has a `CodeReviewThreadType` property.

### 4. People
- **Identity:** `login` = `uniqueName`, falling back to the ID. Grouping uses the login, and the display name shown is the most frequent name for that login.
- **Matching filters:** author and reviewer filters match the display name, the login or the ID. A name is resolved to IDs through the identities seen in the result.

### 5. Analysis engine
- **Row model:**
  - PR analysis: one row per PR. With `groupBy=reviewer` it becomes one row per PR × reviewer; with `file` or `threadStatus`, one row per PR × file or PR × thread status.
  - Test analysis: one row per result after `latest`.
- **`exclude`:**
  - **Title pattern:** a regex with a timeout and at most 200 characters.
  - **Authors:** matched by login or name.
  - **Result:** `excluded` is reported.
- **Metrics:** a fixed whitelist. Unknown names give an error listing the allowed ones.
- **Output:** `{ scope, rows[{key, ...metrics}], total, notes, processed, truncated }`, at most 500 rows (sorted by count descending).

### 6. trace_links
- **Search:** breadth-first with a visited set keyed by `kind:id`.
- **Sources:**
  - artifact links on work items (`vstfs:///Git/PullRequestId/{proj}%2F{repo}%2F{id}`, `Git/Commit`, `Git/Ref`, `Build/Build`);
  - `TestedBy` relations;
  - PR `workitems`;
  - commit `workItems`;
  - result work items and bugs.
- **Project check:** artifact links are compared with the selected project's ID; work items keep the existing `System.TeamProject` check.
- **`refPattern`:** the pattern is evaluated on text fields of the objects already loaded. It adds nodes of kind `ref` and edges with source "text", and triggers no extra searches.
- **Limits:** 500 nodes; at most 4 requests in parallel.

### 7. Pipelines area
- **Model:** `AccessArea.Pipelines`, inserted between Code and Test in `AccessAreas.Areas`, and `ToolGroup.Pipelines` → `AccessArea.Pipelines`.
- **Stored data:** areas are stored by name, so a missing key reads as "?" and no schema bump is needed.
- **PAT probe:** `GET build/builds?$top=1` in the probe project.
- **Project right:** the Build namespace action `ViewBuilds` on the project token. Fallback: the same read probe per project.
- **Schreiben:** always "n/a".
- **Permissions:** the `PermissionSettings` default for a missing group is "Erlaubt" for read groups, which already holds.
- **UI:**
  - The compact rights cells take a fifth column; the header becomes "WI L|S · Code · Pipe · Test · Iter".
  - The width per cell stays at 68 and the table grows by one column. The overview at 150 % scaling has to be checked manually, because it was the overflow case before.

## Risks / Trade-offs

- **Request volume on large projects:** 200 PRs with threads cost about 600 requests.
  - **Mitigation:** an include cap of 200, parallelism of 4, analysis caps, and `processed`/`truncated` in the result. Analysis without includes needs only the list.
- **PR date filters:** request-side date filters need api 7.1, which Server 2020 does not have.
  - **Mitigation:** walk the newest-first list and stop at `from` (active) or 180 days before `from` (closed). A PR open for more than 180 days before it closed inside the period is missed; this is accepted and stated in the spec.
- **Comment text reaches the AI client:** this is the requested function. The audit log stays metadata-only. Error texts are scrubbed.
- **Regex inputs:** a bad pattern could hang the server.
  - **Mitigation:** .NET `Regex` with a 100 ms match timeout and a length limit; a timeout gives an error for the call.
- **A fifth column in the UI:** the compact table gets wider. It needs a manual check at 150 % and, if needed, abbreviated headers.
