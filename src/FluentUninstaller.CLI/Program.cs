using FluentUninstaller.Core.Engines;
using FluentUninstaller.Core.Models;
using FluentUninstaller.Core.Scanners;

Console.OutputEncoding = System.Text.Encoding.UTF8;

Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine("=================================================");
Console.WriteLine("        FluentUninstaller CLI v1.1.0             ");
Console.WriteLine("    Open-Source App Scanner & Icon Inspector     ");
Console.WriteLine("=================================================");
Console.ResetColor();
Console.WriteLine();

var scanner = new Win32RegistryScanner();
var leftoverEngine = new LeftoverScanEngine();

Console.Write("Scanning installed applications... ");
var apps = scanner.Scan().ToList();
Console.ForegroundColor = ConsoleColor.Green;
Console.WriteLine($"Found {apps.Count} applications.");
Console.ResetColor();
Console.WriteLine();

while (true)
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine("Commands: [1] List All  [2] Search & Details  [3] Leftovers Scan  [4] Stats  [q] Quit");
    Console.ResetColor();
    Console.Write("Select an option: ");
    var input = Console.ReadLine()?.Trim();

    if (string.Equals(input, "q", StringComparison.OrdinalIgnoreCase))
    {
        break;
    }

    switch (input)
    {
        case "1":
            ListApps(apps);
            break;

        case "2":
            Console.Write("Enter search query: ");
            var query = Console.ReadLine()?.Trim() ?? "";
            var matches = apps.Where(a => a.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                          (a.Publisher != null && a.Publisher.Contains(query, StringComparison.OrdinalIgnoreCase)))
                              .ToList();
            Console.WriteLine($"\nFound {matches.Count} matching applications:");
            foreach (var match in matches)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"\n• {match.DisplayName} (v{match.DisplayVersion ?? "N/A"})");
                Console.ResetColor();
                Console.WriteLine($"  Publisher:       {match.Publisher ?? "Unknown"}");
                Console.WriteLine($"  Size:            {match.SizeFormatted}");
                Console.WriteLine($"  Install Date:    {match.InstallDate ?? "Unknown"}");
                Console.WriteLine($"  Install Path:    {match.InstallLocation ?? "N/A"}");
                Console.WriteLine($"  DisplayIcon:     {match.DisplayIcon ?? "None (auto-fallback)"}");
                Console.WriteLine($"  Uninstall Cmd:   {match.UninstallString ?? "N/A"}");
                Console.WriteLine($"  Quiet Cmd:       {match.QuietUninstallString ?? "None"}");
            }
            Console.WriteLine();
            break;

        case "3":
            Console.Write("Enter exact or partial app name to scan for leftovers: ");
            var appQuery = Console.ReadLine()?.Trim() ?? "";
            var targetApp = apps.FirstOrDefault(a => a.DisplayName.Contains(appQuery, StringComparison.OrdinalIgnoreCase));
            if (targetApp == null)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Application not found!");
                Console.ResetColor();
                break;
            }

            Console.WriteLine($"\nTarget: {targetApp.DisplayName} (Publisher: {targetApp.Publisher ?? "N/A"})");
            Console.Write("Scanning for leftover folders, files, and registry keys... ");
            var leftovers = await leftoverEngine.ScanAsync(targetApp);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Found {leftovers.Count} residual items.\n");
            Console.ResetColor();

            foreach (var item in leftovers)
            {
                Console.ForegroundColor = item.Type switch
                {
                    LeftoverType.Directory => ConsoleColor.Blue,
                    LeftoverType.RegistryKey => ConsoleColor.Magenta,
                    LeftoverType.RegistryValue => ConsoleColor.DarkMagenta,
                    _ => ConsoleColor.Gray
                };
                Console.WriteLine($"[{item.TypeDisplay}] {item.Path}");
                Console.ResetColor();
                Console.WriteLine($"    Size: {item.SizeFormatted} | Reason: {item.Reason}");
            }
            Console.WriteLine();
            break;

        case "4":
            var totalEstimatedKb = apps.Sum(a => a.EstimatedSizeInKb ?? 0);
            var withDirectIcon = apps.Count(a => !string.IsNullOrWhiteSpace(a.DisplayIcon));
            Console.WriteLine($"Total Applications:    {apps.Count}");
            Console.WriteLine($"Total Estimated Size:  {totalEstimatedKb / 1024.0 / 1024.0:F2} GB");
            Console.WriteLine($"Direct Icons defined:  {withDirectIcon} / {apps.Count} ({withDirectIcon * 100 / apps.Count}%)");
            Console.WriteLine($"Fallback to exe/dir:   {apps.Count - withDirectIcon} apps");
            Console.WriteLine($"64-bit Apps:           {apps.Count(a => a.Is64Bit)}");
            Console.WriteLine($"32-bit Apps:           {apps.Count(a => !a.Is64Bit)}");
            Console.WriteLine($"MSI Installers:        {apps.Count(a => a.Type == AppType.Msi)}");
            Console.WriteLine();
            break;

        default:
            Console.WriteLine("Unknown command.");
            break;
    }
}

static void ListApps(List<InstalledApp> list)
{
    Console.WriteLine(new string('-', 100));
    Console.WriteLine($"{"Name",-45} | {"Version",-15} | {"Size",-10} | {"Publisher",-25}");
    Console.WriteLine(new string('-', 100));

    foreach (var app in list)
    {
        var name = Truncate(app.DisplayName, 43);
        var ver = Truncate(app.DisplayVersion ?? "-", 13);
        var pub = Truncate(app.Publisher ?? "-", 23);
        Console.WriteLine($"{name,-45} | {ver,-15} | {app.SizeFormatted,-10} | {pub,-25}");
    }
    Console.WriteLine(new string('-', 100));
    Console.WriteLine();
}

static string Truncate(string val, int maxLen)
{
    if (string.IsNullOrEmpty(val)) return "-";
    return val.Length <= maxLen ? val : val[..(maxLen - 3)] + "...";
}
