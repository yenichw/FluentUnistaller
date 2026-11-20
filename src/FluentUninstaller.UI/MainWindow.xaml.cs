using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;
using FluentUninstaller.Core.Engines;
using FluentUninstaller.Core.Models;
using FluentUninstaller.Core.Scanners;
using FluentUninstaller.Core.Services;
using FluentUninstaller.UI.Services;
using FluentUninstaller.UI.ViewModels;
using FluentUninstaller.UI.Views;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using SysMessageBox = System.Windows.MessageBox;
using SysMessageBoxButton = System.Windows.MessageBoxButton;
using SysMessageBoxImage = System.Windows.MessageBoxImage;
using SysMessageBoxResult = System.Windows.MessageBoxResult;
using WpfMenuItem = System.Windows.Controls.MenuItem;

namespace FluentUninstaller.UI;

public partial class MainWindow : FluentWindow
{
    private readonly Win32RegistryScanner _scanner = new();
    private readonly LeftoverScanEngine _leftoverEngine = new();
    private readonly UninstallEngine _uninstallEngine = new();
    private readonly CleanerEngine _cleanerEngine = new();
    private readonly IconExtractorService _iconService = new();
    private readonly AppWatchdogService _watchdog = new();

    private readonly AppSettings _settings = AppSettings.Load();

    private List<AppItemViewModel> _allViewModels = new();
    public ObservableCollection<AppItemViewModel> FilteredApps { get; } = new();
    public ObservableCollection<LeftoverItem> FoundLeftovers { get; } = new();

    private bool _isInitialized = false;

    public MainWindow()
    {
        InitializeComponent();
        _isInitialized = true;

        AppsDataGrid.ItemsSource = FilteredApps;
        LeftoverListView.ItemsSource = FoundLeftovers;
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _watchdog.Dispose();
        _settings.Save();
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        FilterAllRadio.Checked += Filter_Changed;
        FilterLargeRadio.Checked += Filter_Changed;
        FilterMsiRadio.Checked += Filter_Changed;

        if (_settings.DarkTheme)
        {
            ApplicationThemeManager.Apply(ApplicationTheme.Dark);
        }
        else
        {
            ApplicationThemeManager.Apply(ApplicationTheme.Light);
        }

        UpdateThemeToggleButtonIcon();
        UpdateThemeAppIcon(_settings.DarkTheme);
        UpdateSettingsMenuUi();

        // Register watchdog event before starting
        _watchdog.AppUninstalledExternally += OnAppUninstalledExternally;

        await ReloadAppsAsync();
    }

    private async Task ReloadAppsAsync()
    {
        StatusText.Text = "Scanning registry and system...";
        AppsDataGrid.IsEnabled = false;

        var defaultIcon = _iconService.GetDefaultExeIcon();
        var rawApps = await Task.Run(() => _scanner.Scan().ToList());

        if (!_watchdog.IsRunning)
        {
            _watchdog.Start(rawApps);
        }
        else
        {
            _watchdog.UpdateSnapshot(rawApps);
        }

        _allViewModels = rawApps.Select(a => new AppItemViewModel(a, defaultIcon)).ToList();

        ApplyFilters();

        var totalSizeGb = rawApps.Sum(a => a.EstimatedSizeInKb ?? 0) / (1024.0 * 1024.0);
        StatusText.Text = $"Found applications: {rawApps.Count} | Total size: {totalSizeGb:F2} GB | Watchdog: {_settings.AutoClean}";
        AppsDataGrid.IsEnabled = true;

        _ = LoadIconsInBackgroundAsync(_allViewModels);
    }

    private async Task LoadIconsInBackgroundAsync(List<AppItemViewModel> viewModels)
    {
        await Parallel.ForEachAsync(viewModels, new ParallelOptions { MaxDegreeOfParallelism = 4 }, async (vm, ct) =>
        {
            await vm.LoadIconAsync(_iconService);
        });

        Dispatcher.Invoke(() =>
        {
            if (StatusText.Text.Contains("Loading icons"))
            {
                var totalSizeGb = _allViewModels.Sum(a => a.App.EstimatedSizeInKb ?? 0) / (1024.0 * 1024.0);
                StatusText.Text = $"Found applications: {_allViewModels.Count} | Total size: {totalSizeGb:F2} GB";
            }

            if (AppsDataGrid.SelectedItem is AppItemViewModel current)
            {
                SelectedAppIcon.Source = current.Icon ?? _iconService.GetDefaultExeIcon();
            }
        });
    }

    private void ApplyFilters()
    {
        if (!_isInitialized || SearchBox == null || FilterLargeRadio == null || FilterMsiRadio == null)
            return;

        var query = SearchBox.Text?.Trim() ?? string.Empty;
        var isLarge = FilterLargeRadio.IsChecked == true;
        var isMsi = FilterMsiRadio.IsChecked == true;

        var filtered = _allViewModels.AsEnumerable();

        if (!string.IsNullOrEmpty(query))
        {
            filtered = filtered.Where(vm =>
                vm.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (vm.Publisher != null && vm.Publisher.Contains(query, StringComparison.OrdinalIgnoreCase)));
        }

        if (isLarge)
        {
            filtered = filtered.Where(vm => (vm.App.EstimatedSizeInKb ?? 0) >= 500 * 1024);
        }
        else if (isMsi)
        {
            filtered = filtered.Where(vm => vm.Type == AppType.Msi);
        }

        FilteredApps.Clear();
        foreach (var vm in filtered)
        {
            FilteredApps.Add(vm);
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilters();
    private void Filter_Changed(object sender, RoutedEventArgs e) => ApplyFilters();

    private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        await ReloadAppsAsync();
    }

    private void AppsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AppsDataGrid.SelectedItem is AppItemViewModel selected)
        {
            NoSelectionPlaceholder.Visibility = Visibility.Collapsed;
            AppDetailsPanel.Visibility = Visibility.Visible;

            DetailAppName.Text = selected.DisplayName;
            DetailAppPublisher.Text = selected.Publisher ?? "Unknown publisher";
            DetailVersion.Text = selected.DisplayVersion ?? "N/A";
            DetailSize.Text = selected.SizeFormatted;
            DetailPath.Text = string.IsNullOrEmpty(selected.App.InstallLocation)
                ? (selected.App.DisplayIcon ?? "Not specified in registry")
                : selected.App.InstallLocation;

            SelectedAppIcon.Source = selected.Icon ?? _iconService.GetDefaultExeIcon();

            FoundLeftovers.Clear();
            LeftoversScanPlaceholder.Visibility = Visibility.Visible;
            LeftoverListView.Visibility = Visibility.Collapsed;
            BtnCleanLeftovers.Visibility = Visibility.Collapsed;
            LeftoverCountBadge.Text = " (0)";
        }
        else
        {
            NoSelectionPlaceholder.Visibility = Visibility.Visible;
            AppDetailsPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void AppsDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (AppsDataGrid.SelectedItem is AppItemViewModel)
        {
            BtnScanLeftovers_Click(sender, e);
        }
    }

    private async void BtnUninstall_Click(object sender, RoutedEventArgs e)
    {
        if (AppsDataGrid.SelectedItem is not AppItemViewModel selected)
        {
            SysMessageBox.Show("Please select an application from the list.", "Fluent Uninstaller", SysMessageBoxButton.OK, SysMessageBoxImage.Information);
            return;
        }

        var app = selected.App;
        var confirm = SysMessageBox.Show(
            $"Are you sure you want to start standard uninstall for \"{app.DisplayName}\"?\n\nAfter uninstaller finishes, residual files and registry scan will begin.",
            "Confirm Uninstall",
            SysMessageBoxButton.YesNo,
            SysMessageBoxImage.Question);

        if (confirm != SysMessageBoxResult.Yes) return;

        StatusText.Text = $"Launching uninstaller for {app.DisplayName}...";

        var result = await _uninstallEngine.RunUninstallAsync(app);

        if (!result.Success)
        {
            SysMessageBox.Show(
                $"Uninstaller exited with an error: {result.ErrorMessage}\n(Exit code: {result.ExitCode})",
                "Uninstall Error",
                SysMessageBoxButton.OK,
                SysMessageBoxImage.Error);
        }

        StatusText.Text = $"Scanning leftovers (files, registry) after uninstalling {app.DisplayName}...";
        await ScanLeftoversForAppAsync(app);

        // Auto-refresh the application list after uninstallation
        await ReloadAppsAsync();
    }

    private async void BtnScanLeftovers_Click(object sender, RoutedEventArgs e)
    {
        if (AppsDataGrid.SelectedItem is not AppItemViewModel selected) return;
        await ScanLeftoversForAppAsync(selected.App);
    }

    private async Task ScanLeftoversForAppAsync(InstalledApp app)
    {
        StatusText.Text = $"Deep residual scan for: {app.DisplayName}...";
        LeftoversScanPlaceholder.Visibility = Visibility.Visible;
        LeftoverListView.Visibility = Visibility.Collapsed;
        BtnCleanLeftovers.Visibility = Visibility.Collapsed;
        FoundLeftovers.Clear();

        var leftovers = await _leftoverEngine.ScanAsync(app);

        foreach (var item in leftovers)
        {
            FoundLeftovers.Add(item);
        }

        var fileCount = FoundLeftovers.Count(l => l.IsFileSystem);
        var regCount = FoundLeftovers.Count(l => l.IsRegistry);
        var totalSize = FoundLeftovers.Sum(l => l.SizeBytes ?? 0);
        var sizeStr = totalSize > 0 ? $" (~{totalSize / 1024.0 / 1024.0:F2} MB)" : "";

        if (regCount > 0 && fileCount > 0)
        {
            LeftoverCountBadge.Text = $" (Files: {fileCount}{sizeStr}, Registry: {regCount})";
        }
        else if (regCount > 0)
        {
            LeftoverCountBadge.Text = $" (Registry: {regCount})";
        }
        else
        {
            LeftoverCountBadge.Text = $" (Files: {fileCount}{sizeStr})";
        }

        if (FoundLeftovers.Count > 0)
        {
            LeftoversScanPlaceholder.Visibility = Visibility.Collapsed;
            LeftoverListView.Visibility = Visibility.Visible;
            BtnCleanLeftovers.Visibility = Visibility.Visible;

            StatusText.Text = $"Found leftovers: {fileCount} folders/files{sizeStr} and {regCount} registry entries.";
        }
        else
        {
            StatusText.Text = $"No leftover files or registry entries found for \"{app.DisplayName}\".";
        }
    }

    private async void BtnCleanLeftovers_Click(object sender, RoutedEventArgs e)
    {
        var selected = FoundLeftovers.Where(l => l.IsSelected).ToList();
        if (selected.Count == 0)
        {
            SysMessageBox.Show("No items selected for cleanup.", "Fluent Uninstaller", SysMessageBoxButton.OK, SysMessageBoxImage.Warning);
            return;
        }

        var selectedFiles = selected.Count(l => l.IsFileSystem);
        var selectedReg = selected.Count(l => l.IsRegistry);

        var confirm = SysMessageBox.Show(
            $"Are you sure you want to permanently delete selected residual items?\n\n" +
            $"• Folders and files: {selectedFiles}\n" +
            $"• Registry entries: {selectedReg}\n\n" +
            $"Files in AppData/ProgramData directories and corresponding system registry keys will be removed.",
            "Confirm Cleanup",
            SysMessageBoxButton.YesNo,
            SysMessageBoxImage.Warning);

        if (confirm != SysMessageBoxResult.Yes) return;

        var report = _cleanerEngine.Clean(selected);

        var details = new List<string>();
        if (report.DeletedFolders > 0 || report.DeletedFiles > 0)
        {
            var sizeFreedStr = report.BytesFreed > 0 ? $" ({report.BytesFreed / 1024.0 / 1024.0:F2} MB)" : "";
            details.Add($"📁 Deleted folders & files: {report.DeletedFolders + report.DeletedFiles}{sizeFreedStr}");
        }
        if (report.DeletedRegistryKeys > 0 || report.DeletedRegistryValues > 0)
        {
            details.Add($"🗃 Deleted registry entries: {report.DeletedRegistryKeys + report.DeletedRegistryValues} (keys: {report.DeletedRegistryKeys}, values: {report.DeletedRegistryValues})");
        }
        if (report.DeletedServices > 0)
        {
            details.Add($"⚙ Deleted Windows services: {report.DeletedServices}");
        }

        var message = details.Count > 0 ? string.Join("\n", details) : $"Successfully cleaned items: {report.DeletedItems}";

        SysMessageBox.Show(
            $"Residual cleanup complete!\n\n{message}\nErrors: {report.FailedItems}",
            "Cleanup Results",
            SysMessageBoxButton.OK,
            SysMessageBoxImage.Information);

        for (int i = FoundLeftovers.Count - 1; i >= 0; i--)
        {
            if (FoundLeftovers[i].IsSelected)
            {
                FoundLeftovers.RemoveAt(i);
            }
        }

        var remainingFiles = FoundLeftovers.Count(l => l.IsFileSystem);
        var remainingReg = FoundLeftovers.Count(l => l.IsRegistry);
        LeftoverCountBadge.Text = $" (Files: {remainingFiles}, Registry: {remainingReg})";

        if (FoundLeftovers.Count == 0)
        {
            LeftoversScanPlaceholder.Visibility = Visibility.Visible;
            LeftoverListView.Visibility = Visibility.Collapsed;
            BtnCleanLeftovers.Visibility = Visibility.Collapsed;
        }

        StatusText.Text = $"Cleanup finished: deleted {report.DeletedItems} items ({report.DeletedFolders + report.DeletedFiles} files, {report.DeletedRegistryKeys + report.DeletedRegistryValues} registry entries).";
        await ReloadAppsAsync();
    }

    // External Uninstall Watchdog Notification Handler (Solid Toast in Bottom-Right)
    private void OnAppUninstalledExternally(InstalledApp uninstalledApp)
    {
        Dispatcher.Invoke(async () =>
        {
            StatusText.Text = $"External uninstall detected: {uninstalledApp.DisplayName}. Scanning leftovers...";

            await ReloadAppsAsync();

            var leftovers = await _leftoverEngine.ScanAsync(uninstalledApp);

            if (leftovers.Count > 0)
            {
                var icon = _iconService.GetIcon(uninstalledApp, 32);

                if (_settings.AutoClean == AutoCleanMode.AlwaysClean)
                {
                    // Clean automatically without asking
                    var report = _cleanerEngine.Clean(leftovers);
                    StatusText.Text = $"Automatically cleaned {report.DeletedItems} leftovers (files: {report.DeletedFolders + report.DeletedFiles}, registry: {report.DeletedRegistryKeys + report.DeletedRegistryValues}) for \"{uninstalledApp.DisplayName}\".";

                    // Show notification report that items were cleaned
                    var toast = new ToastNotificationWindow(uninstalledApp, leftovers, icon, isAlreadyCleaned: true);
                    toast.ShowRequested += (app) => ActivateMainWindow();
                    toast.Show();
                }
                else if (_settings.AutoClean == AutoCleanMode.Ask)
                {
                    // Show interactive prompt toast
                    var toast = new ToastNotificationWindow(uninstalledApp, leftovers, icon, isAlreadyCleaned: false);
                    toast.CleanRequested += (app, items) =>
                    {
                        var report = _cleanerEngine.Clean(items);
                        StatusText.Text = $"Leftovers of \"{app.DisplayName}\" cleaned ({report.DeletedItems} items, registry: {report.DeletedRegistryKeys + report.DeletedRegistryValues}).";
                        
                        var details = new List<string>();
                        if (report.DeletedFolders > 0 || report.DeletedFiles > 0)
                            details.Add($"📁 Deleted files/folders: {report.DeletedFolders + report.DeletedFiles}");
                        if (report.DeletedRegistryKeys > 0 || report.DeletedRegistryValues > 0)
                            details.Add($"🗃 Deleted registry entries: {report.DeletedRegistryKeys + report.DeletedRegistryValues}");

                        SysMessageBox.Show(
                            $"Leftovers of \"{app.DisplayName}\" successfully deleted!\n\n{string.Join("\n", details)}",
                            "Leftover Cleanup",
                            SysMessageBoxButton.OK,
                            SysMessageBoxImage.Information);
                    };

                    toast.ShowRequested += (app) => ActivateMainWindow();
                    toast.Show();
                }
                else
                {
                    StatusText.Text = $"Application \"{uninstalledApp.DisplayName}\" uninstalled (auto-clean is disabled in settings).";
                }
            }
            else
            {
                StatusText.Text = $"Application \"{uninstalledApp.DisplayName}\" uninstalled cleanly. No leftovers found.";
            }
        });
    }

    private void ActivateMainWindow()
    {
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
        Focus();
    }

    // ==========================================
    // Drag & Drop: Shortcut / Exe Uninstallation
    // ==========================================
    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            if (DragDropOverlay.Visibility != Visibility.Visible)
            {
                DragDropOverlay.Visibility = Visibility.Visible;
            }
            e.Handled = true;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
    }

    private void Window_DragLeave(object sender, DragEventArgs e)
    {
        var pos = e.GetPosition(this);
        // Only collapse if the cursor has actually left the window perimeter
        if (pos.X <= 2 || pos.Y <= 2 || pos.X >= ActualWidth - 2 || pos.Y >= ActualHeight - 2)
        {
            DragDropOverlay.Visibility = Visibility.Collapsed;
        }
    }

    private void Window_QueryContinueDrag(object sender, QueryContinueDragEventArgs e)
    {
        if (e.EscapePressed)
        {
            DragDropOverlay.Visibility = Visibility.Collapsed;
        }
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        DragDropOverlay.Visibility = Visibility.Collapsed;

        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        if (files == null || files.Length == 0) return;

        var droppedPath = files[0];
        var resolvedTarget = ShortcutResolver.ResolveTarget(droppedPath) ?? droppedPath;

        StatusText.Text = $"Analyzing shortcut/file: {Path.GetFileName(droppedPath)} -> {resolvedTarget}...";

        // Try to match with installed applications
        var matchedApp = ShortcutResolver.FindMatchingApp(resolvedTarget, _allViewModels.Select(v => v.App));

        if (matchedApp != null)
        {
            var vm = _allViewModels.FirstOrDefault(v => v.App.Id == matchedApp.Id);
            if (vm != null)
            {
                AppsDataGrid.SelectedItem = vm;
                AppsDataGrid.ScrollIntoView(vm);
            }

            var confirm = SysMessageBox.Show(
                $"Shortcut points to installed software:\n\n" +
                $"\"{matchedApp.DisplayName}\" (Version: {matchedApp.DisplayVersion ?? "N/A"})\n" +
                $"Path: {resolvedTarget}\n\n" +
                $"Start standard uninstall and deep residual cleanup?",
                "Uninstall via Shortcut",
                SysMessageBoxButton.YesNo,
                SysMessageBoxImage.Question);

            if (confirm == SysMessageBoxResult.Yes)
            {
                // Delete the shortcut file if it was a .lnk
                try
                {
                    if (droppedPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) && File.Exists(droppedPath))
                    {
                        File.Delete(droppedPath);
                    }
                }
                catch { }

                BtnUninstall_Click(this, new RoutedEventArgs());
            }
        }
        else
        {
            // Application not found in registry (Portable app or broken install)
            var targetDir = Directory.Exists(resolvedTarget) ? resolvedTarget : Path.GetDirectoryName(resolvedTarget);
            var appName = Path.GetFileNameWithoutExtension(resolvedTarget);

            var confirmForce = SysMessageBox.Show(
                $"Program not found in the official registry of installed apps.\n\n" +
                $"Target: {resolvedTarget}\n" +
                $"Folder: {targetDir}\n\n" +
                $"Perform \"Force Uninstall\"?\n" +
                $"Fluent Uninstaller will delete the program folder and clean associated residual files in AppData and registry.",
                "Force Uninstall via Shortcut",
                SysMessageBoxButton.YesNo,
                SysMessageBoxImage.Warning);

            if (confirmForce == SysMessageBoxResult.Yes && !string.IsNullOrWhiteSpace(targetDir))
            {
                var fakeApp = new InstalledApp
                {
                    Id = $"Manual_{appName}",
                    DisplayName = appName,
                    InstallLocation = targetDir,
                    Type = AppType.Win32
                };

                StatusText.Text = $"Force uninstalling {appName}...";
                var leftovers = await _leftoverEngine.ScanAsync(fakeApp);

                // Add main folder if exists
                if (Directory.Exists(targetDir) && !leftovers.Any(l => l.Path.Equals(targetDir, StringComparison.OrdinalIgnoreCase)))
                {
                    leftovers.Insert(0, new LeftoverItem
                    {
                        Path = targetDir,
                        Type = LeftoverType.Directory,
                        Confidence = ConfidenceLevel.High,
                        Reason = "Main application directory",
                        IsSelected = true
                    });
                }

                // Delete the shortcut file if it was a .lnk
                try
                {
                    if (droppedPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) && File.Exists(droppedPath))
                    {
                        File.Delete(droppedPath);
                    }
                }
                catch { }

                var report = _cleanerEngine.Clean(leftovers);
                SysMessageBox.Show(
                    $"Force uninstall of \"{appName}\" complete!\n\nCleaned items: {report.DeletedItems}\nErrors: {report.FailedItems}",
                    "Uninstall Results",
                    SysMessageBoxButton.OK,
                    SysMessageBoxImage.Information);

                await ReloadAppsAsync();
            }
        }
    }

    // ==========================================
    // Settings & Theme Handlers
    // ==========================================
    private void BtnSettings_Click(object sender, RoutedEventArgs e)
    {
        if (BtnSettings.ContextMenu != null)
        {
            BtnSettings.ContextMenu.PlacementTarget = BtnSettings;
            BtnSettings.ContextMenu.IsOpen = true;
        }
    }

    private void MenuAutoClean_Click(object sender, RoutedEventArgs e)
    {
        if (sender is WpfMenuItem item && item.Tag is string tag)
        {
            _settings.AutoClean = tag switch
            {
                "AlwaysClean" => AutoCleanMode.AlwaysClean,
                "NeverClean" => AutoCleanMode.NeverClean,
                _ => AutoCleanMode.Ask
            };

            _settings.Save();
            UpdateSettingsMenuUi();
        }
    }

    private void UpdateSettingsMenuUi()
    {
        var menu = Resources["SettingsContextMenu"] as System.Windows.Controls.ContextMenu;
        if (menu != null)
        {
            foreach (var item in menu.Items.OfType<WpfMenuItem>())
            {
                if (item.Tag is string tag)
                {
                    if (tag == "AutoStart")
                    {
                        item.IsChecked = AutoStartService.IsAutoStartEnabled();
                    }
                    else
                    {
                        item.IsChecked = tag switch
                        {
                            "AlwaysClean" => _settings.AutoClean == AutoCleanMode.AlwaysClean,
                            "NeverClean" => _settings.AutoClean == AutoCleanMode.NeverClean,
                            "Ask" => _settings.AutoClean == AutoCleanMode.Ask,
                            _ => false
                        };
                    }
                }
            }
        }

        WatchdogBadgeText.Text = _settings.AutoClean switch
        {
            AutoCleanMode.AlwaysClean => "Auto-Watchdog: Always Clean",
            AutoCleanMode.NeverClean => "Auto-Watchdog: Never Clean",
            _ => "Auto-Watchdog: Ask"
        };
    }

    private void MenuStartWithWindows_Click(object sender, RoutedEventArgs e)
    {
        var newState = !AutoStartService.IsAutoStartEnabled();
        AutoStartService.SetAutoStart(newState);
        _settings.StartWithWindows = newState;
        _settings.Save();
        UpdateSettingsMenuUi();
    }

    private void BtnThemeToggle_Click(object sender, RoutedEventArgs e)
    {
        var currentTheme = ApplicationThemeManager.GetAppTheme();
        var newTheme = currentTheme == ApplicationTheme.Dark 
            ? ApplicationTheme.Light 
            : ApplicationTheme.Dark;
        var isDark = newTheme == ApplicationTheme.Dark;

        // Instant, hardware-accelerated theme application without blocking overlays
        ApplicationThemeManager.Apply(newTheme);
        _settings.DarkTheme = isDark;
        _settings.Save();
        UpdateThemeToggleButtonIcon();
        UpdateThemeAppIcon(isDark);
    }

    private void UpdateThemeAppIcon(bool isDark)
    {
        try
        {
            var iconFile = isDark ? "dark.png" : "white.png";
            var iconUri = new Uri($"pack://application:,,,/Assets/{iconFile}", UriKind.Absolute);
            var bitmap = new BitmapImage(iconUri);

            // Update OS Window icon (Taskbar, Alt+Tab, System Menu)
            this.Icon = bitmap;
        }
        catch (Exception ex)
        {
            App.Log($"UpdateThemeAppIcon error: {ex}");
        }
    }

    private void UpdateThemeToggleButtonIcon()
    {
        var currentTheme = ApplicationThemeManager.GetAppTheme();
        if (currentTheme == ApplicationTheme.Dark)
        {
            BtnThemeToggle.Icon = new SymbolIcon(SymbolRegular.WeatherSunny24);
            BtnThemeToggle.ToolTip = "Switch to light theme";
        }
        else
        {
            BtnThemeToggle.Icon = new SymbolIcon(SymbolRegular.WeatherMoon24);
            BtnThemeToggle.ToolTip = "Switch to dark theme";
        }
    }

    // Context Menu Handlers
    private void MenuUninstall_Click(object sender, RoutedEventArgs e) => BtnUninstall_Click(sender, e);
    private void MenuScanLeftovers_Click(object sender, RoutedEventArgs e) => BtnScanLeftovers_Click(sender, e);

    private void MenuOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (AppsDataGrid.SelectedItem is not AppItemViewModel selected) return;

        var path = selected.App.InstallLocation;
        if (string.IsNullOrEmpty(path) && !string.IsNullOrEmpty(selected.App.DisplayIcon))
        {
            path = Path.GetDirectoryName(selected.App.DisplayIcon.Trim('"', '\''));
        }

        if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
        {
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        else
        {
            SysMessageBox.Show("Installation directory not found or inaccessible.", "Fluent Uninstaller", SysMessageBoxButton.OK, SysMessageBoxImage.Warning);
        }
    }

    private void MenuCopyPath_Click(object sender, RoutedEventArgs e)
    {
        if (AppsDataGrid.SelectedItem is not AppItemViewModel selected) return;

        var path = selected.App.InstallLocation ?? selected.App.DisplayIcon ?? "";
        if (!string.IsNullOrEmpty(path))
        {
            Clipboard.SetText(path);
            StatusText.Text = $"Path copied to clipboard: {path}";
        }
    }

    // Quick Row Action Handlers
    private void QuickRowUninstall_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is AppItemViewModel vm)
        {
            AppsDataGrid.SelectedItem = vm;
            BtnUninstall_Click(sender, e);
        }
    }

    private void QuickRowScan_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is AppItemViewModel vm)
        {
            AppsDataGrid.SelectedItem = vm;
            BtnScanLeftovers_Click(sender, e);
        }
    }

    private void QuickRowOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is AppItemViewModel vm)
        {
            AppsDataGrid.SelectedItem = vm;
            MenuOpenFolder_Click(sender, e);
        }
    }
}