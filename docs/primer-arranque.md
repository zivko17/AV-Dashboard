# Primer arranque

El código compila y las pruebas pasan, pero **nadie lo ha ejecutado nunca**. Compilar no
es funcionar: falta saber si el agente arranca, si la consola conecta y si los P/Invoke
devuelven lo que se espera de ellos.

Esta guía es para ese primer intento, en una máquina que puedas romper sin consecuencias
—no un rig con un evento el viernes—.

## 1. Requisitos

- Windows 11 x64 (vale Windows 10 1809+).
- **SDK de .NET 8**: `winget install Microsoft.DotNet.SDK.8`
- No hace falta ser administrador. Todo esto corre en modo portátil.

```powershell
git clone https://github.com/zivko17/AV-Dashboard.git
cd AV-Dashboard
dotnet build
```

Si el build falla aquí, para y mándame la salida: significa que tu SDK resuelve paquetes
distintos a los del CI.

## 1-bis. El atajo, una vez has compilado la primera vez

```powershell
.\scripts\Start-Dev.ps1
```

Para el agente si estaba corriendo, compila, lo relanza en segundo plano y abre la
consola. **El agente en marcha bloquea sus propias DLL**, así que un `dotnet build` con él
vivo falla con MSB3027; el script hace el orden correcto para que no haya que recordarlo.

`.\scripts\Stop-Dev.ps1` lo para todo. `-SinConsola` arranca solo el agente, para
trastear con la API desde PowerShell.

Lo que sigue es el detalle paso a paso, útil la primera vez y cuando algo falla.

## 1-ter. Desde Visual Studio Code

```powershell
code C:\Users\andre\AV-Dashboard
```

Al abrir la carpeta, VS Code ofrece instalar las dos extensiones que hacen falta y que
están declaradas en `.vscode/extensions.json`: **C# Dev Kit**, sin la cual no hay
IntelliSense ni depurador, y **PowerShell** para los guiones.

En el panel de ejecución (`Ctrl+Shift+D`) hay cuatro configuraciones y una combinada:

| | Qué hace |
|---|---|
| **Agente + consola** | Lo normal. Arranca los dos con puntos de ruptura en ambos |
| Agente | Solo el agente, en el terminal integrado, para ver su registro |
| Consola (WPF) | Solo la interfaz, contra un agente que ya esté en marcha |
| Interfaz WinUI 3 | La nueva, a medio portar |
| CLI | `avdash audit --todo`, para trastear con los argumentos |

`F5` con «Agente + consola» para el agente si estaba corriendo antes de compilar. No es
un detalle: el agente en marcha mantiene tomadas sus propias DLL y `dotnet build` falla
con MSB3027, que es la fricción que se paga en cada iteración si no está resuelta.

VS Code no tiene diseñador de XAML. Para WPF no importa mucho —aquí el XAML se escribe a
mano— pero conviene saberlo.

## 2. Arrancar el agente

En un terminal, **sin elevar**:

```powershell
dotnet run --project src/AvDashboard.Agent -- --portable
```

Deberías ver algo así:

```
info: AvDashboard.Agent[0]
      AV Dashboard agente 0.1.0 — nodo <guid> (TU-PC) — modo portátil — datos en ...\data
info: AvDashboard.Agent[0]
      Token del agente en ...\data\agent.token y C:\Users\tú\AppData\Local\AvDashboard\agent.token
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://127.0.0.1:47821
```

Déjalo corriendo.

## 3. Comprobar el agente sin la consola

Esto separa dos problemas que si no se mezclan. En **otro** terminal:

```powershell
# Salud: es la única ruta sin token
Invoke-RestMethod http://127.0.0.1:47821/health

# Telemetría de sistema (M1)
$t = Get-Content "$env:LOCALAPPDATA\AvDashboard\agent.token"
$h = @{ Authorization = "Bearer $t" }
Invoke-RestMethod http://127.0.0.1:47821/api/v1/system -Headers $h | ConvertTo-Json -Depth 6

# Audio (M3): aquí es donde se ve si Core Audio responde
Invoke-RestMethod http://127.0.0.1:47821/api/v1/audio/devices -Headers $h | ConvertTo-Json -Depth 6

# Red (M5) y servicios (M6)
Invoke-RestMethod http://127.0.0.1:47821/api/v1/network/interfaces -Headers $h | ConvertTo-Json -Depth 5
Invoke-RestMethod http://127.0.0.1:47821/api/v1/services -Headers $h | ConvertTo-Json -Depth 4

# Qué módulos se ha auto-desactivado y por qué
Invoke-RestMethod http://127.0.0.1:47821/api/v1/modules -Headers $h | ConvertTo-Json -Depth 3
```

**Lo que más me interesa de esta tanda es `/api/v1/audio/devices`.** Es la parte con más
COM e interop crudo: enumeración de endpoints, formato de mezcla vía `IAudioClient`, y el
formato declarado leído de un `PROPVARIANT` con layout explícito para x64. Si algo va a
fallar por una firma mal escrita, falla ahí.

Concretamente, mira si:

- Aparecen todos tus dispositivos, incluidos los desconectados.
- `mixSampleRate` trae un número real y no cero.
- `defaultSampleRate` trae algo. Si viene `null` en todos, el `PROPVARIANT` está mal leído.
- El que tienes por defecto en Windows sale con `isDefault: true`.

## 4. Arrancar la consola

```powershell
dotnet run --project src/AvDashboard.Console
```

Debería abrirse una ventana oscura con el rail a la izquierda, la barra superior con el
nodo y el semáforo, y el dashboard con cinco tarjetas.

Cosas que sé que pueden salir mal y no son graves:

- **Iconos del rail como cuadraditos.** Los glifos de Segoe Fluent Icons los elegí sin
  poder verlos. Dime cuáles salen mal y los cambio.
- **Sin gráfico de latencia.** Normal: no hay dispositivos críticos configurados todavía.
  Ver el paso 5.
- **Semáforo en gris unos segundos.** El primer ciclo del colector tarda un segundo.

## 5. Ping continuo, que es el módulo que más veces salva un evento

Edita `src/AvDashboard.Agent/appsettings.json` y añade lo que quieras vigilar:

```json
"CriticalDevices": [
  { "Address": "192.168.1.1", "Alias": "Router" },
  { "Address": "8.8.8.8",     "Alias": "Internet" }
]
```

Reinicia el agente. En el dashboard debería aparecer una fila por dispositivo con su
gráfico. Déjalo un par de minutos y desenchufa algo: la banda roja de pérdida y la entrada
en el Timeline son la prueba de que la cadena entera funciona.

## 6. El CLI

```powershell
dotnet run --project src/AvDashboard.Cli -- netinfo
dotnet run --project src/AvDashboard.Cli -- audio
dotnet run --project src/AvDashboard.Cli -- services
dotnet run --project src/AvDashboard.Cli -- preshow
dotnet run --project src/AvDashboard.Cli -- timeline 15
```

`preshow` es el que da veredicto verde o rojo con la lista de lo que falla.

## 7. El informe de diagnóstico

Antes que ninguna captura, esto:

```powershell
.\scripts\Get-Diagnostico.ps1
```

Con el agente en marcha, pregunta por la auditoría, las pantallas y el audio, y escribe un
informe de texto en el escritorio, además de copiarlo al portapapeles. No cambia nada: solo
lee.

Merece la pena ejecutarlo **dos veces, normal y como administrador**. La diferencia entre
las dos en el bloque «sin lectura» es exactamente el dato que dice si la sonda necesita
correr en modo servicio o le basta con la sesión del usuario, y no hay forma de saberlo sin
probarlo en una máquina de verdad.

## 8. Qué mandarme si algo va mal

Con esto puedo arreglar casi cualquier cosa sin verte la pantalla:

1. **La salida completa del terminal del agente**, desde el arranque. Ahí van los avisos de
   los colectores que fallan, que están limitados a cinco repeticiones para no llenar el log.
2. **La respuesta de la ruta que falle**, en JSON.
3. **Si la consola casca**, el diálogo que sale: está preparada para no cerrarse ante una
   excepción no controlada y enseñar el mensaje.
4. **Una captura del dashboard** si lo que falla es visual. Para los iconos y los anchos de
   columna no hay otra.
5. **El informe de `Get-Diagnostico.ps1`**, que ahorra los tres primeros puntos en la
   mayoría de los casos.

## Resultado del primer arranque real (22/08/2026, PC_ANDRES)

Verificado en una máquina con Focusrite, NVIDIA, AMD, VB-Cable y NDI Tools:

| Qué | Resultado |
|---|---|
| Enumeración de audio | 38 endpoints, render y captura, incluidos los no presentes |
| `PROPVARIANT` del formato declarado | Correcto: 48 kHz/16, /24 y /32 según el endpoint |
| Formato de mezcla vía `IAudioClient` | Correcto: 48 kHz/32/2 en los activos |
| Detección de mismatch de sample rate | **Encontró uno real**: los NDI Webcam Audio declaran 44,1 kHz con el motor a 48 |
| Registro ASIO | 5 drivers con ruta de DLL |
| Interfaces de red | MAC, velocidad de enlace, DHCP, gateways, DNS, throughput |
| Servicios | 308 clasificados por sector |
| Telemetría de CPU por core | 24 cores, valores plausibles |
| Colectores | Cero avisos: ninguno falla en silencio |

### La consola, misma máquina

| Qué | Resultado |
|---|---|
| Arranque de la ventana y navegación por el rail | Correcto |
| Iconos de Segoe Fluent Icons | Se ven bien, ninguno sale como caja |
| Semáforo global y contador de alertas | Ámbar y «1 alertas» por el mismatch de las webcams |
| Recorrido completo de una alerta | Core Audio → colector → alerta → API → dashboard, con su hora y su módulo |
| Página de Sistema | Valor actual contra valor esperado, con punto de color por fila |
| Medidores de audio a 20 fps | **Se mueven.** Valida el hub, la suscripción por canal y el `LevelMeter` |
| Secciones de fases futuras | Se presentan con su etiqueta de fase, no vacías |

Con eso, la Fase 1 queda verificada de punta a punta en hardware real: interop,
agente, API, SQLite, WebSocket y UI.

### Huecos conocidos que salieron de ahí

- **`measuredRefreshHz` viene `null`.** `DwmGetCompositionTimingInfo` no devuelve nada útil
  desde el agente. No se investiga ahora: la medida por salida necesita NVAPI de todas
  formas y es Fase 4.
- **`monitorName` dice «Generic PnP Monitor».** Es lo que da `EnumDisplayDevices`; el
  nombre real está en el EDID, que es M18.
- **Sin elevación, Defender no es legible.** Correcto y esperado; ahora se reporta como
  desconocido en lugar de como «no hace falta intervención».

## Lo que todavía no va a funcionar, y es esperado

- Todo lo que escriba en el sistema: responde `501` con la fase en la que llega.
- PTP: necesita Npcap.
- DeckLink, NDI, procesadores LED, UPS, licencias: Fases 4, 5 y 7.
- Temperaturas y throttling térmico: requieren LibreHardwareMonitorLib, que carga un driver
  en kernel y no entra por defecto. Ver [dependencias](dependencies.md).

Las secciones de la consola que corresponden a esos módulos aparecen con su nombre y la
fase en la que llegan, no vacías y sin explicación.
