using AvDashboard.Client;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace AvDashboard.Shell.Services;

/// <summary>
/// Lo que los ViewModels necesitan de WinUI 3.
///
/// Las tres cosas son más ceremoniosas aquí que en WPF y las tres por el mismo motivo: los
/// selectores de fichero y los diálogos vienen del mundo de las aplicaciones empaquetadas,
/// donde había una sola ventana implícita. Sin paquete hay que decirles cuál es: al
/// selector, con el identificador nativo; al diálogo, con el XamlRoot.
/// </summary>
internal sealed class WinUiShell : IUiShell
{
    private readonly Window _window;

    public WinUiShell(Window window) => _window = window;

    public void Post(Action action)
    {
        // TryEnqueue devuelve false si la cola ya se está cerrando —la ventana se está
        // yendo—. En ese caso no hay nada que pintar y perder la trama es lo correcto.
        if (!_window.DispatcherQueue.TryEnqueue(() => action()))
        {
            return;
        }
    }

    public async Task<string?> PickSaveFileAsync(string suggestedName, string extension, string filterLabel)
    {
        FileSavePicker picker = new()
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = suggestedName,
        };

        picker.FileTypeChoices.Add(filterLabel, new List<string> { extension });

        Attach(picker);

        StorageFile? file = await picker.PickSaveFileAsync();
        return file?.Path;
    }

    public async Task<string?> PickOpenFileAsync(string extension, string filterLabel)
    {
        FileOpenPicker picker = new()
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            ViewMode = PickerViewMode.List,
        };

        picker.FileTypeFilter.Add(extension);

        Attach(picker);

        StorageFile? file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    public async Task<bool> ConfirmAsync(string title, string message, string confirmLabel)
    {
        if (_window.Content?.XamlRoot is not { } root)
        {
            return false;
        }

        ContentDialog dialog = new()
        {
            XamlRoot = root,
            Title = title,
            Content = message,
            PrimaryButtonText = confirmLabel,
            CloseButtonText = "Cancelar",

            // El botón por defecto es el de cerrar a propósito: lo que se confirma con esto
            // interrumpe una salida en directo, y un Enter de más no puede ser lo que la tire.
            DefaultButton = ContentDialogButton.Close,
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    /// <summary>
    /// Sin paquete, un selector de fichero no sabe de qué ventana cuelga y lanza al
    /// abrirse. Hay que dárselo a mano; es el precio de no depender de la Store.
    /// </summary>
    private void Attach(object picker) =>
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(_window));
}
