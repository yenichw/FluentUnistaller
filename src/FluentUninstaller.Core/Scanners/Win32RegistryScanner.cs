using System.Security;
using System.Text.RegularExpressions;
using FluentUninstaller.Core.Models;
using Microsoft.Win32;

namespace FluentUninstaller.Core.Scanners;

public class Win32RegistryScanner
{
    private const string UninstallSubKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    public IEnumerable<InstalledApp> Scan(bool includeSystemComponents = false)
    {
        var apps = new Dictionary<string, InstalledApp>(StringComparer.OrdinalIgnoreCase);

        // 1. HKLM 64-bit
        ScanHive(RegistryHive.LocalMachine, RegistryView.Registry64, apps, is64Bit: true, includeSystemComponents);

        // 2. HKLM 32-bit (WOW6432Node)
        if (Environment.Is64BitOperatingSystem)
        {
            ScanHive(RegistryHive.LocalMachine, RegistryView.Registry32, apps, is64Bit: false, includeSystemComponents);
        }

        // 3. HKCU (User-installed programs)
        ScanHive(RegistryHive.CurrentUser, RegistryView.Default, apps, is64Bit: Environment.Is64BitOperatingSystem, includeSystemComponents);

        return apps.Values.OrderBy(a => a.DisplayName);
    }

    private void ScanHive(
        RegistryHive hive,
        RegistryView view,
        Dictionary<string, InstalledApp> targetList,
        bool is64Bit,
        bool includeSystemComponents)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var uninstallKey = baseKey.OpenSubKey(UninstallSubKey);
            if (uninstallKey == null) return;

            foreach (var subKeyName in uninstallKey.GetSubKeyNames())
            {
                try
                {
                    using var appKey = uninstallKey.OpenSubKey(subKeyName);
                    if (appKey == null) continue;

                    var displayName = appKey.GetValue("DisplayName")?.ToString()?.Trim();
                    if (string.IsNullOrEmpty(displayName))
                        continue;

                    // Filter out system components and updates unless requested
                    var systemComponent = appKey.GetValue("SystemComponent");
                    var isSys = systemComponent is int sc && sc == 1;
                    if (isSys && !includeSystemComponents)
                        continue;

                    var parentKeyName = appKey.GetValue("ParentKeyName")?.ToString();
                    if (!string.IsNullOrEmpty(parentKeyName) && !includeSystemComponents)
                        continue; // Child component / patch

                    var uninstallString = appKey.GetValue("UninstallString")?.ToString();
                    var quietUninstallString = appKey.GetValue("QuietUninstallString")?.ToString();

                    // If neither uninstall string is present, usually not directly uninstallable
                    if (string.IsNullOrEmpty(uninstallString) && string.IsNullOrEmpty(quietUninstallString))
                        continue;

                    var publisher = appKey.GetValue("Publisher")?.ToString()?.Trim();
                    var version = appKey.GetValue("DisplayVersion")?.ToString()?.Trim();
                    var installDate = appKey.GetValue("InstallDate")?.ToString()?.Trim();
                    var installLocation = appKey.GetValue("InstallLocation")?.ToString()?.Trim();
                    var displayIcon = appKey.GetValue("DisplayIcon")?.ToString()?.Trim();

                    long? estimatedSize = null;
                    var rawSize = appKey.GetValue("EstimatedSize");
                    if (rawSize is int s32 && s32 > 0)
                        estimatedSize = s32;
                    else if (rawSize is long s64 && s64 > 0)
                        estimatedSize = s64;

                    var isMsi = subKeyName.StartsWith('{') && subKeyName.EndsWith('}') &&
                                (appKey.GetValue("WindowsInstaller") is int wi && wi == 1);

                    var id = $"{hive}_{view}_{subKeyName}";

                    // Smart deduction of quiet uninstall string if missing
                    if (string.IsNullOrEmpty(quietUninstallString) && !string.IsNullOrEmpty(uninstallString))
                    {
                        quietUninstallString = DetectQuietString(uninstallString, isMsi);
                    }

                    var app = new InstalledApp
                    {
                        Id = id,
                        DisplayName = displayName,
                        Publisher = publisher,
                        DisplayVersion = version,
                        InstallDate = FormatInstallDate(installDate),
                        InstallLocation = CleanPath(installLocation),
                        UninstallString = uninstallString,
                        QuietUninstallString = quietUninstallString,
                        EstimatedSizeInKb = estimatedSize,
                        DisplayIcon = CleanIconRaw(displayIcon),
                        Is64Bit = is64Bit,
                        Type = isMsi ? AppType.Msi : AppType.Win32,
                        RegistryKeyPath = $@"{hive}\{UninstallSubKey}\{subKeyName}",
                        IsSystemComponent = isSys
                    };

                    // Deduplicate by Name + Publisher if already found
                    var dedupeKey = $"{displayName}_{publisher}_{version}";
                    if (!targetList.ContainsKey(dedupeKey))
                    {
                        targetList[dedupeKey] = app;
                    }
                }
                catch (SecurityException) { }
                catch (Exception) { }
            }
        }
        catch (Exception) { }
    }

    private static string? DetectQuietString(string uninstallString, bool isMsi)
    {
        if (isMsi)
        {
            return Regex.Replace(uninstallString, @"(?i)/[IX]", "/X") + " /qn /norestart";
        }

        var lower = uninstallString.ToLowerInvariant();
        if (lower.Contains("unins000.exe") || lower.Contains("inno setup"))
        {
            return $"{uninstallString} /VERYSILENT /SUPPRESSMSGBOXES /NORESTART";
        }

        if (lower.EndsWith(".exe\"") || lower.EndsWith(".exe"))
        {
            if (lower.Contains("uninstall") || lower.Contains("uninst"))
            {
                return $"{uninstallString} /S";
            }
        }

        return null;
    }

    private static string? FormatInstallDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (raw.Length == 8 && int.TryParse(raw, out _))
        {
            var y = raw[..4];
            var m = raw.Substring(4, 2);
            var d = raw.Substring(6, 2);
            return $"{y}-{m}-{d}";
        }
        return raw;
    }

    private static string? CleanPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        return path.Trim('\"', ' ', '\t');
    }

    private static string? CleanIconRaw(string? rawIcon)
    {
        if (string.IsNullOrWhiteSpace(rawIcon)) return null;
        return rawIcon.Trim('\"', ' ', '\t');
    }
}
