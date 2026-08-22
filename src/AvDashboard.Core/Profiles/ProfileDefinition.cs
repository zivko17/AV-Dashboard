using System.Text.Json.Serialization;

namespace AvDashboard.Core.Profiles;

/// <summary>
/// Un perfil de escenario (M10). El resto de la aplicacion es consulta; esto es accion,
/// y es el modulo que hace que la uses de verdad.
/// </summary>
public sealed record ProfileDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }

    public IReadOnlyList<ProfileStep> Steps { get; init; } = Array.Empty<ProfileStep>();

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Id del perfil inverso generado desde el snapshot de la primera aplicacion.
    /// El "Oficina" que te devuelve la maquina despues del evento.
    /// </summary>
    public string? InverseProfileId { get; init; }
}

/// <summary>Un paso del perfil: un actuador mas sus parametros.</summary>
public sealed record ProfileStep
{
    /// <summary>Identificador del actuador: "system.powerplan", "services.start".</summary>
    public required string ActionId { get; init; }

    /// <summary>Parametros del actuador, serializados. Su forma la define cada actuador.</summary>
    public required string ParametersJson { get; init; }

    /// <summary>Etiqueta para la UI: "Plan de energía → Ultimate Performance".</summary>
    public string? Label { get; init; }

    /// <summary>
    /// Si es true, un fallo en este paso no aborta el perfil. Util para pasos que
    /// dependen de hardware que puede no estar en un nodo concreto.
    /// </summary>
    public bool ContinueOnError { get; init; }

    /// <summary>Orden de ejecucion. Los pasos se aplican de menor a mayor.</summary>
    public int Order { get; init; }
}

public enum ProfileRunStatus
{
    Pending = 0,
    Running,
    Completed,

    /// <summary>Termino, pero uno o mas pasos marcados como opcionales fallaron.</summary>
    CompletedWithWarnings,

    Failed,
    RolledBack,

    /// <summary>El rollback tambien fallo. Requiere intervencion manual y se dice claramente.</summary>
    RollbackFailed,

    Cancelled,
}

/// <summary>Registro de una ejecucion de perfil. Sin esto no hay rollback posible.</summary>
public sealed record ProfileRun
{
    public required string Id { get; init; }
    public required string ProfileId { get; init; }
    public required string NodeId { get; init; }
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? FinishedAt { get; init; }
    public ProfileRunStatus Status { get; init; } = ProfileRunStatus.Pending;

    /// <summary>Snapshot tomado antes de tocar nada. Es lo que hace el rollback posible.</summary>
    public string? SnapshotId { get; init; }

    public IReadOnlyList<ProfileStepResult> Steps { get; init; } = Array.Empty<ProfileStepResult>();

    /// <summary>true si la ejecucion se lanzo con show activo y confirmacion explicita.</summary>
    public bool ForcedDuringShow { get; init; }

    /// <summary>
    /// Que ha quedado a medias si el rollback fallo. Null cuando no hubo rollback o cuando
    /// salio bien. Es el campo que hay que leer antes de tocar nada mas en esa maquina.
    /// </summary>
    public string? RollbackMessage { get; init; }

    [JsonIgnore]
    public bool CanRollback =>
        SnapshotId is not null &&
        Status is ProfileRunStatus.Completed
            or ProfileRunStatus.CompletedWithWarnings
            or ProfileRunStatus.Failed;
}

public sealed record ProfileStepResult
{
    public required string ActionId { get; init; }
    public required int Order { get; init; }
    public required bool Success { get; init; }
    public string? Message { get; init; }
    public string? PreviousValue { get; init; }
    public string? AppliedValue { get; init; }
    public bool Skipped { get; init; }
    public TimeSpan Duration { get; init; }
}
