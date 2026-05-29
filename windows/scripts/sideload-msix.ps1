#requires -Version 5.1
<#
.SYNOPSIS
    Build, self-sign, and sideload TokenSpendie.WidgetProvider MSIX for local smoke.

.DESCRIPTION
    Dev-machine smoke path. CI signs via SignPath (future M6 Task 11); this script
    signs with a per-user self-signed certificate whose subject matches the MSIX
    Identity Publisher (CN=SignPath OSS) so the same install path validates against
    both signed and locally-signed packages.

.PARAMETER Version
    Semver string. Defaults to "0.0.0-local".

.PARAMETER Uninstall
    Remove the installed package and the LocalMachine\TrustedPeople trust entry.

.EXAMPLE
    powershell windows\scripts\sideload-msix.ps1
.EXAMPLE
    powershell windows\scripts\sideload-msix.ps1 -Version 1.2.3-local
.EXAMPLE
    powershell windows\scripts\sideload-msix.ps1 -Uninstall
#>
[CmdletBinding()]
param(
    [string]$Version = "0.0.0-local",
    [switch]$Uninstall
)

$ErrorActionPreference = "Stop"

$repo    = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$pkgName = "Sensecherise.TokenSpendie.WidgetProvider"
$subject = "CN=SignPath OSS"

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)

# Helper: run a command elevated (UAC) or inline if already admin.
function Invoke-Elevated {
    param([string]$Command)
    if ($isAdmin) {
        Invoke-Expression $Command
    } else {
        Start-Process powershell -Verb RunAs -Wait -ArgumentList @("-NoProfile", "-Command", $Command)
    }
}

if ($Uninstall) {
    Write-Host "Uninstalling widget MSIX..." -ForegroundColor Yellow
    Get-AppxPackage $pkgName -ErrorAction SilentlyContinue |
        ForEach-Object { Remove-AppxPackage -Package $_.PackageFullName }

    # Cert removal from LocalMachine\TrustedPeople requires admin rights.
    $toRemove = Get-ChildItem Cert:\LocalMachine\TrustedPeople -ErrorAction SilentlyContinue |
        Where-Object Subject -eq $subject
    if ($toRemove) {
        if ($isAdmin) {
            foreach ($c in $toRemove) {
                Write-Host "Removing trusted cert: $($c.Thumbprint)"
                Remove-Item -Path "Cert:\LocalMachine\TrustedPeople\$($c.Thumbprint)" -Force
            }
        } else {
            Write-Host "NOTE: cert $($toRemove.Thumbprint) left in LocalMachine\TrustedPeople (re-run as admin to remove)." -ForegroundColor Yellow
        }
    }
    Write-Host "Uninstalled." -ForegroundColor Green
    exit 0
}

# 1. Build MSIX
Write-Host "Building widget provider $Version..." -ForegroundColor Cyan
Push-Location $repo
try {
    dotnet msbuild windows/src/TokenSpendie.WidgetProvider/TokenSpendie.WidgetProvider.csproj `
        -t:Build -p:Configuration=Release -p:Platform=x64 -p:Version=$Version `
        -p:GenerateAppxPackageOnBuild=true
    if ($LASTEXITCODE -ne 0) { throw "dotnet msbuild exit $LASTEXITCODE" }
} finally { Pop-Location }

$msix = Get-ChildItem (Join-Path $repo "windows\src\TokenSpendie.WidgetProvider\AppPackages") `
    -Recurse -Filter "*.msix" |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $msix) { throw "MSIX not produced under AppPackages." }
Write-Host "Built: $($msix.FullName)"

# 2. Self-signed cert (reuse if present)
$cert = Get-ChildItem Cert:\CurrentUser\My |
    Where-Object Subject -eq $subject | Select-Object -First 1
if (-not $cert) {
    Write-Host "Generating self-signed cert..." -ForegroundColor Cyan
    $cert = New-SelfSignedCertificate `
        -Type CodeSigningCert -Subject $subject `
        -KeyAlgorithm RSA -KeyLength 2048 -KeyExportPolicy Exportable `
        -NotAfter (Get-Date).AddYears(1) `
        -CertStoreLocation "Cert:\CurrentUser\My"
}

$pfxDir = Join-Path $repo "windows\releases\widget-sideload"
New-Item -ItemType Directory -Force -Path $pfxDir | Out-Null
$pfxPath = Join-Path $pfxDir "sideload-cert.pfx"
$pfxPassword = ConvertTo-SecureString -String "tokenspendie-local" -Force -AsPlainText
Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $pfxPassword | Out-Null
Write-Host "PFX: $pfxPath"

# 3. Trust in LocalMachine\TrustedPeople (admin scope)
$trusted = Get-ChildItem Cert:\LocalMachine\TrustedPeople -ErrorAction SilentlyContinue |
    Where-Object Subject -eq $subject
if (-not $trusted) {
    Write-Host "Importing cert into LocalMachine\TrustedPeople (may prompt UAC)..." -ForegroundColor Cyan
    $importCmd = "Import-PfxCertificate -FilePath '$pfxPath' -CertStoreLocation Cert:\LocalMachine\TrustedPeople -Password (ConvertTo-SecureString 'tokenspendie-local' -Force -AsPlainText) | Out-Null"
    Invoke-Elevated $importCmd
    $verify = Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object Subject -eq $subject
    if (-not $verify) { throw "Cert import was cancelled or failed." }
} else {
    Write-Host "Cert already trusted: $($trusted.Thumbprint)"
}

# 4. signtool from BuildTools NuGet
$signtool = Get-ChildItem "$env:USERPROFILE\.nuget\packages\microsoft.windows.sdk.buildtools\*\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue |
    Sort-Object FullName -Descending | Select-Object -First 1
if (-not $signtool) {
    throw "signtool.exe not found in NuGet cache. Run 'dotnet restore windows/TokenSpendie.Windows.sln' first."
}

# 5. Sign MSIX
Write-Host "Signing MSIX..." -ForegroundColor Cyan
& $signtool.FullName sign /fd SHA256 /f $pfxPath /p tokenspendie-local $msix.FullName
if ($LASTEXITCODE -ne 0) { throw "signtool sign exit $LASTEXITCODE" }

$sig = Get-AuthenticodeSignature $msix.FullName
if ($sig.Status -ne "Valid") { throw "Post-sign verification: $($sig.Status)" }

# 6. Install
Write-Host "Installing $($msix.Name)..." -ForegroundColor Cyan
Get-AppxPackage $pkgName -ErrorAction SilentlyContinue |
    ForEach-Object { Remove-AppxPackage -Package $_.PackageFullName }
Add-AppxPackage -Path $msix.FullName

Write-Host ""
Write-Host "Installed." -ForegroundColor Green
Write-Host "Next: Win+W -> + -> pin Token Spendie - Session / Full"
Write-Host "Uninstall: powershell $($PSCommandPath) -Uninstall"
