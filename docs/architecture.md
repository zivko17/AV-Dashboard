# Arquitectura

Documento de referencia. Recoge la especificación acordada y marca con ⚠ lo que se ha
corregido o acotado respecto a ella al implementarlo.

---

## 0. Decisión de UI: cerrada — nativa

**Aplicación nativa de Windows, WPF.** Descartado Vite + React + WebView2.

1. Los medidores de audio a 20 fps, los grids de 250 dispositivos y los gráficos de ping
   continuo son exactamente donde una UI web en WebView2 empieza a comer CPU en la
   máquina que estás intentando optimizar. Contradice el requisito de < 1 % en reposo.
2. WebView2 es Chromium embebido: ~150 MB de RAM adicionales y un proceso más que puede
   colgarse.
3. Un panel de control del sistema debe sentirse como parte del sistema.

La API REST + WebSocket del agente se mantiene igualmente: es lo que da el multi-nodo, el
CLI y el puente con Companion. Como efecto colateral, un frontend web para el iPad de FOH
se añadiría sin tocar el backend, pero no es objetivo y no condiciona ninguna decisión.

**Consecuencias asumidas:** sin acceso desde móvil o tablet, sin acceso desde una máquina
sin AV Dashboard instalado.

⚠ **Corrección al implementar:** WPF-UI (la capa Fluent) y ScottPlot no entran todavía.
Ver [ADR 0005](adr/0005-ui-sin-dependencias-en-fase-1.md): el tema oscuro, el rail y los
gráficos son propios para que la Fase 1 compile y arranque sin dependencias de terceros,
y hay un punto de enganche preparado en `App.xaml`.

---

## 1. Requisitos

### Funcionales

| ID | Requisito |
|----|-----------|
| F-01 | Estado y control del sistema Windows (energía, rendimiento, updates, efectos visuales) |
| F-02 | Firewall por perfil y reglas AV |
| F-03 | Audio: dispositivos, ruteo por app, sample rate, exclusivo, ASIO, medidores, PTP |
| F-04 | Vídeo: displays, EDID, GPU, DeckLink, captura, NDI, procesadores LED |
| F-05 | Red: interfaces, perfiles IP, descubrimiento, latencia, ancho de banda, protocolos AV |
| F-06 | Servicios de Windows con vista curada AV y diff contra baseline |
| F-07 | Inventario de máquinas remotas y lanzamiento de sesión |
| F-08 | Perfiles de escenario reversibles |
| F-09 | Monitorización de aplicaciones de producción |
| F-10 | Histórico, alertas, timeline correlado e informes |
| F-11 | Validación de contenido multimedia antes del show |
| F-12 | Acciones de pánico por hotkey global |
| F-13 | Integración bidireccional con Bitfocus Companion |
| F-14 | Inventario de flota y detección de divergencias entre máquinas |
| F-15 | Paleta de comandos con los nombres cortos de `av-light-tools` |

### No funcionales

- Telemetría ligera 1 s; medidores audio 20 fps; escaneos pesados bajo demanda.
- **Agente < 1 % CPU y < 80 MB RAM en reposo.** Innegociable: corre en máquinas que están
  produciendo.
- Agente operativo antes del login de usuario.
- Ningún cambio sin snapshot previo persistido.
- Escala: 1 consola, ~10 nodos, ~250 dispositivos por segmento.
- **Funcionamiento completo sin internet.** Todo local o Tailscale.

Cómo se sostiene el presupuesto de CPU, en concreto:

- Telemetría de CPU por `GetSystemTimes` y `NtQuerySystemInformation`, no por
  `PerformanceCounter`. Ver `SystemTelemetryProvider`.
- Los medidores de audio solo se leen si hay una consola suscrita al canal
  (`LiveTelemetryHub.HasSubscribers`). Una consola mirando la pestaña de Red no cuesta nada.
- La enumeración completa de audio activa un `IAudioClient` por dispositivo: va cada 3 s,
  no a 20 fps.
- El ping escribe a disco por lotes de 30 muestras, no una transacción por muestra.
- WMI se consulta una vez para todos los servicios, no una por servicio.

### Restricciones

- Desarrollador único, tiempo parcial.
- C# no es el stack habitual. Contemplado en el plan de fases.
- Máquinas objetivo en producción: un bug no puede tumbar un evento.

---

## 2. Stack

**Runtime: .NET 8.** El 70 % del software es P/Invoke a Win32 (Core Audio,
`INetFwPolicy2`, `QueryDisplayConfig`, WMI, NVAPI, DeckLink SDK, ETW). En Node cada una es
un módulo nativo mal mantenido o un `child_process` a PowerShell. En C# son llamadas
directas.

**UI: WPF, MVVM con CommunityToolkit.Mvvm.** Descartado WinUI 3: los data grids densos y
los gráficos en tiempo real todavía pelean.

---

## 3. Topología

```
┌──────────────────────────────────────────────────────────────┐
│  AvDashboard.Console  (WPF, sesión usuario, sin admin)       │
│  NodeManager: Localhost │ Rig1 │ clap-house │ aguas │ ...    │
│  Paleta Ctrl+K · CompanionBridge · TimelineViewer            │
└────┬──────────────────────────────────┬──────────────────────┘
     │ HTTPS + WS 127.0.0.1:47821       │ HTTPS + WS vía Tailscale
┌────▼─────────────────┐   ┌────────────▼─────────────────────┐
│ AvDashboard.Agent    │   │ AvDashboard.Agent (idéntico)     │
│ Windows Service      │   │ clap-house / aguas / rig-portátil│
│ LocalSystem          │   │                                  │
│  Collectors          │   │  mismos módulos, misma API       │
│  Actuators           │   │                                  │
│  Sniffers (Npcap)    │   │                                  │
│  Watchdogs           │   │                                  │
│  SQLite              │   │                                  │
└──────────────────────┘   └──────────────────────────────────┘
```

---

## 4. Módulos

### M1 — System & Performance

Plan de energía incluido Ultimate Performance, estado mín/máx del procesador, core
parking, `VisualFXSetting`, **Fast Startup** (`HiberbootEnabled`, crítico para la
recuperación tras un corte de luz), USB selective suspend, PCIe ASPM, **power management
de NIC** (`PnPCapabilities`, tumba Dante y NDI en silencio), **MMCSS**
(`SystemResponsiveness`, `NetworkThrottlingIndex` — el ajuste de mayor impacto real en
NDI y DVS), Nagle por interfaz, Windows Update, Focus Assist, Game Bar, HAGS, prioridad y
afinidad por proceso, arranque y tareas programadas, telemetría de CPU/RAM/temperaturas y
**eventos de throttling térmico y de power limit** — la razón real de que un rig en flight
case caliente empiece a soltar frames.

⚠ **Defender no es desactivable programáticamente** con Tamper Protection activo. El
módulo lee el estado y avisa; el toggle requiere intervención manual una vez.

⚠ **Temperaturas y contadores de throttling requieren LibreHardwareMonitorLib**, que carga
un driver en kernel. Es una decisión con consecuencias en máquinas de cliente: no entra
por defecto, se activa explícitamente. Ver [dependencias](dependencies.md).

### M2 — Firewall

Estado por perfil vía `INetFwPolicy2`, CRUD de reglas y **preset «AV Ports»**
(`AvDashboard.Core/Taxonomy/AvPorts.cs`): vMix 8088, OBS 4455, Resolume 8080/7000,
Companion 8000, NDI 5353 + 5960-5990, Dante 4440-4455 + 8700-8708, Watchout 3040,
Art-Net 6454, sACN 5568, Tailscale. Siempre preferible a desactivar el firewall entero.

### M3 — Audio & Clock

Enumeración render/capture con formato en uso y declarado, **detección de mismatch de
sample rate** (causa nº 1 de crujidos), volumen y ruteo por app, dispositivo por defecto,
medidores en tiempo real, **drivers ASIO** (`HKLM\SOFTWARE\ASIO`) y watchdog de
dispositivo desaparecido.

**PTP / IEEE 1588:** sniffer de PTPv1 (Dante nativo, UDP 319/320) y PTPv2 (AES67),
Grandmaster del segmento, offset, deriva, pérdidas de sync y **alerta de cambio de
Grandmaster** — normalmente significa que alguien ha enchufado algo.

⚠ Requiere **Npcap** en modo compatible con WinPcap. Es una dependencia externa: detección
en runtime y degradación elegante si falta. No se puede leer PTP con sockets normales.

**DVS:** estado de bloqueo de Dante Virtual Soundcard, sample rate, latencia.
⚠ DVS no expone API pública documentada. Vía realista: estado del servicio + su log +
presencia en la red Dante vía mDNS. Lo que no se pueda leer se marca como no legible
(`DvsStatus.UnreadableFields`), no se inventa.

⚠ `IPolicyConfig` y `IAudioPolicyConfig` son APIs no documentadas. Aisladas en
`Native/Audio/Undocumented` con detección de build y degradación. Las reglas de esa
carpeta están en su propio README y no admiten excepciones.

### M4 — Video

Topología vía `QueryDisplayConfig` + NVAPI, **guardar y restaurar layout completo**,
**Window Coordinate Keeper** (memoriza posición y tamaño de las ventanas de salida y las
fuerza de vuelta con `SetWindowPos`), **monitorización de HPD**, **forzado de EDID por
software**, alerta de display desaparecido, y GPU con NVENC/NVDEC por separado y **rama
del driver (Studio vs Game Ready)**.

⚠ El forzado de EDID es **NVAPI** (`NvAPI_GPU_SetEDID`), no `nvidia-smi`: `nvidia-smi` no
gestiona EDID de salidas de display. Y puede requerir GPU profesional en algunos
escenarios — verificar contra el hardware real antes de comprometerlo.

**Captura:** DirectShow y Media Foundation con el proceso que los ocupa; **DeckLink** vía
Desktop Video SDK con señal, formato y **genlock enganchado**; fuentes NDI vía NDI SDK Find.

**Sincronía de refresh:** refresh real vía `DwmGetCompositionTimingInfo`, comparado con el
framerate de render que reporta vMix/Resolume/Watchout, con la desviación en ms.

⚠ Esto **no es un detector de tearing**. El tearing es un artefacto visual que solo se ve
en pantalla. Lo que sí se puede medir con fiabilidad es el mismatch de frecuencia y la
deriva acumulada, que es la causa. Se llama por su nombre (`RefreshSyncStatus`).
⚠ Además, `DwmGetCompositionTimingInfo` da el ritmo del compositor, no el de cada salida
por separado; la medida por salida necesita NVAPI.

**Procesadores LED (M4-LED):** alcanzabilidad, latencia y estado de puerto para VX600 Pro,
KU20 y serie Coex.

⚠ **Aviso serio.** Novastar no publica API abierta documentada para VX600 Pro. Plan por
capas, modelado en `LedProbeLayer`:

1. **Garantizada:** ping, TCP, sondeo de puerto web, detección de reinicio por cambio de
   uptime. Funciona hoy y ya avisa de un procesador caído.
2. **Scraping:** leer los valores del HTML o JSON de la interfaz web, donde exista.
3. **Ingeniería inversa:** capturar el tráfico entre NovaLCT y el procesador.

Las capas 2 y 3 se implementan **si y cuando** haya hardware delante para capturar. No
están en el plan como si estuvieran resueltas.

### M5 — Network & AV Protocols

Interfaces con velocidad de enlace y MTU, **perfiles de IP estática conmutables**,
descubrimiento por capas (ARP + ICMP + mDNS + SSDP + NetBIOS), fabricante por OUI sin
internet, sondeo de puertos web, **alias propios persistentes**, **ping continuo con
latencia, jitter y pérdida** (el módulo que más veces salva un evento), `iperf3` para LAN
y **detección de link flap**.

**Protocolos AV:** conmutador de NDI Discovery Server, sniffers de Art-Net (UDP 6454) y
sACN (UDP 5568), detección de tormentas broadcast/multicast y **detección de IGMP querier**
en el segmento.

⚠ Art-Net, sACN y los contadores de multicast se hacen con sockets normales unidos al
grupo. Solo PTP y el análisis fino de multicast necesitan Npcap.
⚠ La ruta de `ndi-config.v1.json` cambió: NDI 5/6 usa `%ProgramData%\NDI\` para máquina y
`%APPDATA%\NDI\` para usuario; `%LocalAppData%\NewTek` es la ruta antigua. El módulo debe
buscar en las tres.

### M6 — Services

Listado completo con estado, tipo de arranque, cuenta, dependencias y PID; **vista «AV
Relevant»** curada; alerta de servicio AV esperado que está detenido; y **diff contra
baseline** — la pregunta que te haces a las 19:40 con el cliente detrás.

### M7 — Remote Nodes

Registro de nodos por mDNS y Tailscale, estado por nodo, **vista consolidada** de una fila
por máquina, inventario de máquinas *sin* agente (RDP, VNC, SSH, web) con lanzador, y
estado de Tailscale incluida la **expiración de key**.

### M8 — Production Apps

| App | Endpoint | Datos |
|-----|----------|-------|
| vMix | HTTP 8088, XML | inputs, tally, recording, streaming, render time, dropped frames |
| OBS | obs-websocket v5, 4455 | escenas, stream/record, fps, skipped frames |
| Resolume Arena 7 | REST 8080 + WS | composición, capas, clips activos |
| Watchout | TCP 3040 | estado de show, timeline |
| Companion | HTTP 8000 | estado de conexiones |

⚠ La API de Watchout cambió sustancialmente entre v6 y v7. Verificar contra la versión
instalada.

Este módulo también alimenta el guardarraíl de show activo (`IShowGuard`).

### M9 — Storage

Espacio libre, velocidad de escritura medida bajo demanda, SMART, alerta por umbral.

### M10 — Scenario Profiles

El módulo que hace que uses esto de verdad. El resto es consulta; esto es acción.

```
Profile "Evento — Rig Portátil"
  ├─ Power plan            → Ultimate Performance
  ├─ Visual effects        → Best performance
  ├─ Fast startup          → Off
  ├─ NIC power mgmt        → Off (todas)
  ├─ MMCSS                 → SystemResponsiveness=10, NetworkThrottling=0xFFFFFFFF
  ├─ Windows Update        → Paused 7d
  ├─ Firewall              → Enabled + AV Ports preset
  ├─ Network profile       → "Dante 172.31"
  ├─ NDI discovery         → Discovery Server 10.0.0.5
  ├─ Audio default         → Dante Virtual Soundcard
  ├─ Services              → start [DVS, NDI Discovery, Tailscale]
  ├─ Process priority      → vMix64.exe = High
  ├─ Display layout        → restore "LED Wall 3x2"
  └─ Window positions      → restore "Resolume Output"
```

Ejecución transaccional con snapshot previo. Si un paso falla se registra y se hace
rollback. Perfil inverso «Oficina» generado desde el snapshot.

**Guardarraíl:** con show activo (vMix grabando o emitiendo, Watchout en play), aplicar un
perfil exige confirmación explícita adicional — `ShowInProgressException`.

El motor está implementado y probado en `Core/Profiles/ProfileEngine.cs`; faltan los
actuadores, que son la Fase 2.

### M11 — Timeline, Alerts & Reports

**Show Timeline:** log unificado y correlado por timestamp de todos los módulos en una
sola vista. Pérdida de display a las 21:04:12, pico de ping a las 21:04:11, servicio caído
a las 21:04:09. Reconstruyes qué pasó en lugar de discutirlo.

**Alertas** por umbral, ausencia y cambio de estado, con deduplicación: un display que
parpadea genera una alerta, no cuarenta. Destinos: toast, log, webhook, Telegram.

**Informe pre-show** exportable y **checklist pre-show automatizada** con veredicto verde
o rojo y lista de fallos. No un dashboard que haya que interpretar: un semáforo.

### M12 — Media Toolkit

Validación con `ffprobe` (codec, resolución, fps, **detección de VFR** —la causa
silenciosa de desincronía en Resolume y vMix—, sample rate, flags de incompatibilidad) y
conversión con presets HAP/HAP Q, ProRes, NVENC, audio, **normalización EBU R128 a −23
LUFS en dos pasadas** y trim. Lo que añade la app: cola de trabajos, conversión por lotes
y **conversión sugerida automáticamente desde el informe de validación**.

La lógica de validación ya está en `Core/Diagnostics/MediaValidator.cs`, probada; falta el
proceso `ffprobe` que la alimenta.

### M13 — Licenses & Dongles

vMix, dongle de Watchout, licencia de Resolume, NDI Tools, DVS. Un dongle que se suelta a
mitad de show es un fallo evitable y detectable con antelación.

### M14 — Power & UPS

Estado de UPS por HID Power Device o NUT. «Estamos en batería» debe ser una alerta, no un
descubrimiento.

### M15 — Panic Actions & Hotkeys

Restaurar layout de pantallas, restaurar posiciones de ventanas, reiniciar el motor de
audio, matar y relanzar una aplicación, aplicar perfil de emergencia. Con hotkey global.
Bajo presión no navegas una UI: pulsas.

### M16 — Companion Bridge

**Salida:** el agente expone su estado como variables y feedbacks; un botón del Stream
Deck se pone rojo cuando desaparece un display o el UPS pasa a batería. **Entrada:**
endpoints HTTP para disparar perfiles y acciones de pánico desde un botón físico.

### M17 — Fleet Inventory & Drift

Manifiesto por nodo (hardware, número de serie, drivers, versiones, parches) y comparación
entre nodos: cuando el rig A funciona y el B no, la respuesta suele estar en un diff de
versiones. Exportable como documentación de entrega.

### M18 — EDID Toolkit

`edid-list`, `edid-info`, `edid-check`, **`edid-diff`** (la función más útil del conjunto:
por qué el procesador ve 1920×1080@60 y el splitter reporta otra cosa) y `edid-dump`.

Lo que añade AV Dashboard: biblioteca persistente de EDID con nombre, **diff contra el
EDID guardado con alerta automática** —la detección temprana de «alguien cambió el
splitter»— y enlace con el forzado por NVAPI de M4.

---

## 5. Seguridad

1. **Bind exclusivo a `127.0.0.1` y a la IP de Tailscale.** Nunca `0.0.0.0`. Implementado
   en `ConfigureKestrel`.
2. **Token bearer** en `%ProgramData%\AvDashboard\agent.token`, ACL Administradores +
   SYSTEM, rotación al reiniciar el servicio. Comparación en tiempo constante.
3. **Certificado autofirmado por nodo**, pinning al emparejar. *(Fase 3.)*
4. **Emparejamiento explícito** con confirmación manual de huella. *(Fase 3.)*
5. **Lista blanca de acciones.** El agente no ejecuta comandos arbitrarios; solo actuadores
   compilados. Nada de endpoint genérico de PowerShell. Es la diferencia entre una
   herramienta y una puerta trasera en tres máquinas de cliente.
6. **Log de auditoría** inmutable de toda escritura.
7. **Credenciales de acceso remoto** en Windows Credential Manager (DPAPI), nunca en SQLite.

---

## 6. Riesgos

| Riesgo | Impacto | Mitigación |
|--------|---------|------------|
| Alcance infinito: 18 módulos es mucho para un desarrollador parcial | **Crítico** | La Fase 1 debe ser útil por sí sola. Si el proyecto muere ahí, sigue valiendo la pena. Este es el riesgo real, por encima de cualquier detalle técnico |
| APIs de audio no documentadas se rompen en una build | Alto | Aislamiento en `Audio/Undocumented`, detección de versión, degradar sin crashear |
| Npcap como dependencia externa | Medio | Módulos PTP/multicast opcionales, detección en runtime |
| Novastar sin API documentada | Medio | Diseño por capas; la capa 1 funciona siempre |
| Agente LocalSystem comprometido | Crítico | §5, especialmente la lista blanca |
| Perfil deja la máquina inutilizable en un show | Crítico | Snapshot + rollback + `dryRun` + guardarraíl de show activo, todo probado |
| El consumo del agente afecta al rendimiento que optimiza | Medio | Presupuesto de 1 % medido; sniffers solo bajo demanda; canales de telemetría con suscripción |
| `NvAPI_GPU_SetEDID` limitado por modelo de GPU | Bajo | Verificar contra hardware real antes de comprometer la función |

---

## 7. Qué revisar al crecer

- Más de ~10 nodos: la consola necesita un agregador en vez de N conexiones directas.
- Acceso desde móvil en FOH: frontend web sobre la API existente, sin cambios de backend.
- El motor de alertas es deliberadamente simple. Si necesita correlación entre nodos, es un
  componente propio.
- Si M4-LED llega a la capa 3, merece ser una librería separada y reutilizable.
