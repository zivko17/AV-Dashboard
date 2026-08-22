using System.Runtime.InteropServices;

namespace AvDashboard.Native.Video;

/// <summary>
/// Declaraciones de la API de configuracion de display (QueryDisplayConfig).
///
/// Es la sucesora de EnumDisplayDevices y da tres cosas que la vieja no da: el conector
/// fisico por el que sale cada senal, el nombre real del monitor —el que viene en el
/// EDID, no «Generic PnP Monitor»— y el refresco como fraccion exacta, que es lo unico
/// que distingue un 59,94 de un 60.
/// </summary>
internal static class DisplayConfigInterop
{
    internal const uint QdcAllPaths = 1;
    internal const uint QdcOnlyActivePaths = 2;

    internal const int ErrorSuccess = 0;
    internal const int ErrorInsufficientBuffer = 122;

    // DISPLAYCONFIG_DEVICE_INFO_TYPE
    internal const int GetSourceName = 1;
    internal const int GetTargetName = 2;
    internal const int GetAdvancedColorInfo = 9;

    [DllImport("user32.dll")]
    internal static extern int GetDisplayConfigBufferSizes(
        uint flags, out uint pathCount, out uint modeCount);

    [DllImport("user32.dll")]
    internal static extern int QueryDisplayConfig(
        uint flags,
        ref uint pathCount,
        [Out] DisplayConfigPathInfo[] paths,
        ref uint modeCount,
        [Out] DisplayConfigModeInfo[] modes,
        IntPtr currentTopologyId);

    [DllImport("user32.dll")]
    internal static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigTargetDeviceName request);

    [DllImport("user32.dll")]
    internal static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigSourceDeviceName request);

    [DllImport("user32.dll")]
    internal static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigAdvancedColorInfo request);

    /// <summary>Nombre legible del conector fisico.</summary>
    internal static string DescribeOutputTechnology(int technology) => technology switch
    {
        -1 => "Otro",
        0 => "VGA",
        1 => "S-Video",
        2 => "Vídeo compuesto",
        3 => "Vídeo por componentes",
        4 => "DVI",
        5 => "HDMI",
        6 => "LVDS",
        8 => "D-Jpn",
        9 => "SDI",
        10 => "DisplayPort",
        11 => "DisplayPort (embebido)",
        12 => "UDI",
        13 => "UDI (embebido)",
        14 => "SDTV dongle",
        15 => "Miracast",
        16 => "Cableado indirecto",
        17 => "Virtual indirecto",
        unchecked((int)0x80000000) => "Panel interno",
        _ => $"Desconocido ({technology})",
    };
}

[StructLayout(LayoutKind.Sequential)]
internal struct Luid
{
    public uint LowPart;
    public int HighPart;

    public readonly long Value => ((long)HighPart << 32) | LowPart;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigRational
{
    public uint Numerator;
    public uint Denominator;

    /// <summary>
    /// El valor como decimal. Es una fraccion y no un entero justamente para poder
    /// expresar 60000/1001 = 59,94 sin redondear.
    /// </summary>
    public readonly double AsDouble => Denominator == 0 ? 0 : Numerator / (double)Denominator;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfig2DRegion
{
    public uint Cx;
    public uint Cy;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigPathSourceInfo
{
    public Luid AdapterId;
    public uint Id;
    public uint ModeInfoIdx;
    public uint StatusFlags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigPathTargetInfo
{
    public Luid AdapterId;
    public uint Id;
    public uint ModeInfoIdx;
    public int OutputTechnology;
    public uint Rotation;
    public uint Scaling;
    public DisplayConfigRational RefreshRate;
    public uint ScanLineOrdering;

    /// <summary>BOOL de Win32. Se guarda como entero para que el struct siga siendo
    /// blittable y el array de rutas no tenga que marshalarse elemento a elemento.</summary>
    public int TargetAvailableRaw;

    public uint StatusFlags;

    public readonly bool TargetAvailable => TargetAvailableRaw != 0;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigPathInfo
{
    public DisplayConfigPathSourceInfo SourceInfo;
    public DisplayConfigPathTargetInfo TargetInfo;
    public uint Flags;

    /// <summary>DISPLAYCONFIG_PATH_ACTIVE.</summary>
    public readonly bool IsActive => (Flags & 0x1) != 0;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigVideoSignalInfo
{
    public ulong PixelRate;
    public DisplayConfigRational HSyncFreq;
    public DisplayConfigRational VSyncFreq;
    public DisplayConfig2DRegion ActiveSize;
    public DisplayConfig2DRegion TotalSize;
    public uint VideoStandard;
    public uint ScanLineOrdering;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigTargetMode
{
    public DisplayConfigVideoSignalInfo TargetVideoSignalInfo;
}

[StructLayout(LayoutKind.Sequential)]
internal struct PointL
{
    public int X;
    public int Y;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigSourceMode
{
    public uint Width;
    public uint Height;
    public uint PixelFormat;
    public PointL Position;
}

/// <summary>
/// DISPLAYCONFIG_MODE_INFO. Los tres modos son una union en C, asi que aqui van
/// solapados en el mismo desplazamiento con LayoutKind.Explicit.
/// </summary>
[StructLayout(LayoutKind.Explicit)]
internal struct DisplayConfigModeInfo
{
    [FieldOffset(0)] public uint InfoType;
    [FieldOffset(4)] public uint Id;
    [FieldOffset(8)] public Luid AdapterId;
    [FieldOffset(16)] public DisplayConfigTargetMode TargetMode;
    [FieldOffset(16)] public DisplayConfigSourceMode SourceMode;

    internal const uint InfoTypeSource = 1;
    internal const uint InfoTypeTarget = 2;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigDeviceInfoHeader
{
    public int Type;
    public int Size;
    public Luid AdapterId;
    public uint Id;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct DisplayConfigTargetDeviceName
{
    public DisplayConfigDeviceInfoHeader Header;
    public uint Flags;
    public int OutputTechnology;
    public ushort EdidManufactureId;
    public ushort EdidProductCodeId;
    public uint ConnectorInstance;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
    public string MonitorFriendlyDeviceName;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    public string MonitorDevicePath;

    /// <summary>bit 0: el nombre amigable viene del EDID y no de un valor por defecto.</summary>
    public readonly bool FriendlyNameFromEdid => (Flags & 0x1) != 0;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct DisplayConfigSourceDeviceName
{
    public DisplayConfigDeviceInfoHeader Header;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
    public string ViewGdiDeviceName;
}

/// <summary>Estado de color avanzado: es de donde sale si el HDR esta activo de verdad.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigAdvancedColorInfo
{
    public DisplayConfigDeviceInfoHeader Header;
    public uint Value;
    public uint ColorEncoding;
    public uint BitsPerColorChannel;

    public readonly bool AdvancedColorSupported => (Value & 0x1) != 0;
    public readonly bool AdvancedColorEnabled => (Value & 0x2) != 0;
    public readonly bool WideColorEnforced => (Value & 0x4) != 0;
}
