using AvDashboard.Core.Models;

namespace AvDashboard.Core.Interfaces;

/// <summary>
/// Un colector lee estado y no lo modifica nunca. Toda la Fase 1 es colectores:
/// es la parte que no puede tumbar un evento.
/// </summary>
public interface ICollector
{
    /// <summary>Modulo al que pertenece: "M1", "M3"...</summary>
    string ModuleId { get; }

    /// <summary>
    /// Por que este colector esta o no disponible en este nodo. Se evalua al arrancar
    /// y permite que el mismo binario corra en todas las maquinas sin variantes.
    /// </summary>
    ModuleDescriptor Describe();
}

/// <summary>Colector que devuelve una lectura completa bajo demanda.</summary>
public interface ICollector<TResult> : ICollector
{
    Task<TResult> CollectAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Colector con cadencia propia. La telemetria ligera va a 1 s y los medidores de
/// audio a 20 fps; los escaneos pesados no implementan esto y se piden a mano.
/// </summary>
public interface IPeriodicCollector<TResult> : ICollector<TResult>
{
    TimeSpan Interval { get; }
}
