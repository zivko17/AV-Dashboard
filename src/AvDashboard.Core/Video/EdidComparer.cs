using AvDashboard.Core.Models;

namespace AvDashboard.Core.Video;

/// <summary>Una diferencia entre dos EDID.</summary>
public sealed record EdidDifference
{
    /// <summary>Campo comparado, en lenguaje humano: "Modo preferido", "Nombre del monitor".</summary>
    public required string Field { get; init; }

    public string? Left { get; init; }
    public string? Right { get; init; }

    /// <summary>
    /// true cuando la diferencia cambia lo que el equipo de enfrente va a negociar.
    /// Un numero de serie distinto es informativo; un modo preferido distinto no lo es.
    /// </summary>
    public required bool Significant { get; init; }
}

/// <summary>
/// `edid-diff`. La función más útil del conjunto: por qué el procesador ve 1920×1080@60
/// y el splitter reporta otra cosa.
///
/// Compara campo a campo en lugar de byte a byte, porque dos EDID pueden diferir en
/// decenas de bytes y ser equivalentes para lo que importa, o coincidir en casi todo y
/// diferir justo en el modo preferido.
/// </summary>
public static class EdidComparer
{
    public static IReadOnlyList<EdidDifference> Compare(Edid left, Edid right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        List<EdidDifference> differences = new();

        Add(differences, "Fabricante", left.ManufacturerId, right.ManufacturerId, significant: true);
        Add(differences, "Código de producto", $"{left.ProductCode:X4}", $"{right.ProductCode:X4}", true);
        Add(differences, "Nombre del monitor", left.MonitorName, right.MonitorName, true);
        Add(differences, "Número de serie", left.SerialNumber.ToString(), right.SerialNumber.ToString(), false);
        Add(differences, "Serie en texto", left.SerialText, right.SerialText, false);
        Add(differences, "Fecha de fabricación", FormatDate(left), FormatDate(right), false);
        Add(differences, "Versión de EDID", $"{left.Version}.{left.Revision}", $"{right.Version}.{right.Revision}", true);

        Add(differences, "Entrada digital", left.IsDigitalInput ? "sí" : "no",
            right.IsDigitalInput ? "sí" : "no", true);
        Add(differences, "Interfaz", left.DigitalInterface, right.DigitalInterface, true);
        Add(differences, "Bits por color", left.BitsPerColor?.ToString(), right.BitsPerColor?.ToString(), true);

        Add(differences, "Modo preferido", left.PreferredTiming?.ToString(), right.PreferredTiming?.ToString(), true);
        Add(differences, "Reloj del modo preferido",
            FormatClock(left.PreferredTiming), FormatClock(right.PreferredTiming), true);

        Add(differences, "Límites declarados", left.RangeLimits?.ToString(), right.RangeLimits?.ToString(), true);

        Add(differences, "Tamaño físico", FormatSize(left), FormatSize(right), false);
        Add(differences, "Gamma", left.Gamma?.ToString("0.00"), right.Gamma?.ToString("0.00"), false);

        Add(differences, "Extensiones", left.ExtensionCount.ToString(), right.ExtensionCount.ToString(), true);

        CompareTimingSets(differences, "Timings detallados",
            left.DetailedTimings.Select(t => t.ToString()), right.DetailedTimings.Select(t => t.ToString()));

        CompareTimingSets(differences, "Timings establecidos",
            left.EstablishedTimings, right.EstablishedTimings);

        CompareTimingSets(differences, "Timings estándar",
            left.StandardTimings.Select(t => t.ToString()), right.StandardTimings.Select(t => t.ToString()));

        CompareCta(differences, left.Cta, right.Cta);

        return differences;
    }

    /// <summary>true si hay alguna diferencia que cambie lo que se negocia en el cable.</summary>
    public static bool HasSignificantDifferences(IReadOnlyList<EdidDifference> differences) =>
        differences.Any(d => d.Significant);

    private static void CompareCta(List<EdidDifference> differences, CtaExtension? left, CtaExtension? right)
    {
        if (left is null && right is null)
        {
            return;
        }

        if (left is null || right is null)
        {
            differences.Add(new EdidDifference
            {
                Field = "Extensión CTA-861",
                Left = left is null ? "ausente" : "presente",
                Right = right is null ? "ausente" : "presente",
                Significant = true,
            });

            return;
        }

        Add(differences, "CTA · audio básico", left.BasicAudioSupported ? "sí" : "no",
            right.BasicAudioSupported ? "sí" : "no", true);
        Add(differences, "CTA · YCbCr 4:4:4", left.YCbCr444Supported ? "sí" : "no",
            right.YCbCr444Supported ? "sí" : "no", true);
        Add(differences, "CTA · YCbCr 4:2:2", left.YCbCr422Supported ? "sí" : "no",
            right.YCbCr422Supported ? "sí" : "no", true);

        Add(differences, "CTA · HDR",
            left.Hdr is null ? "no" : string.Join(", ", left.Hdr.SupportedEotf),
            right.Hdr is null ? "no" : string.Join(", ", right.Hdr.SupportedEotf),
            true);

        CompareTimingSets(differences, "CTA · descriptores de audio",
            left.AudioDescriptors.Select(a => a.ToString()), right.AudioDescriptors.Select(a => a.ToString()));

        CompareTimingSets(differences, "CTA · códigos VIC",
            left.VideoIdentificationCodes.Select(v => v.ToString()),
            right.VideoIdentificationCodes.Select(v => v.ToString()));
    }

    /// <summary>
    /// Compara dos conjuntos y reporta lo que sobra y lo que falta, en vez de un
    /// «son distintos» que obliga a mirar los dos a ojo.
    /// </summary>
    private static void CompareTimingSets(
        List<EdidDifference> differences,
        string field,
        IEnumerable<string> left,
        IEnumerable<string> right)
    {
        List<string> a = left.ToList();
        List<string> b = right.ToList();

        List<string> onlyLeft = a.Except(b, StringComparer.Ordinal).ToList();
        List<string> onlyRight = b.Except(a, StringComparer.Ordinal).ToList();

        if (onlyLeft.Count == 0 && onlyRight.Count == 0)
        {
            return;
        }

        differences.Add(new EdidDifference
        {
            Field = field,
            Left = onlyLeft.Count == 0 ? "—" : string.Join(", ", onlyLeft),
            Right = onlyRight.Count == 0 ? "—" : string.Join(", ", onlyRight),
            Significant = true,
        });
    }

    private static void Add(
        List<EdidDifference> differences, string field, string? left, string? right, bool significant)
    {
        if (string.Equals(left, right, StringComparison.Ordinal))
        {
            return;
        }

        differences.Add(new EdidDifference
        {
            Field = field,
            Left = left ?? "—",
            Right = right ?? "—",
            Significant = significant,
        });
    }

    private static string FormatDate(Edid edid) => edid switch
    {
        { ManufactureYear: null } => "—",
        { YearIsModelYear: true } => $"modelo {edid.ManufactureYear}",
        { ManufactureWeek: null } => edid.ManufactureYear!.Value.ToString(),
        _ => $"semana {edid.ManufactureWeek}/{edid.ManufactureYear}",
    };

    private static string FormatSize(Edid edid) =>
        edid.HorizontalSizeCm is int w && edid.VerticalSizeCm is int h ? $"{w}×{h} cm" : "—";

    private static string? FormatClock(DetailedTiming? timing) =>
        timing is null ? null : $"{timing.PixelClockKhz / 1000.0:0.###} MHz";
}
