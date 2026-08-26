using AvDashboard.Core.Diagnostics;
using AvDashboard.Core.Models;

namespace AvDashboard.Client.ViewModels;

/// <summary>
/// Un porcentaje de carga del panel principal, con su color ya decidido.
///
/// El color sale de <see cref="LoadGrade"/> y no de un umbral escrito en la vista, por dos
/// razones. La primera es que la escala no es lineal y depende de la magnitud: una CPU al
/// 50 % va sobrada y un enlace de red al 50 % ya se ha movido de su sitio, y eso no cabe en
/// un <c>DataTrigger</c>. La segunda es que en WinUI no hay <c>StringFormat</c> ni
/// <c>DataTrigger</c>, así que escribir esto en el XAML significaría escribirlo dos veces y
/// verlas divergir.
/// </summary>
public sealed class LoadGaugeViewModel
{
    public LoadGaugeViewModel(
        string label,
        LoadMetric metric,
        double? percent,
        string? detail = null,
        string? unavailableReason = null)
    {
        Label = label;
        Detail = detail;
        UnavailableReason = unavailableReason;
        Percent = percent;

        if (percent is double value)
        {
            LoadColor color = LoadGrade.ColorFor(metric, value);

            ColorHex = color.Hex;
            Health = LoadGrade.HealthFor(metric, value);
            PercentText = $"{value:0} %";

            // El relleno de la barra se satura al 100 aunque el dato venga por encima: un
            // contador que devuelve 103 % no puede pintar una barra que se sale de su caja.
            Fill = Math.Clamp(value, 0, 100) / 100.0;
        }
        else
        {
            // Sin dato no hay color: gris. Pintar un cero en verde seria afirmar que va bien
            // algo de lo que no se sabe nada, y es justo lo que se prohibe en Sistema.
            ColorHex = "#6E7876";
            Health = HealthState.Unknown;
            PercentText = "—";
            Fill = 0;
        }
    }

    public string Label { get; }

    /// <summary>La cifra en crudo debajo del porcentaje: «17,7 / 61,7 GB», «1 Gb/s»…</summary>
    public string? Detail { get; }

    public double? Percent { get; }
    public string PercentText { get; }

    /// <summary>Color en `#RRGGBB`. Cada interfaz tiene su conversor a pincel.</summary>
    public string ColorHex { get; }

    public HealthState Health { get; }

    /// <summary>De 0 a 1, para el ancho de la barra.</summary>
    public double Fill { get; }

    public bool HasValue => Percent is not null;

    /// <summary>
    /// Por qué no hay dato, cuando no lo hay.
    ///
    /// Una casilla en gris sin explicación es exactamente el fallo que la pestaña Sistema
    /// tiene prohibido: una fila sin valor y sin motivo.
    /// </summary>
    public string? UnavailableReason { get; }

    /// <summary>
    /// Lo que se lee debajo del número: la cifra si hay dato, el motivo si no lo hay.
    ///
    /// Se mira <see cref="HasValue"/> y no si <see cref="Detail"/> está vacío. Encadenándolo
    /// —`Detail ?? UnavailableReason`— la GPU salía marcando «1 %» con un pie que decía «sin
    /// contadores de GPU en esta máquina», porque tenía carga pero no temperatura. Una fila
    /// que se contradice consigo misma es peor que una fila sin dato.
    /// </summary>
    public string? Footnote => HasValue ? Detail : UnavailableReason;
}
