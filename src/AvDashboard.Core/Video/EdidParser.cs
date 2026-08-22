using AvDashboard.Core.Models;

namespace AvDashboard.Core.Video;

/// <summary>
/// Decodifica un EDID segun VESA E-EDID.
///
/// Es logica de bytes pura y por eso vive en Core: se prueba entera en CI sin necesidad
/// de una maquina Windows ni de un monitor enchufado. Un desplazamiento de bits mal
/// puesto aqui no revienta, produce datos plausibles y equivocados, que es la peor clase
/// de error posible en una herramienta de diagnostico.
/// </summary>
public static class EdidParser
{
    /// <summary>Tamano de un bloque, base o extension.</summary>
    public const int BlockSize = 128;

    private static readonly byte[] Header = { 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00 };

    public static bool TryParse(ReadOnlySpan<byte> data, out Edid? edid, out string? error)
    {
        edid = null;
        error = null;

        if (data.Length < BlockSize)
        {
            error = $"Un EDID tiene al menos {BlockSize} bytes y este trae {data.Length}.";
            return false;
        }

        if (!data[..8].SequenceEqual(Header))
        {
            error = "La cabecera no es 00 FF FF FF FF FF FF 00: esto no es un EDID.";
            return false;
        }

        try
        {
            edid = Parse(data);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or ArgumentOutOfRangeException)
        {
            error = $"EDID malformado: {ex.Message}";
            return false;
        }
    }

    public static Edid Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < BlockSize)
        {
            throw new ArgumentException($"Se necesitan {BlockSize} bytes.", nameof(data));
        }

        int version = data[18];
        int revision = data[19];

        (int? week, int? year, bool modelYear) = ParseDate(data[16], data[17]);
        (bool digital, int? bits, string? iface) = ParseVideoInput(data[20], version, revision);

        List<DetailedTiming> detailed = new();
        List<string> texts = new();
        string? monitorName = null;
        string? serialText = null;
        DisplayRangeLimits? limits = null;

        // Los cuatro descriptores de 18 bytes que empiezan en el 54.
        for (int i = 0; i < 4; i++)
        {
            ReadOnlySpan<byte> descriptor = data.Slice(54 + (i * 18), 18);

            // Un descriptor cuyos dos primeros bytes son cero no es un timing: es un
            // descriptor de pantalla, y el tipo esta en el byte 3.
            if (descriptor[0] == 0 && descriptor[1] == 0)
            {
                switch (descriptor[3])
                {
                    case 0xFC:
                        monitorName = ReadDescriptorText(descriptor);
                        break;
                    case 0xFF:
                        serialText = ReadDescriptorText(descriptor);
                        break;
                    case 0xFE:
                        string text = ReadDescriptorText(descriptor);
                        if (text.Length > 0)
                        {
                            texts.Add(text);
                        }

                        break;
                    case 0xFD:
                        limits = ParseRangeLimits(descriptor);
                        break;
                }

                continue;
            }

            detailed.Add(ParseDetailedTiming(descriptor));
        }

        int extensionCount = data[126];
        CtaExtension? cta = null;

        for (int block = 1; block <= extensionCount; block++)
        {
            int offset = block * BlockSize;
            if (offset + BlockSize > data.Length)
            {
                // El contador anuncia mas bloques de los que han llegado. No es motivo
                // para tirar el parseo: se decodifica lo que hay y el validador lo canta.
                break;
            }

            ReadOnlySpan<byte> extension = data.Slice(offset, BlockSize);
            if (extension[0] == 0x02)
            {
                cta = ParseCta(extension);
                break;
            }
        }

        return new Edid
        {
            Raw = data.ToArray(),
            ManufacturerId = ParseManufacturer(data[8], data[9]),
            ProductCode = (ushort)(data[10] | (data[11] << 8)),
            SerialNumber = (uint)(data[12] | (data[13] << 8) | (data[14] << 16) | (data[15] << 24)),
            ManufactureWeek = week,
            ManufactureYear = year,
            YearIsModelYear = modelYear,
            Version = version,
            Revision = revision,
            IsDigitalInput = digital,
            BitsPerColor = bits,
            DigitalInterface = iface,
            HorizontalSizeCm = data[21] == 0 ? null : data[21],
            VerticalSizeCm = data[22] == 0 ? null : data[22],
            Gamma = data[23] == 0xFF ? null : (data[23] + 100) / 100.0,
            MonitorName = monitorName,
            SerialText = serialText,
            TextDescriptors = texts,
            EstablishedTimings = ParseEstablishedTimings(data[35], data[36], data[37]),
            StandardTimings = ParseStandardTimings(data.Slice(38, 16), version, revision),
            DetailedTimings = detailed,
            RangeLimits = limits,
            ExtensionCount = extensionCount,
            Cta = cta,
        };
    }

    /// <summary>
    /// Tres letras empaquetadas en 5 bits cada una dentro de dos bytes big-endian, con
    /// 'A' = 1. Es el mismo esquema que los PNP ID de Windows.
    /// </summary>
    internal static string ParseManufacturer(byte high, byte low)
    {
        int packed = (high << 8) | low;

        Span<char> letters = stackalloc char[3];
        letters[0] = (char)('A' + ((packed >> 10) & 0x1F) - 1);
        letters[1] = (char)('A' + ((packed >> 5) & 0x1F) - 1);
        letters[2] = (char)('A' + (packed & 0x1F) - 1);

        foreach (char c in letters)
        {
            if (c is < 'A' or > 'Z')
            {
                return "???";
            }
        }

        return new string(letters);
    }

    private static (int? Week, int? Year, bool ModelYear) ParseDate(byte weekByte, byte yearByte)
    {
        int? year = yearByte == 0 ? null : 1990 + yearByte;

        // 0xFF en la semana significa, desde EDID 1.4, que el ano es el de modelo y no el
        // de fabricacion. Distinguirlo importa cuando comparas dos monitores del mismo lote.
        if (weekByte == 0xFF)
        {
            return (null, year, true);
        }

        int? week = weekByte is >= 1 and <= 54 ? weekByte : null;
        return (week, year, false);
    }

    private static (bool Digital, int? Bits, string? Interface) ParseVideoInput(
        byte value, int version, int revision)
    {
        bool digital = (value & 0x80) != 0;

        if (!digital || version < 1 || (version == 1 && revision < 4))
        {
            return (digital, null, null);
        }

        int? bits = ((value >> 4) & 0x07) switch
        {
            1 => 6,
            2 => 8,
            3 => 10,
            4 => 12,
            5 => 14,
            6 => 16,
            _ => null,
        };

        string? iface = (value & 0x0F) switch
        {
            1 => "DVI",
            2 => "HDMIa",
            3 => "HDMIb",
            4 => "MDDI",
            5 => "DisplayPort",
            _ => null,
        };

        return (true, bits, iface);
    }

    /// <summary>Texto de un descriptor 0xFC, 0xFE o 0xFF: 13 bytes, terminado en 0x0A y rellenado con espacios.</summary>
    private static string ReadDescriptorText(ReadOnlySpan<byte> descriptor)
    {
        ReadOnlySpan<byte> payload = descriptor.Slice(5, 13);

        int length = payload.IndexOf((byte)0x0A);
        if (length < 0)
        {
            length = payload.Length;
        }

        Span<char> chars = stackalloc char[length];
        for (int i = 0; i < length; i++)
        {
            chars[i] = (char)payload[i];
        }

        return new string(chars).TrimEnd();
    }

    private static DisplayRangeLimits ParseRangeLimits(ReadOnlySpan<byte> d)
    {
        // EDID 1.4 anade desplazamientos para poder declarar frecuencias por encima de
        // 255: el byte 4 dice a cuales hay que sumarles 255.
        int flags = d[4];
        int verticalOffsetMax = (flags & 0x03) is 2 or 3 ? 255 : 0;
        int verticalOffsetMin = (flags & 0x03) == 3 ? 255 : 0;
        int horizontalOffsetMax = ((flags >> 2) & 0x03) is 2 or 3 ? 255 : 0;
        int horizontalOffsetMin = ((flags >> 2) & 0x03) == 3 ? 255 : 0;

        return new DisplayRangeLimits
        {
            MinVerticalHz = d[5] + verticalOffsetMin,
            MaxVerticalHz = d[6] + verticalOffsetMax,
            MinHorizontalKhz = d[7] + horizontalOffsetMin,
            MaxHorizontalKhz = d[8] + horizontalOffsetMax,
            MaxPixelClockMhz = d[9] * 10,
        };
    }

    internal static DetailedTiming ParseDetailedTiming(ReadOnlySpan<byte> d)
    {
        int horizontalSizeMm = ((d[14] & 0xF0) << 4) | d[12];
        int verticalSizeMm = ((d[14] & 0x0F) << 8) | d[13];

        return new DetailedTiming
        {
            // El reloj viene en unidades de 10 kHz.
            PixelClockKhz = ((d[1] << 8) | d[0]) * 10,

            HorizontalActive = ((d[4] & 0xF0) << 4) | d[2],
            HorizontalBlanking = ((d[4] & 0x0F) << 8) | d[3],
            VerticalActive = ((d[7] & 0xF0) << 4) | d[5],
            VerticalBlanking = ((d[7] & 0x0F) << 8) | d[6],

            HorizontalSyncOffset = ((d[11] & 0xC0) << 2) | d[8],
            HorizontalSyncPulse = ((d[11] & 0x30) << 4) | d[9],
            VerticalSyncOffset = ((d[11] & 0x0C) << 2) | (d[10] >> 4),
            VerticalSyncPulse = ((d[11] & 0x03) << 4) | (d[10] & 0x0F),

            HorizontalSizeMm = horizontalSizeMm == 0 ? null : horizontalSizeMm,
            VerticalSizeMm = verticalSizeMm == 0 ? null : verticalSizeMm,

            Interlaced = (d[17] & 0x80) != 0,
        };
    }

    /// <summary>Mapa de bits de los bytes 35-37. La tabla la fija VESA y no cambia.</summary>
    internal static IReadOnlyList<string> ParseEstablishedTimings(byte b35, byte b36, byte b37)
    {
        (byte Mask, string Mode)[] first =
        {
            (0x80, "720×400@70"), (0x40, "720×400@88"), (0x20, "640×480@60"),
            (0x10, "640×480@67"), (0x08, "640×480@72"), (0x04, "640×480@75"),
            (0x02, "800×600@56"), (0x01, "800×600@60"),
        };

        (byte Mask, string Mode)[] second =
        {
            (0x80, "800×600@72"), (0x40, "800×600@75"), (0x20, "832×624@75"),
            (0x10, "1024×768@87i"), (0x08, "1024×768@60"), (0x04, "1024×768@70"),
            (0x02, "1024×768@75"), (0x01, "1280×1024@75"),
        };

        List<string> modes = new();

        foreach ((byte mask, string mode) in first)
        {
            if ((b35 & mask) != 0)
            {
                modes.Add(mode);
            }
        }

        foreach ((byte mask, string mode) in second)
        {
            if ((b36 & mask) != 0)
            {
                modes.Add(mode);
            }
        }

        // Del byte 37 solo el bit 7 esta definido; el resto es del fabricante.
        if ((b37 & 0x80) != 0)
        {
            modes.Add("1152×870@75");
        }

        return modes;
    }

    internal static IReadOnlyList<StandardTiming> ParseStandardTimings(
        ReadOnlySpan<byte> block, int version, int revision)
    {
        List<StandardTiming> timings = new();

        for (int i = 0; i + 1 < block.Length; i += 2)
        {
            byte b0 = block[i];
            byte b1 = block[i + 1];

            // 01 01 es el relleno de las entradas no usadas.
            if (b0 == 0x01 && b1 == 0x01)
            {
                continue;
            }

            if (b0 == 0x00 || b0 == 0xFF)
            {
                continue;
            }

            int width = (b0 + 31) * 8;
            int refresh = (b1 & 0x3F) + 60;

            // El codigo 0 significa 16:10 desde EDID 1.3, y 1:1 en las versiones anteriores.
            bool legacy = version < 1 || (version == 1 && revision < 3);

            (string aspect, int height) = ((b1 >> 6) & 0x03) switch
            {
                0 => legacy ? ("1:1", width) : ("16:10", width * 10 / 16),
                1 => ("4:3", width * 3 / 4),
                2 => ("5:4", width * 4 / 5),
                _ => ("16:9", width * 9 / 16),
            };

            timings.Add(new StandardTiming
            {
                Width = width,
                Height = height,
                RefreshHz = refresh,
                AspectRatio = aspect,
            });
        }

        return timings;
    }

    private static CtaExtension ParseCta(ReadOnlySpan<byte> ext)
    {
        int revision = ext[1];
        int detailedOffset = ext[2];
        byte flags = ext[3];

        List<ShortAudioDescriptor> audio = new();
        List<int> vics = new();
        List<string> vendors = new();
        HdrStaticMetadata? hdr = null;

        // La coleccion de bloques de datos va del byte 4 hasta donde empiezan los timings
        // detallados. Un offset de 0 significa que no hay ni timings ni bloques.
        if (detailedOffset > 4)
        {
            int cursor = 4;
            while (cursor < detailedOffset && cursor < ext.Length)
            {
                byte header = ext[cursor];
                int tag = (header >> 5) & 0x07;
                int length = header & 0x1F;

                if (length == 0 || cursor + 1 + length > ext.Length)
                {
                    break;
                }

                ReadOnlySpan<byte> payload = ext.Slice(cursor + 1, length);

                switch (tag)
                {
                    case 1:
                        for (int i = 0; i + 2 < payload.Length; i += 3)
                        {
                            audio.Add(ParseShortAudioDescriptor(payload.Slice(i, 3)));
                        }

                        break;

                    case 2:
                        foreach (byte vic in payload)
                        {
                            vics.Add(vic & 0x7F);
                        }

                        break;

                    case 3:
                        if (payload.Length >= 3)
                        {
                            // El OUI viene en little-endian dentro del bloque.
                            vendors.Add($"{payload[2]:X2}{payload[1]:X2}{payload[0]:X2}");
                        }

                        break;

                    case 7:
                        if (payload.Length >= 2 && payload[0] == 0x06)
                        {
                            hdr = ParseHdr(payload);
                        }

                        break;
                }

                cursor += 1 + length;
            }
        }

        List<DetailedTiming> detailed = new();
        if (detailedOffset >= 4)
        {
            for (int offset = detailedOffset; offset + 18 <= BlockSize - 1; offset += 18)
            {
                ReadOnlySpan<byte> descriptor = ext.Slice(offset, 18);
                if (descriptor[0] == 0 && descriptor[1] == 0)
                {
                    break;
                }

                detailed.Add(ParseDetailedTiming(descriptor));
            }
        }

        return new CtaExtension
        {
            Revision = revision,
            UnderscanSupported = (flags & 0x80) != 0,
            BasicAudioSupported = (flags & 0x40) != 0,
            YCbCr444Supported = (flags & 0x20) != 0,
            YCbCr422Supported = (flags & 0x10) != 0,
            AudioDescriptors = audio,
            VideoIdentificationCodes = vics,
            DetailedTimings = detailed,
            Hdr = hdr,
            VendorBlocks = vendors,
        };
    }

    internal static ShortAudioDescriptor ParseShortAudioDescriptor(ReadOnlySpan<byte> sad)
    {
        int format = (sad[0] >> 3) & 0x0F;
        int channels = (sad[0] & 0x07) + 1;

        (byte Mask, int Rate)[] rates =
        {
            (0x01, 32000), (0x02, 44100), (0x04, 48000), (0x08, 88200),
            (0x10, 96000), (0x20, 176400), (0x40, 192000),
        };

        List<int> supported = new();
        foreach ((byte mask, int rate) in rates)
        {
            if ((sad[1] & mask) != 0)
            {
                supported.Add(rate);
            }
        }

        List<int> depths = new();
        if (format == 1)
        {
            // El tercer byte solo describe profundidades en LPCM; en los formatos
            // comprimidos significa otra cosa y no se interpreta.
            if ((sad[2] & 0x01) != 0) depths.Add(16);
            if ((sad[2] & 0x02) != 0) depths.Add(20);
            if ((sad[2] & 0x04) != 0) depths.Add(24);
        }

        return new ShortAudioDescriptor
        {
            FormatCode = format,
            FormatName = AudioFormatName(format),
            MaxChannels = channels,
            SampleRates = supported,
            BitDepths = depths,
        };
    }

    internal static string AudioFormatName(int code) => code switch
    {
        1 => "LPCM",
        2 => "AC-3",
        3 => "MPEG-1",
        4 => "MP3",
        5 => "MPEG-2",
        6 => "AAC LC",
        7 => "DTS",
        8 => "ATRAC",
        9 => "DSD",
        10 => "E-AC-3",
        11 => "DTS-HD",
        12 => "Dolby TrueHD",
        13 => "DST",
        14 => "WMA Pro",
        15 => "Extendido",
        _ => $"Desconocido ({code})",
    };

    private static HdrStaticMetadata ParseHdr(ReadOnlySpan<byte> payload)
    {
        byte eotf = payload[1];

        return new HdrStaticMetadata
        {
            TraditionalSdr = (eotf & 0x01) != 0,
            TraditionalHdr = (eotf & 0x02) != 0,
            Pq = (eotf & 0x04) != 0,
            HybridLogGamma = (eotf & 0x08) != 0,
        };
    }
}
