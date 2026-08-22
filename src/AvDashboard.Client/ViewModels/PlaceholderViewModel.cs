using AvDashboard.Core.Models;

namespace AvDashboard.Client.ViewModels;

/// <summary>
/// Seccion declarada en el diseno que todavia no existe.
///
/// Se ensena con su nombre, la fase en la que llega y que hara. Es deliberado: una
/// aplicacion que esconde lo que no ha hecho obliga a recordar el roadmap de memoria.
/// </summary>
public sealed class PlaceholderViewModel : PageViewModel
{
    public PlaceholderViewModel(NodeManager nodes, IUiShell shell, string title, string glyph, string phase, string what)
        : base(nodes, shell)
    {
        Title = title;
        Glyph = glyph;
        Phase = phase;
        What = what;
        Health = HealthState.Unknown;
    }

    public override string Title { get; }
    public override string Glyph { get; }

    public string Phase { get; }
    public string What { get; }

    protected override Task LoadAsync() => Task.CompletedTask;
}
