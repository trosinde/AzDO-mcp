<#
.SYNOPSIS
  Builds, tests and packages AzDO MCP Manager.

.DESCRIPTION
  Produces
    artifacts\releases\AzdoMcp-win-Setup.exe      Velopack installer, per user, no UAC
    artifacts\AzdoMcp-<Version>-portable.zip      runs without installation
  Writes only below .\artifacts. Fails (exit code 1) when any step fails, tests included.

.EXAMPLE
  .\build\build.ps1 -Version 2.0.0
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')]
    [string]$Version,

    [string]$Configuration = 'Release',

    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $root 'artifacts'
$publish = Join-Path $artifacts 'publish'
$releases = Join-Path $artifacts 'releases'
$portableZip = Join-Path $artifacts "AzdoMcp-$Version-portable.zip"

function Find-Dotnet {
    $candidates = @()
    $userSdk = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'
    if (Test-Path $userSdk) { $candidates += $userSdk }
    $onPath = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($onPath) { $candidates += $onPath.Source }
    foreach ($c in $candidates) {
        $sdks = & $c --list-sdks 2>$null
        if ($sdks -match '^10\.') { return $c }
    }
    throw '.NET 10 SDK nicht gefunden (weder %LOCALAPPDATA%\Microsoft\dotnet noch PATH).'
}

function Invoke-Step([string]$Title, [scriptblock]$Block) {
    Write-Host ""
    Write-Host "==> $Title" -ForegroundColor Cyan
    & $Block
    if ($LASTEXITCODE -ne 0) { throw "$Title fehlgeschlagen (Exit-Code $LASTEXITCODE)." }
}

$dotnet = Find-Dotnet
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
Write-Host "dotnet: $dotnet"
Write-Host "Version: $Version"

Push-Location $root
try {
    if (Test-Path $artifacts) { Remove-Item -Recurse -Force $artifacts }
    New-Item -ItemType Directory -Force -Path $publish, $releases | Out-Null

    Invoke-Step 'Restore' { & $dotnet restore AzdoMcp.sln }
    Invoke-Step 'Build' { & $dotnet build AzdoMcp.sln -c $Configuration --no-restore "-p:Version=$Version" }
    Invoke-Step 'Tests' { & $dotnet run --project tests\AzdoMcp.Tests -c $Configuration --no-build }

    # Both executables go into one folder; self-contained so no .NET runtime is needed.
    $common = @('-c', $Configuration, '-r', $Runtime, '--self-contained', 'true', "-p:Version=$Version",
                '-p:PublishReadyToRun=false', '-p:DebugType=none', '-o', $publish)
    Invoke-Step 'Publish azdo-mcp.exe' { & $dotnet publish src\AzdoMcp.Server\AzdoMcp.Server.csproj @common }
    Invoke-Step 'Publish AzdoMcp.Manager.exe' { & $dotnet publish src\AzdoMcp.Manager\AzdoMcp.Manager.csproj @common }

    foreach ($exe in 'azdo-mcp.exe', 'AzdoMcp.Manager.exe') {
        if (-not (Test-Path (Join-Path $publish $exe))) { throw "$exe fehlt im Publish-Ordner." }
    }

    # Portable: plain folder, no Update.exe – the Manager detects it and turns updates off.
    Invoke-Step 'Portable-ZIP' {
        Compress-Archive -Path (Join-Path $publish '*') -DestinationPath $portableZip -Force
        $global:LASTEXITCODE = 0
    }

    Invoke-Step 'Tool restore (vpk)' { & $dotnet tool restore }
    Invoke-Step 'Velopack pack' {
        & $dotnet vpk pack `
            --packId AzdoMcp `
            --packVersion $Version `
            --packDir $publish `
            --mainExe AzdoMcp.Manager.exe `
            --packTitle 'AzDO MCP Manager' `
            --packAuthors 'AzdoMcp' `
            --runtime $Runtime `
            --shortcuts StartMenuRoot `
            --outputDir $releases `
            --yes
    }

    $setup = Join-Path $releases 'AzdoMcp-win-Setup.exe'
    if (-not (Test-Path $setup)) { throw "Setup fehlt: $setup" }

    Write-Host ""
    Write-Host 'Fertig:' -ForegroundColor Green
    Get-Item $setup, $portableZip | ForEach-Object { '{0,-40} {1,8:N1} MB' -f $_.Name, ($_.Length / 1MB) } | Write-Host
    exit 0
}
catch {
    Write-Host ""
    Write-Host "FEHLER: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
finally {
    Pop-Location
}
