using AvDashboard.Core.Models;
using AvDashboard.Core.Networking;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AvDashboard.Client.ViewModels;

/// <summary>
/// Un equipo en la tabla del inventario de red.
///
/// Lleva sus propios comandos y no es una decisión de estilo: en WinUI no hay
/// <c>RelativeSource AncestorType</c>, así que un botón de fila no puede alcanzar el
/// comando del ViewModel de la página. Viven aquí y las dos interfaces los usan igual.
/// </summary>
public sealed partial class DeviceRow : ObservableObject
{
    private readonly Func<DeviceRow, Task> _save;
    private readonly Func<DeviceRow, Task> _remove;

    public DeviceRow(
        DiscoveredDevice device,
        PingStatistics? ping,
        DateTimeOffset? lastScanAt,
        DateTimeOffset now,
        Func<DeviceRow, Task> save,
        Func<DeviceRow, Task> remove)
    {
        Device = device;
        _save = save;
        _remove = remove;

        _alias = device.Alias ?? string.Empty;
        _isCritical = device.IsCritical;

        State = DeviceSeenStates.For(device, ping, lastScanAt);
        IsNew = DeviceSeenStates.IsNewToday(device, now);
        SeenText = Describe(State, device, lastScanAt);
    }

    public DiscoveredDevice Device { get; }

    public string IpAddress => Device.IpAddress;
    public string? MacAddress => Device.MacAddress;
    public string? Vendor => Device.Vendor;

    /// <summary>Lo que se lee cuando no le has puesto nombre todavía.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Alias) ? Device.DisplayName : Alias;

    public DeviceSeenState State { get; }

    /// <summary>
    /// Marca de equipo visto por primera vez hoy.
    ///
    /// Va aparte del estado y no como un valor más suyo: mezclada con los demás taparía al
    /// de responder justo cuando ese es el que hace falta leer.
    /// </summary>
    public bool IsNew { get; }

    public string SeenText { get; }

    /// <summary>
    /// Salud de la fila, para que la pinte el conversor que ya usan las dos interfaces.
    ///
    /// Solo hay rojo cuando de verdad no contesta algo que vigilas. Un equipo sin ★ nunca
    /// sale en rojo: no se le hace ping, asi que no hay nada que declarar sobre el.
    /// </summary>
    public HealthState Health => State switch
    {
        DeviceSeenState.Responding => HealthState.Ok,
        DeviceSeenState.WentQuiet or DeviceSeenState.NeverAnswered => HealthState.Critical,
        _ => HealthState.Unknown,
    };

    /// <summary>
    /// La estrella, como texto y no como conversor.
    ///
    /// En WinUI los enlaces no tienen StringFormat y un conversor booleano-a-glifo habria
    /// que declararlo en las dos interfaces. Sale de aqui por lo mismo que el resto de las
    /// filas de formato.
    /// </summary>
    public string StarGlyph => IsCritical ? "★" : "☆";

    [ObservableProperty]
    private string _alias;

    [ObservableProperty]
    private bool _isCritical;

    /// <summary>Marcar y desmarcar. Es lo que mete o saca al equipo del ping continuo.</summary>
    [RelayCommand]
    private Task ToggleCriticalAsync()
    {
        IsCritical = !IsCritical;
        return _save(this);
    }

    /// <summary>Guarda el alias tal cual está escrito ahora en la fila.</summary>
    [RelayCommand]
    private Task SaveAliasAsync() => _save(this);

    [RelayCommand]
    private Task RemoveAsync() => _remove(this);

    /// <summary>
    /// El texto de la columna «visto», que dice lo que se sabe y ni una palabra más.
    ///
    /// Un equipo sin vigilar nunca dice cuánto lleva sin verse: no hay barrido automático de
    /// fondo, así que lo único que consta es lo que dijo el último escaneo.
    /// </summary>
    private static string Describe(
        DeviceSeenState state, DiscoveredDevice device, DateTimeOffset? lastScanAt) => state switch
        {
            DeviceSeenState.Measuring => "midiendo…",
            DeviceSeenState.Responding => "responde",
            DeviceSeenState.WentQuiet => $"sin responder desde las {device.LastSeen.ToLocalTime():HH:mm}",
            DeviceSeenState.NeverAnswered => "no responde desde que se añadió",
            DeviceSeenState.NeverSeen => "añadido a mano, sin confirmar",

            // El caso sin hora no se puede dar: SeenInLastScan solo sale cuando hay un
            // barrido con el que comparar. Se deja como salida por defecto del patrón, no
            // como una posibilidad real; si algún día aparece en pantalla, es que
            // DeviceSeenStates ha cambiado y hay que mirar allí y no aquí.
            DeviceSeenState.SeenInLastScan => lastScanAt is DateTimeOffset scan
                ? $"visto en el escaneo de las {scan.ToLocalTime():HH:mm}"
                : "visto en el último escaneo",

            DeviceSeenState.SeenEarlier => $"visto por última vez el {device.LastSeen.ToLocalTime():d MMM}",

            _ => "",
        };
}
