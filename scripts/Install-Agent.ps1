<#
.SYNOPSIS
    Instala el agente de AV Dashboard como servicio de Windows.

.DESCRIPTION
    El agente corre como LocalSystem para poder leer el estado del sistema antes del login
    y sobrevivir al cierre de sesión. Escucha solo en 127.0.0.1 y, si la hay, en la IP de
    Tailscale: nunca en 0.0.0.0.

    Requiere un terminal elevado.

.PARAMETER Path
    Carpeta que contiene AvDashboard.Agent.exe. Por defecto, la publicación estándar.

.EXAMPLE
    .\Install-Agent.ps1 -Path C:\Program` Files\AvDashboard
#>
[CmdletBinding()]
param(
    [string]$Path = "$env:ProgramFiles\AvDashboard",
    [string]$ServiceName = 'AvDashboardAgent'
)

$ErrorActionPreference = 'Stop'

if (-not ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()
        ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Este script necesita un terminal elevado.'
}

$exe = Join-Path $Path 'AvDashboard.Agent.exe'
if (-not (Test-Path $exe)) {
    throw "No se encuentra $exe. Publica primero: dotnet publish src/AvDashboard.Agent -c Release -o `"$Path`""
}

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "El servicio $ServiceName ya existe. Se detiene y se actualiza el binario." -ForegroundColor Yellow
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
}

New-Service -Name $ServiceName `
    -BinaryPathName "`"$exe`"" `
    -DisplayName 'AV Dashboard Agent' `
    -Description 'Telemetría y control de rig audiovisual. Escucha solo en loopback y Tailscale.' `
    -StartupType Automatic | Out-Null

# Reinicio automático ante caída: el agente no puede quedarse muerto durante un show.
sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/10000/restart/30000 | Out-Null

Start-Service -Name $ServiceName

$dataDir = Join-Path $env:ProgramData 'AvDashboard'
Write-Host ''
Write-Host "Servicio $ServiceName instalado y arrancado." -ForegroundColor Green
Write-Host "  Datos y token: $dataDir"
Write-Host '  El token rota en cada arranque del servicio.'
Write-Host '  La consola necesita leerlo, así que ejecútala elevada o añade tu usuario a la ACL.'
