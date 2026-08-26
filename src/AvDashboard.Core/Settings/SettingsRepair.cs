namespace AvDashboard.Core.Settings;

/// <summary>
/// Qué se puede arreglar de una auditoría sin preguntar fila por fila.
///
/// Vive en Core y no en el ViewModel porque es la decisión más peligrosa de la aplicación:
/// un botón que escribe varios ajustes de golpe. Lo que ese botón se lleva por delante tiene
/// que estar escrito en un sitio, ser el mismo en las dos interfaces y estar cubierto por
/// pruebas. Escrito en la vista, bastaría con que alguien añadiese un `where` para que un
/// ajuste de seguridad entrase en un barrido sin que nadie se enterase.
/// </summary>
public static class SettingsRepair
{
    /// <summary>
    /// Reparto de la auditoría en lo que se arregla y lo que no, con el motivo.
    ///
    /// El motivo importa tanto como la lista: un ajuste que se queda fuera y no dice por qué
    /// parece un olvido, y a las 19:40 nadie va a ir a buscar si lo es.
    /// </summary>
    public static RepairPlan Plan(SettingsAudit audit, bool includeWarnings = false)
    {
        ArgumentNullException.ThrowIfNull(audit);

        List<SettingEvaluation> fixable = new();
        List<SettingEvaluation> notWritable = new();
        List<SettingEvaluation> byRisk = new();

        foreach (SettingEvaluation evaluation in audit.All)
        {
            bool inScope = evaluation.Verdict == SettingVerdict.DiffersCritical
                           || (includeWarnings && evaluation.Verdict == SettingVerdict.Differs);

            if (!inScope || !evaluation.Definition.HasTarget)
            {
                continue;
            }

            // El riesgo se mira antes que la escribibilidad a propósito. Un ajuste de
            // seguridad escribible es justo el que hay que enseñar apartado y con su
            // explicación, no el que hay que colar en un barrido porque «se puede».
            if (evaluation.Definition.Risk != SettingRisk.None)
            {
                byRisk.Add(evaluation);
                continue;
            }

            if (!evaluation.Writable)
            {
                notWritable.Add(evaluation);
                continue;
            }

            fixable.Add(evaluation);
        }

        return new RepairPlan
        {
            Fixable = fixable,
            SkippedNotWritable = notWritable,
            SkippedByRisk = byRisk,
        };
    }
}

/// <summary>Lo que un arreglo en bloque tocaría, y lo que dejaría como está.</summary>
public sealed record RepairPlan
{
    public IReadOnlyList<SettingEvaluation> Fixable { get; init; } = Array.Empty<SettingEvaluation>();

    /// <summary>Desviado y con objetivo, pero el agente no puede escribirlo desde aquí.</summary>
    public IReadOnlyList<SettingEvaluation> SkippedNotWritable { get; init; } = Array.Empty<SettingEvaluation>();

    /// <summary>
    /// Desviado y escribible, pero con contrapartida: seguridad o compromiso medible.
    ///
    /// Nunca entra en un cambio automático. Los de seguridad reducen la protección de la
    /// máquina de verdad, y los de compromiso —HAGS es el caso claro— dependen del equipo:
    /// imponer un objetivo sin medirlo es cambiar un problema por otro.
    /// </summary>
    public IReadOnlyList<SettingEvaluation> SkippedByRisk { get; init; } = Array.Empty<SettingEvaluation>();

    public bool HasWork => Fixable.Count > 0;

    /// <summary>Una línea por ajuste, tal y como se lee en la confirmación.</summary>
    public IEnumerable<string> Describe() => Fixable.Select(f =>
        $"{f.Definition.Name}: {f.CurrentText} → {f.Definition.TargetDescription}");
}
