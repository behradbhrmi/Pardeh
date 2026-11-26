<#
.SYNOPSIS
    Removes Pardeh installed by install.ps1. Your library, playlists, skins and settings in %APPDATA%\Pardeh are
    kept unless you pass -RemoveData.
#>
param([switch]$RemoveData)

$target = Join-Path $env:LOCALAPPDATA "Programs\Pardeh"
$exe = Join-Path $target "Pardeh.exe"
Get-Process Pardeh -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe } | Stop-Process -Force

# File type registration (Tools > Options > File Types), if any.
Remove-Item "HKCU:\Software\Classes\Pardeh.Media" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item "HKCU:\Software\Classes\Applications\Pardeh.exe" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item "HKCU:\Software\Pardeh" -Recurse -Force -ErrorAction SilentlyContinue
Remove-ItemProperty "HKCU:\Software\RegisteredApplications" -Name Pardeh -ErrorAction SilentlyContinue
Get-ChildItem "HKCU:\Software\Classes" -ErrorAction SilentlyContinue | Where-Object { $_.PSChildName.StartsWith(".") } | ForEach-Object {
    Remove-ItemProperty "$($_.PSPath)\OpenWithProgids" -Name "Pardeh.Media" -ErrorAction SilentlyContinue
}

# The Widgets board widget installed by install.ps1.
& (Join-Path $PSScriptRoot "uninstall-widget.ps1")

Remove-Item (Join-Path ([Environment]::GetFolderPath("Programs")) "Pardeh.lnk") -Force -ErrorAction SilentlyContinue
Remove-Item "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Pardeh" -Recurse -Force -ErrorAction SilentlyContinue

# The script lives in the folder it deletes: finish from a temporary copy-free command.
Start-Process cmd.exe -ArgumentList "/c timeout /t 2 >nul & rmdir /s /q `"$target`"" -WindowStyle Hidden
if ($RemoveData) { Remove-Item (Join-Path $env:APPDATA "Pardeh") -Recurse -Force -ErrorAction SilentlyContinue }
Write-Host "Pardeh was removed."
