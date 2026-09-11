<#
.SYNOPSIS
    Builds the PS Mobile Wallpaper release artifacts: an MSI, a portable ZIP, and the UXP plugin bundle.

.DESCRIPTION
    Produces, under artifacts/:
      PSMobileWallpaper-PhoneBridge-<version>-x64.msi   WiX installer (spec §2.9)
      PSMobileWallpaper-PhoneBridge-<version>-x64.zip   portable, no installer
      PSMobileWallpaper-Plugin-<version>.zip            built UXP panel

    The published executable is self-contained, so the target machine needs neither the .NET runtime
    nor an Adobe account.

.PARAMETER Version
    Product version. Must be a three-part version, e.g. 1.0.0.

.PARAMETER Configuration
    Build configuration. Defaults to Release.
#>
[CmdletBinding()]
param(
    [string]$Version = "1.0.0",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$staging = Join-Path $repoRoot "installer/wix/staging"
$artifacts = Join-Path $repoRoot "artifacts"
$apiProject = Join-Path $repoRoot "apps/phone-bridge/src/PSMobileWallpaper.Api/PSMobileWallpaper.Api.csproj"
$pluginDir = Join-Path $repoRoot "apps/photoshop-plugin"

Write-Host "==> Repo:      $repoRoot"
Write-Host "==> Version:   $Version"
Write-Host "==> Artifacts: $artifacts"

# --- Clean -------------------------------------------------------------------
foreach ($dir in @($staging, $artifacts)) {
    if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
}
New-Item -ItemType Directory -Force -Path (Join-Path $staging "app") | Out-Null

# --- 1. Publish the bridge (self-contained, single file) ----------------------
Write-Host "`n==> Publishing PhoneBridge (self-contained win-x64)..."
dotnet publish $apiProject `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    -p:Version=$Version `
    -o (Join-Path $staging "app") `
    --nologo

if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

# appsettings.Development.json is a developer-only file; do not ship it.
Remove-Item (Join-Path $staging "app/appsettings.Development.json") -ErrorAction SilentlyContinue
Remove-Item (Join-Path $staging "app/*.pdb") -ErrorAction SilentlyContinue

# --- 2. Build the UXP plugin --------------------------------------------------
Write-Host "`n==> Building the Photoshop UXP plugin..."
Push-Location $pluginDir
try {
    if (-not (Test-Path "node_modules")) {
        npm install --no-fund --no-audit
        if ($LASTEXITCODE -ne 0) { throw "npm install failed." }
    }

    npm run build
    if ($LASTEXITCODE -ne 0) { throw "npm run build failed." }
}
finally {
    Pop-Location
}

New-Item -ItemType Directory -Force -Path (Join-Path $staging "plugin") | Out-Null
Copy-Item (Join-Path $pluginDir "dist/*") (Join-Path $staging "plugin") -Recurse -Force

# --- 3. Build the MSI ---------------------------------------------------------
Write-Host "`n==> Building the MSI with WiX..."
if (-not (Get-Command wix -ErrorAction SilentlyContinue)) {
    throw "The 'wix' tool was not found. Install it with: dotnet tool install --global wix --version 4.0.6"
}

New-Item -ItemType Directory -Force -Path $artifacts | Out-Null
$msiPath = Join-Path $artifacts "PSMobileWallpaper-PhoneBridge-$Version-x64.msi"

Push-Location (Join-Path $repoRoot "installer/wix")
try {
    wix build Package.wxs -arch x64 -o $msiPath `
        -d "ProductVersion=$Version" `
        -bindpath "."
    if ($LASTEXITCODE -ne 0) { throw "wix build failed." }
}
finally {
    Pop-Location
}

# --- 4. Portable ZIP ----------------------------------------------------------
Write-Host "`n==> Packing the portable ZIP..."
$portableStage = Join-Path $staging "portable"
New-Item -ItemType Directory -Force -Path $portableStage | Out-Null

Copy-Item (Join-Path $staging "app/*") $portableStage -Recurse -Force
Copy-Item (Join-Path $staging "plugin") $portableStage -Recurse -Force

$readme = Join-Path $portableStage "README.txt"
@"
PS Mobile Wallpaper - PhoneBridge (portable)

1. Run PSMobileWallpaper.Api.exe. The first run creates:
     %AppData%\PSMobileWallpaper\config.json
     %AppData%\PSMobileWallpaper\auth.token
2. Load the Photoshop panel from the 'plugin' folder using the UXP Developer Tool.
3. Copy the contents of auth.token into the panel's token field.

The bridge listens on http://127.0.0.1:18765 and only accepts requests that
carry the token.
"@ | Set-Content -Path $readme -Encoding UTF8

$zipPath = Join-Path $artifacts "PSMobileWallpaper-PhoneBridge-$Version-x64.zip"
Compress-Archive -Path (Join-Path $portableStage "*") -DestinationPath $zipPath -Force

# --- 5. Plugin ZIP ------------------------------------------------------------
$pluginZip = Join-Path $artifacts "PSMobileWallpaper-Plugin-$Version.zip"
Compress-Archive -Path (Join-Path $staging "plugin/*") -DestinationPath $pluginZip -Force

# --- Summary ------------------------------------------------------------------
Write-Host "`n==> Artifacts:"
Get-ChildItem $artifacts | ForEach-Object {
    "{0,-52} {1,8:N1} MB" -f $_.Name, ($_.Length / 1MB) | Write-Host
}
