# Tasks

## 1. Groundwork
- [x] 1.1 Split `Tools/OtherTools.cs` into `GitTools.cs`, `TestTools.cs`, `PlanningSystemTools.cs` without behaviour change. Verify: existing tests green (`dotnet run --project tests/AzdoMcp.Tests`).
- [x] 1.2 Add the shared helpers:
  - `Shape.cs` (person, voteText, shorten+scrub, noise stripping);
  - `Paging.cs` (skip and continuation paging, 7-day window walk, processed/truncated);
  - `Expand.cs` (bounded parallel expansion).

  Verify: `PagingTests` (pages merged, stop at limit, windows gap-free and de-duplicated), `ShapeTests` (vote mapping, no avatar/descriptor, scrubbed error text).
- [x] 1.3 Add `CanRead(Area)` for mixed-area tools with the note text "<Bereich>: kein Lesezugriff – ausgelassen". Verify: `PermissionTests.Mixed_tool_leaves_out_denied_area`, `PermissionTests.Unknown_right_does_not_restrict`.

## 2. Pipelines area
- [x] 2.1 Add `AccessArea.Pipelines`, `ToolGroup.Pipelines` and the mapping, plus the display name "Pipelines"; a missing stored area reads as "?". Verify: `AccessModelTests.Pipelines_missing_reads_unknown`, `PermissionTests.Pipelines_group_default_allowed`.
- [x] 2.2 Add the PAT probe `build/builds?$top=1` to the connection tester and discovery, the Build `ViewBuilds` project right with its fallback probe, and Pipelines Schreiben "n/a". Verify: `DiscoveryTests.Pipelines_scope_probe`, `DiscoveryTests.Builds_view_denied`, `ConnectionTesterTests.Missing_build_scope`.
- [ ] 2.3 Manager:
  - the Pipelines column in the overview, the scope grid, the wizard projects step and the rescan changes;
  - the Pipelines group in the wizard project step and in "Tools & Rechte";
  - the footnote text.

  Verify: `ManagerTests` for rows and columns, and a manual check of the overview at 150 % scaling.
  - Done: the ManagerTests are adapted and green.
  - Open: the manual check at 150 %, which needs the installed Manager.

## 3. Layer 1 – Git
- [x] 3.1 Add `get_branches`. Verify: `ToolTests.Branches_ahead_behind`, `ToolTests.Stale_branches_without_pr`.
- [x] 3.2 Rework `get_pull_requests`:
  - filters, applied server-side with the old-server fallback walk;
  - the compact list and `cycleTimeHours`.

  Verify: `ToolTests.Pull_requests_completed_in_period_across_pages`, `ToolTests.Pull_requests_date_walk_on_old_server`, `ToolTests.Pull_requests_truncated`, `ToolTests.Pull_request_has_no_identity_noise`.
- [x] 3.3 Add `include`:
  - reviewers with `votedAt` and `firstResponseHours`;
  - threads with the system split and count;
  - iterations, files (one compare call), workItems;
  - the cap of 200 with its note.

  Verify: `ToolTests.Vote_time_from_vote_event`, `ToolTests.System_threads_counted_not_listed`, `ToolTests.Files_across_pushes_once`, `ToolTests.Include_cap_note`.
- [x] 3.4 Add `get_pull_request` by ID with all includes. Verify: `ToolTests.Pull_request_by_id`, `ToolTests.Pull_request_unknown_id`.

## 4. Layer 1 – Pipelines and Test
- [x] 4.1 Add `get_pipelines`, `get_builds` with server-side filters and newest first, and `get_releases` with its host derivation and "nicht verfügbar". Verify: `ToolTests.Builds_by_tag_filter_in_request`, `ToolTests.Releases_host_on_services`, `ToolTests.Releases_not_available`.
- [x] 4.2 Add `get_test_plans`, and rework `get_test_runs`:
  - the query API with the window walk and newest first;
  - all filters, with the branch and tags path via builds in chunks of 50;
  - build and release references in the output.

  Verify: `ToolTests.Test_runs_newest_first`, `ToolTests.Test_runs_by_release_definition`, `ToolTests.Test_runs_by_branch_via_builds`, `ToolTests.Test_runs_long_period_split`.
- [x] 4.3 Rework `get_test_results`:
  - exactly one of run, build, release or plan;
  - paging, `latest`, and the outcome counts first;
  - the full result content with a shortened error text.

  Verify: `ToolTests.Latest_per_testcase_configuration`, `ToolTests.Results_selection_ambiguous`, `ToolTests.Result_has_linked_bug`.

## 5. Links
- [x] 5.1 Add `trace_links`:
  - start points, include kinds, depth, breadth-first search;
  - nodes, edges and summary lines.

  Verify: `ToolTests.Trace_story_to_prs_and_tests`, `ToolTests.Trace_pr_to_work_items`.
- [x] 5.2 Add `refPattern` (length limit, 100 ms timeout, "text" edges), the project ID check, the foreign count, the 500-node cap and the area rights. Verify: `ToolTests.Trace_ref_pattern_in_commits_and_tests`, `ToolTests.Trace_bad_pattern_times_out`, `ToolTests.Trace_foreign_link_counted`, `ToolTests.Trace_without_test_right`.

## 6. Layer 2 – Analysis
- [x] 6.1 Add `Aggregate.cs`: groups, the metric whitelist, avg/median/p90/rate, the total row, ordering and the 500-row cap. Verify: `AggregateTests` (median/p90 on even and odd counts, empty groups, unknown metric error).
- [x] 6.2 Add `analyze_pull_requests`:
  - the row model per groupBy and `exclude` with its count;
  - grouping by login and text comments only;
  - all metrics.

  Verify: `AnalysisTests.Developer_ranking`, `AnalysisTests.Reviewer_rates_and_first_response`, `AnalysisTests.Comments_per_file`, `AnalysisTests.Test_prs_excluded`, `AnalysisTests.Two_names_one_login`.
- [x] 6.3 Add `analyze_test_results`:
  - the selection, with pipeline, branch and tags via builds;
  - `latest` per group and groupBy;
  - the counts and pass rate, `failingSince` and `newFailures`.

  Verify: `AnalysisTests.Results_per_build_latest`, `AnalysisTests.New_failure_between_builds`, `AnalysisTests.Failed_counts_aborted`.

## 7. Catalogue, fakes, docs
- [x] 7.1 Register the 31 tools in 7 groups, update the group tests and the Tools & Rechte tree, and add the analysis hint to `ServerInstructions`. Verify: `ToolTests.Group_membership_is_fixed`, the `ServerProtocolTests` tool list.
- [x] 7.2 Add fake endpoints for every request in design §2, with placeholder data only (Max Muster, azdo.example.local).
  - **How:** tool tests use `FakeHttp` routes (helpers `Az.*` in `AnalysisToolTests.cs`); discovery uses `FakeAzdo` with the build endpoint and Build namespace.
  - **Verify:** used by the tests above; `grep` for real hosts finds nothing.
- [x] 7.3 Update the README tool list and analysis examples, change "22 tools" to 31 in CLAUDE.md, and validate the specs. Verify: `openspec validate --all --strict` passes.
- [x] 7.4 Run a manual stdio check of `get_pull_request`, `analyze_pull_requests groupBy=author`, `get_test_results`, and `analyze_test_results` with `groupBy=release` and `groupBy=stage`.
  - **How:** the check ran against the live server (task 7.5) instead of the fake.
  - **Verify:** compact output was inspected: 18 KB for a PR with all details; 10 KB for 736 results grouped by release.
- [x] 7.5 Run a read-only live check with the Debug build against two real projects, one for PRs and one for tests.
  - **Fixes from it:** run details for the build reference, a 1-day first window, stage names via the release, and `firstResponse` without deleted comments.
  - **Open:** the figures were not compared one by one with the two team applications.
  - **Verify:** manual; no data was written to the repo.
