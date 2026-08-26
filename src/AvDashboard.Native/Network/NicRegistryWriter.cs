using System.Runtime.Versioning;
using AvDashboard.Core.Models;
using Microsoft.Win32;

namespace AvDashboard.Native.Network;

/// <summary>
/// Los dos ajustes del adaptador que viven en el registro y no en las propiedades avanzadas
/// del driver: el ahorro de energía y Nagle.
///
/// Van juntos porque comparten la parte difícil —encontrar la subclave de cada adaptador—
/// y se separan del de las descargas, que se escribe por WMI.
/// </summary>
[SupportedOSPlatform("windows")]
public static class NicRegistryWriter
{
    /// <summary>Clase de dispositivo de los adaptadores de red. Es fija y está documentada.</summary>
    private const string NetworkClassKey =
        @"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}";

    private const string TcpipInterfacesKey =
        @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces";

    /// <summary>
    /// Los tres bits que desactivan la gestión de energía del adaptador.
    ///
    /// 0x18 es lo que escribe Windows al desmarcar las dos casillas de la pestaña
    /// «Administración de energía» en el Administrador de dispositivos.
    /// </summary>
    private const int PowerManagementOff = 0x18;

    /// <summary>
    /// Apaga el ahorro de energía en todas las tarjetas físicas activas.
    ///
    /// **Reinicia el adaptador**: el enlace se cae un instante, igual que con las descargas.
    /// Por eso la fila está declarada como disruptiva en el catálogo.
    /// </summary>
    public static NicWriteResult DisablePowerManagement(bool dryRun = false)
    {
        Platform.EnsureWindows();

        return Apply(
            dryRun,
            nic => nic.PowerManagementEnabled != false,
            (adapterKey, _) => adapterKey.SetValue(
                "PnPCapabilities", PowerManagementOff, RegistryValueKind.DWord),
            after => after.PowerManagementEnabled == false);
    }

    /// <summary>
    /// Desactiva Nagle en todas las tarjetas físicas activas.
    ///
    /// A diferencia del anterior, **no corta el enlace**: los dos valores se aplican a las
    /// conexiones TCP nuevas, así que lo que ya está establecido sigue como estaba. En un
    /// rig eso significa que Art-Net y sACN —que son UDP— no se enteran, y que el control
    /// por TCP mejora cuando se reconecte.
    /// </summary>
    public static NicWriteResult DisableNagle(bool dryRun = false)
    {
        Platform.EnsureWindows();

        return Apply(
            dryRun,
            nic => nic.NagleDisabled != true,
            (_, interfaceKey) =>
            {
                interfaceKey?.SetValue("TcpAckFrequency", 1, RegistryValueKind.DWord);
                interfaceKey?.SetValue("TCPNoDelay", 1, RegistryValueKind.DWord);
            },
            after => after.NagleDisabled == true);
    }

    /// <summary>
    /// El recorrido común: buscar cada tarjeta física, escribir, y comprobar releyendo.
    ///
    /// La comprobación final no sobra. Escribir en el registro puede devolver éxito y no
    /// surtir efecto —una directiva que lo repone, un driver que ignora el valor— y ya se ha
    /// pagado dos veces el precio de dar por bueno lo que devuelve una API sin mirar el
    /// resultado.
    /// </summary>
    private static NicWriteResult Apply(
        bool dryRun,
        Func<NetworkInterfaceInfo, bool> needsWork,
        Action<RegistryKey, RegistryKey?> write,
        Func<NetworkInterfaceInfo, bool> isFixed)
    {
        NetworkInterfaceProvider provider = new();

        List<NetworkInterfaceInfo> targets = provider.GetInterfaces()
            .Where(n => n.IsUp && n.IsPhysical)
            .Where(needsWork)
            .ToList();

        if (targets.Count == 0)
        {
            return new NicWriteResult { AlreadyClean = true };
        }

        if (dryRun)
        {
            return new NicWriteResult
            {
                DryRun = true,
                Touched = targets.Select(n => n.Name).ToList(),
            };
        }

        List<string> touched = new();
        List<string> failures = new();

        foreach (NetworkInterfaceInfo nic in targets)
        {
            try
            {
                if (WriteFor(nic.Id, write))
                {
                    touched.Add(nic.Name);
                }
                else
                {
                    failures.Add($"{nic.Name}: no se encuentra su clave en el registro");
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException
                                          or global::System.Security.SecurityException)
            {
                // Estas dos claves son de maquina: sin elevacion no hay nada que hacer, y
                // decirlo es mas util que un fallo generico.
                failures.Add($"{nic.Name}: hace falta que el agente corra como servicio");
            }
        }

        // Se relee el estado entero, no lo que se acaba de escribir.
        List<string> remaining = new NetworkInterfaceProvider().GetInterfaces()
            .Where(n => n.IsUp && n.IsPhysical)
            .Where(n => !isFixed(n))
            .Select(n => n.Name)
            .ToList();

        return new NicWriteResult
        {
            Touched = touched,
            Failures = failures,
            Remaining = remaining,
        };
    }

    /// <summary>
    /// Escribe en la subclave del adaptador y, si hace falta, en la de su interfaz TCP/IP.
    ///
    /// Las dos se localizan por el mismo identificador —`NetCfgInstanceId` en una,
    /// el nombre de subclave en la otra— y esa correspondencia es la parte que no es obvia.
    /// </summary>
    private static bool WriteFor(string interfaceId, Action<RegistryKey, RegistryKey?> write)
    {
        using RegistryKey baseKey = RegistryKey.OpenBaseKey(
            RegistryHive.LocalMachine, RegistryView.Registry64);

        using RegistryKey? classKey = baseKey.OpenSubKey(NetworkClassKey);

        if (classKey is null)
        {
            return false;
        }

        foreach (string subKeyName in classKey.GetSubKeyNames())
        {
            using RegistryKey? adapter = classKey.OpenSubKey(subKeyName, writable: true);

            if (adapter?.GetValue("NetCfgInstanceId") as string != interfaceId)
            {
                continue;
            }

            using RegistryKey? tcpip = baseKey.OpenSubKey(
                $@"{TcpipInterfacesKey}\{interfaceId}", writable: true);

            write(adapter, tcpip);
            return true;
        }

        return false;
    }
}

/// <summary>Lo que ha pasado al escribir en los adaptadores.</summary>
public sealed record NicWriteResult
{
    public IReadOnlyList<string> Touched { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Failures { get; init; } = Array.Empty<string>();

    /// <summary>Los que siguen mal despues de escribir. Vacio es el resultado bueno.</summary>
    public IReadOnlyList<string> Remaining { get; init; } = Array.Empty<string>();

    public bool DryRun { get; init; }
    public bool AlreadyClean { get; init; }

    /// <summary>Exito es que no quede ninguno mal, no que las escrituras no hayan lanzado.</summary>
    public bool Success => DryRun || AlreadyClean || Remaining.Count == 0;
}
