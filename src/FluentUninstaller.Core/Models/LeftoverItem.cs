namespace FluentUninstaller.Core.Models;

public class LeftoverItem
{
    public required string Path { get; init; }
    public required LeftoverType Type { get; init; }
    public ConfidenceLevel Confidence { get; init; } = ConfidenceLevel.High;
    public string Reason { get; init; } = string.Empty;
    public long? SizeBytes { get; set; }
    public bool IsSelected { get; set; } = true;

    public bool IsRegistry => Type is LeftoverType.RegistryKey or LeftoverType.RegistryValue;
    public bool IsFileSystem => Type is LeftoverType.Directory or LeftoverType.File;

    public string TypeDisplay => Type switch
    {
        LeftoverType.Directory => "Folder",
        LeftoverType.File => "File",
        LeftoverType.RegistryKey => "Registry Key",
        LeftoverType.RegistryValue => "Registry Value",
        LeftoverType.Service => "Windows Service",
        LeftoverType.ScheduledTask => "Scheduled Task",
        _ => "Other"
    };

    public string IconSymbolName => Type switch
    {
        LeftoverType.Directory => "Folder24",
        LeftoverType.File => "Document24",
        LeftoverType.RegistryKey => "Table24",
        LeftoverType.RegistryValue => "Code24",
        LeftoverType.Service => "Server24",
        _ => "Cube24"
    };

    public string SizeFormatted
    {
        get
        {
            if (SizeBytes == null || SizeBytes <= 0)
                return IsRegistry ? "Registry" : "0 B";

            if (SizeBytes >= 1024 * 1024 * 1024)
                return $"{SizeBytes.Value / (1024.0 * 1024 * 1024):F2} GB";
            if (SizeBytes >= 1024 * 1024)
                return $"{SizeBytes.Value / (1024.0 * 1024):F2} MB";
            if (SizeBytes >= 1024)
                return $"{SizeBytes.Value / 1024.0:F1} KB";

            return $"{SizeBytes} B";
        }
    }
}
