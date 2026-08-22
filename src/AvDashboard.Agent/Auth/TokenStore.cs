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
    private readonly bool _mirrorForUser;

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

    public TokenStore(string dataDirectory, bool mirrorForUser = false)
    {
        _path = Path.Combine(dataDirectory, "agent.token");
        _mirrorForUser = mirrorForUser;
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

        if (TryWrite(_path))
        {
            written.Add(_path);
        }

        // El espejo de usuario solo tiene sentido en modo portatil. En modo servicio el
        // token debe quedarse donde solo lo leen Administradores y SYSTEM.
        if (_mirrorForUser && !string.Equals(_path, UserScopedPath, StringComparison.OrdinalIgnoreCase)
                           && TryWrite(UserScopedPath))
        {
            written.Add(UserScopedPath);
        }

        WrittenTo = written;
        return Token;
    }

    /// <summary>Rutas en las que ha quedado el token en la ultima rotacion.</summary>
    public IReadOnlyList<string> WrittenTo { get; private set; } = Array.Empty<string>();

    [SupportedOSPlatform("windows")]
    private bool TryWrite(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, Token);
            RestrictAcl(path);
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
