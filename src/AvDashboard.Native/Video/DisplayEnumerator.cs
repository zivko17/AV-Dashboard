using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using AvDashboard.Core.Models;

namespace AvDashboard.Native.Video;

/// <summary>
/// Enumeracion de displays con la API clasica de GDI (M4, lectura basica).
///
/// La topologia fina —conector fisico, EDID crudo, HDR— necesita QueryDisplayConfig y
/// NVAPI, que son la Fase 4. Esto da ya lo que resuelve el caso mas frecuente: cuantas
/// salidas hay, a que resolucion y refresh, y donde estan colocadas. Con eso se detecta
/// «ha desaparecido un display», que en una pared LED significa que has perdido la
/// pared y todavia no lo sabes.
/// </summary>
[SupportedOSPlatform("windows")]
public static class DisplayEnumerator
{
    public static DisplayTopology GetTopology()
    {
        List<DisplayOutput> outputs = new();
        double? compositionRate = TryGetCompositionRefreshHz();

        DisplayDevice adapter = new() { Size = Marshal.SizeOf<DisplayDevice>() };

        for (uint adapterIndex = 0; EnumDisplayDevices(null, adapterIndex, ref adapter, 0); adapterIndex++)
        {
            bool attached = (adapter.StateFlags & DisplayDeviceAttachedToDesktop) != 0;
            bool primary = (adapter.StateFlags & DisplayDevicePrimaryDevice) != 0;

            if (!attached)
            {
                adapter = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>() };
                continue;
            }

            string deviceName = adapter.DeviceName;
            string? monitorName = ReadMonitorName(deviceName);

            DevMode mode = new() { Size = (ushort)Marshal.SizeOf<DevMode>() };
            bool hasMode = EnumDisplaySettings(deviceName, EnumCurrentSettings, ref mode);

            outputs.Add(new DisplayOutput
            {
                DeviceName = deviceName,
                MonitorName = monitorName,
                IsActive = true,
                IsPrimary = primary,
                Width = hasMode ? mode.PelsWidth : 0,
                Height = hasMode ? mode.PelsHeight : 0,
                PositionX = hasMode ? mode.PositionX : 0,
                PositionY = hasMode ? mode.PositionY : 0,
                RefreshHz = hasMode ? mode.DisplayFrequency : 0,
                MeasuredRefreshHz = primary ? compositionRate : null,
            });

            adapter = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>() };
        }

        return new DisplayTopology { Outputs = outputs };
    }

    /// <summary>
    /// Refresh real de la composicion del escritorio segun DWM.
    ///
    /// Importante y a menudo malentendido: esto es el ritmo del compositor, no el de
    /// cada salida por separado. Sirve para detectar la desviacion entre 50 y 59.94 en
    /// la salida principal; la medida por salida necesita NVAPI y llega en la Fase 4.
    /// </summary>
    public static double? TryGetCompositionRefreshHz()
    {
        DwmTimingInfo info = new() { Size = Marshal.SizeOf<DwmTimingInfo>() };

        if (DwmGetCompositionTimingInfo(IntPtr.Zero, ref info) != 0)
        {
            return null;
        }

        if (info.RateCompose.Denominator == 0)
        {
            return null;
        }

        return (double)info.RateCompose.Numerator / info.RateCompose.Denominator;
    }

    private static string? ReadMonitorName(string adapterDeviceName)
    {
        DisplayDevice monitor = new() { Size = Marshal.SizeOf<DisplayDevice>() };
        return EnumDisplayDevices(adapterDeviceName, 0, ref monitor, 0)
            ? monitor.DeviceString
            : null;
    }

    // --- Interop ---

    private const int EnumCurrentSettings = -1;
    private const int DisplayDeviceAttachedToDesktop = 0x00000001;
    private const int DisplayDevicePrimaryDevice = 0x00000004;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public int Size;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;

        public int StateFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceId;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;

        public ushort SpecVersion;
        public ushort DriverVersion;
        public ushort Size;
        public ushort DriverExtra;
        public uint Fields;

        // Union POINTL para modos de display: posicion del escritorio virtual.
        public int PositionX;
        public int PositionY;
        public uint DisplayOrientation;
        public uint DisplayFixedOutput;

        public short Color;
        public short Duplex;
        public short YResolution;
        public short TrueTypeOption;
        public short Collate;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string FormName;

        public ushort LogPixels;
        public uint BitsPerPel;
        public int PelsWidth;
        public int PelsHeight;
        public uint DisplayFlags;
        public int DisplayFrequency;
        public uint ICMMethod;
        public uint ICMIntent;
        public uint MediaType;
        public uint DitherType;
        public uint Reserved1;
        public uint Reserved2;
        public uint PanningWidth;
        public uint PanningHeight;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UnsignedRatio
    {
        public uint Numerator;
        public uint Denominator;
    }

    /// <summary>
    /// DWM_TIMING_INFO. Solo se leen los primeros campos hasta rateCompose; el resto
    /// del struct se declara como relleno para que el tamano cuadre, que es lo unico
    /// que comprueba la API.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct DwmTimingInfo
    {
        public int Size;
        public UnsignedRatio RateRefresh;
        public ulong QpcRefreshPeriod;
        public UnsignedRatio RateCompose;
        public ulong QpcVBlank;
        public ulong CRefresh;
        public uint CDXRefresh;
        public ulong QpcCompose;
        public ulong CFrame;
        public uint CDXPresent;
        public ulong CRefreshFrame;
        public ulong CFrameSubmitted;
        public uint CDXPresentSubmitted;
        public ulong CFrameConfirmed;
        public uint CDXPresentConfirmed;
        public ulong CRefreshConfirmed;
        public uint CDXRefreshConfirmed;
        public uint CFramesLate;
        public uint CFramesOutstanding;
        public ulong CFrameDisplayed;
        public ulong QpcFrameDisplayed;
        public ulong CRefreshFrameDisplayed;
        public ulong CFrameComplete;
        public ulong QpcFrameComplete;
        public ulong CFramePending;
        public ulong QpcFramePending;
        public ulong CFramesDisplayed;
        public ulong CFramesComplete;
        public ulong CFramesPending;
        public ulong CFramesAvailable;
        public ulong CFramesDropped;
        public ulong CFramesMissed;
        public ulong CRefreshNextDisplayed;
        public ulong CRefreshNextPresented;
        public ulong CRefreshesDisplayed;
        public ulong CRefreshesPresented;
        public ulong CRefreshStarted;
        public ulong CPixelsReceived;
        public ulong CPixelsDrawn;
        public ulong CBuffersEmpty;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayDevices(
        string? device, uint deviceIndex, ref DisplayDevice displayDevice, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplaySettings(
        string deviceName, int modeNum, ref DevMode devMode);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetCompositionTimingInfo(IntPtr hwnd, ref DwmTimingInfo timingInfo);
}
