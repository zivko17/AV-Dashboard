using System.Text.Json;

namespace AvDashboard.Cli;

/// <summary>
/// Salida legible para un terminal.
///
/// No intenta ser bonita: intenta que puedas leerla de un vistazo con el cliente
/// detrás. Para consumo por script está `--json`, que devuelve la respuesta del agente
/// tal cual.
/// </summary>
public static class Formatter
{
    public static void Print(string command, string json, string[]? arguments = null)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);

            switch (command)
            {
                case "netinfo":
                    PrintInterfaces(document.RootElement);
                    return;
                case "audio":
                    PrintAudio(document.RootElement);
                    return;
                case "services":
                    PrintServices(document.RootElement);
                    return;
                case "latency":
                    PrintPing(document.RootElement);
                    return;
                case "timeline":
                    PrintTimeline(document.RootElement);
                    return;
                case "preshow":
                    PrintPreShow(document.RootElement);
                    return;
                case "profiles":
                    PrintProfiles(document.RootElement);
                    return;
                case "profile-import":
                    PrintProfileImport(document.RootElement);
                    return;
                case "audit":
                    PrintAudit(document.RootElement, arguments);
                    return;
                case "edid-list":
                    PrintEdidList(document.RootElement);
                    return;
                case "edid-check":
                    PrintEdidCheck(document.RootElement);
                    return;
                case "edid-info":
                    PrintEdidInfo(document.RootElement, arguments);
                    return;
                case "edid-diff":
                    PrintEdidDiff(document.RootElement);
                    return;
                case "edid-ref":
                    PrintEdidReferences(document.RootElement);
                    return;
                default:
                    Console.WriteLine(Pretty(document));
                    return;
            }
        }
        catch (JsonException)
        {
            Console.WriteLine(json);
        }
    }

    private static void PrintInterfaces(JsonElement root)
    {
        if (!root.TryGetProperty("interfaces", out JsonElement interfaces))
        {
            Console.WriteLine(root);
            return;
        }

        foreach (JsonElement nic in interfaces.EnumerateArray())
        {
            string name = Text(nic, "name");
            string state = Bool(nic, "isUp") ? "UP  " : "DOWN";
            string ip = FirstOfArray(nic, "ipv4Addresses") ?? "—";
            string mask = Text(nic, "subnetMask");
            long speed = Number(nic, "linkSpeedBps");

            Console.WriteLine($"{state}  {name,-34}  {ip,-16} {mask,-16} {FormatSpeed(speed)}");

            if (nic.TryGetProperty("powerManagementEnabled", out JsonElement pm) &&
                pm.ValueKind == JsonValueKind.True)
            {
                Console.WriteLine("      ⚠ power management activo: puede tumbar Dante y NDI en silencio");
            }
        }
    }

    private static void PrintAudio(JsonElement root)
    {
        foreach (JsonElement device in Array(root, "devices"))
        {
            bool isDefault = Bool(device, "isDefault");
            string marker = isDefault ? "*" : " ";
            long mix = Number(device, "mixSampleRate");
            string flow = Text(device, "dataFlow");

            Console.WriteLine($"{marker} {Text(device, "friendlyName"),-44} {flow,-8} " +
                              $"{mix / 1000.0:0.#} kHz  {Text(device, "state")}");
        }

        List<string> mismatches = Array(root, "sampleRateMismatches")
            .Select(e => e.GetString() ?? string.Empty)
            .Where(s => s.Length > 0)
            .ToList();

        if (mismatches.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("⚠ Mismatch de sample rate en: " + string.Join(", ", mismatches));
        }
    }

    private static void PrintServices(JsonElement root)
    {
        foreach (JsonElement sector in Array(root, "sectors"))
        {
            Console.WriteLine();
            Console.WriteLine($"── {Text(sector, "displayName")}");

            foreach (JsonElement service in Array(sector, "services"))
            {
                string state = Text(service, "state");
                string marker = state == "Running" ? " " : "!";
                Console.WriteLine($" {marker} {Text(service, "displayName"),-46} {state,-10} " +
                                  $"{Text(service, "startMode")}");
            }
        }
    }

    private static void PrintPing(JsonElement root)
    {
        foreach (JsonElement stats in root.EnumerateArray())
        {
            double? average = OptionalDouble(stats, "averageMs");
            double? jitter = OptionalDouble(stats, "jitterMs");
            double loss = OptionalDouble(stats, "lossPercent") ?? 0;

            string line = average is double avg
                ? $"{avg,7:0.0} ms   jitter {jitter,6:0.0} ms   pérdida {loss,5:0.0} %"
                : $"     sin respuesta        pérdida {loss,5:0.0} %";

            Console.WriteLine($"{Text(stats, "target"),-30} {line}");
        }
    }

    private static void PrintTimeline(JsonElement root)
    {
        foreach (JsonElement item in Array(root, "events"))
        {
            string timestamp = DateTimeOffset.TryParse(Text(item, "timestamp"), out DateTimeOffset ts)
                ? ts.LocalDateTime.ToString("dd/MM HH:mm:ss")
                : Text(item, "timestamp");

            Console.WriteLine($"{timestamp}  {Text(item, "module"),-6} {Text(item, "kind"),-26} " +
                              Text(item, "message"));
        }
    }

    private static void PrintPreShow(JsonElement root)
    {
        int failures = 0;

        foreach (JsonElement check in Array(root, "checks"))
        {
            string state = Text(check, "state");
            string marker = state switch
            {
                "Ok" => "OK  ",
                "Warning" => "AVISO",
                "Critical" => "FALLO",
                _ => "—   ",
            };

            if (state is "Warning" or "Critical")
            {
                failures++;
            }

            Console.WriteLine($"[{marker}] {Text(check, "title")}");

            string detail = Text(check, "detail");
            if (detail.Length > 0)
            {
                Console.WriteLine($"         {detail}");
            }
        }

        Console.WriteLine();
        Console.WriteLine(failures == 0
            ? "VERDE: no hay nada que revisar."
            : $"ROJO: {failures} comprobaciones requieren atención.");
    }

    private static void PrintProfiles(JsonElement root)
    {
        PrintProfileGroup("DE SERIE", Array(root, "builtIn"));
        PrintProfileGroup("GUARDADOS EN ESTE NODO", Array(root, "stored"));

        Console.WriteLine();
        Console.WriteLine("`profile-export <id> fichero.json` se lo lleva a otra máquina.");
    }

    private static void PrintProfileGroup(string title, IEnumerable<JsonElement> profiles)
    {
        List<JsonElement> rows = profiles.ToList();

        Console.WriteLine();
        Console.WriteLine($"  {title}");

        if (rows.Count == 0)
        {
            Console.WriteLine("    (ninguno)");
            return;
        }

        foreach (JsonElement profile in rows)
        {
            int steps = Array(profile, "steps").Count();

            Console.WriteLine($"    {Text(profile, "id"),-24} {Text(profile, "name"),-30} {steps} pasos");

            if (Text(profile, "description") is { Length: > 0 } description)
            {
                Console.WriteLine($"      {description}");
            }
        }
    }

    private static void PrintProfileImport(JsonElement root)
    {
        if (!Bool(root, "saved"))
        {
            Console.Error.WriteLine(Text(root, "error"));
            return;
        }

        Console.WriteLine($"Importado «{Text(root, "profileName")}» como {Text(root, "profileId")}.");

        List<JsonElement> unknown = Array(root, "unknownActions").ToList();

        if (unknown.Count == 0)
        {
            return;
        }

        // No es un error: un perfil de un nodo con otro hardware trae pasos que aqui no
        // aplican. Pero se dice ahora y no a mitad de la ejecucion.
        Console.WriteLine();
        Console.WriteLine("Este nodo no conoce estos actuadores, así que esos pasos se saltarán:");

        foreach (JsonElement action in unknown)
        {
            Console.WriteLine($"  {action.GetString()}");
        }
    }

    /// <summary>
    /// `audit`. Por defecto solo lo que difiere del objetivo, que es lo que quieres ver
    /// quince minutos antes de que empiece: cinco filas y no cuarenta y cinco. Con --todo
    /// sale el catálogo entero.
    /// </summary>
    private static void PrintAudit(JsonElement root, string[]? arguments)
    {
        bool all = arguments?.Any(a =>
            a.Equals("--todo", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("--all", StringComparison.OrdinalIgnoreCase)) == true;

        int shown = 0;
        int hidden = 0;

        foreach (JsonElement group in Array(root, "groups"))
        {
            List<JsonElement> rows = Array(group, "settings")
                .Where(s => all || Text(s, "verdict").StartsWith("Differs", StringComparison.Ordinal))
                .ToList();

            hidden += Array(group, "settings").Count() - rows.Count;

            if (rows.Count == 0)
            {
                continue;
            }

            Console.WriteLine();
            Console.WriteLine($"  {GroupName(group).ToUpperInvariant()}");

            foreach (JsonElement row in rows)
            {
                shown++;

                JsonElement definition = row.GetProperty("definition");
                string marker = Text(row, "verdict") switch
                {
                    "DiffersCritical" => "[!]",
                    "Differs" => "[~]",
                    "Match" => "[ok]",
                    _ => "[ ]",
                };

                Console.WriteLine($"  {marker,-5} {Text(definition, "name"),-42} " +
                                  $"{Text(row, "currentText"),-24} → {Text(definition, "targetDescription")}");

                // El motivo solo en lo que esta mal: en el listado completo seria ruido.
                if (Text(row, "verdict").StartsWith("Differs", StringComparison.Ordinal))
                {
                    Console.WriteLine($"        {Text(definition, "rationale")}");
                }
            }
        }

        Console.WriteLine();

        long critical = Number(root, "criticalCount");
        long warning = Number(root, "warningCount");

        Console.WriteLine(critical == 0 && warning == 0
            ? "Ningún ajuste difiere del objetivo."
            : $"{critical} afectan a producción, {warning} conviene revisarlos.");

        if (!all && hidden > 0)
        {
            Console.WriteLine($"{hidden} filas coinciden con el objetivo o no se han podido leer. `audit --todo` las muestra.");
        }
    }

    private static string GroupName(JsonElement group) => Text(group, "group") switch
    {
        "Power" => "Energía",
        "Kernel" => "Kernel y rendimiento",
        "Updates" => "Interrupciones y actualizaciones",
        "Security" => "Seguridad",
        "Startup" => "Arranque y sesión",
        "Diagnostics" => "Diagnóstico",
        string other => other,
    };

    // --- M18 ---

    private static void PrintEdidList(JsonElement root)
    {
        string reason = Text(root, "unavailableReason");
        if (reason.Length > 0)
        {
            Console.WriteLine(reason);
            return;
        }

        foreach (JsonElement entry in Array(root, "entries"))
        {
            string marker = Bool(entry, "connected") ? "*" : " ";
            string device = Text(entry, "deviceName");

            Console.WriteLine($"{marker} {Label(entry),-42} {device,-16} " +
                              $"{Text(entry, "fingerprint")}  {Text(entry, "health")}");
            Console.WriteLine($"    {Text(entry, "id")}");
        }

        Console.WriteLine();
        Console.WriteLine("  * conectado ahora mismo. El identificador de la segunda línea es el que");
        Console.WriteLine("    piden edid-info, edid-diff y edid-dump.");
    }

    private static void PrintEdidCheck(JsonElement root)
    {
        int problemas = 0;

        foreach (JsonElement entry in Array(root, "entries"))
        {
            List<JsonElement> findings = Array(entry, "findings").ToList();

            Console.WriteLine($"{(findings.Count == 0 ? "[OK ]" : "[!  ]")} {Label(entry)}");

            foreach (JsonElement finding in findings)
            {
                problemas++;
                Console.WriteLine($"        {Text(finding, "severity"),-8} {Text(finding, "message")}");
            }
        }

        Console.WriteLine();
        Console.WriteLine(problemas == 0
            ? "Ningún EDID presenta problemas."
            : $"{problemas} problemas encontrados.");
    }

    private static void PrintEdidInfo(JsonElement root, string[]? arguments)
    {
        string? id = arguments?.FirstOrDefault(a => !a.StartsWith('-'));

        JsonElement? match = null;
        foreach (JsonElement entry in Array(root, "entries"))
        {
            if (id is null || Text(entry, "id") == id)
            {
                match = entry;
                break;
            }
        }

        if (match is not JsonElement entryFound)
        {
            Console.Error.WriteLine("No hay ningún EDID con ese identificador.");
            return;
        }

        if (!entryFound.TryGetProperty("parsed", out JsonElement edid) ||
            edid.ValueKind != JsonValueKind.Object)
        {
            Console.WriteLine("La entrada existe pero no se ha podido decodificar.");
            return;
        }

        Console.WriteLine($"  {Label(entryFound)}");
        Console.WriteLine($"  {Text(entryFound, "hardwareId")}  ·  huella {Text(entryFound, "fingerprint")}" +
                          $"  ·  {Text(entryFound, "byteCount")} bytes");
        Console.WriteLine();

        Console.WriteLine($"  Fabricante        {Text(edid, "manufacturerId")}");
        Console.WriteLine($"  Producto          {Text(edid, "productCode")}");
        Console.WriteLine($"  Versión           {Text(edid, "version")}.{Text(edid, "revision")}");
        Console.WriteLine($"  Entrada           {(Bool(edid, "isDigitalInput") ? "digital" : "analógica")}" +
                          $" {Text(edid, "digitalInterface")} {Text(edid, "bitsPerColor")} bit");

        if (edid.TryGetProperty("rangeLimits", out JsonElement limits) &&
            limits.ValueKind == JsonValueKind.Object)
        {
            Console.WriteLine($"  Límites           {Text(limits, "minVerticalHz")}-{Text(limits, "maxVerticalHz")} Hz, " +
                              $"{Text(limits, "minHorizontalKhz")}-{Text(limits, "maxHorizontalKhz")} kHz, " +
                              $"≤{Text(limits, "maxPixelClockMhz")} MHz");
        }

        Console.WriteLine();
        Console.WriteLine("  TIMINGS DETALLADOS (el primero es el preferido)");
        foreach (JsonElement timing in Array(edid, "detailedTimings"))
        {
            double? refresh = OptionalDouble(timing, "refreshHz");
            Console.WriteLine($"    {Text(timing, "horizontalActive")}×{Text(timing, "verticalActive")}" +
                              $"{(Bool(timing, "interlaced") ? "i" : "p")}" +
                              $" @ {refresh:0.###} Hz   reloj {Number(timing, "pixelClockKhz") / 1000.0:0.###} MHz" +
                              $"   total {Text(timing, "horizontalTotal")}×{Text(timing, "verticalTotal")}");
        }

        List<string> established = Array(edid, "establishedTimings")
            .Select(e => e.GetString() ?? string.Empty).ToList();
        if (established.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("  ESTABLECIDOS      " + string.Join(", ", established));
        }

        if (edid.TryGetProperty("cta", out JsonElement cta) && cta.ValueKind == JsonValueKind.Object)
        {
            Console.WriteLine();
            Console.WriteLine($"  CTA-861 rev {Text(cta, "revision")}   " +
                              $"audio básico {(Bool(cta, "basicAudioSupported") ? "sí" : "no")}   " +
                              $"YCbCr 4:4:4 {(Bool(cta, "yCbCr444Supported") ? "sí" : "no")}");

            foreach (JsonElement audio in Array(cta, "audioDescriptors"))
            {
                string rates = string.Join("/", Array(audio, "sampleRates")
                    .Select(r => (r.GetInt32() / 1000.0).ToString("0.#")));

                Console.WriteLine($"    audio           {Text(audio, "formatName")} " +
                                  $"{Text(audio, "maxChannels")}ch @ {rates} kHz");
            }

            if (cta.TryGetProperty("hdr", out JsonElement hdr) && hdr.ValueKind == JsonValueKind.Object)
            {
                string eotf = string.Join(", ", Array(hdr, "supportedEotf")
                    .Select(e => e.GetString() ?? string.Empty));

                Console.WriteLine($"    HDR             {eotf}");
            }
        }
    }

    private static void PrintEdidDiff(JsonElement root)
    {
        Console.WriteLine($"  izquierda: {Text(root, "leftLabel")}");
        Console.WriteLine($"  derecha:   {Text(root, "rightLabel")}");
        Console.WriteLine();

        List<JsonElement> differences = Array(root, "differences").ToList();

        if (differences.Count == 0)
        {
            Console.WriteLine("  Son idénticos.");
            return;
        }

        foreach (JsonElement difference in differences)
        {
            string marker = Bool(difference, "significant") ? "!" : " ";
            Console.WriteLine($"{marker} {Text(difference, "field"),-32} {Text(difference, "left"),-28} → " +
                              Text(difference, "right"));
        }

        Console.WriteLine();
        Console.WriteLine("  ! marca lo que cambia lo que se negocia en el cable.");
    }

    private static void PrintEdidReferences(JsonElement root)
    {
        List<JsonElement> references = Array(root, "references").ToList();

        if (references.Count == 0)
        {
            Console.WriteLine("No hay ninguna referencia guardada.");
            Console.WriteLine("Guarda una desde la pestaña Vídeo de la consola para que el agente");
            Console.WriteLine("avise cuando el EDID de esa salida deje de coincidir.");
            return;
        }

        Console.WriteLine("  REFERENCIAS GUARDADAS");
        foreach (JsonElement reference in references)
        {
            Console.WriteLine($"    {Text(reference, "name"),-34} {Text(reference, "connectorKey"),-12} " +
                              $"{Text(reference, "fingerprint"),-18} {Text(reference, "deviceName")}");
        }

        List<JsonElement> drift = Array(root, "drift").ToList();

        Console.WriteLine();

        if (drift.Count == 0)
        {
            Console.WriteLine("Todas las salidas con referencia coinciden con ella.");
            return;
        }

        Console.WriteLine("  HAN CAMBIADO");
        foreach (JsonElement changed in drift)
        {
            Console.WriteLine($"  !  {Text(changed, "referenceName")} ({Text(changed, "deviceName")})");
            Console.WriteLine($"        se esperaba {Text(changed, "expectedFingerprint")}, " +
                              $"se lee {Text(changed, "actualFingerprint")}");
            Console.WriteLine($"        ahora presenta: {Text(changed, "monitorName")}");
        }

        Console.WriteLine();
        Console.WriteLine("Un EDID que cambia en una salida suele significar que alguien ha cambiado");
        Console.WriteLine("un splitter o un procesador. La imagen se sigue viendo; el modo negociado no.");
    }

    private static string Label(JsonElement entry)
    {
        if (entry.TryGetProperty("parsed", out JsonElement parsed) &&
            parsed.ValueKind == JsonValueKind.Object)
        {
            string name = Text(parsed, "monitorName");
            string manufacturer = Text(parsed, "manufacturerId");

            if (name.Length > 0)
            {
                return $"{manufacturer} · {name}";
            }
        }

        return Text(entry, "hardwareId");
    }

    private static string Pretty(JsonDocument document) =>
        JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true });

    private static IEnumerable<JsonElement> Array(JsonElement root, string property) =>
        root.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray()
            : Enumerable.Empty<JsonElement>();

    private static string Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.ValueKind is JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : element.TryGetProperty(property, out JsonElement other) && other.ValueKind is JsonValueKind.Number
                ? other.ToString()
                : string.Empty;

    private static bool Bool(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.True;

    private static long Number(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt64(out long number)
            ? number
            : 0;

    private static double? OptionalDouble(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetDouble(out double number)
            ? number
            : null;

    private static string? FirstOfArray(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) &&
        value.ValueKind == JsonValueKind.Array &&
        value.GetArrayLength() > 0
            ? value[0].GetString()
            : null;

    private static string FormatSpeed(long bitsPerSecond) => bitsPerSecond switch
    {
        >= 1_000_000_000 => $"{bitsPerSecond / 1_000_000_000.0:0.#} Gb/s",
        >= 1_000_000 => $"{bitsPerSecond / 1_000_000.0:0} Mb/s",
        > 0 => $"{bitsPerSecond} b/s",
        _ => "—",
    };
}
