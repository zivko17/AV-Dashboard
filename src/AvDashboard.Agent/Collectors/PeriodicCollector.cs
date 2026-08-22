using AvDashboard.Core.Interfaces;
using AvDashboard.Core.Models;

namespace AvDashboard.Agent.Collectors;

/// <summary>
/// Base de los colectores periodicos.
///
/// Dos reglas que no se negocian, porque este codigo corre en maquinas que estan
/// produciendo: una excepcion en un ciclo no puede tumbar el colector, y un ciclo que
/// tarda mas que su intervalo no se solapa consigo mismo.
/// </summary>
public abstract class PeriodicCollector : BackgroundService, ICollector
{
    private readonly TimeSpan _interval;
    private readonly ILogger _logger;
    private int _consecutiveFailures;

    protected PeriodicCollector(TimeSpan interval, ILogger logger)
    {
        _interval = interval;
        _logger = logger;
    }

    /// <summary>Nombre para los logs. Normalmente el modulo: "M1", "M5"...</summary>
    protected abstract string Name { get; }

    /// <inheritdoc />
    public string ModuleId => Name;

    /// <summary>
    /// Por defecto un colector esta disponible. Los que dependen de hardware o de un SDK
    /// lo sobrescriben y devuelven el motivo por el que no lo estan, que es lo que la
    /// consola pinta en gris en vez de un cero.
    /// </summary>
    public virtual ModuleDescriptor Describe() => new()
    {
        Id = ModuleId,
        Name = ModuleId,
        Availability = ModuleAvailability.Available,
    };

    protected abstract Task CollectAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Numero de fallos seguidos tras el cual el colector se calla. Evita llenar el log
    /// con la misma excepcion mil veces cuando falta un driver.
    /// </summary>
    private const int QuietAfterFailures = 5;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(_interval);

        try
        {
            do
            {
                try
                {
                    await CollectAsync(stoppingToken).ConfigureAwait(false);
                    _consecutiveFailures = 0;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _consecutiveFailures++;

                    if (_consecutiveFailures <= QuietAfterFailures)
                    {
                        _logger.LogWarning(ex,
                            "El colector {Collector} falló ({Count} veces seguidas).",
                            Name, _consecutiveFailures);
                    }
                    else if (_consecutiveFailures == QuietAfterFailures + 1)
                    {
                        _logger.LogError(ex,
                            "El colector {Collector} lleva {Count} fallos seguidos; se silencia hasta que vuelva a funcionar.",
                            Name, _consecutiveFailures);
                    }
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            // Parada normal del servicio.
        }
    }
}
