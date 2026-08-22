# 0005 — Sin WPF-UI ni ScottPlot en la Fase 1

**Estado:** aceptada. Revisar en la Fase 6.

## Contexto

El diseño original elegía WPF-UI para la capa Fluent (`NavigationView`, tema Mica) y
ScottPlot para las series temporales.

## Decisión

La Fase 1 no lleva ninguna librería de UI de terceros. El tema oscuro, el rail lateral, el
medidor de nivel y el gráfico de latencia son propios:

- `Theme.xaml` — paleta, tipografía y estilos.
- Rail: un `ListBox` con plantilla, enlazado a la lista de páginas.
- `Controls/LevelMeter.cs` — un `FrameworkElement` que pinta un rectángulo y un pico que
  decae, con escala logarítmica.
- `Controls/PingSparkline.cs` — un `StreamGeometry` por serie: una sola operación de
  render para novecientos puntos.

`App.xaml` tiene el punto de enganche documentado para meter los diccionarios de WPF-UI
delante de `Theme.xaml` sin tocar el resto de la aplicación.

## Razones

1. **Verificabilidad.** El resto del stack son APIs de Windows y paquetes de Microsoft con
   versiones conocidas. Una librería de UI de terceros con una API que cambia entre
   versiones mayores es la parte del proyecto que menos se puede validar sin ejecutarla, y
   una que no restaura deja el repositorio sin compilar el primer día.
2. **Coste real.** Para un medidor a 20 fps y una ventana de quince minutos de ping, un
   `DrawingContext` gana a cualquier control genérico con plantilla. El presupuesto de CPU
   del agente no se protege gastándolo en la consola.
3. **Sin internet.** Menos dependencias, menos superficie en la restauración.

## Consecuencias

- La consola no tiene Mica ni las animaciones de Windows 11. Es oscura, densa y legible a
  dos metros, que es lo que pide una cabina, pero no se ve «Fluent».
- El rail no se colapsa a iconos todavía.
- Cuando el Timeline necesite zoom, pan y varios ejes (Fase 6), ScottPlot entra: ahí sí
  aporta algo que no merece la pena escribir a mano.
