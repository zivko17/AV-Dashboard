using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace AvDashboard.Native.Audio.Undocumented;

/// <summary>
/// IPolicyConfig: cambiar el dispositivo de audio por defecto. API no documentada.
/// Lee las reglas en el README de esta carpeta antes de tocar nada aqui.
/// </summary>
[ComImport]
[Guid("f8679f50-850a-41cf-9c72-430f290290c8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPolicyConfig
{
    // Los primeros metodos existen pero no los usamos; se declaran para mantener el
    // orden de la vtable, que es lo unico que importa en una interfaz COM.
    [PreserveSig] int GetMixFormat(IntPtr deviceName, IntPtr format);
    [PreserveSig] int GetDeviceFormat(IntPtr deviceName, int isDefault, IntPtr format);
    [PreserveSig] int ResetDeviceFormat(IntPtr deviceName);
    [PreserveSig] int SetDeviceFormat(IntPtr deviceName, IntPtr endpointFormat, IntPtr mixFormat);
    [PreserveSig] int GetProcessingPeriod(IntPtr deviceName, int isDefault, IntPtr defaultPeriod, IntPtr minimumPeriod);
    [PreserveSig] int SetProcessingPeriod(IntPtr deviceName, IntPtr period);
    [PreserveSig] int GetShareMode(IntPtr deviceName, IntPtr mode);
    [PreserveSig] int SetShareMode(IntPtr deviceName, IntPtr mode);
    [PreserveSig] int GetPropertyValue(IntPtr deviceName, int store, IntPtr key, IntPtr value);
    [PreserveSig] int SetPropertyValue(IntPtr deviceName, int store, IntPtr key, IntPtr value);

    /// <summary>El unico metodo que nos interesa.</summary>
    [PreserveSig]
    int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ERole role);

    [PreserveSig] int SetEndpointVisibility(IntPtr deviceName, int visible);
}

/// <summary>
/// Punto de entrada unico a las APIs de audio no documentadas. Detecta lo que hay y
/// devuelve capacidades apagadas si no reconoce la build, sin lanzar nunca.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class UndocumentedAudio
{
    // CLSID de CPolicyConfigClient. Estable desde Windows 7 hasta Windows 11 23H2,
    // pero no hay garantia contractual de que lo siga siendo.
    private static readonly Guid ClsidPolicyConfigClient =
        new("870af99c-171d-4f9e-af0d-e63df40c2bc9");

    private readonly Lazy<IPolicyConfig?> _policyConfig;

    public UndocumentedAudio()
    {
        _policyConfig = new Lazy<IPolicyConfig?>(TryCreatePolicyConfig);
    }

    /// <summary>true si se puede cambiar el dispositivo por defecto en esta maquina.</summary>
    public bool CanSetDefaultDevice => _policyConfig.Value is not null;

    /// <summary>
    /// Por que no esta disponible, para poder ensenarlo en la UI en lugar de un
    /// interruptor que no hace nada.
    /// </summary>
    public string UnavailableReason { get; private set; } =
        "No se ha intentado inicializar todavía.";

    /// <summary>
    /// Fija el dispositivo por defecto para los tres roles. Devuelve false y deja el
    /// motivo en <see cref="UnavailableReason"/> si no se ha podido.
    /// </summary>
    public bool TrySetDefaultDevice(string deviceId)
    {
        IPolicyConfig? config = _policyConfig.Value;
        if (config is null)
        {
            return false;
        }

        try
        {
            foreach (ERole role in new[] { ERole.EConsole, ERole.EMultimedia, ERole.ECommunications })
            {
                int hr = config.SetDefaultEndpoint(deviceId, role);
                if (hr != 0)
                {
                    UnavailableReason = $"SetDefaultEndpoint devolvió 0x{hr:X8} para el rol {role}.";
                    return false;
                }
            }

            return true;
        }
        catch (COMException ex)
        {
            UnavailableReason = $"COMException al fijar el dispositivo por defecto: 0x{ex.HResult:X8}.";
            return false;
        }
        catch (InvalidCastException)
        {
            UnavailableReason = "La vtable de IPolicyConfig no coincide en esta build de Windows.";
            return false;
        }
    }

    private IPolicyConfig? TryCreatePolicyConfig()
    {
        if (!Platform.IsWindows)
        {
            UnavailableReason = "No es Windows.";
            return null;
        }

        try
        {
            Type? type = Type.GetTypeFromCLSID(ClsidPolicyConfigClient);
            if (type is null)
            {
                UnavailableReason = "CPolicyConfigClient no está registrado en esta máquina.";
                return null;
            }

            object? instance = Activator.CreateInstance(type);
            if (instance is not IPolicyConfig config)
            {
                UnavailableReason = "CPolicyConfigClient no expone IPolicyConfig en esta build.";
                return null;
            }

            UnavailableReason = string.Empty;
            return config;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or NotSupportedException)
        {
            UnavailableReason = $"No se pudo crear IPolicyConfig: {ex.Message}";
            return null;
        }
    }
}
