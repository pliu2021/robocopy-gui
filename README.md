# Robocopy Tool

A modern Windows desktop tool for filtering and copying MF4 measurement files with Robocopy.

![License](https://img.shields.io/badge/license-MIT-blue.svg)

## Features

- **Fixed folder keyword filtering** - Filter folders by 14 predefined keywords
- **MF4-only copying** - Copy only `.MF4` measurement files
- **Multiple keyword selection** - Select one or more keywords to match folders
- **Incremental copying** - Skip existing destination files (no overwrite)
- **Automatic Run** - Automatically repeat copy cycle every 10 minutes
- **In-memory Activity view** - Real-time log display without disk writes
- **Single-file executable** - Self-contained Windows executable
- **Modern WPF interface** - Windows 11 Fluent design with dark theme

## Requirements

- Windows 10 or Windows 11
- .NET 8 SDK (for development)
- Visual Studio Code or Visual Studio
- Windows built-in Robocopy

## Build

```bash
dotnet restore
dotnet build -c Release
```

## Publish

Run the build script:

```bash
.\build-release.cmd
```

Output: `release\RobocopyTool.exe`

## Usage

1. Set **Source Directory** (folder containing keyword-matched subfolders)
2. Set **Destination Directory** (target location)
3. Select one or more **Keywords** (default: `MT_RE`)
4. Click **START** to begin copying
5. Enable **Automatic Run** for periodic execution (every 10 minutes)

## Safety

- Does **not** use `/MIR` (no mirroring or deletion)
- Does **not** delete source or destination files
- Copies **only** `.MF4` files
- Existing destination files are **not overwritten** (`/XC /XN /XO`)

## Settings

Settings are stored in:
```
%LOCALAPPDATA%\RobocopyTool\settings.json
```

Contains:
- Last used source/destination paths
- Selected keywords
- Thread count
- Automatic Run preference

## Project Structure

```
robocopy-gui/
├── ViewModels/          # MainViewModel, MVVM logic
├── Views/               # MainWindow, AboutWindow
├── Models/              # AppSettings, KeywordOption
├── Services/            # SettingsService
├── Styles/              # Colors, Buttons, ScrollBars
├── Assets/              # Application icon
├── TestData/            # Test fixtures (0-byte MF4 files)
├── release/             # Final single-file EXE
└── LICENSE              # MIT License
```

## Third-party Dependencies

- **CommunityToolkit.Mvvm** - MVVM helpers ([MIT License](https://licenses.nuget.org/MIT))
- **Wpf.Ui** - Fluent UI controls ([MIT License](https://licenses.nuget.org/MIT))

Third-party dependencies remain subject to their respective licenses.

## License

This project is licensed under the [MIT License](LICENSE).

Copyright (c) 2026 Liu Pengfei
