# 0007 — Fluent de Windows 11 con los tokens de WinUI, y Sistema partido en dos

**Estado:** aceptada.

## Contexto

La consola se veía como una aplicación de WPF de hace quince años pintada de gris: barra de
título blanca sobre una ventana oscura, casillas de Windows 95, cabeceras de sección en
mayúsculas, esquinas rectas y ninguna relación con el color de acento del usuario. Y la
página Sistema mezclaba dos cosas distintas en una sola tabla filtrada por defecto, con lo
que ni servía para saber cómo está la máquina ni para cambiar un ajuste.

## Decisión

### El aspecto

Fluent de verdad, con los valores de WinUI, y sin meter WPF-UI. La [0005](0005-ui-sin-dependencias-en-fase-1.md)
sigue en pie: lo que cambia no es la dependencia, es que los números dejan de estar
elegidos a ojo.

- La paleta son los tokens de WinUI resueltos sobre el fondo base: `#202020` de ventana,
  `#2D2D2D` de tarjeta, blanco al 7 % de borde. El gris sigue siendo antracita, pero
  neutro: Windows 11 no tiñe sus superficies y la desviación se nota al lado de una
  ventana del sistema.
- Tipografía Segoe UI Variable, con Display para títulos y Text para el cuerpo. En un
  Windows 10 cae a Segoe UI, que es exactamente lo que se quiere.
- Radios de 4 para controles y 8 para tarjetas. Interruptor, casilla, caja de texto,
  Expander, barra de desplazamiento y elemento de navegación con la forma de WinUI.
- La ventana adopta la silueta del `NavigationView`: panel de navegación a todo lo alto y
  superficie de contenido con la esquina superior izquierda redondeada.
- `Services/DesktopChrome.cs` pide a DWM barra de título oscura, esquinas redondeadas y
  fondo Mica, y lee el acento del usuario de `AccentPalette`.

### La página Sistema

Dos pestañas, porque son dos preguntas:

- **Estado** — cómo está la máquina y si hay algo que mirar. Telemetría, lo que difiere del
  objetivo con su control al lado, lo que no se ha podido leer y los hechos que no se
  configuran.
- **Configuración** — el catálogo entero agrupado, con buscador y el control en cada fila,
  esté bien o mal el valor de ahora.

## Razones

**El acento se lee del sistema y no se elige.** Una aplicación que ignora el color que el
usuario ha puesto en Personalización nunca parece suya. La fuente es `AccentPalette`, la
tabla de ocho tonos que calcula el propio Windows; sobre fondo oscuro se usa Light2 y no el
base, que está pensado para fondo claro. Es el mismo tono que los interruptores de
Configuración, y por eso encajan.

**El interruptor pasa de ámbar a acento.** El argumento de antes era bueno —aquí «activado»
no significa «bien», y un salvapantallas activado está mal—, pero un interruptor ámbar no
es un control de Windows y se nota. El juicio lo sigue dando el punto de estado de la
izquierda, que para eso está.

**Las mayúsculas se van.** `DISPOSITIVOS` en gris de segunda es una convención de otra
época; Windows 11 escribe las cabeceras en caja normal y del color del texto.

**El motivo de cada ajuste pasa a estar siempre visible.** Enseñarlo solo en los que fallan
lo convertía en un reproche en lugar de en documentación.

**Los filtros de casilla se van y entra un buscador.** El filtro «solo lo que difiere»
puesto por defecto escondía treinta y siete de cuarenta y nueve filas, y quien venía a
cambiar un ajuste no lo encontraba. Un buscador no esconde nada que no le hayas pedido.

## Consecuencias

**Malas.** Todo esto es código propio y hay que mantenerlo: cuando WinUI cambie sus valores,
aquí no cambia nada solo. Y hay tres llamadas a DWM que no se pueden probar en el CI de
Linux ni en el de Windows sin escritorio.

**Mitigación.** Nada de lo de `DesktopChrome` es necesario para que la aplicación funcione:
si DWM dice que no, o el Windows es anterior al 22H2, la ventana se queda exactamente como
estaba. El fondo solo se vuelve transparente después de que Mica confirme que se ha
aplicado — una ventana transparente sin fondo detrás no es traslúcida, es negra.
