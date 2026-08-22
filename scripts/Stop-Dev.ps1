<#
.SYNOPSIS
    Para el agente y la consola que haya arrancado Start-Dev.ps1.
#>
[CmdletBinding()]
param()

foreach ($nombre in 'AvDashboard.Agent', 'AvDashboard') {
    $procesos = Get-Process -Name $nombre -ErrorAction SilentlyContinue
    if ($procesos) {
        $procesos | Stop-Process -Force
        Write-Host "Parado: $nombre" -ForegroundColor DarkGray
    }
}
