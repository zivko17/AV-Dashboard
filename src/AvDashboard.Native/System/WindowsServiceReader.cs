using System.Management;
using System.Runtime.Versioning;
using System.ServiceProcess;
using AvDashboard.Core.Models;
using AvDashboard.Core.Taxonomy;
// System.ServiceProcess tiene su propio ServiceStartMode; el nuestro gana en este fichero.
using ServiceStartMode = AvDashboard.Core.Models.ServiceStartMode;

// Ver la nota sobre este namespace en PowerConfiguration.cs.
namespace AvDashboard.Native.System;

/// <summary>
/// Inventario de servicios de Windows (M6).
///
/// ServiceController da estado y dependencias barato; el tipo de arranque, la cuenta y
/// el PID solo estan en WMI. La consulta WMI es la parte cara, asi que se hace una sola
/// vez para todos los servicios y no una por servicio.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsServiceReader
{
    private readonly ServiceTaxonomy _taxonomy;
    private readonly HashSet<string> _expectedRunning;

    public WindowsServiceReader(
        ServiceTaxonomy? taxonomy = null,
        IEnumerable<string>? expectedRunning = null)
    {
        _taxonomy = taxonomy ?? new ServiceTaxonomy();
        _expectedRunning = new HashSet<string>(
            expectedRunning ?? ServiceTaxonomy.DefaultExpectedRunning,
            StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ServiceInfo> GetServices()
    {
        Platform.EnsureWindows();

        Dictionary<string, WmiServiceRow> wmi = ReadWmiRows();
        List<ServiceInfo> services = new();

        foreach (ServiceController controller in ServiceController.GetServices())
        {
            using (controller)
            {
                wmi.TryGetValue(controller.ServiceName, out WmiServiceRow row);

                services.Add(new ServiceInfo
                {
                    Name = controller.ServiceName,
                    DisplayName = controller.DisplayName,
                    State = MapState(controller.Status),
                    StartMode = MapStartMode(row.StartMode, row.DelayedAutoStart),
                    Account = row.StartName,
                    ProcessId = row.ProcessId > 0 ? row.ProcessId : null,
                    DependsOn = ReadDependencies(controller),
                    Sector = _taxonomy.Classify(controller.ServiceName, controller.DisplayName),
                    ExpectedRunning = _expectedRunning.Contains(controller.ServiceName),
                });
            }
        }

        return services;
    }

    /// <summary>
    /// Diff contra el baseline: que se ha detenido, que ha cambiado de tipo de arranque,
    /// que es nuevo. La pregunta que te haces a las 19:40 con el cliente detras.
    /// </summary>
    public static IReadOnlyList<ServiceDiffEntry> Diff(
        IReadOnlyList<ServiceInfo> baseline,
        IReadOnlyList<ServiceInfo> current)
    {
        Dictionary<string, ServiceInfo> before =
            baseline.ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);
        Dictionary<string, ServiceInfo> after =
            current.ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);

        List<ServiceDiffEntry> diff = new();

        foreach (ServiceInfo now in current)
        {
            if (!before.TryGetValue(now.Name, out ServiceInfo? then))
            {
                diff.Add(new ServiceDiffEntry
                {
                    Name = now.Name,
                    DisplayName = now.DisplayName,
                    Kind = ServiceDiffKind.Added,
                    CurrentState = now.State,
                    CurrentStartMode = now.StartMode,
                    Sector = now.Sector,
                });
                continue;
            }

            if (then.State != now.State)
            {
                diff.Add(new ServiceDiffEntry
                {
                    Name = now.Name,
                    DisplayName = now.DisplayName,
                    Kind = ServiceDiffKind.StateChanged,
                    BaselineState = then.State,
                    CurrentState = now.State,
                    Sector = now.Sector,
                });
            }

            if (then.StartMode != now.StartMode)
            {
                diff.Add(new ServiceDiffEntry
                {
                    Name = now.Name,
                    DisplayName = now.DisplayName,
                    Kind = ServiceDiffKind.StartModeChanged,
                    BaselineStartMode = then.StartMode,
                    CurrentStartMode = now.StartMode,
                    Sector = now.Sector,
                });
            }
        }

        foreach (ServiceInfo gone in baseline.Where(s => !after.ContainsKey(s.Name)))
        {
            diff.Add(new ServiceDiffEntry
            {
                Name = gone.Name,
                DisplayName = gone.DisplayName,
                Kind = ServiceDiffKind.Removed,
                BaselineState = gone.State,
                BaselineStartMode = gone.StartMode,
                Sector = gone.Sector,
            });
        }

        return diff;
    }

    private static IReadOnlyList<string> ReadDependencies(ServiceController controller)
    {
        try
        {
            ServiceController[] dependencies = controller.ServicesDependedOn;
            if (dependencies.Length == 0)
            {
                return Array.Empty<string>();
            }

            string[] names = new string[dependencies.Length];
            for (int i = 0; i < dependencies.Length; i++)
            {
                names[i] = dependencies[i].ServiceName;
                dependencies[i].Dispose();
            }

            return names;
        }
        catch (InvalidOperationException)
        {
            // El servicio ha desaparecido entre la enumeracion y esta consulta.
            return Array.Empty<string>();
        }
    }

    private static Dictionary<string, WmiServiceRow> ReadWmiRows()
    {
        Dictionary<string, WmiServiceRow> rows = new(StringComparer.OrdinalIgnoreCase);

        try
        {
            using ManagementObjectSearcher searcher = new(
                "SELECT Name, StartMode, StartName, ProcessId, DelayedAutoStart FROM Win32_Service");

            foreach (ManagementBaseObject item in searcher.Get())
            {
                using (item)
                {
                    string? name = item["Name"] as string;
                    if (name is null)
                    {
                        continue;
                    }

                    rows[name] = new WmiServiceRow
                    {
                        StartMode = item["StartMode"] as string,
                        StartName = item["StartName"] as string,
                        ProcessId = item["ProcessId"] is uint pid ? (int)pid : 0,
                        DelayedAutoStart = item["DelayedAutoStart"] is bool delayed && delayed,
                    };
                }
            }
        }
        catch (ManagementException)
        {
            // Sin WMI seguimos teniendo estado y nombre. Es menos, pero no es nada.
        }

        return rows;
    }

    private static ServiceState MapState(ServiceControllerStatus status) => status switch
    {
        ServiceControllerStatus.Stopped => ServiceState.Stopped,
        ServiceControllerStatus.StartPending => ServiceState.StartPending,
        ServiceControllerStatus.StopPending => ServiceState.StopPending,
        ServiceControllerStatus.Running => ServiceState.Running,
        ServiceControllerStatus.ContinuePending => ServiceState.ContinuePending,
        ServiceControllerStatus.PausePending => ServiceState.PausePending,
        ServiceControllerStatus.Paused => ServiceState.Paused,
        _ => ServiceState.Unknown,
    };

    private static ServiceStartMode MapStartMode(string? wmiValue, bool delayed) => wmiValue switch
    {
        "Boot" => ServiceStartMode.Boot,
        "System" => ServiceStartMode.System,
        "Auto" => delayed ? ServiceStartMode.AutomaticDelayed : ServiceStartMode.Automatic,
        "Manual" => ServiceStartMode.Manual,
        "Disabled" => ServiceStartMode.Disabled,
        _ => ServiceStartMode.Unknown,
    };

    private struct WmiServiceRow
    {
        public string? StartMode;
        public string? StartName;
        public int ProcessId;
        public bool DelayedAutoStart;
    }
}
