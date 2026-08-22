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
profile_runs     (id, profile_id, node_id, started_at, finished_at, status, snapshot_id, log_json)
devices          (id, node_id, mac, ip, alias, vendor, kind, web_port, is_critical,
                  first_seen, last_seen, notes)
ping_samples     (device_id, ts, rtt_ms, lost)
ping_rollup      (device_id, hour, samples, lost, avg_ms, min_ms, max_ms, jitter_ms)
metrics          (node_id, ts, key, value_num, value_txt)
events           (id, node_id, ts, module, sector, severity, kind, message, payload_json)
alerts           (id, node_id, raised_at, cleared_at, module, sector, severity, kind,
                  message, acknowledged)
remote_hosts     (id, label, address, protocol, port, credential_ref, notes)
media_scans      (id, path, scanned_at, results_json)
manifests        (id, node_id, taken_at, payload_json)
window_layouts   (id, name, node_id, entries_json, saved_at)
display_layouts  (id, name, node_id, topology_json, edid_blobs, saved_at)
service_sectors  (service_name, sector)
service_baseline (taken_at, payload_json)
```

Notas de diseño:

- `trigger_kind` y no `trigger`: **`trigger` es palabra reservada en SQLite**.
- `events` es el Show Timeline. `sector` está desnormalizado a propósito para poder
  filtrar la vista sin joins.
- `service_sectors` guarda la clasificación manual: mueves un servicio de sector una vez y
  se queda ahí para siempre, en todos los nodos.
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

- **Credenciales de acceso remoto.** Van a Windows Credential Manager (DPAPI);
  `remote_hosts.credential_ref` guarda solo la referencia.
- **El token del agente.** Está en su fichero con ACL restringida y rota en cada arranque.
