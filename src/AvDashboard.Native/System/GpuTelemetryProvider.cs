using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using AvDashboard.Core.Models;
using Microsoft.Win32;

namespace AvDashboard.Native.System;

/// <summary>
/// Carga y memoria de la GPU (M1).
///
/// Va aparte de <see cref="SystemTelemetryProvider"/> y con su propia cadencia porque es la
/// única lectura del panel que sí necesita <c>PerformanceCounter</c>. Windows no publica la
/// ocupación de la GPU por ninguna otra vía documentada: ni WMI ni una llamada de sistema la
/// dan, y las APIs de fabricante —NVAPI, ADL— resolverían solo una marca cada una.
///
/// El coste es real. La categoría «GPU Engine» tiene una instancia por proceso y motor, así
/// que en una máquina con veinte aplicaciones abiertas son cientos, y enumerarlas cuesta
/// decenas de milisegundos. Por eso esto se lee cada dos segundos y no cada uno, y por eso
/// el resultado se cachea: la carga de GPU no cambia lo bastante en un segundo como para
/// pagar ese peaje al ritmo del resto de la telemetría.
///
/// Si la categoría no existe —sin controlador WDDM 2.0, o con los contadores rotos, que
/// pasa— se devuelve null y la tarjeta lo dice. No se inventa un cero.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class GpuTelemetryProvider : IDisposable
{
    /// <summary>
    /// Cada cuánto se paga la enumeración. Ver la nota de arriba sobre el coste.
    /// </summary>
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    private GpuLoad? _cached;
    private DateTimeOffset _readAt = DateTimeOffset.MinValue;

    /// <summary>
    /// Contadores vivos por instancia. Ver la nota de <see cref="ReadUtilization"/>: un
    /// contador de tasa recien creado devuelve cero, asi que no se pueden crear y tirar.
    /// </summary>
    private readonly Dictionary<string, PerformanceCounter> _engines =
        new(StringComparer.OrdinalIgnoreCase);

    private long? _dedicatedTotalBytes;
    private bool _categoriesMissing;
    private bool _disposed;

    /// <summary>
    /// Última lectura, refrescada como mucho cada <see cref="Interval"/>.
    ///
    /// Devuelve null mientras no haya podido leer nada, que es distinto de leer cero.
    /// </summary>
    public GpuLoad? Read()
    {
        Platform.EnsureWindows();

        if (_categoriesMissing)
        {
            return null;
        }

        if (DateTimeOffset.UtcNow - _readAt < Interval)
        {
            return _cached;
        }

        _readAt = DateTimeOffset.UtcNow;

        try
        {
            double? utilization = ReadUtilization();

            if (utilization is null)
            {
                _categoriesMissing = true;
                return _cached = null;
            }

            long? used = ReadDedicatedUsage();
            long? total = _dedicatedTotalBytes ??= ReadDedicatedTotalFromRegistry();

            return _cached = new GpuLoad
            {
                UsagePercent = utilization.Value,
                MemoryUsedBytes = used,
                MemoryTotalBytes = total,
            };
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException
                                      or Win32Exception or PlatformNotSupportedException)
        {
            // Los contadores de rendimiento se corrompen con una frecuencia que sorprende
            // —basta una actualizacion de controlador a medias— y cuando pasa, lanzan.
            // Una tarjeta que no sabe su dato no puede tumbar el ciclo de telemetria.
            _categoriesMissing = true;
            return _cached = null;
        }
    }

    /// <summary>
    /// Ocupación como la enseña el Administrador de tareas: el motor más cargado.
    ///
    /// Sumar todos los motores da cifras por encima del 100 %, porque 3D, copia, codificación
    /// y descodificación trabajan a la vez y son recursos distintos. Lo que a ti te importa
    /// es si alguno está saturado, no cuántos hay ocupados, así que se suma por motor y se
    /// coge el mayor.
    ///
    /// Los contadores se guardan vivos entre lecturas y **no** se crean en cada una. Este es
    /// un contador de tasa: su valor sale de comparar dos muestras, así que la primera
    /// llamada de un contador recién creado devuelve siempre cero. Creándolo cada vez, la
    /// GPU salía al 0 % de forma permanente mientras Windows marcaba 1,4 %, y el fallo es
    /// invisible en una máquina en reposo, que es justo donde se prueba.
    /// </summary>
    private double? ReadUtilization()
    {
        if (!PerformanceCounterCategory.Exists("GPU Engine"))
        {
            return null;
        }

        string[] instances = new PerformanceCounterCategory("GPU Engine").GetInstanceNames();
        Dictionary<string, double> byEngine = new(StringComparer.OrdinalIgnoreCase);

        foreach (string instance in instances)
        {
            if (!_engines.TryGetValue(instance, out PerformanceCounter? counter))
            {
                counter = new PerformanceCounter(
                    "GPU Engine", "Utilization Percentage", instance, readOnly: true);

                _engines[instance] = counter;

                // Primera muestra de este proceso: sin pareja con la que comparar, su valor
                // no significa nada. Se descarta y contará a partir de la vuelta siguiente.
                counter.NextValue();
                continue;
            }

            byEngine[EngineOf(instance)] =
                byEngine.GetValueOrDefault(EngineOf(instance)) + counter.NextValue();
        }

        // Los procesos que han terminado dejan su contador colgando. Sin esto, una jornada
        // abriendo y cerrando aplicaciones acumula miles de handles.
        foreach (string gone in _engines.Keys.Except(instances, StringComparer.OrdinalIgnoreCase).ToList())
        {
            _engines[gone].Dispose();
            _engines.Remove(gone);
        }

        return byEngine.Count == 0 ? 0 : Math.Min(byEngine.Values.Max(), 100);
    }

    /// <summary>
    /// El motor sale del nombre de instancia: `pid_1234_luid_..._phys_0_eng_0_engtype_3D`.
    ///
    /// Se parte por el sufijo y no por posición porque el prefijo cambia de longitud con el
    /// pid y con el LUID, y trocear por índice funcionaba en la máquina de desarrollo y
    /// dejaba de funcionar en la siguiente.
    /// </summary>
    private static string EngineOf(string instance)
    {
        const string marker = "engtype_";
        int at = instance.IndexOf(marker, StringComparison.OrdinalIgnoreCase);

        return at < 0 ? "desconocido" : instance[(at + marker.Length)..];
    }

    private static long? ReadDedicatedUsage()
    {
        if (!PerformanceCounterCategory.Exists("GPU Adapter Memory"))
        {
            return null;
        }

        PerformanceCounterCategory category = new("GPU Adapter Memory");
        double total = 0;

        foreach (string instance in category.GetInstanceNames())
        {
            using PerformanceCounter counter =
                new("GPU Adapter Memory", "Dedicated Usage", instance, readOnly: true);

            total += counter.NextValue();
        }

        return (long)total;
    }

    /// <summary>
    /// VRAM total de la tarjeta, del registro.
    ///
    /// No sale de los contadores, y la vía evidente —`Win32_VideoController.AdapterRAM` por
    /// WMI— está limitada a 32 bits y devuelve 4 GB para cualquier tarjeta que tenga más,
    /// que hoy son todas las que importan. `qwMemorySize` sí es de 64 bits.
    ///
    /// Se lee una sola vez: la VRAM instalada no cambia sin reiniciar.
    /// </summary>
    private static long? ReadDedicatedTotalFromRegistry()
    {
        const string displayClass =
            @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

        try
        {
            using RegistryKey? root = Registry.LocalMachine.OpenSubKey(displayClass);

            if (root is null)
            {
                return null;
            }

            long largest = 0;

            foreach (string name in root.GetSubKeyNames())
            {
                // Las subclaves son «0000», «0001»… Cualquier otra cosa es configuración
                // del propio controlador y no un adaptador.
                if (!int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out _))
                {
                    continue;
                }

                using RegistryKey? adapter = root.OpenSubKey(name);

                if (adapter?.GetValue("HardwareInformation.qwMemorySize") is long size && size > largest)
                {
                    largest = size;
                }
            }

            return largest > 0 ? largest : null;
        }
        catch (Exception ex) when (ex is global::System.Security.SecurityException
                                      or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cached = null;

        foreach (PerformanceCounter counter in _engines.Values)
        {
            counter.Dispose();
        }

        _engines.Clear();
    }
}
