# Construit ClaudeBar-Setup.exe sous Windows.
# Prérequis : .NET SDK 10 (winget install Microsoft.DotNet.SDK.10) et NSIS (winget install NSIS.NSIS).
# Usage : .\windows\build.ps1 [-Version 1.0.0]
param(
    [string]$Version = "1.0.0"
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$publish = Join-Path $root "publish"
$dist = Join-Path $root "dist"

foreach ($dir in @($publish, $dist)) {
    if (Test-Path $dir) { Remove-Item -Recurse -Force $dir }
}
New-Item -ItemType Directory -Force -Path $dist | Out-Null

Write-Host "==> dotnet publish (win-x64, autonome, fichier unique) - version $Version"
dotnet publish (Join-Path $root "src\ClaudeBar\ClaudeBar.csproj") `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:Version=$Version `
    -o $publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish a échoué" }

$makensis = $null
$cmd = Get-Command makensis -ErrorAction SilentlyContinue
if ($cmd) { $makensis = $cmd.Source }
if (-not $makensis) {
    foreach ($candidate in @("${env:ProgramFiles(x86)}\NSIS\makensis.exe", "$env:ProgramFiles\NSIS\makensis.exe")) {
        if (Test-Path $candidate) { $makensis = $candidate; break }
    }
}
if (-not $makensis) {
    throw "makensis introuvable. Installe NSIS : winget install NSIS.NSIS (l'exécutable seul est dans $publish)"
}

Write-Host "==> makensis ($makensis)"
& $makensis /INPUTCHARSET UTF8 "/DVERSION=$Version" "/DPUBLISH_DIR=$publish" "/DOUT_DIR=$dist" (Join-Path $root "installer\ClaudeBar.nsi")
if ($LASTEXITCODE -ne 0) { throw "makensis a échoué" }

Write-Host ""
Write-Host "Installateur prêt : $(Join-Path $dist 'ClaudeBar-Setup.exe')"
