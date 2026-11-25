# One-command release for real Android phones (arm64-v8a).
#   .\scripts\release-android-arm64.ps1                 builds + signs + verifies artifacts\android\Pardeh-<version>-arm64-v8a.apk
#   .\scripts\release-android-arm64.ps1 -Install        ...and installs it on the connected phone over adb (USB debugging on)
#   .\scripts\release-android-arm64.ps1 -Install -Serial R58M123ABC     pick one device when several are connected
#   .\scripts\release-android-arm64.ps1 -ClassicBranding $true          build with the classic launcher name and icon
# Needs the keystore at $HOME\.pardeh\keystore.properties (see docs/android-build.md) and the FFmpeg libs
# (scripts\get-ffmpeg-android.ps1). To update an installed app the versionCode (ApplicationVersion in
# src\Pardeh.Android\Pardeh.Android.csproj) must be higher than the installed one: bump it for every release you hand out.
param(
    [switch]$Install,
    [string]$Serial = "",
    [bool]$ClassicBranding = $false,
    [string]$AndroidSdk = $(if ($env:ANDROID_SDK_ROOT) { $env:ANDROID_SDK_ROOT } else { Join-Path $env:LOCALAPPDATA "Android\Sdk" })
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

& (Join-Path $PSScriptRoot "publish-android.ps1") -Abi arm64-v8a -ClassicBranding $ClassicBranding -AndroidSdk $AndroidSdk
if ($LASTEXITCODE -ne 0 -and $null -ne $LASTEXITCODE) { throw "publish-android.ps1 failed." }

$apk = Get-ChildItem (Join-Path $root "artifacts\android") -Filter "Pardeh-*-arm64-v8a.apk" |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $apk) { throw "No arm64 APK found in artifacts\android." }
Write-Host ""
Write-Host ("APK ready: {0}  ({1:N1} MB)" -f $apk.FullName, ($apk.Length / 1MB))

if ($Install) {
    $adb = Join-Path $AndroidSdk "platform-tools\adb.exe"
    if (-not (Test-Path $adb)) { throw "adb not found at $adb" }
    $devArgs = if ($Serial) { @("-s", $Serial) } else { @() }

    $devices = & $adb devices | Select-Object -Skip 1 | Where-Object { $_ -match "\sdevice$" }
    if (-not $devices) { throw "No device connected (enable USB debugging and accept the prompt on the phone, then check 'adb devices')." }
    if (-not $Serial -and @($devices).Count -gt 1) { throw "Several devices connected; pass -Serial <id> (see 'adb devices')." }

    $abi = (& $adb @devArgs shell getprop ro.product.cpu.abi).Trim()
    if ($abi -ne "arm64-v8a") { throw "The target device is '$abi', not arm64-v8a; use scripts\publish-android.ps1 -Abi x86_64 for emulators." }

    Write-Host "Installing on the device ($abi)..."
    & $adb @devArgs install -r $apk.FullName
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "Install failed. If the message says INSTALL_FAILED_UPDATE_INCOMPATIBLE the phone has a build signed with another key (e.g. a debug build): run 'adb uninstall org.pardeh.player' and install again (this clears the app's data). INSTALL_FAILED_VERSION_DOWNGRADE means the versionCode is lower than the installed one."
        exit 1
    }
    Write-Host "Installed. Launch it from the app drawer (Pardeh / Windows Media Player, depending on branding)."
}
