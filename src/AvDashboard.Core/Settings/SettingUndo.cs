using System.Text.Json;
using AvDashboard.Core.Models;

namespace AvDashboard.Core.Settings;

/// <summary>
/// Lo que se guardó de una escritura, tal y como viaja en el payload del Timeline.
///
/// Es una copia deliberada del resultado que devuelve el actuador. El actuador vive en la
/// capa nativa, que Core no puede conocer, y además esto es un formato persistido: si algún
/// día el actuador cambia de forma, lo escrito hace tres meses tiene que seguir leyéndose.
/// </summary>
public sealed record SettingWriteRecord
{
    public string? Id { get; init; }
    public bool Success { get; init; }
    public string? PreviousValue { get; init; }
    public string? AppliedValue { get; init; }
    public bool DryRun { get; init; }
}

/// <summary>Por qué una escritura no se puede deshacer.</summary>
public enum UndoRefusal
{
    None,

    /// <summary>El evento no es una escritura de ajuste.</summary>
    NotASettingWrite,

    /// <summary>Se escribió el mismo valor que había: no hay nada que devolver.</summary>
    NothingChanged,

    /// <summary>No se guardó el valor anterior, o el payload no se puede leer.</summary>
    NoPreviousValue,

    /// <summary>El ajuste ya no está en el catálogo de esta versión.</summary>
    UnknownSetting,

    /// <summary>El ajuste existe pero este agente no sabe escribirlo.</summary>
    NotWritable,

    /// <summary>El valor actual ya no es el que dejó esta escritura: alguien lo cambió después.</summary>
    ChangedSince,
}

/// <summary>
/// Si una escritura del Timeline se puede deshacer, y a qué valor.
///
/// Deshacer es la promesa que lleva escrita el proyecto desde el principio —«toda escritura
/// deja rastro con el valor anterior, y es lo único que hará posible deshacer»— y el dato
/// para cumplirla lleva guardado desde entonces. Lo que faltaba no era el dato sino la
/// decisión, y la decisión es sobre todo saber **cuándo no**.
///
/// El caso que obliga a pensar es el último: si el valor actual ya no es el que dejó esa
/// escritura, alguien lo tocó después —tú a mano, un perfil, una directiva de dominio— y
/// deshacer pisaría ese cambio con un valor de hace tres horas. Un «deshacer» que revierte
/// algo que no hiciste es peor que no tener el botón.
/// </summary>
public static class SettingUndo
{
    /// <summary>Tipo de evento que deja una escritura que sí cambió algo.</summary>
    public const string ChangedKind = "setting.changed";

    /// <summary>Tipo de evento de una escritura que dejó el valor como estaba.</summary>
    public const string UnchangedKind = "setting.unchanged";

    /// <summary>Tipo con el que se anota el propio deshacer.</summary>
    public const string UndoneKind = "setting.undone";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Qué haría deshacer este evento.
    ///
    /// <paramref name="currentValue"/> es lo que el ajuste vale ahora mismo, según la última
    /// auditoría. Null significa que no se ha podido leer, y entonces no se deshace: no
    /// saber en qué estado está algo no es permiso para cambiarlo.
    /// </summary>
    public static UndoPlan Plan(
        TimelineEvent timelineEvent,
        string? currentValue,
        IReadOnlySet<string> writableIds)
    {
        ArgumentNullException.ThrowIfNull(timelineEvent);
        ArgumentNullException.ThrowIfNull(writableIds);

        if (string.Equals(timelineEvent.Kind, UnchangedKind, StringComparison.Ordinal))
        {
            return UndoPlan.No(UndoRefusal.NothingChanged,
                "Esa escritura dejó el valor como estaba: no hay nada que devolver.");
        }

        if (!string.Equals(timelineEvent.Kind, ChangedKind, StringComparison.Ordinal))
        {
            return UndoPlan.No(UndoRefusal.NotASettingWrite,
                "Esa línea del Timeline no es un cambio de ajuste.");
        }

        SettingWriteRecord? record = Read(timelineEvent.PayloadJson);

        if (record?.Id is not string id || record.PreviousValue is not string previous)
        {
            return UndoPlan.No(UndoRefusal.NoPreviousValue,
                "No se guardó el valor anterior de esa escritura.");
        }

        if (SettingsCatalog.Find(id) is not SettingDefinition definition)
        {
            return UndoPlan.No(UndoRefusal.UnknownSetting,
                $"«{id}» ya no está en el catálogo de esta versión.");
        }

        if (!writableIds.Contains(id))
        {
            return UndoPlan.No(UndoRefusal.NotWritable,
                $"Este agente no sabe escribir «{definition.Name}».");
        }

        // El corazón del asunto. Se compara contra lo que se aplicó, no contra el valor
        // anterior: si alguien lo ha tocado después, deshacer pisaría su cambio.
        if (currentValue is null ||
            !string.Equals(currentValue, record.AppliedValue, StringComparison.Ordinal))
        {
            return UndoPlan.No(UndoRefusal.ChangedSince,
                $"«{definition.Name}» ya no vale lo que dejó ese cambio" +
                (currentValue is null ? " y ahora no se puede leer." : $": ahora es «{currentValue}»."));
        }

        return new UndoPlan
        {
            SettingId = id,
            Name = definition.Name,
            TargetValue = previous,
            AppliedValue = record.AppliedValue,

            // Deshacer un disruptivo vuelve a cortar lo que cortó al aplicarse. Se puede,
            // pero hay que decirlo antes y no después.
            IsDisruptive = definition.Risk == SettingRisk.Disruptive,
        };
    }

    private static SettingWriteRecord? Read(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<SettingWriteRecord>(payload, Json);
        }
        catch (JsonException)
        {
            // Un payload de una versión que ya no se entiende no es un fallo del agente: es
            // una línea vieja que simplemente no se puede deshacer.
            return null;
        }
    }
}

/// <summary>Qué haría deshacer una escritura, o por qué no se puede.</summary>
public sealed record UndoPlan
{
    public string? SettingId { get; init; }

    /// <summary>Nombre legible del ajuste, para la confirmación.</summary>
    public string? Name { get; init; }

    /// <summary>El valor al que se volvería.</summary>
    public string? TargetValue { get; init; }

    /// <summary>El valor que dejó la escritura que se deshace.</summary>
    public string? AppliedValue { get; init; }

    public bool IsDisruptive { get; init; }

    public UndoRefusal Refusal { get; init; } = UndoRefusal.None;

    /// <summary>Por qué no se puede, en palabras. Null cuando sí se puede.</summary>
    public string? Reason { get; init; }

    public bool CanUndo => Refusal == UndoRefusal.None && SettingId is not null;

    /// <summary>Lo que se lee en la confirmación: de dónde a dónde vuelve.</summary>
    public string Describe() => CanUndo
        ? $"{Name}: {AppliedValue} → {TargetValue}"
        : Reason ?? "No se puede deshacer.";

    internal static UndoPlan No(UndoRefusal refusal, string reason) =>
        new() { Refusal = refusal, Reason = reason };
}
