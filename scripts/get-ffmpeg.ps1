# Downloads an LGPL shared build of FFmpeg 8.1 (BtbN builds) into tools/ffmpeg/win-x64.
# Resumes interrupted downloads. The binaries are not committed (see .gitignore).
param([string]$Version = "8.1")

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$target = Join-Path $root "tools/ffmpeg/win-x64"
$zip = Join-Path $root "tools/ffmpeg/ffmpeg-$Version-win64-lgpl-shared.zip"
$url = "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-n$Version-latest-win64-lgpl-shared-$Version.zip"

if (Test-Path (Join-Path $target "avcodec-*.dll")) { Write-Host "FFmpeg already in $target"; exit 0 }
New-Item -ItemType Directory -Force (Split-Path $zip) | Out-Null

for ($attempt = 1; $attempt -le 30; $attempt++) {
    & curl.exe -L --fail --retry 5 --retry-delay 3 -C - -o $zip $url
    if ($LASTEXITCODE -eq 0) { break }
    Write-Host "Download interrupted (attempt $attempt), resuming..."
    Start-Sleep -Seconds 3
}
if ($LASTEXITCODE -ne 0) { throw "Download failed: $url" }

$extract = Join-Path $root "tools/ffmpeg/extract"
Remove-Item -Recurse -Force $extract -ErrorAction SilentlyContinue
Expand-Archive -Path $zip -DestinationPath $extract
$bin = Get-ChildItem -Path $extract -Recurse -Filter "avcodec-*.dll" | Select-Object -First 1
New-Item -ItemType Directory -Force $target | Out-Null
Copy-Item -Path (Join-Path $bin.DirectoryName "*.dll") -Destination $target
Get-ChildItem -Path $extract -Recurse -Include "LICENSE*", "COPYING*" | Copy-Item -Destination $target
Remove-Item -Recurse -Force $extract
Write-Host "FFmpeg $Version installed to $target"
