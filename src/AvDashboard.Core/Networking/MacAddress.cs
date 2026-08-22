using System.Globalization;

namespace AvDashboard.Core.Networking;

/// <summary>
/// Normalizacion de direcciones MAC. Cada API de Windows las devuelve en un formato
/// distinto (con guiones, con dos puntos, pegadas) y compararlas a pelo es una fuente
/// silenciosa de duplicados en la tabla de dispositivos.
/// </summary>
public static class MacAddress
{
    /// <summary>Devuelve la MAC en mayusculas y sin separadores, o null si no es valida.</summary>
    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        Span<char> buffer = stackalloc char[12];
        int written = 0;

        foreach (char c in raw)
        {
            if (c is '-' or ':' or '.' or ' ')
            {
                continue;
            }

            if (!Uri.IsHexDigit(c))
            {
                return null;
            }

            if (written == 12)
            {
                return null;
            }

            buffer[written++] = char.ToUpperInvariant(c);
        }

        return written == 12 ? new string(buffer) : null;
    }

    /// <summary>Formato legible con dos puntos: "00:1D:C1:2A:3B:4C".</summary>
    public static string? Format(string? raw)
    {
        string? normalized = Normalize(raw);
        if (normalized is null)
        {
            return null;
        }

        return string.Create(17, normalized, static (span, source) =>
        {
            int target = 0;
            for (int i = 0; i < 12; i += 2)
            {
                if (i > 0)
                {
                    span[target++] = ':';
                }

                span[target++] = source[i];
                span[target++] = source[i + 1];
            }
        });
    }

    /// <summary>
    /// true si el bit de administracion local esta puesto. Estas MAC no tienen OUI
    /// que buscar: son de maquinas virtuales, de contenedores o aleatorizadas.
    /// </summary>
    public static bool IsLocallyAdministered(string? raw)
    {
        string? normalized = Normalize(raw);
        if (normalized is null)
        {
            return false;
        }

        byte firstOctet = byte.Parse(normalized.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return (firstOctet & 0x02) != 0;
    }

    /// <summary>true si es una MAC multicast (bit menos significativo del primer octeto).</summary>
    public static bool IsMulticast(string? raw)
    {
        string? normalized = Normalize(raw);
        if (normalized is null)
        {
            return false;
        }

        byte firstOctet = byte.Parse(normalized.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return (firstOctet & 0x01) != 0;
    }
}
