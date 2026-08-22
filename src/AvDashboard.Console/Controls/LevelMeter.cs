using System.Windows;
using System.Windows.Media;

namespace AvDashboard.Console.Controls;

/// <summary>
/// Medidor de nivel dibujado a mano.
///
/// Sin librerias ni animaciones de WPF: a 20 fps y con veinte endpoints, un
/// <c>ProgressBar</c> con plantilla genera mas trabajo de layout del que este control
/// hace entero. Aqui solo se pinta un rectangulo y un pico que decae.
/// </summary>
public sealed class LevelMeter : FrameworkElement
{
    public static readonly DependencyProperty PeakProperty = DependencyProperty.Register(
        nameof(Peak), typeof(float), typeof(LevelMeter),
        new FrameworkPropertyMetadata(0f, FrameworkPropertyMetadataOptions.AffectsRender, OnPeakChanged));

    private static readonly Brush TrackBrush = Frozen(Color.FromRgb(0x20, 0x26, 0x2D));
    private static readonly Brush LevelBrush = Frozen(Color.FromRgb(0x3F, 0xBF, 0x6F));
    private static readonly Brush HotBrush = Frozen(Color.FromRgb(0xE0, 0xA8, 0x3C));
    private static readonly Brush ClipBrush = Frozen(Color.FromRgb(0xE4, 0x57, 0x4C));
    private static readonly Pen HoldPen = FrozenPen(Color.FromRgb(0xE8, 0xED, 0xF2), 1.5);

    private float _hold;
    private DateTime _holdSetAt = DateTime.MinValue;

    /// <summary>Cuanto se mantiene el pico antes de empezar a caer.</summary>
    private static readonly TimeSpan HoldTime = TimeSpan.FromMilliseconds(900);

    public float Peak
    {
        get => (float)GetValue(PeakProperty);
        set => SetValue(PeakProperty, value);
    }

    private static void OnPeakChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        LevelMeter meter = (LevelMeter)d;
        float peak = (float)e.NewValue;

        if (peak >= meter._hold)
        {
            meter._hold = peak;
            meter._holdSetAt = DateTime.UtcNow;
        }
        else if (DateTime.UtcNow - meter._holdSetAt > HoldTime)
        {
            // Caida lineal suave: un pico que desaparece de golpe no se llega a leer.
            meter._hold = Math.Max(peak, meter._hold - 0.02f);
        }
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        double width = ActualWidth;
        double height = ActualHeight;

        if (width <= 0 || height <= 0)
        {
            return;
        }

        drawingContext.DrawRectangle(TrackBrush, null, new Rect(0, 0, width, height));

        float level = Math.Clamp(Peak, 0f, 1f);

        // Escala logaritmica: en lineal, todo lo que pasa por debajo de -20 dBFS es
        // invisible, y ahi es donde vive la mitad del material de un show.
        double scaled = level <= 0 ? 0 : Math.Clamp(1 + (Math.Log10(level) / 3.0), 0, 1);

        Brush brush = level switch
        {
            >= 0.99f => ClipBrush,
            >= 0.85f => HotBrush,
            _ => LevelBrush,
        };

        drawingContext.DrawRectangle(brush, null, new Rect(0, 0, width * scaled, height));

        if (_hold > 0)
        {
            double holdScaled = Math.Clamp(1 + (Math.Log10(Math.Max(_hold, 0.0001f)) / 3.0), 0, 1);
            double x = width * holdScaled;
            drawingContext.DrawLine(HoldPen, new Point(x, 0), new Point(x, height));
        }
    }

    private static Brush Frozen(Color color)
    {
        SolidColorBrush brush = new(color);
        brush.Freeze();
        return brush;
    }

    private static Pen FrozenPen(Color color, double thickness)
    {
        Pen pen = new(Frozen(color), thickness);
        pen.Freeze();
        return pen;
    }
}
