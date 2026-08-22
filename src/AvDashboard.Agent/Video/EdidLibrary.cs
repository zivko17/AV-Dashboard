using AvDashboard.Core.Contracts;
using AvDashboard.Core.Models;
using AvDashboard.Core.Video;
using AvDashboard.Native.Video;

namespace AvDashboard.Agent.Video;

/// <summary>
/// Biblioteca de EDID del nodo (M18).
///
/// Junta lo que Windows recuerda de todos los monitores que ha visto con lo que hay
/// enchufado ahora, decodifica cada uno y lo valida. Que aparezcan los desconectados no
/// es un descuido: el EDID del monitor que hoy no está es justo el que quieres tener
/// guardado para comparar cuando vuelva.
/// </summary>
public sealed class EdidLibrary
{
    private readonly ILogger<EdidLibrary> _logger;

    public EdidLibrary(ILogger<EdidLibrary> logger)
    {
        _logger = logger;
    }

    public EdidListResponse List()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new EdidListResponse { UnavailableReason = "Solo disponible en Windows." };
        }

        IReadOnlyList<RawEdid> all = EdidRegistryReader.EnumerateAll();

        if (all.Count == 0)
        {
            return new EdidListResponse
            {
                UnavailableReason =
                    "No se ha podido leer ningún EDID de HKLM\\SYSTEM\\CurrentControlSet\\Enum\\DISPLAY. " +
                    "Suele ser falta de permisos sobre esa rama del registro.",
            };
        }

        // Las salidas activas se resuelven aparte para poder marcar cuáles están
        // conectadas y por qué salida GDI salen.
        Dictionary<string, string> connectedByFingerprint = new(StringComparer.Ordinal);
        try
        {
            foreach ((string deviceName, RawEdid edid) in DisplayTopologyProvider.GetActiveEdids())
            {
                connectedByFingerprint[edid.Fingerprint] = deviceName;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or PlatformNotSupportedException)
        {
            _logger.LogWarning(ex, "No se ha podido resolver qué EDID corresponden a salidas activas.");
        }

        List<EdidEntry> entries = new();

        foreach (RawEdid raw in all)
        {
            EdidParser.TryParse(raw.Bytes, out Edid? parsed, out _);
            IReadOnlyList<EdidFinding> findings = EdidValidator.Validate(raw.Bytes, parsed);

            entries.Add(new EdidEntry
            {
                Id = EdidEntryId.Encode(raw.HardwareId, raw.InstanceId),
                HardwareId = raw.HardwareId,
                InstanceId = raw.InstanceId,
                Fingerprint = raw.Fingerprint,
                Connected = connectedByFingerprint.ContainsKey(raw.Fingerprint),
                DeviceName = connectedByFingerprint.GetValueOrDefault(raw.Fingerprint),
                ByteCount = raw.Bytes.Length,
                Parsed = parsed,
                Findings = findings,
                Health = EdidValidator.Evaluate(findings),
            });
        }

        // Primero los conectados: son los que se están mirando cuando algo falla.
        return new EdidListResponse
        {
            Entries = entries
                .OrderByDescending(e => e.Connected)
                .ThenBy(e => e.Label, StringComparer.CurrentCulture)
                .ToList(),
        };
    }

    public RawEdid? Get(string id) =>
        OperatingSystem.IsWindows() && EdidEntryId.TryDecode(id, out string hardware, out string instance)
            ? EdidRegistryReader.TryRead(hardware, instance)
            : null;

    /// <summary>Decodifica una entrada, o null si no existe o no es un EDID legible.</summary>
    public Edid? GetParsed(string id)
    {
        RawEdid? raw = Get(id);
        if (raw is null)
        {
            return null;
        }

        EdidParser.TryParse(raw.Bytes, out Edid? parsed, out _);
        return parsed;
    }
}
