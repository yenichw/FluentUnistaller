namespace FluentUninstaller.Core.Models;

public class InstalledApp
{
    public required string Id { get; init; }
    public required string DisplayName { get; set; }
    public string? Publisher { get; set; }
    public string? DisplayVersion { get; set; }
    public string? InstallDate { get; set; }
    public string? InstallLocation { get; set; }
    public string? UninstallString { get; set; }
    public string? QuietUninstallString { get; set; }
    public long? EstimatedSizeInKb { get; set; }
    public string? DisplayIcon { get; set; }
    public bool Is64Bit { get; set; }
    public AppType Type { get; set; } = AppType.Win32;
    public string? RegistryKeyPath { get; set; }
    public string? PackageFullName { get; set; } // For UWP
    public bool IsSystemComponent { get; set; }

    public string SizeFormatted
    {
        get
        {
            if (EstimatedSizeInKb == null || EstimatedSizeInKb <= 0)
                return "Unknown";

            double mb = EstimatedSizeInKb.Value / 1024.0;
            if (mb >= 1024.0)
                return $"{mb / 1024.0:F2} GB";
            
            return $"{mb:F1} MB";
        }
    }

    public override string ToString() => $"{DisplayName} ({DisplayVersion ?? "No version"}) - {Publisher ?? "Unknown publisher"}";
}
