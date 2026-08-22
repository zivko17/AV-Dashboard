using AvDashboard.Agent.Storage;
using AvDashboard.Core.Contracts;
using AvDashboard.Core.Models;

namespace AvDashboard.Agent.Video;

/// <summary>
/// Compara el EDID de cada salida activa contra la referencia guardada para ese conector
/// (M18).
///
/// Es la mitad que hace útil la biblioteca. Guardar EDID con nombre está bien; lo que
/// cambia las cosas es que el agente avise solo cuando el de una salida deja de coincidir,
/// porque ese es exactamente el síntoma de que alguien ha cambiado un splitter o un
/// procesador y la imagen sigue viéndose.
/// </summary>
public sealed class EdidDriftDetector
{
    private readonly EdidReferenceRepository _references;

    public EdidDriftDetector(EdidReferenceRepository references)
    {
        _references = references;
    }

    /// <summary>
    /// Salidas cuya huella no coincide con su referencia. Una salida sin referencia no es
    /// deriva: es que nunca se guardó nada contra lo que comparar.
    /// </summary>
    public async Task<IReadOnlyList<EdidDrift>> DetectAsync(
        DisplayTopology topology, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<EdidReference> saved =
            await _references.ListAsync(cancellationToken).ConfigureAwait(false);

        if (saved.Count == 0)
        {
            return Array.Empty<EdidDrift>();
        }

        Dictionary<string, EdidReference> byConnector =
            saved.ToDictionary(r => r.ConnectorKey, StringComparer.OrdinalIgnoreCase);

        List<EdidDrift> drift = new();

        foreach (DisplayOutput output in topology.Outputs)
        {
            if (!output.IsActive || output.EdidHash is not string actual)
            {
                continue;
            }

            string key = EdidConnectorKey.FromDevicePath(output.MonitorDevicePath);

            if (key.Length == 0 || !byConnector.TryGetValue(key, out EdidReference? reference))
            {
                continue;
            }

            if (string.Equals(reference.Fingerprint, actual, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            drift.Add(new EdidDrift
            {
                ConnectorKey = key,
                ReferenceName = reference.Name,
                ExpectedFingerprint = reference.Fingerprint,
                ActualFingerprint = actual,
                DeviceName = output.DeviceName,
                MonitorName = output.MonitorName,
            });
        }

        return drift;
    }
}
