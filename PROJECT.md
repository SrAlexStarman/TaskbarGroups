# TaskbarGroups project overview

TaskbarGroups is a portable Windows application for organizing application shortcuts into editable groups in the Windows taskbar. Each group appears as a native taskbar icon using either a symbol and color or an imported image. Hovering over or clicking that icon opens a panel containing the group's applications.

The public copy starts with no groups. On first launch, the editor opens so each user can create their own groups and choose applications discovered on their computer. The current interface is in Spanish.

## Project files

The project folder contains the clean source checkout, a release ZIP, and an extracted application:

| Path | Purpose |
| --- | --- |
| `src/TaskbarGroups/` | C# source, application manifest, and .NET project |
| `src/TaskbarGroups/Assets/` | Application artwork and optional Work, Games, and IA group artwork, with PNG sources and ICO files |
| `scripts/Publish.ps1` | Build, configuration checks, and clean ZIP packaging |
| `scripts/GenerateIcon.ps1` | Generates Windows ICO sizes from an artwork PNG; accepts `-IconName` |
| `.github/workflows/build.yml` | Windows build and downloadable ZIP artifact in GitHub Actions |
| `README.md` | User instructions, build commands, and implementation notes |
| `LEEME.txt` | Spanish instructions also included with the application |
| `PROJECT.md` | Project structure, implementation status, and local validation record |
| `groups.example.json` | Empty configuration template |
| `TaskbarGroups-public.zip` | Copied clean release package |
| `dist/TaskbarGroups/` | Extracted release, ready to run |
| `global.json` | Selects a compatible .NET 10 SDK |

The checkout retains its Git history and points to [SrAlexStarman/TaskbarGroups](https://github.com/SrAlexStarman/TaskbarGroups). Current development is on `codex/popup-icons-and-animation`. The initial clean distribution and the earlier application-icon and popup-animation changes have already been merged into `main`; the current changes extend that implementation.

## Main behavior

- Users can create, rename, reorder, and delete groups and their application shortcuts.
- Group icons use reusable symbols with a selected color or imported ICO, PNG, JPG, or BMP artwork. The editor previews either option and keeps both when switching.
- Generated symbols fill more of the available icon area. Imported artwork retains its proportions and transparency, with transparent outer margins trimmed. The app copies imported artwork locally so the original file is no longer needed.
- Applications can be added from the installed application list, executable files, shortcuts, or URLs.
- Application buttons display icons, show names in hover tooltips, and mark running applications beneath their icons. Running status refreshes while the panel is visible and includes minimized windows.
- Hover delay is adjustable. Clicking an icon also opens its panel; Esc closes it.
- The panel is positioned using the hovered taskbar button and its monitor, with support for different display scaling.
- Popups slide into view from behind the taskbar, including when switching groups. Animation respects the Windows animation-effects setting; outlines and tooltips use rounded shapes without popup shadows.
- Windows thumbnail previews can be suppressed while interacting with a group. This option is enabled by default and can be changed in the editor.
- Groups can be pinned using generated shortcuts, and startup with Windows is optional.
- Saving a group's appearance refreshes its window icon and generated shortcut, plus matching pinned and implicit Windows shortcuts for this installation.

Existing individual application pins are managed by the user through Windows. Group icons remain active while the utility runs.

## How the code fits together

| File | Responsibility |
| --- | --- |
| `Program.cs` | Entry point, configuration models and persistence, single-instance communication, group windows, hover management, and popup rendering |
| `GroupArtwork.cs` | Imports, normalizes, trims, and renders custom artwork; creates portable local icon files |
| `GroupShortcuts.cs` | Refreshes matching pinned and implicit Shell shortcuts without changing unrelated applications or other installations |
| `PopupFrame.cs` | Rounded, shadow-free popup outline and entrance animation, with outline mouse input passed through to the application controls |
| `PopupHints.cs` | Application-name tooltips that follow popup position and display scaling without intercepting the pointer |
| `PopupRunningState.cs` | Refreshes running indicators asynchronously while the panel is visible |
| `RunningApps.cs` | Resolves application identities and matches running processes and windows, including minimized windows |
| `Editor.cs` | Group and shortcut editing, installed application discovery, generated shortcuts, and startup option |
| `Native.cs` | Windows interop, taskbar identities, icons, and native window helpers |
| `NativeUia.cs` | Windows UI Automation access used to locate taskbar elements |
| `NativeTaskbar.cs` | Identifies accessible group buttons by AppUserModelID or Explorer display names; matches group artwork when accessibility elements are missing |
| `NativePreviewBlocker.cs` | Identifies Explorer thumbnail hosts and hides them within the active group interaction |
| `Verification.cs` | Configuration checks and a synthetic group for desktop smoke tests |

The manager creates a taskbar window for each group with a stable AppUserModelID and tracks its button bounds on each monitor. It matches UI Automation elements by that identity first, with localized Explorer display names as a fallback. When accessibility elements are missing, it matches group artwork in an in-memory taskbar capture. Accessibility queries and visual detection run independently so a stalled provider cannot block the visual fallback. When the pointer remains over a group for the configured delay, the manager opens the popup above that button. Selecting an application launches its configured target and arguments.

Preview suppression identifies classic Explorer thumbnail windows and modern Windows 11 XAML hosts. The modern host is checked against the real taskbar's process and thread, including hosts whose bounds cover the full monitor. Suppression is scoped to interaction with the utility's groups and excludes menus and unrelated applications. Native thumbnail callbacks can also open the group panel on the correct monitor. Explorer window classes and behavior can change across Windows versions, so this integration is an area to revisit when investigating compatibility problems.

Icon refresh registers the full icon resource before setting the window's application identity and sends file-specific Shell change notifications. Explorer can prefer its cached implicit shortcut over the current window icon, so `GroupShortcuts.cs` also updates shortcuts whose group identity and executable path both match. Group IDs stay stable. Single-instance communication normalizes installation paths and user names so different path casing does not create separate command channels.

## Run the copied release

Use Windows 11 x64 with .NET Desktop Runtime 10 x64 installed. From this project folder, run:

```powershell
.\dist\TaskbarGroups\TaskbarGroups.exe
```

Create a group with **+ Crear grupo**, add applications, and select **Guardar cambios**. Double-click the tray icon or choose **Crear y editar grupos…** to reopen the editor. Use **Salir** in the tray menu to stop the utility.

The release can also be extracted from `TaskbarGroups-public.zip` into another writable folder. Keep its files together. If an existing installation is moved, pinned and startup shortcuts may need to be recreated for the new executable path.

## Build and check changes

Building requires Windows and the .NET SDK 10. Run these commands from the project root:

```powershell
dotnet build src/TaskbarGroups/TaskbarGroups.csproj -c Release
dotnet src/TaskbarGroups/bin/Release/net10.0-windows/TaskbarGroups.dll --config-test
./scripts/Publish.ps1
```

The publish script creates `artifacts/TaskbarGroups-win-x64.zip` from a fresh build and runs configuration checks before packaging. It copies only the four application files and two help documents. Rebuilding produces a new package under `artifacts/`; it does not replace the copied ZIP or extracted release automatically.

The configuration checks cover empty startup, saved edits, backups, rejected invalid edits, thumbnail suppression scope, taskbar identities and localized labels, case-insensitive instance paths, application icon lookup, running application identities, custom artwork persistence and display sizes, editor appearance switching, and shortcut icon refresh. Shortcut fixtures also verify that unrelated applications and other installations are left unchanged.

An optional `--self-test` mode uses a synthetic group and writes local diagnostic reports and screenshots. Run it in a separate writable build folder on an interactive desktop. Actual hover behavior, placement on multiple monitors, and application launching also need manual testing when those parts of the code change.

## Current validation and status — 2026-10-12

- The latest Release publish and configuration checks passed. The clean ZIP contains exactly the four application files and the two help documents.
- The desktop self-test passed with the synthetic custom-icon group detected on both connected monitors. It checked periodic and background detection, hover delay, leaving the panel, placement and native callbacks on each monitor, preview suppression rules and real host identification, native window hiding, and popup layouts with empty, single, long-name, and 60-application groups.
- Repeated manual hovering over Trabajo, Juego, and IA was confirmed to show their application panels without Windows thumbnails covering them.
- The IA icon was confirmed to update to the purple chip after saving the new image again. Earlier attempts still showed the previous icon even after the window and Shell shortcut metadata had been refreshed. The final confirmation therefore includes that additional save; it does not establish that every existing Windows cache refreshes after the first save.
- The local extracted application matches the latest packaged application assembly, and the copied public ZIP matches the generated ZIP. Local validation above is separate from the GitHub Actions result for the next pushed commit.

## Local data and clean distribution

The app stores settings beside its executable in `groups.json`, with the previous configuration in `groups.json.bak`. It generates group icons in `icons/`, pinning shortcuts in `Grupos/`, imported shortcuts in `Accesos/`, and imported group artwork in `AppIcons/`. With the extracted release, these files are created under `dist/TaskbarGroups/` as the user configures the app.

The source repository includes reusable artwork under `src/TaskbarGroups/Assets/`; its README records the artwork and regeneration commands. These assets do not create predefined groups or add separate icon files to the public package. Personal group configuration, installed application catalogs, imported shortcuts and artwork, generated icons, logs, and diagnostic captures are excluded from version control. Build output, the local extracted release, and the copied ZIP are also excluded. The public ZIP contains exactly the executable, application assembly, two runtime metadata files, and two help documents and starts with no groups.
