<#
.SYNOPSIS
    Builds a release of Pardeh and packages it as a zip in dist\ with install.ps1 inside.

.DESCRIPTION
    By default the package is small and needs the .NET 11 desktop runtime on the target PC
    (Pardeh-<version>-win-x64.zip). With -Standalone it carries its own runtime and needs nothing installed
    (Pardeh-<version>-win-x64-standalone.zip). -Installer builds that package and, from it, the setup program
    (Pardeh-<version>-win-x64-setup.exe; needs Inno Setup 6.6 or newer). The app is compiled ahead of time with
    ReadyToRun for a faster start. FFmpeg comes from
    tools\ffmpeg\win-x64 (run scripts\get-ffmpeg.ps1 first). Nothing from Windows XP is ever packaged: the script
    drops native debug symbols and refuses to produce a package that contains wmploc / skins / private files.

.EXAMPLE
    .\scripts\publish.ps1 -Version 0.1.0
    .\scripts\publish.ps1 -Version 0.1.0 -Standalone
    .\scripts\publish.ps1 -Version 0.1.0 -Installer
#>
param(
    [string]$Version = "0.1.0",
    [string]$Runtime = "win-x64",
    [switch]$SkipWidget,
    [switch]$Standalone,
    [switch]$Installer
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if ($Installer) {
    $Standalone = $true
    if ($Runtime -ne "win-x64") { throw "The installer is only made for win-x64." }
    # Look for the compiler before the long build.
    $iscc = @(
        (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
    ) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
    if (-not $iscc) { throw "Inno Setup 6 is needed for -Installer: winget install JRSoftware.InnoSetup" }
}
$flavour = if ($Standalone) { "-standalone" } else { "" }
$out = Join-Path $root "dist\Pardeh-$Version-$Runtime$flavour"
$zip = "$out.zip"

if (-not (Test-Path (Join-Path $root "tools\ffmpeg\$Runtime\avcodec-62.dll"))) {
    Write-Warning "FFmpeg isn't in tools\ffmpeg\${Runtime}: the package will fall back to Media Foundation. Run scripts\get-ffmpeg.ps1 first."
}

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
$selfContained = if ($Standalone) { "true" } else { "false" }
dotnet publish (Join-Path $root "src\Pardeh.App\Pardeh.App.csproj") -c Release -r $Runtime --self-contained $selfContained `
    -p:Version=$Version -p:PublishDir="$out\" -p:DebugType=none -p:PublishReadyToRun=true -nologo
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
Copy-Item (Join-Path $PSScriptRoot "install-widget.ps1") $out
Copy-Item (Join-Path $PSScriptRoot "uninstall-widget.ps1") $out
Copy-Item (Join-Path $root "README.md") $out -ErrorAction SilentlyContinue
Copy-Item (Join-Path $root "LICENSE") $out
if ($Standalone) {
    # The package redistributes the .NET runtime: its license and notices go with it.
    $dotnetRoot = Split-Path -Parent (Get-Command dotnet).Source
    foreach ($notice in "LICENSE.txt", "ThirdPartyNotices.txt") {
        Copy-Item (Join-Path $dotnetRoot $notice) (Join-Path $out "dotnet-$notice")
    }
}

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

if ($Installer) {
    & $iscc /Qp "/DAppVersion=$Version" "/DSourceDir=$out" "/DOutputDir=$(Join-Path $root 'dist')" (Join-Path $PSScriptRoot "installer.iss")
    if ($LASTEXITCODE -ne 0) { throw "building the installer failed" }
    Write-Host "Installer: $(Join-Path $root "dist\Pardeh-$Version-$Runtime-setup.exe")"
}
