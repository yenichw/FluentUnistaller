using System.Diagnostics;
using System.Runtime.InteropServices;
using FluentUninstaller.Core.Models;
using Microsoft.Win32;

namespace FluentUninstaller.Core.Engines;

public class CleaningReport
{
    public int TotalItems { get; set; }
    public int DeletedItems { get; set; }
    public int FailedItems { get; set; }

    public int DeletedFolders { get; set; }
    public int DeletedFiles { get; set; }
    public int DeletedRegistryKeys { get; set; }
    public int DeletedRegistryValues { get; set; }
    public int DeletedServices { get; set; }
    public long BytesFreed { get; set; }

    public List<string> Errors { get; } = new();
}

public class CleanerEngine
{
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool MoveFileEx(string lpExistingFileName, string? lpNewFileName, int dwFlags);

    private const int MOVEFILE_DELAY_UNTIL_REBOOT = 0x4;

    public CleaningReport Clean(IEnumerable<LeftoverItem> items)
    {
        var report = new CleaningReport();

        foreach (var item in items.Where(i => i.IsSelected))
        {
            report.TotalItems++;
            try
            {
                switch (item.Type)
                {
                    case LeftoverType.Directory:
                        if (Directory.Exists(item.Path))
                        {
                            Directory.Delete(item.Path, recursive: true);
                            report.DeletedItems++;
                            report.DeletedFolders++;
                            report.BytesFreed += item.SizeBytes ?? 0;
                        }
                        break;

                    case LeftoverType.File:
                        if (File.Exists(item.Path))
                        {
                            try
                            {
                                File.Delete(item.Path);
                                report.DeletedItems++;
                                report.DeletedFiles++;
                                report.BytesFreed += item.SizeBytes ?? 0;
                            }
                            catch (IOException)
                            {
                                // Schedule delete on reboot if file is locked
                                if (MoveFileEx(item.Path, null, MOVEFILE_DELAY_UNTIL_REBOOT))
                                {
                                    report.DeletedItems++;
                                    report.DeletedFiles++;
                                    report.BytesFreed += item.SizeBytes ?? 0;
                                }
                                else
                                {
                                    throw;
                                }
                            }
                        }
                        break;

                    case LeftoverType.RegistryKey:
                        DeleteRegistryKey(item.Path);
                        report.DeletedItems++;
                        report.DeletedRegistryKeys++;
                        break;

                    case LeftoverType.RegistryValue:
                        DeleteRegistryValue(item.Path);
                        report.DeletedItems++;
                        report.DeletedRegistryValues++;
                        break;

                    case LeftoverType.Service:
                        DeleteWindowsService(item.Path);
                        report.DeletedItems++;
                        report.DeletedServices++;
                        break;
                }
            }
            catch (Exception ex)
            {
                report.FailedItems++;
                report.Errors.Add($"{item.Path}: {ex.Message}");
            }
        }

        return report;
    }

    private static void DeleteRegistryKey(string fullPath)
    {
        // Format: Hive\View\SubKeyPath
        // Example: CurrentUser\Default\Software\Vendor\App or LocalMachine\Registry64\Software\App
        var firstSlash = fullPath.IndexOf('\\');
        if (firstSlash <= 0) return;
        var secondSlash = fullPath.IndexOf('\\', firstSlash + 1);
        if (secondSlash <= 0) return;

        var hiveStr = fullPath[..firstSlash];
        var viewStr = fullPath.Substring(firstSlash + 1, secondSlash - firstSlash - 1);
        var subKeyPath = fullPath[(secondSlash + 1)..];

        var hive = hiveStr.ToUpperInvariant() switch
        {
            "HKEY_CURRENT_USER" or "CURRENTUSER" => RegistryHive.CurrentUser,
            "HKEY_CLASSES_ROOT" or "CLASSESROOT" => RegistryHive.ClassesRoot,
            _ => RegistryHive.LocalMachine
        };

        var view = viewStr.Equals("Registry32", StringComparison.OrdinalIgnoreCase)
            ? RegistryView.Registry32
            : RegistryView.Registry64;

        using var baseKey = RegistryKey.OpenBaseKey(hive, view);

        var lastSlash = subKeyPath.LastIndexOf('\\');
        if (lastSlash > 0)
        {
            var parentPath = subKeyPath[..lastSlash];
            var keyToDelete = subKeyPath[(lastSlash + 1)..];
            using var parent = baseKey.OpenSubKey(parentPath, writable: true);
            parent?.DeleteSubKeyTree(keyToDelete, throwOnMissingSubKey: false);
        }
        else
        {
            baseKey.DeleteSubKeyTree(subKeyPath, throwOnMissingSubKey: false);
        }
    }

    private static void DeleteRegistryValue(string rawPath)
    {
        // Format: Hive\SubKey\ValName -> Data or Hive\SubKey\ValName
        var arrowIdx = rawPath.IndexOf(" -> ", StringComparison.Ordinal);
        var left = arrowIdx > 0 ? rawPath[..arrowIdx] : rawPath;

        var lastSlash = left.LastIndexOf('\\');
        if (lastSlash <= 0) return;

        var keyPath = left[..lastSlash];
        var valName = left[(lastSlash + 1)..];

        var hive = keyPath.StartsWith("HKEY_CURRENT_USER", StringComparison.OrdinalIgnoreCase) ||
                   keyPath.StartsWith("CurrentUser", StringComparison.OrdinalIgnoreCase)
            ? RegistryHive.CurrentUser
            : RegistryHive.LocalMachine;

        var subKeyPart = keyPath.Substring(keyPath.IndexOf('\\') + 1);

        using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
        using var target = baseKey.OpenSubKey(subKeyPart, writable: true);
        target?.DeleteValue(valName, throwOnMissingValue: false);
    }

    private static void DeleteWindowsService(string servicePath)
    {
        // Service path: LocalMachine\Registry64\SYSTEM\CurrentControlSet\Services\<svcName>
        var svcName = Path.GetFileName(servicePath);
        if (string.IsNullOrWhiteSpace(svcName)) return;

        try
        {
            using var sc = Process.Start(new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = $"delete \"{svcName}\"",
                CreateNoWindow = true,
                UseShellExecute = false
            });
            sc?.WaitForExit(3000);
        }
        catch { }

        // Fallback: Delete registry key directly
        DeleteRegistryKey(servicePath);
    }
}
