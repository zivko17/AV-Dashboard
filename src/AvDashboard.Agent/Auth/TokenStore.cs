using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Runtime.Versioning;

namespace AvDashboard.Agent.Auth;

/// <summary>
/// Token bearer del agente.
///
/// En modo servicio vive en %ProgramData%\AvDashboard\agent.token con ACL de
/// Administradores y SYSTEM, que es donde la consola elevada lo encuentra.
///
/// En modo portatil el agente corre como un usuario normal y escribe junto al
/// ejecutable, donde la consola no tiene forma de adivinar que esta. Por eso ahi se
/// escribe ademas una copia en %LocalAppData%\AvDashboard\agent.token: es una ruta que
/// los dos procesos conocen y que no necesita privilegios.
///
/// La rotacion al arrancar es deliberada: si alguien copio el token, deja de valer en
/// cuanto el servicio se reinicia.
/// </summary>
public sealed class TokenStore
{
    private readonly string _path;
    private readonly bool _portable;

    /// <summary>Ruta compartida entre agente y consola cuando no hay servicio de por medio.</summary>
    public static string UserScopedPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AvDashboard",
        "agent.token");

    /// <summary>Ruta del modo servicio.</summary>
    public static string MachineScopedPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "AvDashboard",
        "agent.token");

    /// <param name="portable">
    /// En modo portatil el agente corre como un usuario normal y la consola tambien, asi
    /// que el token se deja con la ACL heredada del perfil del usuario y se copia a
    /// %LocalAppData%. En modo servicio se endurece a SYSTEM y Administradores.
    /// </param>
    public TokenStore(string dataDirectory, bool portable = false)
    {
        _path = Path.Combine(dataDirectory, "agent.token");
        _portable = portable;
    }

    public string Token { get; private set; } = string.Empty;

    /// <summary>
    /// Genera un token nuevo y lo persiste. Devuelve el token; las rutas donde ha quedado
    /// estan en <see cref="WrittenTo"/>, que es lo que el agente escribe en el log para
    /// que sepas donde mirar cuando la consola diga que no lo encuentra.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public string Rotate()
    {
        Token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));

        List<string> written = new();

        // El endurecimiento de ACL es exclusivo del modo servicio. En portatil, quien
        // escribe y quien lee son el mismo usuario sin privilegios: dejar el fichero solo
        // para SYSTEM y Administradores lo hace ilegible para la propia consola, que es
        // justo lo contrario de para lo que esta.
        if (TryWrite(_path, harden: !_portable))
        {
            written.Add(_path);
        }

        if (_portable && !string.Equals(_path, UserScopedPath, StringComparison.OrdinalIgnoreCase)
                      && TryWrite(UserScopedPath, harden: false))
        {
            written.Add(UserScopedPath);
        }

        WrittenTo = written;
        return Token;
    }

    /// <summary>Rutas en las que ha quedado el token en la ultima rotacion.</summary>
    public IReadOnlyList<string> WrittenTo { get; private set; } = Array.Empty<string>();

    [SupportedOSPlatform("windows")]
    private bool TryWrite(string path, bool harden)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            // Un token de una version anterior puede tener una ACL que nos impida
            // sobrescribirlo, incluso siendo nuestro. Borrar y recrear es mas fiable que
            // pelearse con el descriptor de seguridad, y el permiso para borrar viene de
            // la carpeta, que si es nuestra.
            if (File.Exists(path))
            {
                try
                {
                    File.Delete(path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Si no se puede borrar, todavia puede que se pueda sobrescribir.
                }
            }

            File.WriteAllText(path, Token);

            if (harden)
            {
                RestrictAcl(path);
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Un USB de solo lectura, o una carpeta sin permisos. Se sigue con las demas
            // rutas: quedarse sin ninguna es lo unico que impide usar la aplicacion.
            return false;
        }
    }

    /// <summary>Lee el token existente. Lo usa la consola local para autenticarse.</summary>
    public string? TryRead()
    {
        try
        {
            return File.Exists(_path) ? File.ReadAllText(_path).Trim() : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Ruta del fichero, para poder decirsela al usuario cuando algo falla.</summary>
    public string Path_ => _path;

    /// <summary>
    /// Deja el fichero accesible solo a SYSTEM y a Administradores, quitando la herencia.
    /// Sin esto, cualquier proceso de usuario puede leer el token y hablar con el agente.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static void RestrictAcl(string path)
    {
        try
        {
            FileInfo file = new(path);
            FileSecurity security = new();

            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

            SecurityIdentifier system = new(WellKnownSidType.LocalSystemSid, null);
            SecurityIdentifier administrators = new(WellKnownSidType.BuiltinAdministratorsSid, null);

            security.AddAccessRule(new FileSystemAccessRule(
                system, FileSystemRights.FullControl, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(
                administrators, FileSystemRights.FullControl, AccessControlType.Allow));

            file.SetAccessControl(security);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // En modo portatil, sin privilegios, no se puede endurecer la ACL. El agente
            // sigue arrancando pero solo escucha en loopback: ver AgentOptions.
        }
    }
}
