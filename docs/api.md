# API del agente

REST para acciones, WebSocket (SignalR) para telemetría. Todas las rutas están declaradas
en `AvDashboard.Core/Contracts/ApiRoutes.cs`: el agente las sirve y la consola, el CLI y el
puente con Companion las consumen desde la misma constante.

**Base:** `http://127.0.0.1:47821/api/v1` — y la IP de Tailscale, si la hay. Nunca `0.0.0.0`.

> El agente **escribe**. Treinta y cuatro ajustes del catálogo tienen actuador, todos
> aceptan `dryRun`, y una escritura suelta se puede **deshacer** desde su línea del
> Timeline. Lo que no hay es rollback de una ejecución de perfil entera:
> `/runs/{id}/rollback` sigue devolviendo 501.

## Autenticación

Token bearer en cada petición:

```
Authorization: Bearer <contenido de %ProgramData%\AvDashboard\agent.token>
```

El token rota en cada arranque del servicio. Un `401` después de haber funcionado
normalmente significa que el servicio se ha reiniciado; hay que releer el fichero.

`GET /health` es la única ruta sin token: devuelve estado y versión, y nada más.

SignalR no puede poner cabeceras en el handshake de WebSocket, así que acepta el token
como `?access_token=`. Es la única excepción y está documentada aquí a propósito.

## Convenciones

- Toda escritura acepta `?dryRun=true` y devuelve el diff sin aplicar nada.
- Los errores tienen forma uniforme (`ApiError`): `code`, `message`, `detail`. El agente
  nunca devuelve un stack trace.
- Un módulo no implementado responde **501** con la fase en la que llega, no 404: la
  consola necesita distinguir «no existe» de «todavía no». **Toda constante de `ApiRoutes`
  se sirve**, aunque sea para decir que todavía no está; un 404 aquí sería un fallo.
- Un módulo sin primera lectura responde **503** en vez de ceros que parecen datos.

## Rutas

### Nodo

| Método | Ruta | Estado |
|---|---|---|
| GET | `/node` | ✅ Identidad, salud y número de alertas activas |
| GET | `/modules` | ✅ Qué módulos hay y por qué los que faltan no están |
| GET | `/manifest` | ✅ Manifiesto del nodo (M17) |
| GET | `/alerts` | ✅ Alertas activas |

### Sistema

| Método | Ruta | Estado |
|---|---|---|
| GET | `/system` | ✅ Telemetría y ajustes de M1 |
| GET | `/system/audit` | ✅ Los 49 ajustes del catálogo contra su objetivo, con impacto y motivo |
| POST | `/system/{setting}?value=&dryRun=` | ✅ Escribe uno de los 34 con actuador. Lista blanca compilada: un id que no esté en `SettingsWriter.Apply` se rechaza aunque la petición venga bien formada |
| GET | `/system/processes` | 501 — Fase 2 |

### Audio

| Método | Ruta | Estado |
|---|---|---|
| GET | `/audio/devices` | ✅ Endpoints, formatos, mismatches y drivers ASIO |
| GET | `/audio/asio` | ✅ |
| GET | `/audio/ptp` | ✅ Devuelve `available: false` y el motivo si falta Npcap |
| POST | `/audio/default` | 501 — Fase 2 |
| GET | `/audio/sessions` · `/audio/session/{pid}` | 501 — Fase 2 |
| POST | `/audio/session/{pid}` | 501 — Fase 2 |

### Vídeo

| Método | Ruta | Estado |
|---|---|---|
| GET | `/video/displays` | ✅ Topología por `QueryDisplayConfig`: conector físico, refresco como fracción exacta, HDR y huella de EDID |
| GET | `/video/edid` | ✅ Biblioteca de EDID: los conectados y los que Windows recuerda, decodificados y validados |
| GET | `/video/edid/raw?id=` | ✅ Bytes crudos en base64, con nombre de fichero sugerido |
| GET | `/video/edid/diff?left=&right=` | ✅ Comparación campo a campo, marcando lo que cambia la negociación |
| GET · POST | `/video/edid/reference` | ✅ Biblioteca de referencia con nombre y salidas cuyo EDID ya no coincide |
| GET | `/video/decklink` · `/video/sync` · `/video/led-processors` · `/video/gpu` | 501 — Fases 4 y 7 |
| POST | `/video/displays/restore` · `/video/windows/restore` | 501 — Fase 4 |

### Red

| Método | Ruta | Estado |
|---|---|---|
| GET | `/network/interfaces` | ✅ |
| GET | `/network/ping` | ✅ Estadística por dispositivo crítico |
| GET | `/network/ping/samples?device=` | ✅ Ventana en vivo, desde memoria |
| POST | `/network/scan` | ✅ Barrido ICMP + ARP + sondeo web, y vuelca al inventario |
| GET | `/network/devices` | ✅ Inventario: lo barrido más lo añadido a mano |
| POST | `/network/devices` | ✅ Alta, renombrar, marcar y desmarcar. Upsert por IP |
| DELETE | `/network/devices/{ip}` | ✅ Sacarlo del inventario |
| POST | `/network/profile` | 501 — Fase 2 |
| GET | `/network/protocols/artnet` · `/sacn` | 501 — Fase 5 |
| POST | `/network/ndi-discovery` | 501 — Fase 5 |

### Servicios

| Método | Ruta | Estado |
|---|---|---|
| GET | `/services` | ✅ Agrupados por sector, con esperados en marcha |
| GET | `/services/baseline` | ✅ Diff contra el último baseline |
| POST | `/services/baseline` | ✅ Toma baseline (no toca la máquina) |
| POST | `/services/{name}` | 501 — Fase 2 |

### Perfiles

| Método | Ruta | Estado |
|---|---|---|
| GET | `/profiles` | ✅ Los de serie y los guardados en el nodo |
| GET | `/runs` | 501 — Fase 2 |
| GET | `/profiles/export?id=` | ✅ El perfil escrito a fichero, para llevarlo a otra máquina |
| POST | `/profiles/import` | ✅ Lee, valida y guarda; avisa de actuadores que este nodo no conoce |
| POST | `/profiles/{id}/apply` · `/runs/{runId}/rollback` | 501 — Fase 2 |

### Resto

| Método | Ruta | Estado |
|---|---|---|
| GET | `/storage` | ✅ |
| GET | `/timeline?from=&to=&modules=&limit=` | ✅ |
| POST | `/timeline/{id}/undo?dryRun=` | ✅ Devuelve un ajuste al valor anterior a esa escritura. Se niega si alguien lo tocó después |
| POST | `/report/preshow` | ✅ Veredicto verde/rojo con la lista de fallos |
| POST | `/media/scan` | 501 — Fase 5 |
| GET | `/licenses` · `/power/ups` · `/companion/variables` | 501 — Fases 5 y 6 |
| POST | `/panic/restart-graphics-driver` | ✅ Reinicia el controlador gráfico por SetupAPI; deja las salidas en negro un instante |
| POST | `/panic/{action}` | 501 — Fase 2 |

## Telemetría en vivo

`WS /api/v1/stream` (SignalR).

El cliente llama a `Subscribe(canal)` y `Unsubscribe(canal)`. El agente **solo produce lo
que alguien escucha**: si ninguna consola está suscrita a `audio.meters`, los medidores no
se leen. Es la mitad del contrato de coste del agente.

| Canal | Cadencia | Payload |
|---|---|---|
| `system` | 1 s | `SystemTelemetryFrame` |
| `audio.meters` | 20 fps | `AudioMeterFrame` |
| `network.throughput` | 1 s | `ThroughputFrame` |
| `network.ping` | 1 s | `PingFrame` |
| `timeline` | por evento | `TimelineEvent` |
| `alerts` | por cambio | `Alert` |
| `node.health` | 1 s | `NodeHealthFrame` |

Tras una reconexión, los grupos del servidor se pierden: el cliente vuelve a suscribirse
solo (`TelemetryClient.ResubscribeAsync`). Sin eso, la vista se queda congelada sin decirlo.
