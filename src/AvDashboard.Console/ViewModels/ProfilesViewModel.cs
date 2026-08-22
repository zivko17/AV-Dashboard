using System.Collections.ObjectModel;
using AvDashboard.Console.Services;
using AvDashboard.Core.Contracts;
using AvDashboard.Core.Models;
using AvDashboard.Core.Profiles;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AvDashboard.Console.ViewModels;

/// <summary>
/// Perfiles de escenario (M10).
///
/// Aplicarlos llega con los actuadores de la Fase 2. Lo que ya funciona —y es lo que
/// resuelve el caso de llegar a un rig nuevo— es verlos, llevártelos a fichero y traerlos
/// de vuelta. El botón de aplicar se queda visible y deshabilitado a propósito: enseñar lo
/// que va a haber es mejor que esconderlo.
/// </summary>
public sealed partial class ProfilesViewModel : PageViewModel
{
    public ProfilesViewModel(NodeManager nodes) : base(nodes)
    {
    }

    public override string Title => "Perfiles";
    public override string Glyph => "\uE8FD";

    public ObservableCollection<ProfileRowViewModel> Profiles { get; } = new();

    [ObservableProperty]
    private ProfileRowViewModel? _selected;

    [ObservableProperty]
    private string _status =
        "Aplicar un perfil llega con los actuadores. Exportar e importar ya funcionan.";

    protected override async Task LoadAsync()
    {
        ApiResult<ProfilesResponse> result = await Nodes.Api().GetProfilesAsync().ConfigureAwait(true);

        if (!Apply(result, out ProfilesResponse response))
        {
            return;
        }

        string? selectedId = Selected?.Id;
        Selected = null;
        Profiles.Clear();

        foreach (ProfileDefinition profile in response.BuiltIn)
        {
            Add(profile, builtIn: true, selectedId);
        }

        foreach (ProfileDefinition profile in response.Stored)
        {
            Add(profile, builtIn: false, selectedId);
        }

        Selected ??= Profiles.FirstOrDefault();
        Health = HealthState.Ok;
    }

    private void Add(ProfileDefinition profile, bool builtIn, string? selectedId)
    {
        ProfileRowViewModel row = new(profile, builtIn);
        Profiles.Add(row);

        if (row.Id == selectedId)
        {
            Selected = row;
        }
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        if (Selected is null)
        {
            return;
        }

        ApiResult<string> result = await Nodes.Api().ExportProfileAsync(Selected.Id).ConfigureAwait(true);

        if (!result.Success)
        {
            Status = result.Error ?? "No se ha podido exportar el perfil.";
            return;
        }

        Microsoft.Win32.SaveFileDialog dialog = new()
        {
            FileName = $"{Sanitize(Selected.Name)}.avprofile.json",
            DefaultExt = ".json",
            Filter = "Perfil de AV Dashboard (*.json)|*.json|Todos los archivos (*.*)|*.*",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            await File.WriteAllTextAsync(dialog.FileName, result.Value!).ConfigureAwait(true);
            Status = $"Perfil escrito en {dialog.FileName}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = ex.Message;
        }
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        Microsoft.Win32.OpenFileDialog dialog = new()
        {
            Filter = "Perfil de AV Dashboard (*.json)|*.json|Todos los archivos (*.*)|*.*",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        string json;

        try
        {
            json = await File.ReadAllTextAsync(dialog.FileName).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = ex.Message;
            return;
        }

        ApiResult<ProfileImportResponse> result =
            await Nodes.Api().ImportProfileAsync(json).ConfigureAwait(true);

        if (!result.Success)
        {
            Status = result.Error ?? "No se ha podido importar el perfil.";
            return;
        }

        ProfileImportResponse imported = result.Value!;

        Status = imported.UnknownActions.Count == 0
            ? $"Importado «{imported.ProfileName}»."
            : $"Importado «{imported.ProfileName}». Este nodo no conoce " +
              $"{imported.UnknownActions.Count} de sus actuadores, así que esos pasos se saltarán: " +
              string.Join(", ", imported.UnknownActions);

        await RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>Un nombre de perfil puede llevar cualquier cosa; un nombre de fichero no.</summary>
    private static string Sanitize(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string cleaned = new(name.Select(c => invalid.Contains(c) ? '-' : c).ToArray());

        return cleaned.Trim('-', ' ') is { Length: > 0 } trimmed ? trimmed : "perfil";
    }
}

/// <summary>Un perfil en la lista.</summary>
public sealed class ProfileRowViewModel
{
    public ProfileRowViewModel(ProfileDefinition profile, bool builtIn)
    {
        Profile = profile;
        BuiltIn = builtIn;
    }

    public ProfileDefinition Profile { get; }
    public bool BuiltIn { get; }

    public string Id => Profile.Id;
    public string Name => Profile.Name;
    public string? Description => Profile.Description;

    public string Origin => BuiltIn ? "de serie" : "guardado aquí";

    public string Steps => Profile.Steps.Count == 1 ? "1 paso" : $"{Profile.Steps.Count} pasos";

    /// <summary>
    /// Los pasos que tocarían algo que puede interrumpir una salida en directo. Es lo que
    /// mira el guardarraíl de show activo, y conviene verlo antes de aplicar nada.
    /// </summary>
    public string? Disruptive
    {
        get
        {
            int count = Profile.Steps.Count(s => ActionIds.IsDisruptive(s.ActionId));

            return count == 0
                ? null
                : count == 1
                    ? "1 paso puede interrumpir una salida en directo"
                    : $"{count} pasos pueden interrumpir una salida en directo";
        }
    }
}
