# AV Dashboard

Consola nativa de Windows y agente para diagnosticar y controlar rigs audiovisuales en
directo: sistema, audio, vídeo, red, servicios y perfiles de escenario.

Nace de [`av-light-tools`](docs/heredado.md) y le añade lo que a un script de PowerShell
le falta: estado persistente, alertas correladas y multi-nodo.

> **Estado: el agente ya escribe, con lista blanca.** Treinta y cuatro ajustes del catálogo
> tienen actuador, todos con `dryRun` y todos dejando rastro en el Timeline con su valor
> anterior. Lo que **no** hay todavía es deshacer: `POST /runs/{id}/rollback` sigue
> devolviendo 501, así que un cambio se revierte a mano. Aplicar un perfil entero tampoco
> está. Ver [el roadmap](docs/roadmap.md).
>
> La regla que no se rompe sigue intacta: el agente nunca ejecuta un comando arbitrario, y
> el `switch` de `SettingsWriter.Apply` **es** la lista blanca. Ver
> [ADR 0003](docs/adr/0003-lista-blanca-de-actuadores.md).

---

## Qué resuelve

Las preguntas que te haces a las 19:40 con el cliente detrás:

- ¿Por qué cruje el audio? → mismatch de sample rate entre el formato declarado del
  endpoint y el que está usando el motor de Windows. No aparece en ninguna pantalla de
  Windows; aquí es una fila en ámbar.
- ¿Por qué se cae Dante? → power management del adaptador de red, `NetworkThrottlingIndex`,
  o un enlace que ha hecho flap y nadie ha visto.
- ¿Qué ha cambiado desde la última vez que esto funcionaba? → diff de servicios contra el
  baseline.
- ¿Qué pasó exactamente a las 21:04? → Show Timeline: todos los módulos correlados por
  timestamp en una sola vista.
- ¿Está todo listo para empezar? → checklist pre-show con veredicto verde o rojo y la
  lista de lo que falla. No un dashboard que haya que interpretar.

## Cómo se compone

```
AvDashboard.Console  (WPF, sesión de usuario, sin admin)
        │  HTTPS + WebSocket  127.0.0.1:47821 · Tailscale
AvDashboard.Agent    (servicio Windows LocalSystem, o modo portátil desde un USB)
        │  Collectors · Actuators · Watchdogs · SQLite
AvDashboard.Cli      (avdash — los mismos nombres cortos de av-light-tools)
```

El agente es **el mismo binario en todas las máquinas**. Sin variantes: los módulos se
auto-desactivan si falta el hardware o el SDK, y la consola pinta en gris lo que no
aplica en lugar de enseñar un cero que parece un dato.

| Proyecto | Qué es | TFM |
|---|---|---|
| `AvDashboard.Core` | Modelos, taxonomía, motor de perfiles y toda la lógica probable. Sin dependencias. | `net8.0` |
| `AvDashboard.Native` | P/Invoke y COM: Core Audio, PowrProf, registro, WMI, IpHelper, GDI. | `net8.0-windows` |
| `AvDashboard.Agent` | Servicio, API REST, hub de SignalR, SQLite. | `net8.0-windows` |
| `AvDashboard.Console` | La consola WPF: rail, dashboard, paleta `Ctrl+K`. | `net8.0-windows` |
| `AvDashboard.Cli` | `avdash`, para scripts y para Companion. | `net8.0` |

## Arrancar

Requiere el **SDK de .NET 8** y Windows 11 x64 (funciona en Windows 10 1809+).

```powershell
git clone <url> av-dashboard
cd av-dashboard

# Compila, arranca el agente en segundo plano y abre la consola
.\scripts\Start-Dev.ps1
```

El script para el agente antes de compilar —en marcha bloquea sus propias DLL y `dotnet
build` falla con MSB3027—, espera a que responda `/health` y solo entonces abre la ventana.
`-SinConsola` deja solo el agente; `-SinCompilar` relanza sin volver a compilar; y
`Stop-Dev.ps1` para las dos cosas.

A mano, si se prefiere:

```powershell
dotnet build
dotnet run --project src/AvDashboard.Agent -- --portable   # un terminal
dotnet run --project src/AvDashboard.Console               # otro
```

Para ver qué está leyendo el agente en esta máquina, con el informe listo para pegar:

```powershell
.\scripts\Get-Diagnostico.ps1
```

Para instalarlo como servicio (arranca antes del login), ver
[`scripts/Install-Agent.ps1`](scripts/Install-Agent.ps1).

Para configurar el ping continuo a tus dispositivos críticos, edita
`src/AvDashboard.Agent/appsettings.json`:

```json
"CriticalDevices": [
  { "Address": "192.168.0.47", "Alias": "Novastar VX600 rack B" },
  { "Address": "172.31.1.20",  "Alias": "Xilica FR1" }
]
```

## Qué funciona hoy

| Módulo | Estado |
|---|---|
| M1 Sistema | Telemetría a 1 s y auditoría de 42 ajustes contra el objetivo de un rig, con impacto y motivo: energía, MMCSS, Delivery Optimization, Game DVR, acceso controlado a carpetas, apagados inesperados… |
| M3 Audio | Endpoints, formato en uso vs. declarado, mismatch de sample rate, medidores a 20 fps, drivers ASIO, watchdog de dispositivo desaparecido |
| M4 Vídeo | Topología con conector físico, refresco como fracción exacta (59,94 no se redondea a 60), HDR y alerta de display desaparecido. DeckLink y NVAPI, pendientes |
| M18 EDID | Lectura del EDID crudo, decodificación completa, validación de coherencia y diff entre dos monitores. Volcado a `.bin` para CRU o una matriz. Referencia con nombre por conector y alerta automática cuando el EDID de una salida deja de coincidir |
| M5 Red | Interfaces, power management de NIC, Nagle, throughput, link flap, escaneo del segmento, ping continuo con jitter y pérdida |
| M6 Servicios | Inventario agrupado por sector AV, servicios esperados, diff contra baseline |
| M9 Almacenamiento | Espacio libre y medida de escritura bajo demanda |
| M11 Timeline | Log correlado de todos los módulos, alertas con ciclo de vida, checklist pre-show |
| M17 Flota | Manifiesto de nodo: hardware, número de serie, drivers y versiones |

Todo lo demás está declarado, responde `501` con la fase en la que llega, y aparece en la
consola como una sección que dice qué hará. Ver [`docs/roadmap.md`](docs/roadmap.md).

## Documentación

- **[Contexto del proyecto](CLAUDE.md)** — la vista de conjunto en dos minutos, y lo que lee un asistente al abrir el repositorio
- **[Primer arranque](docs/primer-arranque.md)** — empieza por aquí
- [Arquitectura](docs/architecture.md) — decisiones, topología, módulos, seguridad
- [Roadmap por fases](docs/roadmap.md)
- [API del agente](docs/api.md)
- [Modelo de datos](docs/data-model.md)
- [Dependencias externas](docs/dependencies.md) — Npcap, SDKs, lo que no se redistribuye
- [Herencia de av-light-tools](docs/heredado.md)
- [Decisiones de arquitectura (ADR)](docs/adr/)

## Licencia

MIT. Ver [LICENSE](LICENSE).
