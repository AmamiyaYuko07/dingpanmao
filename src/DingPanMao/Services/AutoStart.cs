using Microsoft.Win32;
using DingPanMao.Config;

namespace DingPanMao.Services;

/// <summary>开机自启，写入当前用户的 Run 键，不需要管理员权限。</summary>
public static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static void Apply(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key is null)
            {
                return;
            }

            if (enabled)
            {
                var path = Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(path))
                {
                    key.SetValue(AppConfig.ProductName, $"\"{path}\"");
                }
            }
            else
            {
                key.DeleteValue(AppConfig.ProductName, throwOnMissingValue: false);
            }
        }
        catch
        {
            // 注册表不可写时静默忽略，不影响其它功能。
        }
    }
}
