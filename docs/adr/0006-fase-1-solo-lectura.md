# 0006 — La Fase 1 no escribe nada

**Estado:** aceptada.

## Contexto

Las funciones de más valor del proyecto son las que cambian la máquina: los perfiles de
escenario, las acciones de pánico, el conmutador de perfiles de IP. Es tentador empezar
por ahí.

## Decisión

La Fase 1 es de solo lectura. Ni un actuador. Las rutas de escritura están declaradas y
responden 501 con la fase en la que llegan.

## Razones

- Las máquinas objetivo están en producción: **un bug no puede tumbar un evento.** Un
  colector que falla escribe una línea en el log; un actuador que falla deja un rig sin
  salida de vídeo a las nueve de la noche.
- C# no es el stack habitual del autor. La curva se hace en el terreno menos peligroso.
- Obliga a que la Fase 1 sea útil por sí sola, que es la mitigación del riesgo real del
  proyecto: el alcance.

## Consecuencias

- El motor de perfiles existe y está probado antes de que exista un solo actuador. Cuando
  lleguen, el marco transaccional —snapshot, diff, rollback, guardarraíl de show activo—
  ya está.
- Hay funcionalidad visible que todavía no hace nada. Se enseña igualmente, con la fase en
  la que llega: una aplicación que esconde lo que no ha hecho obliga a llevar el roadmap
  en la cabeza.
- `POST /services/baseline` es la única excepción aparente: escribe en el estado del
  agente, no en la máquina.
