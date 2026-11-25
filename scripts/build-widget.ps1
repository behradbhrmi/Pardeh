<#
.SYNOPSIS
    Builds the Windows 11 Widgets board package of Pardeh (src\Pardeh.Widgets) and collects it, signed, into dist\Pardeh-Widget\.

.DESCRIPTION
    The widget is a small MSIX that holds only the widget provider; the player stays the normal Pardeh app and talks to the provider
    over a local pipe. The package is signed with a self-signed certificate (subject CN=Pardeh) kept outside the repository in
    $HOME\.pardeh (pardeh-widget.pfx + pardeh-widget.pwd, created on first run). Installing it needs that certificate to be trusted once:
    Pardeh install.ps1 does that (one administrator prompt) and installs the package as part of installing the app.

.EXAMPLE
    .\scripts\build-widget.ps1
#>
param(
    [string]$Version = "1.0.0.0",
    [string]$OutDir = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\Pardeh.Widgets\Pardeh.Widgets.csproj"
if (-not $OutDir) { $OutDir = Join-Path $root "dist\Pardeh-Widget" }
$keyDir = Join-Path $HOME ".pardeh"
$pfx = Join-Path $keyDir "pardeh-widget.pfx"
$pwdFile = Join-Path $keyDir "pardeh-widget.pwd"
$cer = Join-Path $keyDir "pardeh-widget.cer"

# The signing certificate (created once, never committed, not left in any certificate store).
if (-not (Test-Path $pfx)) {
    New-Item -ItemType Directory -Force $keyDir | Out-Null
    $pw = [Guid]::NewGuid().ToString("N")
    Set-Content $pwdFile $pw -NoNewline
    $c = New-SelfSignedCertificate -Type Custom -Subject "CN=Pardeh" -KeyUsage DigitalSignature -FriendlyName "Pardeh widget (self-signed)" `
        -CertStoreLocation "Cert:\CurrentUser\My" -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}") -NotAfter (Get-Date).AddYears(5)
    Export-PfxCertificate -Cert $c -FilePath $pfx -Password (ConvertTo-SecureString $pw -AsPlainText -Force) | Out-Null
    Export-Certificate -Cert $c -FilePath $cer | Out-Null
    Remove-Item "Cert:\CurrentUser\My\$($c.Thumbprint)"
}
$password = (Get-Content $pwdFile -Raw).Trim()

# signtool from the newest Windows SDK
$signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -match '^\d' } |
    Sort-Object { [version]($_.Name -replace '[^0-9.].*$', '') } -Descending |
    ForEach-Object { Join-Path $_.FullName "x64\signtool.exe" } | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $signtool) { throw "signtool.exe not found (install the Windows SDK)." }

# The manifest version follows the build.
$manifest = Join-Path $root "src\Pardeh.Widgets\Package.appxmanifest"
(Get-Content $manifest -Raw) -replace '(<Identity [^>]*Version=")[^"]*(")', "`${1}$Version`$2" | Set-Content $manifest -NoNewline

Remove-Item (Join-Path $root "src\Pardeh.Widgets\AppPackages") -Recurse -Force -ErrorAction SilentlyContinue
dotnet build $project -c Release -p:Platform=x64 -p:PackageCertificateKeyFile=$pfx -p:PackageCertificatePassword=$password -nologo | Out-Host
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed" }

$packageDir = Get-ChildItem (Join-Path $root "src\Pardeh.Widgets\AppPackages") -Directory | Select-Object -First 1
$msix = Get-ChildItem $packageDir.FullName -Filter *.msix | Select-Object -First 1
if (-not $msix) { throw "No .msix was produced." }

& $signtool sign /fd SHA256 /f $pfx /p $password $msix.FullName | Out-Host
if ($LASTEXITCODE -ne 0) { throw "signtool failed" }

if (Test-Path $OutDir) { Remove-Item $OutDir -Recurse -Force }
New-Item -ItemType Directory -Force $OutDir | Out-Null
Copy-Item $msix.FullName (Join-Path $OutDir "PardehWidget.msix")
Copy-Item $cer (Join-Path $OutDir "PardehWidget.cer")
Copy-Item (Join-Path $packageDir.FullName "Dependencies\x64\Microsoft.WindowsAppRuntime.2.msix") (Join-Path $OutDir "Microsoft.WindowsAppRuntime.2.msix")
Write-Host ""
Write-Host "Widget package ready in $OutDir"
Write-Host "It is installed by Pardeh install.ps1 (the package is copied into the Pardeh package in Widget\)."
