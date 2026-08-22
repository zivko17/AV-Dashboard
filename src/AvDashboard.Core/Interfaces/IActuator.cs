using AvDashboard.Core.Models;

namespace AvDashboard.Core.Interfaces;

/// <summary>
/// Un actuador es una accion concreta y compilada. El agente no ejecuta comandos
/// arbitrarios: no hay endpoint generico de PowerShell y no lo habra. Esa es la
/// diferencia entre una herramienta y una puerta trasera en tres maquinas de cliente.
/// </summary>
public interface IActuator
{
    string ModuleId { get; }

    /// <summary>Identificador estable de la accion: "system.powerplan", "network.profile".</summary>
    string ActionId { get; }

    ModuleDescriptor Describe();

    /// <summary>true si aplicar esta accion requiere que el agente corra elevado.</summary>
    bool RequiresElevation { get; }

    /// <summary>
    /// true si la accion es potencialmente disruptiva durante un show. El guardarrail
    /// de show activo exige confirmacion adicional para estas.
    /// </summary>
    bool IsDisruptive { get; }
}

/// <summary>Actuador tipado. Toda escritura pasa antes por <see cref="PreviewAsync"/>.</summary>
public interface IActuator<TRequest> : IActuator
{
    /// <summary>
    /// Calcula el cambio sin aplicarlo. Es lo que responde `?dryRun=true` y lo que la
    /// UI ensena antes de pedir confirmacion.
    /// </summary>
    Task<ActionDiff> PreviewAsync(TRequest request, CancellationToken cancellationToken = default);

    Task<ActuationResult> ApplyAsync(TRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Resultado de aplicar un actuador.</summary>
public sealed record ActuationResult
{
    public required bool Success { get; init; }
    public required string ActionId { get; init; }
    public string? Message { get; init; }
    public ActionDiff? Applied { get; init; }
    public string? SnapshotId { get; init; }

    public static ActuationResult Ok(string actionId, ActionDiff? applied = null, string? snapshotId = null) =>
        new() { Success = true, ActionId = actionId, Applied = applied, SnapshotId = snapshotId };

    public static ActuationResult Failed(string actionId, string message) =>
        new() { Success = false, ActionId = actionId, Message = message };
}

/// <summary>Cambio propuesto o aplicado sobre un ajuste concreto.</summary>
public sealed record ActionDiff
{
    public required string ActionId { get; init; }

    /// <summary>Descripcion legible: "Plan de energia: Equilibrado -> Ultimate Performance".</summary>
    public required string Description { get; init; }

    public string? CurrentValue { get; init; }
    public string? DesiredValue { get; init; }

    /// <summary>true cuando el valor actual ya es el deseado y no hay nada que hacer.</summary>
    public bool IsNoOp => string.Equals(CurrentValue, DesiredValue, StringComparison.Ordinal);
}
