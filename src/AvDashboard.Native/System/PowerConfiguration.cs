using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Win32Exception = System.ComponentModel.Win32Exception;

// Ojo: este namespace se llama `System`. Dentro de el, el identificador `System`
// resuelve a AvDashboard.Native.System, no al del framework, asi que los tipos del BCL
// se traen con `using` arriba del fichero (que si resuelve en el ambito global) o con
// un alias, como el Win32Exception de aqui. Si te falla un `System.Algo`, es esto.
namespace AvDashboard.Native.System;

/// <summary>
/// Planes de energia via PowrProf (M1). Se usa la API y no `powercfg.exe` para leer:
/// lanzar un proceso una vez por segundo no cabe en el presupuesto de CPU.
/// La duplicacion del esquema Ultimate Performance si usa powercfg, porque no hay
/// equivalente en la API publica.
/// </summary>
[SupportedOSPlatform("windows")]
public static class PowerConfiguration
{
    /// <summary>GUID del esquema Ultimate Performance. Es fijo y esta documentado por Microsoft.</summary>
    public static readonly Guid UltimatePerformanceScheme =
        new("e9a42b02-d5df-448d-aa00-03f14749eb61");

    public static readonly Guid HighPerformanceScheme =
        new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");

    public static readonly Guid BalancedScheme =
        new("381b4222-f694-41f0-9685-ff5bb260df2e");

    /// <summary>GUID del plan activo, o null si no se pudo leer.</summary>
    public static Guid? GetActiveSchemeGuid()
    {
        IntPtr pointer = IntPtr.Zero;

        try
        {
            if (PowerGetActiveScheme(IntPtr.Zero, out pointer) != 0 || pointer == IntPtr.Zero)
            {
                return null;
            }

            return Marshal.PtrToStructure<Guid>(pointer);
        }
        finally
        {
            if (pointer != IntPtr.Zero)
            {
                LocalFree(pointer);
            }
        }
    }

    /// <summary>Nombre amigable de un plan de energia.</summary>
    public static string? GetSchemeName(Guid scheme)
    {
        uint size = 0;
        Guid local = scheme;

        // Primera llamada para conocer el tamano; asi funciona toda esta familia de APIs.
        PowerReadFriendlyName(IntPtr.Zero, ref local, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, ref size);

        if (size == 0)
        {
            return null;
        }

        IntPtr buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (PowerReadFriendlyName(IntPtr.Zero, ref local, IntPtr.Zero, IntPtr.Zero, buffer, ref size) != 0)
            {
                return null;
            }

            return Marshal.PtrToStringUni(buffer);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>Todos los planes disponibles, en orden de enumeracion de Windows.</summary>
    public static IReadOnlyList<(Guid Guid, string? Name)> EnumerateSchemes()
    {
        List<(Guid, string?)> schemes = new();
        uint index = 0;

        while (true)
        {
            uint bufferSize = (uint)Marshal.SizeOf<Guid>();
            byte[] buffer = new byte[bufferSize];

            uint result = PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                AccessSchemeIndex, index, buffer, ref bufferSize);

            if (result != 0)
            {
                break;
            }

            Guid scheme = new(buffer);
            schemes.Add((scheme, GetSchemeName(scheme)));
            index++;

            if (index > 64)
            {
                // Guardarrail: ninguna maquina real tiene 64 planes de energia.
                break;
            }
        }

        return schemes;
    }

    /// <summary>true si Ultimate Performance ya esta duplicado y se puede seleccionar.</summary>
    public static bool IsUltimatePerformanceAvailable() =>
        EnumerateSchemes().Any(s => s.Guid == UltimatePerformanceScheme);

    /// <summary>Cambia el plan activo. Requiere elevacion.</summary>
    public static bool SetActiveScheme(Guid scheme)
    {
        Guid local = scheme;
        return PowerSetActiveScheme(IntPtr.Zero, ref local) == 0;
    }

    /// <summary>
    /// Duplica el esquema Ultimate Performance para que aparezca en la lista.
    /// No hay API publica para esto, asi que aqui si se invoca powercfg, una sola vez.
    /// </summary>
    public static bool TryDuplicateUltimatePerformance()
    {
        if (IsUltimatePerformanceAvailable())
        {
            return true;
        }

        ProcessStartInfo info = new()
        {
            FileName = "powercfg.exe",
            Arguments = $"/duplicatescheme {UltimatePerformanceScheme:D}",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        try
        {
            using Process? process = Process.Start(info);
            if (process is null)
            {
                return false;
            }

            process.WaitForExit(10_000);
            return process.ExitCode == 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            return false;
        }
    }

    private const uint AccessSchemeIndex = 16;

    [DllImport("powrprof.dll")]
    private static extern uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);

    [DllImport("powrprof.dll")]
    private static extern uint PowerSetActiveScheme(IntPtr userRootPowerKey, ref Guid schemeGuid);

    [DllImport("powrprof.dll", CharSet = CharSet.Unicode)]
    private static extern uint PowerReadFriendlyName(
        IntPtr rootPowerKey, ref Guid schemeGuid, IntPtr subGroupOfPowerSettingsGuid,
        IntPtr powerSettingGuid, IntPtr buffer, ref uint bufferSize);

    [DllImport("powrprof.dll")]
    private static extern uint PowerEnumerate(
        IntPtr rootPowerKey, IntPtr schemeGuid, IntPtr subGroupOfPowerSettingsGuid,
        uint accessFlags, uint index, byte[] buffer, ref uint bufferSize);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr handle);
}
