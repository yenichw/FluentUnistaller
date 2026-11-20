using FluentUninstaller.Core.Models;
using Microsoft.Win32;

namespace FluentUninstaller.Core.Engines;

public class LeftoverScanEngine
{
    private static readonly HashSet<string> ProtectedDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft", "Windows", "Common Files", "System32", "SysWOW64",
        "Program Files", "Program Files (x86)", "ProgramData",
        "AppData", "Local", "Roaming", "LocalLow", "Temp", "Classes",
        "Policies", "System", "CurrentControlSet"
    };

    public async Task<List<LeftoverItem>> ScanAsync(InstalledApp app, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => Scan(app, cancellationToken), cancellationToken);
    }

    public List<LeftoverItem> Scan(InstalledApp app, CancellationToken cancellationToken = default)
    {
        var results = new List<LeftoverItem>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var appTokens = ExtractTokens(app.DisplayName);
        var publisherTokens = ExtractTokens(app.Publisher);

        // 1. Check if InstallLocation still exists and has files
        if (!string.IsNullOrWhiteSpace(app.InstallLocation) && Directory.Exists(app.InstallLocation))
        {
            var fullPath = Path.GetFullPath(app.InstallLocation);
            if (!IsSystemRootOrProtected(fullPath))
            {
                var size = SafeGetDirectorySize(fullPath);
                AddResult(results, seenPaths, new LeftoverItem
                {
                    Path = fullPath,
                    Type = LeftoverType.Directory,
                    Confidence = ConfidenceLevel.High,
                    Reason = "Main application installation directory",
                    SizeBytes = size
                });
            }
        }

        // 2. Scan standard AppData and ProgramData folders
        var targetSearchRoots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), // C:\ProgramData
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        }.Where(p => !string.IsNullOrWhiteSpace(p) && Directory.Exists(p)).Distinct();

        foreach (var root in targetSearchRoots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ScanDirectoryForMatches(root, appTokens, publisherTokens, results, seenPaths);
        }

        // 3. Scan Registry Software trees (HKCU and HKLM 64/32)
        ScanRegistryTree(RegistryHive.CurrentUser, RegistryView.Default, appTokens, publisherTokens, results, seenPaths);
        ScanRegistryTree(RegistryHive.LocalMachine, RegistryView.Registry64, appTokens, publisherTokens, results, seenPaths);
        if (Environment.Is64BitOperatingSystem)
        {
            ScanRegistryTree(RegistryHive.LocalMachine, RegistryView.Registry32, appTokens, publisherTokens, results, seenPaths);
        }

        // 4. Scan App Paths (Registry paths used for execution)
        ScanAppPaths(appTokens, results, seenPaths);

        // 5. Scan Shell & Classes (Applications, ProgID, File associations)
        ScanClassesAndApplications(appTokens, results, seenPaths);

        // 6. Scan Startup items (Run/RunOnce)
        ScanStartupRunKeys(appTokens, results, seenPaths);

        // 7. Scan AppCompatibility Flags (DPI / compatibility registry entries)
        ScanAppCompatibility(appTokens, app.InstallLocation, results, seenPaths);

        // 8. Scan Windows Services remnants
        ScanServices(appTokens, app.InstallLocation, results, seenPaths);

        // 9. Scan Orphaned Uninstall Registry Keys
        ScanOrphanedUninstallKeys(app, appTokens, results, seenPaths);

        return results;
    }

    private void ScanDirectoryForMatches(
        string root,
        List<string> appTokens,
        List<string> publisherTokens,
        List<LeftoverItem> results,
        HashSet<string> seenPaths)
    {
        try
        {
            var directories = Directory.EnumerateDirectories(root);
            foreach (var dir in directories)
            {
                var dirName = Path.GetFileName(dir);
                if (ProtectedDirectoryNames.Contains(dirName))
                    continue;

                // Match publisher folder, then look inside
                if (publisherTokens.Any(p => dirName.Contains(p, StringComparison.OrdinalIgnoreCase)))
                {
                    try
                    {
                        var subDirs = Directory.EnumerateDirectories(dir);
                        foreach (var subDir in subDirs)
                        {
                            var subName = Path.GetFileName(subDir);
                            if (appTokens.Any(t => subName.Contains(t, StringComparison.OrdinalIgnoreCase)))
                            {
                                AddResult(results, seenPaths, new LeftoverItem
                                {
                                    Path = subDir,
                                    Type = LeftoverType.Directory,
                                    Confidence = ConfidenceLevel.High,
                                    Reason = "Residual folder in publisher directory",
                                    SizeBytes = SafeGetDirectorySize(subDir)
                                });
                            }
                        }
                    }
                    catch { }
                }

                // Or direct match on app name
                if (appTokens.Any(t => string.Equals(dirName, t, StringComparison.OrdinalIgnoreCase) ||
                                       dirName.Contains(t, StringComparison.OrdinalIgnoreCase)))
                {
                    AddResult(results, seenPaths, new LeftoverItem
                    {
                        Path = dir,
                        Type = LeftoverType.Directory,
                        Confidence = ConfidenceLevel.High,
                        Reason = "Residual app data files (AppData / ProgramData)",
                        SizeBytes = SafeGetDirectorySize(dir)
                    });
                }
            }
        }
        catch { }
    }

    private void ScanRegistryTree(
        RegistryHive hive,
        RegistryView view,
        List<string> appTokens,
        List<string> publisherTokens,
        List<LeftoverItem> results,
        HashSet<string> seenPaths)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var softwareKey = baseKey.OpenSubKey("Software");
            if (softwareKey == null) return;

            foreach (var vendorKeyName in softwareKey.GetSubKeyNames())
            {
                if (ProtectedDirectoryNames.Contains(vendorKeyName))
                    continue;

                // Match publisher in Software\<Vendor>
                if (publisherTokens.Any(p => vendorKeyName.Contains(p, StringComparison.OrdinalIgnoreCase)))
                {
                    try
                    {
                        using var vendorKey = softwareKey.OpenSubKey(vendorKeyName);
                        if (vendorKey != null)
                        {
                            foreach (var appSubKey in vendorKey.GetSubKeyNames())
                            {
                                if (appTokens.Any(t => appSubKey.Contains(t, StringComparison.OrdinalIgnoreCase)))
                                {
                                    var fullKeyPath = $@"{hive}\{view}\Software\{vendorKeyName}\{appSubKey}";
                                    AddResult(results, seenPaths, new LeftoverItem
                                    {
                                        Path = fullKeyPath,
                                        Type = LeftoverType.RegistryKey,
                                        Confidence = ConfidenceLevel.High,
                                        Reason = "App configuration and settings key in registry"
                                    });
                                }
                            }
                        }
                    }
                    catch { }
                }

                // Match app name directly in Software\<AppName>
                if (appTokens.Any(t => string.Equals(vendorKeyName, t, StringComparison.OrdinalIgnoreCase)))
                {
                    var fullKeyPath = $@"{hive}\{view}\Software\{vendorKeyName}";
                    AddResult(results, seenPaths, new LeftoverItem
                    {
                        Path = fullKeyPath,
                        Type = LeftoverType.RegistryKey,
                        Confidence = ConfidenceLevel.High,
                        Reason = "Main application registry key (Software)"
                    });
                }
            }
        }
        catch { }
    }

    private void ScanAppPaths(List<string> appTokens, List<LeftoverItem> results, HashSet<string> seenPaths)
    {
        var targets = new (RegistryHive Hive, RegistryView View)[]
        {
            (RegistryHive.CurrentUser, RegistryView.Default),
            (RegistryHive.LocalMachine, RegistryView.Registry64),
            (RegistryHive.LocalMachine, RegistryView.Registry32)
        };

        foreach (var (hive, view) in targets)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var appPathsKey = baseKey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths");
                if (appPathsKey == null) continue;

                foreach (var subKeyName in appPathsKey.GetSubKeyNames())
                {
                    if (appTokens.Any(t => subKeyName.Contains(t, StringComparison.OrdinalIgnoreCase)))
                    {
                        var fullKeyPath = $@"{hive}\{view}\Software\Microsoft\Windows\CurrentVersion\App Paths\{subKeyName}";
                        AddResult(results, seenPaths, new LeftoverItem
                        {
                            Path = fullKeyPath,
                            Type = LeftoverType.RegistryKey,
                            Confidence = ConfidenceLevel.High,
                            Reason = "Execution path registration (App Paths in registry)"
                        });
                    }
                }
            }
            catch { }
        }
    }

    private void ScanClassesAndApplications(List<string> appTokens, List<LeftoverItem> results, HashSet<string> seenPaths)
    {
        var targets = new (RegistryHive Hive, RegistryView View)[]
        {
            (RegistryHive.CurrentUser, RegistryView.Default),
            (RegistryHive.ClassesRoot, RegistryView.Default)
        };

        foreach (var (hive, view) in targets)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                var appsPath = hive == RegistryHive.ClassesRoot ? "Applications" : @"Software\Classes\Applications";
                using var appsKey = baseKey.OpenSubKey(appsPath);
                if (appsKey != null)
                {
                    foreach (var appSubName in appsKey.GetSubKeyNames())
                    {
                        if (appTokens.Any(t => appSubName.Contains(t, StringComparison.OrdinalIgnoreCase)))
                        {
                            var fullKeyPath = $@"{hive}\{view}\{appsPath}\{appSubName}";
                            AddResult(results, seenPaths, new LeftoverItem
                            {
                                Path = fullKeyPath,
                                Type = LeftoverType.RegistryKey,
                                Confidence = ConfidenceLevel.High,
                                Reason = "File association & shell registration (Classes\\Applications)"
                            });
                        }
                    }
                }
            }
            catch { }
        }
    }

    private void ScanAppCompatibility(List<string> appTokens, string? installDir, List<LeftoverItem> results, HashSet<string> seenPaths)
    {
        var hives = new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine };
        foreach (var hive in hives)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
                using var layersKey = baseKey.OpenSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers");
                if (layersKey == null) continue;

                foreach (var valName in layersKey.GetValueNames())
                {
                    bool match = appTokens.Any(t => valName.Contains(t, StringComparison.OrdinalIgnoreCase)) ||
                                 (!string.IsNullOrEmpty(installDir) && valName.StartsWith(installDir, StringComparison.OrdinalIgnoreCase));

                    if (match)
                    {
                        AddResult(results, seenPaths, new LeftoverItem
                        {
                            Path = $@"{hive}\Default\Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers\{valName}",
                            Type = LeftoverType.RegistryValue,
                            Confidence = ConfidenceLevel.Medium,
                            Reason = "Application compatibility flags (AppCompatFlags)"
                        });
                    }
                }
            }
            catch { }
        }
    }

    private void ScanServices(List<string> appTokens, string? installDir, List<LeftoverItem> results, HashSet<string> seenPaths)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var servicesKey = baseKey.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
            if (servicesKey == null) return;

            foreach (var svcName in servicesKey.GetSubKeyNames())
            {
                if (ProtectedDirectoryNames.Contains(svcName)) continue;

                try
                {
                    using var svc = servicesKey.OpenSubKey(svcName);
                    if (svc == null) continue;

                    var displayName = svc.GetValue("DisplayName")?.ToString() ?? string.Empty;
                    var imagePath = svc.GetValue("ImagePath")?.ToString() ?? string.Empty;

                    bool match = appTokens.Any(t => svcName.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                                                    displayName.Contains(t, StringComparison.OrdinalIgnoreCase)) ||
                                 (!string.IsNullOrEmpty(installDir) && imagePath.Contains(installDir, StringComparison.OrdinalIgnoreCase));

                    if (match)
                    {
                        AddResult(results, seenPaths, new LeftoverItem
                        {
                            Path = $@"LocalMachine\Registry64\SYSTEM\CurrentControlSet\Services\{svcName}",
                            Type = LeftoverType.Service,
                            Confidence = ConfidenceLevel.High,
                            Reason = $"Residual Windows background service: {displayName}"
                        });
                    }
                }
                catch { }
            }
        }
        catch { }
    }

    private void ScanOrphanedUninstallKeys(InstalledApp app, List<string> appTokens, List<LeftoverItem> results, HashSet<string> seenPaths)
    {
        var targets = new (RegistryHive Hive, RegistryView View)[]
        {
            (RegistryHive.CurrentUser, RegistryView.Default),
            (RegistryHive.LocalMachine, RegistryView.Registry64),
            (RegistryHive.LocalMachine, RegistryView.Registry32)
        };

        foreach (var (hive, view) in targets)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var uninstallKey = baseKey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstallKey == null) continue;

                foreach (var subKeyName in uninstallKey.GetSubKeyNames())
                {
                    // Check if it matches app.Id or name after uninstall
                    if (string.Equals(subKeyName, app.Id, StringComparison.OrdinalIgnoreCase) ||
                        appTokens.Any(t => string.Equals(subKeyName, t, StringComparison.OrdinalIgnoreCase)))
                    {
                        var fullKeyPath = $@"{hive}\{view}\Software\Microsoft\Windows\CurrentVersion\Uninstall\{subKeyName}";
                        AddResult(results, seenPaths, new LeftoverItem
                        {
                            Path = fullKeyPath,
                            Type = LeftoverType.RegistryKey,
                            Confidence = ConfidenceLevel.High,
                            Reason = "Orphaned uninstall entry in registry (Uninstall Key)"
                        });
                    }
                }
            }
            catch { }
        }
    }

    private void ScanStartupRunKeys(List<string> appTokens, List<LeftoverItem> results, HashSet<string> seenPaths)
    {
        var runPaths = new[]
        {
            @"Software\Microsoft\Windows\CurrentVersion\Run",
            @"Software\Microsoft\Windows\CurrentVersion\RunOnce"
        };

        foreach (var runPath in runPaths)
        {
            CheckRunKey(RegistryHive.CurrentUser, RegistryView.Default, runPath, appTokens, results, seenPaths);
            CheckRunKey(RegistryHive.LocalMachine, RegistryView.Registry64, runPath, appTokens, results, seenPaths);
            if (Environment.Is64BitOperatingSystem)
            {
                CheckRunKey(RegistryHive.LocalMachine, RegistryView.Registry32, runPath, appTokens, results, seenPaths);
            }
        }
    }

    private void CheckRunKey(RegistryHive hive, RegistryView view, string subKeyPath, List<string> appTokens, List<LeftoverItem> results, HashSet<string> seenPaths)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var runKey = baseKey.OpenSubKey(subKeyPath);
            if (runKey == null) return;

            foreach (var valName in runKey.GetValueNames())
            {
                var valData = runKey.GetValue(valName)?.ToString() ?? string.Empty;
                if (appTokens.Any(t => valName.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                                       valData.Contains(t, StringComparison.OrdinalIgnoreCase)))
                {
                    AddResult(results, seenPaths, new LeftoverItem
                    {
                        Path = $@"{hive}\{view}\{subKeyPath}\{valName} -> {valData}",
                        Type = LeftoverType.RegistryValue,
                        Confidence = ConfidenceLevel.Medium,
                        Reason = "Startup run entry in registry (Run/RunOnce)"
                    });
                }
            }
        }
        catch { }
    }

    private static void AddResult(List<LeftoverItem> results, HashSet<string> seenPaths, LeftoverItem item)
    {
        if (seenPaths.Add(item.Path))
        {
            results.Add(item);
        }
    }

    private static List<string> ExtractTokens(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return new List<string>();

        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { input.Trim() };

        // Split by punctuation and spaces
        var parts = input.Split(new[] { ' ', '-', '_', '.', ',', '(', ')' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            if (part.Length >= 3 && !IsGenericStopWord(part))
            {
                tokens.Add(part);
            }
        }

        return tokens.ToList();
    }

    private static bool IsGenericStopWord(string word)
    {
        var lower = word.ToLowerInvariant();
        return lower is "the" or "pro" or "free" or "edition" or "version" or "software" or "tool" or "utility" or "setup" or "client" or "installer";
    }

    private static bool IsSystemRootOrProtected(string path)
    {
        var root = Path.GetPathRoot(path);
        if (string.Equals(path.TrimEnd('\\', '/'), root?.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
            return true;

        var windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        if (string.Equals(path, windir, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(path, programFiles, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(path, programFilesX86, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static long SafeGetDirectorySize(string path)
    {
        try
        {
            return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .Select(f => {
                    try { return new FileInfo(f).Length; } catch { return 0L; }
                })
                .Sum();
        }
        catch
        {
            return 0L;
        }
    }
}
