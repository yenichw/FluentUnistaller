using System.IO;
using System.Media;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using FluentUninstaller.Core.Models;
using Wpf.Ui.Appearance;

namespace FluentUninstaller.UI.Views;

public partial class ToastNotificationWindow : Window
{
    private readonly InstalledApp _app;
    private readonly List<LeftoverItem> _leftovers;
    private readonly DispatcherTimer _autoCloseTimer = new();

    public event Action<InstalledApp, List<LeftoverItem>>? CleanRequested;
    public event Action<InstalledApp>? ShowRequested;

    public ToastNotificationWindow(InstalledApp app, List<LeftoverItem> leftovers, ImageSource? icon = null, bool isAlreadyCleaned = false)
    {
        InitializeComponent();
        _app = app;
        _leftovers = leftovers;

        ApplyThemeStyles();

        AppTitleText.Text = app.DisplayName;
        if (icon != null)
        {
            AppIconImage.Source = icon;
        }

        var totalSizeBytes = leftovers.Sum(l => l.SizeBytes ?? 0);
        var sizeStr = totalSizeBytes > 0 ? FormatBytes(totalSizeBytes) : null;

        var fileCount = leftovers.Count(l => l.IsFileSystem);
        var regCount = leftovers.Count(l => l.IsRegistry);

        var descParts = new List<string>();
        if (fileCount > 0)
        {
            var s = sizeStr != null ? $" ({sizeStr})" : "";
            descParts.Add($"{fileCount} folders/files{s}");
        }
        if (regCount > 0)
        {
            descParts.Add($"{regCount} registry entries");
        }
        if (descParts.Count == 0)
        {
            descParts.Add($"{leftovers.Count} residual items");
        }
        var breakdown = string.Join(" and ", descParts);

        if (isAlreadyCleaned)
        {
            HeaderBadgeText.Text = "Fluent Uninstaller • Automatic Clean";
            LeftoverDetailsText.Text = $"Successfully cleaned: {breakdown}";
            BtnCleanNow.Visibility = Visibility.Collapsed;
            BtnOpenApp.Content = "OK";
        }
        else
        {
            HeaderBadgeText.Text = "Fluent Uninstaller • Uninstall Detected";
            LeftoverDetailsText.Text = $"Found: {breakdown}";
            BtnCleanText.Text = $"Clean ({leftovers.Count})";
        }

        Loaded += ToastNotificationWindow_Loaded;

        _autoCloseTimer.Interval = TimeSpan.FromSeconds(16);
        _autoCloseTimer.Tick += (s, e) =>
        {
            _autoCloseTimer.Stop();
            CloseToast();
        };
    }

    private void ApplyThemeStyles()
    {
        var settings = AppSettings.Load();
        var currentTheme = ApplicationThemeManager.GetAppTheme();
        var isDark = settings.DarkTheme || currentTheme == ApplicationTheme.Dark;

        if (isDark)
        {
            RootBorder.Background = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20));
            RootBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x45, 0x45, 0x45));
            AppTitleText.Foreground = Brushes.White;
            LeftoverDetailsText.Foreground = new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0));
            HeaderBadgeText.Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA));
            BtnOpenApp.Background = new SolidColorBrush(Color.FromRgb(0x32, 0x32, 0x32));
            BtnOpenApp.Foreground = Brushes.White;
            BtnOpenApp.BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
        }
        else
        {
            RootBorder.Background = new SolidColorBrush(Color.FromRgb(0xF9, 0xF9, 0xF9));
            RootBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0));
            AppTitleText.Foreground = new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x11));
            LeftoverDetailsText.Foreground = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
            HeaderBadgeText.Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
            BtnOpenApp.Background = new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xEE));
            BtnOpenApp.Foreground = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22));
            BtnOpenApp.BorderBrush = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC));
        }
    }

    private void ToastNotificationWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 14;
        Top = workArea.Bottom - Height - 14;

        try
        {
            SystemSounds.Asterisk.Play();
        }
        catch { }

        var anim = (Storyboard?)Resources["ShowAnim"];
        anim?.Begin(this);

        _autoCloseTimer.Start();
    }

    private void BtnCleanNow_Click(object sender, RoutedEventArgs e)
    {
        _autoCloseTimer.Stop();
        CleanRequested?.Invoke(_app, _leftovers);
        CloseToast();
    }

    private void BtnOpenApp_Click(object sender, RoutedEventArgs e)
    {
        _autoCloseTimer.Stop();
        ShowRequested?.Invoke(_app);
        CloseToast();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        _autoCloseTimer.Stop();
        CloseToast();
    }

    private void CloseToast()
    {
        try
        {
            Close();
        }
        catch { }
    }

    private static string FormatBytes(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB"];
        int order = 0;
        double len = bytes;
        while (len >= 1024 && order < suffixes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.#} {suffixes[order]}";
    }
}
