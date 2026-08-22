namespace AvDashboard.Client;

/// <summary>
/// Lo poco que un ViewModel necesita de la interfaz que lo pinta.
///
/// Son tres cosas y ninguna más: volver al hilo de la interfaz, pedir un fichero y
/// preguntar antes de algo irreversible. Todo lo demás de un ViewModel es datos y lógica,
/// y por eso viven aquí y no dentro de una aplicación de escritorio concreta.
///
/// Existe porque hay dos interfaces: la de WPF, que es la que funciona hoy, y la de
/// WinUI 3, que es a donde vamos. Sin esto, cada una necesitaría su copia de los mismos mil
/// ochocientos renglones de ViewModel, y las dos copias empezarían a divergir el primer día.
/// </summary>
public interface IUiShell
{
    /// <summary>
    /// Ejecuta algo en el hilo de la interfaz. El hub de telemetría entrega en un hilo del
    /// pool y las colecciones están enlazadas a la vista, así que no es opcional.
    /// </summary>
    void Post(Action action);

    /// <summary>Diálogo de guardar. Devuelve null si el usuario cancela.</summary>
    Task<string?> PickSaveFileAsync(string suggestedName, string extension, string filterLabel);

    /// <summary>Diálogo de abrir. Devuelve null si el usuario cancela.</summary>
    Task<string?> PickOpenFileAsync(string extension, string filterLabel);

    /// <summary>
    /// Confirmación de algo que no se puede deshacer. Devuelve false por defecto: si una
    /// interfaz no implementa el diálogo, lo que no debe pasar es que la acción se dispare.
    /// </summary>
    Task<bool> ConfirmAsync(string title, string message, string confirmLabel);
}

/// <summary>
/// Interfaz que no existe. Sirve para instanciar un ViewModel en una prueba sin arrastrar
/// un escritorio detrás: <c>Post</c> ejecuta ahí mismo y todo diálogo se cancela.
/// </summary>
public sealed class HeadlessShell : IUiShell
{
    public void Post(Action action) => action();

    public Task<string?> PickSaveFileAsync(string suggestedName, string extension, string filterLabel) =>
        Task.FromResult<string?>(null);

    public Task<string?> PickOpenFileAsync(string extension, string filterLabel) =>
        Task.FromResult<string?>(null);

    public Task<bool> ConfirmAsync(string title, string message, string confirmLabel) =>
        Task.FromResult(false);
}
