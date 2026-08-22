using System.Runtime.CompilerServices;

// El parser de EDID expone sus piezas internas a las pruebas para poder verificar cada
// campo por separado —el desempaquetado del fabricante, un descriptor de audio suelto—
// sin tener que ampliar la superficie publica de Core para ello.
[assembly: InternalsVisibleTo("AvDashboard.Core.Tests")]
