# 0001 — Inventario de red y dispositivos críticos

**Estado:** aprobado, sin implementar.
**Módulos:** M5 Red, M17 Inventario de flota.

## El problema

El ping continuo es, según su propio comentario en el código, «el módulo que más veces te
va a salvar». Hoy está apagado, y para encenderlo la interfaz te dice esto:

> «Sin dispositivos críticos configurados: añádelos en appsettings.json del agente,
> sección Agent:CriticalDevices, y reinícialo.»

Editar un fichero de configuración y reiniciar un servicio no es algo que vayas a hacer a
las siete y media con el cliente detrás. El resultado es que la tabla de latencia, el
gráfico de ping, las alertas de pérdida y los checks «Alcanzable: X» del pre-show están
escritos, probados y sin usar.

## Lo que ya existe y no se toca entre sí

Esta función es sobre todo cableado. Antes de escribir una línea ya están:

- La tabla `devices` en `AgentDatabase`, con `mac`, `ip`, `alias`, `vendor`, `kind`,
  `web_port`, `is_critical`, `first_seen`, `last_seen` y `notes`, más un índice parcial
  `ix_devices_critical` y un único `ux_devices_node_ip`. **No la lee ni la escribe nadie.**
- El modelo `DiscoveredDevice`, que encaja con esa tabla columna por columna, incluidos
  `Alias`, `IsCritical`, `FirstSeen`, `LastSeen` y `DisplayName`.
- La constante `ApiRoutes.NetworkDevices`, declarada y **no servida**: hoy devuelve 404 con
  el cuerpo vacío.
- `SubnetScanner`, que barre la subred y resuelve el fabricante por OUI sin internet.
- `PreShowChecklist`, que ya construye un check de alcanzabilidad por cada equipo con
  estadísticas de ping. **No hay que tocarlo.**

## La decisión

Los dispositivos críticos dejan de vivir en `appsettings.json` y pasan a la base de datos
del agente, y el escaneo deja de tirar sus resultados y pasa a alimentar un inventario.

Va en un ADR aparte —`0008`— porque revertirlo obligaría a migrar datos.

### El almacén

Repositorio `DeviceRepository` en `Agent/Storage`, con el patrón de `EdidReferenceRepository`.

**La identidad es la IP**, respetando el `UNIQUE(node_id, ip)` que ya está. Es lo honesto:
haces ping a una IP, no a una MAC. Que un equipo cambie de IP y aparezca como nuevo no es
un fallo, es información, y de la que arruina un montaje.

| Origen | `is_critical` | `mac` / `vendor` | `first_seen` | `last_seen` |
|---|---|---|---|---|
| Escaneo | 0, lo marcas tú | del barrido + OUI | primer barrido que lo vio | último barrido |
| Alta manual | 1 | vacíos | al añadirlo | vacío hasta que responda |
| Ping (★) | — | — | — | se refresca al responder |

Marcar y desmarcar es independiente del escaneo: **un barrido nuevo nunca borra tus
estrellas ni pisa tu alias.** Un equipo añadido a mano y encontrado después por el escaneo
es el mismo: misma IP, se fusiona.

`last_seen` del ping **no se escribe cada segundo**. `PingCollector` ya acumula muestras y
toca disco cada 30 (`FlushThreshold`); la fecha viaja en ese mismo vuelco.

### La migración desde la configuración

Al arrancar, si `Agent:CriticalDevices` trae equipos se insertan como críticos **una sola
vez**; a partir de ahí manda la base. La marca de «ya migrado» necesita un sitio, y esta
base no tiene ni tabla de metadatos ni versión de esquema. Se añade:

```sql
CREATE TABLE IF NOT EXISTS meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);
```

Tres líneas que además dejan sitio para la versión de esquema, que hoy no existe. La
alternativa —«sembrar solo si la tabla está vacía»— significa que si borras todos tus
equipos y reinicias, te los devuelve, que es exactamente el tipo de sorpresa que este
proyecto persigue.

**En modo portátil se puede escribir.** Es estado del agente, no de la máquina: el mismo
razonamiento que ya está escrito en el endpoint del baseline de servicios.

### La API

| Verbo | Ruta | Qué hace |
|---|---|---|
| `GET` | `/api/v1/network/devices` | El inventario entero |
| `POST` | `/api/v1/network/devices` | Alta manual, renombrar, marcar y desmarcar. Upsert por IP |
| `DELETE` | `/api/v1/network/devices/{ip}` | Sacarlo del inventario |

Un solo `POST` para las cuatro operaciones porque las cuatro son «así queda este equipo», y
un único sitio donde se valida es la misma idea que el `switch` de `SettingsWriter`.

Dos desviaciones conscientes:

- **`DELETE` es el primer verbo de borrado del proyecto.** Todo lo que escribe hoy es
  `POST`. Se usa porque es el verbo correcto y no cuesta nada, pero rompe el patrón.
- **Solo se aceptan IPs, no nombres.** El índice único es sobre `ip`, el escaneo produce
  IPs, y un nombre que resuelve a una IP distinta cada día rompe la identidad. Además, en
  la red de un show el DNS o no está o miente.

`POST /network/scan` sigue devolviendo lo mismo, pero antes vuelca lo encontrado al
inventario y rellena los campos de `DiscoveredDevice` que hoy salen vacíos. No cambia el
contrato.

**El «esto no estaba ayer» sale del Timeline**, con la regla del commit `setting.unchanged`:
un evento por equipo nuevo, pero **el primer escaneo de una máquina emite uno solo** —
«inventario inicial: 14 equipos»—. Catorce líneas que no son un cambio restarían al
Timeline en lugar de sumarle.

### El colector

`PingCollector` deja de recibir `AgentOptions` y recibe el repositorio; en cada tick pide la
lista de críticos. Un `SELECT` sobre `ix_devices_critical` con decenas de filas, al lado de
un ICMP con timeout de 800 ms, es ruido de fondo. Se descarta cachear en `NodeState`: ahorra
una consulta que no duele a cambio de dos fuentes de verdad que se desincronizan.

**Dar de baja un equipo es lo que hay que hacer bien**, porque hoy no hay camino para eso y
deja basura en tres sitios:

- se tira su `PingProbe`, su ventana de muestras y su último estado de salud;
- se borra de `NodeState`, que hoy solo sabe añadir: hace falta un `RemovePing`;
- y se **limpia su alerta activa**. Sin esto, borrar un equipo en rojo deja una alerta
  permanente de algo que ya no existe y sin forma de bajarla.

### La interfaz

Fila con comandos propios, `DeviceRow` en Client, como `NetworkInterfaceRow`. No es
opcional: en WinUI no hay `RelativeSource AncestorType`, así que los comandos de fila tienen
que vivir en la fila. Lleva `AlternarCritico`, `GuardarAlias` y `Quitar`.

La página Red sigue con tres tarjetas. **Interfaces** no se toca. **Ping continuo** solo
cambia su mensaje de vacío. **Escaneo del segmento** pasa a llamarse **Inventario de red** y
enseña el inventario persistido, no el último barrido, con una fila de alta manual, la de
escaneo que ya está, y la lista con ★/☆ pulsable, IP, MAC, fabricante, alias editable en la
fila y la columna «visto».

La columna «visto» dice lo que se sabe y ni una palabra más. Los estados se evalúan **en
este orden**, y gana el primero que aplica:

| # | Estado | Texto |
|---:|---|---|
| 1 | ★ y todavía sin diez muestras | *midiendo…* |
| 2 | ★ respondiendo | *responde* |
| 3 | ★ que ha respondido antes y ahora no | *sin responder desde las 18:40* |
| 4 | ★ que no ha respondido nunca | *no responde desde que se añadió* |
| 5 | ☆ visto en el último barrido | *visto en el escaneo de las 17:12* |
| 6 | ☆ visto solo en barridos anteriores | *visto por última vez el 24 ago* |

El «✨ 1ª vez hoy» **no es un estado**, es una marca aparte que se pinta junto al alias
cuando `first_seen` cae en el día de hoy. Si fuera un estado más taparía al de responder,
que es el que importa cuando algo se está cayendo.

Los estados 1 y 4 son los que evitan mentir en los dos primeros minutos de vida de un
equipo: `PingCollector` ya se calla hasta tener diez muestras porque «con menos, cualquier
veredicto es ruido», y la fila tiene que decir lo mismo. Y un equipo recién añadido a una IP
equivocada no ha dejado de responder: es que nunca respondió, y son dos averías distintas.

**Una fila ☆ nunca dice «lleva 22 minutos sin verse».** No hay escaneo automático de fondo
—un barrido ARP continuo sobre la red de un show es justo lo que no quieres corriendo sin
que nadie lo pida— así que de los no críticos solo se sabe lo que dijo el último barrido.
Es la misma regla que en Sistema: una fila no puede afirmar más de lo que sabe.

## Pruebas

La lógica que decide no vive en el repositorio ni en el colector, sino en tres funciones
puras en Core, que es `net8.0` y por tanto lo que el CI prueba también en Linux:

- **`DeviceInventory.Merge(inventario, escaneo, ahora)`** — que un escaneo no pise el alias,
  no borre las estrellas, sí actualice MAC, fabricante y `last_seen`, y distinga nuevo de
  conocido.
- **`DeviceSeenState`** — cuál de los seis estados corresponde y en qué orden ganan. Así la
  regla de no insinuar vigilancia inexistente se prueba en vez de confiarse al XAML, y se
  cubren los dos casos que se escapan a ojo: el equipo con menos de diez muestras y el que
  no ha respondido nunca.
- **El diff del conjunto de críticos** — qué probes se crean y cuáles se retiran. Es donde
  vive el bug de la alerta huérfana; sacarlo del colector es lo que lo hace comprobable.

**Lo que no se prueba, dicho claro:** `DeviceRepository`, los endpoints y el ciclo de vida
real del colector se quedan sin test automático. No hay proyecto de pruebas para Agent y no
se monta uno de tapadillo dentro de esta función. Compilan, y eso es todo lo que se sabrá de
ellos.

La comprobación real es a mano, con el agente en marcha y **sin reiniciarlo**: escanear,
marcar el router como crítico y verlo entrar en el ping en menos de un segundo; que
`avdash preshow` saque «Alcanzable»; tirar el enlace y ver la alerta y la línea del
Timeline; y **borrar el equipo y comprobar que la alerta se limpia sola**.

## Entrega

Seis commits. Del 1 al 4 la función está completa y usable por API antes de que exista un
solo botón; si se para en el 4, lo que hay sirve y no queda nada a medias.

1. Core: las tres funciones puras y sus tests.
2. Tabla `meta`, `DeviceRepository`, migración desde `appsettings`, los tres endpoints, y el
   ADR `0008`.
3. `PingCollector` lee del repositorio: alta y baja en caliente, alerta limpiada.
4. El escaneo persiste al inventario y emite los eventos de equipo nuevo.
5. `DeviceRow`, ViewModel y la tarjeta Inventario en WPF.
6. El mismo XAML en WinUI 3.

## Fuera de alcance

- **`kind`, `web_port` y `notes` se quedan a `NULL`.** Están en la tabla; no se les inventa
  uso todavía.
- **El CLI no gana comandos de inventario.** `avdash scan` enseñará la estrella porque el
  campo ya viaja, y nada más.
- **Nada de escaneo automático**, por la razón de arriba.
- **Detectar que una MAC conocida ha cambiado de IP** es útil y es barato con lo que queda
  montado, pero es otra entrega.

## Consecuencias, incluidas las malas

- `Agent:CriticalDevices` queda como reliquia de migración. Seguirá en `appsettings.json`
  sin efecto tras el primer arranque, y eso confunde a quien lo lea. Hay que documentarlo
  donde se lea antes: en el propio comentario del fichero.
- La identidad por IP significa que un equipo con DHCP se duplica al cambiar de dirección.
  Es deliberado, pero deja filas muertas en el inventario que hay que borrar a mano.
- El inventario crece con lo que haya en la red: teléfonos, impresoras, portátiles ajenos.
  No hay purga automática y `last_seen` es lo único que permitirá hacerla algún día.
- La API gana un verbo (`DELETE`) que rompe la uniformidad del resto.
