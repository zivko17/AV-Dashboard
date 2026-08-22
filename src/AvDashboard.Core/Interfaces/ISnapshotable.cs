namespace AvDashboard.Core.Interfaces;

/// <summary>
/// Ningun cambio sin snapshot previo persistido. Un modulo que escribe debe saber
/// capturar y restaurar su propio estado, o no se le permite escribir.
/// </summary>
public interface ISnapshotable
{
    string ModuleId { get; }

    /// <summary>Captura el estado actual en una forma que <see cref="RestoreAsync"/> entienda.</summary>
    Task<string> CaptureAsync(CancellationToken cancellationToken = default);

    /// <summary>Devuelve el modulo al estado capturado. Debe ser idempotente.</summary>
    Task<bool> RestoreAsync(string payload, CancellationToken cancellationToken = default);
}
