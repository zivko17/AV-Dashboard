using System.Text.Json;
using System.Text.Json.Serialization;
using AvDashboard.Core.Models;

namespace AvDashboard.Core.Profiles;

/// <summary>
/// Parámetros tipados de cada actuador.
///
/// Viven en Core y no en Native a propósito: son el contrato. Permiten escribir un perfil
/// completo, calcular su diff y probarlo antes de que exista una sola línea de código que
/// toque el registro de Windows.
/// </summary>
public static class ActionParameters
{
    /// <summary>
    /// Serialización compartida por el motor de perfiles, la API y SQLite. Un perfil
    /// guardado hoy tiene que poder leerse dentro de dos versiones, así que el formato
    /// se fija aquí y no en cada sitio que serializa.
    /// </summary>
    public static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);

    /// <summary>
    /// Deserializa los parámetros de un paso. Lanza <see cref="ProfileParameterException"/>
    /// con el ActionId en el mensaje: un JSON mal formado en un perfil tiene que decir de
    /// qué paso viene, no dejar un «unexpected token» a secas.
    /// </summary>
    public static T Deserialize<T>(string json, string actionId)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, Json)
                   ?? throw new ProfileParameterException(actionId, "los parámetros están vacíos");
        }
        catch (JsonException ex)
        {
            throw new ProfileParameterException(actionId, ex.Message, ex);
        }
    }
}

/// <summary>Parámetros ilegibles en un paso de perfil.</summary>
public sealed class ProfileParameterException : Exception
{
    public ProfileParameterException(string actionId, string reason, Exception? inner = null)
        : base($"Parámetros no válidos para «{actionId}»: {reason}", inner)
    {
        ActionId = actionId;
    }

    public string ActionId { get; }
}

// --- M1 Sistema ---

public sealed record PowerPlanParameters
{
    /// <summary>GUID del esquema. Null con <see cref="Ultimate"/> en true usa el conocido.</summary>
    public Guid? SchemeGuid { get; init; }

    /// <summary>Atajo para Ultimate Performance, duplicándolo si hace falta.</summary>
    public bool Ultimate { get; init; }
}

public sealed record BooleanSettingParameters
{
    public required bool Enabled { get; init; }
}

public sealed record NumericSettingParameters
{
    public required long Value { get; init; }
}

public sealed record VisualEffectsParameters
{
    public required VisualEffectsSetting Setting { get; init; }
}

public sealed record FocusAssistParameters
{
    public required FocusAssistMode Mode { get; init; }
}

public sealed record WindowsUpdatePauseParameters
{
    /// <summary>Días de pausa. Windows admite hasta 35.</summary>
    public required int Days { get; init; }
}

public sealed record ProcessPriorityParameters
{
    public required string ImageName { get; init; }
    public required ProcessPriority Priority { get; init; }
    public ulong? AffinityMask { get; init; }
}

// --- M2 Firewall ---

public sealed record FirewallProfileParameters
{
    public required bool Enabled { get; init; }

    /// <summary>Perfiles a los que aplica. Vacío = los tres.</summary>
    public IReadOnlyList<string> Profiles { get; init; } = Array.Empty<string>();
}

public sealed record FirewallPresetParameters
{
    /// <summary>Servicios del catálogo de <c>AvPorts</c> a permitir. Vacío = todos.</summary>
    public IReadOnlyList<string> Services { get; init; } = Array.Empty<string>();

    public bool Inbound { get; init; } = true;
    public bool Outbound { get; init; } = true;
}

// --- M3 Audio ---

public sealed record AudioDefaultDeviceParameters
{
    /// <summary>Id del endpoint. Si no está presente en el nodo, el paso se salta.</summary>
    public string? DeviceId { get; init; }

    /// <summary>
    /// Alternativa por nombre, que es lo que sobrevive a reinstalar un driver. El actuador
    /// resuelve el id por coincidencia exacta y, si no la hay, por prefijo.
    /// </summary>
    public string? FriendlyName { get; init; }
}

// --- M4 Vídeo ---

public sealed record LayoutRestoreParameters
{
    public required string LayoutName { get; init; }
}

// --- M5 Red ---

public sealed record NetworkProfileParameters
{
    public required string ProfileName { get; init; }
}

public sealed record NicSettingParameters
{
    /// <summary>Nombre de la interfaz. Vacío = todas las interfaces físicas activas.</summary>
    public string? InterfaceName { get; init; }

    public required bool Enabled { get; init; }
}

public sealed record NdiDiscoveryParameters
{
    /// <summary>Dirección del Discovery Server. Null vuelve a mDNS multicast.</summary>
    public string? ServerAddress { get; init; }
}

// --- M6 Servicios ---

public sealed record ServiceStateParameters
{
    public required IReadOnlyList<string> Services { get; init; }
    public required bool Running { get; init; }

    /// <summary>Segundos a esperar a que el servicio alcance el estado pedido.</summary>
    public int TimeoutSeconds { get; init; } = 30;
}

public sealed record ServiceStartModeParameters
{
    public required IReadOnlyList<string> Services { get; init; }
    public required ServiceStartMode StartMode { get; init; }
}
