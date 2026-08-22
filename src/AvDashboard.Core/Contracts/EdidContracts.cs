using System.Text;
using AvDashboard.Core.Models;
using AvDashboard.Core.Video;

namespace AvDashboard.Core.Contracts;

/// <summary>
/// Identificador de una entrada de la biblioteca de EDID.
///
/// Se codifica porque el identificador de instancia de Windows lleva ampersands y
/// barras, que en una URL obligan a escapar dos veces y acaban rompiendose por el camino.
/// </summary>
public static class EdidEntryId
{
    public static string Encode(string hardwareId, string instanceId) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{hardwareId}\\{instanceId}"))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

    public static bool TryDecode(string? id, out string hardwareId, out string instanceId)
    {
        hardwareId = string.Empty;
        instanceId = string.Empty;

        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        string padded = id.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch { 2 => "==", 3 => "=", _ => string.Empty };

        try
        {
            string decoded = Encoding.UTF8.GetString(Convert.FromBase64String(padded));
            int separator = decoded.IndexOf('\\');

            if (separator <= 0 || separator == decoded.Length - 1)
            {
                return false;
            }

            hardwareId = decoded[..separator];
            instanceId = decoded[(separator + 1)..];
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

/// <summary>
/// Clave del conector fisico por el que sale una senal.
///
/// La referencia de EDID se guarda contra el conector y no contra el monitor, y esa es
/// justo la diferencia que hace util la funcion: si guardaras la referencia contra el
/// monitor, cambiar el splitter por otro no dispararia nada porque el monitor nuevo se
/// comparia consigo mismo. Guardandola contra el puerto, la pregunta que se contesta es
/// «por esta salida sigue estando lo mismo que el dia que funcionaba», que es la que
/// importa.
///
/// Windows codifica el puerto en el identificador de instancia como un token UID:
/// <c>5&amp;1a2b3c&amp;0&amp;UID4353</c>. Cuando no aparece se cae al identificador entero,
/// que al menos es estable mientras no se toque el cableado.
/// </summary>
public static class EdidConnectorKey
{
    public static string FromInstanceId(string? instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            return string.Empty;
        }

        foreach (string token in instanceId.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.StartsWith("UID", StringComparison.OrdinalIgnoreCase))
            {
                return token.ToUpperInvariant();
            }
        }

        return instanceId.ToUpperInvariant();
    }

    /// <summary>La misma clave a partir de la ruta de dispositivo de QueryDisplayConfig.</summary>
    public static string FromDevicePath(string? monitorDevicePath)
    {
        if (string.IsNullOrWhiteSpace(monitorDevicePath))
        {
            return string.Empty;
        }

        string[] parts = monitorDevicePath.Split('#');
        return parts.Length < 3 ? string.Empty : FromInstanceId(parts[2]);
    }
}

/// <summary>
/// Un EDID guardado como referencia, con el nombre que le pones tu: «VX600 rack B»,
/// «Proyector sala 2», «Splitter PureTools».
/// </summary>
public sealed record EdidReference
{
    /// <summary>Conector contra el que se guarda. Ver <see cref="EdidConnectorKey"/>.</summary>
    public required string ConnectorKey { get; init; }

    public required string Name { get; init; }
    public required string Fingerprint { get; init; }
    public required int ByteCount { get; init; }
    public DateTimeOffset SavedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Nombre GDI de la salida el dia que se guardo, solo como pista.</summary>
    public string? DeviceName { get; init; }
}

public sealed record EdidReferenceListResponse
{
    public IReadOnlyList<EdidReference> References { get; init; } = Array.Empty<EdidReference>();

    /// <summary>Salidas activas cuyo EDID no coincide con la referencia guardada.</summary>
    public IReadOnlyList<EdidDrift> Drift { get; init; } = Array.Empty<EdidDrift>();
}

/// <summary>Una salida cuyo EDID ha cambiado respecto a la referencia.</summary>
public sealed record EdidDrift
{
    public required string ConnectorKey { get; init; }
    public required string ReferenceName { get; init; }
    public required string ExpectedFingerprint { get; init; }
    public required string ActualFingerprint { get; init; }
    public string? DeviceName { get; init; }
    public string? MonitorName { get; init; }
}

/// <summary>Peticion de guardar una referencia.</summary>
public sealed record SaveEdidReferenceRequest
{
    /// <summary>Identificador de la entrada de la biblioteca, de `edid-list`.</summary>
    public required string Id { get; init; }

    /// <summary>El nombre con el que lo vas a reconocer dentro de seis meses.</summary>
    public required string Name { get; init; }
}

/// <summary>Una entrada de la biblioteca de EDID.</summary>
public sealed record EdidEntry
{
    public required string Id { get; init; }
    public required string HardwareId { get; init; }
    public required string InstanceId { get; init; }

    /// <summary>Huella corta. Si cambia respecto a la de referencia, alguien tocó algo.</summary>
    public required string Fingerprint { get; init; }

    /// <summary>true si esta salida está activa ahora mismo.</summary>
    public bool Connected { get; init; }

    /// <summary>Nombre GDI de la salida por la que sale, si está conectada.</summary>
    public string? DeviceName { get; init; }

    public int ByteCount { get; init; }

    public Edid? Parsed { get; init; }
    public IReadOnlyList<EdidFinding> Findings { get; init; } = Array.Empty<EdidFinding>();
    public HealthState Health { get; init; } = HealthState.Unknown;

    public string Label => Parsed?.DisplayLabel ?? HardwareId;
}

public sealed record EdidListResponse
{
    public IReadOnlyList<EdidEntry> Entries { get; init; } = Array.Empty<EdidEntry>();

    /// <summary>Motivo por el que la lista está vacía, si lo está.</summary>
    public string? UnavailableReason { get; init; }
}

public sealed record EdidRawResponse
{
    public required string Id { get; init; }
    public required string Base64 { get; init; }
    public required int ByteCount { get; init; }

    /// <summary>Nombre sugerido para el fichero, con el modelo dentro.</summary>
    public required string SuggestedFileName { get; init; }
}

public sealed record EdidDiffResponse
{
    public required string LeftId { get; init; }
    public required string RightId { get; init; }
    public string? LeftLabel { get; init; }
    public string? RightLabel { get; init; }
    public IReadOnlyList<EdidDifference> Differences { get; init; } = Array.Empty<EdidDifference>();

    public bool Identical => Differences.Count == 0;
    public bool HasSignificantDifferences => Differences.Any(d => d.Significant);
}
