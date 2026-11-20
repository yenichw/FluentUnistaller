using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentUninstaller.Core.Models;

namespace FluentUninstaller.UI.Services;

public class IconExtractorService
{
    private readonly ConcurrentDictionary<string, ImageSource?> _iconCache = new(StringComparer.OrdinalIgnoreCase);
    private ImageSource? _defaultAppIcon;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint PrivateExtractIcons(
        string szFileName,
        int nIconIndex,
        int cxIcon,
        int cyIcon,
        IntPtr[] phicon,
        int[] piconid,
        uint nIcons,
        uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SHGetFileInfo(
        string pszPath,
        uint dwFileAttributes,
        ref SHFILEINFO psfi,
        uint cbFileInfo,
        uint uFlags);

    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_LARGEICON = 0x000000000;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;

    public ImageSource GetIcon(InstalledApp app, int size = 32)
    {
        var cacheKey = $"{app.Id}_{size}";
        if (_iconCache.TryGetValue(cacheKey, out var cached) && cached != null)
        {
            return cached;
        }

        var icon = ExtractIconInternal(app, size) ?? GetDefaultExeIcon();
        _iconCache[cacheKey] = icon;
        return icon;
    }

    private ImageSource? ExtractIconInternal(InstalledApp app, int size)
    {
        // 1. Try DisplayIcon
        if (!string.IsNullOrWhiteSpace(app.DisplayIcon))
        {
            var (path, index) = ParseIconLocation(app.DisplayIcon);
            if (File.Exists(path))
            {
                var icon = ExtractFromPath(path, index, size);
                if (icon != null) return icon;
            }
        }

        // 2. Try InstallLocation (look for matching or main exe)
        if (!string.IsNullOrWhiteSpace(app.InstallLocation) && Directory.Exists(app.InstallLocation))
        {
            try
            {
                var exeFiles = Directory.EnumerateFiles(app.InstallLocation, "*.exe", SearchOption.TopDirectoryOnly).ToList();
                if (exeFiles.Count > 0)
                {
                    // Look for exe matching app name
                    var matching = exeFiles.FirstOrDefault(f =>
                        Path.GetFileNameWithoutExtension(f).Contains(app.DisplayName, StringComparison.OrdinalIgnoreCase) ||
                        app.DisplayName.Contains(Path.GetFileNameWithoutExtension(f), StringComparison.OrdinalIgnoreCase))
                        ?? exeFiles.First();

                    var icon = ExtractFromPath(matching, 0, size);
                    if (icon != null) return icon;
                }
            }
            catch { }
        }

        // 3. Try UninstallString
        if (!string.IsNullOrWhiteSpace(app.UninstallString))
        {
            var exePath = ExtractExePathFromCommand(app.UninstallString);
            if (!string.IsNullOrWhiteSpace(exePath) && File.Exists(exePath))
            {
                var icon = ExtractFromPath(exePath, 0, size);
                if (icon != null) return icon;
            }
        }

        return null;
    }

    private static ImageSource? ExtractFromPath(string filePath, int index, int size)
    {
        try
        {
            var phicon = new IntPtr[1];
            var piconid = new int[1];

            var count = PrivateExtractIcons(filePath, index, size, size, phicon, piconid, 1, 0);
            if (count > 0 && phicon[0] != IntPtr.Zero)
            {
                var hIcon = phicon[0];
                try
                {
                    var source = Imaging.CreateBitmapSourceFromHIcon(
                        hIcon,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());

                    source.Freeze(); // Allow cross-thread usage
                    return source;
                }
                finally
                {
                    DestroyIcon(hIcon);
                }
            }
        }
        catch { }

        return null;
    }

    public ImageSource GetDefaultExeIcon()
    {
        if (_defaultAppIcon != null) return _defaultAppIcon;

        try
        {
            var shinfo = new SHFILEINFO();
            SHGetFileInfo(
                ".exe",
                FILE_ATTRIBUTE_NORMAL,
                ref shinfo,
                (uint)Marshal.SizeOf(shinfo),
                SHGFI_ICON | SHGFI_USEFILEATTRIBUTES | SHGFI_LARGEICON);

            if (shinfo.hIcon != IntPtr.Zero)
            {
                try
                {
                    var source = Imaging.CreateBitmapSourceFromHIcon(
                        shinfo.hIcon,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                    source.Freeze();
                    _defaultAppIcon = source;
                    return source;
                }
                finally
                {
                    DestroyIcon(shinfo.hIcon);
                }
            }
        }
        catch { }

        return null!;
    }

    private static (string Path, int Index) ParseIconLocation(string raw)
    {
        raw = raw.Trim().Trim('\"');
        var commaIdx = raw.LastIndexOf(',');
        if (commaIdx > 0 && commaIdx < raw.Length - 1)
        {
            var potentialIndex = raw[(commaIdx + 1)..].Trim();
            if (int.TryParse(potentialIndex, out var idx))
            {
                var path = raw[..commaIdx].Trim().Trim('\"');
                return (path, idx);
            }
        }

        return (raw, 0);
    }

    private static string? ExtractExePathFromCommand(string raw)
    {
        raw = raw.Trim();
        if (raw.StartsWith('\"'))
        {
            var nextQuote = raw.IndexOf('\"', 1);
            if (nextQuote > 1)
            {
                return raw.Substring(1, nextQuote - 1);
            }
        }

        var spaceIdx = raw.IndexOf(' ');
        if (spaceIdx > 0)
        {
            var candidate = raw[..spaceIdx];
            if (File.Exists(candidate))
                return candidate;
        }

        return File.Exists(raw) ? raw : null;
    }
}
