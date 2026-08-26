namespace AvDashboard.Core.Settings;

/// <summary>
/// Las preferencias globales de DirectX, que Windows guarda como una sola cadena.
///
/// En <c>HKCU\Software\Microsoft\DirectX\UserGpuPreferences</c>, el valor
/// <c>DirectXUserGlobalSettings</c> contiene todos los interruptores juntos, separados por
/// punto y coma:
///
/// <code>SwapEffectUpgradeEnable=1;VRROptimizeEnable=0;AutoHDREnable=0;</code>
///
/// Es un formato incómodo y por eso el parseo vive aquí y no en la sonda: escribir uno de
/// los tres significa reescribir la cadena entera conservando los otros dos, y hacerlo a
/// mano en la capa nativa es la manera de perder el que no se estaba tocando.
/// </summary>
public static class DirectXGlobalSettings
{
    /// <summary>«Optimizaciones para juegos en ventana». En un servidor de medios, off.</summary>
    public const string SwapEffectUpgrade = "SwapEffectUpgradeEnable";

    /// <summary>Frecuencia de actualización variable. En un servidor de medios, off.</summary>
    public const string VariableRefreshRate = "VRROptimizeEnable";

    public const string AutoHdr = "AutoHDREnable";

    /// <summary>
    /// Los pares de la cadena, en el orden en que venían.
    ///
    /// El orden importa al reescribir: Windows la regenera con sus claves y devolverla
    /// barajada no rompe nada, pero un diff del registro deja de ser legible.
    /// </summary>
    public static IReadOnlyList<KeyValuePair<string, string>> Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Array.Empty<KeyValuePair<string, string>>();
        }

        List<KeyValuePair<string, string>> pairs = new();

        foreach (string chunk in raw.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            int equals = chunk.IndexOf('=', StringComparison.Ordinal);

            if (equals <= 0)
            {
                // Un trozo sin «=» no es un par. Se ignora en lugar de reventar: esta cadena
                // la escribe Windows y puede traer cosas que esta version no conoce.
                continue;
            }

            pairs.Add(new KeyValuePair<string, string>(
                chunk[..equals].Trim(), chunk[(equals + 1)..].Trim()));
        }

        return pairs;
    }

    /// <summary>
    /// El valor de un interruptor, o null si la cadena no lo trae.
    ///
    /// Ausente **no** es «apagado»: significa que Windows aplica su valor por defecto, y en
    /// estos tres es «encendido». Devolver false aquí sería el mismo fallo que ya costó dos
    /// filas verdes falsas en la sonda de red.
    /// </summary>
    public static bool? Read(string? raw, string key)
    {
        foreach (KeyValuePair<string, string> pair in Parse(raw))
        {
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value switch
                {
                    "1" => true,
                    "0" => false,
                    _ => null,
                };
            }
        }

        return null;
    }

    /// <summary>
    /// Lo que Windows hace cuando el interruptor no está en la cadena.
    ///
    /// Los tres vienen activados de fábrica. Es lo que permite que la fila diga «activado»
    /// en vez de «sin leer» en una máquina donde nadie ha tocado nunca esa pantalla, que es
    /// justo la máquina donde hace falta que lo diga.
    /// </summary>
    public const bool DefaultWhenAbsent = true;

    /// <summary>El valor efectivo: lo que dice la cadena, o el de fábrica si no dice nada.</summary>
    public static bool Effective(string? raw, string key) =>
        Read(raw, key) ?? DefaultWhenAbsent;

    /// <summary>
    /// La cadena con un interruptor cambiado y los demás intactos.
    ///
    /// Reescribir solo el que se toca es todo el motivo de que esto exista: apagar VRR
    /// regenerando la cadena de cero se llevaría por delante la preferencia de GPU por
    /// aplicación que el usuario tenga puesta al lado.
    /// </summary>
    public static string With(string? raw, string key, bool enabled)
    {
        List<KeyValuePair<string, string>> pairs = Parse(raw).ToList();
        string value = enabled ? "1" : "0";

        int index = pairs.FindIndex(p =>
            string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));

        if (index >= 0)
        {
            pairs[index] = new KeyValuePair<string, string>(pairs[index].Key, value);
        }
        else
        {
            pairs.Add(new KeyValuePair<string, string>(key, value));
        }

        // Con punto y coma final, que es como la escribe Windows.
        return string.Concat(pairs.Select(p => $"{p.Key}={p.Value};"));
    }
}
