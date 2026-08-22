using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using AvDashboard.Core.Models;

namespace AvDashboard.Native.Audio;

/// <summary>
/// Enumeracion de endpoints de Core Audio (M3 base).
///
/// Nota de coste: la lectura completa activa un <c>IAudioClient</c> por dispositivo para
/// obtener el mix format, asi que no se llama a 20 fps. Los medidores usan
/// <see cref="ReadPeaks"/>, que reutiliza los objetos COM ya abiertos y es barato.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class CoreAudioProvider : IDisposable
{
    private readonly IMMDeviceEnumerator _enumerator;
    private readonly Dictionary<string, IAudioMeterInformation> _meters = new(StringComparer.Ordinal);
    private bool _disposed;

    public CoreAudioProvider()
    {
        Platform.EnsureWindows();

        Type? comType = Type.GetTypeFromCLSID(CoreAudioInterop.ClsidMMDeviceEnumerator);
        if (comType is null)
        {
            throw new InvalidOperationException("No se pudo resolver MMDeviceEnumerator.");
        }

        object? instance = Activator.CreateInstance(comType);
        _enumerator = instance as IMMDeviceEnumerator
            ?? throw new InvalidOperationException("MMDeviceEnumerator no expone IMMDeviceEnumerator.");
    }

    /// <summary>Enumera todos los endpoints, incluidos los deshabilitados y desconectados.</summary>
    public IReadOnlyList<AudioDevice> GetDevices(bool includeInactive = true)
    {
        int stateMask = includeInactive ? CoreAudioInterop.DeviceStateAll : CoreAudioInterop.DeviceStateActive;

        string? defaultRenderId = TryGetDefaultId(EDataFlow.ERender, ERole.EConsole);
        string? defaultCaptureId = TryGetDefaultId(EDataFlow.ECapture, ERole.EConsole);
        string? commsRenderId = TryGetDefaultId(EDataFlow.ERender, ERole.ECommunications);
        string? commsCaptureId = TryGetDefaultId(EDataFlow.ECapture, ERole.ECommunications);

        List<AudioDevice> devices = new();

        foreach (EDataFlow flow in new[] { EDataFlow.ERender, EDataFlow.ECapture })
        {
            if (_enumerator.EnumAudioEndpoints(flow, stateMask, out IMMDeviceCollection collection) != 0)
            {
                continue;
            }

            try
            {
                if (collection.GetCount(out int count) != 0)
                {
                    continue;
                }

                for (int i = 0; i < count; i++)
                {
                    if (collection.Item(i, out IMMDevice device) != 0)
                    {
                        continue;
                    }

                    try
                    {
                        AudioDevice? described = Describe(
                            device,
                            flow,
                            flow == EDataFlow.ERender ? defaultRenderId : defaultCaptureId,
                            flow == EDataFlow.ERender ? commsRenderId : commsCaptureId);

                        if (described is not null)
                        {
                            devices.Add(described);
                        }
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(device);
                    }
                }
            }
            finally
            {
                Marshal.ReleaseComObject(collection);
            }
        }

        return devices;
    }

    private AudioDevice? Describe(IMMDevice device, EDataFlow flow, string? defaultId, string? commsId)
    {
        if (device.GetId(out string id) != 0)
        {
            return null;
        }

        device.GetState(out int state);

        string friendlyName = id;
        string? description = null;
        int declaredSampleRate = 0;
        int declaredBitDepth = 0;

        if (device.OpenPropertyStore(CoreAudioInterop.StgmRead, out IPropertyStore store) == 0)
        {
            try
            {
                friendlyName = ReadString(store, CoreAudioInterop.PkeyDeviceFmtId,
                    CoreAudioInterop.PidDeviceFriendlyName) ?? id;

                description = ReadString(store, CoreAudioInterop.PkeyDeviceFmtId,
                    CoreAudioInterop.PidDeviceDescription);

                byte[]? formatBlob = ReadBlob(store, CoreAudioInterop.PkeyAudioEngineFmtId,
                    CoreAudioInterop.PidAudioEngineDeviceFormat);

                if (formatBlob is not null && TryParseWaveFormat(formatBlob, out WaveFormatEx declared))
                {
                    declaredSampleRate = (int)declared.SamplesPerSec;
                    declaredBitDepth = declared.BitsPerSample;
                }
            }
            finally
            {
                Marshal.ReleaseComObject(store);
            }
        }

        (int mixRate, int mixBits, int mixChannels) = ReadMixFormat(device);
        (float volume, bool muted) = ReadVolume(device);

        return new AudioDevice
        {
            Id = id,
            FriendlyName = friendlyName,
            DeviceDescription = description,
            DataFlow = flow == EDataFlow.ERender ? AudioDataFlow.Render : AudioDataFlow.Capture,
            State = MapState(state),
            IsDefault = defaultId is not null && string.Equals(defaultId, id, StringComparison.Ordinal),
            IsDefaultCommunications = commsId is not null && string.Equals(commsId, id, StringComparison.Ordinal),
            MixSampleRate = mixRate,
            MixBitDepth = mixBits,
            MixChannels = mixChannels,
            DefaultSampleRate = declaredSampleRate > 0 ? declaredSampleRate : null,
            DefaultBitDepth = declaredBitDepth > 0 ? declaredBitDepth : null,
            VolumeScalar = volume,
            Muted = muted,
            // ExclusiveModeAllowed y ExclusiveHolder salen de propiedades no documentadas
            // del endpoint. Viven en Audio/Undocumented y se rellenan alli cuando estan.
        };
    }

    private (int SampleRate, int BitDepth, int Channels) ReadMixFormat(IMMDevice device)
    {
        Guid iid = typeof(IAudioClient).GUID;

        if (device.Activate(ref iid, CoreAudioInterop.ClsCtxAll, IntPtr.Zero, out object instance) != 0 ||
            instance is not IAudioClient client)
        {
            return (0, 0, 0);
        }

        try
        {
            if (client.GetMixFormat(out IntPtr formatPtr) != 0 || formatPtr == IntPtr.Zero)
            {
                return (0, 0, 0);
            }

            try
            {
                WaveFormatEx format = Marshal.PtrToStructure<WaveFormatEx>(formatPtr);
                return ((int)format.SamplesPerSec, format.BitsPerSample, format.Channels);
            }
            finally
            {
                Ole32.CoTaskMemFree(formatPtr);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(client);
        }
    }

    private (float Volume, bool Muted) ReadVolume(IMMDevice device)
    {
        Guid iid = typeof(IAudioEndpointVolume).GUID;

        if (device.Activate(ref iid, CoreAudioInterop.ClsCtxAll, IntPtr.Zero, out object instance) != 0 ||
            instance is not IAudioEndpointVolume volume)
        {
            return (0, false);
        }

        try
        {
            volume.GetMasterVolumeLevelScalar(out float scalar);
            volume.GetMute(out bool muted);
            return (scalar, muted);
        }
        finally
        {
            Marshal.ReleaseComObject(volume);
        }
    }

    /// <summary>
    /// Lectura barata de picos para los medidores a 20 fps. Cachea el objeto COM por
    /// dispositivo: reactivarlo veinte veces por segundo si se nota en el perfil de CPU,
    /// y el presupuesto del agente es del 1 %.
    /// </summary>
    public IReadOnlyDictionary<string, float> ReadPeaks(IEnumerable<string> deviceIds)
    {
        Dictionary<string, float> peaks = new(StringComparer.Ordinal);

        foreach (string id in deviceIds)
        {
            IAudioMeterInformation? meter = GetOrCreateMeter(id);
            if (meter is null)
            {
                continue;
            }

            if (meter.GetPeakValue(out float peak) == 0)
            {
                peaks[id] = peak;
            }
            else
            {
                // El dispositivo ha desaparecido bajo los pies: fuera de la cache para
                // que el watchdog lo detecte en la siguiente enumeracion.
                DisposeMeter(id);
            }
        }

        return peaks;
    }

    private IAudioMeterInformation? GetOrCreateMeter(string deviceId)
    {
        if (_meters.TryGetValue(deviceId, out IAudioMeterInformation? cached))
        {
            return cached;
        }

        if (_enumerator.GetDevice(deviceId, out IMMDevice device) != 0)
        {
            return null;
        }

        try
        {
            Guid iid = typeof(IAudioMeterInformation).GUID;
            if (device.Activate(ref iid, CoreAudioInterop.ClsCtxAll, IntPtr.Zero, out object instance) != 0 ||
                instance is not IAudioMeterInformation meter)
            {
                return null;
            }

            _meters[deviceId] = meter;
            return meter;
        }
        finally
        {
            Marshal.ReleaseComObject(device);
        }
    }

    private void DisposeMeter(string deviceId)
    {
        if (_meters.Remove(deviceId, out IAudioMeterInformation? meter))
        {
            Marshal.ReleaseComObject(meter);
        }
    }

    private string? TryGetDefaultId(EDataFlow flow, ERole role)
    {
        // Sin dispositivo por defecto el HRESULT es E_NOTFOUND y no es un error:
        // una maquina sin tarjeta de captura no tiene default de captura.
        if (_enumerator.GetDefaultAudioEndpoint(flow, role, out IMMDevice device) != 0)
        {
            return null;
        }

        try
        {
            return device.GetId(out string id) == 0 ? id : null;
        }
        finally
        {
            Marshal.ReleaseComObject(device);
        }
    }

    private static string? ReadString(IPropertyStore store, Guid fmtId, int pid)
    {
        PropertyKey key = new(fmtId, pid);
        if (store.GetValue(ref key, out PropVariant value) != 0)
        {
            return null;
        }

        try
        {
            return value.AsString();
        }
        finally
        {
            Ole32.PropVariantClear(ref value);
        }
    }

    private static byte[]? ReadBlob(IPropertyStore store, Guid fmtId, int pid)
    {
        PropertyKey key = new(fmtId, pid);
        if (store.GetValue(ref key, out PropVariant value) != 0)
        {
            return null;
        }

        try
        {
            return value.AsBlob();
        }
        finally
        {
            Ole32.PropVariantClear(ref value);
        }
    }

    internal static bool TryParseWaveFormat(byte[] blob, out WaveFormatEx format)
    {
        format = default;

        if (blob.Length < 16)
        {
            return false;
        }

        GCHandle handle = GCHandle.Alloc(blob, GCHandleType.Pinned);
        try
        {
            format = Marshal.PtrToStructure<WaveFormatEx>(handle.AddrOfPinnedObject());
            return format.SamplesPerSec > 0;
        }
        finally
        {
            handle.Free();
        }
    }

    private static AudioDeviceState MapState(int state) => state switch
    {
        CoreAudioInterop.DeviceStateActive => AudioDeviceState.Active,
        CoreAudioInterop.DeviceStateDisabled => AudioDeviceState.Disabled,
        CoreAudioInterop.DeviceStateNotPresent => AudioDeviceState.NotPresent,
        CoreAudioInterop.DeviceStateUnplugged => AudioDeviceState.Unplugged,
        _ => AudioDeviceState.Unknown,
    };

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (IAudioMeterInformation meter in _meters.Values)
        {
            Marshal.ReleaseComObject(meter);
        }

        _meters.Clear();
        Marshal.ReleaseComObject(_enumerator);
        _disposed = true;
    }
}
