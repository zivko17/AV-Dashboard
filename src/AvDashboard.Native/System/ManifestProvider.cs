using System.Management;
using System.Runtime.Versioning;
using AvDashboard.Core.Models;
using Microsoft.Win32;

// Ver la nota sobre este namespace en PowerConfiguration.cs.
namespace AvDashboard.Native.System;

/// <summary>
/// Manifiesto del nodo (M17): hardware, numero de serie, drivers y versiones.
/// Es lo que convierte «el rig A funciona y el B no» en un diff concreto.
/// </summary>
[SupportedOSPlatform("windows")]
public static class ManifestProvider
{
    public static NodeManifest Build(string nodeId)
    {
        Platform.EnsureWindows();

        return new NodeManifest
        {
            NodeId = nodeId,
            Hostname = Environment.MachineName,
            Hardware = ReadHardware(),
            Os = ReadOs(),
            Drivers = ReadDrivers(),
            Applications = ReadApplications(),
        };
    }

    private static HardwareManifest ReadHardware()
    {
        string? manufacturer = null, model = null, serial = null, bios = null, cpu = null;
        long memory = 0;

        Query("SELECT Manufacturer, Model FROM Win32_ComputerSystem", row =>
        {
            manufacturer = row["Manufacturer"] as string;
            model = row["Model"] as string;
        });

        Query("SELECT SerialNumber, SMBIOSBIOSVersion FROM Win32_BIOS", row =>
        {
            serial = row["SerialNumber"] as string;
            bios = row["SMBIOSBIOSVersion"] as string;
        });

        Query("SELECT Name FROM Win32_Processor", row => cpu ??= row["Name"] as string);

        Query("SELECT Capacity FROM Win32_PhysicalMemory", row =>
        {
            if (row["Capacity"] is ulong capacity)
            {
                memory += (long)capacity;
            }
        });

        List<string> gpus = new();
        Query("SELECT Name, DriverVersion FROM Win32_VideoController", row =>
        {
            string? name = row["Name"] as string;
            string? driver = row["DriverVersion"] as string;
            if (name is not null)
            {
                gpus.Add(driver is null ? name : $"{name} ({driver})");
            }
        });

        List<string> nics = new();
        Query("SELECT Name, MACAddress FROM Win32_NetworkAdapter WHERE PhysicalAdapter = TRUE", row =>
        {
            string? name = row["Name"] as string;
            string? mac = row["MACAddress"] as string;
            if (name is not null)
            {
                nics.Add(mac is null ? name : $"{name} [{mac}]");
            }
        });

        List<string> disks = new();
        Query("SELECT Model, Size FROM Win32_DiskDrive", row =>
        {
            string? diskModel = row["Model"] as string;
            if (diskModel is null)
            {
                return;
            }

            string size = row["Size"] is ulong bytes
                ? $" {bytes / 1_000_000_000UL} GB"
                : string.Empty;

            disks.Add(diskModel + size);
        });

        return new HardwareManifest
        {
            Manufacturer = manufacturer,
            Model = model,
            SerialNumber = serial,
            BiosVersion = bios,
            Cpu = cpu,
            LogicalCores = Environment.ProcessorCount,
            TotalMemoryBytes = memory,
            Gpus = gpus,
            Nics = nics,
            Disks = disks,
        };
    }

    private static OsManifest ReadOs()
    {
        string? caption = null, version = null;
        DateTimeOffset? installDate = null;

        Query("SELECT Caption, Version, InstallDate FROM Win32_OperatingSystem", row =>
        {
            caption = row["Caption"] as string;
            version = row["Version"] as string;

            if (row["InstallDate"] is string raw && raw.Length >= 14 &&
                DateTime.TryParseExact(raw[..14], "yyyyMMddHHmmss", null,
                    global::System.Globalization.DateTimeStyles.AssumeLocal, out DateTime parsed))
            {
                installDate = parsed;
            }
        });

        List<string> hotfixes = new();
        Query("SELECT HotFixID, InstalledOn FROM Win32_QuickFixEngineering", row =>
        {
            if (row["HotFixID"] is string id)
            {
                hotfixes.Add(row["InstalledOn"] is string when_ ? $"{id} ({when_})" : id);
            }
        });

        const string currentVersionKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";

        return new OsManifest
        {
            Caption = caption,
            Version = version,
            Build = RegistrySettingsReader.ReadString(RegistryHive.LocalMachine, currentVersionKey, "CurrentBuildNumber"),
            DisplayVersion = RegistrySettingsReader.ReadString(RegistryHive.LocalMachine, currentVersionKey, "DisplayVersion"),
            InstallDate = installDate,
            RecentHotfixes = hotfixes.TakeLast(15).ToList(),
        };
    }

    /// <summary>
    /// Versiones de los drivers y SDK que importan en un rig. Se leen del registro de
    /// desinstalacion porque es el unico sitio donde estan todos juntos.
    /// </summary>
    private static IReadOnlyDictionary<string, string> ReadDrivers()
    {
        string[] interesting =
        {
            "NVIDIA Graphics Driver",
            "Desktop Video",
            "Dante",
            "NDI",
            "Npcap",
            "Blackmagic",
        };

        return ReadInstalledPrograms(interesting);
    }

    private static IReadOnlyDictionary<string, string> ReadApplications()
    {
        string[] interesting =
        {
            "vMix",
            "Resolume",
            "WATCHOUT",
            "OBS Studio",
            "Companion",
            "Tailscale",
        };

        return ReadInstalledPrograms(interesting);
    }

    private static IReadOnlyDictionary<string, string> ReadInstalledPrograms(string[] nameFilters)
    {
        Dictionary<string, string> found = new(StringComparer.OrdinalIgnoreCase);

        string[] uninstallKeys =
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
        };

        foreach (string keyPath in uninstallKeys)
        {
            try
            {
                using RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                using RegistryKey? uninstall = baseKey.OpenSubKey(keyPath);

                if (uninstall is null)
                {
                    continue;
                }

                foreach (string subKeyName in uninstall.GetSubKeyNames())
                {
                    using RegistryKey? entry = uninstall.OpenSubKey(subKeyName);
                    if (entry?.GetValue("DisplayName") is not string displayName)
                    {
                        continue;
                    }

                    if (!nameFilters.Any(f => displayName.Contains(f, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    string version = entry.GetValue("DisplayVersion") as string ?? "desconocida";
                    found[displayName] = version;
                }
            }
            catch (Exception ex) when (ex is global::System.Security.SecurityException
                                          or UnauthorizedAccessException)
            {
                // Sin acceso a esa rama del registro. Se sigue con la otra.
            }
        }

        return found;
    }

    private static void Query(string wql, Action<ManagementBaseObject> handler)
    {
        try
        {
            using ManagementObjectSearcher searcher = new(wql);
            foreach (ManagementBaseObject row in searcher.Get())
            {
                using (row)
                {
                    handler(row);
                }
            }
        }
        catch (ManagementException)
        {
            // WMI no disponible o clase ausente. El manifiesto sale incompleto y lo dice.
        }
    }
}
