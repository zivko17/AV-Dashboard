using AvDashboard.Core.Taxonomy;

namespace AvDashboard.Core.Contracts;

/// <summary>
/// Un comando de la paleta (F-15) y del CLI. Mismo nombre corto en los dos sitios:
/// la memoria muscular de av-light-tools no se tira, se hereda.
/// </summary>
public sealed record CommandDescriptor
{
    /// <summary>Nombre corto de una palabra: "scan", "latency", "setip", "killp".</summary>
    public required string Name { get; init; }

    public required string Summary { get; init; }
    public Sector Sector { get; init; } = Sector.Unclassified;

    /// <summary>Sintaxis de los argumentos, para la ayuda: "setip &lt;perfil|CIDR&gt;".</summary>
    public string? Usage { get; init; }

    /// <summary>Ruta de la API que ejecuta el comando.</summary>
    public required string Route { get; init; }

    public required CommandMethod Method { get; init; }

    /// <summary>Nombres alternativos aceptados. `myip` es `netinfo` con otro sombrero.</summary>
    public IReadOnlyList<string> Aliases { get; init; } = Array.Empty<string>();

    /// <summary>true si escribe. En la paleta se pinta distinto y pide confirmacion.</summary>
    public bool IsWrite => Method != CommandMethod.Get;

    /// <summary>Modulo que lo implementa; si no esta disponible, el comando se pinta en gris.</summary>
    public string? ModuleId { get; init; }
}

public enum CommandMethod
{
    Get,
    Post,
}

/// <summary>
/// Catalogo unico de comandos. Lo consumen la paleta Ctrl+K de la consola y
/// AvDashboard.Cli, para que un comando exista una sola vez.
/// </summary>
public static class CommandCatalog
{
    public static IReadOnlyList<CommandDescriptor> All { get; } = new CommandDescriptor[]
    {
        new()
        {
            Name = "netinfo", Aliases = new[] { "myip" }, ModuleId = "M5", Sector = Sector.Network,
            Summary = "Interfaces de red: IP, máscara, gateway, DNS, velocidad de enlace y MTU.",
            Route = ApiRoutes.NetworkInterfaces, Method = CommandMethod.Get,
        },
        new()
        {
            Name = "scan", Aliases = new[] { "whoison" }, ModuleId = "M5", Sector = Sector.Network,
            Summary = "Descubre dispositivos del segmento por ARP, ICMP y mDNS.",
            Usage = "scan [CIDR]",
            Route = ApiRoutes.NetworkScan, Method = CommandMethod.Post,
        },
        new()
        {
            Name = "latency", Aliases = new[] { "pingmulti" }, ModuleId = "M5", Sector = Sector.Network,
            Summary = "Latencia, jitter y pérdida de los dispositivos críticos.",
            Route = ApiRoutes.NetworkPing, Method = CommandMethod.Get,
        },
        new()
        {
            Name = "portcheck", ModuleId = "M5", Sector = Sector.Network,
            Summary = "Comprueba si un puerto TCP está abierto en un host.",
            Usage = "portcheck <host> <puerto>",
            Route = ApiRoutes.NetworkScan, Method = CommandMethod.Post,
        },
        new()
        {
            Name = "setip", ModuleId = "M5", Sector = Sector.Network,
            Summary = "Aplica un perfil de IP con nombre, o DHCP.",
            Usage = "setip <perfil|dhcp>",
            Route = ApiRoutes.NetworkProfile, Method = CommandMethod.Post,
        },
        new()
        {
            Name = "services", Aliases = new[] { "services-audio" }, ModuleId = "M6", Sector = Sector.Production,
            Summary = "Servicios agrupados por sector AV, con lo que se espera en marcha.",
            Route = ApiRoutes.Services, Method = CommandMethod.Get,
        },
        new()
        {
            Name = "audio", ModuleId = "M3", Sector = Sector.Audio,
            Summary = "Dispositivos de audio, formato en uso y mismatches de sample rate.",
            Route = ApiRoutes.AudioDevices, Method = CommandMethod.Get,
        },
        new()
        {
            Name = "ptp", ModuleId = "M3", Sector = Sector.Audio,
            Summary = "Grandmaster PTP del segmento, offset y pérdidas de sync.",
            Route = ApiRoutes.AudioPtp, Method = CommandMethod.Get,
        },
        new()
        {
            Name = "monitors", ModuleId = "M4", Sector = Sector.Video,
            Summary = "Displays activos: resolución, refresh, posición y EDID.",
            Route = ApiRoutes.VideoDisplays, Method = CommandMethod.Get,
        },
        new()
        {
            Name = "profiles", ModuleId = "M10", Sector = Sector.SystemEssential,
            Summary = "Perfiles de escenario que conoce este nodo.",
            Route = ApiRoutes.Profiles, Method = CommandMethod.Get,
        },
        new()
        {
            Name = "profile-export", ModuleId = "M10", Sector = Sector.SystemEssential,
            Summary = "Escribe un perfil a fichero para llevarlo a otra máquina.",
            Usage = "profile-export <id> [fichero]",
            Route = ApiRoutes.ProfileExport, Method = CommandMethod.Get,
        },
        new()
        {
            Name = "profile-import", ModuleId = "M10", Sector = Sector.SystemEssential,
            Summary = "Lee un perfil de un fichero, lo valida y lo guarda en este nodo.",
            Usage = "profile-import <fichero>",
            Route = ApiRoutes.ProfileImport, Method = CommandMethod.Post,
        },
        new()
        {
            Name = "audit", ModuleId = "M1", Sector = Sector.SystemEssential,
            Summary = "Compara los ajustes del sistema con el objetivo de un rig y lista lo que difiere.",
            Usage = "audit [--todo]",
            Route = ApiRoutes.SystemAudit, Method = CommandMethod.Get,
        },
        new()
        {
            Name = "edid-list", ModuleId = "M18", Sector = Sector.Video,
            Summary = "Los EDID que conoce la máquina, conectados y recordados.",
            Route = ApiRoutes.VideoEdid, Method = CommandMethod.Get,
        },
        new()
        {
            Name = "edid-info", ModuleId = "M18", Sector = Sector.Video,
            Summary = "Decodificación completa de un EDID: timings, límites, audio y HDR.",
            Usage = "edid-info <id>",
            Route = ApiRoutes.VideoEdid, Method = CommandMethod.Get,
        },
        new()
        {
            Name = "edid-check", ModuleId = "M18", Sector = Sector.Video,
            Summary = "Valida checksums y coherencia entre los timings y los límites declarados.",
            Route = ApiRoutes.VideoEdid, Method = CommandMethod.Get,
        },
        new()
        {
            Name = "edid-diff", ModuleId = "M18", Sector = Sector.Video,
            Summary = "Compara dos EDID y marca lo que cambia la negociación del cable.",
            Usage = "edid-diff <id-izquierda> <id-derecha>",
            Route = ApiRoutes.VideoEdidDiff, Method = CommandMethod.Get,
        },
        new()
        {
            Name = "edid-ref", ModuleId = "M18", Sector = Sector.Video,
            Summary = "Referencias guardadas y salidas cuyo EDID ya no coincide con la suya.",
            Route = ApiRoutes.VideoEdidReference, Method = CommandMethod.Get,
        },
        new()
        {
            Name = "edid-dump", ModuleId = "M18", Sector = Sector.Video,
            Summary = "Vuelca un EDID a .bin, para CRU, AW EDID Editor o cargarlo en una matriz.",
            Usage = "edid-dump <id> [fichero]",
            Route = ApiRoutes.VideoEdidRaw, Method = CommandMethod.Get,
        },
        new()
        {
            Name = "tele", Aliases = new[] { "top" }, ModuleId = "M1", Sector = Sector.SystemEssential,
            Summary = "Telemetría de CPU, RAM y temperaturas.",
            Route = ApiRoutes.System, Method = CommandMethod.Get,
        },
        new()
        {
            Name = "sysinfo", ModuleId = "M17", Sector = Sector.SystemEssential,
            Summary = "Manifiesto del nodo: hardware, número de serie, drivers y versiones.",
            Route = ApiRoutes.Manifest, Method = CommandMethod.Get,
        },
        new()
        {
            Name = "disks", ModuleId = "M9", Sector = Sector.Storage,
            Summary = "Espacio libre y estado de las unidades.",
            Route = ApiRoutes.Storage, Method = CommandMethod.Get,
        },
        new()
        {
            Name = "preshow", ModuleId = "M11", Sector = Sector.Production,
            Summary = "Checklist pre-show completa con veredicto verde o rojo.",
            Route = ApiRoutes.ReportPreShow, Method = CommandMethod.Post,
        },
        new()
        {
            Name = "timeline", ModuleId = "M11", Sector = Sector.Production,
            Summary = "Log correlado de todos los módulos en una sola línea temporal.",
            Usage = "timeline [minutos]",
            Route = ApiRoutes.Timeline, Method = CommandMethod.Get,
        },
        new()
        {
            Name = "panic", ModuleId = "M15", Sector = Sector.Production,
            Summary = "Acciones de pánico: restaurar pantallas, ventanas, motor de audio.",
            Usage = "panic <acción>",
            Route = ApiRoutes.Panic, Method = CommandMethod.Post,
        },
        new()
        {
            Name = "profile", ModuleId = "M10", Sector = Sector.Production,
            Summary = "Aplica un perfil de escenario. Siempre enseña el diff antes.",
            Usage = "profile <nombre> [--apply]",
            Route = ApiRoutes.ProfileApply, Method = CommandMethod.Post,
        },
    };

    /// <summary>Busca por nombre o alias, sin distinguir mayusculas.</summary>
    public static CommandDescriptor? Find(string nameOrAlias)
    {
        foreach (CommandDescriptor command in All)
        {
            if (string.Equals(command.Name, nameOrAlias, StringComparison.OrdinalIgnoreCase))
            {
                return command;
            }

            foreach (string alias in command.Aliases)
            {
                if (string.Equals(alias, nameOrAlias, StringComparison.OrdinalIgnoreCase))
                {
                    return command;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Filtro para la paleta: coincidencia por prefijo primero y por subcadena despues,
    /// que es como se comporta cualquier buscador que no estorba.
    /// </summary>
    public static IEnumerable<CommandDescriptor> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return All;
        }

        string q = query.Trim();

        return All
            .Select(c => (Command: c, Score: Score(c, q)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Command.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Command);
    }

    private static int Score(CommandDescriptor command, string query)
    {
        if (command.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
        {
            return 100;
        }

        foreach (string alias in command.Aliases)
        {
            if (alias.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            {
                return 90;
            }
        }

        if (command.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return 50;
        }

        return command.Summary.Contains(query, StringComparison.OrdinalIgnoreCase) ? 10 : 0;
    }
}
