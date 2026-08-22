# Dependencias externas

Todo lo que AV Dashboard necesita y no puede traer consigo. Cada una se detecta en
runtime y su ausencia degrada un módulo concreto, nunca tumba el agente.

## Del ecosistema .NET

Se restauran desde NuGet en el primer `dotnet build`. Después, la caché local basta: el
requisito de funcionar sin internet aplica a la ejecución, no a la compilación.

| Paquete | Dónde | Para qué |
|---|---|---|
| `Microsoft.Win32.Registry` | Native | Lectura de MMCSS, Fast Startup, ASIO, PnPCapabilities |
| `System.Management` | Native | WMI: servicios, manifiesto de hardware |
| `System.ServiceProcess.ServiceController` | Native | Estado y control de servicios |
| `Microsoft.Data.Sqlite` | Agent | Base de datos local |
| `Microsoft.Extensions.Hosting.WindowsServices` | Agent | Arrancar como servicio antes del login |
| `CommunityToolkit.Mvvm` | Console | MVVM con generadores de código |
| `Microsoft.AspNetCore.SignalR.Client` | Console | Telemetría en vivo |
| `xunit` | Tests | |

## Externas al proyecto

### Npcap — **requerido por PTP**

PTP no se puede leer con sockets normales: hay que capturar en la interfaz. Npcap debe
estar instalado **en modo compatible con WinPcap**.

- Sin Npcap: M3-PTP y el análisis fino de multicast se declaran no disponibles y la
  consola dice por qué. Todo lo demás de M3 y M5 funciona igual.
- Art-Net, sACN y los contadores de multicast **no** lo necesitan: se hacen con sockets
  normales unidos al grupo.

Comprobación: `scripts/Check-Dependencies.ps1`.

### LibreHardwareMonitorLib — temperaturas y throttling

Carga un driver en kernel para leer sensores. Es una decisión con consecuencias en
máquinas de cliente que no son tuyas, así que **no entra por defecto**: `SystemStatus`
devuelve `null` en temperaturas y contadores de throttling mientras no se active
explícitamente.

Cuando se active, es lo que da los eventos de throttling térmico y de power limit — la
razón real de que un rig en flight case caliente empiece a soltar frames.

### SDKs propietarios — no se redistribuyen

Ninguno viaja en el repositorio; `.gitignore` los excluye por si acaso.

| SDK | Módulo | Ruta esperada |
|---|---|---|
| Blackmagic Desktop Video SDK | M4 DeckLink | `vendor/DeckLinkSDK/` |
| NDI SDK | M4 fuentes NDI | `vendor/NdiSdk/` |
| NVAPI | M4 GPU, EDID, salidas físicas | `vendor/nvapi/` |

Si falta cualquiera, el módulo correspondiente aparece como `MissingDependency` con el
nombre del SDK que falta.

### Herramientas de línea de comandos

Se buscan en el `PATH` y son opcionales.

| Herramienta | Módulo | Sin ella |
|---|---|---|
| `ffprobe` / `ffmpeg` | M12 | No hay validación ni conversión de contenido |
| `yt-dlp` | M12 | No hay descarga |
| `iperf3` | M5 | No hay medida de throughput de LAN |
| `speedtest` | M5 | No hay medida de internet |

### Base de OUI de IEEE

La base completa son tres ficheros de varios megas y no viaja en el repositorio. Sin ella,
`OuiDatabase` funciona con una semilla mínima de prefijos verificados y el resto de
dispositivos aparecen **sin fabricante**, que es preferible a aparecer con uno equivocado.

Para tenerla completa y offline: `scripts/Update-OuiDatabase.ps1`, una sola vez.
