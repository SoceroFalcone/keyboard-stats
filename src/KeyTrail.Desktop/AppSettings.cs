using System.Diagnostics;
using System.Text.Json;
using Microsoft.Win32;

namespace KeyTrail.Desktop;

public sealed class AppSettings
{
    public bool CountRepeats { get; set; }
    public bool Paused { get; set; }
    public bool CloseToTray { get; set; } = true;
    public static AppSettings Load(string directory)
    {
        var path = Path.Combine(directory, "settings.json");
        if (!File.Exists(path)) return new();
        return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? throw new InvalidDataException("设置文件为空。");
    }
    public void Save(string directory) => StatsStore.AtomicWrite(Path.Combine(directory, "settings.json"), JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }), true);
}

public static class StartupRegistration
{
    private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "KeyTrail";
    public static bool Enabled
    {
        get { using var key = Registry.CurrentUser.OpenSubKey(RunPath); return key?.GetValue(ValueName) is string; }
    }
    public static void SetEnabled(bool enabled, string dataDirectory)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunPath, true);
        if (enabled)
        {
            var path = Environment.ProcessPath ?? throw new InvalidOperationException("无法定位应用程序，请从发布目录启动。");
            if (Path.GetFileNameWithoutExtension(path).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("请运行 KeyTrail.exe 后再开启开机启动。");
            key.SetValue(ValueName, $"\"{path}\" --background --data-dir \"{Path.GetFullPath(dataDirectory)}\"");
        }
        else key.DeleteValue(ValueName, false);
    }
}
