using System;
using System.IO;
using System.Text;
using Android.App;
using Android.Content;

namespace Un0Tv;

// Catches every fatal crash and writes the full stack trace to crash.log in
// the app's files folder, so the exact crashing line can be read off the TV
// (or over USB with adb) even when the device is not connected.
public static class CrashLog
{
    private static readonly object Gate = new();
    private static bool _installed;

    public static string LogPath
    {
        get
        {
            try
            {
                var ctx = Android.App.Application.Context;
                var files = ctx?.FilesDir?.AbsolutePath;
                var dir = ctx?.GetExternalFilesDir(null)?.AbsolutePath;
                return Path.Combine(dir ?? files ?? Path.GetTempPath(), "crash.log");
            }
            catch
            {
                return Path.Combine(Path.GetTempPath(), "un0tv-crash.log");
            }
        }
    }

    public static void Install()
    {
        lock (Gate)
        {
            if (_installed)
                return;
            _installed = true;

            Java.Lang.Thread.DefaultUncaughtExceptionHandler =
                new Handler(Write);
        }
    }

    private sealed class Handler : Java.Lang.Object, Java.Lang.Thread.IUncaughtExceptionHandler
    {
        private readonly Action<Java.Lang.Thread?, Java.Lang.Throwable?> _write;

        public Handler(Action<Java.Lang.Thread?, Java.Lang.Throwable?> write)
            => _write = write;

        void Java.Lang.Thread.IUncaughtExceptionHandler.UncaughtException(Java.Lang.Thread t, Java.Lang.Throwable e)
            => _write(t, e);
    }

    private static void Write(Java.Lang.Thread? thread, Java.Lang.Throwable? error)
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"=== CRASH {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
            sb.AppendLine($"Thread: {thread?.Name}");
            sb.AppendLine(error?.ToString());
            sb.AppendLine(error?.StackTrace);
            sb.AppendLine();
            File.AppendAllText(LogPath, sb.ToString());
        }
        catch
        {
            // Never let the logger itself crash the app.
        }
    }

    // Also records a non-fatal note (e.g. a caught player error) in the same file.
    public static void Note(string message)
    {
        try
        {
            File.AppendAllText(LogPath,
                $"--- note {DateTime.Now:HH:mm:ss}: {message}{Environment.NewLine}");
        }
        catch
        {
        }
    }
}
