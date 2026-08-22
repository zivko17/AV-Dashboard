# AV Dashboard

Consola nativa de Windows y agente para diagnosticar y controlar rigs audiovisuales en
directo: sistema, audio, vídeo, red, servicios y perfiles de escenario.

Nace de [`av-light-tools`](docs/heredado.md) y le añade lo que a un script de PowerShell
le falta: estado persistente, alertas correladas y multi-nodo.

> **Estado: Fase 1 (solo lectura).** El agente lee y no escribe nada en el sistema. Es
> deliberado — ver [el roadmap](docs/roadmap.md). La escritura llega en la Fase 2 con
> snapshot previo, `dryRun` y rollback.

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
dotnet build

# El agente, en primer plano y sin instalar nada
dotnet run --project src/AvDashboard.Agent -- --portable

# La consola, en otro terminal
dotnet run --project src/AvDashboard.Console
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
| M1 Sistema | Telemetría a 1 s, plan de energía, MMCSS, Fast Startup, HAGS, Windows Update, Defender (lectura) |
| M3 Audio | Endpoints, formato en uso vs. declarado, mismatch de sample rate, medidores a 20 fps, drivers ASIO, watchdog de dispositivo desaparecido |
| M4 Vídeo | Enumeración de displays y alerta de display desaparecido. El resto, Fase 4 |
| M5 Red | Interfaces, power management de NIC, Nagle, throughput, link flap, escaneo del segmento, ping continuo con jitter y pérdida |
| M6 Servicios | Inventario agrupado por sector AV, servicios esperados, diff contra baseline |
| M9 Almacenamiento | Espacio libre y medida de escritura bajo demanda |
| M11 Timeline | Log correlado de todos los módulos, alertas con ciclo de vida, checklist pre-show |
| M17 Flota | Manifiesto de nodo: hardware, número de serie, drivers y versiones |

Todo lo demás está declarado, responde `501` con la fase en la que llega, y aparece en la
consola como una sección que dice qué hará. Ver [`docs/roadmap.md`](docs/roadmap.md).

## Documentación

- [Arquitectura](docs/architecture.md) — decisiones, topología, módulos, seguridad
- [Roadmap por fases](docs/roadmap.md)
- [API del agente](docs/api.md)
- [Modelo de datos](docs/data-model.md)
- [Dependencias externas](docs/dependencies.md) — Npcap, SDKs, lo que no se redistribuye
- [Herencia de av-light-tools](docs/heredado.md)
- [Decisiones de arquitectura (ADR)](docs/adr/)

## Licencia

MIT. Ver [LICENSE](LICENSE).
