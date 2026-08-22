using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using AvDashboard.Core.Models;
using AvDashboard.Core.Video;

namespace AvDashboard.Native.Video;

/// <summary>
/// Topologia de salida de vídeo via QueryDisplayConfig (M4).
///
/// Sustituye a <see cref="DisplayEnumerator"/>, que se queda como respaldo: la API vieja
/// no sabe por que conector sale cada senal, devuelve «Generic PnP Monitor» en lugar del
/// nombre real, y redondea el refresco a entero, con lo que un 59,94 y un 60 se ven
/// iguales. Los tres datos son exactamente los que hacen falta cuando un procesador LED
/// no engancha.
/// </summary>
[SupportedOSPlatform("windows")]
public static class DisplayTopologyProvider
{
    public static DisplayTopology GetTopology()
    {
        Platform.EnsureWindows();

        if (!TryQuery(out DisplayConfigPathInfo[]? paths, out DisplayConfigModeInfo[]? modes))
        {
            // Si la API moderna falla —pasa en sesiones sin escritorio— se cae a la
            // clasica en vez de devolver una topologia vacia, que se leeria como
            // «has perdido todas las pantallas».
            return DisplayEnumerator.GetTopology();
        }

        List<DisplayOutput> outputs = new();

        foreach (DisplayConfigPathInfo path in paths!)
        {
            if (!path.IsActive)
            {
                continue;
            }

            DisplayConfigSourceMode? sourceMode = FindSourceMode(modes!, path);
            string? gdiName = QuerySourceName(path);
            DisplayConfigTargetDeviceName target = QueryTargetName(path, out bool targetOk);
            DisplayConfigAdvancedColorInfo color = QueryAdvancedColor(path, out bool colorOk);

            RawEdid? edid = targetOk ? EdidRegistryReader.TryReadForDevicePath(target.MonitorDevicePath) : null;
            Edid? parsedEdid = null;
            if (edid is not null)
            {
                EdidParser.TryParse(edid.Bytes, out parsedEdid, out _);
            }

            string? monitorName = targetOk && target.MonitorFriendlyDeviceName.Length > 0
                ? target.MonitorFriendlyDeviceName
                : parsedEdid?.MonitorName;

            uint numerator = path.TargetInfo.RefreshRate.Numerator;
            uint denominator = path.TargetInfo.RefreshRate.Denominator;

            outputs.Add(new DisplayOutput
            {
                DeviceName = gdiName ?? $"Target {path.TargetInfo.Id}",
                MonitorName = monitorName,
                PhysicalConnector = DisplayConfigInterop.DescribeOutputTechnology(
                    targetOk ? target.OutputTechnology : path.TargetInfo.OutputTechnology),

                IsActive = true,
                IsPrimary = sourceMode is { Position.X: 0, Position.Y: 0 },

                Width = (int)(sourceMode?.Width ?? 0),
                Height = (int)(sourceMode?.Height ?? 0),
                PositionX = sourceMode?.Position.X ?? 0,
                PositionY = sourceMode?.Position.Y ?? 0,

                RefreshHz = path.TargetInfo.RefreshRate.AsDouble,
                RefreshNumerator = (int)numerator,
                RefreshDenominator = (int)denominator,

                MonitorDevicePath = targetOk ? target.MonitorDevicePath : null,

                HdrSupported = colorOk && color.AdvancedColorSupported,
                HdrEnabled = colorOk && color.AdvancedColorEnabled,
                BitsPerColorChannel = colorOk && color.BitsPerColorChannel > 0
                    ? (int)color.BitsPerColorChannel
                    : null,

                EdidManufacturerId = parsedEdid?.ManufacturerId,
                EdidProductCode = parsedEdid?.ProductCode,
                EdidSerial = parsedEdid?.SerialText ?? parsedEdid?.SerialNumber.ToString(),
                EdidHash = edid?.Fingerprint,
                EdidAvailable = edid is not null,
            });
        }

        return new DisplayTopology { Outputs = outputs };
    }

    /// <summary>EDID crudo de cada salida activa, indexado por el nombre GDI del display.</summary>
    public static IReadOnlyDictionary<string, RawEdid> GetActiveEdids()
    {
        Dictionary<string, RawEdid> result = new(StringComparer.OrdinalIgnoreCase);

        if (!TryQuery(out DisplayConfigPathInfo[]? paths, out _))
        {
            return result;
        }

        foreach (DisplayConfigPathInfo path in paths!)
        {
            if (!path.IsActive)
            {
                continue;
            }

            DisplayConfigTargetDeviceName target = QueryTargetName(path, out bool ok);
            if (!ok)
            {
                continue;
            }

            RawEdid? edid = EdidRegistryReader.TryReadForDevicePath(target.MonitorDevicePath);
            if (edid is null)
            {
                continue;
            }

            string key = QuerySourceName(path) ?? target.MonitorDevicePath;
            result[key] = edid;
        }

        return result;
    }

    private static bool TryQuery(out DisplayConfigPathInfo[]? paths, out DisplayConfigModeInfo[]? modes)
    {
        paths = null;
        modes = null;

        int status = DisplayConfigInterop.GetDisplayConfigBufferSizes(
            DisplayConfigInterop.QdcOnlyActivePaths, out uint pathCount, out uint modeCount);

        if (status != DisplayConfigInterop.ErrorSuccess || pathCount == 0)
        {
            return false;
        }

        DisplayConfigPathInfo[] pathBuffer = new DisplayConfigPathInfo[pathCount];
        DisplayConfigModeInfo[] modeBuffer = new DisplayConfigModeInfo[modeCount];

        status = DisplayConfigInterop.QueryDisplayConfig(
            DisplayConfigInterop.QdcOnlyActivePaths,
            ref pathCount, pathBuffer,
            ref modeCount, modeBuffer,
            IntPtr.Zero);

        if (status != DisplayConfigInterop.ErrorSuccess)
        {
            return false;
        }

        // QueryDisplayConfig puede devolver menos elementos de los reservados.
        paths = pathBuffer[..(int)pathCount];
        modes = modeBuffer[..(int)modeCount];
        return true;
    }

    private static DisplayConfigSourceMode? FindSourceMode(
        DisplayConfigModeInfo[] modes, DisplayConfigPathInfo path)
    {
        uint index = path.SourceInfo.ModeInfoIdx;

        // 0xFFFFFFFF es DISPLAYCONFIG_PATH_MODE_IDX_INVALID.
        if (index == 0xFFFFFFFF || index >= modes.Length)
        {
            return null;
        }

        DisplayConfigModeInfo mode = modes[index];
        return mode.InfoType == DisplayConfigModeInfo.InfoTypeSource ? mode.SourceMode : null;
    }

    private static string? QuerySourceName(DisplayConfigPathInfo path)
    {
        DisplayConfigSourceDeviceName request = new()
        {
            ViewGdiDeviceName = string.Empty,
            Header = new DisplayConfigDeviceInfoHeader
            {
                Type = DisplayConfigInterop.GetSourceName,
                Size = Marshal.SizeOf<DisplayConfigSourceDeviceName>(),
                AdapterId = path.SourceInfo.AdapterId,
                Id = path.SourceInfo.Id,
            },
        };

        return DisplayConfigInterop.DisplayConfigGetDeviceInfo(ref request) == DisplayConfigInterop.ErrorSuccess
            ? request.ViewGdiDeviceName
            : null;
    }

    private static DisplayConfigTargetDeviceName QueryTargetName(DisplayConfigPathInfo path, out bool ok)
    {
        DisplayConfigTargetDeviceName request = new()
        {
            MonitorFriendlyDeviceName = string.Empty,
            MonitorDevicePath = string.Empty,
            Header = new DisplayConfigDeviceInfoHeader
            {
                Type = DisplayConfigInterop.GetTargetName,
                Size = Marshal.SizeOf<DisplayConfigTargetDeviceName>(),
                AdapterId = path.TargetInfo.AdapterId,
                Id = path.TargetInfo.Id,
            },
        };

        ok = DisplayConfigInterop.DisplayConfigGetDeviceInfo(ref request) == DisplayConfigInterop.ErrorSuccess;
        return request;
    }

    private static DisplayConfigAdvancedColorInfo QueryAdvancedColor(DisplayConfigPathInfo path, out bool ok)
    {
        DisplayConfigAdvancedColorInfo request = new()
        {
            Header = new DisplayConfigDeviceInfoHeader
            {
                Type = DisplayConfigInterop.GetAdvancedColorInfo,
                Size = Marshal.SizeOf<DisplayConfigAdvancedColorInfo>(),
                AdapterId = path.TargetInfo.AdapterId,
                Id = path.TargetInfo.Id,
            },
        };

        // En Windows 10 anteriores a 1803 este tipo de consulta no existe y devuelve
        // ERROR_INVALID_PARAMETER. No es un fallo: es que no hay informacion de HDR.
        ok = DisplayConfigInterop.DisplayConfigGetDeviceInfo(ref request) == DisplayConfigInterop.ErrorSuccess;
        return request;
    }
}
