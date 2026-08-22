<#
.SYNOPSIS
    Instala los ganchos de git del repositorio.

.DESCRIPTION
    Apunta core.hooksPath a .githooks, que es donde vive el gancho de pre-commit que
    regenera docs/grafo.md.

    Esto hay que ejecutarlo una vez por clon. Git no instala ganchos al clonar a
    propósito: un repositorio que ejecutara código en tu máquina solo por descargarlo
    sería un problema de seguridad, no una comodidad.

.EXAMPLE
    .\scripts\Install-Hooks.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$raiz = Split-Path -Parent $PSScriptRoot

git -C $raiz config core.hooksPath .githooks

Write-Host "Ganchos instalados." -ForegroundColor Green
Write-Host "A partir de ahora, cada commit regenera docs/grafo.md y lo añade." -ForegroundColor DarkGray
Write-Host ""
Write-Host "Para quitarlos:  git config --unset core.hooksPath" -ForegroundColor DarkGray
