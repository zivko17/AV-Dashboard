namespace AvDashboard.Core.Taxonomy;

/// <summary>
/// El sector es un concepto transversal: la misma taxonomia clasifica servicios,
/// procesos, reglas de firewall y entradas del Timeline. Se define una vez, aqui.
/// </summary>
public enum Sector
{
    Unclassified = 0,
    Audio = 1,
    Video = 2,
    Network = 3,
    Production = 4,

    /// <summary>Sistema: candidatos a desactivar. Windows Update, SysMain, Spooler, Search, Xbox.</summary>
    SystemCandidate = 5,

    /// <summary>Sistema esencial. Nunca se ofrece desactivarlo sin confirmacion explicita.</summary>
    SystemEssential = 6,

    Storage = 7,
    Power = 8,
}

public static class SectorNames
{
    /// <summary>Etiqueta en castellano para la UI. La consola se usa en espanol.</summary>
    public static string Display(Sector sector) => sector switch
    {
        Sector.Audio => "Audio",
        Sector.Video => "Vídeo",
        Sector.Network => "Red",
        Sector.Production => "Producción",
        Sector.SystemCandidate => "Sistema — candidatos a desactivar",
        Sector.SystemEssential => "Sistema — esencial",
        Sector.Storage => "Almacenamiento",
        Sector.Power => "Alimentación",
        _ => "Sin clasificar",
    };

    /// <summary>Orden de presentacion en la vista de servicios. Lo que mas se mira, arriba.</summary>
    public static readonly IReadOnlyList<Sector> DisplayOrder = new[]
    {
        Sector.Audio,
        Sector.Video,
        Sector.Network,
        Sector.Production,
        Sector.Storage,
        Sector.Power,
        Sector.SystemCandidate,
        Sector.SystemEssential,
        Sector.Unclassified,
    };
}
