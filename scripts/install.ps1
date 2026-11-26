<#
.SYNOPSIS
    Installs Pardeh for the current user: %LOCALAPPDATA%\Programs\Pardeh, a Start menu shortcut, and an entry
    in Settings > Apps with a working uninstall. The app needs no administrator rights; the Windows 11 widget asks for them once (to trust its package). Run it from the unzipped package.
#>
$ErrorActionPreference = "Stop"
$source = $PSScriptRoot
$target = Join-Path $env:LOCALAPPDATA "Programs\Pardeh"
$exe = Join-Path $target "Pardeh.exe"

if (-not (Test-Path (Join-Path $source "Pardeh.exe"))) { throw "Run install.ps1 from the unzipped Pardeh package." }

# Close a running Pardeh so its files can be replaced.
$running = Get-Process Pardeh -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe }
if ($running) {
    $running | Stop-Process -Force
    $running | Wait-Process -Timeout 10 -ErrorAction SilentlyContinue
}

# An earlier installation is replaced, not copied over: a standalone package leaves a .NET runtime behind, which would
# stop the smaller package from starting. Only a folder that holds Pardeh is emptied, and never the one being installed
# from. Your library, playlists, skins and settings are in %APPDATA%\Pardeh and are not touched.
if ((Test-Path $exe) -and ([IO.Path]::GetFullPath($source).TrimEnd('\') -ne [IO.Path]::GetFullPath($target).TrimEnd('\'))) {
    Get-ChildItem -LiteralPath $target -Force | Remove-Item -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $target | Out-Null
Copy-Item -Path (Join-Path $source "*") -Destination $target -Recurse -Force

# Start menu shortcut.
$shortcutPath = Join-Path ([Environment]::GetFolderPath("Programs")) "Pardeh.lnk"
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $exe
$shortcut.WorkingDirectory = $target
$shortcut.Description = "Pardeh media player"
$shortcut.Save()

# Settings > Apps entry. One left by the installer (setup.exe) would point at files this script has replaced.
Remove-Item "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{8F3A2C91-5D47-4E0B-A6B2-1C9D84F7E350}_is1" -Recurse -Force -ErrorAction SilentlyContinue
$version = (Get-Item $exe).VersionInfo.ProductVersion
$key = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Pardeh"
New-Item -Path $key -Force | Out-Null
Set-ItemProperty -Path $key -Name DisplayName -Value "Pardeh"
Set-ItemProperty -Path $key -Name DisplayVersion -Value $version
Set-ItemProperty -Path $key -Name Publisher -Value "Pardeh contributors"
Set-ItemProperty -Path $key -Name DisplayIcon -Value "`"$exe`",0"
Set-ItemProperty -Path $key -Name InstallLocation -Value $target
Set-ItemProperty -Path $key -Name UninstallString -Value "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$target\uninstall.ps1`""
Set-ItemProperty -Path $key -Name NoModify -Value 1 -Type DWord
Set-ItemProperty -Path $key -Name NoRepair -Value 1 -Type DWord

# Windows 11 Widgets board widget (part of the install when the package is in this folder).
& (Join-Path $target "install-widget.ps1")

Write-Host "Pardeh $version installed to $target."
Write-Host "To open media files with it: Pardeh > Tools > Options > File Types."
