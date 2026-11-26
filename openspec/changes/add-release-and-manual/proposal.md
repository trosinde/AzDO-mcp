# Proposal

## Why

Releases of AzDO-mcp are built by `build/build.ps1` without a tag check, code signing, signature
verification, checksums or a user manual, and the release workflow uploads everything in `artifacts/releases`
plus a second portable zip (`v2.5.0-rc.1` is an unsigned prerelease). The README is a technical reference rather
than an entry point for users. This change aligns the release chain, manual and README with the reference repos
(app-launcher, Annota) as described in `docs/konzept/release-und-handbuch.md`, while keeping the product
specifics: per-user Velopack setup, portable zip, no MSI, no admin rights.

## What Changes

- `global.json` pins the SDK (`10.0.100`, `rollForward: latestFeature`); CI and release read it.
- CI: test artifact named `test-results-${{ runner.os }}`, new step that builds the manual HTML
  (`Build-Manual.ps1 -SkipPdf`) and uploads it as `manual-html`; the `openspec` job stays.
- New repo files: `CONTRIBUTING.md`, `SECURITY.md`, `.github/pull_request_template.md`. `LICENSE` (MIT) is already
  in the repository.
- **BREAKING** (build interface only): `build/build.ps1` is renamed to `build/Build-Release.ps1` with the
  app-launcher parameters (`-Version`, `-PfxPath`, `-PfxPassword`, `-CertificateThumbprint`, `-TimestampUrl`,
  `-OutputDir`, `-SkipTests`). Output moves from `artifacts/releases` + `artifacts/*.zip` to one folder,
  default `artifacts/release`.
- Release script signs both executables and the Velopack output when a certificate is given, verifies every
  signature, deletes the duplicate `AzdoMcp-win-Portable.zip`, adds the manual PDF and writes `SHA256SUMS.txt`.
- `release.yml` follows app-launcher: tag check, signing certificate from `SIGNING_PFX_BASE64` (mandatory for
  regular releases, prereleases may be unsigned), asset filter and mandatory-asset check.
- New German user manual under `docs/manual/` (11 chapters + images), built by `build/ManualBuilder` and
  `build/Build-Manual.ps1` into `AzdoMcp-Handbuch-<v>.pdf`; shipped as release asset and inside setup and
  portable zip.
- New test `ManualCompletenessTests` in `tests/AzdoMcp.Tests`: every registered tool, tool group, permission mode,
  update footer text and CLI command is described in the manual.
- README rewritten from the user's point of view (Annota structure, German); technical content moves to the
  manual, `docs/entwicklung.md` and `docs/README.md`. README images rendered from `docs/ui-proposal/`.
- `Markdig` (1.6.0) pinned in `Directory.Packages.props`, referenced only by `build/ManualBuilder` and
  `tests/AzdoMcp.Tests` (not by Core, Server or Manager.Core).
- Out of scope: GitHub repository settings (listed for the owner, see design), a "Handbuch" menu entry in the
  tray or Start menu (decision 5); the repository is already public with its settings in place.

## Capabilities

### New Capabilities

- `documentation`: user manual shipped with every release, its completeness against the software, and the README
  as the user-facing entry point.

### Modified Capabilities

- `packaging`: requirement "Build script" changes (new name, single output folder, manual, checksums, optional
  signing); new requirements "Signed release" and "Release assets".

## Impact

- Code: none in `src/` (the manual PDF is added at packaging time; no UI change).
- Build: `build/build.ps1` → `build/Build-Release.ps1`; new `build/Build-Manual.ps1`, `build/ManualBuilder/`,
  `global.json`, `tools/` render script for images.
- CI/CD: `.github/workflows/ci.yml`, `.github/workflows/release.yml`; new secrets `SIGNING_PFX_BASE64`,
  `SIGNING_PFX_PASSWORD` (set by the owner). Without them only prereleases (`-rc`) can be published.
- Tests: new `ManualCompletenessTests`; `tests/AzdoMcp.Tests` gains a `Markdig` reference.
- Docs: `README.md`, `docs/README.md`, `docs/entwicklung.md`, `docs/manual/`, `docs/assets/`, `CLAUDE.md`
  (commands, layout), all references to `build.ps1`.
- Release assets: names `AzdoMcp-win-Setup.exe`, `AzdoMcp-<v>-portable.zip`, `AzdoMcp-<v>-full.nupkg`, `RELEASES`,
  `releases.win.json`, `assets.win.json` stay, so the Velopack `github:` update source keeps working;
  `AzdoMcp-win-Portable.zip` is no longer published.
