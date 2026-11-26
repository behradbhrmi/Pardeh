<#
.SYNOPSIS
    Installs the Windows 11 Widgets board widget from the Widget folder next to this script. Used by install.ps1 and by
    the installer. Windows asks once for administrator rights, to trust the widget's certificate.
#>
$widget = Join-Path $PSScriptRoot "Widget"
if (-not (Test-Path (Join-Path $widget "PardehWidget.msix")) -or [Environment]::OSVersion.Version.Build -lt 22000) { return }

try {
    $cerFile = Join-Path $widget "PardehWidget.cer"
    $thumb = [Security.Cryptography.X509Certificates.X509Certificate2]::new($cerFile).Thumbprint
    $isTrusted = { [bool](Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object { $_.Thumbprint -eq $thumb }) }
    if (-not (& $isTrusted)) {
        # Windows only installs a package signed by a trusted publisher: trusting the widget's own certificate is the one step that needs administrator rights.
        Write-Host "Windows will ask once for administrator rights, to trust the Pardeh widget package..."
        $trust = "Import-Certificate -FilePath '$($cerFile -replace "'", "''")' -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null"
        try { Start-Process powershell -Verb RunAs -Wait -WindowStyle Hidden -ArgumentList "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", "`"$trust`"" } catch { }
    }

    if (& $isTrusted) {
        $deps = @(); $runtime = Join-Path $widget "Microsoft.WindowsAppRuntime.2.msix"
        if (Test-Path $runtime) { $deps = @($runtime) }
        Add-AppxPackage -Path (Join-Path $widget "PardehWidget.msix") -DependencyPath $deps -ForceApplicationShutdown -ErrorAction Stop
        Write-Host "Widget installed: open the Widgets board (Win + W) > + > Pardeh player."
    } else {
        Write-Warning "The widget was not installed (the administrator prompt was declined). Run the installation again to add it."
        exit 1
    }
} catch {
    Write-Warning "The widget could not be installed: $($_.Exception.Message)"
    exit 1
}
exit 0
