using AvDashboard.Core.Interfaces;
using AvDashboard.Core.Profiles;
using Xunit;

namespace AvDashboard.Core.Tests;

/// <summary>
/// El motor de perfiles es el código que puede dejar una máquina inutilizable en un
/// show. No se valida a mano.
/// </summary>
public class ProfileEngineTests
{
    private static ProfileDefinition Profile(params ProfileStep[] steps) => new()
    {
        Id = "evento-rig",
        Name = "Evento — Rig Portátil",
        Steps = steps,
    };

    private static ProfileStep Step(string actionId, int order = 0, bool continueOnError = false) => new()
    {
        ActionId = actionId,
        ParametersJson = "{}",
        Order = order,
        ContinueOnError = continueOnError,
    };

    [Fact]
    public async Task Toma_snapshot_antes_de_tocar_nada()
    {
        FakeRegistry registry = new();
        registry.Add("system.powerplan", "Equilibrado", "Ultimate Performance");
        FakeSnapshotStore snapshots = new(() => registry.ApplyCount);

        ProfileEngine engine = new(registry, snapshots);

        ProfileRun run = await engine.ApplyAsync(Profile(Step("system.powerplan")), "nodo-1");

        Assert.Equal("snapshot-1", run.SnapshotId);
        Assert.Equal(ProfileRunStatus.Completed, run.Status);
        Assert.Equal(0, snapshots.AppliesBeforeCapture);
        Assert.Equal(1, registry.ApplyCount);
    }

    [Fact]
    public async Task Si_el_rollback_tambien_falla_se_dice_exactamente_que_ha_quedado_a_medias()
    {
        // Es el peor escenario posible y el unico en el que la maquina queda en un estado
        // que no es ni el de antes ni el de despues. Tiene que gritarlo, no devolver false.
        FakeRegistry registry = new();
        registry.Add("network.profile", "DHCP", "Dante 172.31", failsOnApply: true);
        FakeSnapshotStore snapshots = new(restoreSucceeds: false);

        ProfileEngine engine = new(registry, snapshots);

        ProfileRun run = await engine.ApplyAsync(Profile(Step("network.profile")), "nodo-1");

        Assert.Equal(ProfileRunStatus.RollbackFailed, run.Status);
        Assert.Contains("M1", run.RollbackMessage);
    }

    [Fact]
    public async Task Un_paso_que_ya_esta_en_el_valor_deseado_se_salta()
    {
        FakeRegistry registry = new();
        registry.Add("system.powerplan", "Ultimate Performance", "Ultimate Performance");

        ProfileEngine engine = new(registry, new FakeSnapshotStore());

        ProfileRun run = await engine.ApplyAsync(Profile(Step("system.powerplan")), "nodo-1");

        Assert.True(run.Steps[0].Skipped);
        Assert.Equal(0, registry.ApplyCount);
    }

    [Fact]
    public async Task Un_actuador_ausente_se_salta_en_vez_de_fallar()
    {
        // En multi-nodo es normal: el rig portátil no tiene DeckLink.
        ProfileEngine engine = new(new FakeRegistry(), new FakeSnapshotStore());

        ProfileRun run = await engine.ApplyAsync(Profile(Step("video.decklink")), "rig-portatil");

        Assert.True(run.Steps[0].Skipped);
        Assert.Equal(ProfileRunStatus.CompletedWithWarnings, run.Status);
    }

    [Fact]
    public async Task Un_fallo_obligatorio_dispara_el_rollback()
    {
        FakeSnapshotStore snapshots = new();
        FakeRegistry registry = new();
        registry.Add("system.powerplan", "Equilibrado", "Ultimate Performance");
        registry.Add("network.profile", "DHCP", "Dante 172.31", failsOnApply: true);

        ProfileEngine engine = new(registry, snapshots);

        ProfileRun run = await engine.ApplyAsync(
            Profile(Step("system.powerplan", order: 0), Step("network.profile", order: 1)),
            "nodo-1");

        Assert.Equal(ProfileRunStatus.RolledBack, run.Status);
        Assert.Equal("snapshot-1", snapshots.RestoredId);
    }

    [Fact]
    public async Task Un_paso_marcado_continueOnError_no_aborta_el_perfil()
    {
        FakeRegistry registry = new();
        registry.Add("video.edid", "A", "B", failsOnApply: true);
        registry.Add("system.powerplan", "Equilibrado", "Ultimate Performance");

        ProfileEngine engine = new(registry, new FakeSnapshotStore());

        ProfileRun run = await engine.ApplyAsync(
            Profile(Step("video.edid", order: 0, continueOnError: true), Step("system.powerplan", order: 1)),
            "nodo-1");

        Assert.Equal(ProfileRunStatus.CompletedWithWarnings, run.Status);
        Assert.Equal(2, run.Steps.Count);
        Assert.True(run.Steps[1].Success);
    }

    [Fact]
    public async Task Con_show_activo_no_se_aplica_sin_confirmacion_explicita()
    {
        FakeRegistry registry = new();
        registry.Add("system.powerplan", "Equilibrado", "Ultimate Performance");

        ProfileEngine engine = new(registry, new FakeSnapshotStore(), new FakeShowGuard("vMix está grabando"));

        ShowInProgressException error = await Assert.ThrowsAsync<ShowInProgressException>(
            () => engine.ApplyAsync(Profile(Step("system.powerplan")), "nodo-1"));

        Assert.Contains("vMix", error.Message);
    }

    [Fact]
    public async Task Con_confirmacion_explicita_se_aplica_y_queda_registrado()
    {
        FakeRegistry registry = new();
        registry.Add("system.powerplan", "Equilibrado", "Ultimate Performance");

        ProfileEngine engine = new(registry, new FakeSnapshotStore(), new FakeShowGuard("Watchout en play"));

        ProfileRun run = await engine.ApplyAsync(
            Profile(Step("system.powerplan")), "nodo-1",
            new ProfileApplyOptions { ConfirmedDuringShow = true });

        Assert.Equal(ProfileRunStatus.Completed, run.Status);
        Assert.True(run.ForcedDuringShow);
    }

    [Fact]
    public async Task El_preview_no_aplica_nada_y_cuenta_los_cambios_reales()
    {
        FakeRegistry registry = new();
        registry.Add("system.powerplan", "Equilibrado", "Ultimate Performance");
        registry.Add("system.fast-startup", "off", "off");

        ProfileEngine engine = new(registry, new FakeSnapshotStore());

        ProfilePreview preview = await engine.PreviewAsync(
            Profile(Step("system.powerplan", 0), Step("system.fast-startup", 1), Step("no.existe", 2)));

        Assert.Equal(0, registry.ApplyCount);
        Assert.Equal(2, preview.Diffs.Count);
        Assert.Equal(1, preview.ChangeCount);
        Assert.Equal(new[] { "no.existe" }, preview.UnresolvedActions);
    }

    [Fact]
    public async Task El_preview_marca_que_pasos_pueden_cortar_una_salida()
    {
        // No basta con decir «se van a cambiar tres cosas»: hay que decir cuales se notan.
        FakeRegistry registry = new();
        registry.Add("system.powerplan", "Equilibrado", "Ultimate Performance");
        registry.Add("network.profile", "DHCP", "Dante 172.31", disruptive: true);

        ProfileEngine engine = new(registry, new FakeSnapshotStore());

        ProfilePreview preview = await engine.PreviewAsync(
            Profile(Step("system.powerplan", 0), Step("network.profile", 1)));

        Assert.Equal(new[] { "network.profile" }, preview.DisruptiveActions);
    }

    [Fact]
    public async Task Un_paso_disruptivo_que_no_cambia_nada_no_se_marca()
    {
        // Aplicar el perfil de red que ya esta puesto no corta nada.
        FakeRegistry registry = new();
        registry.Add("network.profile", "Dante 172.31", "Dante 172.31", disruptive: true);

        ProfileEngine engine = new(registry, new FakeSnapshotStore());

        ProfilePreview preview = await engine.PreviewAsync(Profile(Step("network.profile")));

        Assert.Empty(preview.DisruptiveActions);
        Assert.Equal(0, preview.ChangeCount);
    }

    [Fact]
    public async Task La_confirmacion_extra_solo_hace_falta_si_hay_show_y_algo_que_se_note()
    {
        FakeRegistry sinRuido = new();
        sinRuido.Add("system.powerplan", "Equilibrado", "Ultimate Performance");

        ProfileEngine conShow = new(sinRuido, new FakeSnapshotStore(), new FakeShowGuard("vMix grabando"));
        ProfilePreview inocuo = await conShow.PreviewAsync(Profile(Step("system.powerplan")));

        Assert.True(inocuo.ShowActivity.IsShowActive);
        Assert.False(inocuo.NeedsExtraConfirmation);

        FakeRegistry conRuido = new();
        conRuido.Add("network.profile", "DHCP", "Dante 172.31", disruptive: true);

        ProfileEngine peligroso = new(conRuido, new FakeSnapshotStore(), new FakeShowGuard("vMix grabando"));
        ProfilePreview corta = await peligroso.PreviewAsync(Profile(Step("network.profile")));

        Assert.True(corta.NeedsExtraConfirmation);
    }

    // --- Dobles de prueba ---

    private sealed class FakeRegistry : IActuatorRegistry
    {
        private readonly Dictionary<string, FakeExecutor> _executors = new(StringComparer.Ordinal);

        public int ApplyCount { get; private set; }

        public IEnumerable<string> KnownActionIds => _executors.Keys;

        public void Add(
            string actionId,
            string current,
            string desired,
            bool failsOnApply = false,
            bool disruptive = false) =>
            _executors[actionId] = new FakeExecutor(
                actionId, current, desired, failsOnApply, disruptive, () => ApplyCount++);

        public bool TryResolve(string actionId, out IProfileStepExecutor executor)
        {
            if (_executors.TryGetValue(actionId, out FakeExecutor? found))
            {
                executor = found;
                return true;
            }

            executor = null!;
            return false;
        }
    }

    private sealed class FakeExecutor : IProfileStepExecutor
    {
        private readonly string _current;
        private readonly string _desired;
        private readonly bool _fails;
        private readonly Action _onApply;

        public FakeExecutor(
            string actionId,
            string current,
            string desired,
            bool fails,
            bool disruptive,
            Action onApply)
        {
            ActionId = actionId;
            _current = current;
            _desired = desired;
            _fails = fails;
            IsDisruptive = disruptive;
            _onApply = onApply;
        }

        public string ActionId { get; }
        public bool IsDisruptive { get; }

        public Task<ActionDiff> PreviewAsync(string parametersJson, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ActionDiff
            {
                ActionId = ActionId,
                Description = $"{ActionId}: {_current} -> {_desired}",
                CurrentValue = _current,
                DesiredValue = _desired,
            });

        public Task<ActuationResult> ApplyAsync(string parametersJson, CancellationToken cancellationToken = default)
        {
            _onApply();

            return Task.FromResult(_fails
                ? ActuationResult.Failed(ActionId, "El actuador ha fallado")
                : ActuationResult.Ok(ActionId));
        }

        public Task<string> CaptureCurrentAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult($"{{\"value\":\"{_current}\"}}");
    }

    private sealed class FakeSnapshotStore : ISnapshotStore
    {
        private readonly Func<int>? _applyCount;
        private readonly bool _restoreSucceeds;

        public FakeSnapshotStore(Func<int>? applyCount = null, bool restoreSucceeds = true)
        {
            _applyCount = applyCount;
            _restoreSucceeds = restoreSucceeds;
        }

        public string? RestoredId { get; private set; }

        /// <summary>Cuantos actuadores se habian aplicado ya cuando se tomo el snapshot.</summary>
        public int AppliesBeforeCapture { get; private set; } = -1;

        public Task<string> CaptureAsync(string nodeId, string trigger, CancellationToken cancellationToken = default)
        {
            AppliesBeforeCapture = _applyCount?.Invoke() ?? 0;
            return Task.FromResult("snapshot-1");
        }

        public Task<RestoreResult> RestoreAsync(string snapshotId, CancellationToken cancellationToken = default)
        {
            RestoredId = snapshotId;

            return Task.FromResult(_restoreSucceeds
                ? RestoreResult.Ok
                : new RestoreResult { Success = false, Message = "El módulo M1 no ha podido restaurar." });
        }
    }

    private sealed class FakeShowGuard : IShowGuard
    {
        private readonly string[] _reasons;

        public FakeShowGuard(params string[] reasons) => _reasons = reasons;

        public Task<ShowActivity> GetActivityAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ShowActivity { Reasons = _reasons });
    }
}
