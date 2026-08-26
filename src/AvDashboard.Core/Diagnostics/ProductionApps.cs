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

    /// <summary>
    /// Autoría y control: no pinta la salida, pero manda órdenes contra un reloj.
    ///
    /// Se distingue del servidor de medios a propósito. Si el que pinta y el que manda
    /// compiten por la misma CPU en un equipo, el que tiene que ganar es el que pinta.
    /// </summary>
    ShowControl,
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
/// **Los nombres están verificados contra instalaciones reales, no deducidos.** La primera
/// versión de esta lista los adivinó y falló en dos de los tres casos que importaban:
/// Resolume Arena es `Arena.exe` y no «Resolume Arena.exe», y Watchout 7 no tiene ningún
/// `WATCHOUT.exe`. Una lista con nombres inventados no da error: simplemente no encuentra
/// nada nunca, y la fila sale verde diciendo «ninguna en marcha» con el rig funcionando.
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
        // ------------------------------------------------------------ mezcladores
        new("vMix64.exe", "vMix", ProductionWorkload.LiveSwitcher,
            "Mezcla, codifica NDI y SRT y mete audio en el mismo hilo de tiempo. Lo que le " +
            "duele son los núcleos aparcados y las interrupciones de la capturadora."),

        new("obs64.exe", "OBS Studio", ProductionWorkload.LiveSwitcher,
            "Mismo perfil que vMix: codifica en directo y pierde frames en cuanto la CPU " +
            "tiene que competir."),

        // ------------------------------------------------------- servidores de medios
        new("Arena.exe", "Resolume Arena", ProductionWorkload.MediaServer,
            "Reproduce DXV a cientos de MB/s y lo presenta con cadencia fija. Lo que le " +
            "duele es cualquier cosa que altere el ritmo de presentación."),

        new("Avenue.exe", "Resolume Avenue", ProductionWorkload.MediaServer,
            "Igual que Arena sin la salida por pared LED, pero con la misma exigencia de " +
            "cadencia."),

        // Watchout 7 es multiproceso y no tiene un ejecutable unico: el que pinta la salida
        // es visualrenderer, y es el que no puede perder un frame. Los demas mandan y
        // sirven, y molestan si compiten con el.
        new("visualrenderer.exe", "Watchout · display", ProductionWorkload.MediaServer,
            "El motor que pinta la salida de Watchout. Es el proceso que no puede perder un " +
            "frame, y el único de los de Watchout que compite de verdad por la GPU."),

        new("audiorenderer.exe", "Watchout · audio", ProductionWorkload.AudioPlayback,
            "La salida de audio de Watchout, contra el mismo reloj que el vídeo. Una pausa " +
            "aquí se oye antes de que se vea."),

        // ------------------------------------------------------------ control de show
        new("Producer.exe", "Watchout · producer", ProductionWorkload.ShowControl,
            "La autoría y el control de Watchout. No pinta la salida, pero si se queda sin " +
            "CPU deja de mandar órdenes a tiempo."),

        new("director.exe", "Watchout · director", ProductionWorkload.ShowControl,
            "Coordina los nodos del cluster. Su problema no es la cadencia sino llegar a " +
            "tiempo a los demás equipos."),

        new("runner.exe", "Watchout · runner", ProductionWorkload.ShowControl,
            "Ejecuta la timeline en cada nodo. Va con el director y se le aplica el mismo " +
            "criterio."),

        // ------------------------------------------------------------------- audio
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
    /// Se nombran las aplicaciones y no se da un número: «Watchout · display» dice dónde
    /// mirar, «1 proceso» no dice nada.
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
