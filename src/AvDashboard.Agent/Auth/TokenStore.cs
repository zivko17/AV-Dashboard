using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Runtime.Versioning;

namespace AvDashboard.Agent.Auth;

/// <summary>
/// Token bearer del agente. Vive en %ProgramData%\AvDashboard\agent.token con ACL de
/// Administradores y SYSTEM, y se rota en cada arranque del servicio.
///
/// La rotacion al arrancar es deliberada: si alguien copio el token, deja de valer en
/// cuanto el servicio se reinicia, y la consola lo relee del fichero porque corre en la
/// misma maquina.
/// </summary>
public sealed class TokenStore
{
    private readonly string _path;

    public TokenStore(string dataDirectory)
    {
        _path = Path.Combine(dataDirectory, "agent.token");
    }

    public string Token { get; private set; } = string.Empty;

    /// <summary>Genera un token nuevo y lo persiste con ACL restringida.</summary>
    [SupportedOSPlatform("windows")]
    public string Rotate()
    {
        Token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));

        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, Token);
        RestrictAcl(_path);

        return Token;
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
