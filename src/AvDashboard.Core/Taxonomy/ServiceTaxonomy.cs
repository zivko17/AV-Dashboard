namespace AvDashboard.Core.Taxonomy;

/// <summary>
/// Clasificacion de servicios de Windows por sector AV. La semilla viene de la lista
/// de `services-audio` de av-light-tools y se amplia con lo que aparece en cada rig.
///
/// La clasificacion del usuario tiene prioridad sobre la semilla y es persistente:
/// mueves un servicio de "Sin clasificar" a un sector y se queda ahi para siempre,
/// en todos los nodos.
/// </summary>
public sealed class ServiceTaxonomy
{
    private static readonly IReadOnlyDictionary<string, Sector> Seed =
        new Dictionary<string, Sector>(StringComparer.OrdinalIgnoreCase)
        {
            // --- Audio ---
            ["Audiosrv"] = Sector.Audio,
            ["AudioEndpointBuilder"] = Sector.Audio,
            ["Dante Virtual Soundcard"] = Sector.Audio,
            ["DanteVirtualSoundcard"] = Sector.Audio,
            ["Dante Discovery"] = Sector.Audio,
            ["DanteDiscovery"] = Sector.Audio,
            ["Dante Control and Monitoring"] = Sector.Audio,
            ["RtkAudioUniversalService"] = Sector.Audio,
            ["FocusriteUSBAudioDriverService"] = Sector.Audio,
            ["RMEUSBAudioService"] = Sector.Audio,
            ["MOTU Audio Service"] = Sector.Audio,
            ["Voicemeeter"] = Sector.Audio,

            // --- Video ---
            ["Blackmagic Desktop Video"] = Sector.Video,
            ["BlackmagicDesktopVideoHelper"] = Sector.Video,
            ["NDI Discovery Service"] = Sector.Video,
            ["NDIDiscoveryService"] = Sector.Video,
            ["NVDisplay.ContainerLocalSystem"] = Sector.Video,
            ["NvContainerLocalSystem"] = Sector.Video,
            ["NvContainerNetworkService"] = Sector.Video,
            ["AMD External Events Utility"] = Sector.Video,
            ["Intel(R) Graphics Command Center Service"] = Sector.Video,

            // --- Red ---
            ["Tailscale"] = Sector.Network,
            ["Dhcp"] = Sector.Network,
            ["Dnscache"] = Sector.Network,
            ["SSDPSRV"] = Sector.Network,
            ["FDResPub"] = Sector.Network,
            ["fdPHost"] = Sector.Network,
            ["Bonjour Service"] = Sector.Network,
            ["mDNSResponder"] = Sector.Network,
            ["NlaSvc"] = Sector.Network,
            ["netprofm"] = Sector.Network,
            ["npcap"] = Sector.Network,
            ["WlanSvc"] = Sector.Network,

            // --- Produccion ---
            ["companion"] = Sector.Production,
            ["Bitfocus Companion"] = Sector.Production,
            ["WATCHOUT"] = Sector.Production,
            ["Sentinel LDK License Manager"] = Sector.Production,
            ["hasplms"] = Sector.Production,
            ["CodeMeter.exe"] = Sector.Production,
            ["FlexNet Licensing Service"] = Sector.Production,

            // --- Sistema: candidatos a desactivar en un rig de directo ---
            ["wuauserv"] = Sector.SystemCandidate,
            ["UsoSvc"] = Sector.SystemCandidate,
            ["WaaSMedicSvc"] = Sector.SystemCandidate,
            ["SysMain"] = Sector.SystemCandidate,
            ["Spooler"] = Sector.SystemCandidate,
            ["WSearch"] = Sector.SystemCandidate,
            ["XblAuthManager"] = Sector.SystemCandidate,
            ["XblGameSave"] = Sector.SystemCandidate,
            ["XboxGipSvc"] = Sector.SystemCandidate,
            ["XboxNetApiSvc"] = Sector.SystemCandidate,
            ["DiagTrack"] = Sector.SystemCandidate,
            ["dmwappushservice"] = Sector.SystemCandidate,
            ["MapsBroker"] = Sector.SystemCandidate,
            ["RetailDemo"] = Sector.SystemCandidate,

            // --- Sistema esencial: se listan para que no aparezcan como candidatos ---
            ["RpcSs"] = Sector.SystemEssential,
            ["DcomLaunch"] = Sector.SystemEssential,
            ["LSM"] = Sector.SystemEssential,
            ["Power"] = Sector.SystemEssential,
            ["PlugPlay"] = Sector.SystemEssential,
            ["Winmgmt"] = Sector.SystemEssential,
            ["EventLog"] = Sector.SystemEssential,
            ["Schedule"] = Sector.SystemEssential,

            // --- Almacenamiento ---
            ["vds"] = Sector.Storage,
            ["defragsvc"] = Sector.Storage,
        };

    private readonly Dictionary<string, Sector> _overrides;

    public ServiceTaxonomy(IEnumerable<KeyValuePair<string, Sector>>? userOverrides = null)
    {
        _overrides = userOverrides is null
            ? new Dictionary<string, Sector>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, Sector>(userOverrides, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Sector de un servicio, con la clasificacion del usuario por delante de la semilla.</summary>
    public Sector Classify(string serviceName, string? displayName = null)
    {
        if (_overrides.TryGetValue(serviceName, out Sector overridden))
        {
            return overridden;
        }

        if (Seed.TryGetValue(serviceName, out Sector seeded))
        {
            return seeded;
        }

        if (displayName is not null)
        {
            if (_overrides.TryGetValue(displayName, out Sector displayOverride))
            {
                return displayOverride;
            }

            if (Seed.TryGetValue(displayName, out Sector displaySeeded))
            {
                return displaySeeded;
            }

            // Ultimo recurso: coincidencia por palabra clave en el nombre visible.
            // Deliberadamente conservadora; ante la duda se queda sin clasificar y
            // ya la mueves tu a mano una vez.
            Sector? guessed = GuessFromDisplayName(displayName);
            if (guessed is Sector g)
            {
                return g;
            }
        }

        return Sector.Unclassified;
    }

    private static Sector? GuessFromDisplayName(string displayName)
    {
        if (ContainsAny(displayName, "dante", "asio", "audio"))
        {
            return Sector.Audio;
        }

        if (ContainsAny(displayName, "blackmagic", "decklink", "ndi", "nvidia"))
        {
            return Sector.Video;
        }

        if (ContainsAny(displayName, "tailscale", "bonjour", "npcap"))
        {
            return Sector.Network;
        }

        return null;
    }

    private static bool ContainsAny(string haystack, params string[] needles)
    {
        foreach (string needle in needles)
        {
            if (haystack.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Registra o cambia la clasificacion manual de un servicio.</summary>
    public void Assign(string serviceName, Sector sector) => _overrides[serviceName] = sector;

    /// <summary>Clasificaciones manuales, para persistirlas.</summary>
    public IReadOnlyDictionary<string, Sector> Overrides => _overrides;

    /// <summary>Servicios de la semilla que se esperan en marcha en un rig de directo.</summary>
    public static IReadOnlyList<string> DefaultExpectedRunning { get; } = new[]
    {
        "Audiosrv",
        "AudioEndpointBuilder",
        "Dnscache",
        "Winmgmt",
    };
}
