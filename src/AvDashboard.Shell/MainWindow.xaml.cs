using AvDashboard.Client.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml.Media;

namespace AvDashboard.Shell;

public sealed partial class MainWindow : Window
{
    private MainWindowViewModel? _viewModel;

    public MainWindow()
    {
        InitializeComponent();

        // Mica. En la version de WPF esto eran tres llamadas a DwmSetWindowAttribute, una
        // comprobacion de version, una de HRESULT y volver el fondo transparente solo
        // despues de que las dos salieran bien. Aqui es una linea, y la barra de titulo
        // oscura y las esquinas redondeadas vienen de serie.
        if (MicaController.IsSupported())
        {
            SystemBackdrop = new MicaBackdrop();
        }
    }

    /// <summary>
    /// Se llama desde App una vez construida la ventana. El ViewModel no puede llegar por
    /// el constructor porque el shell que necesita depende de esta misma ventana.
    /// </summary>
    public void Bind(MainWindowViewModel viewModel)
    {
        _viewModel = viewModel;
        Root.DataContext = viewModel;

        // Los elementos del panel se construyen a mano y no con MenuItemsSource. Con una
        // plantilla, el icono acaba dentro del contenido, y en modo compacto el
        // NavigationView solo ensena la propiedad Icon: la lista se veria recortada en vez
        // de como una columna de iconos. Es justo el modo que se queria.
        foreach (PageViewModel page in viewModel.Pages)
        {
            Nav.MenuItems.Add(new NavigationViewItem
            {
                Content = page.Title,
                Icon = new FontIcon { Glyph = page.Glyph },
                Tag = page,
            });
        }

        Nav.SelectedItem = Nav.MenuItems.Count > 0 ? Nav.MenuItems[0] : null;
    }

    private void OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_viewModel is not null &&
            args.SelectedItem is NavigationViewItem { Tag: PageViewModel page })
        {
            _viewModel.Current = page;
        }
    }
}
