using AvDashboard.Core.Models;

namespace AvDashboard.Core.Networking;

/// <summary>
/// Lo que se sabe de cuando se vio un equipo por ultima vez.
///
/// Existe como enum en Core y no como una cadena montada en la vista porque es una regla
/// del proyecto y no una decision de maquetacion: una fila no puede afirmar mas de lo que
/// se sabe. Un equipo que no vigilamos con ping no puede decir «lleva 22 minutos sin
/// verse», porque no hay barrido automatico de fondo —ni lo va a haber, un sondeo ARP
/// continuo sobre la red de un show es justo lo que no quieres corriendo sin que nadie lo
/// haya pedido— y lo unico que consta es lo que dijo el ultimo escaneo.
/// </summary>
public enum DeviceSeenState
{
    /// <summary>Vigilado, pero aun sin muestras suficientes para decir nada.</summary>
    Measuring,

    /// <summary>Vigilado y contestando.</summary>
    Responding,

    /// <summary>Vigilado, contestaba, y ha dejado de hacerlo.</summary>
    WentQuiet,

    /// <summary>Vigilado y no ha contestado ni una vez desde que se anadio.</summary>
    NeverAnswered,

    /// <summary>Sin vigilar, presente en el ultimo barrido.</summary>
    SeenInLastScan,

    /// <summary>Sin vigilar, visto en algun barrido anterior pero no en el ultimo.</summary>
    SeenEarlier,

    /// <summary>Anadido a mano y nunca confirmado por nadie: ni ping ni barrido.</summary>
    NeverSeen,
}

public static class DeviceSeenStates
{
    /// <summary>
    /// Muestras por debajo de las cuales no se emite veredicto.
    ///
    /// Es el mismo umbral que usa el colector para no registrar eventos —«con menos de
    /// diez, cualquier veredicto es ruido»— y esta aqui para que las dos reglas no puedan
    /// separarse. Cuando estaban en dos sitios, la fila pintaba «no responde» en rojo a un
    /// equipo que acababas de anadir y estaba perfectamente.
    /// </summary>
    public const int MinimumSamples = 10;

    /// <summary>
    /// El primero que aplica, gana. El orden importa: el estado de responder tiene que ir
    /// por delante de todo lo demas, porque es el que miras cuando algo se esta cayendo.
    /// </summary>
    public static DeviceSeenState For(
        DiscoveredDevice device,
        PingStatistics? ping,
        DateTimeOffset? lastScanAt)
    {
        ArgumentNullException.ThrowIfNull(device);

        bool everSeen = device.LastSeen != default;

        if (device.IsCritical)
        {
            if (ping is null || ping.SampleCount < MinimumSamples)
            {
                return DeviceSeenState.Measuring;
            }

            if (ping.LostCount < ping.SampleCount)
            {
                return DeviceSeenState.Responding;
            }

            // Perdida del 100 %. Que nunca haya respondido y que haya dejado de responder
            // son dos averias distintas: la primera casi siempre es una IP mal escrita, y
            // la segunda es un equipo que se ha caido. Decirle «sin responder desde las
            // 18:40» a algo que jamas contesto manda a mirar donde no es.
            return everSeen ? DeviceSeenState.WentQuiet : DeviceSeenState.NeverAnswered;
        }

        if (!everSeen)
        {
            return DeviceSeenState.NeverSeen;
        }

        return lastScanAt is DateTimeOffset scan && device.LastSeen >= scan
            ? DeviceSeenState.SeenInLastScan
            : DeviceSeenState.SeenEarlier;
    }

    /// <summary>
    /// Si el equipo aparecio por primera vez hoy.
    ///
    /// No es un estado sino una marca aparte, y por eso es un metodo suyo: como estado mas
    /// taparia al de responder justo cuando ese es el que hace falta leer.
    /// </summary>
    public static bool IsNewToday(DiscoveredDevice device, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(device);

        return device.FirstSeen != default
               && device.FirstSeen.ToLocalTime().Date == now.ToLocalTime().Date;
    }
}
