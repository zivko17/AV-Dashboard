using System.Text.Json;
using AvDashboard.Core.Models;
using AvDashboard.Core.Settings;
using AvDashboard.Core.Taxonomy;
using Xunit;

namespace AvDashboard.Core.Tests;

/// <summary>
/// Deshacer una escritura desde su línea del Timeline.
///
/// El dato lleva guardado desde el principio: «toda escritura deja rastro con el valor
/// anterior, y es lo único que hará posible deshacer». Lo que faltaba no era el dato sino la
/// decisión, y la decisión es sobre todo saber **cuándo no**.
/// </summary>
public class SettingUndoTests
{
    private static readonly IReadOnlySet<string> TodoEscribible =
        SettingsCatalog.All.Select(d => d.Id).ToHashSet();

    private static TimelineEvent Evento(
        string kind = SettingUndo.ChangedKind,
        string? id = SettingIds.Transparency,
        string? previous = "on",
        string? applied = "off",
        string? payloadCrudo = null) => new()
    {
        Id = 42,
        NodeId = "n",
        Timestamp = DateTimeOffset.UtcNow,
        Module = "M1",
        Sector = Sector.SystemEssential,
        Kind = kind,
        Message = "Transparencia: on → off",
        PayloadJson = payloadCrudo ?? JsonSerializer.Serialize(
            new SettingWriteRecord
            {
                Id = id,
                Success = true,
                PreviousValue = previous,
                AppliedValue = applied,
            },
            new JsonSerializerOptions(JsonSerializerDefaults.Web)),
    };

    // ------------------------------------------------------------------ el caso que sí

    [Fact]
    public void Una_escritura_intacta_se_puede_deshacer()
    {
        UndoPlan plan = SettingUndo.Plan(Evento(), currentValue: "off", TodoEscribible);

        Assert.True(plan.CanUndo);
        Assert.Equal(SettingIds.Transparency, plan.SettingId);
        Assert.Equal("on", plan.TargetValue);
        Assert.Contains("off → on", plan.Describe(), StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ los casos que no

    [Fact]
    public void Si_alguien_lo_ha_cambiado_despues_no_se_deshace()
    {
        // El caso que obliga a pensar. La escritura dejo «off»; ahora vale «on», asi que
        // alguien lo toco despues —a mano, un perfil, una directiva— y deshacer pisaria ese
        // cambio con un valor de hace tres horas. Un deshacer que revierte algo que no
        // hiciste es peor que no tener el boton.
        UndoPlan plan = SettingUndo.Plan(Evento(), currentValue: "on", TodoEscribible);

        Assert.False(plan.CanUndo);
        Assert.Equal(UndoRefusal.ChangedSince, plan.Refusal);
        Assert.Contains("ahora es", plan.Reason!, StringComparison.Ordinal);
    }

    [Fact]
    public void Si_el_valor_actual_no_se_puede_leer_tampoco()
    {
        // No saber en que estado esta algo no es permiso para cambiarlo.
        UndoPlan plan = SettingUndo.Plan(Evento(), currentValue: null, TodoEscribible);

        Assert.False(plan.CanUndo);
        Assert.Equal(UndoRefusal.ChangedSince, plan.Refusal);
    }

    [Fact]
    public void Una_escritura_que_no_cambio_nada_no_tiene_nada_que_deshacer()
    {
        UndoPlan plan = SettingUndo.Plan(
            Evento(kind: SettingUndo.UnchangedKind), currentValue: "off", TodoEscribible);

        Assert.Equal(UndoRefusal.NothingChanged, plan.Refusal);
    }

    [Fact]
    public void Una_linea_del_Timeline_que_no_es_una_escritura_no_se_deshace()
    {
        UndoPlan plan = SettingUndo.Plan(
            Evento(kind: "ping.critical"), currentValue: "off", TodoEscribible);

        Assert.Equal(UndoRefusal.NotASettingWrite, plan.Refusal);
    }

    [Fact]
    public void Sin_valor_anterior_guardado_no_se_puede_volver_a_ninguna_parte()
    {
        UndoPlan plan = SettingUndo.Plan(
            Evento(previous: null), currentValue: "off", TodoEscribible);

        Assert.Equal(UndoRefusal.NoPreviousValue, plan.Refusal);
    }

    [Fact]
    public void Un_payload_de_otra_version_no_revienta_el_agente()
    {
        // Es un formato persistido: lo escrito hace tres meses tiene que poder leerse, y si
        // no se entiende es una linea vieja que no se puede deshacer, no un fallo.
        foreach (string basura in new[] { "no es json", "{\"otra\":\"forma\"}", "", "   " })
        {
            UndoPlan plan = SettingUndo.Plan(
                Evento(payloadCrudo: basura), currentValue: "off", TodoEscribible);

            Assert.Equal(UndoRefusal.NoPreviousValue, plan.Refusal);
        }
    }

    [Fact]
    public void Un_ajuste_que_ya_no_esta_en_el_catalogo_se_dice_asi()
    {
        UndoPlan plan = SettingUndo.Plan(
            Evento(id: "ajuste.que.se.borro"), currentValue: "off", TodoEscribible);

        Assert.Equal(UndoRefusal.UnknownSetting, plan.Refusal);
    }

    [Fact]
    public void Un_ajuste_que_este_agente_no_sabe_escribir_se_dice_asi()
    {
        UndoPlan plan = SettingUndo.Plan(
            Evento(), currentValue: "off", writableIds: new HashSet<string>());

        Assert.Equal(UndoRefusal.NotWritable, plan.Refusal);
    }

    // ------------------------------------------------------------------ disruptivos

    [Fact]
    public void Deshacer_un_disruptivo_se_puede_pero_avisa()
    {
        // Los offloads de la NIC: deshacerlos vuelve a reiniciar el adaptador, asi que el
        // enlace se cae otra vez. Se puede, pero hay que decirlo antes y no despues.
        UndoPlan plan = SettingUndo.Plan(
            Evento(id: SettingIds.NicOffloads, previous: "1", applied: "0"),
            currentValue: "0", TodoEscribible);

        Assert.True(plan.CanUndo);
        Assert.True(plan.IsDisruptive);
    }

    [Fact]
    public void Un_ajuste_normal_no_se_marca_como_disruptivo()
    {
        Assert.False(SettingUndo.Plan(Evento(), "off", TodoEscribible).IsDisruptive);
    }
}
