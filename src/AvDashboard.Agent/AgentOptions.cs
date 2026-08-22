namespace AvDashboard.Agent;

/// <summary>Configuracion del agente. Se lee de appsettings.json y de la linea de comandos.</summary>
public sealed class AgentOptions
{
    public const string SectionName = "Agent";

    /// <summary>Puerto de escucha. Nunca se expone en 0.0.0.0: ver <see cref="BindLoopback"/>.</summary>
    public int Port { get; set; } = 47821;

    /// <summary>Escuchar en 127.0.0.1. Es lo que usa la consola local.</summary>
    public bool BindLoopback { get; set; } = true;

    /// <summary>
    /// Escuchar tambien en la IP de Tailscale, si la hay. Es la unica forma de acceso
    /// remoto contemplada: nada de bind a todas las interfaces.
    /// </summary>
    public bool BindTailscale { get; set; } = true;

    /// <summary>Nombre visible del nodo. Null = nombre de la maquina.</summary>
    public string? NodeName { get; set; }

    /// <summary>
    /// Modo portatil: base de datos y configuracion junto al ejecutable, sin servicio,
    /// solo lectura. Para diagnosticar en una maquina de cliente en cinco minutos.
    /// </summary>
    public bool PortableMode { get; set; }

    public TelemetryOptions Telemetry { get; set; } = new();
    public RetentionOptions Retention { get; set; } = new();

    /// <summary>Dispositivos que entran en el ping continuo y en la checklist pre-show.</summary>
    public List<CriticalDeviceOptions> CriticalDevices { get; set; } = new();
}

public sealed class TelemetryOptions
{
    /// <summary>Telemetria ligera. 1 s es el compromiso entre util y barato.</summary>
    public int SystemIntervalMs { get; set; } = 1000;

    /// <summary>Medidores de audio: 50 ms son 20 fps, que es lo que pide el ojo.</summary>
    public int AudioMeterIntervalMs { get; set; } = 50;

    /// <summary>Los servicios cambian poco; cada 15 s sobra y ahorra una consulta WMI.</summary>
    public int ServiceIntervalMs { get; set; } = 15000;

    public int NetworkIntervalMs { get; set; } = 1000;
    public int PingIntervalMs { get; set; } = 1000;
    public int PingTimeoutMs { get; set; } = 800;
}

public sealed class RetentionOptions
{
    /// <summary>Dias de muestras en crudo antes del rollup horario.</summary>
    public int RawSampleDays { get; set; } = 7;

    public int EventDays { get; set; } = 90;
}

public sealed class CriticalDeviceOptions
{
    public required string Address { get; set; }
    public string? Alias { get; set; }
}
