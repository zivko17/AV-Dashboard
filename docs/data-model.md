# Modelo de datos

SQLite por nodo, en modo WAL. Un solo proceso escribe y las lecturas no lo bloquean, que
es exactamente el patrón del agente.

- **Modo servicio:** `%ProgramData%\AvDashboard\avdashboard.db`
- **Modo portátil:** `<carpeta del ejecutable>\data\avdashboard.db`

El esquema ejecutable está en `AvDashboard.Agent/Storage/AgentDatabase.cs`.

## Tablas

```sql
nodes            (id, hostname, tailscale_ip, lan_ip, agent_version, last_seen)
snapshots        (id, node_id, taken_at, trigger_kind, payload_json, notes)
profiles         (id, name, description, actions_json, inverse_id, created_at, updated_at)
devices          (id, node_id, mac, ip, alias, vendor, kind, web_port, is_critical,
                  first_seen, last_seen, notes)
ping_samples     (device_id, ts, rtt_ms, lost)
ping_rollup      (device_id, hour, samples, lost, avg_ms, min_ms, max_ms, jitter_ms)
metrics          (node_id, ts, key, value_num, value_txt)
events           (id, node_id, ts, module, sector, severity, kind, message, payload_json)
alerts           (id, node_id, raised_at, cleared_at, module, sector, severity, kind,
                  message, acknowledged)
edid_references  (connector_key, name, fingerprint, bytes, device_name, saved_at)
meta             (key, value)
```

Ocho tablas más se crearon para módulos que todavía no existen y estuvieron meses sin que
nadie las leyera ni las escribiera: `profile_runs`, `remote_hosts`, `media_scans`,
`manifests`, `window_layouts`, `display_layouts`, `service_sectors` y `service_baseline`.
Se han quitado. Un esquema que declara lo que no existe no documenta el proyecto: lo
disfraza, y hace que leer el fichero cueste el doble para saber qué se usa de verdad.
Cuando llegue el módulo, llega su tabla.

Notas de diseño:

- `trigger_kind` y no `trigger`: **`trigger` es palabra reservada en SQLite**.
- `events` es el Show Timeline. `sector` está desnormalizado a propósito para poder
  filtrar la vista sin joins.
- `meta` guarda las marcas del propio agente: qué migraciones se han hecho, cuándo fue el
  último barrido de red. Es también donde iría la versión de esquema si algún día hace
  falta una migración de verdad.
- Índice parcial en `alerts(cleared_at) WHERE cleared_at IS NULL`: la consulta de alertas
  activas se hace cada cinco segundos y es la más frecuente de todas.

## Retención

Sin esto la base crece sin control: un ping por segundo a doce dispositivos son 378
millones de filas al año.

| Tabla | Retención en crudo | Después |
|---|---|---|
| `ping_samples` | 7 días | rollup horario en `ping_rollup`, y se borran |
| `metrics` | 7 días | se borran |
| `events` | 90 días | se borran |

`RetentionService` corre cada hora, con la primera pasada un minuto después de arrancar
para no competir con el arranque del sistema. El jitter del rollup se calcula con la misma
fórmula que la vista en vivo, para que el histórico y el directo digan lo mismo.

## Lo que nunca entra en SQLite

- **Credenciales de acceso remoto.** Cuando lleguen los nodos remotos irán a Windows
  Credential Manager (DPAPI), y la base guardará solo la referencia.
- **El token del agente.** Está en su fichero con ACL restringida y rota en cada arranque.
