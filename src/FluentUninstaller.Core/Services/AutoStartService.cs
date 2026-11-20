using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace FluentUninstaller.Core.Services;

public static class AutoStartService
{
    private const string RunRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "FluentUninstaller";

    public static bool IsAutoStartEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, false);
            var val = key?.GetValue(AppName);
            return val != null;
        }
        catch
        {
            return false;
        }
    }

    public static void SetAutoStart(bool enable, string? exePath = null)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, true);
            if (key == null) return;

            if (enable)
            {
                var targetExe = exePath ?? Environment.ProcessPath;
                if (!string.IsNullOrEmpty(targetExe) && File.Exists(targetExe))
                {
                    key.SetValue(AppName, $"\"{targetExe}\" --autostart");
                }
            }
            else
            {
                key.DeleteValue(AppName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to set auto-start: {ex.Message}");
        }
    }
}
