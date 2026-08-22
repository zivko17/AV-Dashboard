using AvDashboard.Core.Models;
using AvDashboard.Core.Video;
using Xunit;

namespace AvDashboard.Core.Tests;

/// <summary>
/// `edid-diff` es, según el diseño, la función más útil del conjunto: por qué el
/// procesador ve un modo y el splitter reporta otro.
/// </summary>
public class EdidComparerTests
{
    private static Edid Parse(byte[] bytes) => EdidParser.Parse(bytes);

    private static Edid Modified(Action<byte[]> change)
    {
        byte[] bytes = EdidBuilder.BuildBaseOnly();
        change(bytes);
        bytes[127] = EdidBuilder.Checksum(bytes);
        return EdidParser.Parse(bytes);
    }

    [Fact]
    public void Dos_edid_identicos_no_producen_diferencias()
    {
        Edid a = Parse(EdidBuilder.BuildValid());
        Edid b = Parse(EdidBuilder.BuildValid());

        Assert.Empty(EdidComparer.Compare(a, b));
    }

    [Fact]
    public void Un_modo_preferido_distinto_es_una_diferencia_significativa()
    {
        Edid original = Parse(EdidBuilder.BuildBaseOnly());

        // 1280 en vez de 1920 de activo horizontal.
        Edid cambiado = Modified(b =>
        {
            b[54 + 2] = 0x00;
            b[54 + 4] = (byte)((5 << 4) | 1);
        });

        IReadOnlyList<EdidDifference> diff = EdidComparer.Compare(original, cambiado);

        Assert.Contains(diff, d => d.Field == "Modo preferido");
        Assert.True(EdidComparer.HasSignificantDifferences(diff));
    }

    [Fact]
    public void Un_numero_de_serie_distinto_no_es_significativo()
    {
        // Dos unidades del mismo modelo difieren en la serie y son intercambiables.
        Edid original = Parse(EdidBuilder.BuildBaseOnly());
        Edid otraUnidad = Modified(b => b[12] = 0x99);

        IReadOnlyList<EdidDifference> diff = EdidComparer.Compare(original, otraUnidad);

        EdidDifference serie = Assert.Single(diff, d => d.Field == "Número de serie");
        Assert.False(serie.Significant);
        Assert.False(EdidComparer.HasSignificantDifferences(diff));
    }

    [Fact]
    public void Detecta_un_cambio_en_los_limites_declarados()
    {
        Edid original = Parse(EdidBuilder.BuildBaseOnly());
        Edid recortado = Modified(b => b[72 + 6] = 60);

        Assert.Contains(EdidComparer.Compare(original, recortado),
            d => d.Field == "Límites declarados" && d.Significant);
    }

    [Fact]
    public void Detecta_que_una_extension_cta_ha_desaparecido()
    {
        // Es el caso de «alguien ha metido un splitter en medio»: se pierde el audio.
        Edid conCta = Parse(EdidBuilder.BuildValid());
        Edid sinCta = Parse(EdidBuilder.BuildBaseOnly());

        IReadOnlyList<EdidDifference> diff = EdidComparer.Compare(conCta, sinCta);

        EdidDifference cta = Assert.Single(diff, d => d.Field == "Extensión CTA-861");
        Assert.Equal("presente", cta.Left);
        Assert.Equal("ausente", cta.Right);
        Assert.True(cta.Significant);
    }

    [Fact]
    public void Los_conjuntos_de_timings_se_comparan_por_lo_que_sobra_y_lo_que_falta()
    {
        Edid original = Parse(EdidBuilder.BuildBaseOnly());

        // Se quita 1024×768@60 del mapa de timings establecidos.
        Edid recortado = Modified(b => b[36] = 0x00);

        EdidDifference diff = Assert.Single(
            EdidComparer.Compare(original, recortado),
            d => d.Field == "Timings establecidos");

        Assert.Equal("1024×768@60", diff.Left);
        Assert.Equal("—", diff.Right);
    }

    [Fact]
    public void Detecta_un_cambio_de_interfaz_digital()
    {
        Edid hdmi = Parse(EdidBuilder.BuildBaseOnly());
        Edid displayPort = Modified(b => b[20] = 0xA5);

        Assert.Contains(EdidComparer.Compare(hdmi, displayPort),
            d => d.Field == "Interfaz" && d.Left == "HDMIa" && d.Right == "DisplayPort");
    }

    [Fact]
    public void Detecta_un_cambio_de_fabricante_o_de_modelo()
    {
        Edid original = Parse(EdidBuilder.BuildBaseOnly());
        Edid otro = Modified(b => b[10] = 0x99);

        Assert.Contains(EdidComparer.Compare(original, otro),
            d => d.Field == "Código de producto" && d.Significant);
    }
}
