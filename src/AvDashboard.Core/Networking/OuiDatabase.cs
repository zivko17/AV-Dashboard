namespace AvDashboard.Core.Networking;

/// <summary>
/// Resolucion de fabricante por prefijo de MAC, sin internet.
///
/// La base oficial de IEEE son tres ficheros (MA-L de 24 bits, MA-M de 28 y MA-S de 36)
/// y ocupa varios megas, asi que no viaja en el repositorio: se descarga una vez con
/// `scripts/Update-OuiDatabase.ps1` y queda junto al ejecutable. Mientras no este,
/// funciona la semilla minima de abajo y el resto de dispositivos aparecen sin
/// fabricante, que es preferible a aparecer con uno equivocado.
/// </summary>
public sealed class OuiDatabase
{
    // Prefijos indexados por longitud en digitos hexadecimales: 6 (MA-L), 7 (MA-M), 9 (MA-S).
    private readonly Dictionary<string, string> _24Bit;
    private readonly Dictionary<string, string> _28Bit = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _36Bit = new(StringComparer.Ordinal);

    /// <summary>
    /// Semilla minima. Solo prefijos MA-L de 24 bits que aparecen constantemente en un
    /// rig y que estan verificados. No se amplia a ojo: para eso esta el fichero de IEEE.
    /// </summary>
    private static readonly (string Prefix, string Vendor)[] Seed =
    {
        ("001DC1", "Audinate"),
        ("7C2E0D", "Blackmagic Design"),
        ("00A0DE", "Yamaha"),
        ("00000C", "Cisco Systems"),
        ("000C29", "VMware"),
        ("005056", "VMware"),
        ("080027", "Oracle VirtualBox"),
        ("B827EB", "Raspberry Pi Foundation"),
        ("DCA632", "Raspberry Pi Trading"),
        ("E45F01", "Raspberry Pi Trading"),
    };

    public OuiDatabase()
    {
        _24Bit = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string prefix, string vendor) in Seed)
        {
            _24Bit[prefix] = vendor;
        }
    }

    /// <summary>Numero de prefijos cargados. La UI lo usa para avisar de que solo hay semilla.</summary>
    public int Count => _24Bit.Count + _28Bit.Count + _36Bit.Count;

    /// <summary>true cuando solo esta la semilla y conviene sugerir la descarga de IEEE.</summary>
    public bool IsSeedOnly => Count <= Seed.Length;

    /// <summary>
    /// Carga el CSV de IEEE (Registry,Assignment,Organization Name,Organization Address).
    /// Acepta los tres ficheros: la longitud del campo Assignment determina el tramo.
    /// </summary>
    public int LoadIeeeCsv(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        int loaded = 0;
        bool first = true;

        while (reader.ReadLine() is string line)
        {
            if (first)
            {
                first = false;
                if (line.StartsWith("Registry", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
            }

            if (line.Length == 0)
            {
                continue;
            }

            string[] fields = ParseCsvLine(line);
            if (fields.Length < 3)
            {
                continue;
            }

            string assignment = fields[1].Trim().Replace("-", string.Empty).Replace(":", string.Empty).ToUpperInvariant();
            string vendor = fields[2].Trim();

            if (vendor.Length == 0 || !IsHex(assignment))
            {
                continue;
            }

            Dictionary<string, string>? target = assignment.Length switch
            {
                6 => _24Bit,
                7 => _28Bit,
                9 => _36Bit,
                _ => null,
            };

            if (target is null)
            {
                continue;
            }

            target[assignment] = vendor;
            loaded++;
        }

        return loaded;
    }

    /// <summary>Carga el fichero si existe. Devuelve 0 si no esta, sin lanzar.</summary>
    public int LoadIeeeCsvFile(string path)
    {
        if (!File.Exists(path))
        {
            return 0;
        }

        using StreamReader reader = new(path);
        return LoadIeeeCsv(reader);
    }

    /// <summary>
    /// Fabricante de una MAC, o null si no se conoce. Busca del prefijo mas especifico
    /// al mas general, que es como funciona la asignacion de IEEE.
    /// </summary>
    public string? Lookup(string? mac)
    {
        string? normalized = MacAddress.Normalize(mac);
        if (normalized is null)
        {
            return null;
        }

        if (MacAddress.IsLocallyAdministered(normalized))
        {
            // No hay fabricante que buscar: MAC virtual o aleatorizada.
            return null;
        }

        if (_36Bit.Count > 0 && _36Bit.TryGetValue(normalized[..9], out string? ms))
        {
            return ms;
        }

        if (_28Bit.Count > 0 && _28Bit.TryGetValue(normalized[..7], out string? mm))
        {
            return mm;
        }

        return _24Bit.TryGetValue(normalized[..6], out string? ml) ? ml : null;
    }

    private static bool IsHex(string value)
    {
        foreach (char c in value)
        {
            if (!Uri.IsHexDigit(c))
            {
                return false;
            }
        }

        return value.Length > 0;
    }

    /// <summary>
    /// Parser de CSV suficiente para el fichero de IEEE: campos entre comillas con
    /// comillas dobles escapadas. No pretende cubrir el RFC 4180 entero.
    /// </summary>
    internal static string[] ParseCsvLine(string line)
    {
        List<string> fields = new(4);
        System.Text.StringBuilder current = new();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(c);
                }
            }
            else if (c == '"')
            {
                inQuotes = true;
            }
            else if (c == ',')
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        fields.Add(current.ToString());
        return fields.ToArray();
    }

    /// <summary>Convierte "00-1D-C1" o "001DC1" en la clave interna. Para pruebas y para el CLI.</summary>
    public static string? NormalizePrefix(string prefix)
    {
        string cleaned = prefix.Replace("-", string.Empty).Replace(":", string.Empty).ToUpperInvariant();
        return cleaned.Length is 6 or 7 or 9 && IsHex(cleaned) ? cleaned : null;
    }

    /// <summary>Registra un prefijo a mano. Lo usa el usuario para etiquetar hardware propio.</summary>
    public bool Register(string prefix, string vendor)
    {
        string? key = NormalizePrefix(prefix);
        if (key is null || string.IsNullOrWhiteSpace(vendor))
        {
            return false;
        }

        Dictionary<string, string> target = key.Length switch
        {
            6 => _24Bit,
            7 => _28Bit,
            _ => _36Bit,
        };

        target[key] = vendor.Trim();
        return true;
    }
}
