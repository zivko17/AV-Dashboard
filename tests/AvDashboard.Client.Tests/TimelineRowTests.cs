using AvDashboard.Client.ViewModels;
using AvDashboard.Core.Models;
using AvDashboard.Core.Settings;
using AvDashboard.Core.Taxonomy;
using Xunit;

namespace AvDashboard.Client.Tests;

/// <summary>
/// La fila del Timeline y cuándo ofrece deshacer.
///
/// Quien decide de verdad si una escritura se puede deshacer es el agente: mira si el
/// ajuste sigue existiendo, si sabe escribirlo y —sobre todo— si alguien lo ha tocado
/// después. Aquí solo se comprueba lo que evita ofrecer el botón donde no pinta nada, que
/// es en una línea de ping o de alerta.
/// </summary>
public class TimelineRowTests
{
    private static TimelineEvent Evento(string kind, string mensaje = "algo pasó") => new()
    {
        Id = 7,
        NodeId = "n",
        Timestamp = DateTimeOffset.Now,
        Module = "M1",
        Sector = Sector.SystemEssential,
        Severity = Severity.Warning,
        Kind = kind,
        Message = mensaje,
    };

    [Fact]
    public void Una_escritura_que_cambio_algo_ofrece_deshacer()
    {
        TimelineEventRow row = new(
            Evento(SettingUndo.ChangedKind, "Transparencia: on → off"), _ => Task.CompletedTask);

        Assert.True(row.CanAttemptUndo);
    }

    [Fact]
    public void Una_escritura_que_no_cambio_nada_no_lo_ofrece()
    {
        TimelineEventRow row = new(
            Evento(SettingUndo.UnchangedKind), _ => Task.CompletedTask);

        Assert.False(row.CanAttemptUndo);
    }

    [Fact]
    public void Lo_que_ya_es_un_deshacer_no_se_vuelve_a_deshacer_desde_su_fila()
    {
        // Deshacer un deshacer se hace deshaciendo la linea original, no encadenando. Si no,
        // dos clics seguidos dejan el ajuste donde estaba y el Timeline lleno de ruido.
        TimelineEventRow row = new(
            Evento(SettingUndo.UndoneKind), _ => Task.CompletedTask);

        Assert.False(row.CanAttemptUndo);
    }

    [Fact]
    public void Una_linea_que_no_es_una_escritura_no_ofrece_nada()
    {
        foreach (string kind in new[] { "ping.critical", "device.new", "inventory.initial" })
        {
            TimelineEventRow row = new(Evento(kind), _ => Task.CompletedTask);
            Assert.False(row.CanAttemptUndo, kind);
        }
    }

    [Fact]
    public void Sin_quien_lo_ejecute_no_se_ofrece_el_boton()
    {
        // Las filas del dashboard se construyen sin accion de deshacer: alli no hay a donde
        // volver porque no es la vista del Timeline.
        TimelineEventRow row = new(Evento(SettingUndo.ChangedKind));

        Assert.False(row.CanAttemptUndo);
    }

    [Fact]
    public void El_comando_llama_a_quien_deshace_con_su_propia_fila()
    {
        TimelineEventRow? recibida = null;
        TimelineEventRow row = new(
            Evento(SettingUndo.ChangedKind), r => { recibida = r; return Task.CompletedTask; });

        row.UndoCommand.Execute(null);

        Assert.Same(row, recibida);
    }

    [Fact]
    public void La_hora_lleva_segundos_porque_el_orden_dentro_del_minuto_es_lo_que_se_lee()
    {
        DateTime local = new(2026, 8, 26, 21, 4, 12, DateTimeKind.Local);

        TimelineEventRow row = new(Evento(SettingUndo.ChangedKind) with
        {
            Timestamp = new DateTimeOffset(local),
        });

        Assert.Equal("26/08 21:04:12", row.TimeText);
    }
}
