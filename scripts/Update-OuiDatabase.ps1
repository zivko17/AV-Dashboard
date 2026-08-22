<#
.SYNOPSIS
    Descarga la base de OUI de IEEE para resolver fabricantes por MAC sin internet.

.DESCRIPTION
    Es lo único de todo el proyecto que necesita conexión, y solo una vez. El resultado se
    deja en %ProgramData%\AvDashboard\oui.csv y a partir de ahí el escaneo del segmento
    resuelve fabricantes offline.

    Sin este fichero, AV Dashboard funciona con una semilla mínima de prefijos verificados
    y el resto de dispositivos aparecen sin fabricante — que es preferible a aparecer con
    uno equivocado.

.PARAMETER Destination
    Dónde dejar el CSV combinado.
#>
[CmdletBinding()]
param(
    [string]$Destination = (Join-Path $env:ProgramData 'AvDashboard\oui.csv')
)

$ErrorActionPreference = 'Stop'

# MA-L son 24 bits, MA-M 28 y MA-S 36. AV Dashboard los lee los tres del mismo fichero:
# la longitud del campo Assignment determina el tramo.
$sources = @(
    'https://standards-oui.ieee.org/oui/oui.csv',
    'https://standards-oui.ieee.org/oui28/mam.csv',
    'https://standards-oui.ieee.org/oui36/oui36.csv'
)

$directory = Split-Path -Parent $Destination
if (-not (Test-Path $directory)) {
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}

$temporary = "$Destination.tmp"
if (Test-Path $temporary) { Remove-Item $temporary -Force }

$first = $true
foreach ($source in $sources) {
    Write-Host "Descargando $source" -ForegroundColor Cyan

    try {
        $response = Invoke-WebRequest -Uri $source -UseBasicParsing -TimeoutSec 120
    } catch {
        Write-Warning "No se ha podido descargar $source : $_"
        continue
    }

    $lines = $response.Content -split "`r?`n"

    # La cabecera solo se conserva del primer fichero.
    if (-not $first) { $lines = $lines | Select-Object -Skip 1 }
    $first = $false

    Add-Content -Path $temporary -Value $lines -Encoding UTF8
}

if (-not (Test-Path $temporary)) {
    throw 'No se ha podido descargar ninguna de las tres bases de IEEE.'
}

Move-Item -Path $temporary -Destination $Destination -Force

$count = (Get-Content $Destination | Measure-Object -Line).Lines - 1
Write-Host ''
Write-Host "Base de OUI actualizada: $count prefijos en $Destination" -ForegroundColor Green
