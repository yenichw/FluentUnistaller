using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using FluentUninstaller.Core.Models;
using FluentUninstaller.UI.Services;

namespace FluentUninstaller.UI.ViewModels;

public class AppItemViewModel : INotifyPropertyChanged
{
    private ImageSource? _icon;

    public InstalledApp App { get; }

    public string DisplayName => App.DisplayName;
    public string? Publisher => App.Publisher;
    public string? DisplayVersion => App.DisplayVersion;
    public string SizeFormatted => App.SizeFormatted;
    public string? InstallDate => App.InstallDate;
    public AppType Type => App.Type;

    public ImageSource? Icon
    {
        get => _icon;
        set
        {
            if (_icon != value)
            {
                _icon = value;
                OnPropertyChanged();
            }
        }
    }

    public AppItemViewModel(InstalledApp app, ImageSource? initialIcon = null)
    {
        App = app;
        _icon = initialIcon;
    }

    public async Task LoadIconAsync(IconExtractorService iconService)
    {
        if (_icon != null && _icon != iconService.GetDefaultExeIcon())
            return;

        var extracted = await Task.Run(() => iconService.GetIcon(App, 32));
        if (extracted != null)
        {
            Icon = extracted;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
