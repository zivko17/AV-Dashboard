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
