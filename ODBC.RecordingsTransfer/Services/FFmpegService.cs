using System.IO;

namespace ODBC.RecordingsTransfer.Services;

public static class FFmpegService
{
    public static string? FindExecutable()
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, "FFmpeg", "bin", "ffmpeg.exe");
        if (File.Exists(bundled))
            return bundled;

        var legacy = @"C:\FFmpeg\bin\ffmpeg.exe";
        if (File.Exists(legacy))
            return legacy;

        return null;
    }

    public static bool IsAvailable() => FindExecutable() != null;
}
