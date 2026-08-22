using AvDashboard.Core.Interfaces;

namespace AvDashboard.Core.Profiles;

/// <summary>
/// Resuelve un <see cref="IActuator"/> a partir de su ActionId. Lo implementa el
/// agente, que es quien conoce los actuadores compilados de este nodo.
/// </summary>
public interface IActuatorRegistry
{
    bool TryResolve(string actionId, out IProfileStepExecutor executor);
    IEnumerable<string> KnownActionIds { get; }
}

/// <summary>
/// Adaptador entre un paso de perfil (parametros como JSON) y un actuador tipado.
/// Existe para que el motor de perfiles no tenga que conocer los tipos concretos.
/// </summary>
public interface IProfileStepExecutor
{
    string ActionId { get; }
    bool IsDisruptive { get; }
    Task<ActionDiff> PreviewAsync(string parametersJson, CancellationToken cancellationToken = default);
    Task<ActuationResult> ApplyAsync(string parametersJson, CancellationToken cancellationToken = default);
}

/// <summary>Guarda y recupera snapshots. Lo implementa la capa de almacenamiento del agente.</summary>
public interface ISnapshotStore
{
    Task<string> CaptureAsync(string nodeId, string trigger, CancellationToken cancellationToken = default);
    Task<bool> RestoreAsync(string snapshotId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Ejecucion transaccional de perfiles con snapshot previo y rollback.
///
/// La logica vive en Core, sin dependencias de Windows, precisamente para poder
/// probarla: es el codigo que puede dejar una maquina inutilizable en un show y
/// no se valida a mano.
/// </summary>
public sealed class ProfileEngine
{
    private readonly IActuatorRegistry _registry;
    private readonly ISnapshotStore _snapshots;
    private readonly IShowGuard? _showGuard;
    private readonly TimeProvider _time;

    public ProfileEngine(
        IActuatorRegistry registry,
        ISnapshotStore snapshots,
        IShowGuard? showGuard = null,
        TimeProvider? timeProvider = null)
    {
        _registry = registry;
        _snapshots = snapshots;
        _showGuard = showGuard;
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Calcula el diff completo del perfil sin tocar nada. Es lo que responde
    /// `?dryRun=true` y lo que la consola ensena antes de pedir confirmacion.
    /// </summary>
    public async Task<ProfilePreview> PreviewAsync(
        ProfileDefinition profile,
        CancellationToken cancellationToken = default)
    {
        List<ActionDiff> diffs = new();
        List<string> unresolved = new();

        foreach (ProfileStep step in profile.Steps.OrderBy(s => s.Order))
        {
            if (!_registry.TryResolve(step.ActionId, out IProfileStepExecutor executor))
            {
                unresolved.Add(step.ActionId);
                continue;
            }

            try
            {
                diffs.Add(await executor.PreviewAsync(step.ParametersJson, cancellationToken)
                    .ConfigureAwait(false));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                diffs.Add(new ActionDiff
                {
                    ActionId = step.ActionId,
                    Description = $"No se pudo calcular el cambio: {ex.Message}",
                });
            }
        }

        ShowActivity activity = _showGuard is null
            ? ShowActivity.Idle
            : await _showGuard.GetActivityAsync(cancellationToken).ConfigureAwait(false);

        return new ProfilePreview
        {
            ProfileId = profile.Id,
            Diffs = diffs,
            UnresolvedActions = unresolved,
            ShowActivity = activity,
        };
    }

    /// <summary>
    /// Aplica el perfil. Toma snapshot antes de tocar nada; si un paso obligatorio
    /// falla, deshace lo aplicado y devuelve el run marcado como RolledBack.
    /// </summary>
    public async Task<ProfileRun> ApplyAsync(
        ProfileDefinition profile,
        string nodeId,
        ProfileApplyOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new ProfileApplyOptions();

        ShowActivity activity = _showGuard is null
            ? ShowActivity.Idle
            : await _showGuard.GetActivityAsync(cancellationToken).ConfigureAwait(false);

        if (activity.IsShowActive && !options.ConfirmedDuringShow)
        {
            throw new ShowInProgressException(activity);
        }

        string runId = Guid.NewGuid().ToString("N");
        DateTimeOffset started = _time.GetUtcNow();

        string? snapshotId = null;
        if (options.TakeSnapshot)
        {
            snapshotId = await _snapshots
                .CaptureAsync(nodeId, $"profile:{profile.Id}", cancellationToken)
                .ConfigureAwait(false);
        }

        List<ProfileStepResult> results = new();
        bool hardFailure = false;

        foreach (ProfileStep step in profile.Steps.OrderBy(s => s.Order))
        {
            cancellationToken.ThrowIfCancellationRequested();

            long ticksBefore = _time.GetTimestamp();

            if (!_registry.TryResolve(step.ActionId, out IProfileStepExecutor executor))
            {
                // Un actuador ausente es normal en multi-nodo: el rig portatil no tiene
                // DeckLink. Se registra como saltado, no como fallo.
                results.Add(new ProfileStepResult
                {
                    ActionId = step.ActionId,
                    Order = step.Order,
                    Success = false,
                    Skipped = true,
                    Message = "Actuador no disponible en este nodo",
                    Duration = _time.GetElapsedTime(ticksBefore),
                });
                continue;
            }

            try
            {
                ActionDiff diff = await executor.PreviewAsync(step.ParametersJson, cancellationToken)
                    .ConfigureAwait(false);

                if (diff.IsNoOp)
                {
                    results.Add(new ProfileStepResult
                    {
                        ActionId = step.ActionId,
                        Order = step.Order,
                        Success = true,
                        Skipped = true,
                        Message = "Ya estaba en el valor deseado",
                        PreviousValue = diff.CurrentValue,
                        AppliedValue = diff.DesiredValue,
                        Duration = _time.GetElapsedTime(ticksBefore),
                    });
                    continue;
                }

                ActuationResult result = await executor
                    .ApplyAsync(step.ParametersJson, cancellationToken)
                    .ConfigureAwait(false);

                results.Add(new ProfileStepResult
                {
                    ActionId = step.ActionId,
                    Order = step.Order,
                    Success = result.Success,
                    Message = result.Message,
                    PreviousValue = diff.CurrentValue,
                    AppliedValue = diff.DesiredValue,
                    Duration = _time.GetElapsedTime(ticksBefore),
                });

                if (!result.Success && !step.ContinueOnError)
                {
                    hardFailure = true;
                    break;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                results.Add(new ProfileStepResult
                {
                    ActionId = step.ActionId,
                    Order = step.Order,
                    Success = false,
                    Message = ex.Message,
                    Duration = _time.GetElapsedTime(ticksBefore),
                });

                if (!step.ContinueOnError)
                {
                    hardFailure = true;
                    break;
                }
            }
        }

        ProfileRunStatus status = DetermineStatus(results, hardFailure);

        if (hardFailure && options.RollbackOnFailure && snapshotId is not null)
        {
            bool restored = await _snapshots.RestoreAsync(snapshotId, cancellationToken)
                .ConfigureAwait(false);
            status = restored ? ProfileRunStatus.RolledBack : ProfileRunStatus.RollbackFailed;
        }

        return new ProfileRun
        {
            Id = runId,
            ProfileId = profile.Id,
            NodeId = nodeId,
            StartedAt = started,
            FinishedAt = _time.GetUtcNow(),
            Status = status,
            SnapshotId = snapshotId,
            Steps = results,
            ForcedDuringShow = activity.IsShowActive,
        };
    }

    internal static ProfileRunStatus DetermineStatus(
        IReadOnlyList<ProfileStepResult> results,
        bool hardFailure)
    {
        if (hardFailure)
        {
            return ProfileRunStatus.Failed;
        }

        bool anySoftFailure = results.Any(r => !r.Success);
        return anySoftFailure ? ProfileRunStatus.CompletedWithWarnings : ProfileRunStatus.Completed;
    }
}

public sealed record ProfileApplyOptions
{
    public bool TakeSnapshot { get; init; } = true;
    public bool RollbackOnFailure { get; init; } = true;

    /// <summary>
    /// El usuario ha confirmado explicitamente aplicar con show en marcha. Sin esto,
    /// <see cref="ProfileEngine.ApplyAsync"/> lanza <see cref="ShowInProgressException"/>.
    /// </summary>
    public bool ConfirmedDuringShow { get; init; }
}

public sealed record ProfilePreview
{
    public required string ProfileId { get; init; }
    public IReadOnlyList<ActionDiff> Diffs { get; init; } = Array.Empty<ActionDiff>();

    /// <summary>Pasos cuyo actuador no existe en este nodo. Se saltaran al aplicar.</summary>
    public IReadOnlyList<string> UnresolvedActions { get; init; } = Array.Empty<string>();

    public ShowActivity ShowActivity { get; init; } = ShowActivity.Idle;

    public int ChangeCount => Diffs.Count(d => !d.IsNoOp);
}

/// <summary>Se lanza cuando se intenta aplicar un perfil con show activo sin confirmar.</summary>
public sealed class ShowInProgressException : InvalidOperationException
{
    public ShowInProgressException(ShowActivity activity)
        : base("Hay un show en marcha: " + string.Join(", ", activity.Reasons))
    {
        Activity = activity;
    }

    public ShowActivity Activity { get; }
}
