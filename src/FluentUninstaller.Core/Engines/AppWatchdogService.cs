using FluentUninstaller.Core.Models;
using FluentUninstaller.Core.Scanners;

namespace FluentUninstaller.Core.Engines;

public class AppWatchdogService : IDisposable
{
    private readonly Win32RegistryScanner _scanner;
    private readonly Dictionary<string, InstalledApp> _knownApps = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _cts = new();
    private Task? _monitorTask;
    private bool _isPaused = false;

    public event Action<InstalledApp>? AppUninstalledExternally;
    public event Action<InstalledApp>? AppInstalledExternally;

    public bool IsRunning { get; private set; }

    public AppWatchdogService(Win32RegistryScanner? scanner = null)
    {
        _scanner = scanner ?? new Win32RegistryScanner();
    }

    public void Start(IEnumerable<InstalledApp>? initialSnapshot = null)
    {
        if (IsRunning) return;

        lock (_knownApps)
        {
            _knownApps.Clear();
            var apps = initialSnapshot ?? _scanner.Scan();
            foreach (var app in apps)
            {
                _knownApps[app.Id] = app;
            }
        }

        IsRunning = true;
        _monitorTask = Task.Run(() => MonitorLoopAsync(_cts.Token));
    }

    public void Pause() => _isPaused = true;
    public void Resume() => _isPaused = false;

    public void UpdateSnapshot(IEnumerable<InstalledApp> currentApps)
    {
        lock (_knownApps)
        {
            // Only add or update, do not blindly erase during active monitoring
            foreach (var app in currentApps)
            {
                _knownApps[app.Id] = app;
            }
        }
    }

    public void RemoveFromSnapshot(string appId)
    {
        lock (_knownApps)
        {
            _knownApps.Remove(appId);
        }
    }

    private async Task MonitorLoopAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1.5));

        while (!token.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(token);

                if (_isPaused) continue;

                var currentList = _scanner.Scan().ToList();
                var currentMap = currentList.ToDictionary(a => a.Id, a => a, StringComparer.OrdinalIgnoreCase);

                List<InstalledApp> removedApps = new();
                List<InstalledApp> addedApps = new();

                lock (_knownApps)
                {
                    // Detect uninstalled apps: was in known, but not in current
                    foreach (var known in _knownApps.Values.ToList())
                    {
                        var existsById = currentMap.ContainsKey(known.Id);
                        var existsByName = currentList.Any(c => string.Equals(c.DisplayName, known.DisplayName, StringComparison.OrdinalIgnoreCase));

                        if (!existsById && !existsByName)
                        {
                            removedApps.Add(known);
                            _knownApps.Remove(known.Id);
                        }
                    }

                    // Detect newly installed apps: was not in known, but in current
                    foreach (var curr in currentList)
                    {
                        var knownById = _knownApps.ContainsKey(curr.Id);
                        var knownByName = _knownApps.Values.Any(k => string.Equals(k.DisplayName, curr.DisplayName, StringComparison.OrdinalIgnoreCase));

                        if (!knownById && !knownByName)
                        {
                            addedApps.Add(curr);
                            _knownApps[curr.Id] = curr;
                        }
                    }
                }

                // Fire notifications
                foreach (var removed in removedApps)
                {
                    AppUninstalledExternally?.Invoke(removed);
                }

                foreach (var added in addedApps)
                {
                    AppInstalledExternally?.Invoke(added);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Resilient against transient registry reading errors
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        IsRunning = false;
    }
}
