<#
.SYNOPSIS
    Removes the Widgets board widget and the trust in its certificate. Used by uninstall.ps1 and by the installer.
#>
Get-AppxPackage -Name "Pardeh.Widgets" -ErrorAction SilentlyContinue | Remove-AppxPackage -ErrorAction SilentlyContinue
$widgetCer = Join-Path $PSScriptRoot "Widget\PardehWidget.cer"
if (Test-Path $widgetCer) {
    $thumb = [Security.Cryptography.X509Certificates.X509Certificate2]::new($widgetCer).Thumbprint
    if (Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object { $_.Thumbprint -eq $thumb }) {
        # Take the certificate trust back too (one administrator prompt; declining only leaves the unused certificate behind).
        $untrust = "Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object { `$_.Thumbprint -eq '$thumb' } | Remove-Item"
        try { Start-Process powershell -Verb RunAs -Wait -WindowStyle Hidden -ArgumentList "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", "`"$untrust`"" } catch { }
    }
}
