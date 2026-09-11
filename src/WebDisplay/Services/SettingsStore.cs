using System;
using System.IO;
using System.Text.Json;
using WebDisplay.Models;

namespace WebDisplay.Services;

public sealed class SettingsStore
{
    public string DataDirectory { get; }
    public string FilePath => Path.Combine(DataDirectory, "settings.json");
    public bool HasSettings => File.Exists(FilePath);
    public string? LoadWarning { get; private set; }
    public SettingsStore(string directory) { DataDirectory = Path.GetFullPath(directory); Directory.CreateDirectory(DataDirectory); }

    public AppSettings Load()
    {
        if (!HasSettings) return new AppSettings();
        try
        {
            var result = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? throw new InvalidDataException("配置为空");
            result.Validate();
            return result;
        }
        catch (Exception ex)
        {
            LoadWarning = "设置文件无法读取，已使用默认值。原文件保留，保存设置后会更新。";
            AppLog.Write("Settings load: " + ex.GetType().Name);
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        settings.Validate();
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, FilePath, true);
    }
}
