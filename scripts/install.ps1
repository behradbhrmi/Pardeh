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
Get-Process Pardeh -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe } | Stop-Process -Force

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

# Settings > Apps entry.
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

# Windows 11 Widgets board widget (part of the install; skipped on Windows 10 and when the package is not in this folder).
$widget = Join-Path $target "Widget"
if ((Test-Path (Join-Path $widget "PardehWidget.msix")) -and [Environment]::OSVersion.Version.Build -ge 22000) {
    try {
        $cerFile = Join-Path $widget "PardehWidget.cer"
        $thumb = [Security.Cryptography.X509Certificates.X509Certificate2]::new($cerFile).Thumbprint
        $isTrusted = { [bool](Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object { $_.Thumbprint -eq $thumb }) }
        if (-not (& $isTrusted)) {
            # Windows only installs a package signed by a trusted publisher: trusting the widget's own certificate is the one step that needs administrator rights.
            Write-Host "Windows will ask once for administrator rights, to trust the Pardeh widget package..."
            $trust = "Import-Certificate -FilePath '$cerFile' -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null"
            try { Start-Process powershell -Verb RunAs -Wait -WindowStyle Hidden -ArgumentList "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", $trust } catch { }
        }

        if (& $isTrusted) {
            $deps = @(); $runtime = Join-Path $widget "Microsoft.WindowsAppRuntime.2.msix"
            if (Test-Path $runtime) { $deps = @($runtime) }
            Add-AppxPackage -Path (Join-Path $widget "PardehWidget.msix") -DependencyPath $deps -ForceApplicationShutdown
            Write-Host "Widget installed: open the Widgets board (Win + W) > + > Pardeh player."
        } else {
            Write-Warning "The widget was not installed (the administrator prompt was declined). Run install.ps1 again to add it."
        }
    } catch {
        Write-Warning "The widget could not be installed: $($_.Exception.Message)"
    }
}

Write-Host "Pardeh $version installed to $target."
Write-Host "To open media files with it: Pardeh > Tools > Options > File Types."
