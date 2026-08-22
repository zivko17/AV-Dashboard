using System.Runtime.Versioning;
using AvDashboard.Core.Models;
using Microsoft.Win32;

namespace AvDashboard.Native.Audio;

/// <summary>
/// Drivers ASIO declarados en HKLM\SOFTWARE\ASIO. Es la unica forma fiable de saber
/// que interfaces hay instaladas: ASIO no pasa por Core Audio y no aparece en ningun
/// sitio del panel de sonido.
/// </summary>
[SupportedOSPlatform("windows")]
public static class AsioRegistry
{
    private const string AsioKeyPath = @"SOFTWARE\ASIO";

    public static IReadOnlyList<AsioDriver> GetDrivers()
    {
        List<AsioDriver> drivers = new();

        // Los drivers de 64 bits estan en la vista de 64; los de 32 en WOW6432Node.
        // Un rig con una interfaz antigua puede tener solo la de 32, asi que se miran las dos.
        foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using RegistryKey? asioKey = baseKey.OpenSubKey(AsioKeyPath);

            if (asioKey is null)
            {
                continue;
            }

            foreach (string name in asioKey.GetSubKeyNames())
            {
                using RegistryKey? driverKey = asioKey.OpenSubKey(name);
                if (driverKey is null)
                {
                    continue;
                }

                string? clsid = driverKey.GetValue("CLSID") as string;
                if (string.IsNullOrWhiteSpace(clsid))
                {
                    continue;
                }

                if (drivers.Any(d => string.Equals(d.ClsId, clsid, StringComparison.OrdinalIgnoreCase)))
                {
                    // Mismo driver visto en las dos vistas del registro.
                    continue;
                }

                drivers.Add(new AsioDriver
                {
                    Name = name,
                    ClsId = clsid,
                    Description = driverKey.GetValue("Description") as string,
                    DriverPath = ResolveDriverPath(view, clsid),
                });
            }
        }

        return drivers;
    }

    /// <summary>Ruta del DLL desde HKCR\CLSID\{...}\InprocServer32, si se puede leer.</summary>
    private static string? ResolveDriverPath(RegistryView view, string clsid)
    {
        try
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, view);
            using RegistryKey? inproc = baseKey.OpenSubKey($@"CLSID\{clsid}\InprocServer32");
            return inproc?.GetValue(null) as string;
        }
        // `System` a secas resuelve aqui a AvDashboard.Native.System: hay que cualificar.
        catch (Exception ex) when (ex is global::System.Security.SecurityException
                                      or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
