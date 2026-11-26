# Tasks

## 1. SDK pin and CI

- [x] 1.1 Add `global.json` (`sdk.version` `10.0.100`, `rollForward: latestFeature`, no `test.runner`); verify `dotnet --version` in the repo resolves a 10.0.x SDK and `dotnet build AzdoMcp.sln` succeeds
- [ ] 1.2 `ci.yml`: `setup-dotnet` with `global-json-file: global.json`, cache key adds `**/global.json`, test artifact `test-results-${{ runner.os }}`; verify CI run on the PR is green and shows artifact `test-results-Windows`

## 2. Repository files

- [x] 2.1 Add `CONTRIBUTING.md` (maintainer @trosinde, fork + branch + PR, local test command `dotnet run --project tests/AzdoMcp.Tests -c Release`, review after green required checks, branch up to date, squash merge, vulnerabilities → SECURITY.md); verify every command in it runs as written
- [x] 2.2 Add `SECURITY.md` (report only via GitHub Security Advisories, no public issue); verify link target `https://github.com/trosinde/AzDO-mcp/security/advisories/new` is used
- [x] 2.3 Add `.github/pull_request_template.md` («Was ändert sich?» + checklist: tests, local test run, `openspec validate --all --strict`, CI green); verify by opening the draft PR with it

## 3. Release script

- [x] 3.1 `git mv build/build.ps1 build/Build-Release.ps1`; port parameters `-Version`, `-PfxPath`, `-PfxPassword` (SecureString), `-CertificateThumbprint`, `-TimestampUrl`, `-OutputDir` (default `artifacts/release`), `-SkipTests`, keep `Find-Dotnet`, English messages; verify `pwsh -c "Get-Help ./build/Build-Release.ps1 -Parameter *"` lists them and `-PfxPath` with `-CertificateThumbprint` is rejected
- [ ] 3.2 Pack folder `artifacts/pack`, sign both executables there with `signtool` when a certificate is given (PFX imported to `Cert:\CurrentUser\My`, removed in `finally`), then portable zip into `-OutputDir`; verify on Windows the zip contains `azdo-mcp.exe` and `AzdoMcp.Manager.exe`
- [ ] 3.3 `vpk pack` into `-OutputDir` with `--signParams` when signing, delete `AzdoMcp-win-Portable.zip`; verify output has no `AzdoMcp-win-Portable.zip`
- [ ] 3.4 Signature verification of setup, `lib/app/*.exe` in `*-full.nupkg` and the portable zip (status `Valid`, own executables with release thumbprint, missing executable fails); verify with a self-signed test certificate: green build, and a manipulated EXE fails the build (manual check, Windows)
- [ ] 3.5 Write `SHA256SUMS.txt` (`<lowercase hex>  <name>`, all other files in `-OutputDir`); without certificate print warning "release is NOT signed"; verify `./build/Build-Release.ps1 -Version 2.5.0-rc.2` (Windows, no certificate) produces the file set of spec scenario "Unsigned local build" except the manual (added in 5.4)
- [x] 3.6 Update spec text references, README, CLAUDE.md and other docs from `build.ps1` to `Build-Release.ps1`; verify `grep -rn "build.ps1" --exclude-dir=openspec/changes/archive .` finds no stale reference

## 4. Release workflow

- [x] 4.1 Rewrite `release.yml` after app-launcher (D6): `global-json-file`, *Check version*, *Prepare signing certificate*, *Trust self-signed test certificate (runner only)*, *Build, test, sign and package*, *Remove signing certificate* `if: always()`, concurrency group by tag; verify with `actionlint` if available, else by review against app-launcher
- [ ] 4.2 *Create GitHub release*: filter regex, mandatory-asset checks (setup, portable zip, manual, checksums, full nupkg, `RELEASES`, `releases.win.json`, `assets.win.json`), title `AzDO MCP Manager $TAG`, `--generate-notes --verify-tag`, prerelease for `-`; verify by owner: tag `v2.5.0-rc.2` creates a prerelease with exactly these assets; regular tag without `SIGNING_PFX_BASE64` fails before publishing

## 5. Manual tooling

- [x] 5.1 Pin `Markdig` 1.6.0 in `Directory.Packages.props`; verify `dotnet restore AzdoMcp.sln` succeeds and Core/Server/Manager.Core have no Markdig reference
- [x] 5.2 Copy `build/ManualBuilder` from app-launcher (namespace `AzdoMcp.ManualBuilder`, German texts unchanged, not in `AzdoMcp.sln`); verify `dotnet build build/ManualBuilder` succeeds with warnings as errors
- [x] 5.3 Copy `build/Build-Manual.ps1` (name `AzdoMcp-Handbuch`, version from `Directory.Build.props` or `-Version`, Edge PDF, `-SkipPdf`); verify `pwsh ./build/Build-Manual.ps1 -SkipPdf` writes `artifacts/manual/AzdoMcp-Handbuch.html` on Linux
- [ ] 5.4 `Build-Release.ps1` builds the PDF, puts `AzdoMcp-Handbuch.pdf` into the pack folder and `AzdoMcp-Handbuch-<v>.pdf` into `-OutputDir`; verify on Windows the PDF is in setup content, portable zip and output folder
- [ ] 5.5 CI step `./build/Build-Manual.ps1 -SkipPdf -OutputDir artifacts/manual` and upload `manual-html`; verify CI run shows artifact `manual-html`

## 6. Manual content

- [x] 6.1 Write `docs/manual/index.md` and chapters 01–06 (overview, prerequisites, installation, connections and projects, MCP clients, permissions) in German, Sie-Form, Swiss spelling, placeholders only; verify HTML build and spot check against specs `mcp-server`, `connections`, `packaging`, `setup-wizard`, `client-setup`, `tool-permissions`
- [x] 6.2 Write chapters 07–11 (tools, log and data, messages and troubleshooting, update and uninstall, reference); verify HTML build and that every tool in `ToolCatalog.All` appears in 07 (checked by 7.1)
- [x] 6.3 Add `tools/render-images.mjs` (Playwright, 1720 px) and render manual images from `docs/ui-proposal/` into `docs/manual/images/`; list missing images in `docs/manual/images/README.md`; verify HTML shows images or placeholders and no image contains real names, URLs or PATs (manual check)

## 7. Completeness test

- [x] 7.1 Add `ManualCompletenessTests` to `tests/AzdoMcp.Tests` (Markdig reference; tools from `ToolCatalog.All` in `07-tools.md`; group and mode display names; footer texts in chapter 10; `--stdio`, `discover`, `--apply` in chapter 11; message names text and source); verify `dotnet run --project tests/AzdoMcp.Tests` passes, and fails naming the tool when one tool line is removed from `07-tools.md` (manual check, then restore)
- [x] 7.2 Record in this file that XAML messages are not covered by the test (non-goal D8); verify note present

## 8. README and developer docs

- [x] 8.1 Render README images (`hero.png`, `feature-1-erkennung.png` … `feature-4-protokoll.png`) into `docs/assets/` with the script from 6.3; verify files exist and show placeholder data only
- [x] 8.2 Rewrite `README.md` (blocks 1–9 of the concept, German, Sie-Form, no app icon yet → slot empty and noted below); verify it contains no build steps or tool tables and all relative links resolve (link check script or manual)
- [x] 8.3 Add `docs/entwicklung.md` (prerequisites, build, tests, release, workflow table, project layout, OpenSpec workflow) and `docs/README.md` (document / content / audience); verify every command in `docs/entwicklung.md` matches CI and `Build-Release.ps1`

## 9. Wrap-up

- [x] 9.1 Update `CLAUDE.md` commands and layout (`build/`, `docs/manual/`, manual build command); verify `openspec validate --all --strict` is green and `dotnet build AzdoMcp.sln` plus `dotnet run --project tests/AzdoMcp.Tests` pass
- [x] 9.2 Report the GitHub settings from design "Settings for the owner" and the acceptance checks that need Windows, secrets or a tag; verify the report lists each acceptance item of the concept with status

## Notes

- Open (needs Windows, a CI run or a tag; checked by the owner): 1.2, 3.2–3.5, 4.2, 5.4, 5.5. Linux checks done: parse checks, parameter sets, early Windows exit, asset filter and SHA256SUMS unit check, YAML parse.
- Asset filter uses case-sensitive `-cmatch`: `-match` would match Velopack's `AzdoMcp-win-Portable.zip`.

- Open gap: XAML messages are not collected in a catalog; chapter 09 is maintained by hand (see 7.2).
- Open: no app icon for the README header.

## Workflow follow-up

- Archive the change after the PR is merged and the `v2.5.0-rc.2` prerelease was checked.
