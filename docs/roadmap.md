# Roadmap

El riesgo real de este proyecto no es técnico: es el alcance. Dieciocho módulos son
muchos para un desarrollador a tiempo parcial con la temporada alta comiéndose el
calendario.

De ahí la regla que ordena todo lo demás:

> **La Fase 1 tiene que ser útil por sí sola. Si el proyecto muere ahí, tiene que haber
> valido la pena.**

---

## Fase 1 — Solo lectura ✅

Consola WPF y agente local. M1, M3 base, M4 (enumeración de displays), M5 base, M6, M9,
M11 y M17 en lectura. Ping continuo. Timeline y checklist pre-show.

Útil desde el primer día, sin riesgo, y resuelve la curva de C# en el terreno menos
peligroso: nada de lo que hay aquí puede tumbar un evento porque nada escribe.

**Hecho.** Lo que queda pendiente dentro de la fase está marcado en el
[README](../README.md) y responde `501` con su motivo.

## Fase 2 — Escritura y perfiles

Agente promovido a servicio. Snapshot engine, rollback, M2 (firewall), M10 (perfiles),
M15 (pánico). `dryRun` obligatorio en toda escritura.

El motor de perfiles ya existe y está probado (`Core/Profiles/ProfileEngine.cs`); lo que
falta son los actuadores concretos, cada uno con su `ISnapshotable`.

Criterio de salida: aplicar el perfil «Evento» en un rig y volver a «Oficina» con un clic,
con el diff visible antes de aplicar.

## Fase 3 — Multi-nodo

M7, emparejamiento con confirmación de huella, vista consolidada. Despliegue en clap-house
y Aguas.

Criterio de salida: una fila por máquina con salud resumida, y la expiración de key de
Tailscale como alerta antes de que muerda.

## Fase 4 — Vídeo

M4 completo (`QueryDisplayConfig`, NVAPI, layouts de pantalla, Window Coordinate Keeper,
DeckLink, desviación de refresh) y M18 EDID Toolkit. La fase más técnica.

Criterio de salida: el procesador LED reinicia, Windows recoloca todo, y un clic lo
devuelve a su sitio.

## Fase 5 — Protocolos y producción

PTP, Art-Net/sACN, M8 (aplicaciones), M12 (media toolkit), M13 (licencias), M14 (UPS).
Requiere Npcap.

## Fase 6 — Observabilidad e integración

M11 completo con exportación, M16 Companion, M17 con diff entre nodos. Aquí es donde
entran WPF-UI y ScottPlot, si para entonces siguen aportando (ver
[ADR 0005](adr/0005-ui-sin-dependencias-en-fase-1.md)).

## Fase 7 — Procesadores LED

M4-LED por capas. **Solo con hardware delante.** La capa 1 —alcanzabilidad, latencia,
detección de reinicio— se puede hacer antes y ya avisa de un procesador caído; las capas 2
y 3 no se planifican hasta poder capturar tráfico real.
