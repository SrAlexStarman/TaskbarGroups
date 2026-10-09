# TaskbarGroups

Create editable groups of application shortcuts in the Windows taskbar. Each group has its own native taskbar icon; hovering over it opens a panel of selectable applications above the taskbar.

The public distribution starts empty. It contains no predefined groups, application catalog, personal shortcuts, or icon files. The editor discovers applications on the computer where it runs. The current interface is in Spanish.

## Features

- Create, rename, reorder, and delete groups and their shortcuts.
- Choose a group symbol and color; icons are generated locally.
- Add installed applications, executable files, shortcuts, or URLs.
- Open groups by hover or click, with adjustable hover delay.
- Slide popups into view from behind the taskbar, including when switching groups; respect Windows' animation effects setting.
- Place the popup on the monitor containing the hovered taskbar button, including displays with different scaling.
- Suppress the Windows thumbnail flyout while interacting with a group. This can be disabled in the editor.
- Optionally start with Windows; keep the previous configuration as a backup when saving.

## Run

The packaged app targets **Windows 11 x64** and requires the **.NET Desktop Runtime 10 x64**, available from [Microsoft](https://dotnet.microsoft.com/en-us/download/dotnet/10.0). It runs without administrator privileges.

1. Extract the ZIP into a writable folder and run `TaskbarGroups.exe`. Keep the extracted files together.
2. The editor opens automatically on first launch. Click **+ Crear grupo**, choose its name, symbol, and color, and add applications. Click **Guardar cambios**.
3. The group icons appear in the taskbar while the utility runs. Hover over or click an icon to open its applications.
4. To keep a group pinned, open **Accesos de grupo para anclar…** from the tray menu and pin its generated shortcut. Unpin individual application shortcuts yourself if you want to replace them with groups.

Double-click the tray icon or choose **Crear y editar grupos…** in its menu to change groups. **Esc** closes a popup. **Salir** in the tray menu stops the utility. To show icons on additional monitors, enable the corresponding Windows taskbar setting.

Settings and generated assets are stored beside the executable: `groups.json` and its backup, `icons/`, `Grupos/`, and imported shortcuts in `Accesos/`. Moving the installation can require updating pinned or startup shortcuts. The empty `groups.example.json` is a template, not an imported configuration.

## Build and verify

Install the [.NET SDK 10](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) on Windows, then run from the repository root:

```powershell
dotnet build src/TaskbarGroups/TaskbarGroups.csproj -c Release
dotnet src/TaskbarGroups/bin/Release/net10.0-windows/TaskbarGroups.dll --config-test
./scripts/Publish.ps1
```

The checks cover empty startup, saved edits, backups, rejected invalid edits, preview suppression scope, icon transparency and orientation, and registered Windows app icon lookup (when registered apps are available). `Publish.ps1` builds a fresh release, runs those checks, and produces `artifacts/TaskbarGroups-win-x64.zip`. The package uses a file allowlist so installation data cannot be copied into it. GitHub Actions builds and attaches the same ZIP to successful workflow runs.

For an interactive desktop smoke test, run the app with `--self-test` from a separate writable build folder. It uses a synthetic group and writes local diagnostic reports and screenshots. Those files are excluded from version control and packaging. This test does not replace manual hover, multi-monitor, or application-launch verification.

## Implementation notes

This is a portable .NET Windows Forms application using Windows UI Automation and native taskbar window identities. It searches the taskbar for its own group buttons; when Explorer omits their accessibility elements, it matches the generated group artwork in an in-memory taskbar capture.

Preview suppression recognizes Explorer's thumbnail windows and hides them only while a group is active. It does not change system-wide preview settings or inject code into Explorer. Explorer behavior varies across Windows updates, so hover detection and preview suppression may need maintenance on other builds. The current implementation was manually checked on the development computer; CI checks configuration and compilation.

Do not commit files from an existing installation. Local configurations, imported shortcuts, generated icons, catalogs, logs, and diagnostic captures are ignored by this repository.
