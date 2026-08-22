using AvDashboard.Core.Models;
using AvDashboard.Core.Taxonomy;

namespace AvDashboard.Core.Diagnostics;

/// <summary>Una comprobacion concreta de la checklist pre-show (M11).</summary>
public sealed record CheckResult
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required HealthState State { get; init; }
    public required Sector Sector { get; init; }

    /// <summary>Que se ha encontrado. En verde puede ir vacio; en rojo nunca.</summary>
    public string? Detail { get; init; }

    /// <summary>Que hacer al respecto, cuando hay algo que hacer.</summary>
    public string? Remedy { get; init; }
}

/// <summary>
/// Veredicto de la checklist pre-show. No es un dashboard que haya que interpretar:
/// es un semaforo con la lista de lo que falla.
/// </summary>
public sealed record PreShowVerdict
{
    public DateTimeOffset RunAt { get; init; } = DateTimeOffset.UtcNow;
    public required string NodeId { get; init; }
    public IReadOnlyList<CheckResult> Checks { get; init; } = Array.Empty<CheckResult>();

    public HealthState Overall => Checks.Select(c => c.State).Worst();

    public IReadOnlyList<CheckResult> Failures =>
        Checks.Where(c => c.State is HealthState.Critical or HealthState.Warning)
              .OrderByDescending(c => c.State)
              .ToList();

    public bool IsGreen => Overall is HealthState.Ok or HealthState.Unknown;
}

/// <summary>
/// Construye el veredicto a partir de los datos que el agente ya tiene. Vive en Core
/// y es logica pura para poder probar las reglas sin una maquina Windows delante.
/// </summary>
public static class PreShowChecklist
{
    public static PreShowVerdict Build(
        string nodeId,
        SystemStatus? system,
        SystemSettings? settings,
        IReadOnlyList<AudioDevice> audioDevices,
        IReadOnlyList<NetworkInterfaceInfo> interfaces,
        IReadOnlyList<ServiceInfo> services,
        IReadOnlyList<PingStatistics> criticalPings,
        IReadOnlyList<StorageVolume> volumes,
        PowerStatus? power)
    {
        List<CheckResult> checks = new();

        // --- Sistema ---
        if (settings is not null)
        {
            checks.Add(new CheckResult
            {
                Id = "system.fast-startup",
                Title = "Fast Startup desactivado",
                Sector = Sector.SystemCandidate,
                State = settings.FastStartupEnabled switch
                {
                    false => HealthState.Ok,
                    true => HealthState.Warning,
                    null => HealthState.Unknown,
                },
                Detail = settings.FastStartupEnabled == true
                    ? "HiberbootEnabled está activo: tras un corte de luz la máquina no arranca limpia."
                    : null,
                Remedy = settings.FastStartupEnabled == true
                    ? "Desactivar Fast Startup antes del evento."
                    : null,
            });

            checks.Add(new CheckResult
            {
                Id = "system.network-throttling",
                Title = "Network throttling desactivado (MMCSS)",
                Sector = Sector.Network,
                State = settings.NetworkThrottlingIndex switch
                {
                    0xFFFFFFFF => HealthState.Ok,
                    null => HealthState.Unknown,
                    _ => HealthState.Warning,
                },
                Detail = settings.NetworkThrottlingIndex is uint idx && idx != 0xFFFFFFFF
                    ? $"NetworkThrottlingIndex = {idx}. NDI y DVS lo van a notar."
                    : null,
                Remedy = "Poner NetworkThrottlingIndex en 0xFFFFFFFF.",
            });

            checks.Add(new CheckResult
            {
                Id = "system.windows-update",
                Title = "Windows Update sin reinicio pendiente",
                Sector = Sector.SystemCandidate,
                State = settings.WindowsUpdate.RebootPending ? HealthState.Critical : HealthState.Ok,
                Detail = settings.WindowsUpdate.RebootPending
                    ? "Hay un reinicio pendiente. Windows lo va a intentar en el peor momento."
                    : null,
                Remedy = "Reiniciar ahora, o pausar Windows Update durante el evento.",
            });
        }

        if (system is not null)
        {
            checks.Add(new CheckResult
            {
                Id = "system.thermal",
                Title = "Sin throttling térmico registrado",
                Sector = Sector.SystemEssential,
                State = system.ThermalThrottleEvents > 0 || system.PowerLimitThrottleEvents > 0
                    ? HealthState.Warning
                    : HealthState.Ok,
                Detail = system.ThermalThrottleEvents > 0
                    ? $"{system.ThermalThrottleEvents} eventos de throttling térmico desde el arranque."
                    : null,
                Remedy = "Revisar ventilación del flight case antes de empezar.",
            });
        }

        // --- Audio ---
        checks.Add(new CheckResult
        {
            Id = "audio.default-device",
            Title = "Dispositivo de salida por defecto presente",
            Sector = Sector.Audio,
            State = AudioDiagnostics.Evaluate(audioDevices),
            Detail = DescribeAudio(audioDevices),
            Remedy = "Fijar el dispositivo por defecto y alinear los sample rates.",
        });

        // --- Red ---
        foreach (NetworkInterfaceInfo nic in interfaces.Where(i => i.IsUp))
        {
            if (nic.PowerManagementEnabled == true)
            {
                checks.Add(new CheckResult
                {
                    Id = $"network.power-mgmt.{nic.Id}",
                    Title = $"Power management desactivado en «{nic.Name}»",
                    Sector = Sector.Network,
                    State = HealthState.Warning,
                    Detail = "El adaptador puede suspenderse y tumbar Dante o NDI sin dejar rastro.",
                    Remedy = "Desmarcar «Permitir que el equipo apague este dispositivo».",
                });
            }
        }

        foreach (PingStatistics ping in criticalPings)
        {
            HealthState state = PingStatisticsCalculator.Evaluate(ping);
            checks.Add(new CheckResult
            {
                Id = $"network.ping.{ping.Target}",
                Title = $"Alcanzable: {ping.Target}",
                Sector = Sector.Network,
                State = state,
                Detail = state == HealthState.Ok
                    ? null
                    : $"RTT medio {Format(ping.AverageMs)} ms, pérdida {ping.LossPercent:0.#} %.",
                Remedy = "Comprobar cableado y conmutador antes de seguir.",
            });
        }

        // --- Servicios ---
        List<ServiceInfo> stopped = services.Where(s => s.IsUnexpectedlyStopped).ToList();
        checks.Add(new CheckResult
        {
            Id = "services.expected-running",
            Title = "Servicios AV esperados en marcha",
            Sector = Sector.Production,
            State = stopped.Count == 0 ? HealthState.Ok : HealthState.Critical,
            Detail = stopped.Count == 0
                ? null
                : "Parados: " + string.Join(", ", stopped.Select(s => s.DisplayName)),
            Remedy = "Arrancarlos desde la vista de Servicios.",
        });

        // --- Almacenamiento ---
        foreach (StorageVolume volume in volumes.Where(v => v.IsRecordingTarget))
        {
            HealthState state = volume.FreePercent switch
            {
                < 5 => HealthState.Critical,
                < 15 => HealthState.Warning,
                _ => HealthState.Ok,
            };

            checks.Add(new CheckResult
            {
                Id = $"storage.free.{volume.DriveLetter}",
                Title = $"Espacio en {volume.DriveLetter}",
                Sector = Sector.Storage,
                State = state,
                Detail = state == HealthState.Ok
                    ? null
                    : $"Queda el {volume.FreePercent:0.#} % ({volume.FreeBytes / 1_000_000_000.0:0.#} GB).",
                Remedy = "Liberar espacio o cambiar la unidad de grabación.",
            });
        }

        // --- Alimentacion ---
        if (power is not null && power.UpsPresent)
        {
            checks.Add(new CheckResult
            {
                Id = "power.ups",
                Title = "SAI en red",
                Sector = Sector.Power,
                State = power.OnBattery switch
                {
                    false => HealthState.Ok,
                    true => HealthState.Critical,
                    null => HealthState.Unknown,
                },
                Detail = power.OnBattery == true
                    ? $"Estamos en batería, {power.EstimatedRuntime?.TotalMinutes:0} min de autonomía."
                    : null,
                Remedy = "Comprobar la acometida antes de continuar.",
            });
        }

        return new PreShowVerdict { NodeId = nodeId, Checks = checks };
    }

    private static string? DescribeAudio(IReadOnlyList<AudioDevice> devices)
    {
        List<AudioDevice> mismatched = AudioDiagnostics.FindSampleRateMismatches(devices).ToList();
        if (mismatched.Count > 0)
        {
            return "Mismatch de sample rate en: " +
                   string.Join(", ", mismatched.Select(d => d.FriendlyName));
        }

        AudioDevice? def = devices.FirstOrDefault(d => d.IsDefault && d.DataFlow == AudioDataFlow.Render);
        return def is null ? "No hay dispositivo de salida por defecto." : null;
    }

    private static string Format(double? value) => value is double v ? v.ToString("0.#") : "—";
}
