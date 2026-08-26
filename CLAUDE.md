# Contexto del proyecto

Panel de control para rigs de AV en directo sobre Windows. Un **agente** que corre en cada
máquina y una **consola** que lo mira. Contesta a la pregunta de las siete y media de la
tarde con el cliente detrás: *¿qué está mal en este equipo y qué hago?*

No es un optimizador ni un debloater. La diferencia está en que cada ajuste sabe cuál es el
valor correcto **en un rig de producción**, cuánto mueve la aguja de verdad y qué
contrapartida tiene tocarlo.

## La regla que no se rompe

**El agente nunca ejecuta un comando arbitrario.** No hay endpoint genérico de PowerShell,
ni de shell, ni de nada parecido. Todo lo que escribe pasa por actuadores compilados, y el
`switch` de `SettingsWriter.Apply` **es** la lista blanca: un identificador que no esté ahí
no se puede tocar por mucho que alguien construya la petición a mano.

Corolarios que también van en serio:

- El agente escucha en `127.0.0.1` y en la IP de Tailscale. Nunca en `0.0.0.0`.
- Token bearer en `%ProgramData%\AvDashboard\agent.token`, ACL de Administradores y SYSTEM,
  rotado al reiniciar el servicio. En modo `--portable`, en `%LocalAppData%`.
- Toda escritura deja rastro en el Timeline con el valor anterior. Es el registro de
  auditoría, y además es lo único que hará posible deshacer.
- Las credenciales de acceso remoto van al Administrador de credenciales de Windows, nunca
  al SQLite.

Ver [`docs/adr/0003-lista-blanca-de-actuadores.md`](docs/adr/0003-lista-blanca-de-actuadores.md).

## Cómo está repartido

| Proyecto | Marco | Qué hay |
|---|---|---|
| `AvDashboard.Core` | net8.0 | Modelos, catálogo de ajustes, perfiles, EDID. **Sin nada de Windows** |
| `AvDashboard.Client` | net8.0 | Cliente HTTP y SignalR, y los once ViewModels. Sin nada de Windows |
| `AvDashboard.Native` | net8.0-windows | P/Invoke, COM y WMI. El 70 % de esta capa es interop |
| `AvDashboard.Agent` | net8.0-windows | Servicio, API REST mínima, hub de SignalR, SQLite |
| `AvDashboard.Console` | net8.0-windows | Interfaz de WPF. **La que funciona entera hoy** |
| `AvDashboard.Shell` | net8.0-windows10.0.19041 | Interfaz de WinUI 3, a medio portar |
| `AvDashboard.Cli` | net8.0 | `avdash`, mismos nombres que av-light-tools |
| `AvDashboard.Graph` | net8.0 | Genera `docs/grafo.md` desde el propio código |

Que Core y Client sean `net8.0` a secas no es casualidad: es lo que permite que el CI de
Linux los compile y los pruebe, y por tanto lo que impide que se aten a Windows sin que
nadie se dé cuenta. **Si algo se puede escribir en Core, va en Core.**

Los dos tienen proyecto de pruebas y el CI de Linux los corre. `Native` y `Agent` no
tienen ninguna, y ahí es donde han salido los fallos que han llegado a una máquina real.

## Compilar y arrancar

```powershell
.\scripts\Start-Dev.ps1          # para el agente, compila, arranca agente y consola
.\scripts\Start-Dev.ps1 -Nueva   # abre la interfaz de WinUI 3 en vez de la de WPF
.\scripts\Start-Dev.ps1 -SinConsola
.\scripts\Stop-Dev.ps1
.\scripts\Get-Diagnostico.ps1    # informe de lo que el agente ve, listo para pegar
```

Desde VS Code: `F5` con «Agente + consola». Ver `.vscode/launch.json`.

**El agente en marcha bloquea sus propias DLL** y `dotnet build` falla con `MSB3027`. Por
eso los scripts y la tarea de VS Code lo paran antes de compilar. Es la fricción que más
veces se paga si no está resuelta en un sitio.

## Convenciones

**Todo en castellano**: interfaz, comentarios, documentación y mensajes de commit. Los
identificadores de código, en inglés.

**Los comentarios explican por qué, no qué.** Un comentario que repite lo que hace la línea
de abajo es ruido; uno que explica por qué se eligió ese camino y no el evidente es lo que
evita que alguien lo «arregle» dentro de seis meses. Si algo parece raro, el comentario
tiene que decir qué pasa si se hace de la forma normal.

**Un ADR por decisión que costaría cara revertir**, en `docs/adr/`. Contexto, decisión y
consecuencias, incluidas las malas.

**Mensajes de commit largos.** El asunto dice qué cambia; el cuerpo, por qué, y qué se
descartó. No hay revisión de código en este proyecto, así que el commit es el único sitio
donde queda el razonamiento.

## Lo que sabe el CI y tú no

`docs/grafo.md` se genera desde el código —ficheros de proyecto, registro de módulos,
catálogo y comandos leídos del ensamblado— y el CI comprueba que está al día. Si tocas un
módulo, un comando o el catálogo y no lo regeneras, sale rojo:

```powershell
dotnet run --project tools/AvDashboard.Graph -- .
```

El gancho de pre-commit lo hace solo, pero hay que instalarlo una vez: `.\scripts\Install-Hooks.ps1`.

El fichero no lleva fecha ni hash a propósito: si los llevara, cambiaría en cada ejecución y
el `--check` no podría comprobar nada.

## El estado real, sin adornos

**Las lecturas están verificadas contra una máquina; las escrituras no.** La sonda de
ajustes, `QueryDisplayConfig`, la lectura de EDID del registro y el recorrido de tareas
programadas por COM han corrido contra un equipo real y se han contrastado valor a valor con
el registro. Las **escrituras** —powercfg, los offloads de la NIC, el reinicio de GPU por
SetupAPI y todas las escrituras de registro— siguen sin verificar: compilan, y eso es todo
lo que se sabe de ellas. La verificación es `Get-Diagnostico.ps1` en una máquina de verdad.

**Un actuador comprueba el resultado; no se cree el código de retorno.** Costó siete
esquemas de energía basura en una máquina real: `PowerDuplicateScheme` devolvía cero, el
actuador daba éxito, y el Timeline anotó un cambio que no había ocurrido porque nadie
volvió a leer. Se descubrió por una captura de pantalla, no por una prueba.

Un valor ausente en el registro no es lo mismo que un valor que no se deja leer, y la sonda
los distingue (`RegistrySettingsReader.Probe`). La mitad de las claves de M1 solo existen
cuando alguien ha tocado el ajuste, así que la ausencia se resuelve al valor por defecto
documentado. La excepción es la que no lo tiene estable entre versiones: esa se queda sin
lectura, pero declarando el motivo. **Una fila sin valor y sin motivo es un fallo.**

Nueve módulos funcionan, uno espera a Npcap y diez están declarados y devuelven `501` con la
fase en la que llegan. Cincuenta y cuatro ajustes en el catálogo, treinta y seis escribibles.

La interfaz de WinUI 3 tiene portadas las ocho secciones que existen. Las tres que dicen
«pendiente» —Producción, Nodos e Informes— tampoco existen en la de WPF. La de WPF sigue
siendo la de por defecto hasta que la nueva se use en un montaje de verdad.

## Diferencias entre WPF y WinUI 3 que cuestan tiempo

Si tocas `AvDashboard.Shell`, esto es lo que no se porta y hay que rehacer:

- Una `Window` no es un `FrameworkElement`: no tiene `DataContext` ni `Resources`.
- No hay `Trigger`, `DataTrigger` ni `Style.Triggers`. Se usa `VisualStateManager` o un
  conversor.
- No hay `RelativeSource AncestorType`. Por eso los comandos de fila viven en la fila.
- No hay plantillas implícitas por tipo: hace falta un `DataTemplateSelector`.
- Los enlaces no tienen `StringFormat`. El texto formateado sale del ViewModel, y por eso
  hay filas (`TimelineEventRow`, `NetworkInterfaceRow`, `ServiceRow`…) que solo existen para
  dar formato. Viven en Client y las usan las dos interfaces: escribir el formato en el XAML
  significaría escribirlo dos veces y verlas divergir.
- No hay `UniformGrid` ni `WrapPanel`. Para una rejilla que se recoloca, `ItemsRepeater` con
  `UniformGridLayout`.
- No existe `OnRender` ni `DrawingContext`: lo que se dibuja a mano se construye con formas
  del árbol visual. Ver `Controls/PingSparkline.cs`.
- Un enlace sin ruta —`{Binding}`— sobre un `DataContext` nulo no llega a llamar al
  conversor. La visibilidad se enlaza en el padre y el `DataContext` se cambia en el hijo.
- `ToolTip="x"` es `ToolTipService.ToolTip="x"`.
- `EnableMsixTooling` va en `true` aunque la aplicación no se empaquete. En `false`, MrtCore
  busca sus tareas donde las deja Visual Studio y `dotnet build` revienta con `MSB4062`.

## Dónde está lo demás

- [`docs/primer-arranque.md`](docs/primer-arranque.md) — empieza por aquí
- [`docs/architecture.md`](docs/architecture.md) — topología, módulos, seguridad
- [`docs/roadmap.md`](docs/roadmap.md) — qué llega en qué fase
- [`docs/api.md`](docs/api.md) — rutas del agente
- [`docs/adr/`](docs/adr/) — las decisiones y por qué
- [`docs/grafo.md`](docs/grafo.md) — el mapa, generado
