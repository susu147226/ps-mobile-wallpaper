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
    Product version. Must be a three-part version, e.g. 1.1.0.

.PARAMETER Configuration
    Build configuration. Defaults to Release.

.PARAMETER AndroidSdkDirectory
    Android SDK used to build the wallpaper helper APK.

.PARAMETER JavaSdkDirectory
    JDK used to build the wallpaper helper APK.

.PARAMETER AdbSourceDirectory
    A directory containing adb.exe (Android SDK Platform Tools). It is copied into the installer so
    a fresh machine can talk to an Android phone with no setup. Left empty, it is taken from PATH.
#>
[CmdletBinding()]
param(
    [string]$Version = "1.1.0",
    [string]$Configuration = "Release",
    [string]$AndroidSdkDirectory = "C:\Android\Sdk",
    [string]$JavaSdkDirectory = "C:\Android\Jdk",
    [string]$AdbSourceDirectory = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$staging = Join-Path $repoRoot "installer/wix/staging"
$artifacts = Join-Path $repoRoot "artifacts"
$apiProject = Join-Path $repoRoot "apps/phone-bridge/src/PSMobileWallpaper.Api/PSMobileWallpaper.Api.csproj"
$pluginDir = Join-Path $repoRoot "apps/photoshop-plugin"
$helperDir = Join-Path $repoRoot "apps/wallpaper-helper"

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

# --- 3. Build the Android wallpaper helper ------------------------------------
# AOT must stay off: with it the app dies on startup with UnsatisfiedLinkError on n_onCreate.
Write-Host "`n==> Building the Android wallpaper helper..."
Push-Location $helperDir
try {
    dotnet build -f net10.0-android -c $Configuration `
        -p:AndroidSdkDirectory=$AndroidSdkDirectory `
        -p:JavaSdkDirectory=$JavaSdkDirectory `
        -p:RunAOTCompilation=false `
        -p:AndroidEnableProfiledAot=false `
        --nologo

    if ($LASTEXITCODE -ne 0) {
        throw "The helper APK failed to build. Ensure the Android SDK and JDK exist at " +
              "'$AndroidSdkDirectory' and '$JavaSdkDirectory' (see apps/wallpaper-helper/README.md)."
    }
}
finally {
    Pop-Location
}

$helperApk = Join-Path $helperDir "bin/$Configuration/net10.0-android/android-arm64/com.psmobilewallpaper.helper-Signed.apk"
if (-not (Test-Path $helperApk)) {
    throw "Expected helper APK not found at '$helperApk'."
}

# The bridge looks for it beside the executable (see BridgePaths.HelperApkPath).
New-Item -ItemType Directory -Force -Path (Join-Path $staging "app/helpers") | Out-Null
Copy-Item $helperApk (Join-Path $staging "app/helpers/psmw-wallpaper-helper.apk") -Force

# --- 4. Stage adb so a fresh install needs no setup ---------------------------
# adb is Apache-2.0 and redistributable; hdc is not (it ships with DevEco Studio), so only adb is
# bundled and hdc.path stays a user setting.
Write-Host "`n==> Staging adb..."

$adbDir = $AdbSourceDirectory
if ([string]::IsNullOrWhiteSpace($adbDir)) {
    $onPath = Get-Command adb.exe -ErrorAction SilentlyContinue
    if ($onPath) { $adbDir = Split-Path -Parent $onPath.Source }
}

if ([string]::IsNullOrWhiteSpace($adbDir) -or -not (Test-Path (Join-Path $adbDir "adb.exe"))) {
    throw "adb.exe not found. Pass -AdbSourceDirectory <platform-tools dir>, or put adb on PATH."
}

$adbStage = Join-Path $staging "app/runtime/adb"
New-Item -ItemType Directory -Force -Path $adbStage | Out-Null
foreach ($name in @("adb.exe", "AdbWinApi.dll", "AdbWinUsbApi.dll")) {
    $source = Join-Path $adbDir $name
    if (Test-Path $source) {
        Copy-Item $source $adbStage -Force
    } else {
        Write-Warning "Expected adb component '$name' was not found in '$adbDir'."
    }
}

# --- 5. Build the MSI ---------------------------------------------------------
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

# --- 6. Portable ZIP ----------------------------------------------------------
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
2. Load the Photoshop panel (the 'plugin' folder) by copying it to:
     %AppData%\Adobe\UXP\Plugins\External\com.psmobilewallpaper.panel
   then restart Photoshop. It appears under the Plug-ins menu.
   (The MSI installer does this step for you.)

Android phones work out of the box: adb ships in 'runtime\adb' and the wallpaper
helper APK in 'helpers\' (the bridge installs it on the phone when needed).

HarmonyOS phones additionally need hdc. It comes with DevEco Studio and cannot be
redistributed here, so point config.json's hdc.path at its folder, or put it on
PATH. The bridge sets up the reverse port forward the phone app needs by itself.

No authentication token is required. Set server.requireToken to true in
config.json if you want one.
"@ | Set-Content -Path $readme -Encoding UTF8

$zipPath = Join-Path $artifacts "PSMobileWallpaper-PhoneBridge-$Version-x64.zip"
Compress-Archive -Path (Join-Path $portableStage "*") -DestinationPath $zipPath -Force

# --- 7. Plugin ZIP ------------------------------------------------------------
$pluginZip = Join-Path $artifacts "PSMobileWallpaper-Plugin-$Version.zip"
Compress-Archive -Path (Join-Path $staging "plugin/*") -DestinationPath $pluginZip -Force

# --- Summary ------------------------------------------------------------------
Write-Host "`n==> Artifacts:"
Get-ChildItem $artifacts | ForEach-Object {
    "{0,-52} {1,8:N1} MB" -f $_.Name, ($_.Length / 1MB) | Write-Host
}
