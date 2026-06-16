# ParallelScope

English | [日本語](./Readme.ja.md)

ParallelScope is a Windows desktop application that lets you browse folders and files across multiple configured root paths.
It is built with WPF and uses a local SQLite cache to speed up listing and search.

## Key Features

- Register multiple root folders (add/remove from the settings window)
- Folder tree + file list browsing UI
- Back/Forward/Up navigation
- Direct path input in the address bar
- Search under the current folder
  - Cache search runs first
  - If no match is found, a live file system scan runs
- Double-click in the list to navigate into folders or open files with the default app

## Requirements

- Windows
- .NET SDK 10.0 or later (`net10.0-windows`)

## Setup

```powershell
dotnet restore
```

## Run

```powershell
dotnet run --project ParallelScope.csproj
```

## Build

```powershell
dotnet build ParallelScope.csproj
# Release
dotnet publish -c Release
```

## Usage

1. After startup, click Settings > Open Settings.
2. Add one or more root folders to monitor, then save.
3. Select a folder in the left tree to display its contents on the right.
4. Enter a query in the search box, then press Enter or click Search.
5. Double-click an item in the list:
   - Folder: navigate into that folder
   - File: open with the default application

## Data Storage

It is saved in the folder under `%LOCALAPPDATA%\Packages\msmsrep.ParallelScope_77t1an0ygyrva\LocalState`.
Saved data will also be deleted when the app is uninstalled.

- `settings.json`: root folder settings
- `ParallelScope.sqlite`: file list cache

## Development Notes

### EF Core Migrations

This repository defines `dotnet-ef` (10.0.9) as a local tool.

```powershell
dotnet tool restore
dotnet ef migrations add <MigrationName>
dotnet ef database update
```

### Main Structure

- `MainWindow.xaml` / `MainWindow.xaml.cs`: main window
- `SettingsWindow.xaml` / `SettingsWindow.xaml.cs`: root folder settings dialog
- `ViewModels/`: UI logic
- `Data/`: settings/cache/DbContext
- `Migrations/`: EF Core migrations

## Privacy Policy

Last updated: 2026/6/14

### Data Collected and Stored

This app does not collect personal information such as account data, names, or email addresses.
However, for application functionality, the following data is stored locally on the user's device.

- Root folder settings (`settings.json`)
- File list cache (`ParallelScope.sqlite`)

Storage location: `%LOCALAPPDATA%\Packages\msmsrep.ParallelScope_77t1an0ygyrva\LocalState`

### Scope of Data Processing

File browsing and search are processed on the user's local machine.
There is no mechanism that uploads data to developer-managed servers for processing.

### External Transmission and Third-Party Sharing

This app does not automatically transmit user data to external services.
User data is not sold, shared, or provided to third parties.

### Cookies and Tracking Technologies

This is a desktop application and does not perform cookie-based tracking commonly used on websites.

### How to Delete Stored Data

Users can delete app-stored data by removing the following files.

- `%LOCALAPPDATA%\Packages\msmsrep.ParallelScope_77t1an0ygyrva\LocalState\settings.json`
- `%LOCALAPPDATA%\Packages\msmsrep.ParallelScope_77t1an0ygyrva\LocalState\ParallelScope.sqlite`

Saved data will also be deleted when the app is uninstalled.

### Contact

For privacy and other inquiries, please use [GitHub Issues](https://github.com/msmsrep/ParallelScope/issues).
