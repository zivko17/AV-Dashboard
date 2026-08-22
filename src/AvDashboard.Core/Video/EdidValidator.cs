using AvDashboard.Core.Models;

namespace AvDashboard.Core.Video;

/// <summary>Un problema encontrado en un EDID.</summary>
public sealed record EdidFinding
{
    public required string Code { get; init; }
    public required Severity Severity { get; init; }
    public required string Message { get; init; }
}

/// <summary>
/// `edid-check`: valida un EDID contra si mismo.
///
/// No comprueba solo los checksums. Lo interesante es la coherencia: un monitor que
/// declara aguantar hasta 60 Hz y trae un timing preferido de 75 esta mintiendo en uno de
/// los dos sitios, y eso es exactamente lo que hace que un procesador LED negocie un modo
/// que luego no sostiene.
/// </summary>
public static class EdidValidator
{
    public static IReadOnlyList<EdidFinding> Validate(ReadOnlySpan<byte> raw, Edid? parsed = null)
    {
        List<EdidFinding> findings = new();

        if (raw.Length < EdidParser.BlockSize)
        {
            findings.Add(new EdidFinding
            {
                Code = "edid.too-short",
                Severity = Severity.Critical,
                Message = $"Solo hay {raw.Length} bytes; un EDID tiene {EdidParser.BlockSize} como mínimo.",
            });

            return findings;
        }

        // --- Checksums ---
        int blocks = raw.Length / EdidParser.BlockSize;
        for (int block = 0; block < blocks; block++)
        {
            ReadOnlySpan<byte> slice = raw.Slice(block * EdidParser.BlockSize, EdidParser.BlockSize);
            int sum = 0;
            foreach (byte b in slice)
            {
                sum += b;
            }

            if (sum % 256 != 0)
            {
                findings.Add(new EdidFinding
                {
                    Code = "edid.checksum",
                    Severity = Severity.Error,
                    Message = block == 0
                        ? "El checksum del bloque base no cuadra."
                        : $"El checksum del bloque de extensión {block} no cuadra.",
                });
            }
        }

        if (raw.Length % EdidParser.BlockSize != 0)
        {
            findings.Add(new EdidFinding
            {
                Code = "edid.truncated",
                Severity = Severity.Error,
                Message = $"La longitud ({raw.Length}) no es múltiplo de {EdidParser.BlockSize}: el volcado está truncado.",
            });
        }

        if (parsed is null && !EdidParser.TryParse(raw, out parsed, out string? parseError))
        {
            findings.Add(new EdidFinding
            {
                Code = "edid.unparseable",
                Severity = Severity.Critical,
                Message = parseError ?? "No se ha podido decodificar.",
            });

            return findings;
        }

        Edid edid = parsed!;

        // --- Estructura ---
        int declaredBlocks = edid.ExtensionCount + 1;
        if (declaredBlocks != blocks)
        {
            findings.Add(new EdidFinding
            {
                Code = "edid.extension-count",
                Severity = Severity.Warning,
                Message = $"Declara {edid.ExtensionCount} extensiones ({declaredBlocks} bloques) " +
                          $"y han llegado {blocks}.",
            });
        }

        if (edid.Version != 1 || edid.Revision > 4)
        {
            findings.Add(new EdidFinding
            {
                Code = "edid.version",
                Severity = Severity.Warning,
                Message = $"Versión {edid.Version}.{edid.Revision}, que no es una de las habituales (1.0 a 1.4).",
            });
        }

        // --- Contenido util ---
        if (string.IsNullOrWhiteSpace(edid.MonitorName))
        {
            findings.Add(new EdidFinding
            {
                Code = "edid.no-name",
                Severity = Severity.Info,
                Message = "Sin descriptor de nombre (0xFC): en la lista aparecerá solo por fabricante y modelo.",
            });
        }

        if (edid.DetailedTimings.Count == 0)
        {
            findings.Add(new EdidFinding
            {
                Code = "edid.no-preferred",
                Severity = Severity.Error,
                Message = "No hay ningún timing detallado, así que no declara modo preferido.",
            });
        }

        // --- Coherencia entre los timings y los limites declarados ---
        if (edid.RangeLimits is DisplayRangeLimits limits)
        {
            foreach (DetailedTiming timing in edid.DetailedTimings)
            {
                if (timing.RefreshHz is not double refresh)
                {
                    continue;
                }

                // Un hercio de margen: los limites se declaran en enteros y un 59,94
                // legitimo no debe cantar como fuera de rango.
                if (refresh < limits.MinVerticalHz - 1 || refresh > limits.MaxVerticalHz + 1)
                {
                    findings.Add(new EdidFinding
                    {
                        Code = "edid.refresh-out-of-range",
                        Severity = Severity.Warning,
                        Message = $"El timing {timing} está fuera del rango vertical declarado " +
                                  $"({limits.MinVerticalHz}-{limits.MaxVerticalHz} Hz).",
                    });
                }

                double horizontalKhz = timing.HorizontalTotal > 0
                    ? timing.PixelClockKhz / (double)timing.HorizontalTotal
                    : 0;

                if (horizontalKhz > 0 &&
                    (horizontalKhz < limits.MinHorizontalKhz - 1 || horizontalKhz > limits.MaxHorizontalKhz + 1))
                {
                    findings.Add(new EdidFinding
                    {
                        Code = "edid.hrate-out-of-range",
                        Severity = Severity.Warning,
                        Message = $"El timing {timing} pide {horizontalKhz:0.#} kHz horizontales, " +
                                  $"fuera del rango declarado ({limits.MinHorizontalKhz}-{limits.MaxHorizontalKhz} kHz).",
                    });
                }

                if (limits.MaxPixelClockMhz > 0 &&
                    timing.PixelClockKhz > (limits.MaxPixelClockMhz * 1000) + 1000)
                {
                    findings.Add(new EdidFinding
                    {
                        Code = "edid.pixelclock-out-of-range",
                        Severity = Severity.Warning,
                        Message = $"El timing {timing} pide {timing.PixelClockKhz / 1000.0:0.#} MHz de reloj, " +
                                  $"por encima del máximo declarado ({limits.MaxPixelClockMhz} MHz).",
                    });
                }
            }
        }

        foreach (DetailedTiming timing in edid.DetailedTimings)
        {
            if (timing.PixelClockKhz == 0)
            {
                findings.Add(new EdidFinding
                {
                    Code = "edid.zero-clock",
                    Severity = Severity.Error,
                    Message = "Hay un timing detallado con el reloj de pixel a cero.",
                });
            }
        }

        return findings;
    }

    /// <summary>Veredicto agregado, para pintar un punto de color al lado del display.</summary>
    public static HealthState Evaluate(IReadOnlyList<EdidFinding> findings) =>
        findings.Count == 0
            ? HealthState.Ok
            : findings.Select(f => f.Severity switch
            {
                Severity.Critical or Severity.Error => HealthState.Critical,
                Severity.Warning => HealthState.Warning,
                _ => HealthState.Ok,
            }).Worst();
}
