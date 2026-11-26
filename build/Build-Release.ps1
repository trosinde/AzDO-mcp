<#
.SYNOPSIS
  Builds, tests, signs and packages AzDO MCP Manager. Same script locally and in the GitHub release workflow.

.DESCRIPTION
  1. dotnet restore / build / test (Release; tests skipped with -SkipTests)
  2. publishes azdo-mcp.exe and AzdoMcp.Manager.exe self-contained (win-x64) into artifacts\pack
  3. builds the user manual (build\Build-Manual.ps1) and copies it as AzdoMcp-Handbuch.pdf into the pack folder
  4. signs both executables in the pack folder with signtool (only with a certificate)
  5. zips the pack folder to <OutputDir>\AzdoMcp-<Version>-portable.zip (no Update.exe: updates stay off)
  6. vpk pack: per-user setup AzdoMcp-win-Setup.exe and the Velopack update files; Velopack's own
     AzdoMcp-win-Portable.zip is deleted (it contains Update.exe)
  7. with a certificate: verifies the signatures of what ships (setup, lib/app in the full package,
     portable zip)
  8. copies the manual as AzdoMcp-Handbuch-<Version>.pdf and writes SHA256SUMS.txt

  Output in -OutputDir (default artifacts\release):
    AzdoMcp-win-Setup.exe, AzdoMcp-<Version>-portable.zip, AzdoMcp-<Version>-full.nupkg (and delta),
    RELEASES, releases.win.json, assets.win.json, AzdoMcp-Handbuch-<Version>.pdf, SHA256SUMS.txt

  Signing: with -PfxPath the certificate is imported into Cert:\CurrentUser\My for the duration of the build
  and signtool / vpk sign by thumbprint (/sha1). The password is a SecureString and never appears on a
  command line or in a log. The certificate is removed again at the end, also on failure.
  -CertificateThumbprint uses a certificate that is already installed (local builds). Without either,
  nothing is signed and the script warns that the release is NOT signed.

  Fails with a non-zero exit code when any step fails, tests included. Cleans only the pack folder, its own
  work folder (artifacts\obj-release) and -OutputDir; other content below artifacts stays.

.PARAMETER Version
  Release version, e.g. 2.5.0 or 2.5.0-rc.2.
.PARAMETER PfxPath
  Code signing certificate as PFX file. Requires -PfxPassword. Not together with -CertificateThumbprint.
.PARAMETER PfxPassword
  Password of the PFX file as SecureString.
.PARAMETER CertificateThumbprint
  Thumbprint of a code signing certificate already installed in Cert:\CurrentUser\My.
.PARAMETER TimestampUrl
  RFC 3161 timestamp server used for all signatures.
.PARAMETER OutputDir
  Output folder for all release files, relative to the repository root or absolute.
.PARAMETER SkipTests
  Skips the unit tests (local packaging tests only).

.EXAMPLE
  ./build/Build-Release.ps1 -Version 2.5.0-rc.2
.EXAMPLE
  ./build/Build-Release.ps1 -Version 2.5.0 -PfxPath $env:RUNNER_TEMP\signing.pfx -PfxPassword $securePassword
.EXAMPLE
  ./build/Build-Release.ps1 -Version 2.5.0 -CertificateThumbprint 0123456789ABCDEF0123456789ABCDEF01234567
#>
[CmdletBinding(DefaultParameterSetName = 'Unsigned')]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')]
    [string] $Version,

    [Parameter(Mandatory, ParameterSetName = 'Pfx')]
    [string] $PfxPath,

    [Parameter(Mandatory, ParameterSetName = 'Pfx')]
    [securestring] $PfxPassword,

    [Parameter(Mandatory, ParameterSetName = 'Thumbprint')]
    [ValidatePattern('^[0-9A-Fa-f]{40}$')]
    [string] $CertificateThumbprint,

    [string] $TimestampUrl = 'http://timestamp.digicert.com',

    [string] $OutputDir = 'artifacts/release',

    [switch] $SkipTests
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$PSNativeCommandUseErrorActionPreference = $true

if (-not $IsWindows -and $PSVersionTable.PSEdition -eq 'Core') {
    throw 'Build-Release.ps1 needs Windows (WPF publish, vpk, signtool, Edge for the manual PDF).'
}

$Runtime = 'win-x64'
$OwnExes = @('azdo-mcp.exe', 'AzdoMcp.Manager.exe')
$SetupName = 'AzdoMcp-win-Setup.exe'
$ManualName = 'AzdoMcp-Handbuch'

$root = Split-Path $PSScriptRoot -Parent
$OutputDir = if ([IO.Path]::IsPathRooted($OutputDir)) { $OutputDir } else { Join-Path $root $OutputDir }
$artifacts = Join-Path $root 'artifacts'
$pack = Join-Path $artifacts 'pack'
$work = Join-Path $artifacts 'obj-release'
$portableZip = Join-Path $OutputDir "AzdoMcp-$Version-portable.zip"

function Find-Dotnet {
    # Developers often use the per-user SDK under %LOCALAPPDATA%; it wins over the one on PATH.
    $candidates = @()
    if ($env:LOCALAPPDATA) {
        $userSdk = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'
        if (Test-Path $userSdk) { $candidates += $userSdk }
    }
    $onPath = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($onPath) { $candidates += $onPath.Source }
    foreach ($c in $candidates) {
        $sdks = try { & $c --list-sdks 2>$null } catch { @() }
        if ($sdks -match '^10\.') { return $c }
    }
    throw '.NET 10 SDK not found (neither in %LOCALAPPDATA%\Microsoft\dotnet nor on PATH).'
}

function Invoke-Step([string] $Title, [scriptblock] $Action) {
    Write-Host ''
    Write-Host "==> $Title" -ForegroundColor Cyan
    $global:LASTEXITCODE = 0
    & $Action
    if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) { throw "$Title failed (exit $LASTEXITCODE)." }
}

function Assert-Signature([IO.FileInfo] $File, [string] $Thumbprint, [bool] $Own, [string] $Origin) {
    $sig = Get-AuthenticodeSignature $File.FullName
    if ($sig.Status -ne 'Valid') {
        throw "Signature check failed for $($File.Name) in $Origin ($($sig.Status))."
    }
    if ($Own -and $sig.SignerCertificate.Thumbprint -ne $Thumbprint) {
        throw "$($File.Name) in $Origin is signed by $($sig.SignerCertificate.Subject), not by the release certificate."
    }
}

$dotnet = Find-Dotnet
# Child scripts (Build-Manual.ps1) and 'dotnet vpk' use the same SDK.
$env:PATH = (Split-Path $dotnet -Parent) + [IO.Path]::PathSeparator + $env:PATH
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
Write-Host "dotnet: $dotnet"
Write-Host "Version: $Version"

$importedThumbprint = $null
Push-Location $root
try {
    Remove-Item $pack, $work, $OutputDir -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force $pack, $work, $OutputDir | Out-Null
    $common = @('-c', 'Release', "-p:Version=$Version", '--nologo')

    Invoke-Step 'Restore' { & $dotnet restore AzdoMcp.sln --nologo }
    Invoke-Step 'Build' { & $dotnet build AzdoMcp.sln @common --no-restore }
    if ($SkipTests) {
        Write-Warning 'Tests skipped (-SkipTests).'
    } else {
        Invoke-Step 'Test' { & $dotnet run --project tests/AzdoMcp.Tests -c Release --no-build }
    }

    # Both executables go into one folder; self-contained so no .NET runtime is needed.
    $publish = $common + @('-r', $Runtime, '--self-contained', 'true', '-p:PublishReadyToRun=false',
                           '-p:DebugType=none', '-o', $pack)
    Invoke-Step 'Publish azdo-mcp.exe' { & $dotnet publish src/AzdoMcp.Server/AzdoMcp.Server.csproj @publish }
    Invoke-Step 'Publish AzdoMcp.Manager.exe' { & $dotnet publish src/AzdoMcp.Manager/AzdoMcp.Manager.csproj @publish }
    foreach ($exe in $OwnExes) {
        if (-not (Test-Path (Join-Path $pack $exe))) { throw "$exe missing in the pack folder." }
    }

    # The manual ships inside setup and portable zip, and as a separate release asset.
    $manualDir = Join-Path $work 'manual'
    Invoke-Step 'Manual (PDF)' {
        & (Join-Path $PSScriptRoot 'Build-Manual.ps1') -Version $Version -OutputDir $manualDir
    }
    $manualPdf = Join-Path $manualDir "$ManualName.pdf"
    if (-not (Test-Path $manualPdf)) { throw "Manual PDF missing: $manualPdf" }
    Copy-Item $manualPdf (Join-Path $pack "$ManualName.pdf")
    Get-ChildItem $pack -Recurse -Filter '*.pdb' | Remove-Item

    # --- signing setup (no secret on any command line)
    $signArgs = @()
    if ($PfxPath) {
        $cert = Import-PfxCertificate -FilePath $PfxPath -CertStoreLocation Cert:\CurrentUser\My -Password $PfxPassword
        $importedThumbprint = $cert.Thumbprint
        $CertificateThumbprint = $cert.Thumbprint
        Write-Host "Signing certificate: $($cert.Subject), valid until $($cert.NotAfter.ToString('yyyy-MM-dd'))"
    }
    if ($CertificateThumbprint) {
        $CertificateThumbprint = $CertificateThumbprint.ToUpperInvariant()
        $signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue |
            Sort-Object FullName -Descending | Select-Object -First 1
        if (-not $signtool) { throw 'signtool.exe not found (Windows SDK).' }
        # vpk --signParams signs only Velopack's own copies, so the pack folder (portable zip) is signed here.
        $exePaths = @($OwnExes | ForEach-Object { Join-Path $pack $_ })
        Invoke-Step 'Sign executables (pack folder)' {
            & $signtool.FullName sign /sha1 $CertificateThumbprint /s My /fd sha256 /tr $TimestampUrl /td sha256 @exePaths
        }
        $signArgs = @('--signParams', "/sha1 $CertificateThumbprint /s My /fd sha256 /tr $TimestampUrl /td sha256")
    }

    # Portable: plain folder, no Update.exe; the Manager detects it and turns updates off.
    Invoke-Step 'Portable zip' {
        Compress-Archive -Path (Join-Path $pack '*') -DestinationPath $portableZip -Force
    }

    Invoke-Step 'Restore tools (vpk)' { & $dotnet tool restore }
    Invoke-Step 'vpk pack (per user)' {
        & $dotnet vpk pack `
            --packId AzdoMcp `
            --packVersion $Version `
            --packDir $pack `
            --mainExe AzdoMcp.Manager.exe `
            --packTitle 'AzDO MCP Manager' `
            --packAuthors 'AzdoMcp' `
            --runtime $Runtime `
            --shortcuts StartMenuRoot `
            --outputDir $OutputDir `
            --yes @signArgs
    }

    # Velopack's portable zip contains Update.exe; the portable mode must not offer updates.
    Get-ChildItem $OutputDir -Filter 'AzdoMcp-win-Portable.zip' | Remove-Item
    $setup = Join-Path $OutputDir $SetupName
    if (-not (Test-Path $setup)) { throw "vpk did not produce $SetupName." }
    $nupkg = Get-ChildItem $OutputDir -Filter '*-full.nupkg' | Select-Object -First 1
    if (-not $nupkg) { throw 'vpk did not produce a full package.' }

    if ($CertificateThumbprint) {
        # Check what actually ships: setup, every EXE in the full package (lib/app) and the portable zip.
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $checked = 0

        Assert-Signature (Get-Item $setup) $CertificateThumbprint $true 'output folder'
        $checked++

        $sources = @(
            @{ Origin = $nupkg.Name; Archive = $nupkg.FullName; Folder = 'lib/app' },
            @{ Origin = (Split-Path $portableZip -Leaf); Archive = $portableZip; Folder = '.' }
        )
        $i = 0
        foreach ($source in $sources) {
            $extract = Join-Path $work "verify-$i"
            $i++
            New-Item -ItemType Directory -Force $extract | Out-Null
            [System.IO.Compression.ZipFile]::ExtractToDirectory($source.Archive, $extract)
            $folder = Join-Path $extract $source.Folder
            if (-not (Test-Path $folder)) { throw "$($source.Folder) missing in $($source.Origin)." }
            $exes = @(Get-ChildItem $folder -Recurse -Filter '*.exe')
            foreach ($required in $OwnExes) {
                if (-not ($exes | Where-Object Name -eq $required)) { throw "$required missing in $($source.Origin)." }
            }
            foreach ($file in $exes) {
                $own = $file.Name -eq 'azdo-mcp.exe' -or $file.Name -like 'AzdoMcp.*.exe'
                Assert-Signature $file $CertificateThumbprint $own $source.Origin
                $checked++
            }
        }
        Write-Host "Signatures verified: $checked executables (setup, $($nupkg.Name), portable zip)."
    }

    # The manual also as a release asset, readable without installing (same file as in the package).
    Copy-Item $manualPdf (Join-Path $OutputDir "$ManualName-$Version.pdf")

    $sums = Get-ChildItem $OutputDir -File | Where-Object Name -ne 'SHA256SUMS.txt' | Sort-Object Name | ForEach-Object {
        '{0}  {1}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name
    }
    Set-Content (Join-Path $OutputDir 'SHA256SUMS.txt') $sums -Encoding ascii

    if (-not $CertificateThumbprint) {
        Write-Warning 'No certificate: the release is NOT signed (only acceptable for prereleases and local tests).'
    }
    Write-Host ''
    Write-Host "==> Done: $OutputDir" -ForegroundColor Green
    Get-ChildItem $OutputDir -File | ForEach-Object { Write-Host ('    {0,-45} {1,12:N0} bytes' -f $_.Name, $_.Length) }
}
finally {
    if ($importedThumbprint) {
        Remove-Item "Cert:\CurrentUser\My\$importedThumbprint" -DeleteKey -ErrorAction SilentlyContinue
        Write-Host 'Signing certificate removed from CurrentUser\My.'
    }
    Pop-Location
}
