# <img src="https://i.imgur.com/gVBGlQz.png" width="50" alt="DeskManager"> DeskManager

> **A lightweight Windows desktop utility for organizing and managing desktop icons into customizable visual containers (Grids).**

---

## 📋 Overview

DeskManager is a minimal yet extensible desktop application that allows users to organize desktop icons into movable, resizable containers that appear directly on the desktop. The application runs as a background utility, overlaying the Windows desktop while maintaining natural behavior with the Windows environment.

---

## ✨ Features

| Feature | Description |
|---------|-------------|
| 📦 **Grid Containers** | Create and manage visual containers for organizing desktop icons |
| 🎯 **Grid Management** | Organize icons into customizable grid-based layouts |
| 🖱️ **Drag & Drop** | Intuitive moving and resizing of grid containers |
| 🎨 **Customization** | Customize appearance and behavior of containers |
| ⚙️ **Settings Panel** | Comprehensive settings for fine-tuning the application |
| 🔄 **Auto-Sync** | Automatic synchronization with desktop changes |
| 🖼️ **Desktop Integration** | Seamless integration with Windows desktop |
| 📊 **Tray Icon** | System tray integration for quick access |
| 🎯 **Spaces System** | Multiple desktop spaces/workspaces support |
| 🔧 **Configuration Management** | Persistent storage of grid layouts and settings |

---

## 🚀 Installation & Setup

### Prerequisites
- Windows 10 or later
- .NET 8.0 Runtime (net8.0-windows)

### Steps

1. **Download the installer:**
   - Download `DeskManager-Installer.exe` from the latest release

2. **Run the installer:**
   ```
   DeskManager-Installer.exe
   ```

3. **Launch the application:**
   - Find DeskManager in your Start Menu
   - Or use the system tray icon to access quick settings

4. **Configure on first launch:**
   - The settings panel opens automatically on first launch
   - Customize your grids and preferences

---

## 📁 Project Structure

```
DeskManager/                       # Main application directory
  ├── App.xaml                     # Application UI definition
  ├── App.xaml.cs                  # Application startup logic
  ├── DeskManager.csproj           # C# project file
  ├── app.manifest                 # Windows application manifest
  │
  ├── Services/                    # Core services
  │   ├── GridManager.cs           # Grid management
  │   ├── DesktopDrawService.cs    # Desktop drawing and rendering
  │   ├── DesktopPositionService.cs # Position tracking
  │   ├── FenceManager.cs          # Grid-specific operations
  │   ├── FileStorageService.cs    # Configuration persistence
  │   ├── FolderWatcherService.cs  # File system monitoring
  │   ├── ThemeService.cs          # Theme management
  │   └── UpdateService.cs         # Update checking and installation
  │
  ├── Windows/                     # WPF Windows
  │   ├── GridWindow.xaml          # Main grid display window
  │   ├── DrawingOverlay.xaml      # Desktop overlay window
  │   ├── GridWindow.xaml          # Individual grid window
  │   ├── SettingsWindow.xaml      # Settings/configuration window
  │   └── PromptDialog.xaml        # User prompt dialogs
  │
  ├── Models/                      # Data models
  │   └── AppConfig.cs             # Configuration data structures
  │
  └── Helpers/                     # Utility helpers
      ├── IconHelper.cs            # Icon loading and management
      ├── Win32Helper.cs           # Windows API utilities
      └── AutostartHelper.cs       # Autostart configuration
```

---

## 🔧 Configuration

Configuration is automatically saved to the user's local app data directory. Key settings can be modified through the Settings panel:

- Grid appearance and transparency
- Grid snap settings
- Autostart on Windows boot
- Desktop icon update frequency
- Theme preferences

Configuration files are stored in:
```
%APPDATA%\DeskManager\
```

---

## 🔨 Technology Stack

- **Language:** C#
- **Framework:** .NET 8.0 (net8.0-windows)
- **UI Framework:** WPF (Windows Presentation Foundation)
- **Additional:** Windows Forms for system tray integration
- **Windows APIs:** Win32 for low-level desktop interaction

---

## 🛠️ Building from Source

### Prerequisites
- Visual Studio 2022 or Visual Studio Code
- .NET 8.0 SDK
- Windows 10 or later

### Build Steps

1. **Clone the repository:**
   ```
   git clone <repository-url>
   cd deskmanager
   ```

2. **Open the project:**
   ```
   open DeskManager.sln
   ```

3. **Build the solution:**
   ```
   dotnet build DeskManager/DeskManager.csproj
   ```

4. **Run the application:**
   ```
   dotnet run --project DeskManager/DeskManager.csproj
   ```

---

## 📦 Building the Installer

Use the provided build scripts to create the installer:

### Using PowerShell:
```powershell
.\build-installer.ps1
```

### Using Batch:
```batch
build-installer.bat
```

See [INSTALLER_SETUP.md](INSTALLER_SETUP.md) for detailed installer configuration.

---

## 🚀 Release & Deployment

Follow the [BUILD_AND_RELEASE_GUIDE.md](BUILD_AND_RELEASE_GUIDE.md) for complete instructions on building and releasing new versions.

---

## 📝 License

See [LICENSE](LICENSE) file for licensing information.

---

## 👨‍💻 Development

### Project Structure Notes

- **Services Layer** - Handles all business logic (grid management, file storage, desktop interaction)
- **Windows Layer** - Contains WPF windows and UI components
- **Models Layer** - Data structures and configuration models
- **Helpers Layer** - Utility functions for Windows API and system integration

### Running in Development

To launch with settings panel on startup:
```
DeskManager.exe --settings
```

To force first-launch configuration:
```
DeskManager.exe --first-launch
```

---

## 🤝 Contributing

Contributions are welcome! Please follow these guidelines:

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Submit a pull request

---

**Version:** 1.0.0  
**Last Updated:** March 2026  
**Platform:** Windows 10+
