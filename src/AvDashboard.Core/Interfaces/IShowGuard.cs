namespace AvDashboard.Core.Interfaces;

/// <summary>
/// Guardarrail de show activo: si vMix esta grabando o emitiendo, o Watchout esta en
/// play, aplicar un perfil exige confirmacion explicita adicional.
/// </summary>
public interface IShowGuard
{
    Task<ShowActivity> GetActivityAsync(CancellationToken cancellationToken = default);
}

public sealed record ShowActivity
{
    public bool IsShowActive => Reasons.Count > 0;

    /// <summary>Por que se considera que hay show en marcha. Vacio = no lo hay.</summary>
    public IReadOnlyList<string> Reasons { get; init; } = Array.Empty<string>();

    public static ShowActivity Idle { get; } = new();
}
