<#
.SYNOPSIS
    Ciclo de desarrollo completo: para el agente, compila, y arranca agente y consola.

.DESCRIPTION
    El agente en marcha mantiene tomadas AvDashboard.Core.dll y AvDashboard.Native.dll
    dentro de su propia carpeta de salida, así que `dotnet build` falla con MSB3027
    mientras esté corriendo. Es la clase de fricción que se paga en cada iteración, y por
    eso el orden correcto vive aquí y no en la memoria de nadie.

    El agente queda corriendo en segundo plano con su log en un fichero.

.PARAMETER SinConsola
    Compila y arranca solo el agente. Útil para trastear con la API desde PowerShell.

.PARAMETER SinCompilar
    Se salta la compilación. Para relanzar rápido sin haber tocado código.

.EXAMPLE
    .\scripts\Start-Dev.ps1
    .\scripts\Start-Dev.ps1 -SinConsola
#>
[CmdletBinding()]
param(
    [switch]$SinConsola,
    [switch]$SinCompilar
)

$ErrorActionPreference = 'Stop'

$raiz = Split-Path -Parent $PSScriptRoot
$log  = Join-Path $env:TEMP 'avdash-agente.log'
Set-Location $raiz

# 1. Parar el agente: es lo que bloquea las DLL.
$vivos = Get-Process -Name AvDashboard.Agent -ErrorAction SilentlyContinue
if ($vivos) {
    Write-Host "Parando el agente ($($vivos.Count) proceso(s))…" -ForegroundColor DarkGray
    $vivos | Stop-Process -Force
    Start-Sleep -Milliseconds 800
}

# 2. Compilar.
if (-not $SinCompilar) {
    Write-Host 'Compilando…' -ForegroundColor DarkGray
    dotnet build --nologo -v minimal
    if ($LASTEXITCODE -ne 0) {
        throw 'La compilación ha fallado. No se arranca nada.'
    }
}

# 3. Arrancar el agente en segundo plano.
Write-Host 'Arrancando el agente…' -ForegroundColor DarkGray
Start-Process -FilePath dotnet -WindowStyle Hidden `
    -ArgumentList 'run --project src/AvDashboard.Agent -- --portable --no-build' `
    -RedirectStandardOutput $log -RedirectStandardError "$log.err" | Out-Null

$vivo = $false
foreach ($i in 1..80) {
    Start-Sleep -Milliseconds 500
    try {
        Invoke-RestMethod 'http://127.0.0.1:47821/health' -TimeoutSec 2 | Out-Null
        $vivo = $true
        break
    } catch { }
}

if (-not $vivo) {
    Write-Host "El agente no responde. Log en $log" -ForegroundColor Red
    Get-Content $log, "$log.err" -ErrorAction SilentlyContinue | Select-Object -Last 30
    return
}

Write-Host "Agente en marcha. Log: $log" -ForegroundColor Green

# 4. La consola, en primer plano, para ver sus excepciones si las hay.
if (-not $SinConsola) {
    Write-Host 'Arrancando la consola…' -ForegroundColor DarkGray
    dotnet run --project src/AvDashboard.Console --no-build
}
