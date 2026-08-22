namespace AvDashboard.Core.Profiles;

/// <summary>
/// Genera el perfil inverso: el «Oficina» que devuelve la máquina a como estaba antes de
/// aplicar el «Evento».
///
/// Se construye **antes** de aplicar nada, preguntándole a cada actuador por los
/// parámetros que representan su estado actual. No se deduce del diff: un diff lleva
/// texto pensado para leerse («Equilibrado → Ultimate Performance») y con eso no se puede
/// reconstruir una llamada.
/// </summary>
public static class InverseProfileBuilder
{
    /// <summary>
    /// Construye el inverso de <paramref name="profile"/> con el estado actual del nodo.
    ///
    /// Los pasos se invierten en orden: si el perfil arranca un servicio y luego cambia la
    /// IP, el inverso restaura la IP y después para el servicio. Deshacer en el mismo
    /// orden en que se hizo es cómo se pierde la conectividad a mitad de rollback.
    /// </summary>
    public static async Task<ProfileDefinition> BuildAsync(
        ProfileDefinition profile,
        IActuatorRegistry registry,
        InverseProfileOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(registry);

        options ??= new InverseProfileOptions();

        List<ProfileStep> steps = new();
        int order = 0;

        foreach (ProfileStep step in profile.Steps.OrderByDescending(s => s.Order))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!registry.TryResolve(step.ActionId, out IProfileStepExecutor executor))
            {
                // El actuador no existe en este nodo, así que tampoco habrá cambiado nada
                // que haya que deshacer.
                continue;
            }

            string current;
            try
            {
                current = await executor.CaptureCurrentAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (!options.SkipUncapturable)
                {
                    throw new InvalidOperationException(
                        $"No se ha podido capturar el estado actual de «{step.ActionId}», " +
                        "así que el perfil inverso quedaría incompleto y no se genera.", ex);
                }

                continue;
            }

            steps.Add(new ProfileStep
            {
                ActionId = step.ActionId,
                ParametersJson = current,
                Label = step.Label is null ? null : $"Deshacer: {step.Label}",
                // Un paso del inverso que falla no debe abortar la vuelta a la normalidad:
                // recuperar nueve ajustes de diez es mejor que quedarse en el estado del
                // evento porque uno no se pudo tocar.
                ContinueOnError = true,
                Order = order++,
            });
        }

        return new ProfileDefinition
        {
            Id = options.Id ?? $"{profile.Id}--inverso",
            Name = options.Name ?? $"{profile.Name} (deshacer)",
            Description =
                $"Generado automáticamente desde el estado del nodo antes de aplicar «{profile.Name}».",
            Steps = steps,
        };
    }
}

public sealed record InverseProfileOptions
{
    public string? Id { get; init; }
    public string? Name { get; init; }

    /// <summary>
    /// Si un actuador no sabe capturar su estado: saltarlo (por defecto) o negarse a
    /// generar un inverso incompleto. Para un rollback de verdad quieres false.
    /// </summary>
    public bool SkipUncapturable { get; init; } = true;
}
