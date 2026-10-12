# TaskbarGroups

Create editable groups of application shortcuts in the Windows taskbar. Each group has its own native taskbar icon; hovering over it opens a panel of selectable applications above the taskbar.

The public distribution starts empty. It contains no predefined groups, application catalog, personal shortcuts, or icon files. The editor discovers applications on the computer where it runs. The current interface is in Spanish.

## Features

- Create, rename, reorder, and delete groups and their shortcuts.
- Choose a group symbol and color, or import an ICO, PNG, JPG, or BMP icon, with a live preview.
- Add installed applications, executable files, shortcuts, or URLs.
- Open groups by hover or click, with adjustable hover delay.
- Show application icons in each group, with application names in hover tooltips.
- Mark running applications with a Windows-style indicator beneath their icons; update automatically for open and minimized windows.
- Slide popups into view from behind the taskbar, including when switching groups; respect Windows' animation effects setting.
- Place the popup on the monitor containing the hovered taskbar button, including displays with different scaling.
- Suppress the Windows thumbnail flyout while interacting with a group. This can be disabled in the editor.
- Optionally start with Windows; keep the previous configuration as a backup when saving.

## Run

The packaged app targets **Windows 11 x64** and requires the **.NET Desktop Runtime 10 x64**, available from [Microsoft](https://dotnet.microsoft.com/en-us/download/dotnet/10.0). It runs without administrator privileges.

1. Extract the ZIP into a writable folder and run `TaskbarGroups.exe`. Keep the extracted files together.
2. The editor opens automatically on first launch. Click **+ Crear grupo**, enter its name, and choose **Color y símbolo** or **Icono de archivo**. For a custom icon, click **Elegir icono…**. Add applications and click **Guardar cambios**.
3. The group icons appear in the taskbar while the utility runs. Hover over or click an icon to open its applications.
4. To keep a group pinned, open **Accesos de grupo para anclar…** from the tray menu and pin its generated shortcut. Unpin individual application shortcuts yourself if you want to replace them with groups.

Double-click the tray icon or choose **Crear y editar grupos…** in its menu to change groups. **Esc** closes a popup. **Salir** in the tray menu stops the utility. To show icons on additional monitors, enable the corresponding Windows taskbar setting.

Settings and generated assets are stored beside the executable: `groups.json` and its backup, `icons/`, `Grupos/`, imported shortcuts in `Accesos/`, and imported group artwork in `AppIcons/`. Custom icons are copied locally, retaining transparency and proportions; transparent outer margins are trimmed to make better use of the taskbar icon space. The original image is no longer needed. Switching appearance keeps the previous icon, symbol, and color available. Moving the installation can require updating pinned or startup shortcuts. The empty `groups.example.json` is a template, not an imported configuration.

## Build and verify

Install the [.NET SDK 10](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) on Windows, then run from the repository root:

```powershell
dotnet build src/TaskbarGroups/TaskbarGroups.csproj -c Release
dotnet src/TaskbarGroups/bin/Release/net10.0-windows/TaskbarGroups.dll --config-test
./scripts/Publish.ps1
```

The checks cover empty startup, saved edits, backups, rejected invalid edits, preview suppression scope, taskbar button identities and localized labels, case-insensitive instance paths, icon transparency and orientation, registered Windows app icon lookup (when registered apps are available), running application identity matching, custom group icon persistence, editor appearance switching, and custom artwork detection at different display scales. `Publish.ps1` builds a fresh release, runs those checks, and produces `artifacts/TaskbarGroups-win-x64.zip`. The package uses a file allowlist so installation data cannot be copied into it. GitHub Actions builds and attaches the same ZIP to successful workflow runs.

For an interactive desktop smoke test, run the app with `--self-test` from a separate writable build folder. It uses a synthetic group with a custom icon and checks detection, hover opening, and preview suppression on the available taskbars. It writes local diagnostic reports and screenshots. Those files are excluded from version control and packaging. This test does not replace manual hover, multi-monitor, or application-launch verification.

## Implementation notes

This is a portable .NET Windows Forms application using Windows UI Automation and native taskbar window identities. It locates group buttons by their AppUserModelID, with support for Explorer's display names and localized running-window counts. When Explorer omits accessibility elements, it matches the group artwork in an in-memory taskbar capture. Accessibility queries and visual detection run independently so a stalled provider cannot block the visual fallback. The desktop self-test checks both periodic background detection and panel opening on the detected monitors. Saving artwork registers the complete icon resource before notifying the taskbar, updates the group's shortcut, and sends a file-specific Shell change notification. It also refreshes existing pinned and implicit Shell shortcuts with the same group identity and executable path, keeping unrelated apps and other installations unchanged.

Preview suppression recognizes Explorer's thumbnail windows and hides them only while a group is active. It does not change system-wide preview settings or inject code into Explorer. Explorer behavior varies across Windows updates, so hover detection and preview suppression may need maintenance on other builds. The current implementation was manually checked on the development computer; CI checks configuration and compilation.

Do not commit files from an existing installation. Local configurations, imported shortcuts, generated icons, catalogs, logs, and diagnostic captures are ignored by this repository.
