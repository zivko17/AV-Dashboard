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
/// Planes de energia via PowrProf (M1). Se usa la API y no `powercfg.exe`: lanzar un
/// proceso una vez por segundo no cabe en el presupuesto de CPU, y para escribir tampoco
/// hace falta —la API cubre todo lo que se necesita aqui, duplicar el esquema Ultimate
/// Performance incluido—.
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

    /// <summary>
    /// true si Ultimate Performance ya esta duplicado y se puede seleccionar.
    ///
    /// Buscar el GUID conocido no basta, y creerlo costo dejar siete esquemas basura en una
    /// maquina real. Al duplicar, Windows crea el esquema con un **GUID nuevo**: el conocido
    /// pertenece a la plantilla, que sigue oculta. Asi que la comprobacion nunca daba true,
    /// la fila seguia diciendo «off» y cada intento de arreglarla creaba otra copia.
    ///
    /// El duplicado si hereda el nombre de la plantilla, y ese nombre se puede leer aunque
    /// la plantilla este oculta. Comparar por ahi funciona en cualquier idioma sin llevar
    /// una tabla de traducciones: en una maquina en espanol los dos son «Maximo rendimiento»
    /// y no hay que saberlo de antemano.
    /// </summary>
    public static bool IsUltimatePerformanceAvailable()
    {
        IReadOnlyList<(Guid Guid, string? Name)> schemes = EnumerateSchemes();

        if (schemes.Any(s => s.Guid == UltimatePerformanceScheme))
        {
            return true;
        }

        string? template = GetSchemeName(UltimatePerformanceScheme);

        return template is not null
               && schemes.Any(s => string.Equals(s.Name, template, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Cambia el plan activo. Requiere elevacion.</summary>
    public static bool SetActiveScheme(Guid scheme)
    {
        Guid local = scheme;
        return PowerSetActiveScheme(IntPtr.Zero, ref local) == 0;
    }

    /// <summary>
    /// Duplica el esquema Ultimate Performance para que aparezca en la lista.
    ///
    /// Antes esto lanzaba `powercfg /duplicatescheme` con un comentario que decia que no
    /// habia API publica. Si la hay: PowerDuplicateScheme, documentada desde Windows 8. Y
    /// aunque el comando era fijo y no arbitrario, arrancar un proceso para esto tenia dos
    /// costes que la P/Invoke no tiene: no se sabe por que ha fallado —solo un codigo de
    /// salida— y en una maquina con la ejecucion de procesos vigilada por el antivirus del
    /// cliente, un servicio lanzando powercfg es justo lo que dispara la alerta.
    /// </summary>
    public static bool TryDuplicateUltimatePerformance()
    {
        if (IsUltimatePerformanceAvailable())
        {
            return true;
        }

        Guid source = UltimatePerformanceScheme;

        // El destino va nulo para que lo genere Windows. Pasarle el GUID conocido no vale:
        // es el de la propia plantilla que se esta duplicando, y la API responde 183,
        // ERROR_ALREADY_EXISTS. Quien evita las copias repetidas es la comprobacion de
        // arriba, que ahora si reconoce el duplicado.
        IntPtr created = IntPtr.Zero;

        try
        {
            if (PowerDuplicateScheme(IntPtr.Zero, ref source, ref created) != 0)
            {
                return false;
            }
        }
        finally
        {
            if (created != IntPtr.Zero)
            {
                LocalFree(created);
            }
        }

        // Y se comprueba en vez de creerselo. Devolver true porque la llamada no dio error
        // es lo que hizo que el Timeline anotara «off → on» de algo que no habia pasado, y
        // una escritura que miente es peor que una que falla: la que falla se ve.
        return IsUltimatePerformanceAvailable();
    }

    /// <summary>
    /// Enciende o apaga la hibernacion.
    ///
    /// No basta con poner HibernateEnabled a 0 en el registro: eso deja el hiberfil.sys
    /// ocupando tanto como la RAM y el inicio rapido puede seguir usandolo. SystemReserveHiberFile
    /// es lo que hace `powercfg /h off` por dentro, reserva o libera el fichero, y es una
    /// llamada normal de la API de energia.
    /// </summary>
    public static bool TrySetHibernation(bool enabled)
    {
        // BOOLEAN de la API son ocho bits, no los cuatro de un BOOL de Win32.
        byte[] input = { enabled ? (byte)1 : (byte)0 };

        return CallNtPowerInformation(
            SystemReserveHiberFile, input, (uint)input.Length, IntPtr.Zero, 0) == 0;
    }

    /// <summary>InformationLevel de CallNtPowerInformation para reservar o soltar el hiberfil.</summary>
    private const int SystemReserveHiberFile = 10;

    /// <summary>
    /// Valor de un ajuste del plan activo, con la maquina enchufada a la red.
    ///
    /// Se lee del plan activo y no de uno concreto porque la pregunta que interesa es
    /// «como esta esta maquina ahora», no «como estaria si aplicaras Ultimate».
    /// </summary>
    public static uint? ReadActiveAcValue(Guid subGroup, Guid setting)
    {
        Guid? scheme = GetActiveSchemeGuid();
        if (scheme is not Guid active)
        {
            return null;
        }

        Guid schemeGuid = active;
        Guid subGroupGuid = subGroup;
        Guid settingGuid = setting;

        return PowerReadACValueIndex(IntPtr.Zero, ref schemeGuid, ref subGroupGuid,
                   ref settingGuid, out uint value) == 0
            ? value
            : null;
    }

    /// <summary>
    /// Escribe un ajuste del plan activo para el perfil de corriente alterna.
    ///
    /// Despues de escribir hay que reactivar el esquema: Windows no aplica el cambio al
    /// plan en curso hasta que se le vuelve a decir cual es el activo. Sin esa segunda
    /// llamada, el valor queda guardado y no surte efecto, que es la peor de las dos
    /// opciones porque la interfaz diria que esta hecho.
    /// </summary>
    public static bool WriteActiveAcValue(Guid subGroup, Guid setting, uint value)
    {
        Guid? scheme = GetActiveSchemeGuid();
        if (scheme is not Guid active)
        {
            return false;
        }

        Guid schemeGuid = active;
        Guid subGroupGuid = subGroup;
        Guid settingGuid = setting;

        if (PowerWriteACValueIndex(IntPtr.Zero, ref schemeGuid, ref subGroupGuid,
                ref settingGuid, value) != 0)
        {
            return false;
        }

        return SetActiveScheme(active);
    }

    /// <summary>
    /// GUID de subgrupo y de ajuste de los que mira el catalogo. Son fijos y estan
    /// documentados; van aqui juntos para no repartirlos por el codigo.
    /// </summary>
    public static class Settings
    {
        public static readonly Guid ProcessorSubGroup = new("54533251-82be-4824-96c1-47b60b740d00");
        public static readonly Guid ProcessorMin = new("893dee8e-2bef-41e0-89c6-b55d0929964c");
        public static readonly Guid ProcessorMax = new("bc5038f7-23e0-4960-96da-33abaf5935ec");

        /// <summary>Minimo de nucleos sin aparcar, en porcentaje. 100 = no se aparca ninguno.</summary>
        public static readonly Guid CoreParkingMinCores = new("0cc5b647-c1df-4637-891a-dec35c318583");

        public static readonly Guid UsbSubGroup = new("2a737441-1930-4402-8d77-b2bebba308a3");
        public static readonly Guid UsbSelectiveSuspend = new("48e6b7a6-50f5-4782-a5d4-53bb8f07e226");

        public static readonly Guid PcieSubGroup = new("501a4d13-42af-4429-9fd1-a8218c268e20");
        public static readonly Guid PcieAspm = new("ee12f906-d277-404b-b6da-e5fa1a576df5");

        public static readonly Guid DiskSubGroup = new("0012ee47-9041-4b5d-9b77-535fba8b1442");
        public static readonly Guid DiskTimeout = new("6738e2c4-e8a5-4a42-b16a-e040e769756e");

        public static readonly Guid SleepSubGroup = new("238c9fa8-0aad-41ed-83f4-97be242c8f20");
        public static readonly Guid StandbyTimeout = new("29f6c1db-86da-48c5-9fdb-f2b67b1f44da");
        public static readonly Guid WakeTimers = new("bd3b718a-0680-4d9d-8ab2-e1d2b4ac806d");

        public static readonly Guid VideoSubGroup = new("7516b95f-f776-4464-8c53-06167f40cc99");
        public static readonly Guid DisplayTimeout = new("3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e");
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

    [DllImport("powrprof.dll")]
    private static extern uint PowerWriteACValueIndex(
        IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subGroupOfPowerSettingsGuid,
        ref Guid powerSettingGuid, uint acValueIndex);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadACValueIndex(
        IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subGroupOfPowerSettingsGuid,
        ref Guid powerSettingGuid, out uint acValueIndex);

    /// <summary>
    /// El destino es un puntero a puntero: si apunta a nulo, la API reserva el GUID y lo
    /// devuelve, y hay que soltarlo con LocalFree. Darle el GUID conocido no funciona, ver
    /// la nota de TryDuplicateUltimatePerformance.
    /// </summary>
    [DllImport("powrprof.dll")]
    private static extern uint PowerDuplicateScheme(
        IntPtr rootPowerKey, ref Guid sourceSchemeGuid, ref IntPtr destinationSchemeGuid);

    [DllImport("powrprof.dll")]
    private static extern uint CallNtPowerInformation(
        int informationLevel, byte[] inputBuffer, uint inputBufferLength,
        IntPtr outputBuffer, uint outputBufferLength);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr handle);
}
