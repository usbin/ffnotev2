using Microsoft.Win32;

namespace ffnotev2.Services;

public class AutoStartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ffnote";

    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(ValueName) is string;
        }
    }

    public string? CurrentExecutablePath => Environment.ProcessPath;

    public bool Enable()
    {
        var path = CurrentExecutablePath;
        if (string.IsNullOrEmpty(path)) return false;

        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        if (key is null) return false;
        // 경로에 공백이 있을 수 있어 따옴표로 감쌈
        key.SetValue(ValueName, $"\"{path}\"", RegistryValueKind.String);
        return true;
    }

    public void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    public bool SetEnabled(bool enabled)
    {
        if (enabled) return Enable();
        Disable();
        return true;
    }
}
