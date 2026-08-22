using AvDashboard.Core.Interfaces;
using AvDashboard.Core.Profiles;
using Xunit;

namespace AvDashboard.Core.Tests;

/// <summary>
/// El perfil inverso es el «Oficina» que devuelve la máquina después del evento. Se genera
/// del estado real del nodo, no del texto del diff.
/// </summary>
public class InverseProfileBuilderTests
{
    private static ProfileDefinition Profile(params (int Order, string ActionId)[] steps) => new()
    {
        Id = "evento",
        Name = "Evento",
        Steps = steps.Select(s => new ProfileStep
        {
            ActionId = s.ActionId,
            ParametersJson = "{}",
            Label = s.ActionId,
            Order = s.Order,
        }).ToList(),
    };

    [Fact]
    public async Task Captura_el_estado_actual_de_cada_paso()
    {
        ActuatorRegistry registry = new();
        registry.Register(new CapturingExecutor(ActionIds.PowerPlan, "{\"ultimate\":false}"));

        ProfileDefinition inverse = await InverseProfileBuilder.BuildAsync(
            Profile((0, ActionIds.PowerPlan)), registry);

        ProfileStep step = Assert.Single(inverse.Steps);
        Assert.Equal(ActionIds.PowerPlan, step.ActionId);
        Assert.Equal("{\"ultimate\":false}", step.ParametersJson);
    }

    [Fact]
    public async Task Deshace_en_orden_inverso()
    {
        // Si el perfil arranca un servicio y luego cambia la IP, deshacer en el mismo orden
        // te deja sin conectividad antes de haber parado el servicio.
        ActuatorRegistry registry = new();
        registry.Register(new CapturingExecutor(ActionIds.ServiceState, "{}"));
        registry.Register(new CapturingExecutor(ActionIds.NetworkProfile, "{}"));

        ProfileDefinition inverse = await InverseProfileBuilder.BuildAsync(
            Profile((0, ActionIds.ServiceState), (1, ActionIds.NetworkProfile)), registry);

        Assert.Equal(
            new[] { ActionIds.NetworkProfile, ActionIds.ServiceState },
            inverse.Steps.Select(s => s.ActionId));
        Assert.Equal(new[] { 0, 1 }, inverse.Steps.Select(s => s.Order));
    }

    [Fact]
    public async Task Todos_los_pasos_del_inverso_continuan_ante_un_fallo()
    {
        // Recuperar nueve ajustes de diez es mejor que quedarse en el estado del evento
        // porque uno no se pudo tocar.
        ActuatorRegistry registry = new();
        registry.Register(new CapturingExecutor(ActionIds.PowerPlan, "{}"));
        registry.Register(new CapturingExecutor(ActionIds.FastStartup, "{}"));

        ProfileDefinition inverse = await InverseProfileBuilder.BuildAsync(
            Profile((0, ActionIds.PowerPlan), (1, ActionIds.FastStartup)), registry);

        Assert.All(inverse.Steps, s => Assert.True(s.ContinueOnError));
    }

    [Fact]
    public async Task Un_actuador_ausente_no_genera_paso_inverso()
    {
        // Si el actuador no existe en este nodo, tampoco habrá cambiado nada que deshacer.
        ActuatorRegistry registry = new();
        registry.Register(new CapturingExecutor(ActionIds.PowerPlan, "{}"));

        ProfileDefinition inverse = await InverseProfileBuilder.BuildAsync(
            Profile((0, ActionIds.PowerPlan), (1, ActionIds.DisplayLayoutRestore)), registry);

        Assert.Single(inverse.Steps);
    }

    [Fact]
    public async Task Por_defecto_se_salta_lo_que_no_se_puede_capturar()
    {
        ActuatorRegistry registry = new();
        registry.Register(new CapturingExecutor(ActionIds.PowerPlan, "{}"));
        registry.Register(new CapturingExecutor(ActionIds.NetworkProfile, "{}", throwsOnCapture: true));

        ProfileDefinition inverse = await InverseProfileBuilder.BuildAsync(
            Profile((0, ActionIds.PowerPlan), (1, ActionIds.NetworkProfile)), registry);

        Assert.Single(inverse.Steps);
        Assert.Equal(ActionIds.PowerPlan, inverse.Steps[0].ActionId);
    }

    [Fact]
    public async Task Con_SkipUncapturable_en_false_se_niega_a_generar_un_inverso_incompleto()
    {
        // Para un rollback de verdad, un inverso a medias es peor que ninguno: promete una
        // vuelta atrás que no va a cumplir.
        ActuatorRegistry registry = new();
        registry.Register(new CapturingExecutor(ActionIds.NetworkProfile, "{}", throwsOnCapture: true));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            InverseProfileBuilder.BuildAsync(
                Profile((0, ActionIds.NetworkProfile)),
                registry,
                new InverseProfileOptions { SkipUncapturable = false }));
    }

    [Fact]
    public async Task El_inverso_se_identifica_a_si_mismo_como_derivado()
    {
        ActuatorRegistry registry = new();
        registry.Register(new CapturingExecutor(ActionIds.PowerPlan, "{}"));

        ProfileDefinition inverse = await InverseProfileBuilder.BuildAsync(
            Profile((0, ActionIds.PowerPlan)), registry);

        Assert.Equal("evento--inverso", inverse.Id);
        Assert.Contains("Evento", inverse.Description);
        Assert.StartsWith("Deshacer:", inverse.Steps[0].Label);
    }

    private sealed class CapturingExecutor : IProfileStepExecutor
    {
        private readonly string _current;
        private readonly bool _throwsOnCapture;

        public CapturingExecutor(string actionId, string current, bool throwsOnCapture = false)
        {
            ActionId = actionId;
            _current = current;
            _throwsOnCapture = throwsOnCapture;
        }

        public string ActionId { get; }
        public bool IsDisruptive => ActionIds.IsDisruptive(ActionId);

        public Task<ActionDiff> PreviewAsync(string parametersJson, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ActionDiff { ActionId = ActionId, Description = ActionId });

        public Task<ActuationResult> ApplyAsync(string parametersJson, CancellationToken cancellationToken = default) =>
            Task.FromResult(ActuationResult.Ok(ActionId));

        public Task<string> CaptureCurrentAsync(CancellationToken cancellationToken = default) =>
            _throwsOnCapture
                ? throw new InvalidOperationException("no se puede leer el estado actual")
                : Task.FromResult(_current);
    }
}
