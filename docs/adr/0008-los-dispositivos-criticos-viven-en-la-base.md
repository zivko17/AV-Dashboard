# 0008 — Los dispositivos críticos viven en la base, no en la configuración

**Estado:** aceptada. Supera la parte de [`0006`](0006-fase-1-solo-lectura.md) que daba por
sentado que el agente no escribe nada, en el sentido acotado que se explica abajo.

## Contexto

El ping continuo es el módulo que más veces salva un evento: la subida de latencia a un
procesador LED aparece ahí antes de que se vea nada en la pared. Estaba apagado en todas
las instalaciones, y no por falta de código —el colector, el gráfico, las alertas y el
check «Alcanzable: X» del pre-show estaban escritos y probados— sino porque para encender
lo había que editar `appsettings.json` y reiniciar el servicio.

La propia interfaz lo decía:

> «Sin dispositivos críticos configurados: añádelos en appsettings.json del agente, sección
> Agent:CriticalDevices, y reinícialo.»

Nadie hace eso a las 19:40 con el cliente detrás. Una función que exige editar un fichero
de configuración para empezar a servir es una función apagada.

## Decisión

Los equipos vigilados pasan a la tabla `devices` de la base del agente, y se gestionan por
la API con `GET`, `POST` y `DELETE` sobre `/api/v1/network/devices`. El colector los relee
en cada vuelta, así que un alta o una baja surten efecto en menos de un segundo sin
reiniciar nada.

`Agent:CriticalDevices` se migra una sola vez al arrancar y después deja de tener efecto.

## Por qué esto no rompe la regla de la lista blanca

El ADR 0003 dice que el agente nunca ejecuta un comando arbitrario y que todo lo que
escribe pasa por actuadores compilados. Esto sigue siendo cierto: **añadir un equipo al
inventario no escribe nada en la máquina.** Es estado del agente sobre su propia base, la
misma categoría que el baseline de servicios, que ya estaba permitido en la Fase 1 con este
razonamiento escrito en el propio endpoint.

La prueba de que la categoría es la correcta es que funciona en modo portátil, donde el
agente es de solo lectura sobre el sistema.

## Razones

- **La fricción de arranque es lo que decide si un módulo se usa.** El coste de escribir
  esto es una tarde; el de no escribirlo era que la función no existía en la práctica.
- **La tabla ya estaba.** `devices` se creó con `mac`, `vendor`, `first_seen`, `last_seen`
  e `is_critical`, y no la leía ni la escribía nadie. La configuración nunca fue el sitio
  previsto: en un JSON de arranque no caben `first_seen` ni `last_seen`.
- **Habilita el inventario.** Con los equipos en la base, el barrido de red deja de tirar
  sus resultados y puede contestar a «¿qué hay en esta red que ayer no estaba?», que es M17.
- **En modo portátil la configuración se pierde.** Va junto al ejecutable, así que un
  diagnóstico en casa de un cliente no podía conservar nada.

## Alternativas descartadas

**Recargar `appsettings.json` con `reloadOnChange`.** Obligaba a que la API reescribiese su
propio fichero de configuración —que es la clase de cosa que muerde a los seis meses—, no
resolvía el modo portátil, y no tenía dónde meter las fechas del inventario.

**Cachear la lista en `NodeState` e invalidar al escribir.** Ahorra una consulta por segundo
que no duele: un `SELECT` sobre el índice parcial `ix_devices_critical`, con decenas de
filas, al lado de un ICMP con timeout de 800 ms. A cambio deja dos fuentes de verdad que se
desincronizan, y con ellas toda la familia de fallos de «lo he añadido y no aparece».

## Consecuencias

- `Agent:CriticalDevices` queda como reliquia. Sigue en `appsettings.json` sin efecto
  después del primer arranque, y quien lo lea puede creer que sirve. Se documenta en el
  propio fichero y el agente escribe una línea en el log cuando migra.
- Hace falta una marca de «ya migrado», y esta base no tenía ni tabla de metadatos ni
  versión de esquema. Entra `meta(key, value)`. La alternativa —sembrar solo si la tabla
  está vacía— devolvería los equipos a quien los hubiera borrado todos.
- **La identidad es la IP**, respetando el `UNIQUE(node_id, ip)` que ya existía. Un equipo
  con DHCP que cambia de dirección aparece como uno nuevo y deja una fila muerta que hay que
  borrar a mano. Es deliberado: se hace ping a una dirección, no a una MAC, y que la
  dirección cambie es justo lo que quieres ver.
- **La API estrena `DELETE`.** Hasta ahora todo lo que escribía era `POST`. Rompe la
  uniformidad a cambio de usar el verbo correcto.
- Dar de baja un equipo obliga a limpiar cuatro cosas —su probe, su ventana de muestras, su
  entrada en `NodeState` y su alerta activa—. Si se olvida la última, borrar un equipo que
  estaba en rojo deja una alerta permanente de algo que ya no existe.
