using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Wpf.Ui.Appearance;

namespace FluentUninstaller.Setup;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private static readonly string LogPath = Path.Combine(Path.GetTempPath(), "FluentUninstaller-Setup.log");

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
        base.OnStartup(e);

        try
        {
            ApplicationThemeManager.Apply(ApplicationTheme.Dark);
            var win = new MainWindow();
            win.Show();
        }
        catch (Exception ex)
        {
            LogAndShowException(ex);
        }
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogAndShowException(e.Exception);
        e.Handled = true;
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            LogAndShowException(ex);
        }
    }

    private static void LogAndShowException(Exception ex)
    {
        try
        {
            var errPath = Path.Combine(Path.GetTempPath(), "FluentUninstaller-Setup-error.log");
            File.WriteAllText(errPath, ex.ToString());
            MessageBox.Show(
                $"A critical error occurred in Setup:\n\n{ex.Message}\n\nDetails saved to: {errPath}",
                "Fluent Uninstaller Setup Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
            // Ignore secondary logging failures
        }
    }
}
