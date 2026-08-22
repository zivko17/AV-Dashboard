using AvDashboard.Client;
using AvDashboard.Client.ViewModels;
using AvDashboard.Shell.Services;
using Microsoft.UI.Xaml;

namespace AvDashboard.Shell;

/// <summary>
/// Raíz de composición de la interfaz de WinUI 3.
///
/// Sin contenedor de inyección de dependencias, igual que en la de WPF y por el mismo
/// motivo: hay tres objetos de larga vida y el grafo cabe en esta pantalla.
/// </summary>
public partial class App : Application
{
    private NodeManager? _nodes;
    private MainWindow? _window;

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _nodes = new NodeManager();

        _window = new MainWindow();

        // La interfaz se construye antes que el ViewModel porque el shell necesita la
        // ventana: los diálogos de fichero de WinUI piden el identificador nativo, y el
        // ContentDialog necesita un XamlRoot. En WPF ninguna de las dos cosas hacía falta.
        WinUiShell shell = new(_window);

        MainWindowViewModel viewModel = new(_nodes, shell);
        _window.Bind(viewModel);
        _window.Activate();

        _ = viewModel.InitializeAsync();
    }
}
