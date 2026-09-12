# ParallelScope

English | [日本語](./Readme.ja.md)

ParallelScope is a Windows desktop application that lets you browse folders and files across multiple configured root paths.
It is built with WPF and uses a local SQLite cache to speed up listing and search.

📖 **[User Guide](https://msmsrep.github.io/ParallelScope/)** — how to use the app, with screenshots.

## Key Features

- Register multiple root folders (add/remove/reorder from the settings window), plus excluded folders
- Folder tree + file list browsing UI
- Back/Forward/Up navigation
- Direct path input in the address bar
- Incremental search under the current folder (runs against the cache as you type)
- "All Files" mode: list every file under the current folder as one flat list
- Double-click in the list to navigate into folders or open files with the default app
- Show or hide hidden and system files/folders (both shown by default; available in the free version)
- Color theme (System / Light / Dark)
- Display language (System / English / Japanese; defaults to your Windows display language, available in the free version)
- Plus features: multiple tabs, split view (two panes), a collapsible folder tree per pane, "★ Favorites", "🕘 Recent" and "🕒 Frequently Used" folders in the tree (which of them to show
  and in what order), file list column customization (which columns, their order, and their widths), regular expression search, an in-memory file name index that speeds search up,
  and CSV export of the current list

## Release

- ver 1.4.7.0
  - Added regular expression search (Plus): the "Search" settings page switches the search box between substring and .NET regular expression matching
  - Added multiple tabs (Plus): each tab keeps its own folder, history, search text, view mode and sort order, and the layout is restored on the next start
  - Added a split view (Plus): two panes side by side or stacked, each with its own tree and file list
  - Added a "🕘 Recent" node to the folder tree (Plus)
  - Added "★ Favorites" and "🕒 Frequently Used" nodes to the folder tree (Plus)
- Ver 1.4.5.0 Added a "User Guide" menu item
- Ver1.4.0.0 Added Monthly Subscription feature
- Ver 1.3.0.0 Feature change
  - Added "All Files" mode
  - Changed search to incremental search
- Ver 1.2.0.0 Adjustments
  - Fixed display of search UI
  - Optimized speed of file scanning logic
- Ver 1.1.0.0 New features
  - Periodic scan execution (default 3 hours)
  - Scan execution from right-clicking a folder
  - Specification of exclusion folders
- Ver 1.0.0.0 Release

## Requirements

- Windows
- .NET SDK 10.0 or later (`net10.0-windows10.0.19041.0`)

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

## Test

```powershell
dotnet test Tests/ParallelScope.Tests/ParallelScope.Tests.csproj
```

Unit tests (xUnit) cover the UI-independent layers. See [Tests/README.md](./Tests/README.md) for what is covered and how to add tests.

## Usage

1. After startup, click Menu > Settings.
2. Add one or more root folders to monitor, then click "Save + Full Scan".
3. Select a folder in the left tree to display its contents on the right.
4. Type in the search box to search under the current folder (results update as you type).
5. Double-click an item in the list:
   - Folder: navigate into that folder
   - File: open with the default application
6. Turn on "All Files" to list every file under the current folder, regardless of depth.
7. Use "Menu > Export CSV..." to write the list you are looking at to a CSV file (Plus).
8. Open a new tab with "＋" on the tab strip or Ctrl+T, and split the window from "Menu > Split View" (both Plus).
9. Collapse the folder tree of a pane with the "☰" button left of the arrow buttons when you want the list at full width (Plus).
10. Switch the search box to regular expressions, or keep the file name index in memory for faster search, on the "Settings > Search" page (both Plus; each applies and is saved as soon as you toggle it).

See the [User Guide](https://msmsrep.github.io/ParallelScope/) for details.

## Data Storage

It is saved in the folder under `%LOCALAPPDATA%\Packages\msmsrep.ParallelScope_77t1an0ygyrva\LocalState`.
Saved data will also be deleted when the app is uninstalled.

- `settings.json`: root/excluded folders, scan interval, theme, display language, search options (regular expressions, file name index), hidden/system item visibility, tree node layout, file list column layout, favorites, and folder access counts
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

- `MainWindow.xaml` / `MainWindow.xaml.cs`: main window (menu and the host for the panes)
- `Views/BrowserPaneView.xaml`: a browsing pane (tab strip + folder tree + file list); the split view shows two of them
- `SettingsWindow.xaml` / `SettingsWindow.xaml.cs`: settings dialog (root folders / folder tree / search / display columns / theme / language / subscription / support)
- `ViewModels/`: UI logic (shell `MainWindowViewModel` / pane `BrowserPaneViewModel` / tab `BrowserTabViewModel`, each split into partial classes by responsibility)
- `Data/`: settings/cache/DbContext
- `Utilities/`: shared helpers (path normalization, CSV export, column definitions, virtual folders, etc.)
- `Services/`: Microsoft Store license lookup
- `Migrations/`: EF Core migrations
- `Tests/ParallelScope.Tests/`: unit tests
- `docs/`: the published user guide

## ParallelScope Plus (Monthly Subscription)

Some features are offered as "ParallelScope Plus", a monthly subscription add-on on the Microsoft Store.

- **Plus features**:
  - Multiple tabs (the tab strip is hidden without a subscription; a saved tab layout is kept as is and comes back when you subscribe)
  - Split view (two panes) ("Menu > Split View" stays greyed out without a subscription)
  - Collapsing the folder tree of a pane (the "☰" button is hidden without a subscription, and the tree always stays open)
  - "★ Favorites" / "🕘 Recent" / "🕒 Frequently Used" folders in the tree, and "Folder Tree" in the settings window for choosing which of them to show and in what order (hidden entirely without a subscription)
  - "Display Columns" in the settings window (which columns the file list shows, their order, and their widths)
  - Regular expression search, and keeping the file name index in memory to speed search up (the "Search" page of the settings window; both greyed out without a subscription)
  - "Menu > Export CSV..." (exporting the displayed list)
- All other features remain free without a subscription. Locked features are either shown grayed out with only their controls disabled, or offer to open the Subscription page when used
- You can subscribe from the "Subscribe to Plus" button on the "Settings > Subscription" page in the Microsoft Store version of the app
- Payment, billing, and cancellation are all handled by the Microsoft Store. 

### Open Source and Paid Features

The full source code of this app is public, including the in-app purchase implementation. The subscription state is verified against the Microsoft Store license, so **the purchase flow (and the Plus feature lock) only takes effect in the version installed from the Microsoft Store**.

## Support

This app (ParallelScope) is independently developed and managed. We accept voluntary support to help with continuous updates and feature improvements.  
If you would like to support us, it would be a great encouragement if you could do so via the link below.  
（This support is a voluntary donation without consideration, and no benefits are provided in return.）  

- Ko‑fi: <https://ko-fi.com/msmsrep>  
- GitHub Sponsors: <https://github.com/sponsors/msmsrep>

## Privacy Policy

Last updated: 2026/7/26

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

### In-App Purchases (ParallelScope Plus)

Purchases, billing, and license management for the Plus subscription are handled by the Microsoft Store.
The app communicates with the Microsoft Store through the OS to check the subscription state, but it never obtains or stores payment information (such as credit card numbers).
You can review your purchase history and manage the subscription from your Microsoft account.

### Cookies and Tracking Technologies

This is a desktop application and does not perform cookie-based tracking commonly used on websites.

### How to Delete Stored Data

Users can delete app-stored data by removing the following files.

- `%LOCALAPPDATA%\Packages\msmsrep.ParallelScope_77t1an0ygyrva\LocalState\settings.json`
- `%LOCALAPPDATA%\Packages\msmsrep.ParallelScope_77t1an0ygyrva\LocalState\ParallelScope.sqlite`

Saved data will also be deleted when the app is uninstalled.

### Contact

For privacy and other inquiries, please use [GitHub Issues](https://github.com/msmsrep/ParallelScope/issues).
