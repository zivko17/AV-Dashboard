using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using AvDashboard.Core.Models;

namespace AvDashboard.Native.System;

/// <summary>
/// Telemetria de CPU y memoria a 1 s (M1).
///
/// Deliberadamente sin PerformanceCounter: el proveedor de contadores de rendimiento
/// tiene un coste de arranque y de consulta que no cuadra con un presupuesto del 1 %
/// de CPU en una maquina que esta produciendo. GetSystemTimes y
/// NtQuerySystemInformation dan lo mismo por una fraccion del coste.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SystemTelemetryProvider
{
    private long _previousIdle;
    private long _previousKernel;
    private long _previousUser;
    private bool _hasPrevious;

    private SystemProcessorPerformanceInformation[]? _previousPerCore;

    private readonly int _coreCount = Environment.ProcessorCount;

    public SystemStatus Read()
    {
        Platform.EnsureWindows();

        double cpuTotal = ReadCpuTotal();
        IReadOnlyList<double> perCore = ReadCpuPerCore();
        (long usedBytes, long totalBytes, double usedPercent) = ReadMemory();

        return new SystemStatus
        {
            Timestamp = DateTimeOffset.UtcNow,
            CpuTotalPercent = cpuTotal,
            CpuPerCorePercent = perCore,
            MemoryUsedBytes = usedBytes,
            MemoryTotalBytes = totalBytes,
            MemoryUsedPercent = usedPercent,
            Uptime = TimeSpan.FromMilliseconds(Environment.TickCount64),

            // Temperaturas y contadores de throttling requieren LibreHardwareMonitorLib,
            // que carga un driver en kernel. Es una decision con consecuencias en
            // maquinas de cliente y esta documentada en docs/dependencies.md: se activa
            // explicitamente, no por defecto.
            CpuTemperatureC = null,
            GpuTemperatureC = null,
        };
    }

    private double ReadCpuTotal()
    {
        if (!GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user))
        {
            return 0;
        }

        long idleTicks = idle.ToInt64();
        long kernelTicks = kernel.ToInt64();
        long userTicks = user.ToInt64();

        if (!_hasPrevious)
        {
            _previousIdle = idleTicks;
            _previousKernel = kernelTicks;
            _previousUser = userTicks;
            _hasPrevious = true;
            return 0;
        }

        // El tiempo de kernel que devuelve Windows ya incluye el idle, de ahi la resta.
        long idleDelta = idleTicks - _previousIdle;
        long kernelDelta = kernelTicks - _previousKernel;
        long userDelta = userTicks - _previousUser;

        _previousIdle = idleTicks;
        _previousKernel = kernelTicks;
        _previousUser = userTicks;

        long total = kernelDelta + userDelta;
        if (total <= 0)
        {
            return 0;
        }

        double busy = total - idleDelta;
        return Math.Clamp(busy * 100.0 / total, 0, 100);
    }

    private IReadOnlyList<double> ReadCpuPerCore()
    {
        int size = Marshal.SizeOf<SystemProcessorPerformanceInformation>();
        int bufferSize = size * _coreCount;
        IntPtr buffer = Marshal.AllocHGlobal(bufferSize);

        try
        {
            int status = NtQuerySystemInformation(
                SystemProcessorPerformanceInformationClass, buffer, bufferSize, out _);

            if (status != 0)
            {
                return Array.Empty<double>();
            }

            SystemProcessorPerformanceInformation[] current =
                new SystemProcessorPerformanceInformation[_coreCount];

            for (int i = 0; i < _coreCount; i++)
            {
                current[i] = Marshal.PtrToStructure<SystemProcessorPerformanceInformation>(
                    buffer + (i * size));
            }

            if (_previousPerCore is null || _previousPerCore.Length != _coreCount)
            {
                _previousPerCore = current;
                return Array.Empty<double>();
            }

            double[] result = new double[_coreCount];
            for (int i = 0; i < _coreCount; i++)
            {
                long idleDelta = current[i].IdleTime - _previousPerCore[i].IdleTime;
                long kernelDelta = current[i].KernelTime - _previousPerCore[i].KernelTime;
                long userDelta = current[i].UserTime - _previousPerCore[i].UserTime;

                long total = kernelDelta + userDelta;
                result[i] = total <= 0
                    ? 0
                    : Math.Clamp((total - idleDelta) * 100.0 / total, 0, 100);
            }

            _previousPerCore = current;
            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>
    /// RAM total en bytes, o 0 si no se puede leer.
    ///
    /// Se expone porque el actuador del archivo de paginacion necesita dimensionarlo y la
    /// alternativa era duplicar la estructura de interop en el escritor.
    /// </summary>
    public static long TotalPhysicalMemoryBytes() => ReadMemory().Total;

    private static (long Used, long Total, double Percent) ReadMemory()
    {
        MemoryStatusEx status = new() { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };

        if (!GlobalMemoryStatusEx(ref status))
        {
            return (0, 0, 0);
        }

        long total = (long)status.TotalPhys;
        long used = total - (long)status.AvailPhys;
        double percent = total == 0 ? 0 : used * 100.0 / total;

        return (used, total, percent);
    }

    // --- Interop ---

    private const int SystemProcessorPerformanceInformationClass = 8;

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint Low;
        public uint High;

        public readonly long ToInt64() => ((long)High << 32) | Low;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemProcessorPerformanceInformation
    {
        public long IdleTime;
        public long KernelTime;
        public long UserTime;
        public long Reserved1;
        public long Reserved2;
        public uint Reserved3;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [DllImport("ntdll.dll")]
    private static extern int NtQuerySystemInformation(
        int systemInformationClass, IntPtr systemInformation, int systemInformationLength, out int returnLength);
}
