using System.Globalization;

namespace AvDashboard.Core.Settings;

/// <summary>Cómo está dimensionado el archivo de paginación.</summary>
public enum PagingFileMode
{
    /// <summary>No se ha podido leer la lista. No es lo mismo que no haber archivo.</summary>
    Unknown,

    /// <summary>Sin archivo de paginación en ningún disco.</summary>
    Disabled,

    /// <summary>Windows decide el tamaño, y por tanto puede cambiarlo mientras corre el show.</summary>
    SystemManaged,

    /// <summary>Mínimo y máximo iguales: no crece nunca.</summary>
    Fixed,

    /// <summary>Tiene mínimo, pero puede crecer hasta el máximo.</summary>
    Growing,

    /// <summary>Varios discos y no todos igual.</summary>
    Mixed,
}

/// <summary>Una línea de <c>PagingFiles</c>: un archivo con su mínimo y su máximo en MB.</summary>
public sealed record PagingFileEntry(string Path, int InitialMb, int MaximumMb)
{
    /// <summary>
    /// La entrada comodín que escribe Windows cuando gestiona el tamaño él solo.
    ///
    /// Es literalmente <c>?:\pagefile.sys</c>: el interrogante significa «el disco que yo
    /// decida». Esa entrada **es** la casilla «administrar automáticamente» del diálogo de
    /// memoria virtual, no un reflejo de ella.
    /// </summary>
    public bool IsAutomatic => Path.StartsWith("?:", StringComparison.Ordinal);

    /// <summary>Un archivo concreto pero con Windows eligiendo el tamaño: se escribe «0 0».</summary>
    public bool IsSystemManaged => IsAutomatic || (InitialMb == 0 && MaximumMb == 0);

    public bool IsFixed => !IsAutomatic && InitialMb > 0 && InitialMb == MaximumMb;

    /// <summary>La letra del disco, para decir dónde está sin repetir la ruta entera.</summary>
    public string Drive => Path.Length >= 2 && Path[1] == ':'
        ? Path[..2].ToUpperInvariant()
        : Path;

    /// <summary>La línea tal y como la guarda el registro.</summary>
    public override string ToString() => IsAutomatic
        ? Path
        : string.Create(CultureInfo.InvariantCulture, $"{Path} {InitialMb} {MaximumMb}");
}

/// <summary>
/// El archivo de paginación, leído de <c>PagingFiles</c> en Memory Management.
///
/// La pregunta que contesta esta clase no es «cuánta paginación hace falta» —con 64 GB casi
/// ninguna— sino **cuándo se reserva el espacio**. Un archivo gestionado por Windows arranca
/// pequeño y crece cuando la carga comprometida sube: reservar gigas en disco es una ráfaga
/// de escritura, y ocurre justo en el momento en que la carga sube, que en un rig es el
/// momento en que se lanza la primera capa de vídeo. Con mínimo y máximo iguales el espacio
/// ya está reservado desde el arranque y ese pico no existe.
///
/// El otro extremo tampoco vale. Desactivarlo «porque hay RAM de sobra» ignora que Windows
/// contabiliza memoria *comprometida*, no usada: Resolume y vMix reservan mucho más de lo
/// que tocan, y sin archivo el límite se alcanza antes de llenar la RAM y las aplicaciones
/// empiezan a fallar al reservar. Además se pierde el volcado si algo revienta, que es lo
/// único que queda para saber qué pasó al día siguiente.
///
/// El parseo vive en Core porque el formato tiene trampas —la ruta puede llevar espacios y
/// los números van al final— y porque así se prueba sin tocar el registro.
/// </summary>
public static class PagingFile
{
    /// <summary>Valor canónico de cada modo. Es contra esto que compara el catálogo.</summary>
    public static string Canonical(PagingFileMode mode) => mode switch
    {
        PagingFileMode.Disabled => "desactivado",
        PagingFileMode.SystemManaged => "gestionado",
        PagingFileMode.Fixed => "fijo",
        PagingFileMode.Growing => "creciente",
        PagingFileMode.Mixed => "mixto",
        _ => "desconocido",
    };

    /// <summary>El único modo que se acepta en un rig.</summary>
    public const string Target = "fijo";

    /// <summary>Volver a dejárselo a Windows. Es el camino de vuelta y no rompe nada.</summary>
    public const string SystemManagedValue = "gestionado";

    /// <summary>
    /// Las entradas de la lista, ignorando las líneas que no lo son.
    ///
    /// Los números se leen **por el final**, no partiendo por espacios: una ruta puede
    /// llevarlos (<c>D:\Archivos de paginación\pagefile.sys</c>) y partir por el primero
    /// convertiría la ruta en basura y los tamaños en cero, que es justo el modo que esta
    /// clase intenta detectar. Con dos números al final, la ruta es todo lo demás.
    /// </summary>
    public static IReadOnlyList<PagingFileEntry> Parse(IEnumerable<string>? lines)
    {
        if (lines is null)
        {
            return Array.Empty<PagingFileEntry>();
        }

        List<PagingFileEntry> entries = new();

        foreach (string line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            string trimmed = line.Trim();
            string[] parts = trimmed.Split(
                ' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (parts.Length >= 3
                && int.TryParse(parts[^2], NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out int initial)
                && int.TryParse(parts[^1], NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out int maximum))
            {
                entries.Add(new PagingFileEntry(
                    string.Join(' ', parts[..^2]), Math.Max(0, initial), Math.Max(0, maximum)));
                continue;
            }

            // Sin números: la línea es solo la ruta, que es como Windows escribe el comodín.
            entries.Add(new PagingFileEntry(trimmed, 0, 0));
        }

        return entries;
    }

    /// <summary>
    /// El modo del conjunto.
    ///
    /// Una lista vacía es «desactivado» de verdad: el valor existe y está a cero, que es lo
    /// que escribe el diálogo al marcar «sin archivo de paginación». Que el valor **no
    /// exista** es otra cosa, y eso lo distingue la sonda antes de llamar aquí.
    /// </summary>
    public static PagingFileMode Classify(IReadOnlyList<PagingFileEntry> entries)
    {
        if (entries.Count == 0)
        {
            return PagingFileMode.Disabled;
        }

        PagingFileMode? common = null;

        foreach (PagingFileEntry entry in entries)
        {
            PagingFileMode mode = entry.IsSystemManaged
                ? PagingFileMode.SystemManaged
                : entry.IsFixed
                    ? PagingFileMode.Fixed
                    : PagingFileMode.Growing;

            if (common is null)
            {
                common = mode;
            }
            else if (common != mode)
            {
                return PagingFileMode.Mixed;
            }
        }

        return common ?? PagingFileMode.Disabled;
    }

    /// <summary>Lo mismo, desde las líneas crudas.</summary>
    public static PagingFileMode Classify(IEnumerable<string>? lines) =>
        Classify(Parse(lines));

    /// <summary>
    /// Qué se enseña en la fila.
    ///
    /// El modo solo dice que está mal; esto dice en qué disco y con qué tamaños, que es lo
    /// que hace falta para decidir si merece la pena reiniciar antes del show o después.
    /// </summary>
    public static string Describe(IReadOnlyList<PagingFileEntry> entries)
    {
        if (entries.Count == 0)
        {
            return "Sin archivo de paginación";
        }

        return string.Join(", ", entries.Select(entry =>
            entry.IsAutomatic ? "gestionado por Windows, disco a su elección"
            : entry.IsSystemManaged ? $"{entry.Drive} gestionado por Windows"
            : entry.IsFixed ? $"{entry.Drive} fijo en {Size(entry.InitialMb)}"
            : $"{entry.Drive} de {Size(entry.InitialMb)} a {Size(entry.MaximumMb)}"));
    }

    /// <summary>
    /// El tamaño que se propone, en MB, o null si el disco no da para uno decente.
    ///
    /// Un cuarto de la RAM, con suelo de 8 GB y techo de 32. La regla vieja de «vez y media
    /// la RAM» viene de cuando había 2 GB y no tiene sentido con 64: reservaría 96 GB de
    /// disco para respaldar una carga comprometida que nunca llega. Lo que sí importa es que
    /// el número no sea ridículo, porque un archivo pequeño obliga a Windows a rechazar
    /// reservas cuando una aplicación pide de golpe, y ahí sí se nota.
    ///
    /// Y no se ocupa más de un quinto de lo libre. En un servidor de medios el disco es la
    /// materia prima: quitarle 32 GB a una unidad que va justa cambia un problema por otro.
    /// </summary>
    public static int? RecommendMb(long ramBytes, long freeBytes)
    {
        if (ramBytes <= 0)
        {
            return null;
        }

        const int megabyte = 1024;
        long ramMb = ramBytes / (1024 * 1024);

        long proposed = Math.Clamp(ramMb / 4, 8L * megabyte, 32L * megabyte);
        long roomMb = freeBytes / (1024 * 1024) / 5;

        proposed = Math.Min(proposed, roomMb);

        // Redondeado a gigas enteros: los tamaños que enseña Windows lo están, y un
        // «15,4 GB» en la fila invita a preguntarse de dónde sale.
        proposed -= proposed % megabyte;

        // Por debajo de 4 GB el problema no es la paginación, es que el disco está lleno.
        // Proponer 2 GB sería contestar a la pregunta equivocada.
        return proposed >= 4L * megabyte ? (int)proposed : null;
    }

    private static string Size(int megabytes) =>
        megabytes % 1024 == 0
            ? string.Create(CultureInfo.InvariantCulture, $"{megabytes / 1024} GB")
            : string.Create(CultureInfo.InvariantCulture, $"{megabytes} MB");
}
