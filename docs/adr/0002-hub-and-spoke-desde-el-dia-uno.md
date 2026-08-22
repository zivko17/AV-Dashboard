# 0002 — Hub & spoke multi-nodo desde el día uno

**Estado:** aceptada.

## Contexto

La alternativa era hacer primero una aplicación monolítica que leyera la máquina local y
partirla en cliente/agente más adelante, cuando hiciera falta el multi-nodo.

## Decisión

Consola y agente separados desde el primer commit, hablando por HTTP y WebSocket incluso
cuando los dos corren en la misma máquina.

## Razones

- Partir un monolito en dos procesos después es una reescritura, no un refactor: todo el
  acceso a estado se convierte en asíncrono y remoto de golpe.
- El agente tiene que correr como LocalSystem y antes del login; la consola tiene que
  correr en la sesión del usuario y sin privilegios. Son dos procesos por requisito, no
  por gusto arquitectónico.
- La misma frontera da gratis el CLI y el puente con Companion.

## Consecuencias

- Hay que serializar y versionar los contratos desde el primer día
  (`Core/Contracts/ApiRoutes.cs` como fuente única).
- La consola tiene que saber pintar «el agente no responde» como un estado normal, no como
  una excepción. De ahí `ApiResult<T>` en vez de excepciones.
- El coste en latencia local es despreciable; el coste en complejidad, real y aceptado.
