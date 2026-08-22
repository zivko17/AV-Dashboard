using AvDashboard.Core.Interfaces;
using AvDashboard.Core.Profiles;
using Xunit;

namespace AvDashboard.Core.Tests;

/// <summary>
/// La biblioteca de perfiles y el registro de actuadores son la lista blanca del §7 hecha
/// código. Estas pruebas existen para que un perfil no pueda referenciar una acción que
/// nadie ha escrito, ni un actuador colarse con un identificador improvisado.
/// </summary>
public class ProfileLibraryTests
{
    [Fact]
    public void Todos_los_perfiles_de_partida_usan_acciones_declaradas()
    {
        foreach (ProfileDefinition profile in ProfileLibrary.All)
        {
            foreach (ProfileStep step in profile.Steps)
            {
                Assert.True(
                    ActionIds.All.Contains(step.ActionId),
                    $"«{profile.Name}» referencia la acción no declarada «{step.ActionId}».");
            }
        }
    }

    [Fact]
    public void Los_pasos_estan_ordenados_y_sin_repetir_posicion()
    {
        foreach (ProfileDefinition profile in ProfileLibrary.All)
        {
            int[] orders = profile.Steps.Select(s => s.Order).ToArray();
            Assert.Equal(orders.Distinct().Count(), orders.Length);
            Assert.Equal(orders.OrderBy(o => o), orders);
        }
    }

    [Fact]
    public void Los_parametros_de_cada_paso_son_json_legible()
    {
        // Un perfil con parámetros mal serializados no falla al guardarse: falla al
        // aplicarse, que es exactamente cuando no puede fallar.
        ProfileStep step = ProfileLibrary.EventoRigPortatil.Steps
            .First(s => s.ActionId == ActionIds.NetworkThrottling);

        NumericSettingParameters parameters =
            ActionParameters.Deserialize<NumericSettingParameters>(step.ParametersJson, step.ActionId);

        Assert.Equal(4294967295L, parameters.Value);
    }

    [Fact]
    public void Unos_parametros_corruptos_dicen_de_que_paso_vienen()
    {
        ProfileParameterException error = Assert.Throws<ProfileParameterException>(
            () => ActionParameters.Deserialize<NumericSettingParameters>("{esto no es json", "system.mmcss"));

        Assert.Equal("system.mmcss", error.ActionId);
        Assert.Contains("system.mmcss", error.Message);
    }

    [Fact]
    public void Lo_que_puede_cortar_una_salida_va_en_la_segunda_mitad_del_perfil()
    {
        // Si algo va a cortar la conexión con el agente remoto o el audio, que sea lo más
        // tarde posible: para entonces el resto del perfil ya está aplicado.
        IReadOnlyList<ProfileStep> steps = ProfileLibrary.EventoRigPortatil.Steps;
        int mitad = steps.Count / 2;

        List<ProfileStep> disruptivos = steps.Where(s => ActionIds.IsDisruptive(s.ActionId)).ToList();

        Assert.NotEmpty(disruptivos);
        Assert.All(disruptivos, s => Assert.True(
            s.Order >= mitad,
            $"«{s.Label}» es disruptivo y va en la posición {s.Order}, antes de la mitad ({mitad})."));
    }

    [Fact]
    public void El_perfil_de_diagnostico_no_cambia_nada()
    {
        Assert.Empty(ProfileLibrary.Diagnostico.Steps);
    }

    [Fact]
    public void Los_identificadores_de_perfil_son_unicos()
    {
        string[] ids = ProfileLibrary.All.Select(p => p.Id).ToArray();
        Assert.Equal(ids.Distinct().Count(), ids.Length);
    }
}

public class ActuatorRegistryTests
{
    [Fact]
    public void Un_actuador_con_identificador_no_declarado_se_rechaza()
    {
        ActuatorRegistry registry = new();

        ArgumentException error = Assert.Throws<ArgumentException>(
            () => registry.Register(new StubExecutor("system.lo-que-sea")));

        Assert.Contains("ActionIds", error.Message);
    }

    [Fact]
    public void No_se_puede_registrar_dos_veces_la_misma_accion()
    {
        ActuatorRegistry registry = new();
        registry.Register(new StubExecutor(ActionIds.PowerPlan));

        Assert.Throws<ArgumentException>(() => registry.Register(new StubExecutor(ActionIds.PowerPlan)));
    }

    [Fact]
    public void Resuelve_lo_registrado_y_solo_lo_registrado()
    {
        ActuatorRegistry registry = new();
        registry.Register(new StubExecutor(ActionIds.PowerPlan));

        Assert.True(registry.TryResolve(ActionIds.PowerPlan, out IProfileStepExecutor resolved));
        Assert.Equal(ActionIds.PowerPlan, resolved.ActionId);
        Assert.False(registry.TryResolve(ActionIds.NetworkProfile, out _));
        Assert.Equal(new[] { ActionIds.PowerPlan }, registry.KnownActionIds);
    }

    [Fact]
    public async Task El_adaptador_tipado_serializa_y_deserializa_los_parametros()
    {
        RecordingActuator actuator = new();
        ActuatorRegistry registry = new();

        registry.Register<PowerPlanParameters>(
            actuator,
            _ => Task.FromResult(new PowerPlanParameters { Ultimate = false }));

        Assert.True(registry.TryResolve(ActionIds.PowerPlan, out IProfileStepExecutor executor));

        await executor.ApplyAsync(ActionParameters.Serialize(new PowerPlanParameters { Ultimate = true }));

        Assert.True(actuator.Received?.Ultimate);

        string captured = await executor.CaptureCurrentAsync();
        Assert.Contains("false", captured, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class StubExecutor : IProfileStepExecutor
    {
        public StubExecutor(string actionId) => ActionId = actionId;

        public string ActionId { get; }
        public bool IsDisruptive => false;

        public Task<ActionDiff> PreviewAsync(string parametersJson, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ActionDiff { ActionId = ActionId, Description = ActionId });

        public Task<ActuationResult> ApplyAsync(string parametersJson, CancellationToken cancellationToken = default) =>
            Task.FromResult(ActuationResult.Ok(ActionId));

        public Task<string> CaptureCurrentAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult("{}");
    }

    private sealed class RecordingActuator : IActuator<PowerPlanParameters>
    {
        public PowerPlanParameters? Received { get; private set; }

        public string ModuleId => "M1";
        public string ActionId => ActionIds.PowerPlan;
        public bool RequiresElevation => true;
        public bool IsDisruptive => false;

        public Core.Models.ModuleDescriptor Describe() => new()
        {
            Id = ModuleId,
            Name = "Sistema",
            Availability = Core.Models.ModuleAvailability.Available,
        };

        public Task<ActionDiff> PreviewAsync(
            PowerPlanParameters request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ActionDiff
            {
                ActionId = ActionId,
                Description = "Plan de energía",
                CurrentValue = "Equilibrado",
                DesiredValue = request.Ultimate ? "Ultimate Performance" : "Equilibrado",
            });

        public Task<ActuationResult> ApplyAsync(
            PowerPlanParameters request, CancellationToken cancellationToken = default)
        {
            Received = request;
            return Task.FromResult(ActuationResult.Ok(ActionId));
        }
    }
}
