using System.Collections.ObjectModel;
using AvDashboard.Console.Services;
using AvDashboard.Core.Contracts;
using AvDashboard.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AvDashboard.Console.ViewModels;

/// <summary>
/// Sistema (M1). Estado antes que control: primero lo que esta pasando, y los ajustes
/// debajo con su valor actual.
/// </summary>
public sealed partial class SystemViewModel : PageViewModel
{
    public SystemViewModel(NodeManager nodes) : base(nodes)
    {
    }

    public override string Title => "Sistema";
    public override string Glyph => "\uE713";

    public ObservableCollection<SettingRow> Settings { get; } = new();

    [ObservableProperty]
    private double _cpuPercent;

    [ObservableProperty]
    private double _memoryPercent;

    [ObservableProperty]
    private string _uptime = "—";

    [ObservableProperty]
    private string _powerPlan = "—";

    protected override async Task LoadAsync()
    {
        ApiResult<SystemResponse> result = await Nodes.Api().GetSystemAsync().ConfigureAwait(true);
        if (!Apply(result, out SystemResponse system))
        {
            return;
        }

        CpuPercent = system.Status.CpuTotalPercent;
        MemoryPercent = system.Status.MemoryUsedPercent;
        Uptime = FormatUptime(system.Status.Uptime);
        PowerPlan = system.Settings.ActivePowerPlanName ?? "desconocido";

        Settings.Clear();

        Add("Plan de energía", system.Settings.ActivePowerPlanName,
            "Ultimate Performance",
            system.Settings.ActivePowerPlanGuid == Guid.Parse("e9a42b02-d5df-448d-aa00-03f14749eb61"));

        Add("Ultimate Performance disponible",
            system.Settings.UltimatePerformanceAvailable ? "sí" : "no",
            "sí", system.Settings.UltimatePerformanceAvailable);

        Add("Fast Startup", Describe(system.Settings.FastStartupEnabled),
            "desactivado", system.Settings.FastStartupEnabled == false,
            "Con Fast Startup, tras un corte de luz la máquina no arranca limpia.");

        Add("MMCSS SystemResponsiveness",
            system.Settings.MmcssSystemResponsiveness?.ToString() ?? "no configurado",
            "10 o menos",
            system.Settings.MmcssSystemResponsiveness <= 10);

        Add("NetworkThrottlingIndex",
            system.Settings.NetworkThrottlingIndex is uint idx ? $"0x{idx:X}" : "no configurado",
            "0xFFFFFFFF",
            system.Settings.NetworkThrottlingIndex == 0xFFFFFFFF,
            "El ajuste con más impacto real en NDI y en Dante Virtual Soundcard.");

        Add("Efectos visuales", DescribeVisualEffects(system.Settings.VisualEffects),
            "Mejor rendimiento",
            system.Settings.VisualEffects == VisualEffectsSetting.BestPerformance);

        Add("HAGS (planificación por GPU)",
            Describe(system.Settings.HardwareAcceleratedGpuScheduling), "según el rig", null);

        Add("Reinicio de Windows Update pendiente",
            system.Settings.WindowsUpdate.RebootPending ? "sí" : "no",
            "no", !system.Settings.WindowsUpdate.RebootPending);

        Add("Defender — Tamper Protection",
            Describe(system.Settings.Defender.TamperProtectionEnabled),
            "lectura solamente", null,
            system.Settings.Defender.RequiresManualIntervention == true
                ? "Con Tamper Protection activo no se puede tocar Defender por software. Es una intervención manual, una vez."
                : null);

        Health = Settings.Any(s => s.IsCorrect == false) ? HealthState.Warning : HealthState.Ok;
    }

    private void Add(string name, string? current, string desired, bool? isCorrect, string? note = null) =>
        Settings.Add(new SettingRow
        {
            Name = name,
            Current = current ?? "no legible",
            Desired = desired,
            IsCorrect = isCorrect,
            Note = note,
        });

    private static string Describe(bool? value) => value switch
    {
        true => "activado",
        false => "desactivado",
        null => "no legible",
    };

    private static string DescribeVisualEffects(VisualEffectsSetting setting) => setting switch
    {
        VisualEffectsSetting.BestPerformance => "Mejor rendimiento",
        VisualEffectsSetting.BestAppearance => "Mejor apariencia",
        VisualEffectsSetting.LetWindowsChoose => "Windows decide",
        VisualEffectsSetting.Custom => "Personalizado",
        _ => "no legible",
    };

    private static string FormatUptime(TimeSpan uptime) =>
        $"{(int)uptime.TotalDays} d {uptime.Hours} h {uptime.Minutes} min";
}

/// <summary>Un ajuste con su valor actual y el que espera el perfil.</summary>
public sealed class SettingRow
{
    public required string Name { get; init; }
    public required string Current { get; init; }
    public required string Desired { get; init; }

    /// <summary>null cuando no hay un valor «correcto» universal y depende del rig.</summary>
    public bool? IsCorrect { get; init; }

    public string? Note { get; init; }

    public HealthState Health => IsCorrect switch
    {
        true => HealthState.Ok,
        false => HealthState.Warning,
        null => HealthState.Unknown,
    };
}
