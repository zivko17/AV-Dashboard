using AvDashboard.Core.Models;
using AvDashboard.Native;

namespace AvDashboard.Agent.Modules;

/// <summary>
/// Que modulos estan vivos en este nodo y por que.
///
/// El agente es el mismo binario en todas partes; los modulos se auto-desactivan si
/// falta el hardware o el SDK. La consola pinta en gris lo que no aplica en lugar de
/// ensenar un cero que parece un dato.
/// </summary>
public sealed class ModuleRegistry
{
    private readonly List<ModuleDescriptor> _modules = new();

    public ModuleRegistry(AgentOptions options)
    {
        bool elevated = OperatingSystem.IsWindows() && Platform.IsElevated;

        Add("M1", "Sistema y rendimiento", ModuleAvailability.Available);
        Add("M2", "Firewall", ModuleAvailability.NotImplemented, "Llega en la Fase 2 junto con los actuadores.");
        Add("M3", "Audio y reloj", ModuleAvailability.Available);
        Add("M3-PTP", "PTP / IEEE 1588", ModuleAvailability.MissingDependency,
            "Requiere Npcap en modo compatible con WinPcap. PTP no se puede leer con sockets normales.");
        Add("M4", "Vídeo", ModuleAvailability.Available,
            "Topología con conector físico, refresco exacto y HDR. DeckLink y NVAPI siguen pendientes.");
        Add("M4-LED", "Procesadores LED", ModuleAvailability.NotImplemented,
            "Capa 1 (alcanzabilidad) en la Fase 7; las capas 2 y 3 necesitan el hardware delante.");
        Add("M5", "Red", ModuleAvailability.Available);
        Add("M6", "Servicios", ModuleAvailability.Available);
        Add("M7", "Nodos remotos", ModuleAvailability.NotImplemented, "Fase 3.");
        Add("M8", "Aplicaciones de producción", ModuleAvailability.NotImplemented, "Fase 5.");
        Add("M9", "Almacenamiento", ModuleAvailability.Available);
        Add("M10", "Perfiles de escenario", ModuleAvailability.NotImplemented,
            "El motor está en Core; los actuadores llegan en la Fase 2.");
        Add("M11", "Timeline, alertas e informes", ModuleAvailability.Available,
            "Timeline y checklist pre-show operativos; los informes exportables en la Fase 6.");
        Add("M12", "Validador de contenido", ModuleAvailability.NotImplemented, "Fase 5. Requiere ffprobe en el PATH.");
        Add("M13", "Licencias y dongles", ModuleAvailability.NotImplemented, "Fase 5.");
        Add("M14", "Alimentación y SAI", ModuleAvailability.NotImplemented, "Fase 5.");
        Add("M15", "Acciones de pánico", ModuleAvailability.NotImplemented, "Fase 2.");
        Add("M16", "Puente con Companion", ModuleAvailability.NotImplemented, "Fase 6.");
        Add("M17", "Inventario de flota", ModuleAvailability.Available);
        Add("M18", "EDID Toolkit", ModuleAvailability.Available,
            "Lectura, decodificación, validación y diff. El forzado por NVAPI sigue pendiente.");

        if (options.PortableMode)
        {
            Downgrade("M2", ModuleAvailability.DisabledByConfiguration, "Modo portátil: solo lectura.");
            Downgrade("M10", ModuleAvailability.DisabledByConfiguration, "Modo portátil: solo lectura.");
            Downgrade("M15", ModuleAvailability.DisabledByConfiguration, "Modo portátil: solo lectura.");
        }

        if (!elevated)
        {
            // Sin elevacion se puede leer casi todo, pero algunas ramas del registro y
            // el control de servicios quedan fuera. Se dice en vez de fallar despues.
            Annotate("M1", "Sin elevación: algunos ajustes del registro no son legibles.");
            Annotate("M6", "Sin elevación: se puede listar pero no arrancar ni parar servicios.");
        }
    }

    public IReadOnlyList<ModuleDescriptor> All => _modules;

    public ModuleDescriptor? Get(string id) =>
        _modules.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));

    public bool IsAvailable(string id) => Get(id)?.IsAvailable ?? false;

    private void Add(string id, string name, ModuleAvailability availability, string? reason = null) =>
        _modules.Add(new ModuleDescriptor
        {
            Id = id,
            Name = name,
            Availability = availability,
            Reason = reason,
        });

    private void Downgrade(string id, ModuleAvailability availability, string reason)
    {
        int index = _modules.FindIndex(m => m.Id == id);
        if (index >= 0)
        {
            _modules[index] = _modules[index] with { Availability = availability, Reason = reason };
        }
    }

    private void Annotate(string id, string note)
    {
        int index = _modules.FindIndex(m => m.Id == id);
        if (index >= 0)
        {
            string existing = _modules[index].Reason is string reason ? reason + " " : string.Empty;
            _modules[index] = _modules[index] with { Reason = existing + note };
        }
    }
}
