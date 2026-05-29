#requires -Version 5.1
<#
.SYNOPSIS
  Builds and packages Token Spendie locally with Velopack. Produces an
  unsigned installer under windows/releases/.

.DESCRIPTION
  This is the dev-machine smoke path. CI uses a different workflow that
  signs the binaries via SignPath. Never run this in CI.

.PARAMETER Version
  Semver string. Defaults to "0.0.0-local".

.EXAMPLE
  pwsh windows/scripts/pack-local.ps1 -Version 0.4.0-local
#>
[CmdletBinding()]
param(
    [string]$Version = "0.0.0-local"
)
$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
Set-Location $repoRoot

# Restore tools (idempotent).
dotnet tool restore

# Clean previous publish output so old DLLs don't bleed into the new package.
$publishDir = Join-Path $repoRoot "windows\src\TokenSpendie.Windows\bin\Release\net8.0-windows10.0.17763\publish"
if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }

# Publish self-contained so the installer ships .NET runtime.
dotnet publish windows/src/TokenSpendie.Windows/TokenSpendie.Windows.csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:Version=$Version `
    -p:PublishSingleFile=false `
    -p:IncludeNativeLibrariesForSelfExtract=false `
    -o $publishDir

# Pack with Velopack. NO --signTemplate here — local builds are unsigned.
$releasesDir = Join-Path $repoRoot "windows\releases"
if (-not (Test-Path $releasesDir)) { New-Item -ItemType Directory -Path $releasesDir | Out-Null }

dotnet tool run vpk -- pack `
    --packId "TokenSpendie.Windows" `
    --packTitle "Token Spendie" `
    --packAuthors "nong.seng" `
    --packVersion $Version `
    --packDir $publishDir `
    --mainExe "TokenSpendie.Windows.exe" `
    --icon "windows\build\icon\AppIcon.ico" `
    --releaseNotes "windows\build\velopack\releasenotes-template.md" `
    --outputDir $releasesDir

Write-Host ""
Write-Host "Local Velopack package built. Output: $releasesDir" -ForegroundColor Green
Write-Host "Install: run the Setup.exe from that directory." -ForegroundColor Green
Write-Host "Uninstall: Settings -> Apps -> Token Spendie -> Uninstall." -ForegroundColor Green
