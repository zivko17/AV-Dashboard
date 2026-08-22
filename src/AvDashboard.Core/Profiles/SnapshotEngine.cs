using AvDashboard.Core.Interfaces;
using AvDashboard.Core.Models;

namespace AvDashboard.Core.Profiles;

/// <summary>Persistencia de snapshots. La implementa la capa de almacenamiento del agente.</summary>
public interface ISnapshotRepository
{
    Task SaveAsync(Snapshot snapshot, CancellationToken cancellationToken = default);

    Task<Snapshot?> LoadAsync(string snapshotId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Snapshot>> ListAsync(string nodeId, int limit = 50, CancellationToken cancellationToken = default);
}

/// <summary>
/// Captura y restaura el estado de todos los módulos que saben hacerlo.
///
/// «Ningún cambio sin snapshot previo persistido» es un requisito no funcional, y este es
/// el sitio donde se cumple o no se cumple.
/// </summary>
public sealed class SnapshotEngine : ISnapshotStore
{
    private readonly IReadOnlyList<ISnapshotable> _modules;
    private readonly ISnapshotRepository _repository;
    private readonly TimeProvider _time;

    public SnapshotEngine(
        IEnumerable<ISnapshotable> modules,
        ISnapshotRepository repository,
        TimeProvider? timeProvider = null)
    {
        _modules = modules.ToList();
        _repository = repository;
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Captura el estado de cada módulo y lo persiste antes de devolver el id.
    ///
    /// Si un módulo falla al capturar, se registra su error como contenido del snapshot en
    /// lugar de abortar: un snapshot parcial y honesto sobre lo que le falta es mucho más
    /// útil que ningún snapshot, y aborta el snapshot entero es justo lo que dejaría
    /// aplicar un perfil sin red debajo.
    /// </summary>
    public async Task<string> CaptureAsync(
        string nodeId,
        string trigger,
        CancellationToken cancellationToken = default)
    {
        Dictionary<string, string> payload = new(StringComparer.Ordinal);
        List<string> failures = new();

        foreach (ISnapshotable module in _modules)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                payload[module.ModuleId] = await module.CaptureAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failures.Add($"{module.ModuleId}: {ex.Message}");
            }
        }

        Snapshot snapshot = new()
        {
            Id = Guid.NewGuid().ToString("N"),
            NodeId = nodeId,
            TakenAt = _time.GetUtcNow(),
            Trigger = trigger,
            Payload = payload,
            Notes = failures.Count == 0
                ? null
                : "No se pudo capturar: " + string.Join("; ", failures),
        };

        await _repository.SaveAsync(snapshot, cancellationToken).ConfigureAwait(false);
        return snapshot.Id;
    }

    /// <summary>
    /// Restaura todos los módulos del snapshot.
    ///
    /// Sigue adelante aunque uno falle. Un rollback que se para a la mitad deja la máquina
    /// en un estado que no es ni el de antes ni el de después, que es el peor de los tres.
    /// </summary>
    public async Task<RestoreResult> RestoreAsync(
        string snapshotId,
        CancellationToken cancellationToken = default)
    {
        Snapshot? snapshot = await _repository.LoadAsync(snapshotId, cancellationToken)
            .ConfigureAwait(false);

        if (snapshot is null)
        {
            return new RestoreResult
            {
                Success = false,
                Message = $"No existe el snapshot {snapshotId}.",
            };
        }

        List<ModuleRestoreOutcome> outcomes = new();

        foreach (ISnapshotable module in _modules)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!snapshot.Payload.TryGetValue(module.ModuleId, out string? state))
            {
                // El módulo no estaba capturado: puede ser un snapshot antiguo o una
                // captura que falló. No es un error de restauración.
                continue;
            }

            try
            {
                bool restored = await module.RestoreAsync(state, cancellationToken).ConfigureAwait(false);
                outcomes.Add(new ModuleRestoreOutcome
                {
                    ModuleId = module.ModuleId,
                    Success = restored,
                    Message = restored ? null : "El módulo informa de que no ha podido restaurar.",
                });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                outcomes.Add(new ModuleRestoreOutcome
                {
                    ModuleId = module.ModuleId,
                    Success = false,
                    Message = ex.Message,
                });
            }
        }

        List<ModuleRestoreOutcome> failed = outcomes.Where(o => !o.Success).ToList();

        return new RestoreResult
        {
            Success = failed.Count == 0,
            Modules = outcomes,
            Message = failed.Count == 0
                ? null
                : "No se ha podido restaurar: " +
                  string.Join("; ", failed.Select(f => $"{f.ModuleId} ({f.Message})")),
        };
    }
}

public sealed record RestoreResult
{
    public required bool Success { get; init; }

    public IReadOnlyList<ModuleRestoreOutcome> Modules { get; init; } =
        Array.Empty<ModuleRestoreOutcome>();

    /// <summary>Qué ha fallado, en texto listo para enseñar. Null si ha ido todo bien.</summary>
    public string? Message { get; init; }

    public static RestoreResult Ok { get; } = new() { Success = true };
}

public sealed record ModuleRestoreOutcome
{
    public required string ModuleId { get; init; }
    public required bool Success { get; init; }
    public string? Message { get; init; }
}
