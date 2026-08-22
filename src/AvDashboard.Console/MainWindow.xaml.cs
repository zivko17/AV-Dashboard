using System;
using System.Windows;
using System.Windows.Input;
using AvDashboard.Console.Services;
using AvDashboard.Console.ViewModels;

namespace AvDashboard.Console;

public partial class MainWindow : Window
{
    /// <summary>
    /// Ancho por debajo del cual el panel de navegacion se repliega a iconos. Es el mismo
    /// umbral que usa el NavigationView de Windows 11 para pasar a modo compacto: cuando la
    /// ventana ya no da para tener el panel y una tabla al lado, el panel es lo que sobra.
    /// </summary>
    private const double CompactThreshold = 1120;

    private const double PaneExpanded = 248;
    private const double PaneCompact = 56;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        SizeChanged += (_, _) => UpdatePane();
    }

    /// <summary>
    /// true cuando el panel esta replegado a iconos. Es propiedad de dependencia y no del
    /// ViewModel a proposito: es estado de la ventana, no del nodo, y las plantillas del
    /// rail se enlazan a ella por RelativeSource.
    /// </summary>
    public static readonly DependencyProperty IsPaneCompactProperty =
        DependencyProperty.Register(
            nameof(IsPaneCompact), typeof(bool), typeof(MainWindow),
            new PropertyMetadata(false));

    public bool IsPaneCompact
    {
        get => (bool)GetValue(IsPaneCompactProperty);
        set => SetValue(IsPaneCompactProperty, value);
    }

    private void UpdatePane()
    {
        bool compacto = ActualWidth > 0 && ActualWidth < CompactThreshold;

        if (compacto == IsPaneCompact)
        {
            return;
        }

        IsPaneCompact = compacto;
        PaneColumn.Width = new GridLength(compacto ? PaneCompact : PaneExpanded);
    }

    /// <summary>
    /// Barra de titulo oscura, esquinas redondeadas y fondo Mica. Aqui y no en el
    /// constructor porque hasta este momento la ventana no tiene identificador nativo.
    /// </summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        DesktopChrome.Apply(this);
        UpdatePane();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is MainWindowViewModel previous)
        {
            previous.PropertyChanged -= OnViewModelPropertyChanged;
        }

        if (e.NewValue is MainWindowViewModel current)
        {
            current.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    /// <summary>El foco va al buscador en cuanto la paleta se abre, y no antes.</summary>
    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.IsPaletteOpen) &&
            ViewModel?.IsPaletteOpen == true)
        {
            Dispatcher.InvokeAsync(() => PaletteInput.Focus());
        }
    }

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    /// <summary>
    /// Teclado de la paleta. Va en el code-behind y no en un comando porque es
    /// interaccion pura de vista: flechas, Enter y Esc no son logica de negocio.
    /// </summary>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        MainWindowViewModel? viewModel = ViewModel;
        if (viewModel is null || !viewModel.IsPaletteOpen)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Escape:
                viewModel.IsPaletteOpen = false;
                e.Handled = true;
                break;

            case Key.Down:
                viewModel.Palette.MoveSelection(1);
                e.Handled = true;
                break;

            case Key.Up:
                viewModel.Palette.MoveSelection(-1);
                e.Handled = true;
                break;

            case Key.Enter:
                // De momento la paleta navega; ejecutar acciones de escritura llega con
                // los actuadores de la Fase 2, y hasta entonces prometerlo seria mentir.
                viewModel.IsPaletteOpen = false;
                e.Handled = true;
                break;
        }
    }
}
