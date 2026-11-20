using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using Wpf.Ui.Controls;
using SysMessageBox = System.Windows.MessageBox;
using SysMessageBoxButton = System.Windows.MessageBoxButton;
using SysMessageBoxImage = System.Windows.MessageBoxImage;

namespace FluentUninstaller.Setup;

public partial class MainWindow : FluentWindow
{
    private readonly bool _isUninstallMode;
    private bool _isCompleted = false;

    public MainWindow()
    {
        InitializeComponent();

        var args = Environment.GetCommandLineArgs();
        _isUninstallMode = args.Any(a => a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase) || a.Equals("/uninstall", StringComparison.OrdinalIgnoreCase));

        var defaultInstallDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            "FluentUninstaller");

        InstallPathBox.Text = defaultInstallDir;

        if (_isUninstallMode)
        {
            SetupUninstallUi();
        }
    }

    private void SetupUninstallUi()
    {
        Title = "Fluent Uninstaller - Uninstall";
        TitleBar.Title = "Fluent Uninstaller - Uninstall";
        HeroTitleText.Text = "Uninstall Fluent Uninstaller";
        HeroSubtitleText.Text = "Remove Fluent Uninstaller and all its components from this computer";
        
        InstallPanel.Visibility = Visibility.Collapsed;
        UninstallPanel.Visibility = Visibility.Visible;
        ProgressBorder.Visibility = Visibility.Collapsed;

        BtnAction.Content = "Uninstall";
        BtnAction.Appearance = ControlAppearance.Danger;
    }

    private void BtnBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select Installation Folder",
            InitialDirectory = InstallPathBox.Text
        };

        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.FolderName))
        {
            InstallPathBox.Text = dialog.FolderName;
        }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void BtnAction_Click(object sender, RoutedEventArgs e)
    {
        if (_isCompleted)
        {
            if (!_isUninstallMode && ChkLaunchApp.IsChecked == true)
            {
                var exePath = Path.Combine(InstallPathBox.Text, "FluentUninstaller.UI.exe");
                if (File.Exists(exePath))
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = exePath,
                            UseShellExecute = true
                        });
                    }
                    catch { }
                }
            }

            Close();
            return;
        }

        if (_isUninstallMode)
        {
            await RunUninstallAsync();
        }
        else
        {
            await RunInstallAsync();
        }
    }

    private async Task RunInstallAsync()
    {
        var targetDir = InstallPathBox.Text.Trim();
        var createDesktop = ChkDesktopShortcut.IsChecked == true;
        var createStartMenu = ChkStartMenuShortcut.IsChecked == true;
        var autoStart = ChkAutoStart.IsChecked == true;

        BtnAction.IsEnabled = false;
        BtnCancel.IsEnabled = false;
        InstallPanel.Visibility = Visibility.Collapsed;
        ProgressBorder.Visibility = Visibility.Visible;
        ProgressSpinner.Visibility = Visibility.Visible;
        SuccessIcon.Visibility = Visibility.Collapsed;
        ProgressBar.Visibility = Visibility.Visible;
        ProgressBar.IsIndeterminate = true;
        StatusText.Text = "Installing Fluent Uninstaller...";

        try
        {
            await Task.Run(() =>
            {
                // 1. Terminate any running instances
                KillRunningInstances();

                // 2. Ensure target folder exists
                Directory.CreateDirectory(targetDir);

                // 3. Extract embedded payload.zip
                var asm = Assembly.GetExecutingAssembly();
                using (var stream = asm.GetManifestResourceStream("FluentUninstaller.Setup.payload.zip"))
                {
                    if (stream == null)
                    {
                        throw new InvalidOperationException("Embedded installation payload not found.");
                    }

                    using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
                    archive.ExtractToDirectory(targetDir, overwriteFiles: true);
                }

                // 4. Copy self as uninstaller inside target dir
                var currentExe = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(currentExe) && File.Exists(currentExe))
                {
                    var targetSetupExe = Path.Combine(targetDir, "FluentUninstaller.Setup.exe");
                    if (!currentExe.Equals(targetSetupExe, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Copy(currentExe, targetSetupExe, true);
                    }
                }

                // 5. Create shortcuts
                var mainExePath = Path.Combine(targetDir, "FluentUninstaller.UI.exe");

                if (createDesktop)
                {
                    var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                    CreateShortcut(Path.Combine(desktop, "Fluent Uninstaller.lnk"), mainExePath, "Fast, modern Windows uninstaller");
                }

                if (createStartMenu)
                {
                    var startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs");
                    Directory.CreateDirectory(startMenu);
                    CreateShortcut(Path.Combine(startMenu, "Fluent Uninstaller.lnk"), mainExePath, "Fast, modern Windows uninstaller");
                }

                // 6. Register in Windows Uninstall Registry key
                RegisterInWindows(targetDir, mainExePath);

                // 7. Auto-start registration
                if (autoStart)
                {
                    try
                    {
                        using var runKey = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
                        runKey.SetValue("FluentUninstaller", "\"" + mainExePath + "\" --autostart");
                    }
                    catch { }
                }
            });

            ProgressBar.IsIndeterminate = false;
            ProgressBar.Value = 100;
            ProgressSpinner.Visibility = Visibility.Collapsed;
            SuccessIcon.Visibility = Visibility.Visible;
            StatusText.Text = "Installation completed successfully!";
            BtnAction.Content = "Finish";
            BtnAction.IsEnabled = true;
            BtnCancel.Visibility = Visibility.Collapsed;
            _isCompleted = true;
        }
        catch (Exception ex)
        {
            ProgressBorder.Visibility = Visibility.Collapsed;
            InstallPanel.Visibility = Visibility.Visible;
            BtnAction.IsEnabled = true;
            BtnCancel.IsEnabled = true;
            SysMessageBox.Show($"Failed to install Fluent Uninstaller:\n\n{ex.Message}", "Installation Error", SysMessageBoxButton.OK, SysMessageBoxImage.Error);
        }
    }

    private async Task RunUninstallAsync()
    {
        BtnAction.IsEnabled = false;
        BtnCancel.IsEnabled = false;
        UninstallPanel.Visibility = Visibility.Collapsed;
        ProgressBorder.Visibility = Visibility.Visible;
        ProgressSpinner.Visibility = Visibility.Visible;
        SuccessIcon.Visibility = Visibility.Collapsed;
        ProgressBar.Visibility = Visibility.Visible;
        ProgressBar.IsIndeterminate = true;
        StatusText.Text = "Removing Fluent Uninstaller...";

        try
        {
            await Task.Run(() =>
            {
                // 1. Terminate running instances
                KillRunningInstances();

                // 2. Remove shortcuts
                var desktopLnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Fluent Uninstaller.lnk");
                if (File.Exists(desktopLnk))
                {
                    try { File.Delete(desktopLnk); } catch { }
                }

                var startMenuLnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", "Fluent Uninstaller.lnk");
                if (File.Exists(startMenuLnk))
                {
                    try { File.Delete(startMenuLnk); } catch { }
                }

                // 3. Remove Registry keys & Auto-start
                try
                {
                    Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\FluentUninstaller", throwOnMissingSubKey: false);
                }
                catch { }

                try
                {
                    using var runKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                    runKey?.DeleteValue("FluentUninstaller", throwOnMissingValue: false);
                }
                catch { }

                // 4. Delete app directory files
                var appDir = AppDomain.CurrentDomain.BaseDirectory;
                var currentExe = Environment.ProcessPath ?? "";

                if (Directory.Exists(appDir))
                {
                    foreach (var file in Directory.GetFiles(appDir))
                    {
                        if (file.Equals(currentExe, StringComparison.OrdinalIgnoreCase))
                            continue;

                        try { File.Delete(file); } catch { }
                    }
                }
            });

            ProgressBar.IsIndeterminate = false;
            ProgressBar.Value = 100;
            ProgressSpinner.Visibility = Visibility.Collapsed;
            SuccessIcon.Visibility = Visibility.Visible;
            StatusText.Text = "Fluent Uninstaller has been removed.";
            BtnAction.Content = "Close";
            BtnAction.Appearance = ControlAppearance.Primary;
            BtnAction.IsEnabled = true;
            BtnCancel.Visibility = Visibility.Collapsed;
            _isCompleted = true;
        }
        catch (Exception ex)
        {
            ProgressBorder.Visibility = Visibility.Collapsed;
            UninstallPanel.Visibility = Visibility.Visible;
            BtnAction.IsEnabled = true;
            BtnCancel.IsEnabled = true;
            SysMessageBox.Show($"Failed to uninstall:\n\n{ex.Message}", "Uninstall Error", SysMessageBoxButton.OK, SysMessageBoxImage.Error);
        }
    }

    private static void KillRunningInstances()
    {
        foreach (var proc in Process.GetProcessesByName("FluentUninstaller.UI"))
        {
            try
            {
                proc.Kill();
                proc.WaitForExit(2000);
            }
            catch { }
        }
    }

    private static void CreateShortcut(string shortcutPath, string targetPath, string description)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return;

            dynamic? shell = Activator.CreateInstance(shellType);
            if (shell == null) return;

            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            shortcut.TargetPath = targetPath;
            shortcut.WorkingDirectory = Path.GetDirectoryName(targetPath);
            shortcut.Description = description;
            shortcut.Save();
        }
        catch { }
    }

    private static void RegisterInWindows(string targetDir, string mainExePath)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\FluentUninstaller");
            key.SetValue("DisplayName", "Fluent Uninstaller");
            key.SetValue("DisplayVersion", "1.0.0");
            key.SetValue("Publisher", "FluentUninstaller Open-Source");
            key.SetValue("DisplayIcon", mainExePath);
            key.SetValue("InstallLocation", targetDir);
            key.SetValue("UninstallString", $"\"{Path.Combine(targetDir, "FluentUninstaller.Setup.exe")}\" --uninstall");
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            key.SetValue("EstimatedSize", 12000, RegistryValueKind.DWord);
        }
        catch { }
    }
}
