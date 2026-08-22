# Herencia de `av-light-tools`

El repo de PowerShell existente es la mejor lista de requisitos que hay: son las funciones
que ya se usan a diario en un rig de directo. AV Dashboard no lo sustituye, lo absorbe y
le añade estado persistente, alertas y multi-nodo.

## Mapeo comando → módulo

| Comando actual | Destino | Estado | Nota |
|---|---|---|---|
| `netinfo`, `myip` | M5 Interfaces | ✅ | Con velocidad de enlace, MTU y power management |
| `scan`, `whoison` | M5 Descubrimiento | ✅ | Falta capa mDNS/SSDP (Fase 5) y alias persistentes |
| `portcheck`, `portscan` | M5 Sondeo | ✅ | |
| `pingmulti`, `latency` | M5 Ping continuo | ✅ | El gráfico de barras pasa a serie temporal con histórico |
| `speed` | M5 | ✅ | Throughput en vivo por interfaz |
| `connections` | M5 | Fase 5 | Conexiones TCP por proceso |
| `speedtest` | M5 | Fase 5 | |
| `flushdns`, `reset-nic` | M15 Pánico | Fase 2 | Acciones de un golpe |
| `setip` | M5 Perfiles IP | Fase 2 | Ampliado a perfiles con nombre |
| `sysinfo` | M17 Manifiesto | ✅ | Con modelo y número de serie |
| `tele`, `top` | M1 Telemetría | ✅ | |
| `killp` | M15 Pánico | Fase 2 | Matar y relanzar |
| `usb` | M1 | Fase 5 | Dispositivos USB agrupados por tipo |
| `disks` | M9 | ✅ | |
| `monitors` | M4 Displays | ✅ | |
| `monitors-arrange` | M4 Actuador | Fase 4 | Resolución y refresh desde la app |
| `audio`, `services-audio` | M3 / M6 | ✅ | Tu lista de servicios de audio es la semilla de la taxonomía |
| `startup`, `battery` | M1 / M14 | Fase 5 | Batería de portátil junto al UPS |
| `edid-*` | M18 | Fase 4 | |
| `av`, `convert`, `probe`, `dl`, `audiotools` | M12 | Fase 5 | |
| `cleanup`, `backup`, `search` | Utilidades | Prioridad baja | |
| `weather`, `timer` | — | Fuera de alcance | Que sigan en PowerShell |

## Lo que el repo enseña sobre el diseño

**El menú es data-driven.** El array `$sections` define entradas, numeración y dispatch.
Es exactamente el concepto de `Core/Taxonomy` — validado por tu propio uso, así que se
adopta tal cual.

**Cada herramienta tiene nombre de una palabra y argumentos opcionales.** Eso es memoria
muscular que no conviene tirar. De ahí el requisito **F-15, la paleta de comandos**:
`Ctrl+K` en la consola abre un buscador con **cada acción con el mismo nombre corto que ya
usas**. Escribes, Enter, se ejecuta. El catálogo es único
(`Core/Contracts/CommandCatalog.cs`) y lo comparten la paleta y `avdash`, así que un
comando existe una sola vez.

Coste bajo, valor altísimo: es lo que hará que no vuelvas a PowerShell por costumbre.

## Modo portátil

El repo funciona desde USB, así que AV Dashboard también: `--portable` deja la base de
datos y la configuración junto al ejecutable, sin instalar servicio y con funcionalidad de
solo lectura completa.

Cubre exactamente el caso «llego a un sitio ajeno y necesito diagnosticar en cinco
minutos». El modo servicio y el portátil comparten binario y difieren solo en privilegios
y persistencia.

## Convivencia

`av-light-tools` **no se retira**. Sigue siendo lo correcto para:

- Máquinas donde no vas a instalar nada.
- Automatización de Resolume vía AutoHotkey, que AV Dashboard no cubre.
- Utilidades fuera de alcance (`weather`, `timer`, `search`).

`avdash` expone los comandos con los mismos nombres, así que puedes migrar función a
función sin romper tus hábitos ni tu `$PROFILE`.
