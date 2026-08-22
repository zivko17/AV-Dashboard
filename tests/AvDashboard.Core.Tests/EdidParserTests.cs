using AvDashboard.Core.Models;
using AvDashboard.Core.Video;
using Xunit;

namespace AvDashboard.Core.Tests;

/// <summary>
/// Un parser de EDID mal escrito no revienta: devuelve datos plausibles y equivocados.
/// Por eso cada aserción va contra bytes que pone la propia prueba.
/// </summary>
public class EdidParserTests
{
    private static Edid Parse() => EdidParser.Parse(EdidBuilder.BuildValid());

    [Fact]
    public void Rechaza_lo_que_no_lleva_la_cabecera_de_edid()
    {
        byte[] basura = new byte[128];

        Assert.False(EdidParser.TryParse(basura, out Edid? edid, out string? error));
        Assert.Null(edid);
        Assert.Contains("cabecera", error);
    }

    [Fact]
    public void Rechaza_un_volcado_mas_corto_que_un_bloque()
    {
        Assert.False(EdidParser.TryParse(new byte[64], out _, out string? error));
        Assert.Contains("128", error);
    }

    [Fact]
    public void Desempaqueta_el_identificador_de_fabricante_de_tres_letras()
    {
        // Tres letras de 5 bits en dos bytes big-endian, con 'A' = 1.
        Assert.Equal("MSI", EdidParser.ParseManufacturer(0x36, 0x69));
        Assert.Equal("AAA", EdidParser.ParseManufacturer(0x04, 0x21));
    }

    [Fact]
    public void Un_fabricante_fuera_de_rango_no_se_inventa()
    {
        Assert.Equal("???", EdidParser.ParseManufacturer(0x00, 0x00));
    }

    [Fact]
    public void Lee_la_identidad_del_monitor()
    {
        Edid edid = Parse();

        Assert.Equal("MSI", edid.ManufacturerId);
        Assert.Equal(0x1234, edid.ProductCode);
        Assert.Equal(0x11223344u, edid.SerialNumber);
        Assert.Equal("MSI MP243X", edid.MonitorName);
        Assert.Equal("ABC123", edid.SerialText);
    }

    [Fact]
    public void Lee_la_fecha_de_fabricacion()
    {
        Edid edid = Parse();

        Assert.Equal(20, edid.ManufactureWeek);
        Assert.Equal(2024, edid.ManufactureYear);
        Assert.False(edid.YearIsModelYear);
    }

    [Fact]
    public void Una_semana_0xFF_significa_ano_de_modelo_no_de_fabricacion()
    {
        byte[] bytes = EdidBuilder.BuildBaseOnly();
        bytes[16] = 0xFF;
        bytes[127] = EdidBuilder.Checksum(bytes);

        Edid edid = EdidParser.Parse(bytes);

        Assert.True(edid.YearIsModelYear);
        Assert.Null(edid.ManufactureWeek);
        Assert.Equal(2024, edid.ManufactureYear);
    }

    [Fact]
    public void Decodifica_la_entrada_digital_con_sus_bits_y_su_interfaz()
    {
        Edid edid = Parse();

        Assert.True(edid.IsDigitalInput);
        Assert.Equal(8, edid.BitsPerColor);
        Assert.Equal("HDMIa", edid.DigitalInterface);
    }

    [Fact]
    public void Una_entrada_analogica_no_declara_bits_ni_interfaz()
    {
        byte[] bytes = EdidBuilder.BuildBaseOnly();
        bytes[20] = 0x0F;               // Bit 7 a cero: analógica.
        bytes[127] = EdidBuilder.Checksum(bytes);

        Edid edid = EdidParser.Parse(bytes);

        Assert.False(edid.IsDigitalInput);
        Assert.Null(edid.BitsPerColor);
        Assert.Null(edid.DigitalInterface);
    }

    [Fact]
    public void Lee_el_tamano_fisico_y_la_gamma()
    {
        Edid edid = Parse();

        Assert.Equal(60, edid.HorizontalSizeCm);
        Assert.Equal(34, edid.VerticalSizeCm);
        Assert.Equal(2.2, edid.Gamma!.Value, precision: 3);
    }

    [Fact]
    public void Una_gamma_de_0xFF_significa_no_declarada()
    {
        byte[] bytes = EdidBuilder.BuildBaseOnly();
        bytes[23] = 0xFF;
        bytes[127] = EdidBuilder.Checksum(bytes);

        Assert.Null(EdidParser.Parse(bytes).Gamma);
    }

    [Fact]
    public void Decodifica_el_mapa_de_bits_de_timings_establecidos()
    {
        Edid edid = Parse();

        Assert.Equal(
            new[] { "640×480@60", "800×600@60", "1024×768@60", "1152×870@75" },
            edid.EstablishedTimings);
    }

    [Fact]
    public void Decodifica_los_timings_estandar_con_su_relacion_de_aspecto()
    {
        StandardTiming timing = Assert.Single(Parse().StandardTimings);

        Assert.Equal(1920, timing.Width);
        Assert.Equal(1080, timing.Height);
        Assert.Equal(60, timing.RefreshHz);
        Assert.Equal("16:9", timing.AspectRatio);
    }

    [Fact]
    public void Las_entradas_de_relleno_de_los_timings_estandar_se_ignoran()
    {
        // Los siete huecos sin usar van a 01 01 y no deben aparecer como modos.
        Assert.Single(Parse().StandardTimings);
    }

    [Fact]
    public void Decodifica_el_modo_preferido_con_sus_totales_y_su_refresco()
    {
        DetailedTiming preferred = Parse().PreferredTiming!;

        Assert.Equal(148500, preferred.PixelClockKhz);
        Assert.Equal(1920, preferred.HorizontalActive);
        Assert.Equal(280, preferred.HorizontalBlanking);
        Assert.Equal(1080, preferred.VerticalActive);
        Assert.Equal(45, preferred.VerticalBlanking);
        Assert.Equal(2200, preferred.HorizontalTotal);
        Assert.Equal(1125, preferred.VerticalTotal);
        Assert.False(preferred.Interlaced);
        Assert.Equal(60.0, preferred.RefreshHz!.Value, precision: 4);
    }

    [Fact]
    public void Decodifica_los_pulsos_de_sincronismo_del_modo_preferido()
    {
        DetailedTiming preferred = Parse().PreferredTiming!;

        Assert.Equal(88, preferred.HorizontalSyncOffset);
        Assert.Equal(44, preferred.HorizontalSyncPulse);
        Assert.Equal(4, preferred.VerticalSyncOffset);
        Assert.Equal(5, preferred.VerticalSyncPulse);
    }

    [Fact]
    public void Decodifica_el_tamano_de_imagen_del_modo_preferido()
    {
        DetailedTiming preferred = Parse().PreferredTiming!;

        Assert.Equal(598, preferred.HorizontalSizeMm);
        Assert.Equal(336, preferred.VerticalSizeMm);
    }

    [Fact]
    public void Decodifica_los_limites_declarados()
    {
        DisplayRangeLimits limits = Parse().RangeLimits!;

        Assert.Equal(48, limits.MinVerticalHz);
        Assert.Equal(75, limits.MaxVerticalHz);
        Assert.Equal(30, limits.MinHorizontalKhz);
        Assert.Equal(83, limits.MaxHorizontalKhz);
        Assert.Equal(170, limits.MaxPixelClockMhz);
    }

    [Fact]
    public void Los_desplazamientos_de_edid_1_4_permiten_declarar_mas_de_255_hz()
    {
        byte[] bytes = EdidBuilder.BuildBaseOnly();
        bytes[72 + 4] = 0x02;    // Al máximo vertical hay que sumarle 255.
        bytes[72 + 6] = 5;       // 5 + 255 = 260 Hz.
        bytes[127] = EdidBuilder.Checksum(bytes);

        Assert.Equal(260, EdidParser.Parse(bytes).RangeLimits!.MaxVerticalHz);
    }

    [Fact]
    public void Decodifica_la_extension_cta_y_sus_banderas()
    {
        CtaExtension cta = Parse().Cta!;

        Assert.Equal(3, cta.Revision);
        Assert.False(cta.UnderscanSupported);
        Assert.True(cta.BasicAudioSupported);
        Assert.True(cta.YCbCr444Supported);
        Assert.True(cta.YCbCr422Supported);
    }

    [Fact]
    public void Decodifica_los_descriptores_cortos_de_audio()
    {
        ShortAudioDescriptor audio = Assert.Single(Parse().Cta!.AudioDescriptors);

        Assert.Equal(1, audio.FormatCode);
        Assert.Equal("LPCM", audio.FormatName);
        Assert.Equal(2, audio.MaxChannels);
        Assert.Equal(new[] { 44100, 48000 }, audio.SampleRates);
        Assert.Equal(new[] { 16, 24 }, audio.BitDepths);
    }

    [Fact]
    public void Un_formato_comprimido_no_declara_profundidades()
    {
        // El tercer byte solo significa profundidades en LPCM.
        ShortAudioDescriptor audio = EdidParser.ParseShortAudioDescriptor(new byte[] { 0x15, 0x07, 0x50 });

        Assert.Equal("AC-3", audio.FormatName);
        Assert.Equal(6, audio.MaxChannels);
        Assert.Empty(audio.BitDepths);
    }

    [Fact]
    public void Decodifica_los_codigos_vic()
    {
        Assert.Equal(new[] { 16, 4 }, Parse().Cta!.VideoIdentificationCodes);
    }

    [Fact]
    public void Decodifica_los_metadatos_hdr_bit_a_bit()
    {
        HdrStaticMetadata hdr = Parse().Cta!.Hdr!;

        Assert.True(hdr.TraditionalSdr);
        Assert.False(hdr.TraditionalHdr);
        Assert.True(hdr.Pq);
        Assert.True(hdr.HybridLogGamma);
        Assert.Equal(new[] { "SDR", "PQ (ST 2084)", "HLG" }, hdr.SupportedEotf);
    }

    [Fact]
    public void Un_edid_sin_extensiones_no_trae_bloque_cta()
    {
        Edid edid = EdidParser.Parse(EdidBuilder.BuildBaseOnly());

        Assert.Equal(0, edid.ExtensionCount);
        Assert.Null(edid.Cta);
    }

    [Fact]
    public void La_etiqueta_de_listado_usa_el_nombre_cuando_lo_hay()
    {
        Assert.Equal("MSI 1234 · MSI MP243X", Parse().DisplayLabel);
    }
}

public class EdidValidatorTests
{
    [Fact]
    public void Un_edid_coherente_no_genera_ningun_aviso()
    {
        Assert.Empty(EdidValidator.Validate(EdidBuilder.BuildValid()));
    }

    [Fact]
    public void Detecta_un_checksum_que_no_cuadra()
    {
        byte[] bytes = EdidBuilder.BuildValid();
        bytes[10] ^= 0xFF;   // Se cambia el producto sin recalcular el checksum.

        Assert.Contains(EdidValidator.Validate(bytes), f => f.Code == "edid.checksum");
    }

    [Fact]
    public void Detecta_que_faltan_bloques_de_extension_anunciados()
    {
        byte[] bytes = EdidBuilder.BuildBaseOnly();
        bytes[126] = 2;      // Anuncia dos extensiones que no vienen.
        bytes[127] = EdidBuilder.Checksum(bytes);

        Assert.Contains(EdidValidator.Validate(bytes), f => f.Code == "edid.extension-count");
    }

    [Fact]
    public void Detecta_un_timing_por_encima_del_refresco_declarado()
    {
        // Es el caso que hace que un procesador negocie un modo que luego no sostiene.
        byte[] bytes = EdidBuilder.BuildBaseOnly();
        bytes[72 + 6] = 50;  // Máximo vertical 50 Hz, con un preferido de 60.
        bytes[127] = EdidBuilder.Checksum(bytes);

        Assert.Contains(EdidValidator.Validate(bytes), f => f.Code == "edid.refresh-out-of-range");
    }

    [Fact]
    public void Detecta_un_timing_por_encima_del_reloj_maximo_declarado()
    {
        byte[] bytes = EdidBuilder.BuildBaseOnly();
        bytes[72 + 9] = 10;  // 100 MHz máximos, con un preferido que pide 148,5.
        bytes[127] = EdidBuilder.Checksum(bytes);

        Assert.Contains(EdidValidator.Validate(bytes), f => f.Code == "edid.pixelclock-out-of-range");
    }

    [Fact]
    public void Un_volcado_truncado_se_reporta_como_tal()
    {
        byte[] bytes = EdidBuilder.BuildValid()[..200];

        Assert.Contains(EdidValidator.Validate(bytes), f => f.Code == "edid.truncated");
    }

    [Fact]
    public void El_veredicto_agregado_se_queda_con_lo_peor()
    {
        byte[] bytes = EdidBuilder.BuildValid();
        bytes[10] ^= 0xFF;

        Assert.Equal(HealthState.Critical, EdidValidator.Evaluate(EdidValidator.Validate(bytes)));
        Assert.Equal(HealthState.Ok, EdidValidator.Evaluate(EdidValidator.Validate(EdidBuilder.BuildValid())));
    }
}
