<#
.SYNOPSIS
    Regenerates everything that is a copy of the Pardeh logo from its master PNG (the variant chosen by PardehLogoArt.Default).

.DESCRIPTION
    The logo is a picture: src\Pardeh.UI\Assets\pardeh-logo-<variant>.png (see Assets\README.md; embedded in
    Pardeh.UI and drawn by FullMode\LogoImage). This script writes every derived file from the default variant's master:
      - Android (src\Pardeh.Android\Resources): drawable-nodpi\pardeh_logo.png (512 px, large notification icon, recents),
        ic_launcher_foreground.png and ic_launcher_monochrome.png (432 px = 108 dp adaptive layers, the mark inside the 66 dp safe
        zone), drawable\ic_launcher_pardeh_background.xml (dark navy tile), ic_stat_pardeh.png in drawable-mdpi..xxxhdpi (24 dp white
        status silhouette); it removes the old vector drawables of the same names.
      - Windows: src\Pardeh.App\pardeh.ico (16, 20, 24, 32, 40, 48, 64, 96, 128, 192, 256 px, PNG frames).
      - docs\img\pardeh-logo-512.png.
    It builds the test project to a scratch folder and runs LogoTests.Export_logo_files with PARDEH_LOGO_EXPORT set.

    -Preview <folder> also writes logo.png, launcher.png (circle / squircle / rounded-square masks and the themed icon),
    notification.png, notification_zoom.png and variants.png there for a look.

    LogoTests.Android_logo_files_are_what_the_master_generates fails if the committed files drift from the master.

.EXAMPLE
    .\scripts\export-logo-image.ps1 -Preview $env:TEMP\pardeh-logo
#>
param(
    [string]$Preview = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

$out = Join-Path ([IO.Path]::GetTempPath()) "pardeh-logo-export"
New-Item -ItemType Directory -Force -Path $out | Out-Null

dotnet build (Join-Path $root "tests\Pardeh.Skinning.Tests") -nologo -v q -p:OutDir="$out\"
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed" }

$env:PARDEH_LOGO_EXPORT = $root
if ($Preview) { $env:PARDEH_LOGO_SHEET = (New-Item -ItemType Directory -Force -Path $Preview).FullName }
try {
    & (Join-Path $out "Pardeh.Skinning.Tests.exe") -method "*Export_logo_files*" -method "*Preview_sheets*"
    if ($LASTEXITCODE -ne 0) { throw "the export test failed" }
}
finally {
    Remove-Item Env:\PARDEH_LOGO_EXPORT -ErrorAction SilentlyContinue
    Remove-Item Env:\PARDEH_LOGO_SHEET -ErrorAction SilentlyContinue
}

Write-Host "Logo files regenerated from the master of the default variant."
