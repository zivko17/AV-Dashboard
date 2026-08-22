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
    public DataTemplate? Sistema { get; set; }

    /// <summary>Todo lo que aún no se ha portado. Se irá vaciando.</summary>
    public DataTemplate? Pendiente { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item) => item switch
    {
        SystemViewModel => Sistema,
        _ => Pendiente,
    };

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container) =>
        SelectTemplateCore(item);
}
