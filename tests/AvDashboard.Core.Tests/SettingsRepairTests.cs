using AvDashboard.Core.Settings;
using Xunit;

namespace AvDashboard.Core.Tests;

/// <summary>
/// El reparto de una auditoría entre lo que un botón puede arreglar solo y lo que no.
///
/// Es la decisión más peligrosa de la aplicación —un botón que escribe varios ajustes de
/// golpe— y por eso se prueba aquí en vez de confiarse a un `where` en la vista, donde nadie
/// vería que un ajuste de seguridad se ha colado en el barrido.
/// </summary>
public class SettingsRepairTests
{
    private static SettingsAudit Auditar(params SettingReading[] readings) =>
        SettingsCatalog.Evaluate(readings, SettingsCatalog.All.Select(d => d.Id).ToHashSet());

    [Fact]
    public void Solo_entra_lo_critico_mientras_no_se_pidan_los_avisos()
    {
        // La transparencia esta en ambar y el plan de energia en rojo.
        SettingsAudit audit = Auditar(
            SettingReading.Of(SettingIds.PowerPlan, "Equilibrado"),
            SettingReading.Of(SettingIds.Transparency, SettingReading.BooleanOn));

        RepairPlan solo = SettingsRepair.Plan(audit);
        Assert.Contains(solo.Fixable, f => f.Definition.Id == SettingIds.PowerPlan);
        Assert.DoesNotContain(solo.Fixable, f => f.Definition.Id == SettingIds.Transparency);

        RepairPlan todo = SettingsRepair.Plan(audit, includeWarnings: true);
        Assert.Contains(todo.Fixable, f => f.Definition.Id == SettingIds.Transparency);
    }

    [Fact]
    public void Ningun_ajuste_con_riesgo_declara_objetivo_en_el_catalogo()
    {
        // Es la garantia de verdad, y es del catalogo y no del filtro: sin valor objetivo no
        // hay nada que escribir, asi que HVCI, las mitigaciones de Spectre y HAGS no pueden
        // entrar en un barrido ni por descuido. Esta prueba salta el dia que alguien le ponga
        // objetivo a uno de ellos, que es justo el dia en que hay que pararse a pensarlo.
        Assert.All(
            SettingsCatalog.All.Where(d => d.Risk != SettingRisk.None),
            d => Assert.False(d.HasTarget, $"«{d.Name}» tiene riesgo {d.Risk} y ademas objetivo"));
    }

    [Fact]
    public void El_filtro_de_riesgo_aparta_lo_que_tenga_contrapartida()
    {
        // Segundo cinturon, igual que en SettingsWriter.Apply: aunque hoy el catalogo no
        // permita que ocurra, el reparto se comprueba tambien aqui. Si manana alguien le pone
        // objetivo a un ajuste de seguridad, se va a «apartado» y no a «arreglable».
        SettingsAudit audit = Sintetica(
            Definicion("prueba.seguridad", SettingRisk.Security),
            Definicion("prueba.compromiso", SettingRisk.Tradeoff),
            Definicion("prueba.normal", SettingRisk.None));

        RepairPlan plan = SettingsRepair.Plan(audit, includeWarnings: true);

        Assert.Equal(new[] { "prueba.normal" }, plan.Fixable.Select(f => f.Definition.Id));
        Assert.Equal(2, plan.SkippedByRisk.Count);
    }

    [Fact]
    public void Lo_que_el_agente_no_puede_escribir_se_aparta_diciendo_por_que()
    {
        // Sin ningun id escribible: todo lo desviado cae en «no se puede escribir».
        SettingsAudit audit = SettingsCatalog.Evaluate(
            new[] { SettingReading.Of(SettingIds.PowerPlan, "Equilibrado") },
            writableIds: new HashSet<string>());

        RepairPlan plan = SettingsRepair.Plan(audit);

        Assert.Empty(plan.Fixable);
        Assert.Contains(plan.SkippedNotWritable, s => s.Definition.Id == SettingIds.PowerPlan);
        Assert.False(plan.HasWork);
    }

    [Fact]
    public void Un_ajuste_sin_objetivo_no_se_puede_arreglar_solo()
    {
        // Los de diagnostico no declaran valor correcto: solo informan. Sin objetivo no hay
        // nada que escribir, y meterlos en la lista seria prometer un arreglo inexistente.
        RepairPlan plan = SettingsRepair.Plan(Auditar(), includeWarnings: true);

        Assert.All(plan.Fixable, f => Assert.True(f.Definition.HasTarget));
    }

    // ------------------------------------------------------------------ utilidades

    /// <summary>
    /// Una definicion inventada, para poder probar combinaciones que el catalogo real no
    /// tiene. Con objetivo siempre: lo que se quiere aislar aqui es el filtro de riesgo.
    /// </summary>
    private static SettingDefinition Definicion(string id, SettingRisk risk) => new()
    {
        Id = id,
        Group = SettingGroup.Kernel,
        Name = id,
        Source = "prueba",
        AcceptableValues = new[] { "correcto" },
        TargetDescription = "correcto",
        Impact = SettingImpact.High,
        Risk = risk,
        Rationale = "prueba",
    };

    /// <summary>Una auditoria montada a mano, con todo desviado y escribible.</summary>
    private static SettingsAudit Sintetica(params SettingDefinition[] definitions) => new()
    {
        Groups = new[]
        {
            new SettingGroupSummary
            {
                Group = SettingGroup.Kernel,
                Settings = definitions.Select(d => new SettingEvaluation
                {
                    Definition = d,
                    Verdict = SettingVerdict.DiffersCritical,
                    Current = "incorrecto",
                    Writable = true,
                }).ToList(),
            },
        },
    };

    [Fact]
    public void La_descripcion_dice_de_donde_a_donde_va_cada_uno()
    {
        RepairPlan plan = SettingsRepair.Plan(
            Auditar(SettingReading.Of(SettingIds.PowerPlan, "Equilibrado")));

        string linea = Assert.Single(plan.Describe());
        Assert.Contains("Equilibrado", linea, StringComparison.Ordinal);
        Assert.Contains("→", linea, StringComparison.Ordinal);
    }
}
