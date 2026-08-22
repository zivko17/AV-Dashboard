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

# 0. Que exista el SDK. El error de `dotnet` no encontrado sale como un ParserError de
#    PowerShell y no dice lo que pasa.
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host 'No se encuentra `dotnet`.' -ForegroundColor Red
    Write-Host 'Hace falta el SDK de .NET 8: https://dotnet.microsoft.com/download/dotnet/8.0' -ForegroundColor DarkGray
    return
}

# Sin privilegios, media docena de ajustes del catalogo no se pueden ni leer ni escribir, y
# la pestana Sistema los lista en «sin lectura». No es un fallo, pero conviene saberlo antes
# de mirar la pantalla y no despues.
$principal = New-Object Security.Principal.WindowsPrincipal(
    [Security.Principal.WindowsIdentity]::GetCurrent())

if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host 'Aviso: esta terminal no es de administrador.' -ForegroundColor Yellow
    Write-Host 'Arranca igual, pero habra ajustes que el agente no pueda leer ni cambiar.' -ForegroundColor DarkGray
    Write-Host ''
}

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

    # Al cerrar la ventana, el agente sigue vivo a proposito: asi se puede seguir
    # trasteando con la API. Pero hay que decirlo, o se queda ahi sin que nadie lo sepa.
    Write-Host ''
    Write-Host 'La consola se ha cerrado. El agente sigue corriendo en segundo plano.' -ForegroundColor DarkGray
    Write-Host 'Para pararlo:  .\scripts\Stop-Dev.ps1' -ForegroundColor DarkGray
}
