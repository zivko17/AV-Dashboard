using System.Windows;
using System.Windows.Media;

namespace AvDashboard.Console.Controls;

/// <summary>
/// Serie temporal de latencia dibujada a mano.
///
/// Reemplaza al grafico de barras de `pingmulti`. Se dibuja con un
/// <see cref="StreamGeometry"/> por serie, que es una sola operacion de render para
/// novecientos puntos: lo que hace falta para tener esto en el dashboard sin coste.
///
/// Cuando el Timeline necesite zoom, pan y varios ejes (Fase 6), ahi entra ScottPlot.
/// Para una ventana de quince minutos, esto sobra y no arrastra dependencias.
/// </summary>
public sealed class PingSparkline : FrameworkElement
{
    public static readonly DependencyProperty SamplesProperty = DependencyProperty.Register(
        nameof(Samples), typeof(IReadOnlyList<double?>), typeof(PingSparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LineBrushProperty = DependencyProperty.Register(
        nameof(LineBrush), typeof(Brush), typeof(PingSparkline),
        new FrameworkPropertyMetadata(Brushes.DeepSkyBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Techo del eje en ms. Las muestras por encima se recortan y se marcan.</summary>
    public static readonly DependencyProperty CeilingMsProperty = DependencyProperty.Register(
        nameof(CeilingMs), typeof(double), typeof(PingSparkline),
        new FrameworkPropertyMetadata(50.0, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Pen GridPen = FrozenPen(Color.FromRgb(0x2C, 0x34, 0x3D), 1);
    private static readonly Brush LossBrush = FrozenBrush(Color.FromArgb(0x66, 0xE4, 0x57, 0x4C));

    public IReadOnlyList<double?>? Samples
    {
        get => (IReadOnlyList<double?>?)GetValue(SamplesProperty);
        set => SetValue(SamplesProperty, value);
    }

    public Brush LineBrush
    {
        get => (Brush)GetValue(LineBrushProperty);
        set => SetValue(LineBrushProperty, value);
    }

    public double CeilingMs
    {
        get => (double)GetValue(CeilingMsProperty);
        set => SetValue(CeilingMsProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        double width = ActualWidth;
        double height = ActualHeight;

        if (width <= 0 || height <= 0)
        {
            return;
        }

        // Fondo transparente pero presente: sin esto el control no recibe hit testing y
        // no se puede hacer clic para saltar al Timeline.
        drawingContext.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, width, height));

        // Linea de media escala, para tener referencia sin ejes ni etiquetas.
        drawingContext.DrawLine(GridPen, new Point(0, height / 2), new Point(width, height / 2));

        IReadOnlyList<double?>? samples = Samples;
        if (samples is null || samples.Count < 2)
        {
            return;
        }

        double ceiling = CeilingMs <= 0 ? 50 : CeilingMs;
        double step = width / (samples.Count - 1);

        StreamGeometry geometry = new();
        using (StreamGeometryContext context = geometry.Open())
        {
            bool open = false;

            for (int i = 0; i < samples.Count; i++)
            {
                double x = i * step;

                if (samples[i] is not double rtt)
                {
                    // Una muestra perdida rompe la linea y pinta una banda roja: es una
                    // ausencia de dato, no un cero, y no debe leerse como latencia baja.
                    drawingContext.DrawRectangle(LossBrush, null,
                        new Rect(Math.Max(0, x - (step / 2)), 0, Math.Max(step, 1), height));
                    open = false;
                    continue;
                }

                double y = height - (Math.Clamp(rtt / ceiling, 0, 1) * height);
                Point point = new(x, y);

                if (!open)
                {
                    context.BeginFigure(point, isFilled: false, isClosed: false);
                    open = true;
                }
                else
                {
                    context.LineTo(point, isStroked: true, isSmoothJoin: false);
                }
            }
        }

        geometry.Freeze();

        Pen pen = new(LineBrush, 1.5);
        pen.Freeze();
        drawingContext.DrawGeometry(null, pen, geometry);
    }

    private static Brush FrozenBrush(Color color)
    {
        SolidColorBrush brush = new(color);
        brush.Freeze();
        return brush;
    }

    private static Pen FrozenPen(Color color, double thickness)
    {
        Pen pen = new(FrozenBrush(color), thickness);
        pen.Freeze();
        return pen;
    }
}
