namespace AvDashboard.Core.Models;

/// <summary>
/// Posiciones memorizadas de ventanas de salida (M4 Window Coordinate Keeper).
/// Recupera un fullscreen perdido sin tocar la aplicacion.
/// </summary>
public sealed record WindowLayout
{
    public required string Name { get; init; }
    public required string NodeId { get; init; }
    public DateTimeOffset SavedAt { get; init; } = DateTimeOffset.UtcNow;
    public IReadOnlyList<WindowPlacement> Entries { get; init; } = Array.Empty<WindowPlacement>();
}

public sealed record WindowPlacement
{
    /// <summary>Nombre de imagen del proceso: "Arena.exe".</summary>
    public required string ProcessName { get; init; }

    /// <summary>Clase de ventana Win32, para distinguir la salida del interfaz de control.</summary>
    public string? WindowClass { get; init; }

    /// <summary>Patron del titulo. Se admite comodin '*' para versiones que cambian el titulo.</summary>
    public string? TitlePattern { get; init; }

    public int X { get; init; }
    public int Y { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }

    public bool Topmost { get; init; }
    public bool Borderless { get; init; }
}
