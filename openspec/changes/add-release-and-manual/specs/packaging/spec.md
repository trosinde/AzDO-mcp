## MODIFIED Requirements

### Requirement: Build script
`build\Build-Release.ps1 -Version <x.y.z>` SHALL restore, build, run the tests (unless `-SkipTests`), publish both executables, build the user manual and produce in one output folder (default `artifacts\release`, set with `-OutputDir`) the setup `AzdoMcp-win-Setup.exe`, the portable zip `AzdoMcp-<x.y.z>-portable.zip`, the Velopack update files, the manual `AzdoMcp-Handbuch-<x.y.z>.pdf` and `SHA256SUMS.txt`. Signing is optional and enabled by `-PfxPath` or `-CertificateThumbprint`. The script SHALL fail with a non-zero exit code if any step fails.

#### Scenario: Failing test stops build
- **WHEN** a unit test fails
- **THEN** the script stops before packaging and exits non-zero

#### Scenario: Unsigned local build
- **WHEN** `build\Build-Release.ps1 -Version 2.5.0-rc.2` runs without a certificate
- **THEN** `artifacts\release` contains `AzdoMcp-win-Setup.exe`, `AzdoMcp-2.5.0-rc.2-portable.zip`, `AzdoMcp-2.5.0-rc.2-full.nupkg`, `RELEASES`, `releases.win.json`, `assets.win.json`, `AzdoMcp-Handbuch-2.5.0-rc.2.pdf` and `SHA256SUMS.txt`, contains no `AzdoMcp-win-Portable.zip`, and the script prints the warning "release is NOT signed"

#### Scenario: Checksums cover all files
- **WHEN** the build finishes
- **THEN** `SHA256SUMS.txt` lists the SHA-256 hash of every other file in the output folder, one line per file as `<lowercase hex>  <file name>`

## ADDED Requirements

### Requirement: Signed release
When a certificate is given, the build SHALL sign the setup and `azdo-mcp.exe` and `AzdoMcp.Manager.exe` in the portable zip and in the Velopack package, with a timestamp, and SHALL then verify that every executable in setup, full package and portable zip has a valid signature and that both own executables carry the release certificate; otherwise it SHALL fail.

#### Scenario: Signed build passes verification
- **WHEN** the build runs with a valid certificate
- **THEN** all executables in setup, full package and portable zip have a valid signature from that certificate and the build succeeds

#### Scenario: Tampered executable stops build
- **WHEN** an own executable in the package is unsigned or signed by another certificate
- **THEN** the build fails and names the file

### Requirement: Regular releases are signed
A regular release (tag without `-`) SHALL NOT be published unsigned; a prerelease MAY be published unsigned with a warning.

#### Scenario: Regular tag without certificate
- **WHEN** tag `v2.5.0` is pushed and the secret `SIGNING_PFX_BASE64` is missing
- **THEN** the release workflow fails before a GitHub release is created

#### Scenario: Prerelease without certificate
- **WHEN** tag `v2.5.0-rc.2` is pushed and the secret `SIGNING_PFX_BASE64` is missing
- **THEN** the workflow publishes an unsigned prerelease and logs a warning

### Requirement: Release assets
A GitHub release SHALL contain exactly `AzdoMcp-win-Setup.exe`, `AzdoMcp-<v>-portable.zip`, `AzdoMcp-Handbuch-<v>.pdf`, `SHA256SUMS.txt`, `AzdoMcp-<v>-full.nupkg`, `RELEASES`, `releases.win.json`, `assets.win.json` and, if built, `*-delta.nupkg`. If one of the mandatory assets (all except the delta package) is missing, the workflow SHALL fail without creating the release. Tags with `-` SHALL be published as prerelease.

#### Scenario: Prerelease with mandatory assets
- **WHEN** tag `v2.5.0-rc.2` is pushed
- **THEN** a prerelease "AzDO MCP Manager v2.5.0-rc.2" is created with the assets listed above and without `AzdoMcp-win-Portable.zip`

#### Scenario: Missing manual stops release
- **WHEN** `AzdoMcp-Handbuch-<v>.pdf` is not in the output folder
- **THEN** the workflow fails and no release is created

#### Scenario: Invalid tag
- **WHEN** the release workflow runs for a tag that does not match `v<major>.<minor>.<patch>[-<suffix>]`
- **THEN** it fails before building
