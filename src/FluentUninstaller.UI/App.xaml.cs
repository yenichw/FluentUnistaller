using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using FluentUninstaller.Core.Models;
using Wpf.Ui.Appearance;

namespace FluentUninstaller.UI;

public partial class App : Application
{
    private static readonly string LogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "startup.log");

    public static void Log(string msg)
    {
        try { File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n"); } catch { }
    }

    public App()
    {
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        Log("OnStartup start");
        base.OnStartup(e);

        try
        {
            var settings = AppSettings.Load();
            var initialTheme = settings.DarkTheme ? ApplicationTheme.Dark : ApplicationTheme.Light;
            ApplicationThemeManager.Apply(initialTheme);
            Log($"Initial theme applied: {initialTheme}");

            var mainWindow = new MainWindow();
            Log("MainWindow instantiated");

            var isAutoStart = e.Args.Any(a => a.Equals("--autostart", StringComparison.OrdinalIgnoreCase) || a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));
            if (isAutoStart)
            {
                Log("Started with --autostart flag: WindowState.Minimized");
                mainWindow.WindowState = WindowState.Minimized;
            }

            mainWindow.Show();
            Log("MainWindow.Show() called");
        }
        catch (Exception ex)
        {
            Log($"OnStartup EXCEPTION: {ex}");
            MessageBox.Show($"Startup Error: {ex.Message}", "Fluent Uninstaller Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log($"DispatcherUnhandledException: {e.Exception}");

        // Suppress non-fatal WPF internal animation glitch (Background property path animation)
        if (e.Exception is InvalidOperationException ioEx && (ioEx.Message.Contains("Background") || ioEx.Message.Contains("DependencyObject")))
        {
            e.Handled = true;
            return;
        }

        MessageBox.Show($"Error: {e.Exception.Message}\n\nDetails have been logged to startup.log.", "Fluent Uninstaller", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        Log($"CurrentDomain_UnhandledException: {e.ExceptionObject}");
    }
}
