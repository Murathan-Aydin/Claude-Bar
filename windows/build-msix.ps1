# Construit le paquet Microsoft Store (MSIX) de Claude Bar. Windows uniquement (SDK Windows : makeappx, makepri, signtool).
# Usage : .\windows\build-msix.ps1 -Version 1.0.1 [-TestSign]
#   - dist\ClaudeBar-Store.msix : paquet NON signé, à téléverser dans Partner Center (le Store le signe lui-même)
#   - avec -TestSign : dist\ClaudeBar-Test.msix + .cer, signés avec un certificat de test pour l'installer en local
param(
    [string]$Version = "1.0.0",
    [string]$IdentityFile = (Join-Path $PSScriptRoot "store\identity.json"),
    [switch]$TestSign
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$publish = Join-Path $root "publish-msix"
$stage = Join-Path $root "msix-stage"
$dist = Join-Path $root "dist"
$work = Join-Path $root "msix-work"

foreach ($dir in @($publish, $stage, $work)) {
    if (Test-Path $dir) { Remove-Item -Recurse -Force $dir }
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
}
New-Item -ItemType Directory -Force -Path $dist | Out-Null

# ── Outils du SDK Windows ─────────────────────────────────────────────────────
function Find-SdkTool([string]$name) {
    $found = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\10.*\x64\$name" -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending | Select-Object -First 1
    if (-not $found) { throw "$name introuvable : installe le SDK Windows 10/11." }
    return $found.FullName
}
$makeappx = Find-SdkTool "makeappx.exe"
$makepri = Find-SdkTool "makepri.exe"

# ── Version MSIX : quatre nombres, le dernier à 0 (exigence du Store) ─────────
$parts = $Version.Split('.')
while ($parts.Count -lt 3) { $parts += "0" }
$msixVersion = "$($parts[0]).$($parts[1]).$($parts[2]).0"

$identity = Get-Content $IdentityFile -Raw | ConvertFrom-Json
Write-Host "==> Identité : $($identity.identityName) / $($identity.publisher) — version $msixVersion"

# ── Publication .NET ──────────────────────────────────────────────────────────
dotnet publish (Join-Path $root "src\ClaudeBar\ClaudeBar.csproj") `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:Version=$Version `
    -o $publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish a échoué" }

# ── Contenu du paquet ─────────────────────────────────────────────────────────
Copy-Item (Join-Path $publish "ClaudeBar.exe") $stage
Copy-Item (Join-Path $publish "WebView2Loader.dll") $stage
Copy-Item -Recurse (Join-Path $root "store\Assets") (Join-Path $stage "Assets")

$manifest = Get-Content (Join-Path $root "store\AppxManifest.xml") -Raw
$replacements = @{
    '$IDENTITY_NAME$'          = $identity.identityName
    '$PUBLISHER$'              = $identity.publisher
    '$PUBLISHER_DISPLAY_NAME$' = $identity.publisherDisplayName
    '$DISPLAY_NAME$'           = $identity.displayName
    '$VERSION$'                = $msixVersion
}
foreach ($key in $replacements.Keys) {
    $value = [System.Security.SecurityElement]::Escape($replacements[$key])
    $manifest = $manifest.Replace($key, $value)
}
[System.IO.File]::WriteAllText((Join-Path $stage "AppxManifest.xml"), $manifest, (New-Object System.Text.UTF8Encoding($false)))

# ── Index des ressources (variantes d'icônes scale-*/targetsize-*) ────────────
$priConfig = Join-Path $work "priconfig.xml"
& $makepri createconfig /cf $priConfig /dq fr-FR /pv 10.0.0 /o
if ($LASTEXITCODE -ne 0) { throw "makepri createconfig a échoué" }
& $makepri new /pr $stage /cf $priConfig /mn (Join-Path $stage "AppxManifest.xml") /of (Join-Path $stage "resources.pri") /o
if ($LASTEXITCODE -ne 0) { throw "makepri new a échoué" }

# ── Paquet ────────────────────────────────────────────────────────────────────
$storeMsix = Join-Path $dist "ClaudeBar-Store.msix"
& $makeappx pack /d $stage /p $storeMsix /o
if ($LASTEXITCODE -ne 0) { throw "makeappx pack a échoué" }
Write-Host "Paquet Store prêt : $storeMsix"

# ── Copie signée avec un certificat de test (installation locale / CI) ────────
if ($TestSign) {
    $signtool = Find-SdkTool "signtool.exe"
    $password = "claudebar-test"
    $cert = New-SelfSignedCertificate -Type Custom -Subject $identity.publisher `
        -KeyUsage DigitalSignature -FriendlyName "Claude Bar - certificat de test" `
        -CertStoreLocation "Cert:\CurrentUser\My" `
        -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")
    $pfx = Join-Path $work "test.pfx"
    Export-PfxCertificate -Cert $cert -FilePath $pfx -Password (ConvertTo-SecureString $password -AsPlainText -Force) | Out-Null
    Export-Certificate -Cert $cert -FilePath (Join-Path $dist "ClaudeBar-Test.cer") | Out-Null

    $testMsix = Join-Path $dist "ClaudeBar-Test.msix"
    Copy-Item $storeMsix $testMsix -Force
    & $signtool sign /fd SHA256 /f $pfx /p $password $testMsix
    if ($LASTEXITCODE -ne 0) { throw "signtool a échoué" }
    Remove-Item "Cert:\CurrentUser\My\$($cert.Thumbprint)"
    Write-Host "Paquet de test signé : $testMsix (+ ClaudeBar-Test.cer)"
}
