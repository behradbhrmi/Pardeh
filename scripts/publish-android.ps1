# Builds the signed Release APK(s) of the Android head into artifacts/android/Pardeh-<version>-<abi>.apk.
# The signing key lives outside the repo (default $HOME\.pardeh\keystore.properties, keys storeFile, storePassword,
# keyAlias, keyPassword; override with -KeystoreProperties or env PARDEH_KEYSTORE_PROPERTIES). Secrets only travel through
# environment variables of the child process and are never printed. See docs/android-build.md.
param(
    [string]$Abi = "arm64-v8a",                 # arm64-v8a | x86_64 | all (builds both)
    [bool]$ClassicBranding = $false,
    [string]$KeystoreProperties = $(if ($env:PARDEH_KEYSTORE_PROPERTIES) { $env:PARDEH_KEYSTORE_PROPERTIES } else { Join-Path $HOME ".pardeh\keystore.properties" }),
    [string]$AndroidSdk = $(if ($env:ANDROID_SDK_ROOT) { $env:ANDROID_SDK_ROOT } else { Join-Path $env:LOCALAPPDATA "Android\Sdk" }),
    [string]$JavaSdk = $(Join-Path $env:LOCALAPPDATA "Android\jdk")
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src/Pardeh.Android"
$out = Join-Path $root "artifacts/android"

$abis = if ($Abi -eq "all") { @("arm64-v8a", "x86_64") } else { @($Abi) }
$rids = @{ "arm64-v8a" = "android-arm64"; "x86_64" = "android-x64" }
foreach ($a in $abis) { if (-not $rids.ContainsKey($a)) { throw "Unknown ABI '$a' (use arm64-v8a, x86_64 or all)." } }

# FFmpeg libraries for every requested ABI
foreach ($a in $abis) {
    if (-not (Test-Path (Join-Path $project "lib/$a/libavcodec.so"))) {
        throw "FFmpeg libraries for $a are missing in src/Pardeh.Android/lib/$a. Run scripts/get-ffmpeg-android.ps1 first."
    }
}

# keystore properties
if (-not (Test-Path $KeystoreProperties)) { throw "Keystore properties file not found: $KeystoreProperties (see docs/android-build.md)." }
$props = @{}
foreach ($line in Get-Content $KeystoreProperties) {
    if ($line -match '^\s*([^#=\s][^=]*?)\s*=\s*(.*)$') { $props[$Matches[1]] = $Matches[2] }
}
foreach ($k in "storeFile", "storePassword", "keyAlias", "keyPassword") {
    if (-not $props.ContainsKey($k) -or [string]::IsNullOrEmpty($props[$k])) { throw "Missing '$k' in $KeystoreProperties." }
}
if (-not (Test-Path $props["storeFile"])) { throw "Keystore not found: $($props['storeFile'])" }

# apksigner (newest build-tools)
$apksigner = Get-ChildItem (Join-Path $AndroidSdk "build-tools") -Directory -ErrorAction SilentlyContinue | Sort-Object { [version]($_.Name -replace '[^0-9.].*$', '') } -Descending |
    ForEach-Object { Join-Path $_.FullName "apksigner.bat" } | Where-Object { Test-Path $_ } | Select-Object -First 1

# version from the csproj
$version = ([xml](Get-Content (Join-Path $project "Pardeh.Android.csproj"))).Project.PropertyGroup.ApplicationDisplayVersion | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { $version = "0.0.0" }
# Stamp every build: versionCode = git commit count (always rises), display version = <csproj base>.<count>+<short hash>.
$versionCode = 1
$shortHash = ""
try {
    $count = (& git -C $root rev-list --count HEAD 2>$null)
    if ($LASTEXITCODE -eq 0 -and $count -match '^\d+$') { $versionCode = [int]$count }
    $shortHash = (& git -C $root rev-parse --short HEAD 2>$null)
} catch { }
$baseParts = $version.Split('.')
$version = "{0}.{1}.{2}" -f $baseParts[0], $(if ($baseParts.Count -gt 1) { $baseParts[1] } else { "0" }), $versionCode
Write-Host "Version $version (versionCode $versionCode, commit $shortHash)"

if (-not $env:JAVA_HOME) { $env:JAVA_HOME = $JavaSdk }   # apksigner needs it
New-Item -ItemType Directory -Force $out | Out-Null
$env:PARDEH_SIGN_STORE_PASS = $props["storePassword"]
$env:PARDEH_SIGN_KEY_PASS = $props["keyPassword"]
try {
    foreach ($a in $abis) {
        Write-Host "== Pardeh $version $a (ClassicBranding=$ClassicBranding) =="
        $tmp = Join-Path $out "build-$a"
        Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
        $branding = if ($ClassicBranding) { "true" } else { "false" }
        & dotnet publish $project -f net11.0-android -c Release -r $rids[$a] --self-contained `
            "-p:AndroidSdkDirectory=$AndroidSdk" "-p:JavaSdkDirectory=$JavaSdk" "-p:ClassicBranding=$branding" "-p:ApplicationVersion=$versionCode" "-p:ApplicationDisplayVersion=$version" `
            "-p:AndroidKeyStore=true" "-p:AndroidSigningKeyStore=$($props['storeFile'])" "-p:AndroidSigningKeyAlias=$($props['keyAlias'])" `
            '-p:AndroidSigningKeyPass=env:PARDEH_SIGN_KEY_PASS' '-p:AndroidSigningStorePass=env:PARDEH_SIGN_STORE_PASS' `
            -o $tmp
        if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $a" }

        $apk = Get-ChildItem $tmp -Filter "*-Signed.apk" | Select-Object -First 1
        if (-not $apk) { throw "No signed APK found in $tmp" }
        $dest = Join-Path $out "Pardeh-$version-$a.apk"
        Copy-Item $apk.FullName $dest -Force
        Remove-Item -Recurse -Force $tmp
        Write-Host ("{0}  {1:N1} MB" -f $dest, ((Get-Item $dest).Length / 1MB))
        if ($apksigner) {
            # Capture everything first: cutting the pipe short with Select-Object can leave a bogus non-zero exit code.
            $verify = & $apksigner verify --verbose $dest 2>&1
            $verifyExit = $LASTEXITCODE
            $verify | Select-Object -First 6 | ForEach-Object { Write-Host "  apksigner: $_" }
            if ($verifyExit -ne 0) { throw "apksigner verify failed for $dest" }
        } else { Write-Warning "apksigner not found under $AndroidSdk\build-tools; signature not verified." }
    }
}
finally {
    Remove-Item Env:PARDEH_SIGN_STORE_PASS, Env:PARDEH_SIGN_KEY_PASS -ErrorAction SilentlyContinue
}
