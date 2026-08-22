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
    public static void Print(string command, string json)
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
