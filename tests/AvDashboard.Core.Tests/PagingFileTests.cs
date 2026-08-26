using AvDashboard.Core.Settings;
using Xunit;

namespace AvDashboard.Core.Tests;

/// <summary>
/// El archivo de paginación, que Windows guarda como una lista de texto con los números al
/// final de cada línea.
///
/// Las cadenas de ejemplo salen de máquinas reales o del diálogo de memoria virtual: el
/// comodín es lo que hay en un equipo recién instalado, y el resto son las cuatro formas que
/// tiene el diálogo de dejarlo.
/// </summary>
public class PagingFileTests
{
    [Fact]
    public void El_comodin_es_la_casilla_de_gestion_automatica()
    {
        // Tal cual está en la máquina de trabajo. El interrogante no es un error de lectura:
        // es como Windows dice «el disco lo elijo yo».
        IReadOnlyList<PagingFileEntry> entradas = PagingFile.Parse(new[] { @"?:\pagefile.sys" });

        Assert.True(entradas[0].IsAutomatic);
        Assert.True(entradas[0].IsSystemManaged);
        Assert.False(entradas[0].IsFixed);
        Assert.Equal(PagingFileMode.SystemManaged, PagingFile.Classify(entradas));
    }

    [Fact]
    public void Minimo_y_maximo_iguales_es_el_unico_modo_que_vale()
    {
        Assert.Equal(PagingFileMode.Fixed,
            PagingFile.Classify(new[] { @"C:\pagefile.sys 16384 16384" }));

        Assert.Equal(PagingFile.Target,
            PagingFile.Canonical(PagingFileMode.Fixed));
    }

    [Fact]
    public void Un_maximo_mayor_que_el_minimo_es_creciente_y_no_fijo()
    {
        // El caso que motiva la fila entera: tiene tamaño inicial, parece configurado, y aun
        // asi Windows puede reservar mas disco a mitad de show.
        Assert.Equal(PagingFileMode.Growing,
            PagingFile.Classify(new[] { @"C:\pagefile.sys 8192 32768" }));
    }

    [Fact]
    public void Ceros_es_gestionado_por_Windows_aunque_el_disco_este_elegido()
    {
        // «Tamaño administrado por el sistema» para un disco concreto. La casilla general
        // esta desmarcada, pero el tamano sigue decidiendolo Windows.
        Assert.Equal(PagingFileMode.SystemManaged,
            PagingFile.Classify(new[] { @"D:\pagefile.sys 0 0" }));
    }

    [Fact]
    public void Sin_lineas_es_desactivado()
    {
        Assert.Equal(PagingFileMode.Disabled, PagingFile.Classify(Array.Empty<string>()));
        Assert.Equal(PagingFileMode.Disabled, PagingFile.Classify((IEnumerable<string>?)null));
    }

    [Fact]
    public void Las_lineas_vacias_no_cuentan_como_entradas()
    {
        // REG_MULTI_SZ termina en cadena vacia y hay lectores que la devuelven. Contarla
        // convertiria un archivo fijo en «mixto» y pintaria de rojo una maquina correcta.
        Assert.Equal(PagingFileMode.Fixed,
            PagingFile.Classify(new[] { @"C:\pagefile.sys 16384 16384", "", "   " }));
    }

    [Fact]
    public void Dos_discos_distintos_son_mixto()
    {
        Assert.Equal(PagingFileMode.Mixed, PagingFile.Classify(new[]
        {
            @"C:\pagefile.sys 16384 16384",
            @"D:\pagefile.sys 0 0",
        }));
    }

    [Fact]
    public void Una_ruta_con_espacios_no_se_parte_por_el_primero()
    {
        // Partir por espacios dejaria la ruta en «D:\Archivos» y los tamanos en cero, o sea
        // gestionado por Windows: exactamente el modo que la fila esta buscando. Los numeros
        // se leen por el final justo para que esto no pase.
        IReadOnlyList<PagingFileEntry> entradas = PagingFile.Parse(
            new[] { @"D:\Archivos de paginacion\pagefile.sys 16384 16384" });

        Assert.Equal(@"D:\Archivos de paginacion\pagefile.sys", entradas[0].Path);
        Assert.True(entradas[0].IsFixed);
        Assert.Equal("D:", entradas[0].Drive);
    }

    [Fact]
    public void La_linea_se_devuelve_con_el_formato_que_espera_el_registro()
    {
        // Se vuelve a escribir en el mismo valor, asi que la forma tiene que ser la que
        // Windows sabe leer. El comodin va sin numeros; los demas, con dos.
        Assert.Equal(@"C:\pagefile.sys 16384 16384",
            new PagingFileEntry(@"C:\pagefile.sys", 16384, 16384).ToString());

        Assert.Equal(@"?:\pagefile.sys",
            new PagingFileEntry(@"?:\pagefile.sys", 0, 0).ToString());
    }

    [Fact]
    public void Lo_que_se_ensena_dice_el_disco_y_el_tamano()
    {
        Assert.Equal("C: fijo en 16 GB",
            PagingFile.Describe(PagingFile.Parse(new[] { @"C:\pagefile.sys 16384 16384" })));

        Assert.Equal("C: de 8 GB a 32 GB",
            PagingFile.Describe(PagingFile.Parse(new[] { @"C:\pagefile.sys 8192 32768" })));

        Assert.Equal("Sin archivo de paginación",
            PagingFile.Describe(Array.Empty<PagingFileEntry>()));
    }

    [Fact]
    public void Un_tamano_que_no_son_gigas_enteros_se_ensena_en_MB()
    {
        Assert.Equal("C: fijo en 4680 MB",
            PagingFile.Describe(PagingFile.Parse(new[] { @"C:\pagefile.sys 4680 4680" })));
    }

    [Fact]
    public void La_propuesta_es_un_cuarto_de_la_RAM_en_gigas_enteros()
    {
        // 61,7 GB de la maquina de trabajo. Un cuarto son 15,4, que se redondea a 15.
        int? propuesta = PagingFile.RecommendMb(66_182_512_640L, 350L * 1024 * 1024 * 1024);

        Assert.NotNull(propuesta);
        Assert.Equal(0, propuesta!.Value % 1024);
        Assert.Equal(15, propuesta.Value / 1024);
    }

    [Fact]
    public void Con_poca_RAM_no_baja_del_suelo()
    {
        // Un cuarto de 8 GB serian 2, y con 2 GB Windows empieza a rechazar reservas cuando
        // una aplicacion pide de golpe. El suelo esta para eso.
        Assert.Equal(8 * 1024, PagingFile.RecommendMb(8L * 1024 * 1024 * 1024, 500L * 1024 * 1024 * 1024));
    }

    [Fact]
    public void Con_mucha_RAM_no_pasa_del_techo()
    {
        // Un cuarto de 256 GB serian 64. La regla vieja de «vez y media la RAM» daria 384.
        Assert.Equal(32 * 1024, PagingFile.RecommendMb(256L * 1024 * 1024 * 1024, 4L * 1024 * 1024 * 1024 * 1024));
    }

    [Fact]
    public void Nunca_se_come_mas_de_un_quinto_de_lo_libre()
    {
        // 64 GB de RAM pero solo 60 GB libres: proponer 16 seria quitarle a los medios mas
        // de lo que vale el arreglo.
        int? propuesta = PagingFile.RecommendMb(64L * 1024 * 1024 * 1024, 60L * 1024 * 1024 * 1024);

        Assert.Equal(12 * 1024, propuesta);
    }

    [Fact]
    public void Con_el_disco_lleno_no_se_propone_nada()
    {
        // Por debajo de 4 GB el problema no es la paginacion. Devolver un numero pequeno
        // seria contestar a la pregunta equivocada y dejar la fila en verde.
        Assert.Null(PagingFile.RecommendMb(64L * 1024 * 1024 * 1024, 10L * 1024 * 1024 * 1024));
        Assert.Null(PagingFile.RecommendMb(0, 500L * 1024 * 1024 * 1024));
    }
}
