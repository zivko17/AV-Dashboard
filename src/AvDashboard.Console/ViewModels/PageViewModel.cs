using AvDashboard.Client;
using AvDashboard.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AvDashboard.Console.ViewModels;

/// <summary>
/// Base de las paginas del rail.
///
/// Todas comparten el mismo ciclo: se activan, cargan, se refrescan y se desactivan
/// soltando sus suscripciones de telemetria. Ese ultimo paso no es opcional: una vista
/// que se deja suscrita a los medidores de audio hace trabajar al agente para nada.
/// </summary>
public abstract partial class PageViewModel : ObservableObject
{
    protected PageViewModel(NodeManager nodes)
    {
        Nodes = nodes;
    }

    protected NodeManager Nodes { get; }

    public abstract string Title { get; }

    /// <summary>Glifo de Segoe Fluent Icons para el rail.</summary>
    public abstract string Glyph { get; }

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _error;

    [ObservableProperty]
    private HealthState _health = HealthState.Unknown;

    [ObservableProperty]
    private DateTimeOffset? _lastUpdated;

    /// <summary>Se llama al entrar en la pagina.</summary>
    public virtual Task ActivateAsync() => RefreshAsync();

    /// <summary>Se llama al salir. Aqui se sueltan las suscripciones al hub.</summary>
    public virtual Task DeactivateAsync() => Task.CompletedTask;

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        Error = null;

        try
        {
            await LoadAsync().ConfigureAwait(true);
            LastUpdated = DateTimeOffset.Now;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Error = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    protected abstract Task LoadAsync();

    /// <summary>
    /// Aplica el resultado de una llamada: valor o mensaje de error, sin repetir el
    /// mismo try/catch en cada vista.
    /// </summary>
    protected bool Apply<T>(ApiResult<T> result, out T value)
    {
        if (result.Success)
        {
            value = result.Value!;
            return true;
        }

        Error = result.Error;
        Health = HealthState.Unknown;
        value = default!;
        return false;
    }
}
