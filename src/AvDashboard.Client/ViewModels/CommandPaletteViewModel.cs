using System.Collections.ObjectModel;
using AvDashboard.Core.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AvDashboard.Client.ViewModels;

/// <summary>
/// Paleta de comandos (F-15). Ctrl+K, escribes `scan`, `latency`, `setip 1`, Enter.
///
/// Los nombres son exactamente los de av-light-tools. Es lo que hace que no vuelvas a
/// PowerShell por costumbre: la velocidad de la terminal dentro de una app nativa.
/// </summary>
public sealed partial class CommandPaletteViewModel : ObservableObject
{
    public CommandPaletteViewModel()
    {
        Results = new ObservableCollection<CommandDescriptor>(CommandCatalog.All);
        Selected = Results.FirstOrDefault();
    }

    public ObservableCollection<CommandDescriptor> Results { get; }

    [ObservableProperty]
    private string _query = string.Empty;

    [ObservableProperty]
    private CommandDescriptor? _selected;

    partial void OnQueryChanged(string value)
    {
        Results.Clear();
        foreach (CommandDescriptor command in CommandCatalog.Search(value))
        {
            Results.Add(command);
        }

        Selected = Results.FirstOrDefault();
    }

    public void Reset()
    {
        Query = string.Empty;
        Selected = Results.FirstOrDefault();
    }

    public void MoveSelection(int delta)
    {
        if (Results.Count == 0)
        {
            return;
        }

        int index = Selected is null ? 0 : Results.IndexOf(Selected) + delta;
        Selected = Results[Math.Clamp(index, 0, Results.Count - 1)];
    }
}
