# FluentUninstaller 🚀

**FluentUninstaller** is a modern, lightweight, and open-source application uninstaller for Windows 10 and 11. Built with **C# (.NET 11)** and **WPF-UI**, it features native Fluent Design with Mica backdrop, HiDPI icon extraction, and deep leftover scanning for files and registry keys.

---

## ✨ Features

- **Fluent Design & Mica**: Fully native Windows 11 aesthetics, dark and light theme support with smooth transitions.
- **Deep Leftover Cleaner**: Heuristic scanning of residual files across `%LocalAppData%`, `%AppData%`, `%ProgramData%`, and registry keys.
- **HiDPI Icon Extraction**: Direct extraction of high-resolution application icons via Win32 Shell APIs (`PrivateExtractIconsW`, `SHGetFileInfo`).
- **Drag & Drop Uninstall**: Drop any desktop shortcut (`.lnk`) or executable (`.exe`) directly into the window to locate and remove it.
- **Background Watchdog**: Optional lightweight notification when an uninstallation finishes, offering instant leftover cleanup.
- **Autostart Integration**: Configurable Windows startup option with silent minimized launch (`--autostart`).
- **Modern Setup Wizard**: Standalone single-file setup installer with customizable installation preferences.
- **CLI Utility**: Companion command-line tool for scripting and terminal management.

---

## 🏛 Solution Architecture

The solution is organized into independent projects:

```
FluentUninstaller.slnx
├── src/
│   ├── FluentUninstaller.Core/     # Engine: registry scanners, leftover detection, uninstaller execution
│   ├── FluentUninstaller.UI/       # WPF application featuring WPF-UI, Mica backdrop, and drag-and-drop
│   ├── FluentUninstaller.CLI/      # Console interface for headless environments and automation
│   └── FluentUninstaller.Setup/    # Standalone setup wizard executable
```

---

## 🚀 Getting Started

### Prerequisites

- Windows 10 (version 1903+) or Windows 11
- [.NET 11 SDK](https://dotnet.microsoft.com/download)

### Building the Solution

Clone the repository and build:

```bash
git clone https://github.com/your-username/FluentUninstaller.git
cd FluentUninstaller
dotnet build FluentUninstaller.slnx
```

### Running the Application

Launch the desktop UI:

```bash
dotnet run --project src/FluentUninstaller.UI
```

Launch the CLI:

```bash
dotnet run --project src/FluentUninstaller.CLI
```

### Creating Release Binaries & Installer

To package release builds and generate `FluentUninstaller-Setup.exe`:

```powershell
powershell -ExecutionPolicy Bypass -File build-installer.ps1
```

The resulting single-file setup executable will be located in `dist/FluentUninstaller-Setup.exe`.

---

## 📄 License

This project is licensed under the [MIT License](LICENSE).
