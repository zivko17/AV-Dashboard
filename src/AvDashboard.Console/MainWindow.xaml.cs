using System.Windows;
using System.Windows.Input;
using AvDashboard.Console.ViewModels;

namespace AvDashboard.Console;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
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
