using AvDashboard.Client.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AvDashboard.Shell;

/// <summary>
/// Elige la vista de cada página.
///
/// En WPF esto no existía: bastaba con declarar un DataTemplate con DataType y el marco
/// emparejaba solo. WinUI no tiene plantillas implícitas por tipo, así que el emparejamiento
/// se escribe. A cambio está en un sitio y se lee de un vistazo, en lugar de repartido por
/// nueve declaraciones en el diccionario de la ventana.
/// </summary>
public sealed partial class PageTemplateSelector : DataTemplateSelector
{
    public DataTemplate? Dashboard { get; set; }
    public DataTemplate? Sistema { get; set; }
    public DataTemplate? Audio { get; set; }
    public DataTemplate? Video { get; set; }
    public DataTemplate? Red { get; set; }
    public DataTemplate? Servicios { get; set; }
    public DataTemplate? Perfiles { get; set; }
    public DataTemplate? Timeline { get; set; }

    /// <summary>
    /// Las secciones que todavía no existen en ninguna interfaz.
    ///
    /// Ya no es «lo que falta por portar»: es el marcador de las tres que llegan en fases
    /// posteriores, y por eso el ViewModel que lo usa es el PlaceholderViewModel y no un
    /// caso por defecto que se trague un tipo nuevo sin avisar.
    /// </summary>
    public DataTemplate? Pendiente { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item) => item switch
    {
        DashboardViewModel => Dashboard,
        SystemViewModel => Sistema,
        AudioViewModel => Audio,
        VideoViewModel => Video,
        NetworkViewModel => Red,
        ServicesViewModel => Servicios,
        ProfilesViewModel => Perfiles,
        TimelineViewModel => Timeline,
        _ => Pendiente,
    };

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container) =>
        SelectTemplateCore(item);
}
