using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace AvDashboard.Native.Audio;

/// <summary>
/// Declaraciones de Core Audio (MMDevice API). Todo lo de este fichero esta documentado
/// por Microsoft: las APIs no documentadas viven aparte, en Audio.Undocumented.
/// </summary>
internal static class CoreAudioInterop
{
    internal static readonly Guid ClsidMMDeviceEnumerator =
        new("BCDE0395-E52F-467C-8E3D-C4579291692E");

    internal const int DeviceStateActive = 0x00000001;
    internal const int DeviceStateDisabled = 0x00000002;
    internal const int DeviceStateNotPresent = 0x00000004;
    internal const int DeviceStateUnplugged = 0x00000008;
    internal const int DeviceStateAll = 0x0000000F;

    internal const uint ClsCtxAll = 0x17;
    internal const uint StgmRead = 0x00000000;

    // PKEY_Device_FriendlyName y PKEY_Device_DeviceDesc comparten fmtid.
    internal static readonly Guid PkeyDeviceFmtId = new("a45c254e-df1c-4efd-8020-67d146a850e0");
    internal const int PidDeviceFriendlyName = 14;
    internal const int PidDeviceDescription = 2;

    // PKEY_AudioEngine_DeviceFormat: el formato que el usuario ha fijado en el panel de
    // sonido. Comparado con el mix format real es como se detecta el mismatch.
    internal static readonly Guid PkeyAudioEngineFmtId = new("f19f064d-082c-4e27-bc73-6882a1bb8e4c");
    internal const int PidAudioEngineDeviceFormat = 0;

    internal const ushort VtEmpty = 0;
    internal const ushort VtLpwstr = 31;
    internal const ushort VtBlob = 65;
    internal const ushort VtUi4 = 19;
}

internal enum EDataFlow
{
    ERender = 0,
    ECapture = 1,
    EAll = 2,
}

internal enum ERole
{
    EConsole = 0,
    EMultimedia = 1,
    ECommunications = 2,
}

[ComImport]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    [PreserveSig]
    int EnumAudioEndpoints(EDataFlow dataFlow, int stateMask, out IMMDeviceCollection devices);

    [PreserveSig]
    int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice device);

    [PreserveSig]
    int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);

    [PreserveSig]
    int RegisterEndpointNotificationCallback(IntPtr client);

    [PreserveSig]
    int UnregisterEndpointNotificationCallback(IntPtr client);
}

[ComImport]
[Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceCollection
{
    [PreserveSig]
    int GetCount(out int count);

    [PreserveSig]
    int Item(int index, out IMMDevice device);
}

[ComImport]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    [PreserveSig]
    int Activate(ref Guid iid, uint clsCtx, IntPtr activationParams,
        [MarshalAs(UnmanagedType.IUnknown)] out object instance);

    [PreserveSig]
    int OpenPropertyStore(uint access, out IPropertyStore properties);

    [PreserveSig]
    int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);

    [PreserveSig]
    int GetState(out int state);
}

[ComImport]
[Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyStore
{
    [PreserveSig]
    int GetCount(out int count);

    [PreserveSig]
    int GetAt(int index, out PropertyKey key);

    [PreserveSig]
    int GetValue(ref PropertyKey key, out PropVariant value);

    [PreserveSig]
    int SetValue(ref PropertyKey key, ref PropVariant value);

    [PreserveSig]
    int Commit();
}

[ComImport]
[Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioMeterInformation
{
    [PreserveSig]
    int GetPeakValue(out float peak);

    [PreserveSig]
    int GetMeteringChannelCount(out int count);

    [PreserveSig]
    int GetChannelsPeakValues(int channelCount, [Out] float[] peaks);

    [PreserveSig]
    int QueryHardwareSupport(out int response);
}

[ComImport]
[Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioEndpointVolume
{
    [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
    [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
    [PreserveSig] int GetChannelCount(out int count);
    [PreserveSig] int SetMasterVolumeLevel(float level, ref Guid eventContext);
    [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid eventContext);
    [PreserveSig] int GetMasterVolumeLevel(out float level);
    [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
    [PreserveSig] int SetChannelVolumeLevel(int channel, float level, ref Guid eventContext);
    [PreserveSig] int SetChannelVolumeLevelScalar(int channel, float level, ref Guid eventContext);
    [PreserveSig] int GetChannelVolumeLevel(int channel, out float level);
    [PreserveSig] int GetChannelVolumeLevelScalar(int channel, out float level);
    [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid eventContext);
    [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
}

[ComImport]
[Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioClient
{
    [PreserveSig]
    int Initialize(int shareMode, int streamFlags, long bufferDuration, long periodicity,
        IntPtr format, IntPtr audioSessionGuid);

    [PreserveSig] int GetBufferSize(out uint frames);
    [PreserveSig] int GetStreamLatency(out long latency);
    [PreserveSig] int GetCurrentPadding(out uint padding);

    [PreserveSig]
    int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closestMatch);

    /// <summary>Formato con el que el motor de Windows esta mezclando ahora mismo.</summary>
    [PreserveSig]
    int GetMixFormat(out IntPtr format);

    [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
    [PreserveSig] int Start();
    [PreserveSig] int Stop();
    [PreserveSig] int Reset();
    [PreserveSig] int SetEventHandle(IntPtr handle);
    [PreserveSig] int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct PropertyKey
{
    public Guid FormatId;
    public int PropertyId;

    public PropertyKey(Guid formatId, int propertyId)
    {
        FormatId = formatId;
        PropertyId = propertyId;
    }
}

/// <summary>
/// PROPVARIANT con layout explicito para x64. En x86 los desplazamientos de la union
/// son distintos; el proyecto declara x64 como unica plataforma y
/// <see cref="Platform.IsX64"/> lo comprueba en caliente antes de usarlo.
/// </summary>
[StructLayout(LayoutKind.Explicit)]
internal struct PropVariant
{
    [FieldOffset(0)] public ushort VarType;
    [FieldOffset(2)] public ushort Reserved1;
    [FieldOffset(4)] public ushort Reserved2;
    [FieldOffset(6)] public ushort Reserved3;

    [FieldOffset(8)] public IntPtr PointerValue;
    [FieldOffset(8)] public uint UIntValue;
    [FieldOffset(8)] public int IntValue;

    /// <summary>Para VT_BLOB: tamano en bytes. El puntero al dato va en <see cref="BlobData"/>.</summary>
    [FieldOffset(8)] public uint BlobSize;

    [FieldOffset(16)] public IntPtr BlobData;

    public readonly string? AsString() =>
        VarType == CoreAudioInterop.VtLpwstr && PointerValue != IntPtr.Zero
            ? Marshal.PtrToStringUni(PointerValue)
            : null;

    /// <summary>Copia el blob a memoria gestionada. Devuelve null si el tipo no es VT_BLOB.</summary>
    public readonly byte[]? AsBlob()
    {
        if (VarType != CoreAudioInterop.VtBlob || BlobData == IntPtr.Zero || BlobSize == 0)
        {
            return null;
        }

        byte[] buffer = new byte[BlobSize];
        Marshal.Copy(BlobData, buffer, 0, (int)BlobSize);
        return buffer;
    }
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct WaveFormatEx
{
    public ushort FormatTag;
    public ushort Channels;
    public uint SamplesPerSec;
    public uint AvgBytesPerSec;
    public ushort BlockAlign;
    public ushort BitsPerSample;
    public ushort ExtraSize;
}

[SupportedOSPlatform("windows")]
internal static class Ole32
{
    [DllImport("ole32.dll")]
    internal static extern int PropVariantClear(ref PropVariant pvar);

    [DllImport("ole32.dll")]
    internal static extern void CoTaskMemFree(IntPtr ptr);
}
