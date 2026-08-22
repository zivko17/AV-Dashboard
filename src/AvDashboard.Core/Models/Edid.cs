namespace AvDashboard.Core.Models;

/// <summary>
/// Un EDID decodificado (M18).
///
/// El bloque base son 128 bytes con una estructura fijada por VESA desde 1994, mas cero o
/// mas bloques de extension del mismo tamano. Todo lo que hay aqui sale de esos bytes: no
/// se consulta ninguna base de datos ni se inventa nada.
/// </summary>
public sealed record Edid
{
    /// <summary>Los bytes tal cual, para volcarlos a .bin o compararlos byte a byte.</summary>
    public required IReadOnlyList<byte> Raw { get; init; }

    /// <summary>Identificador de fabricante de tres letras (PNP ID): "SAM", "DEL", "NVD".</summary>
    public required string ManufacturerId { get; init; }

    public ushort ProductCode { get; init; }
    public uint SerialNumber { get; init; }

    /// <summary>Semana de fabricacion, 1-54. Null si el fabricante no la declara.</summary>
    public int? ManufactureWeek { get; init; }

    public int? ManufactureYear { get; init; }

    /// <summary>
    /// Con EDID 1.4 la semana 0xFF significa que el ano es el de modelo, no el de
    /// fabricacion. Cambia como se lee la fecha y conviene no perderlo.
    /// </summary>
    public bool YearIsModelYear { get; init; }

    public int Version { get; init; }
    public int Revision { get; init; }

    public bool IsDigitalInput { get; init; }

    /// <summary>Bits por color del enlace digital. Null si es analogico o no lo declara.</summary>
    public int? BitsPerColor { get; init; }

    /// <summary>Interfaz declarada: "HDMIa", "DisplayPort", "DVI"... Null si no la declara.</summary>
    public string? DigitalInterface { get; init; }

    public int? HorizontalSizeCm { get; init; }
    public int? VerticalSizeCm { get; init; }
    public double? Gamma { get; init; }

    /// <summary>Nombre del monitor, del descriptor 0xFC. Es el que hay que ensenar al usuario.</summary>
    public string? MonitorName { get; init; }

    /// <summary>Numero de serie en texto, del descriptor 0xFF.</summary>
    public string? SerialText { get; init; }

    public IReadOnlyList<string> TextDescriptors { get; init; } = Array.Empty<string>();

    /// <summary>Modos del mapa de bits de timings establecidos (bytes 35-37).</summary>
    public IReadOnlyList<string> EstablishedTimings { get; init; } = Array.Empty<string>();

    /// <summary>Timings estandar (bytes 38-53), hasta ocho.</summary>
    public IReadOnlyList<StandardTiming> StandardTimings { get; init; } = Array.Empty<StandardTiming>();

    /// <summary>
    /// Timings detallados. El primero es el modo preferido del monitor, y es el que un
    /// procesador LED o un splitter deberia estar respetando.
    /// </summary>
    public IReadOnlyList<DetailedTiming> DetailedTimings { get; init; } = Array.Empty<DetailedTiming>();

    public DisplayRangeLimits? RangeLimits { get; init; }

    public int ExtensionCount { get; init; }

    /// <summary>Extension CTA-861, si la hay. Es donde vive el audio y el HDR.</summary>
    public CtaExtension? Cta { get; init; }

    /// <summary>Modo preferido, que por definicion es el primer timing detallado.</summary>
    public DetailedTiming? PreferredTiming => DetailedTimings.Count > 0 ? DetailedTimings[0] : null;

    /// <summary>Etiqueta corta para listados: "SAM 1234 · MSI MP243X".</summary>
    public string DisplayLabel => MonitorName is { Length: > 0 }
        ? $"{ManufacturerId} {ProductCode:X4} · {MonitorName}"
        : $"{ManufacturerId} {ProductCode:X4}";
}

public sealed record StandardTiming
{
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required int RefreshHz { get; init; }
    public required string AspectRatio { get; init; }

    public override string ToString() => $"{Width}×{Height}@{RefreshHz}";
}

/// <summary>Un timing detallado de 18 bytes, con todo lo que hace falta para reproducirlo.</summary>
public sealed record DetailedTiming
{
    /// <summary>Reloj de pixel en kHz.</summary>
    public required int PixelClockKhz { get; init; }

    public required int HorizontalActive { get; init; }
    public required int HorizontalBlanking { get; init; }
    public required int VerticalActive { get; init; }
    public required int VerticalBlanking { get; init; }

    public int HorizontalSyncOffset { get; init; }
    public int HorizontalSyncPulse { get; init; }
    public int VerticalSyncOffset { get; init; }
    public int VerticalSyncPulse { get; init; }

    public int? HorizontalSizeMm { get; init; }
    public int? VerticalSizeMm { get; init; }

    public bool Interlaced { get; init; }

    public int HorizontalTotal => HorizontalActive + HorizontalBlanking;
    public int VerticalTotal => VerticalActive + VerticalBlanking;

    /// <summary>
    /// Refresh calculado a partir del reloj y los totales. Es el numero que hay que
    /// comparar con lo que dice el procesador LED, y el que delata un 59,94 contra un 60.
    /// </summary>
    public double? RefreshHz => HorizontalTotal > 0 && VerticalTotal > 0
        ? PixelClockKhz * 1000.0 / (HorizontalTotal * (double)VerticalTotal)
        : null;

    public override string ToString() =>
        RefreshHz is double hz
            ? $"{HorizontalActive}×{VerticalActive}{(Interlaced ? "i" : "p")}@{hz:0.###}"
            : $"{HorizontalActive}×{VerticalActive}";
}

/// <summary>Limites declarados por el monitor (descriptor 0xFD).</summary>
public sealed record DisplayRangeLimits
{
    public int MinVerticalHz { get; init; }
    public int MaxVerticalHz { get; init; }
    public int MinHorizontalKhz { get; init; }
    public int MaxHorizontalKhz { get; init; }

    /// <summary>Reloj de pixel maximo en MHz. 0 si no se declara.</summary>
    public int MaxPixelClockMhz { get; init; }

    public override string ToString() =>
        $"{MinVerticalHz}-{MaxVerticalHz} Hz, {MinHorizontalKhz}-{MaxHorizontalKhz} kHz" +
        (MaxPixelClockMhz > 0 ? $", ≤{MaxPixelClockMhz} MHz" : string.Empty);
}

/// <summary>Bloque de extension CTA-861. Es donde estan el audio y el HDR.</summary>
public sealed record CtaExtension
{
    public int Revision { get; init; }
    public bool UnderscanSupported { get; init; }
    public bool BasicAudioSupported { get; init; }
    public bool YCbCr444Supported { get; init; }
    public bool YCbCr422Supported { get; init; }

    public IReadOnlyList<ShortAudioDescriptor> AudioDescriptors { get; init; } =
        Array.Empty<ShortAudioDescriptor>();

    /// <summary>Codigos VIC de los modos de video soportados.</summary>
    public IReadOnlyList<int> VideoIdentificationCodes { get; init; } = Array.Empty<int>();

    public IReadOnlyList<DetailedTiming> DetailedTimings { get; init; } = Array.Empty<DetailedTiming>();

    public HdrStaticMetadata? Hdr { get; init; }

    /// <summary>IEEE OUI de los bloques de fabricante encontrados, en hexadecimal.</summary>
    public IReadOnlyList<string> VendorBlocks { get; init; } = Array.Empty<string>();
}

/// <summary>Descriptor corto de audio: que formato, cuantos canales y a que frecuencias.</summary>
public sealed record ShortAudioDescriptor
{
    public required int FormatCode { get; init; }
    public required string FormatName { get; init; }
    public required int MaxChannels { get; init; }

    /// <summary>Frecuencias de muestreo soportadas, en Hz.</summary>
    public IReadOnlyList<int> SampleRates { get; init; } = Array.Empty<int>();

    /// <summary>Profundidades soportadas en LPCM. Vacio en formatos comprimidos.</summary>
    public IReadOnlyList<int> BitDepths { get; init; } = Array.Empty<int>();

    public override string ToString() =>
        $"{FormatName} {MaxChannels}ch @ {string.Join('/', SampleRates.Select(r => r / 1000.0))} kHz";
}

/// <summary>Metadatos HDR estaticos (bloque extendido 0x06 de CTA-861).</summary>
public sealed record HdrStaticMetadata
{
    public bool TraditionalSdr { get; init; }
    public bool TraditionalHdr { get; init; }

    /// <summary>SMPTE ST 2084, que es lo que la gente llama HDR10.</summary>
    public bool Pq { get; init; }

    public bool HybridLogGamma { get; init; }

    public IReadOnlyList<string> SupportedEotf
    {
        get
        {
            List<string> list = new();
            if (TraditionalSdr) list.Add("SDR");
            if (TraditionalHdr) list.Add("HDR gamma");
            if (Pq) list.Add("PQ (ST 2084)");
            if (HybridLogGamma) list.Add("HLG");
            return list;
        }
    }
}
