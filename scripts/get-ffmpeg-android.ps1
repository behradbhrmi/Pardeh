# Downloads LGPL shared FFmpeg 8.1 builds for Android (tqk2811/FFmpegBuild) into src/Pardeh.Android/lib/<abi>/.
# Verifies SHA-256, keeps only the libraries Pardeh needs (+ libc++_shared.so). Binaries are not committed (.gitignore).
param([string]$Tag = "Release-Android-2026-08-06", [string]$Build = "n8.1.2-21-gce3c09c101")

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$base = "https://github.com/tqk2811/FFmpegBuild/releases/download/$Tag"
$abis = @{
    "arm64-v8a" = "02612fd8ff123116d8db2acdc1dffd18f6a2e6773b8463e6d42af86ad541a446"
    "x86_64"    = "7814bb39547f20f53858435430c8874de333d39269e3b375a44abe4aab23af05"
}
$keep = "libavcodec.so", "libavformat.so", "libavutil.so", "libswresample.so", "libswscale.so", "libc++_shared.so"
$cache = Join-Path $root "tools/ffmpeg/android"
New-Item -ItemType Directory -Force $cache | Out-Null

foreach ($abi in $abis.Keys) {
    $target = Join-Path $root "src/Pardeh.Android/lib/$abi"
    if (Test-Path (Join-Path $target "libavcodec.so")) { Write-Host "FFmpeg already in $target"; continue }
    $name = "ffmpeg-$Build-android-$abi-lgpl-shared-full.tar.xz"
    $archive = Join-Path $cache $name
    for ($attempt = 1; $attempt -le 10; $attempt++) {
        & curl.exe -L --fail --retry 5 --retry-delay 3 -C - -o $archive "$base/$name"
        if ($LASTEXITCODE -eq 0) { break }
    }
    if ($LASTEXITCODE -ne 0) { throw "Download failed: $base/$name" }
    $hash = (Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($hash -ne $abis[$abi]) { Remove-Item $archive; throw "SHA-256 mismatch for $name ($hash)" }

    $extract = Join-Path $cache "extract-$abi"
    Remove-Item -Recurse -Force $extract -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force $extract | Out-Null
    & tar.exe -xf $archive -C $extract
    if ($LASTEXITCODE -ne 0) { throw "tar failed for $name" }
    New-Item -ItemType Directory -Force $target | Out-Null
    foreach ($lib in $keep) { Copy-Item (Join-Path $extract "lib/$lib") $target -Force }
    Copy-Item (Join-Path $extract "LICENSE.txt") (Join-Path $target "LICENSE.txt") -Force
    Remove-Item -Recurse -Force $extract
    Write-Host "FFmpeg $Build ($abi) installed to $target"
}
