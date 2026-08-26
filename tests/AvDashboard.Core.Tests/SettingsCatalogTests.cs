using AvDashboard.Core.Models;
using AvDashboard.Core.Profiles;
using AvDashboard.Core.Settings;
using Xunit;

namespace AvDashboard.Core.Tests;

/// <summary>
/// El catálogo decide qué se pinta en rojo delante de una máquina que tiene que salir a
/// escena en veinte minutos. Un falso rojo hace que dejes de mirarlos; un falso verde es
/// peor. Las dos cosas se prueban aquí.
/// </summary>
public class SettingsCatalogTests
{
    [Fact]
    public void Los_identificadores_no_se_repiten()
    {
        IEnumerable<string> duplicados = SettingsCatalog.All
            .GroupBy(d => d.Id, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key);

        Assert.Empty(duplicados);
    }

    [Fact]
    public void Toda_fila_declara_por_que_importa()
    {
        Assert.All(SettingsCatalog.All, d =>
        {
            Assert.False(string.IsNullOrWhiteSpace(d.Rationale));
            Assert.False(string.IsNullOrWhiteSpace(d.Name));
            Assert.False(string.IsNullOrWhiteSpace(d.Source));
        });
    }

    [Fact]
    public void Toda_fila_pertenece_a_un_grupo_que_se_pinta()
    {
        Assert.All(SettingsCatalog.All, d => Assert.Contains(d.Group, SettingsCatalog.Groups));
    }

    [Fact]
    public void Los_actuadores_referenciados_existen()
    {
        // Una fila que apunte a un actuador inventado compilaria igual y fallaria al
        // aplicar el perfil, que es el peor momento para descubrirlo.
        Assert.All(
            SettingsCatalog.All.Where(d => d.ActionId is not null),
            d => Assert.Contains(d.ActionId!, ActionIds.All));
    }

    [Fact]
    public void Un_ajuste_sin_objetivo_nunca_se_pinta_en_rojo()
    {
        // HAGS no tiene respuesta universal: en unos rigs mejora y en otros empeora.
        SettingDefinition hags = SettingsCatalog.Find(SettingIds.Hags)!;

        Assert.False(hags.HasTarget);
        Assert.Equal(SettingRisk.Tradeoff, hags.Risk);

        SettingsAudit audit = SettingsCatalog.Evaluate(new[]
        {
            SettingReading.Of(SettingIds.Hags, true),
        });

        SettingEvaluation evaluated = Single(audit, SettingIds.Hags);

        Assert.Equal(SettingVerdict.Informational, evaluated.Verdict);
        Assert.Equal(HealthState.Unknown, evaluated.Health);
        Assert.False(evaluated.IsDeviation);
    }

    [Fact]
    public void Los_ajustes_de_riesgo_de_seguridad_quedan_fuera_de_los_perfiles()
    {
        // HVCI y las mitigaciones de CPU se muestran y se explican, pero jamas entran en un
        // perfil que se aplica de un clic.
        SettingDefinition[] sensibles =
        {
            SettingsCatalog.Find(SettingIds.MemoryIntegrity)!,
            SettingsCatalog.Find(SettingIds.CpuMitigations)!,
        };

        Assert.All(sensibles, d =>
        {
            Assert.True(d.ExcludedFromProfiles);
            Assert.False(d.HasTarget);
            Assert.Null(d.ActionId);
        });
    }

    [Fact]
    public void Una_lectura_que_coincide_con_el_objetivo_sale_verde()
    {
        SettingsAudit audit = SettingsCatalog.Evaluate(new[]
        {
            SettingReading.Of(SettingIds.FastStartup, false),
        });

        SettingEvaluation evaluated = Single(audit, SettingIds.FastStartup);

        Assert.Equal(SettingVerdict.Match, evaluated.Verdict);
        Assert.Equal(HealthState.Ok, evaluated.Health);
    }

    [Fact]
    public void Difiere_en_algo_de_impacto_alto_es_rojo_y_no_ambar()
    {
        SettingsAudit audit = SettingsCatalog.Evaluate(new[]
        {
            SettingReading.Of(SettingIds.FastStartup, true),
        });

        SettingEvaluation evaluated = Single(audit, SettingIds.FastStartup);

        Assert.Equal(SettingVerdict.DiffersCritical, evaluated.Verdict);
        Assert.Equal(HealthState.Critical, evaluated.Health);
        Assert.True(evaluated.IsDeviation);
    }

    [Fact]
    public void Difiere_en_algo_de_impacto_menor_es_ambar()
    {
        SettingDefinition transparencia = SettingsCatalog.Find(SettingIds.Transparency)!;
        Assert.Equal(SettingImpact.Low, transparencia.Impact);

        SettingsAudit audit = SettingsCatalog.Evaluate(new[]
        {
            SettingReading.Of(SettingIds.Transparency, true),
        });

        Assert.Equal(SettingVerdict.Differs, Single(audit, SettingIds.Transparency).Verdict);
    }

    [Fact]
    public void No_haber_podido_leer_no_es_estar_mal()
    {
        // Sin privilegios hay claves que no se leen. Pintar eso en rojo llena la pantalla de
        // falsos positivos y consigue que dejes de mirar los verdaderos.
        SettingsAudit audit = SettingsCatalog.Evaluate(Array.Empty<SettingReading>());

        SettingEvaluation evaluated = Single(audit, SettingIds.FastStartup);

        Assert.Equal(SettingVerdict.Unknown, evaluated.Verdict);
        Assert.Equal(HealthState.Unknown, evaluated.Health);
        Assert.False(evaluated.IsDeviation);
        Assert.Equal(0, audit.CriticalCount);
    }

    [Fact]
    public void Una_lectura_marcada_como_ilegible_conserva_el_motivo()
    {
        SettingsAudit audit = SettingsCatalog.Evaluate(new[]
        {
            SettingReading.Missing(SettingIds.TamperProtection, "clave protegida por ACL"),
        });

        SettingEvaluation evaluated = Single(audit, SettingIds.TamperProtection);

        Assert.Equal("clave protegida por ACL", evaluated.Unavailable);
        Assert.Equal("clave protegida por ACL", evaluated.CurrentText);
    }

    [Fact]
    public void Varios_valores_aceptables_valen_todos()
    {
        // SystemResponsiveness admite 10 en general y 0 en un rig dedicado.
        foreach (int valor in new[] { 0, 10 })
        {
            SettingsAudit audit = SettingsCatalog.Evaluate(new[]
            {
                SettingReading.Of(SettingIds.MmcssResponsiveness, valor),
            });

            Assert.Equal(SettingVerdict.Match, Single(audit, SettingIds.MmcssResponsiveness).Verdict);
        }

        SettingsAudit mal = SettingsCatalog.Evaluate(new[]
        {
            SettingReading.Of(SettingIds.MmcssResponsiveness, 20),
        });

        Assert.Equal(SettingVerdict.DiffersCritical, Single(mal, SettingIds.MmcssResponsiveness).Verdict);
    }

    [Fact]
    public void El_valor_sin_limite_de_network_throttling_se_compara_sin_desbordar()
    {
        // 0xFFFFFFFF no cabe en un int con signo. Si la lectura se hace como entero y se
        // formatea como -1, la comparacion falla y el ajuste sale en rojo estando bien.
        SettingsAudit audit = SettingsCatalog.Evaluate(new[]
        {
            SettingReading.Of(SettingIds.NetworkThrottling, unchecked((int)uint.MaxValue).ToString()),
        });

        Assert.Equal(SettingVerdict.DiffersCritical, Single(audit, SettingIds.NetworkThrottling).Verdict);

        SettingsAudit bien = SettingsCatalog.Evaluate(new[]
        {
            SettingReading.Of(SettingIds.NetworkThrottling, uint.MaxValue.ToString()),
        });

        Assert.Equal(SettingVerdict.Match, Single(bien, SettingIds.NetworkThrottling).Verdict);
    }

    [Fact]
    public void El_resumen_por_grupo_cuenta_lo_que_hay_que_mirar()
    {
        SettingsAudit audit = SettingsCatalog.Evaluate(new[]
        {
            SettingReading.Of(SettingIds.FastStartup, true),        // rojo, impacto alto
            SettingReading.Of(SettingIds.ScreenSaver, false),       // verde
            SettingReading.Of(SettingIds.UsbSelectiveSuspend, true) // rojo, otro grupo
        });

        SettingGroupSummary arranque = audit.Groups.Single(g => g.Group == SettingGroup.Startup);

        Assert.Equal(1, arranque.CriticalCount);
        Assert.Equal(HealthState.Critical, arranque.Health);

        Assert.Equal(2, audit.CriticalCount);
        Assert.Equal(HealthState.Critical, audit.Health);
        Assert.Equal(2, audit.Deviations.Count());
    }

    [Fact]
    public void Un_grupo_que_no_se_ha_podido_leer_no_dice_que_este_bien()
    {
        SettingsAudit audit = SettingsCatalog.Evaluate(Array.Empty<SettingReading>());
        SettingGroupSummary arranque = audit.Groups.Single(g => g.Group == SettingGroup.Startup);

        Assert.Equal(HealthState.Unknown, arranque.Health);
    }

    [Fact]
    public void El_filtro_de_desviaciones_deja_solo_lo_accionable()
    {
        SettingsAudit audit = SettingsCatalog.Evaluate(new[]
        {
            SettingReading.Of(SettingIds.FastStartup, true),
            SettingReading.Of(SettingIds.ScreenSaver, false),
            SettingReading.Of(SettingIds.Hags, true),
        });

        Assert.All(audit.Deviations, d => Assert.True(d.IsDeviation));
        Assert.Single(audit.Deviations);
        Assert.Equal(SettingIds.FastStartup, audit.Deviations.Single().Definition.Id);
    }

    [Fact]
    public void Delivery_Optimization_sin_pares_vale_en_cualquiera_de_sus_tres_modos()
    {
        // Salio de una auditoria en una maquina real: en modo 99 la fila se pintaba en rojo
        // mientras su propio texto decia «sin descarga entre pares». Lo que el catalogo
        // quiere evitar es que la maquina sirva actualizaciones a la red del venue, y eso
        // lo cumplen el 0 (solo HTTP), el 99 (descarga simple) y el 100 (se salta DO).
        foreach (string modo in new[] { "0", "99", "100" })
        {
            SettingsAudit audit = SettingsCatalog.Evaluate(new[]
            {
                SettingReading.Of(SettingIds.DeliveryOptimization, modo),
            });

            Assert.Equal(SettingVerdict.Match, Single(audit, SettingIds.DeliveryOptimization).Verdict);
        }

        // Los que si reparten siguen en rojo: son el motivo de que la fila exista.
        foreach (string modo in new[] { "1", "2", "3" })
        {
            SettingsAudit audit = SettingsCatalog.Evaluate(new[]
            {
                SettingReading.Of(SettingIds.DeliveryOptimization, modo),
            });

            Assert.Equal(
                SettingVerdict.DiffersCritical,
                Single(audit, SettingIds.DeliveryOptimization).Verdict);
        }
    }

    private static SettingEvaluation Single(SettingsAudit audit, string id) =>
        audit.All.Single(s => s.Definition.Id == id);
}
