using System;
using System.Threading.Tasks;
using System.Windows;
using AvDashboard.Client;

namespace AvDashboard.Console.Services;

/// <summary>
/// Lo que los ViewModels necesitan de WPF, y nada más. Es la única clase del proyecto que
/// existe solo para que los ViewModels no tengan que conocer este escritorio.
///
/// Los diálogos son asíncronos en la interfaz aunque aquí sean síncronos: en WinUI 3 lo son
/// de verdad, y una firma que promete menos de lo que la otra implementación necesita
/// obliga a cambiarla justo cuando el porte ya está a medias.
/// </summary>
internal sealed class WpfShell : IUiShell
{
    public void Post(Action action)
    {
        Application? app = Application.Current;

        if (app is null)
        {
            action();
            return;
        }

        app.Dispatcher.Invoke(action);
    }

    public Task<string?> PickSaveFileAsync(string suggestedName, string extension, string filterLabel)
    {
        Microsoft.Win32.SaveFileDialog dialog = new()
        {
            FileName = suggestedName,
            DefaultExt = extension,
            Filter = Filter(extension, filterLabel),
        };

        return Task.FromResult(dialog.ShowDialog() == true ? dialog.FileName : null);
    }

    public Task<string?> PickOpenFileAsync(string extension, string filterLabel)
    {
        Microsoft.Win32.OpenFileDialog dialog = new()
        {
            DefaultExt = extension,
            Filter = Filter(extension, filterLabel),
            CheckFileExists = true,
        };

        return Task.FromResult(dialog.ShowDialog() == true ? dialog.FileName : null);
    }

    public Task<bool> ConfirmAsync(string title, string message, string confirmLabel)
    {
        // El botón por defecto es «No» a propósito: lo que se confirma con esto interrumpe
        // una salida en directo, y un Enter de más no puede ser lo que la tire.
        MessageBoxResult answer = MessageBox.Show(
            $"{message}\n\n¿{confirmLabel}?",
            title,
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        return Task.FromResult(answer == MessageBoxResult.Yes);
    }

    private static string Filter(string extension, string label) =>
        $"{label} (*{extension})|*{extension}|Todos los archivos (*.*)|*.*";
}
