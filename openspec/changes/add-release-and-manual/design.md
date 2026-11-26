# Design

## Context

See `proposal.md` and `docs/konzept/release-und-handbuch.md` (work packages A–D). Current state:

- `build/build.ps1` publishes both executables into `artifacts/publish`, zips them to
  `artifacts/AzdoMcp-<v>-portable.zip` and runs `vpk pack` into `artifacts/releases`. Velopack also writes
  `AzdoMcp-win-Portable.zip` there, which `release.yml` uploads together with everything else.
- CI uses `dotnet-version: 10.0.x`, runs tests with `dotnet run --project tests/AzdoMcp.Tests` (xUnit v3 as EXE).
- Tools are registered in `ToolCatalog.All` (`src/AzdoMcp.Core/Tools/PlanningSystemTools.cs`); groups and modes
  have German display names in `ToolGroupNames.Display` and `PermissionModeNames.Display`.
- Footer texts come from `UpdateService.StatusText` and `MainViewModel.FooterInstall` ("Portable · ohne Admin").
- Reference implementation: app-launcher (`build/Build-Release.ps1`, `build/Build-Manual.ps1`,
  `build/ManualBuilder`, `tests/AppStart.Documentation.Tests`, workflows). app-launcher builds an MSI; this
  product does not.

## Goals / Non-Goals

**Goals:**
- Same release guarantees as app-launcher (tag check, signing, signature check, checksums, filtered assets)
  with the Velopack per-user setup and the portable zip.
- Manual that cannot silently drift from tools, modes, footer texts and CLI.

**Non-Goals:**
- MSI, machine-wide install, admin rights.
- Collecting all XAML messages in a catalog (chapter 09 is written by hand; the test does not cover it).
- Changing GitHub settings (already set), a "Handbuch" menu entry.
- Changing `src/` code.

## Decisions

### D1 Build script: rename and extend, keep `Find-Dotnet`
`git mv build/build.ps1 build/Build-Release.ps1`, then port the app-launcher structure (parameters, signing
setup, verification, checksums). `Find-Dotnet` stays because developers use the per-user SDK under
`%LOCALAPPDATA%`. Messages switch to English as in the reference.
Alternative: new script next to the old one – rejected, two scripts would drift.

### D2 Single output folder, pack folder inside it
All deliverables go to `-OutputDir` (default `artifacts/release`); the publish/pack folder moves to
`artifacts/pack`. Only the output folder is filtered and uploaded. This removes the special case for
`artifacts/*.zip` in `release.yml`.

### D3 Sign before zipping and before `vpk pack`
`vpk --signParams` signs only Velopack's own copies, so the executables in the pack folder are signed with
`signtool` first; then the portable zip is created from the signed folder, then `vpk pack` runs with
`--signParams "/sha1 <thumbprint> /s My /fd sha256 /tr <url> /td sha256"` (signs `Update.exe`, setup and package
content). The PFX is imported into `Cert:\CurrentUser\My` and removed in `finally`; the password is passed as
`SecureString` and never on a command line. `-PfxPath` and `-CertificateThumbprint` are mutually exclusive.

### D4 Signature verification reads the shipped files
After packaging the script checks `AzdoMcp-win-Setup.exe`, every `.exe` under `lib/app` in `*-full.nupkg`
(zip, extracted to a temp folder) and both executables in the portable zip with `Get-AuthenticodeSignature`:
status `Valid`, and `azdo-mcp.exe` / `AzdoMcp.*.exe` signed by the release thumbprint. Missing own executable
→ fail. Verification runs only when signing was requested.

### D5 Drop Velopack's portable zip
`AzdoMcp-win-Portable.zip` contains `Update.exe`; the spec's portable mode must not offer updates. The script
deletes it after `vpk pack`; the release filter would not match it anyway.

### D6 Release workflow 1:1 from app-launcher, MSI checks replaced
Steps: checkout tag, `setup-dotnet` with `global-json-file`, *Check version*, *Prepare signing certificate*,
*Trust self-signed test certificate (runner only)*, *Build, test, sign and package*, *Remove signing certificate*
(`if: always()`), *Create GitHub release*. Filter:
`\.(nupkg|json)$|^AzdoMcp-win-Setup\.exe$|^AzdoMcp-.+-portable\.zip$|^AzdoMcp-Handbuch-.+\.pdf$|^RELEASES$|^SHA256SUMS\.txt$`.
Mandatory: setup, portable zip, manual, checksums, full nupkg, `RELEASES`, `releases.win.json`,
`assets.win.json`. `concurrency.group` uses the tag so a manual re-run and the tag push do not race.

### D7 Manual builder copied from app-launcher
`build/ManualBuilder` (net10.0 console, namespace `AzdoMcp.ManualBuilder`, Markdig) and
`build/Build-Manual.ps1` (name `AzdoMcp-Handbuch`, version from `Directory.Build.props` unless `-Version`,
PDF via headless Edge, `-SkipPdf` for CI/Linux). ManualBuilder is **not** added to `AzdoMcp.sln`, so
`dotnet build AzdoMcp.sln` and the shipped executables are unaffected. Markdig is pinned once in
`Directory.Packages.props` (1.6.0); the CLAUDE.md package rule covers Core, Server and Manager.Core only.

### D8 Completeness test inside the existing test project
`ManualCompletenessTests` lives in `tests/AzdoMcp.Tests` (no new test project, test command unchanged). It
reads `docs/manual/*.md` from the repo root (found by walking up to `AzdoMcp.sln`), converts them to plain text
with Markdig and checks: `ToolCatalog.All` names in `07-tools.md`; `ToolGroup` display names and
`PermissionMode` display names anywhere; the four footer texts in chapter 10; `--stdio`, `discover`, `--apply`
in chapter 11. "Update verfügbar" is matched as prefix because the UI appends the version.
Alternative: separate `AzdoMcp.Documentation.Tests` like app-launcher – rejected, CI and CLAUDE.md would need a
second test command.

### D9 Manual in the package
`Build-Release.ps1` copies the PDF as `AzdoMcp-Handbuch.pdf` into the pack folder before zipping and packing,
so setup and portable zip both carry it. Without Edge (non-Windows) the release build fails; `-SkipPdf` is only
for `Build-Manual.ps1` in CI.

### D10 Images rendered from the artboards
`tools/render-images.mjs` (Node + Playwright, dev-only, not in CI) renders `docs/ui-proposal/*.dc.html` at
1720 px width to PNG into `docs/manual/images/` and `docs/assets/`. Artboards contain placeholder data only.
Missing images appear as placeholders in the manual and are listed in `docs/manual/images/README.md`. No app
icon exists yet: the README header leaves the slot empty and a task records it.

### D11 SDK pin and CI
`global.json` with `sdk.version 10.0.100`, `rollForward: latestFeature`, no `test.runner` block (tests stay on
`dotnet run`, switching to `dotnet test --solution` would need `Microsoft.Testing.Platform` configuration – out
of scope). CI cache key adds `**/global.json`; artifact `test-results-${{ runner.os }}`; new step
`./build/Build-Manual.ps1 -SkipPdf -OutputDir artifacts/manual` and upload `manual-html`.

## Risks / Trade-offs

- [Release flow cannot be fully tested locally on Linux] → script and workflow run only on Windows; the
  acceptance checks with a real tag and a test certificate are listed for the owner to run.
- [`github:` update source] → points to the public repo `trosinde/AzDO-mcp`; documented in chapter 10.
- [Self-signed test certificate trusted on the runner] → only on ephemeral runners, with a warning, as in
  app-launcher.
- [Manual chapters drift from XAML messages] → chapter 09 is hand-written; the gap is recorded in tasks.md
  (non-goal).
- [Build interface rename breaks local habits] → all references (README, docs, spec, CLAUDE.md) updated in the
  same change.

## Migration Plan

1. Merge the change; the next tag (`v2.5.0-rc.2`) uses the new workflow and may be unsigned.
2. Owner adds `SIGNING_PFX_BASE64` / `SIGNING_PFX_PASSWORD` before the first regular release.
3. Rollback: revert the merge; old `build.ps1` and `release.yml` come back, existing releases stay valid.

## Repository settings (already in place, not changed by this change)

Rulesets for `main` (PR required, no force push/delete, required checks "Build and test (Windows)" and
"OpenSpec validate", branches up to date), squash merge only, approval for first-time contributors. Set on
2026-11-26 together with secret scanning, push protection, Dependabot alerts and private vulnerability reporting.

## Open Questions

- Skill "dotnet deployment" (decision 1) was not available; if provided later, A4 and B are compared against
  it without changing the specs.
