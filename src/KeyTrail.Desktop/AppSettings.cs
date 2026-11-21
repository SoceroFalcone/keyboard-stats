using System.Diagnostics;
using System.Text.Json;
using Microsoft.Win32;

namespace KeyTrail.Desktop;

public sealed class AppSettings
{
    public bool CountRepeats { get; set; }
    public bool Paused { get; set; }
    public bool CloseToTray { get; set; } = true;
    public static AppSettings Load(string directory) => Load(directory, out _);

    public static AppSettings Load(string directory, out string? recoveryNotice)
    {
        recoveryNotice = null;
        var path = Path.Combine(directory, "settings.json");
        var backupPath = path + ".bak";
        if (!File.Exists(path) && !File.Exists(backupPath)) return new();

        try { return Read(path); }
        catch (Exception error) when (error is IOException or JsonException or InvalidDataException)
        {
            AppSettings recovered;
            try { recovered = Read(backupPath); }
            catch (Exception backupError) when (backupError is IOException or JsonException or InvalidDataException)
            {
                throw new InvalidDataException("无法读取偏好设置及其备份。文件已保留，请从可用的备份恢复。", new AggregateException(error, backupError));
            }

            var hadPrimary = File.Exists(path);
            if (hadPrimary) File.Copy(path, path + ".corrupt-" + Guid.NewGuid().ToString("N"));
            StatsStore.AtomicWrite(path, JsonSerializer.Serialize(recovered, JsonOptions), false);
            recoveryNotice = hadPrimary
                ? "偏好设置已从备份恢复，损坏的原文件已保留。"
                : "偏好设置文件缺失，已从备份恢复。";
            return recovered;
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static AppSettings Read(string path) => JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? throw new InvalidDataException("设置文件为空。");

    public void Save(string directory) => StatsStore.AtomicWrite(Path.Combine(directory, "settings.json"), JsonSerializer.Serialize(this, JsonOptions), true);
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
