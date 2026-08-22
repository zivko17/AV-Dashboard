using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;

namespace AvDashboard.Native;

/// <summary>
/// Comprobaciones de plataforma y privilegios. El mismo binario corre en modo servicio
/// (LocalSystem) y en modo portatil (sesion de usuario, sin admin), y cada proveedor
/// tiene que saber en cual de los dos esta antes de intentar nada.
/// </summary>
public static class Platform
{
    public static bool IsWindows => OperatingSystem.IsWindows();

    /// <summary>true si el proceso corre elevado. En modo portatil sera false casi siempre.</summary>
    [SupportedOSPlatform("windows")]
    public static bool IsElevated
    {
        get
        {
            try
            {
                using WindowsIdentity identity = WindowsIdentity.GetCurrent();
                WindowsPrincipal principal = new(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidOperationException)
            {
                return false;
            }
        }
    }

    /// <summary>true si el proceso corre como LocalSystem, es decir, como servicio.</summary>
    [SupportedOSPlatform("windows")]
    public static bool IsLocalSystem
    {
        get
        {
            try
            {
                using WindowsIdentity identity = WindowsIdentity.GetCurrent();
                return identity.User is not null &&
                       identity.User.IsWellKnown(WellKnownSidType.LocalSystemSid);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidOperationException)
            {
                return false;
            }
        }
    }

    public static bool IsX64 => RuntimeInformation.ProcessArchitecture == Architecture.X64;

    /// <summary>Build de Windows. Se usa para degradar APIs no documentadas por version.</summary>
    public static Version OsVersion => Environment.OSVersion.Version;

    /// <summary>Lanza si no estamos en Windows. Los proveedores nativos lo llaman al construirse.</summary>
    public static void EnsureWindows()
    {
        if (!IsWindows)
        {
            throw new PlatformNotSupportedException(
                "AvDashboard.Native solo funciona en Windows. Core es la capa multiplataforma.");
        }
    }
}
