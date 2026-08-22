<#
.SYNOPSIS
    Comprueba qué dependencias externas hay en esta máquina y qué módulos quedan cojos.

.DESCRIPTION
    Ninguna de estas dependencias es obligatoria: sin ellas el agente arranca igual y
    declara el módulo correspondiente como no disponible, con su motivo. Este script sirve
    para saber qué vas a tener antes de llegar al sitio.
#>
[CmdletBinding()]
param()

function Test-Dependency {
    param(
        [string]$Name,
        [scriptblock]$Check,
        [string]$Module,
        [string]$Without
    )

    $present = $false
    try { $present = [bool](& $Check) } catch { $present = $false }

    $mark = if ($present) { 'OK   ' } else { 'FALTA' }
    $color = if ($present) { 'Green' } else { 'Yellow' }

    Write-Host ("[{0}] {1,-28} {2}" -f $mark, $Name, $Module) -ForegroundColor $color
    if (-not $present) {
        Write-Host ("        Sin ella: {0}" -f $Without) -ForegroundColor DarkGray
    }
}

Write-Host ''
Write-Host 'Dependencias externas de AV Dashboard' -ForegroundColor Cyan
Write-Host ''

Test-Dependency -Name '.NET 8 runtime' -Module 'todo' `
    -Check { (dotnet --list-runtimes 2>$null) -match 'Microsoft\.WindowsDesktop\.App 8\.' } `
    -Without 'no arranca nada; instala el runtime de escritorio de .NET 8'

Test-Dependency -Name 'Npcap' -Module 'M3-PTP, multicast fino' `
    -Check { Test-Path "$env:SystemRoot\System32\Npcap\wpcap.dll" } `
    -Without 'no hay lectura de PTP. Art-Net y sACN funcionan igual'

Test-Dependency -Name 'ffprobe / ffmpeg' -Module 'M12 Media Toolkit' `
    -Check { Get-Command ffprobe -ErrorAction SilentlyContinue } `
    -Without 'no hay validación ni conversión de contenido'

Test-Dependency -Name 'iperf3' -Module 'M5 throughput de LAN' `
    -Check { Get-Command iperf3 -ErrorAction SilentlyContinue } `
    -Without 'no hay medida de throughput antes de montar NDI'

Test-Dependency -Name 'Tailscale' -Module 'M7 multi-nodo' `
    -Check { Get-Service Tailscale -ErrorAction SilentlyContinue } `
    -Without 'el agente solo escucha en loopback'

Test-Dependency -Name 'Blackmagic Desktop Video' -Module 'M4 DeckLink' `
    -Check { Get-Service 'BlackmagicDesktopVideoHelper' -ErrorAction SilentlyContinue } `
    -Without 'no hay estado de DeckLink ni genlock'

Test-Dependency -Name 'Base de OUI de IEEE' -Module 'M5 fabricante por MAC' `
    -Check { Test-Path (Join-Path $env:ProgramData 'AvDashboard\oui.csv') } `
    -Without 'los dispositivos salen sin fabricante. Ejecuta Update-OuiDatabase.ps1'

Write-Host ''
