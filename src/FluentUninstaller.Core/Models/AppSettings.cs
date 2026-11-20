using System.Text.Json;

namespace FluentUninstaller.Core.Models;

public enum AutoCleanMode
{
    Ask,         // Ask (Toast notification prompt)
    AlwaysClean, // Always clean automatically
    NeverClean   // Never clean automatically
}

public class AppSettings
{
    public AutoCleanMode AutoClean { get; set; } = AutoCleanMode.Ask;
    public bool DarkTheme { get; set; } = false;
    public bool StartWithWindows { get; set; } = false;

    private static readonly string SettingsFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
        "FluentUninstaller");

    private static readonly string SettingsFile = Path.Combine(SettingsFolder, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsFile))
            {
                var json = File.ReadAllText(SettingsFile);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch { }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsFolder);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFile, json);
        }
        catch { }
    }
}
