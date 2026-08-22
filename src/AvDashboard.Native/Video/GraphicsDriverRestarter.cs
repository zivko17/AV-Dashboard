using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace AvDashboard.Native.Video;

/// <summary>Resultado de intentar reiniciar el controlador gráfico.</summary>
public sealed record GraphicsRestartResult
{
    public required bool Success { get; init; }
    public required string Message { get; init; }

    /// <summary>Adaptadores sobre los que se ha actuado.</summary>
    public IReadOnlyList<string> Adapters { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Reinicia el controlador gráfico sin reiniciar la máquina (M15).
///
/// Es lo que hace el Administrador de dispositivos con «Deshabilitar y volver a
/// habilitar», y lo mismo que `pnputil /restart-device`, pero por la API en vez de
/// lanzando un proceso: la lista blanca de actuadores compilados no admite ejecutar
/// binarios arbitrarios, y esto tiene que seguir siendo un actuador y no una puerta.
///
/// Frente a `Win+Ctrl+Shift+B`, que es un atajo del shell y solo funciona con una sesión
/// interactiva en primer plano, esto se puede disparar desde el agente aunque la consola
/// esté en otra máquina. Es justo el caso: la pared se ha quedado congelada y tú estás en
/// FOH con el portátil.
///
/// Advertencia que la interfaz debe repetir: durante el reinicio las salidas se quedan
/// negras uno o dos segundos y Windows puede recolocar las ventanas. Se pulsa cuando algo
/// ya está roto, no como medida preventiva.
/// </summary>
[SupportedOSPlatform("windows")]
public static class GraphicsDriverRestarter
{
    /// <summary>GUID_DEVCLASS_DISPLAY: la clase de adaptadores de vídeo.</summary>
    private static readonly Guid DisplayClass = new("4d36e968-e325-11ce-bfc1-08002be10318");

    public static GraphicsRestartResult Restart()
    {
        Platform.EnsureWindows();

        Guid classGuid = DisplayClass;
        IntPtr set = SetupDiGetClassDevs(ref classGuid, IntPtr.Zero, IntPtr.Zero, DigcfPresent);

        if (set == InvalidHandle)
        {
            return new GraphicsRestartResult
            {
                Success = false,
                Message = "No se ha podido enumerar los adaptadores de vídeo.",
            };
        }

        try
        {
            List<string> restarted = new();
            List<string> failed = new();

            SpDevInfoData device = new() { CbSize = (uint)Marshal.SizeOf<SpDevInfoData>() };

            for (uint index = 0; SetupDiEnumDeviceInfo(set, index, ref device); index++)
            {
                string name = DescribeDevice(set, ref device) ?? $"adaptador {index}";

                if (TryRestart(set, ref device))
                {
                    restarted.Add(name);
                }
                else
                {
                    failed.Add(name);
                }

                device = new SpDevInfoData { CbSize = (uint)Marshal.SizeOf<SpDevInfoData>() };
            }

            if (restarted.Count == 0)
            {
                return new GraphicsRestartResult
                {
                    Success = false,
                    Message = failed.Count == 0
                        ? "No se ha encontrado ningún adaptador de vídeo."
                        : "No se ha podido reiniciar ningún adaptador. Hace falta ejecutar el " +
                          "agente como servicio: reiniciar un dispositivo exige privilegios.",
                    Adapters = failed,
                };
            }

            return new GraphicsRestartResult
            {
                Success = true,
                Message = failed.Count == 0
                    ? $"Reiniciados {restarted.Count} adaptadores."
                    : $"Reiniciados {restarted.Count}; {failed.Count} no han dejado.",
                Adapters = restarted,
            };
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }
    }

    /// <summary>
    /// DIF_PROPERTYCHANGE con DICS_PROPCHANGE: es el reinicio en caliente del dispositivo.
    ///
    /// Se usa esto y no un par deshabilitar/habilitar porque son dos operaciones y entre
    /// ellas la GPU se queda apagada: si la segunda falla, te has quedado sin salida de
    /// vídeo. El reinicio es una sola llamada, y si falla no ha cambiado nada.
    /// </summary>
    private static bool TryRestart(IntPtr set, ref SpDevInfoData device)
    {
        SpPropChangeParams parameters = new()
        {
            ClassInstallHeader = new SpClassInstallHeader
            {
                CbSize = (uint)Marshal.SizeOf<SpClassInstallHeader>(),
                InstallFunction = DifPropertyChange,
            },
            StateChange = DicsPropChange,
            Scope = DicsFlagConfigSpecific,
            HwProfile = 0,
        };

        int size = Marshal.SizeOf<SpPropChangeParams>();
        IntPtr buffer = Marshal.AllocHGlobal(size);

        try
        {
            Marshal.StructureToPtr(parameters, buffer, fDeleteOld: false);

            if (!SetupDiSetClassInstallParams(set, ref device, buffer, (uint)size))
            {
                return false;
            }

            return SetupDiCallClassInstaller(DifPropertyChange, set, ref device);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static string? DescribeDevice(IntPtr set, ref SpDevInfoData device)
    {
        // Primera llamada para el tamano; asi funciona toda esta familia de APIs.
        SetupDiGetDeviceRegistryProperty(
            set, ref device, SpdrpDeviceDesc, out _, null, 0, out uint required);

        if (required == 0)
        {
            return null;
        }

        byte[] buffer = new byte[required];

        return SetupDiGetDeviceRegistryProperty(
                   set, ref device, SpdrpDeviceDesc, out _, buffer, required, out _)
            ? global::System.Text.Encoding.Unicode.GetString(buffer).TrimEnd('\0')
            : null;
    }

    // --- Constantes de SetupAPI ---

    private const uint DigcfPresent = 0x00000002;
    private const uint DifPropertyChange = 0x00000012;
    private const uint DicsPropChange = 0x00000003;
    private const uint DicsFlagConfigSpecific = 0x00000002;
    private const uint SpdrpDeviceDesc = 0x00000000;

    private static readonly IntPtr InvalidHandle = new(-1);

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDevInfoData
    {
        public uint CbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpClassInstallHeader
    {
        public uint CbSize;
        public uint InstallFunction;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpPropChangeParams
    {
        public SpClassInstallHeader ClassInstallHeader;
        public uint StateChange;
        public uint Scope;
        public uint HwProfile;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(
        ref Guid classGuid, IntPtr enumerator, IntPtr parent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiEnumDeviceInfo(
        IntPtr deviceInfoSet, uint memberIndex, ref SpDevInfoData deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiSetClassInstallParams(
        IntPtr deviceInfoSet, ref SpDevInfoData deviceInfoData,
        IntPtr classInstallParams, uint classInstallParamsSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiCallClassInstaller(
        uint installFunction, IntPtr deviceInfoSet, ref SpDevInfoData deviceInfoData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceRegistryProperty(
        IntPtr deviceInfoSet, ref SpDevInfoData deviceInfoData, uint property,
        out uint propertyRegDataType, byte[]? propertyBuffer, uint propertyBufferSize,
        out uint requiredSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);
}
