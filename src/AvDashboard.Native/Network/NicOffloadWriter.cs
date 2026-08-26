using System.Management;
using System.Runtime.Versioning;
using AvDashboard.Core.Networking;

namespace AvDashboard.Native.Network;

/// <summary>
/// Apaga las descargas del adaptador de red (M5).
///
/// Es el actuador que más veces salva un evento y el que menos se ve: Large Send Offload,
/// la moderación de interrupciones y Energy Efficient Ethernet rompen la temporización de
/// Dante y de NDI, no aparecen en ninguna pantalla de red normal, y el síntoma —audio que
/// cruje, vídeo que salta— no apunta a la tarjeta de red por ningún lado.
///
/// **Corta el enlace mientras se aplica.** Cambiar una propiedad avanzada reinicia el
/// adaptador, así que el enlace se cae un segundo o dos. Por eso el ajuste está declarado
/// como <c>SettingRisk.Disruptive</c> en el catálogo y no entra en un barrido automático.
/// </summary>
[SupportedOSPlatform("windows")]
public static class NicOffloadWriter
{
    private const string Scope = @"root\StandardCimv2";
    private const string Class = "MSFT_NetAdapterAdvancedPropertySettingData";

    /// <summary>Lo que está encendido ahora mismo y habría que apagar.</summary>
    public static NicOffloadPlan Read()
    {
        Platform.EnsureWindows();

        List<NicAdvancedProperty> properties = new();

        using ManagementObjectSearcher searcher = new(
            new ManagementScope(Scope),
            new ObjectQuery($"SELECT Name, RegistryKeyword, RegistryValue FROM {Class}"));

        foreach (ManagementBaseObject item in searcher.Get())
        {
            using (item)
            {
                if (item["RegistryKeyword"] is string keyword && item["Name"] is string adapter)
                {
                    properties.Add(new NicAdvancedProperty(adapter, keyword,
                        item["RegistryValue"] is string[] { Length: > 0 } values ? values[0] : null));
                }
            }
        }

        return NicOffloadPolicy.Plan(properties);
    }

    /// <summary>
    /// Apaga todo lo que la política declare encendido, y comprueba que se ha quedado apagado.
    ///
    /// La comprobación final no es paranoia: escribir aquí puede devolver éxito y no surtir
    /// efecto —el driver rechaza el valor en silencio, o una directiva lo repone— y ya se ha
    /// pagado una vez el precio de dar por bueno lo que devuelve una API sin mirar el
    /// resultado. Lo que se anota en el Timeline tiene que ser lo que ha pasado.
    /// </summary>
    public static NicOffloadResult DisableAll(bool dryRun = false)
    {
        Platform.EnsureWindows();

        NicOffloadPlan before = Read();

        if (before.IsClean)
        {
            return new NicOffloadResult { Applied = 0, Remaining = before, AlreadyClean = true };
        }

        if (dryRun)
        {
            return new NicOffloadResult { Applied = before.Offending.Count, Remaining = before, DryRun = true };
        }

        int applied = 0;
        List<string> failures = new();

        foreach (NicAdvancedProperty target in before.Offending)
        {
            try
            {
                // Se busca la instancia concreta por adaptador y palabra clave. Escribir
                // sobre el resultado de la consulta amplia no vale: ManagementObject solo
                // sabe hacer Put() si conoce su propia ruta, y la de una proyeccion no la
                // tiene.
                string query =
                    $"SELECT * FROM {Class} WHERE Name = '{Escape(target.Adapter)}' " +
                    $"AND RegistryKeyword = '{Escape(target.Keyword)}'";

                using ManagementObjectSearcher searcher =
                    new(new ManagementScope(Scope), new ObjectQuery(query));

                foreach (ManagementBaseObject found in searcher.Get())
                {
                    using ManagementObject instance = (ManagementObject)found;

                    instance["RegistryValue"] = new[] { NicOffloadPolicy.Disabled };
                    instance.Put();

                    applied++;
                }
            }
            catch (ManagementException ex)
            {
                // Un adaptador que rechaza el cambio no puede dejar los demas sin tocar: en
                // un rig con dos NIC, que falle la que no usas no es motivo para dejar la de
                // Dante como estaba.
                failures.Add($"{target.Adapter} · {target.Keyword}: {ex.Message}");
            }
            catch (UnauthorizedAccessException)
            {
                failures.Add($"{target.Adapter} · {target.Keyword}: sin permisos");
            }
        }

        return new NicOffloadResult
        {
            Applied = applied,
            Remaining = Read(),
            Failures = failures,
        };
    }

    /// <summary>
    /// Comillas simples en WQL. Los nombres de adaptador los pone el fabricante y llevan
    /// apostrofes mas veces de lo que parece.
    /// </summary>
    private static string Escape(string value) => value.Replace("'", "\\'", StringComparison.Ordinal);
}

/// <summary>Lo que ha pasado al apagar las descargas.</summary>
public sealed record NicOffloadResult
{
    public int Applied { get; init; }

    /// <summary>Lo que sigue encendido despues de escribir. Vacio es el resultado bueno.</summary>
    public NicOffloadPlan Remaining { get; init; } = new();

    public IReadOnlyList<string> Failures { get; init; } = Array.Empty<string>();

    public bool DryRun { get; init; }
    public bool AlreadyClean { get; init; }

    /// <summary>
    /// Exito es que no quede nada encendido, no que las escrituras no hayan lanzado.
    /// </summary>
    public bool Success => DryRun || AlreadyClean || Remaining.IsClean;
}
