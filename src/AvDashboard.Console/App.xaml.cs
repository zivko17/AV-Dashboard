using System.Windows;
using System.Windows.Threading;
using AvDashboard.Console.Services;
using AvDashboard.Console.ViewModels;

namespace AvDashboard.Console;

/// <summary>
/// Raiz de composicion.
///
/// Sin contenedor de inyeccion de dependencias a proposito: la consola tiene ocho
/// objetos de larga vida y un grafo de dependencias que cabe en esta pantalla. Un
/// contenedor aqui es ceremonia, no arquitectura.
/// </summary>
public partial class App : Application
{
    private NodeManager? _nodes;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnUnhandledException;

        // Antes de crear la ventana: los estilos leen los pinceles de acento al aplicarse,
        // y aplicarlos ya tenidos evita un parpadeo de azul de fabrica en el arranque.
        DesktopChrome.ApplySystemAccent(Resources);

        _nodes = new NodeManager();
        MainWindowViewModel viewModel = new(_nodes);

        MainWindow window = new() { DataContext = viewModel };
        MainWindow = window;
        window.Show();

        _ = viewModel.InitializeAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _nodes?.Dispose();
        base.OnExit(e);
    }

    /// <summary>
    /// Una excepcion no controlada en la consola no puede cerrar la ventana durante un
    /// show. Se ensena, se registra y se sigue: la consola es una herramienta de
    /// diagnostico, y una herramienta que desaparece cuando algo va mal no sirve.
    /// </summary>
    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            $"Se ha producido un error no controlado:\n\n{e.Exception.Message}\n\n" +
            "La consola sigue funcionando. Revisa el Timeline por si el agente registró algo.",
            "AV Dashboard",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

        e.Handled = true;
    }
}
