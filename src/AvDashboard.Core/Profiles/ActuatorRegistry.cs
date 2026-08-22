using AvDashboard.Core.Interfaces;

namespace AvDashboard.Core.Profiles;

/// <summary>
/// Registro de actuadores disponibles en un nodo.
///
/// Es la lista blanca del §7 de la arquitectura hecha código: el motor de perfiles solo
/// puede ejecutar lo que esté aquí, y aquí solo entra lo que alguien haya compilado. No
/// hay forma de que un perfil guardado en la base de datos invoque nada que no exista.
/// </summary>
public sealed class ActuatorRegistry : IActuatorRegistry
{
    private readonly Dictionary<string, IProfileStepExecutor> _executors = new(StringComparer.Ordinal);

    public IEnumerable<string> KnownActionIds => _executors.Keys;

    /// <summary>
    /// Registra un actuador. Rechaza los identificadores que no estén en
    /// <see cref="ActionIds.All"/>: una acción sin identificador declarado es una acción
    /// que nadie ha revisado.
    /// </summary>
    public ActuatorRegistry Register(IProfileStepExecutor executor)
    {
        ArgumentNullException.ThrowIfNull(executor);

        if (!ActionIds.All.Contains(executor.ActionId))
        {
            throw new ArgumentException(
                $"«{executor.ActionId}» no está declarado en ActionIds. " +
                "Los actuadores no se registran con identificadores improvisados.",
                nameof(executor));
        }

        if (!_executors.TryAdd(executor.ActionId, executor))
        {
            throw new ArgumentException(
                $"Ya hay un actuador registrado para «{executor.ActionId}».", nameof(executor));
        }

        return this;
    }

    /// <summary>Envuelve un actuador tipado y lo registra.</summary>
    public ActuatorRegistry Register<TRequest>(
        IActuator<TRequest> actuator,
        Func<CancellationToken, Task<TRequest>> captureCurrent) =>
        Register(new TypedStepExecutor<TRequest>(actuator, captureCurrent));

    public bool TryResolve(string actionId, out IProfileStepExecutor executor)
    {
        if (_executors.TryGetValue(actionId, out IProfileStepExecutor? found))
        {
            executor = found;
            return true;
        }

        executor = null!;
        return false;
    }

    public IProfileStepExecutor? Get(string actionId) =>
        _executors.TryGetValue(actionId, out IProfileStepExecutor? found) ? found : null;
}

/// <summary>
/// Adaptador entre un <see cref="IActuator{TRequest}"/> tipado y el motor de perfiles,
/// que solo habla de JSON.
///
/// Existe para que un actuador concreto —el que toca el registro de Windows— no tenga que
/// saber nada de serialización ni del formato en que se guardan los perfiles.
/// </summary>
public sealed class TypedStepExecutor<TRequest> : IProfileStepExecutor
{
    private readonly IActuator<TRequest> _actuator;
    private readonly Func<CancellationToken, Task<TRequest>> _captureCurrent;

    public TypedStepExecutor(
        IActuator<TRequest> actuator,
        Func<CancellationToken, Task<TRequest>> captureCurrent)
    {
        _actuator = actuator;
        _captureCurrent = captureCurrent;
    }

    public string ActionId => _actuator.ActionId;

    public bool IsDisruptive => _actuator.IsDisruptive;

    public Task<ActionDiff> PreviewAsync(string parametersJson, CancellationToken cancellationToken = default) =>
        _actuator.PreviewAsync(Parse(parametersJson), cancellationToken);

    public Task<ActuationResult> ApplyAsync(string parametersJson, CancellationToken cancellationToken = default) =>
        _actuator.ApplyAsync(Parse(parametersJson), cancellationToken);

    public async Task<string> CaptureCurrentAsync(CancellationToken cancellationToken = default)
    {
        TRequest current = await _captureCurrent(cancellationToken).ConfigureAwait(false);
        return ActionParameters.Serialize(current);
    }

    private TRequest Parse(string parametersJson) =>
        ActionParameters.Deserialize<TRequest>(parametersJson, ActionId);
}
