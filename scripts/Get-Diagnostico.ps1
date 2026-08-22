<#
.SYNOPSIS
    Saca un informe de lo que el agente ve en esta maquina, para pegarlo tal cual.

.DESCRIPTION
    Todo el interop de este proyecto —registro, WMI, powercfg, tareas programadas,
    QueryDisplayConfig— esta verificado contra el compilador y contra nada mas. Este script
    es la unica forma de saber que devuelve de verdad en un rig, sin ir pantalla por
    pantalla apuntando numeros.

    Pregunta al agente por la auditoria, las pantallas y el audio, y escribe un informe
    corto en texto plano. No cambia nada: solo lee.

    Importa ejecutarlo dos veces, normal y como administrador. La diferencia entre las dos
    en el bloque «sin lectura» es exactamente lo que dice si la sonda necesita correr en
    modo servicio o le basta con la sesion del usuario.

.PARAMETER Puerto
    Puerto del agente. 47821 salvo que se haya cambiado.

.PARAMETER Fichero
    Donde escribir el informe. Por defecto, el escritorio.

.EXAMPLE
    .\scripts\Get-Diagnostico.ps1
#>
[CmdletBinding()]
param(
    [int]$Puerto = 47821,
    [string]$Fichero
)

$ErrorActionPreference = 'Stop'

# ------------------------------------------------------------------ utilidades

function Get-Token {
    if ($env:AVDASHBOARD_TOKEN) { return $env:AVDASHBOARD_TOKEN.Trim() }

    $rutas = @(
        (Join-Path $env:ProgramData 'AvDashboard\agent.token'),
        (Join-Path $env:LocalAppData 'AvDashboard\agent.token')
    )

    foreach ($ruta in $rutas) {
        if (Test-Path $ruta) {
            try { return (Get-Content $ruta -Raw).Trim() } catch { }
        }
    }

    return $null
}

function Get-Api {
    param([string]$Ruta)

    try {
        return Invoke-RestMethod -Uri "$baseUrl$Ruta" -Headers $cabeceras -TimeoutSec 30
    } catch {
        Write-Host "  no se ha podido leer ${Ruta}: $($_.Exception.Message)" -ForegroundColor DarkYellow
        return $null
    }
}

# Una linea de tabla con el nombre a la izquierda y el valor alineado a la derecha.
function Format-Fila {
    param([string]$Etiqueta, $Valor, [int]$Ancho = 34)

    $punteado = $Etiqueta.PadRight($Ancho, '.')
    return "  $punteado $Valor"
}

# ------------------------------------------------------------------ conexion

$baseUrl = "http://127.0.0.1:$Puerto"
$token = Get-Token

if (-not $token) {
    Write-Host 'No se ha encontrado el token del agente.' -ForegroundColor Red
    Write-Host 'Arranca el agente primero con .\scripts\Start-Dev.ps1' -ForegroundColor DarkGray
    return
}

$cabeceras = @{ Authorization = "Bearer $token" }

try {
    $salud = Invoke-RestMethod -Uri "$baseUrl/health" -TimeoutSec 5
} catch {
    Write-Host "El agente no responde en $baseUrl." -ForegroundColor Red
    Write-Host 'Arrancalo con .\scripts\Start-Dev.ps1 y vuelve a intentarlo.' -ForegroundColor DarkGray
    return
}

$identidad = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identidad)
$elevado = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

Write-Host 'Preguntando al agente...' -ForegroundColor DarkGray

$auditoria = Get-Api '/api/v1/system/audit'
$pantallas = Get-Api '/api/v1/video/displays'
$audio     = Get-Api '/api/v1/audio/devices'

# ------------------------------------------------------------------ informe

$lineas = New-Object System.Collections.Generic.List[string]

$lineas.Add('AV DASHBOARD - DIAGNOSTICO DEL NODO')
$lineas.Add('=' * 72)
$lineas.Add('')
$lineas.Add((Format-Fila 'Generado' (Get-Date -Format 'yyyy-MM-dd HH:mm')))
if ($elevado) {
    $lineas.Add((Format-Fila 'Como administrador' 'si'))
} else {
    $lineas.Add((Format-Fila 'Como administrador' 'NO'))
}
$lineas.Add((Format-Fila 'Agente' $salud.version))
$lineas.Add((Format-Fila 'Windows' ([Environment]::OSVersion.Version.ToString())))
$lineas.Add((Format-Fila 'PowerShell' $PSVersionTable.PSVersion.ToString()))
$lineas.Add('')

if ($auditoria) {
    $todos = @()
    foreach ($grupo in $auditoria.groups) { $todos += $grupo.settings }

    $lineas.Add('AUDITORIA')
    $lineas.Add('-' * 72)
    $lineas.Add((Format-Fila 'Filas en el catalogo' $todos.Count))
    $lineas.Add((Format-Fila 'Coinciden con el objetivo' (@($todos | Where-Object { $_.verdict -eq 'Match' })).Count))
    $lineas.Add((Format-Fila 'Difieren, ambar' (@($todos | Where-Object { $_.verdict -eq 'Differs' })).Count))
    $lineas.Add((Format-Fila 'Difieren, rojo' (@($todos | Where-Object { $_.verdict -eq 'DiffersCritical' })).Count))
    $lineas.Add((Format-Fila 'Sin lectura' (@($todos | Where-Object { $_.verdict -eq 'Unknown' })).Count))
    $lineas.Add((Format-Fila 'Informativos' (@($todos | Where-Object { $_.verdict -eq 'Informational' })).Count))
    $lineas.Add((Format-Fila 'Escribibles desde el agente' (@($todos | Where-Object { $_.writable })).Count))
    $lineas.Add('')

    # Lo que no se ha podido leer. Es la pregunta que decide si la sonda necesita servicio.
    $ilegibles = @($todos | Where-Object { $_.verdict -eq 'Unknown' })
    $lineas.Add("SIN LECTURA ($($ilegibles.Count))")
    $lineas.Add('-' * 72)
    if ($ilegibles.Count -eq 0) {
        $lineas.Add('  (ninguna: se ha podido leer todo)')
    }
    foreach ($fila in $ilegibles) {
        $motivo = $fila.unavailable
        if (-not $motivo) { $motivo = 'sin motivo declarado' }
        $lineas.Add("  [$($fila.definition.id)] $($fila.definition.name)")
        $lineas.Add("      $motivo")
    }
    $lineas.Add('')

    # Grupo por grupo, con el valor de verdad. Energia es el que valida los GUID de
    # powercfg escritos a mano, que es lo mas fragil que hay ahora mismo.
    foreach ($grupo in $auditoria.groups) {
        $lineas.Add("$($grupo.group.ToString().ToUpper()) ($($grupo.settings.Count))")
        $lineas.Add('-' * 72)

        foreach ($fila in $grupo.settings) {
            $marca = switch ($fila.verdict) {
                'Match'           { 'ok  ' }
                'Differs'         { 'AMB ' }
                'DiffersCritical' { 'ROJO' }
                'Unknown'         { '??  ' }
                default           { 'info' }
            }

            $valor = $fila.display
            if (-not $valor) { $valor = $fila.current }
            if (-not $valor) { $valor = $fila.unavailable }
            if (-not $valor) { $valor = '(vacio)' }

            $nombre = $fila.definition.name
            if ($nombre.Length -gt 42) { $nombre = $nombre.Substring(0, 41) + '~' }

            $linea = "  $marca $($nombre.PadRight(42)) $valor"

            if ($fila.verdict -eq 'Differs' -or $fila.verdict -eq 'DiffersCritical') {
                $linea += "   [objetivo: $($fila.definition.targetDescription)]"
            }

            $lineas.Add($linea)
        }

        $lineas.Add('')
    }
}

# Pantallas: valida QueryDisplayConfig y, con dos o mas, la clave de conector.
if ($pantallas) {
    $salidas = @($pantallas.outputs)
    $lineas.Add("PANTALLAS ($($salidas.Count))")
    $lineas.Add('-' * 72)

    if ($salidas.Count -eq 0) {
        $lineas.Add('  (ninguna: QueryDisplayConfig no ha devuelto nada)')
    }

    foreach ($salida in $salidas) {
        $etiquetas = @()
        if ($salida.isActive)  { $etiquetas += 'activa' }
        if ($salida.isPrimary) { $etiquetas += 'primaria' }

        $lineas.Add("  $($salida.deviceName)")
        $lineas.Add("      monitor ...... $($salida.monitorName)")
        $lineas.Add("      conector ..... $($salida.physicalConnector)")
        $lineas.Add("      estado ....... $($etiquetas -join ', ')")
        $lineas.Add("      modo ......... $($salida.width)x$($salida.height)")

        # El refresco exacto es la razon de haber cambiado a QueryDisplayConfig: 60000/1001
        # y 60/1 se ven iguales redondeados y son justo la deriva de un show entero.
        $lineas.Add("      refresco ..... $($salida.refreshHz) Hz ($($salida.refreshNumerator)/$($salida.refreshDenominator))")
        $lineas.Add("      EDID leido ... $($salida.edidAvailable)")
    }

    $lineas.Add('')
}

if ($audio) {
    $dispositivos = @($audio.devices)
    $activos = @($dispositivos | Where-Object { $_.state -eq 'Active' })

    $lineas.Add('AUDIO')
    $lineas.Add('-' * 72)
    $lineas.Add((Format-Fila 'Endpoints en total' $dispositivos.Count))
    $lineas.Add((Format-Fila 'Activos' $activos.Count))
    $lineas.Add((Format-Fila 'Con sample rate distinto' (@($dispositivos | Where-Object { $_.hasSampleRateMismatch })).Count))
    $lineas.Add('')
}

$informe = $lineas -join [Environment]::NewLine

Write-Host ''
Write-Host $informe
Write-Host ''

if (-not $Fichero) {
    $Fichero = Join-Path ([Environment]::GetFolderPath('Desktop')) 'avdash-diagnostico.txt'
}

$informe | Set-Content -Path $Fichero -Encoding UTF8
Write-Host "Informe escrito en $Fichero" -ForegroundColor Green

try {
    Set-Clipboard -Value $informe
    Write-Host 'Y copiado al portapapeles: pegalo tal cual.' -ForegroundColor Green
} catch {
    Write-Host 'No se ha podido copiar al portapapeles; abre el fichero y copia de ahi.' -ForegroundColor DarkGray
}

if (-not $elevado) {
    Write-Host ''
    Write-Host 'Esta ejecucion NO era de administrador. Vuelve a lanzarlo desde una' -ForegroundColor Yellow
    Write-Host 'terminal elevada: la diferencia en el bloque «sin lectura» es el dato' -ForegroundColor Yellow
    Write-Host 'que dice si la sonda necesita correr como servicio.' -ForegroundColor Yellow
}
