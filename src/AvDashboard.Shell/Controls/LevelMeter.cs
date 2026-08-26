using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace AvDashboard.Shell.Controls;

/// <summary>
/// Medidor de nivel.
///
/// Tres rectángulos cuyo ancho se recalcula, y nada más: ni animaciones ni plantilla de
/// control. A veinte tramas por segundo y con veinte endpoints en pantalla, un
/// <c>ProgressBar</c> con su plantilla genera más trabajo de layout del que hace este
/// control entero.
///
/// La versión de WPF dibujaba en <c>OnRender</c>; aquí las formas se crean una vez en el
/// constructor y solo se les toca el ancho, que es la operación barata.
/// </summary>
public sealed class LevelMeter : UserControl
{
    /// <summary>Cuánto se mantiene el pico antes de empezar a caer.</summary>
    private static readonly TimeSpan HoldTime = TimeSpan.FromMilliseconds(900);

    private readonly Rectangle _track = new();
    private readonly Rectangle _level = new();
    private readonly Rectangle _hold = new() { Width = 1.5 };
    private readonly Canvas _canvas = new();

    private float _holdValue;
    private DateTime _holdSetAt = DateTime.MinValue;

    public LevelMeter()
    {
        _canvas.Children.Add(_track);
        _canvas.Children.Add(_level);
        _canvas.Children.Add(_hold);

        Content = _canvas;

        SizeChanged += (_, _) => Render();
    }

    public static readonly DependencyProperty PeakProperty = DependencyProperty.Register(
        nameof(Peak), typeof(float), typeof(LevelMeter),
        new PropertyMetadata(0f, OnPeakChanged));

    public float Peak
    {
        get => (float)GetValue(PeakProperty);
        set => SetValue(PeakProperty, value);
    }

    private static void OnPeakChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        LevelMeter meter = (LevelMeter)d;
        float peak = (float)e.NewValue;

        if (peak >= meter._holdValue)
        {
            meter._holdValue = peak;
            meter._holdSetAt = DateTime.UtcNow;
        }
        else if (DateTime.UtcNow - meter._holdSetAt > HoldTime)
        {
            // Caída lineal suave: un pico que desaparece de golpe no da tiempo a leerlo.
            meter._holdValue = Math.Max(peak, meter._holdValue - 0.02f);
        }

        meter.Render();
    }

    private void Render()
    {
        double width = ActualWidth;
        double height = ActualHeight;

        if (width <= 0 || height <= 0)
        {
            return;
        }

        _track.Width = width;
        _track.Height = height;
        _track.Fill = ThemeBrush.Named("ControlAltFillColorSecondaryBrush");

        float level = Math.Clamp(Peak, 0f, 1f);

        // Escala logarítmica: en lineal, todo lo que pasa por debajo de -20 dBFS es
        // invisible, y ahí es donde vive la mitad del material de un show.
        double scaled = level <= 0 ? 0 : Math.Clamp(1 + (Math.Log10(level) / 3.0), 0, 1);

        _level.Width = width * scaled;
        _level.Height = height;
        _level.Fill = ThemeBrush.Named(level switch
        {
            >= 0.99f => "SystemFillColorCriticalBrush",
            >= 0.85f => "SystemFillColorCautionBrush",
            _ => "SystemFillColorSuccessBrush",
        });

        if (_holdValue > 0)
        {
            double holdScaled = Math.Clamp(1 + (Math.Log10(Math.Max(_holdValue, 0.0001f)) / 3.0), 0, 1);

            _hold.Height = height;
            _hold.Fill = ThemeBrush.Named("TextFillColorPrimaryBrush");
            _hold.Visibility = Visibility.Visible;

            Canvas.SetLeft(_hold, Math.Clamp((width * holdScaled) - 0.75, 0, Math.Max(0, width - 1.5)));
        }
        else
        {
            _hold.Visibility = Visibility.Collapsed;
        }
    }
}
