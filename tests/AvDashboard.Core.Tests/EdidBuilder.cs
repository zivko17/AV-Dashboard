namespace AvDashboard.Core.Tests;

/// <summary>
/// Construye EDID sintéticos byte a byte.
///
/// Es la única forma honesta de probar un parser de EDID sin hardware: si los bytes los
/// pone la prueba, cualquier desplazamiento mal puesto en el parser sale a la luz. Un
/// volcado real de un monitor probaría que el parser no revienta, no que acierta.
/// </summary>
internal static class EdidBuilder
{
    /// <summary>
    /// Un EDID 1.4 completo y coherente: MSI MP243X, entrada HDMI de 8 bits,
    /// modo preferido 1920×1080@60 y una extensión CTA-861 con audio y HDR.
    /// </summary>
    public static byte[] BuildValid()
    {
        byte[] edid = new byte[256];

        // --- Cabecera ---
        edid[0] = 0x00;
        for (int i = 1; i <= 6; i++)
        {
            edid[i] = 0xFF;
        }

        edid[7] = 0x00;

        // "MSI": M=13, S=19, I=9 empaquetados en 5 bits cada uno.
        edid[8] = 0x36;
        edid[9] = 0x69;

        edid[10] = 0x34;  // Código de producto 0x1234, little-endian.
        edid[11] = 0x12;

        edid[12] = 0x44;  // Serie 0x11223344, little-endian.
        edid[13] = 0x33;
        edid[14] = 0x22;
        edid[15] = 0x11;

        edid[16] = 20;    // Semana 20.
        edid[17] = 34;    // 1990 + 34 = 2024.
        edid[18] = 1;     // Versión 1.
        edid[19] = 4;     // Revisión 4.

        // Digital (bit 7), 8 bits por color (código 2), interfaz HDMIa (código 2).
        edid[20] = 0xA2;

        edid[21] = 60;    // 60 cm de ancho.
        edid[22] = 34;    // 34 cm de alto.
        edid[23] = 120;   // Gamma (120 + 100) / 100 = 2,2.
        edid[24] = 0x0A;

        // --- Timings establecidos ---
        edid[35] = 0x21;  // 640×480@60 y 800×600@60.
        edid[36] = 0x08;  // 1024×768@60.
        edid[37] = 0x80;  // 1152×870@75.

        // --- Timings estándar: 1920×1080@60, 16:9. El resto sin usar. ---
        edid[38] = 0xD1;  // (209 + 31) × 8 = 1920.
        edid[39] = 0xC0;  // Aspecto 16:9, refresco 60.
        for (int i = 40; i < 54; i += 2)
        {
            edid[i] = 0x01;
            edid[i + 1] = 0x01;
        }

        WriteDetailedTiming1080p60(edid, 54);
        WriteRangeLimits(edid, 72);
        WriteTextDescriptor(edid, 90, 0xFC, "MSI MP243X");
        WriteTextDescriptor(edid, 108, 0xFF, "ABC123");

        edid[126] = 1;    // Una extensión.
        edid[127] = Checksum(edid.AsSpan(0, 128));

        WriteCtaExtension(edid, 128);
        edid[255] = Checksum(edid.AsSpan(128, 128));

        return edid;
    }

    /// <summary>Solo el bloque base, sin extensiones. Para las pruebas que no las necesitan.</summary>
    public static byte[] BuildBaseOnly()
    {
        byte[] full = BuildValid();
        byte[] baseBlock = full[..128];

        baseBlock[126] = 0;
        baseBlock[127] = Checksum(baseBlock.AsSpan(0, 128));

        return baseBlock;
    }

    /// <summary>1920×1080@60: reloj 148,5 MHz, total 2200×1125.</summary>
    private static void WriteDetailedTiming1080p60(byte[] edid, int offset)
    {
        const int pixelClock = 14850;   // Unidades de 10 kHz.
        const int hActive = 1920, hBlank = 280;
        const int vActive = 1080, vBlank = 45;
        const int hSyncOffset = 88, hSyncPulse = 44;
        const int vSyncOffset = 4, vSyncPulse = 5;
        const int hSizeMm = 598, vSizeMm = 336;

        edid[offset + 0] = (byte)(pixelClock & 0xFF);
        edid[offset + 1] = (byte)(pixelClock >> 8);

        edid[offset + 2] = (byte)(hActive & 0xFF);
        edid[offset + 3] = (byte)(hBlank & 0xFF);
        edid[offset + 4] = (byte)(((hActive >> 8) << 4) | (hBlank >> 8));

        edid[offset + 5] = (byte)(vActive & 0xFF);
        edid[offset + 6] = (byte)(vBlank & 0xFF);
        edid[offset + 7] = (byte)(((vActive >> 8) << 4) | (vBlank >> 8));

        edid[offset + 8] = (byte)(hSyncOffset & 0xFF);
        edid[offset + 9] = (byte)(hSyncPulse & 0xFF);
        edid[offset + 10] = (byte)(((vSyncOffset & 0x0F) << 4) | (vSyncPulse & 0x0F));
        edid[offset + 11] = 0x00;

        edid[offset + 12] = (byte)(hSizeMm & 0xFF);
        edid[offset + 13] = (byte)(vSizeMm & 0xFF);
        edid[offset + 14] = (byte)(((hSizeMm >> 8) << 4) | (vSizeMm >> 8));

        edid[offset + 15] = 0x00;
        edid[offset + 16] = 0x00;
        edid[offset + 17] = 0x1E;   // Sync separado, no entrelazado.
    }

    /// <summary>Descriptor 0xFD: 48-75 Hz, 30-83 kHz, reloj máximo 170 MHz.</summary>
    private static void WriteRangeLimits(byte[] edid, int offset)
    {
        edid[offset + 3] = 0xFD;
        edid[offset + 4] = 0x00;   // Sin desplazamientos de EDID 1.4.
        edid[offset + 5] = 48;
        edid[offset + 6] = 75;
        edid[offset + 7] = 30;
        edid[offset + 8] = 83;
        edid[offset + 9] = 17;     // × 10 = 170 MHz.
        edid[offset + 10] = 0x00;
        edid[offset + 11] = 0x0A;

        for (int i = 12; i < 18; i++)
        {
            edid[offset + i] = 0x20;
        }
    }

    private static void WriteTextDescriptor(byte[] edid, int offset, byte type, string text)
    {
        edid[offset + 3] = type;
        edid[offset + 4] = 0x00;

        int i = 0;
        for (; i < text.Length && i < 13; i++)
        {
            edid[offset + 5 + i] = (byte)text[i];
        }

        if (i < 13)
        {
            edid[offset + 5 + i] = 0x0A;
            i++;
        }

        for (; i < 13; i++)
        {
            edid[offset + 5 + i] = 0x20;
        }
    }

    /// <summary>
    /// Extensión CTA-861 rev 3 con audio básico, YCbCr, un descriptor LPCM, dos VIC y
    /// metadatos HDR.
    /// </summary>
    private static void WriteCtaExtension(byte[] edid, int offset)
    {
        edid[offset + 0] = 0x02;   // Etiqueta CTA.
        edid[offset + 1] = 0x03;   // Revisión 3.
        edid[offset + 3] = 0x70;   // Audio básico, YCbCr 4:4:4 y 4:2:2.

        int cursor = offset + 4;

        // Bloque de audio: un LPCM de 2 canales a 44,1 y 48 kHz, 16 y 24 bits.
        edid[cursor++] = (1 << 5) | 3;
        edid[cursor++] = 0x09;     // Formato 1 (LPCM), 2 canales.
        edid[cursor++] = 0x06;     // 44,1 y 48 kHz.
        edid[cursor++] = 0x05;     // 16 y 24 bits.

        // Bloque de vídeo: VIC 16 (1080p60) y VIC 4 (720p60).
        edid[cursor++] = (2 << 5) | 2;
        edid[cursor++] = 16;
        edid[cursor++] = 4;

        // Bloque extendido 0x06: HDR estático con SDR, PQ y HLG.
        edid[cursor++] = (7 << 5) | 3;
        edid[cursor++] = 0x06;
        edid[cursor++] = 0x0D;     // Bits 0, 2 y 3.
        edid[cursor++] = 0x00;

        // Los timings detallados empiezan donde acaban los bloques de datos. Aquí no hay
        // ninguno, así que el parser encontrará ceros y parará.
        edid[offset + 2] = (byte)(cursor - offset);
    }

    public static byte Checksum(ReadOnlySpan<byte> block)
    {
        int sum = 0;
        for (int i = 0; i < block.Length - 1; i++)
        {
            sum += block[i];
        }

        return (byte)((256 - (sum % 256)) % 256);
    }
}
