using System.Runtime.Versioning;
using System.Security.Cryptography;
using Microsoft.Win32;

namespace AvDashboard.Native.Video;

/// <summary>Un EDID crudo leido del registro, con de donde ha salido.</summary>
public sealed record RawEdid
{
    public required byte[] Bytes { get; init; }

    /// <summary>Identificador de hardware: "MSI3CA5". Es el PNP ID mas el codigo de producto.</summary>
    public required string HardwareId { get; init; }

    /// <summary>Instancia concreta: distingue dos monitores idénticos en dos salidas.</summary>
    public required string InstanceId { get; init; }

    /// <summary>Huella para detectar que un EDID ha cambiado respecto a la referencia.</summary>
    public string Fingerprint => Fingerprint16(Bytes);

    /// <summary>
    /// SHA-256 truncado a 64 bits. Para detectar un cambio sobra, y cabe en una columna.
    /// </summary>
    public static string Fingerprint16(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes).AsSpan(0, 8)).ToLowerInvariant();
}

/// <summary>
/// Lee EDID crudos del registro de Windows (M18).
///
/// Windows guarda el EDID de cada monitor que ha visto bajo
/// HKLM\SYSTEM\CurrentControlSet\Enum\DISPLAY. Es la via que no necesita NVAPI ni ningun
/// SDK, funciona con cualquier GPU, y ademas da acceso a monitores que ahora mismo no
/// estan conectados, que es justo lo que hace falta para tener una biblioteca de
/// referencia contra la que comparar.
/// </summary>
[SupportedOSPlatform("windows")]
public static class EdidRegistryReader
{
    private const string DisplayEnumKey = @"SYSTEM\CurrentControlSet\Enum\DISPLAY";

    /// <summary>
    /// EDID de un monitor a partir de la ruta de dispositivo que devuelve
    /// QueryDisplayConfig, con la forma
    /// <c>\\?\DISPLAY#MSI3CA5#5&amp;1a2b3c&amp;0&amp;UID4353#{guid}</c>.
    /// </summary>
    public static RawEdid? TryReadForDevicePath(string? monitorDevicePath)
    {
        if (string.IsNullOrWhiteSpace(monitorDevicePath))
        {
            return null;
        }

        // El primer trozo lleva el prefijo \\?\ y el enumerador; el ultimo es el GUID de
        // la interfaz. Lo que interesa esta en medio.
        string[] parts = monitorDevicePath.Split('#');
        if (parts.Length < 3)
        {
            return null;
        }

        return TryRead(parts[1], parts[2]);
    }

    public static RawEdid? TryRead(string hardwareId, string instanceId)
    {
        try
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using RegistryKey? parameters = baseKey.OpenSubKey(
                $@"{DisplayEnumKey}\{hardwareId}\{instanceId}\Device Parameters");

            if (parameters?.GetValue("EDID") is not byte[] bytes || bytes.Length < 128)
            {
                return null;
            }

            return new RawEdid
            {
                Bytes = bytes,
                HardwareId = hardwareId,
                InstanceId = instanceId,
            };
        }
        catch (Exception ex) when (ex is global::System.Security.SecurityException
                                      or UnauthorizedAccessException
                                      or IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// Todos los EDID que Windows recuerda, conectados o no.
    ///
    /// Es la semilla de la biblioteca de referencia: el monitor que hoy no esta enchufado
    /// pero cuyo EDID quieres tener guardado para comparar cuando vuelva.
    /// </summary>
    public static IReadOnlyList<RawEdid> EnumerateAll()
    {
        List<RawEdid> found = new();

        try
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using RegistryKey? display = baseKey.OpenSubKey(DisplayEnumKey);

            if (display is null)
            {
                return found;
            }

            foreach (string hardwareId in display.GetSubKeyNames())
            {
                using RegistryKey? hardware = display.OpenSubKey(hardwareId);
                if (hardware is null)
                {
                    continue;
                }

                foreach (string instanceId in hardware.GetSubKeyNames())
                {
                    RawEdid? edid = TryRead(hardwareId, instanceId);
                    if (edid is not null)
                    {
                        found.Add(edid);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is global::System.Security.SecurityException
                                      or UnauthorizedAccessException
                                      or IOException)
        {
            // Sin acceso a esa rama del registro. Se devuelve lo que se haya podido leer.
        }

        return found;
    }
}
