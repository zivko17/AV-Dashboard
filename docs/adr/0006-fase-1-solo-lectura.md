# 0006 — La Fase 1 no escribe nada

**Estado:** superada. Cumplió su función y ya no describe el proyecto.

> La Fase 1 terminó: hoy hay treinta y cuatro actuadores y el agente escribe en la máquina.
> Lo que sigue vigente de este ADR es el razonamiento, no la regla. Ver
> [ADR 0008](0008-los-dispositivos-criticos-viven-en-la-base.md) para el primer caso de
> escritura que salió de aquí, y la sección de abajo para qué se cumplió y qué no.

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

## Qué pasó después

La decisión se sostuvo, pero no del todo por las razones que se escribieron.

**Lo que acertó.** La Fase 1 fue útil por sí sola: la sonda de ajustes, el EDID, el
inventario y el pre-show sirven sin escribir nada, y eso mantuvo el alcance a raya. Y la
curva de C# se hizo, efectivamente, en el terreno menos peligroso.

**Lo que no se cumplió.** Este ADR prometía que cuando llegasen los actuadores el marco
transaccional —snapshot, diff, rollback— ya estaría. Los actuadores llegaron primero.
`dryRun` sí existe y se usa; el rollback sigue devolviendo 501. Es exactamente la deuda que
este ADR decía que no iba a existir.

**Lo que costó no tenerlo.** Un actuador escrito sin verificar el resultado —el duplicado
del esquema Ultimate Performance— dio éxito sin haber hecho nada y dejó siete esquemas
basura en una máquina real. Se descubrió por una captura de pantalla, no por una prueba.
De ahí sale la regla que ahora sí se aplica: un actuador comprueba el estado después de
escribir y no se cree el código de retorno.

**Lo que se conserva.** Que la escritura sobre el estado del agente —el baseline de
servicios, el inventario de red— no es escritura sobre la máquina. Eso era una excepción
aparente aquí y hoy es la categoría que permite que el modo portátil siga siendo de solo
lectura sobre el sistema.
