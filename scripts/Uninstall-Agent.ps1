<#
.SYNOPSIS
    Desinstala el servicio del agente. No borra los datos.

.PARAMETER RemoveData
    Borra también %ProgramData%\AvDashboard, incluidos la base de datos y el histórico.
#>
[CmdletBinding()]
param(
    [string]$ServiceName = 'AvDashboardAgent',
    [switch]$RemoveData
)

$ErrorActionPreference = 'Stop'

if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $ServiceName | Out-Null
    Write-Host "Servicio $ServiceName eliminado." -ForegroundColor Green
} else {
    Write-Host "El servicio $ServiceName no está instalado." -ForegroundColor Yellow
}

if ($RemoveData) {
    $dataDir = Join-Path $env:ProgramData 'AvDashboard'
    if (Test-Path $dataDir) {
        # El histórico del Timeline es lo que permite reconstruir qué pasó en un show
        # pasado. Se avisa antes de tirarlo.
        Write-Host "Se va a borrar $dataDir, incluido todo el histórico." -ForegroundColor Yellow
        Remove-Item -Recurse -Force $dataDir
        Write-Host 'Datos eliminados.' -ForegroundColor Green
    }
}
