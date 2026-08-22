# 0001 — UI nativa en WPF, no WebView2

**Estado:** aceptada.

## Contexto

Una revisión del diseño proponía Vite + React + WebView2 para la consola. La alternativa
era WPF nativo.

## Decisión

WPF nativo.

## Razones

1. Los medidores de audio a 20 fps, los grids de 250 dispositivos y los gráficos de ping
   continuo son exactamente donde una UI web en WebView2 empieza a comer CPU **en la
   máquina que estás intentando optimizar**. Contradice el requisito no funcional de
   < 1 % de CPU en reposo.
2. WebView2 es Chromium embebido: unos 150 MB de RAM adicionales y un proceso más que
   puede colgarse durante un show.
3. Un panel de control del sistema debe sentirse como parte del sistema.

## Consecuencias

- Sin acceso desde móvil o tablet.
- Sin acceso desde una máquina que no tenga AV Dashboard instalado.
- La consola es un ejecutable de Windows y punto.

La API REST + WebSocket del agente se mantiene igualmente, porque es lo que da el
multi-nodo, el CLI y el puente con Companion. Si algún día hace falta un panel en el iPad
de FOH, se añade un frontend web al agente sin tocar el backend — pero no es objetivo del
proyecto y no condiciona ninguna decisión.
