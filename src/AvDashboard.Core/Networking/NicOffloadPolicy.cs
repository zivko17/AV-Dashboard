namespace AvDashboard.Core.Networking;

/// <summary>Una propiedad avanzada de un adaptador, tal y como la publica el driver.</summary>
public readonly record struct NicAdvancedProperty(string Adapter, string Keyword, string? Value);

/// <summary>
/// Una descarga que hay que apagar, y por qué.
///
/// El motivo no es documentación de adorno: es lo que se enseña en la interfaz cuando
/// alguien pregunta por qué la aplicación quiere tocar algo del driver de red.
/// </summary>
public sealed record NicOffloadRule(string Keyword, string Reason);

/// <summary>
/// Qué descargas del adaptador de red hay que apagar en un rig de AV.
///
/// Vive en Core, separada de la lectura por WMI y de la escritura, por dos razones. La
/// primera es que la lista de qué se apaga es una decisión y no un detalle de interop: es
/// lo que distingue esta aplicación de un debloater. La segunda es que la lectura y la
/// escritura tienen que usar exactamente la misma lista, y con la lista escrita dos veces
/// se separan a la primera vez que alguien añada una descarga a una sola de ellas: la fila
/// diría «hay tres adaptadores mal» y el botón de arreglarlo dejaría uno sin tocar.
/// </summary>
public static class NicOffloadPolicy
{
    /// <summary>
    /// Se compara por prefijo porque cada fabricante añade sufijos a la misma descarga:
    /// `*LsoV2IPv4` y `*LsoV2IPv6` son las dos caras de Large Send Offload.
    /// </summary>
    public static IReadOnlyList<NicOffloadRule> Rules { get; } = new NicOffloadRule[]
    {
        new("*LsoV2",
            "Large Send Offload junta paquetes para que los trocee la tarjeta. En una red " +
            "con Dante eso mete ráfagas donde debería haber un flujo regular."),

        new("*InterruptModeration",
            "La moderación de interrupciones agrupa avisos al sistema para gastar menos CPU. " +
            "Lo que ahorra en CPU lo paga en jitter, y el jitter es exactamente lo que " +
            "rompe la temporización de Dante y de NDI."),

        new("*EEE",
            "Energy Efficient Ethernet duerme el enlace entre paquetes. Despertarlo cuesta " +
            "milisegundos que el audio en red no tiene."),

        new("*FlowControl",
            "El control de flujo deja que el switch te mande pausas. En un flujo isócrono " +
            "una pausa no se recupera: se oye."),

        new("*RSC",
            "Receive Segment Coalescing junta lo que llega antes de entregarlo. Mismo " +
            "problema que LSO pero en el otro sentido."),
    };

    /// <summary>El valor que debe tener una descarga apagada, tal y como lo guarda el driver.</summary>
    public const string Disabled = "0";

    /// <summary>La regla que aplica a esta propiedad, o null si no es de las que se tocan.</summary>
    public static NicOffloadRule? Match(string? keyword) =>
        keyword is null
            ? null
            : Rules.FirstOrDefault(r => keyword.StartsWith(r.Keyword, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Si esta propiedad está encendida y hay que apagarla.
    ///
    /// Un valor que no se ha podido leer no cuenta como infractor: no saber no es lo mismo
    /// que estar mal, y pintar en rojo lo que no se ha leído llena la pantalla de falsos
    /// positivos hasta que dejas de mirar los verdaderos.
    /// </summary>
    public static bool IsOffending(NicAdvancedProperty property) =>
        Match(property.Keyword) is not null
        && property.Value is not null
        && !string.Equals(property.Value, Disabled, StringComparison.Ordinal);

    /// <summary>
    /// Lo que habría que tocar, a partir de lo que publica el sistema.
    ///
    /// Lo usan la sonda —para contar adaptadores con algo puesto— y el actuador —para saber
    /// qué escribir—. Es la única forma de que las dos no se separen.
    /// </summary>
    public static NicOffloadPlan Plan(IEnumerable<NicAdvancedProperty> properties)
    {
        ArgumentNullException.ThrowIfNull(properties);

        List<NicAdvancedProperty> offending = properties.Where(IsOffending).ToList();

        return new NicOffloadPlan
        {
            Offending = offending,
            Adapters = offending
                .Select(p => p.Adapter)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(a => a, StringComparer.CurrentCulture)
                .ToList(),
        };
    }
}

/// <summary>Lo que hay que apagar en los adaptadores de esta máquina.</summary>
public sealed record NicOffloadPlan
{
    public IReadOnlyList<NicAdvancedProperty> Offending { get; init; } =
        Array.Empty<NicAdvancedProperty>();

    /// <summary>Adaptadores con algo puesto, sin repetir y en orden.</summary>
    public IReadOnlyList<string> Adapters { get; init; } = Array.Empty<string>();

    public bool IsClean => Offending.Count == 0;

    /// <summary>
    /// Lo que se lee en la fila: los adaptadores afectados, no las propiedades.
    ///
    /// Enseñar «7 propiedades» no le dice nada a nadie; «Ethernet, Ethernet 2» sí, porque es
    /// donde hay que ir a mirar. Se cortan a tres porque el ancho de la columna es el que es.
    /// </summary>
    public string Describe() => IsClean
        ? "ninguno"
        : string.Join(", ", Adapters.Take(3)) + (Adapters.Count > 3 ? "…" : string.Empty);
}
