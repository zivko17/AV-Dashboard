# 0004 — Npcap como dependencia opcional detectada en runtime

**Estado:** aceptada.

## Contexto

PTP (IEEE 1588) no se puede leer con sockets normales: hay que capturar tráfico en la
interfaz, y en Windows eso significa Npcap en modo compatible con WinPcap. Npcap es una
instalación aparte, con driver, que no siempre se puede hacer en una máquina de cliente.

## Decisión

Npcap es opcional. Los módulos que dependen de él —M3-PTP y el análisis fino de
multicast— se declaran `MissingDependency` con el motivo, y la consola los pinta en gris
con la explicación.

## Razones

- El requisito de que el agente sea un solo binario que corre en todas partes es más
  importante que tener PTP en todas partes.
- Un módulo que dice «no puedo leer esto y por qué» es más útil que uno que devuelve
  ceros.

## Consecuencias

- La consola tiene que saber pintar módulos no disponibles. Ya lo hace: `ModuleDescriptor`
  lleva el motivo.
- Art-Net, sACN y los contadores básicos de multicast **no** dependen de Npcap: se hacen
  con sockets normales unidos al grupo. Solo PTP y el análisis fino lo necesitan, y
  mezclar las dos cosas habría hecho opcional mucho más de lo necesario.
