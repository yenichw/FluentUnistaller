using System.Diagnostics;
using FluentUninstaller.Core.Models;

namespace FluentUninstaller.Core.Engines;

public class UninstallExecutionResult
{
    public bool Success { get; init; }
    public int ExitCode { get; init; }
    public string? ErrorMessage { get; init; }
}

public class UninstallEngine
{
    public async Task<UninstallExecutionResult> RunUninstallAsync(
        InstalledApp app,
        bool silent = false,
        CancellationToken cancellationToken = default)
    {
        var rawCommand = silent && !string.IsNullOrWhiteSpace(app.QuietUninstallString)
            ? app.QuietUninstallString
            : app.UninstallString;

        if (string.IsNullOrWhiteSpace(rawCommand))
        {
            return new UninstallExecutionResult
            {
                Success = false,
                ErrorMessage = "No uninstall command specified for this application."
            };
        }

        try
        {
            var (exe, args) = SplitCommand(rawCommand);

            var startInfo = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                UseShellExecute = true, // Required for UAC elevation if needed
                Verb = "runas"
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                return new UninstallExecutionResult
                {
                    Success = false,
                    ErrorMessage = "Failed to launch uninstaller process."
                };
            }

            await process.WaitForExitAsync(cancellationToken);

            return new UninstallExecutionResult
            {
                Success = process.ExitCode == 0,
                ExitCode = process.ExitCode
            };
        }
        catch (Exception ex)
        {
            return new UninstallExecutionResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    private static (string Exe, string Args) SplitCommand(string raw)
    {
        raw = raw.Trim();
        if (raw.StartsWith('\"'))
        {
            var nextQuote = raw.IndexOf('\"', 1);
            if (nextQuote > 1)
            {
                var exe = raw.Substring(1, nextQuote - 1);
                var args = raw.Length > nextQuote + 1 ? raw[(nextQuote + 1)..].Trim() : string.Empty;
                return (exe, args);
            }
        }

        var spaceIdx = raw.IndexOf(' ');
        if (spaceIdx > 0 && !File.Exists(raw))
        {
            var exe = raw[..spaceIdx];
            var args = raw[(spaceIdx + 1)..].Trim();
            return (exe, args);
        }

        return (raw, string.Empty);
    }
}
