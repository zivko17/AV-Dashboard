using AvDashboard.Client.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AvDashboard.Shell.Views;

public sealed partial class SistemaPage : UserControl
{
    public SistemaPage() => InitializeComponent();

    /// <summary>
    /// El ToggleSwitch de WinUI no tiene Command, así que el clic pasa por aquí.
    ///
    /// La comparación con el valor de la fila no es defensa de más: Toggled también se
    /// dispara cuando el enlace escribe IsOn al recargar la auditoría, y sin ella cada
    /// refresco volvería a mandar la escritura que acaba de hacerse.
    /// </summary>
    private void OnToggled(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch { DataContext: SettingRow row } toggle)
        {
            return;
        }

        if (toggle.IsOn == row.IsOn)
        {
            return;
        }

        if (row.ToggleRowCommand.CanExecute(null))
        {
            row.ToggleRowCommand.Execute(null);
        }
    }
}
