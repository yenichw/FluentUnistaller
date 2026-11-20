using FluentUninstaller.Core.Models;

namespace FluentUninstaller.Core.Engines;

public static class ShortcutResolver
{
    public static string? ResolveTarget(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".lnk")
        {
            try
            {
                var shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType != null)
                {
                    dynamic? shell = Activator.CreateInstance(shellType);
                    if (shell != null)
                    {
                        dynamic shortcut = shell.CreateShortcut(path);
                        string target = shortcut.TargetPath;
                        if (!string.IsNullOrWhiteSpace(target))
                        {
                            return target;
                        }
                    }
                }
            }
            catch { }
        }

        return File.Exists(path) || Directory.Exists(path) ? path : null;
    }

    public static InstalledApp? FindMatchingApp(string targetPath, IEnumerable<InstalledApp> apps)
    {
        if (string.IsNullOrWhiteSpace(targetPath)) return null;

        var targetDir = Directory.Exists(targetPath) ? targetPath : Path.GetDirectoryName(targetPath);
        var targetExeName = Path.GetFileNameWithoutExtension(targetPath);

        var appList = apps.ToList();

        // 1. Direct InstallLocation match
        if (!string.IsNullOrWhiteSpace(targetDir))
        {
            var matchByDir = appList.FirstOrDefault(a =>
                !string.IsNullOrWhiteSpace(a.InstallLocation) &&
                (string.Equals(a.InstallLocation.TrimEnd('\\'), targetDir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) ||
                 targetDir.StartsWith(a.InstallLocation.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)));

            if (matchByDir != null) return matchByDir;
        }

        // 2. Match by DisplayIcon path
        var matchByIcon = appList.FirstOrDefault(a =>
            !string.IsNullOrWhiteSpace(a.DisplayIcon) &&
            a.DisplayIcon.Contains(targetPath, StringComparison.OrdinalIgnoreCase));

        if (matchByIcon != null) return matchByIcon;

        // 3. Match by UninstallString path
        var matchByUninstall = appList.FirstOrDefault(a =>
            !string.IsNullOrWhiteSpace(a.UninstallString) &&
            !string.IsNullOrWhiteSpace(targetDir) &&
            a.UninstallString.Contains(targetDir, StringComparison.OrdinalIgnoreCase));

        if (matchByUninstall != null) return matchByUninstall;

        // 4. Match by Exe name with DisplayName
        if (!string.IsNullOrWhiteSpace(targetExeName) && targetExeName.Length >= 3)
        {
            var matchByName = appList.FirstOrDefault(a =>
                a.DisplayName.Contains(targetExeName, StringComparison.OrdinalIgnoreCase) ||
                targetExeName.Contains(a.DisplayName, StringComparison.OrdinalIgnoreCase));

            if (matchByName != null) return matchByName;
        }

        return null;
    }
}
