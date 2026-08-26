using AvDashboard.Core.Models;

namespace AvDashboard.Core.Diagnostics;

/// <summary>Qué clase de trabajo hace una aplicación de producción. Decide qué le duele.</summary>
public enum ProductionWorkload
{
    /// <summary>Mezcla y codifica en directo: CPU y red. vMix, OBS.</summary>
    LiveSwitcher,

    /// <summary>Reproduce y compone a cadencia fija: GPU y disco. Resolume, Watchout.</summary>
    MediaServer,

    /// <summary>Dispara audio con latencia baja. QLab, Ableton.</summary>
    AudioPlayback,
}

/// <summary>Una aplicación de producción conocida.</summary>
public sealed record ProductionApp(
    string ImageName,
    string DisplayName,
    ProductionWorkload Workload,
    string Rationale);

/// <summary>
/// Las aplicaciones que un rig de AV está ahí para ejecutar, y cómo deberían correr.
///
/// Es la lista que convierte «optimiza Windows» en «optimiza Windows **para esto**», que es
/// la diferencia entre esta aplicación y un debloater. Sin ella, la prioridad de proceso es
/// un ajuste genérico que no se puede auditar: no hay forma de decir si la máquina está
/// bien si no se sabe qué se espera que corra en ella.
///
/// Vive en Core y no en la capa nativa por lo de siempre: es una decisión con criterio, se
/// prueba, y la usan tanto la sonda —para decir «vMix está en prioridad normal»— como los
/// perfiles.
/// </summary>
public static class ProductionApps
{
    /// <summary>
    /// Prioridad objetivo, y por qué no es «Realtime».
    ///
    /// En Realtime el proceso puede dejar sin CPU al propio kernel: los drivers de audio y
    /// red dejan de atenderse y el resultado es peor que no tocar nada. <c>High</c> es el
    /// escalón que usa el propio Windows para lo que no puede esperar, y es donde MMCSS ya
    /// coloca los hilos multimedia.
    /// </summary>
    public const ProcessPriority TargetPriority = ProcessPriority.High;

    public static IReadOnlyList<ProductionApp> Known { get; } = new ProductionApp[]
    {
        new("vMix64.exe", "vMix", ProductionWorkload.LiveSwitcher,
            "Mezcla, codifica NDI y SRT y mete audio en el mismo hilo de tiempo. Lo que le " +
            "duele son los núcleos aparcados y las interrupciones de la capturadora."),

        new("obs64.exe", "OBS Studio", ProductionWorkload.LiveSwitcher,
            "Mismo perfil que vMix: codifica en directo y pierde frames en cuanto la CPU " +
            "tiene que competir."),

        new("Resolume Arena.exe", "Resolume Arena", ProductionWorkload.MediaServer,
            "Reproduce DXV a cientos de MB/s y lo presenta con cadencia fija. Lo que le " +
            "duele es cualquier cosa que altere el ritmo de presentación."),

        new("Resolume Avenue.exe", "Resolume Avenue", ProductionWorkload.MediaServer,
            "Igual que Arena sin la salida por pared LED, pero con la misma exigencia de " +
            "cadencia."),

        new("WATCHOUT.exe", "Watchout", ProductionWorkload.MediaServer,
            "Lo de Resolume más sincronía entre nodos: además del ritmo local, le duele la " +
            "deriva de reloj contra los demás equipos del cluster."),

        new("WATCHNET.exe", "Watchnet", ProductionWorkload.MediaServer,
            "El control de Watchout. Menos exigente que el display, pero si se queda sin " +
            "CPU deja de mandar órdenes a tiempo."),

        new("QLab.exe", "QLab", ProductionWorkload.AudioPlayback,
            "Dispara audio contra un tiempo. Una pausa de cien milisegundos se oye."),

        new("Ableton Live.exe", "Ableton Live", ProductionWorkload.AudioPlayback,
            "Buffers pequeños y sin margen: cualquier interrupción sale por los altavoces."),
    };

    /// <summary>La aplicación conocida con ese nombre de imagen, o null.</summary>
    public static ProductionApp? Find(string? imageName) =>
        imageName is null
            ? null
            : Known.FirstOrDefault(a =>
                string.Equals(a.ImageName, imageName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// De lo que está corriendo, cuáles son de producción y no van a la prioridad que les toca.
    ///
    /// Se mira lo que corre y no lo que está instalado: un rig con vMix instalado y cerrado
    /// no tiene ningún problema, y decir que sí es el falso positivo que hace que se deje de
    /// mirar la pantalla.
    /// </summary>
    public static ProductionAppAudit Audit(IEnumerable<RunningProcess> running)
    {
        ArgumentNullException.ThrowIfNull(running);

        List<RunningProcess> found = new();
        List<RunningProcess> misconfigured = new();

        foreach (RunningProcess process in running)
        {
            if (Find(process.ImageName) is null)
            {
                continue;
            }

            found.Add(process);

            // Por encima de la objetivo tambien vale: si alguien la ha subido a mano, no
            // hay nada que arreglar. Lo que se busca es que no se quede por debajo.
            if (process.Priority < TargetPriority)
            {
                misconfigured.Add(process);
            }
        }

        return new ProductionAppAudit { Running = found, BelowTarget = misconfigured };
    }
}

/// <summary>Un proceso vivo, con lo poco que hace falta saber de él aquí.</summary>
public readonly record struct RunningProcess(string ImageName, ProcessPriority Priority);

/// <summary>Qué aplicaciones de producción corren y cuáles no van como deberían.</summary>
public sealed record ProductionAppAudit
{
    public IReadOnlyList<RunningProcess> Running { get; init; } = Array.Empty<RunningProcess>();

    public IReadOnlyList<RunningProcess> BelowTarget { get; init; } = Array.Empty<RunningProcess>();

    /// <summary>Nada corriendo no es un problema: es una máquina en reposo.</summary>
    public bool IsClean => BelowTarget.Count == 0;

    /// <summary>
    /// Lo que se lee en la fila.
    ///
    /// Se nombran las aplicaciones y no se da un número: «vMix» dice dónde mirar, «1
    /// proceso» no dice nada.
    /// </summary>
    public string Describe()
    {
        if (Running.Count == 0)
        {
            return "ninguna en marcha";
        }

        return IsClean
            ? "en prioridad alta"
            : string.Join(", ", BelowTarget
                .Select(p => ProductionApps.Find(p.ImageName)?.DisplayName ?? p.ImageName)
                .Distinct(StringComparer.CurrentCulture));
    }
}
