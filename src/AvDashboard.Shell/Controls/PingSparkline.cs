using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

// ImplicitUsings trae System.IO, y ahi hay otro Path. El alias evita tener que escribir el
// nombre completo cada vez que se crea la polilinea.
using ShapePath = Microsoft.UI.Xaml.Shapes.Path;

namespace AvDashboard.Shell.Controls;

/// <summary>
/// Serie temporal de latencia.
///
/// La versión de WPF sobrescribía <c>OnRender</c> y dibujaba con un <c>DrawingContext</c>.
/// En WinUI no hay nada equivalente: el árbol visual es la única forma de pintar sin
/// arrastrar Win2D, que para una polilínea de una ventana de quince minutos es una
/// dependencia nativa entera a cambio de nada.
///
/// Así que se construyen formas: un <see cref="Path"/> con una figura por tramo continuo y
/// un rectángulo por hueco. El número de elementos lo marcan los cortes de la serie, no las
/// muestras, y una serie sana es un solo Path.
/// </summary>
public sealed class PingSparkline : UserControl
{
    private readonly Canvas _canvas = new();

    public PingSparkline()
    {
        Content = _canvas;

        // El Canvas no mide a sus hijos, así que hay que repintar cuando cambia el tamaño:
        // sin esto la gráfica se queda con el ancho que tuviera al primer layout.
        SizeChanged += (_, _) => Render();
    }

    public static readonly DependencyProperty SamplesProperty = DependencyProperty.Register(
        nameof(Samples), typeof(IReadOnlyList<double?>), typeof(PingSparkline),
        new PropertyMetadata(null, OnAnyChanged));

    public static readonly DependencyProperty LineBrushProperty = DependencyProperty.Register(
        nameof(LineBrush), typeof(Brush), typeof(PingSparkline),
        new PropertyMetadata(null, OnAnyChanged));

    /// <summary>Techo del eje en ms. Lo que pase por encima se recorta.</summary>
    public static readonly DependencyProperty CeilingMsProperty = DependencyProperty.Register(
        nameof(CeilingMs), typeof(double), typeof(PingSparkline),
        new PropertyMetadata(50.0, OnAnyChanged));

    public IReadOnlyList<double?>? Samples
    {
        get => (IReadOnlyList<double?>?)GetValue(SamplesProperty);
        set => SetValue(SamplesProperty, value);
    }

    public Brush? LineBrush
    {
        get => (Brush?)GetValue(LineBrushProperty);
        set => SetValue(LineBrushProperty, value);
    }

    public double CeilingMs
    {
        get => (double)GetValue(CeilingMsProperty);
        set => SetValue(CeilingMsProperty, value);
    }

    private static void OnAnyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((PingSparkline)d).Render();

    private void Render()
    {
        _canvas.Children.Clear();

        double width = ActualWidth;
        double height = ActualHeight;

        if (width <= 0 || height <= 0)
        {
            return;
        }

        // Línea de media escala: referencia sin ejes ni etiquetas.
        _canvas.Children.Add(new Line
        {
            X1 = 0,
            Y1 = height / 2,
            X2 = width,
            Y2 = height / 2,
            Stroke = ThemeBrush.Named("DividerStrokeColorDefaultBrush"),
            StrokeThickness = 1,
        });

        IReadOnlyList<double?>? samples = Samples;

        if (samples is null || samples.Count < 2)
        {
            return;
        }

        double ceiling = CeilingMs <= 0 ? 50 : CeilingMs;
        double step = width / (samples.Count - 1);

        PathGeometry geometry = new();
        PathFigure? figure = null;

        for (int i = 0; i < samples.Count; i++)
        {
            double x = i * step;

            if (samples[i] is not double rtt)
            {
                // Una muestra perdida rompe la línea y pinta una banda: es una ausencia de
                // dato, no un cero, y no debe leerse como latencia baja.
                Rectangle gap = new()
                {
                    Width = Math.Max(step, 1),
                    Height = height,
                    Fill = ThemeBrush.Named("SystemFillColorCriticalBackgroundBrush"),
                };

                Canvas.SetLeft(gap, Math.Max(0, x - (step / 2)));
                Canvas.SetTop(gap, 0);
                _canvas.Children.Add(gap);

                figure = null;
                continue;
            }

            Windows.Foundation.Point point = new(x, height - (Math.Clamp(rtt / ceiling, 0, 1) * height));

            if (figure is null)
            {
                figure = new PathFigure { StartPoint = point, IsClosed = false, IsFilled = false };
                geometry.Figures.Add(figure);
                continue;
            }

            figure.Segments.Add(new LineSegment { Point = point });
        }

        if (geometry.Figures.Count > 0)
        {
            _canvas.Children.Add(new ShapePath
            {
                Data = geometry,
                Stroke = LineBrush ?? ThemeBrush.Named("AccentFillColorDefaultBrush"),
                StrokeThickness = 1.5,
            });
        }
    }
}
