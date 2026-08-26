# Mapa del proyecto

> Generado por `tools/AvDashboard.Graph` desde el propio código. **No lo edites a
> mano**: el gancho de commit lo regenera y el CI comprueba que coincide.
>
> Un diagrama dibujado a mano miente a las dos semanas. Este no puede.

## Proyectos y dependencias

```mermaid
graph TD
    AvDashboard_Agent["AvDashboard.Agent<br/><small>net8.0-windows</small>"]
    AvDashboard_Cli(["AvDashboard.Cli<br/><small>net8.0</small>"])
    AvDashboard_Client(["AvDashboard.Client<br/><small>net8.0</small>"])
    AvDashboard_Console["AvDashboard.Console<br/><small>net8.0-windows</small>"]
    AvDashboard_Core(["AvDashboard.Core<br/><small>net8.0</small>"])
    AvDashboard_Native["AvDashboard.Native<br/><small>net8.0-windows</small>"]
    AvDashboard_Shell["AvDashboard.Shell<br/><small>net8.0-windows10.0.19041.0</small>"]
    AvDashboard_Client_Tests(["AvDashboard.Client.Tests<br/><small>net8.0</small>"])
    AvDashboard_Core_Tests(["AvDashboard.Core.Tests<br/><small>net8.0</small>"])
    AvDashboard_Graph(["AvDashboard.Graph<br/><small>net8.0</small>"])

    AvDashboard_Agent --> AvDashboard_Core
    AvDashboard_Agent --> AvDashboard_Native
    AvDashboard_Cli --> AvDashboard_Core
    AvDashboard_Client --> AvDashboard_Core
    AvDashboard_Console --> AvDashboard_Core
    AvDashboard_Console --> AvDashboard_Client
    AvDashboard_Native --> AvDashboard_Core
    AvDashboard_Shell --> AvDashboard_Core
    AvDashboard_Shell --> AvDashboard_Client
    AvDashboard_Client_Tests --> AvDashboard_Client
    AvDashboard_Core_Tests --> AvDashboard_Core
    AvDashboard_Graph --> AvDashboard_Core
```

Los proyectos redondeados son multiplataforma y el CI los prueba en Linux. Los
rectangulares dependen de Windows: COM, P/Invoke y WPF.

## Módulos

```mermaid
graph LR
    subgraph Available["Funciona · 9"]
        direction TB
        M1_Sistema_y_rendimiento["M1 Sistema y rendimiento"]
        M3_Audio_y_reloj["M3 Audio y reloj"]
        M4_Vídeo["M4 Vídeo"]
        M5_Red["M5 Red"]
        M6_Servicios["M6 Servicios"]
        M9_Almacenamiento["M9 Almacenamiento"]
        M11_Timeline__alertas_e_informes["M11 Timeline, alertas e informes"]
        M17_Inventario_de_flota["M17 Inventario de flota"]
        M18_EDID_Toolkit["M18 EDID Toolkit"]
    end
    subgraph MissingDependency["Falta una dependencia · 1"]
        direction TB
        M3_PTP_PTP___IEEE_1588["M3-PTP PTP / IEEE 1588"]
    end
    subgraph NotImplemented["Todavía no · 10"]
        direction TB
        M2_Firewall["M2 Firewall"]
        M4_LED_Procesadores_LED["M4-LED Procesadores LED"]
        M7_Nodos_remotos["M7 Nodos remotos"]
        M8_Aplicaciones_de_producción["M8 Aplicaciones de producción"]
        M10_Perfiles_de_escenario["M10 Perfiles de escenario"]
        M12_Validador_de_contenido["M12 Validador de contenido"]
        M13_Licencias_y_dongles["M13 Licencias y dongles"]
        M14_Alimentación_y_SAI["M14 Alimentación y SAI"]
        M15_Acciones_de_pánico["M15 Acciones de pánico"]
        M16_Puente_con_Companion["M16 Puente con Companion"]
    end
```

## Catálogo de ajustes (M1)

| Grupo | Ajustes | Con objetivo | Impacto alto | Fuera de los perfiles |
|---|---:|---:|---:|---:|
| Energía | 13 | 13 | 8 | 0 |
| Kernel y rendimiento | 14 | 11 | 8 | 2 |
| Interrupciones y actualizaciones | 11 | 11 | 8 | 0 |
| Seguridad | 3 | 1 | 1 | 0 |
| Arranque y sesión | 4 | 4 | 3 | 0 |
| Adaptador de red | 3 | 3 | 2 | 0 |
| Diagnóstico | 4 | 0 | 0 | 0 |
| **Total** | **52** | **43** | **30** | **2** |

«Fuera de los perfiles» son los que nunca entran en un cambio automático porque
tocarlos reduce la seguridad de la máquina de forma real.

## Superficie

- **27 comandos** de CLI y paleta.
- **24 actuadores** declarados, de los cuales 10 pueden interrumpir una salida en directo.

### Comandos por sector

| Sector | Comandos |
|---|---|
| Audio | `audio`, `ptp` |
| Video | `edid-check`, `edid-diff`, `edid-dump`, `edid-info`, `edid-list`, `edid-ref`, `gpu-reset`, `monitors` |
| Network | `latency`, `netinfo`, `portcheck`, `scan`, `setip` |
| Production | `panic`, `preshow`, `profile`, `services`, `timeline` |
| SystemEssential | `audit`, `profile-export`, `profile-import`, `profiles`, `sysinfo`, `tele` |
| Storage | `disks` |

