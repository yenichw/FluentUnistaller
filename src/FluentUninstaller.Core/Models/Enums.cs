namespace FluentUninstaller.Core.Models;

public enum AppType
{
    Win32,
    Msi,
    Uwp,
    WindowsUpdate
}

public enum LeftoverType
{
    File,
    Directory,
    RegistryKey,
    RegistryValue,
    Service,
    ScheduledTask
}

public enum ConfidenceLevel
{
    High,
    Medium,
    Low
}
