# 0003 — Lista blanca de actuadores, sin PowerShell genérico

**Estado:** aceptada.

## Contexto

La forma rápida de implementar los perfiles de escenario es un endpoint que reciba un
script y lo ejecute. El agente corre como LocalSystem, así que funcionaría para todo.

## Decisión

El agente **no ejecuta comandos arbitrarios**. Solo actuadores compilados, cada uno con su
identificador estable, su `PreviewAsync` y su `ApplyAsync`. No hay endpoint genérico de
PowerShell y no lo habrá.

## Razones

Un agente LocalSystem con ejecución arbitraria de comandos, escuchando en un puerto, en
tres máquinas de cliente, es una puerta trasera. Da igual que esté detrás de un token y de
Tailscale: la diferencia entre una herramienta y una puerta trasera es precisamente esta
decisión.

## Consecuencias

- Cada capacidad nueva de escritura es código nuevo y una revisión. No se puede
  «configurar» una acción que no existía.
- Los perfiles no pueden hacer nada que un actuador no sepa hacer, lo cual limita la
  flexibilidad a propósito.
- El motor de perfiles trata un actuador ausente como paso saltado, no como fallo: en
  multi-nodo es normal que el rig portátil no tenga DeckLink.
