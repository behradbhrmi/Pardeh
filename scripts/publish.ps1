<#
.SYNOPSIS
    Builds a release of Pardeh and packages it as dist\Pardeh-<version>-win-x64.zip with install.ps1 inside.

.DESCRIPTION
    Framework-dependent build (needs the .NET 11 desktop runtime on the target PC). FFmpeg comes from
    tools\ffmpeg\win-x64 (run scripts\get-ffmpeg.ps1 first). Nothing from Windows XP is ever packaged: the script
    drops native debug symbols and refuses to produce a package that contains wmploc / skins / private files.

.EXAMPLE
    .\scripts\publish.ps1 -Version 0.1.0
#>
param(
    [string]$Version = "0.1.0",
    [string]$Runtime = "win-x64",
    [switch]$SkipWidget
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root "dist\Pardeh-$Version-$Runtime"
$zip = "$out.zip"

if (-not (Test-Path (Join-Path $root "tools\ffmpeg\$Runtime\avcodec-62.dll"))) {
    Write-Warning "FFmpeg isn't in tools\ffmpeg\${Runtime}: the package will fall back to Media Foundation. Run scripts\get-ffmpeg.ps1 first."
}

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
dotnet publish (Join-Path $root "src\Pardeh.App\Pardeh.App.csproj") -c Release -r $Runtime --self-contained false `
    -p:Version=$Version -p:PublishDir="$out\" -p:DebugType=none -nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

# The SkiaSharp / HarfBuzzSharp NuGet packages carry their native debug symbols (about 100 MB uncompressed); drop them.
Get-ChildItem $out -Recurse -Filter *.pdb | Remove-Item -Force

# Safety net: nothing from Windows XP / WMP, no skins and no private development files may ever be packaged.
$forbidden = Get-ChildItem $out -Recurse -File | Where-Object {
    $_.Name -match '^(wmploc|wmp)\.(dll|dl_)$' -or $_.Extension -in '.wmz', '.wms', '.wsz', '.wal' -or $_.FullName.Substring($out.Length) -match '[\\/](private|WMP9)[\\/]'
}
if ($forbidden) { throw "Refusing to package third-party assets: $($forbidden.FullName -join ', ')" }

Copy-Item (Join-Path $PSScriptRoot "install.ps1") $out
Copy-Item (Join-Path $PSScriptRoot "uninstall.ps1") $out
Copy-Item (Join-Path $root "README.md") $out -ErrorAction SilentlyContinue
Copy-Item (Join-Path $root "LICENSE") $out

# The Windows 11 Widgets board widget: its own signed package, installed by install.ps1 together with the app.
if (-not $SkipWidget) {
    & (Join-Path $PSScriptRoot "build-widget.ps1") -OutDir (Join-Path $out "Widget") -Version "$Version.0"
    if ($LASTEXITCODE -ne 0 -and $null -ne $LASTEXITCODE) { throw "build-widget.ps1 failed" }
}

if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path "$out\*" -DestinationPath $zip
Write-Host "Package: $zip"
Write-Host "Install: unzip it, then right-click install.ps1 > Run with PowerShell (the app itself needs no administrator rights)."
if (-not $SkipWidget) { Write-Host "It also installs the Windows 11 Widgets board widget (Windows asks once for administrator rights to trust it)." }
