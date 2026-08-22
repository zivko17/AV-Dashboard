using System.Runtime.Versioning;
using AvDashboard.Core.Models;

// Ver la nota sobre este namespace en PowerConfiguration.cs.
namespace AvDashboard.Native.System;

/// <summary>Unidades y espacio libre (M9).</summary>
[SupportedOSPlatform("windows")]
public static class StorageProvider
{
    public static IReadOnlyList<StorageVolume> GetVolumes(
        IReadOnlySet<string>? recordingTargets = null)
    {
        List<StorageVolume> volumes = new();

        foreach (DriveInfo drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady || drive.DriveType is DriveType.CDRom or DriveType.Network)
            {
                continue;
            }

            string letter = drive.Name.TrimEnd('\\');

            volumes.Add(new StorageVolume
            {
                DriveLetter = letter,
                Label = SafeLabel(drive),
                FileSystem = SafeFileSystem(drive),
                TotalBytes = drive.TotalSize,
                FreeBytes = drive.AvailableFreeSpace,
                IsRecordingTarget = recordingTargets?.Contains(letter) ?? false,
            });
        }

        return volumes;
    }

    /// <summary>
    /// Mide escritura secuencial escribiendo un fichero temporal. Es una operacion
    /// deliberadamente bajo demanda: no tiene ningun sentido hacerla durante un show.
    /// </summary>
    public static async Task<double> MeasureWriteMbpsAsync(
        string driveLetter,
        int megabytes = 128,
        CancellationToken cancellationToken = default)
    {
        string path = Path.Combine(driveLetter + Path.DirectorySeparatorChar,
            $".avdashboard-write-test-{Guid.NewGuid():N}.tmp");

        byte[] buffer = new byte[4 * 1024 * 1024];
        Random.Shared.NextBytes(buffer);

        long startTicks = Environment.TickCount64;

        try
        {
            await using (FileStream stream = new(
                path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 0, FileOptions.WriteThrough | FileOptions.SequentialScan))
            {
                int written = 0;
                while (written < megabytes)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await stream.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
                    written += buffer.Length / (1024 * 1024);
                }

                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            double seconds = (Environment.TickCount64 - startTicks) / 1000.0;
            return seconds <= 0 ? 0 : megabytes / seconds;
        }
        finally
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
                // El fichero de prueba se queda; no merece tirar la medida por esto.
            }
        }
    }

    private static string? SafeLabel(DriveInfo drive)
    {
        try
        {
            return drive.VolumeLabel;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static string? SafeFileSystem(DriveInfo drive)
    {
        try
        {
            return drive.DriveFormat;
        }
        catch (IOException)
        {
            return null;
        }
    }
}
