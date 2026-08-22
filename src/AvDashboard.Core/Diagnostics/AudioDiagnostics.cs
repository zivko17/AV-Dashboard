using AvDashboard.Core.Models;

namespace AvDashboard.Core.Diagnostics;

/// <summary>Reglas de salud del subsistema de audio (M3).</summary>
public static class AudioDiagnostics
{
    /// <summary>
    /// Evalua el conjunto de endpoints activos. Un mismatch de sample rate es ambar,
    /// no rojo: la maquina suena, pero va a crujir y quieres saberlo antes del show.
    /// </summary>
    public static HealthState Evaluate(IReadOnlyList<AudioDevice> devices)
    {
        ArgumentNullException.ThrowIfNull(devices);

        List<AudioDevice> active = devices
            .Where(d => d.State == AudioDeviceState.Active)
            .ToList();

        if (active.Count == 0)
        {
            return HealthState.Unknown;
        }

        AudioDevice? defaultRender = active
            .FirstOrDefault(d => d.IsDefault && d.DataFlow == AudioDataFlow.Render);

        if (defaultRender is null)
        {
            // Sin dispositivo de salida por defecto no sale audio de ningun sitio.
            return HealthState.Critical;
        }

        if (active.Any(d => d.HasSampleRateMismatch))
        {
            return HealthState.Warning;
        }

        return HealthState.Ok;
    }

    /// <summary>
    /// Endpoints cuyo sample rate declarado no coincide con el de mezcla en uso.
    /// Causa numero uno de crujidos y no se ve en ninguna pantalla de Windows.
    /// </summary>
    public static IEnumerable<AudioDevice> FindSampleRateMismatches(IEnumerable<AudioDevice> devices) =>
        devices.Where(d => d.State == AudioDeviceState.Active && d.HasSampleRateMismatch);

    /// <summary>
    /// Sample rates distintos conviviendo entre los endpoints activos. Con Dante en el
    /// medio, mezclar 44.1 y 48 es garantia de problema.
    /// </summary>
    public static IReadOnlyList<int> DistinctActiveSampleRates(IEnumerable<AudioDevice> devices) =>
        devices
            .Where(d => d.State == AudioDeviceState.Active && d.MixSampleRate > 0)
            .Select(d => d.MixSampleRate)
            .Distinct()
            .OrderBy(r => r)
            .ToList();
}
