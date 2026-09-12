using System;
using System.IO;

namespace WebDisplay.Services;

public static class AppLog
{
    private static readonly object Gate = new();
    private static string? _file;
    public static void Initialize(string directory) => _file = Path.Combine(directory, "WebDisplay.log");
    public static void Write(string message)
    {
        lock (Gate)
        {
            if (_file == null) return;
            try
            {
                if (File.Exists(_file) && new FileInfo(_file).Length > 2_000_000) File.Move(_file, _file + ".old", true);
                File.AppendAllText(_file, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
            }
            catch { /* Logging must never interrupt the display. */ }
        }
    }
}
