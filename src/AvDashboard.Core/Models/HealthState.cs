namespace AvDashboard.Core.Models;

/// <summary>
/// Semaforo unico de toda la aplicacion. Tres estados y ninguno mas: el matiz
/// invita a interpretar y bajo presion no se interpreta, se mira.
/// </summary>
public enum HealthState
{
    /// <summary>No hay dato todavia, o el modulo esta desactivado en este nodo.</summary>
    Unknown = 0,

    /// <summary>Funciona.</summary>
    Ok = 1,

    /// <summary>Revisalo: aun funciona pero se ha salido de lo esperado.</summary>
    Warning = 2,

    /// <summary>Esta roto ahora.</summary>
    Critical = 3,
}

public static class HealthStateExtensions
{
    /// <summary>
    /// Agrega estados quedandose con el peor. <see cref="HealthState.Unknown"/> no
    /// contamina: un modulo sin datos no pone en rojo una tarjeta que si los tiene.
    /// </summary>
    public static HealthState Worst(this IEnumerable<HealthState> states)
    {
        HealthState worst = HealthState.Unknown;
        bool anyKnown = false;

        foreach (HealthState state in states)
        {
            if (state == HealthState.Unknown)
            {
                continue;
            }

            anyKnown = true;
            if (state > worst || worst == HealthState.Unknown)
            {
                worst = state;
            }
        }

        return anyKnown ? worst : HealthState.Unknown;
    }
}
