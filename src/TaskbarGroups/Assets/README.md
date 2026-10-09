# Application icon

`AppIcon.png` is the approved artwork created with the built-in ImageGen tool.
The concept is four app tiles in a floating popup above a taskbar launcher,
with a cyan-to-indigo rounded square, softly shaded white forms and transparent corners.

`AppIcon.ico` contains transparent 32-bit PNG frames at 16, 20, 24, 32, 40,
48, 64, 96, 128 and 256 pixels. Regenerate it from the PNG by running
`./scripts/GenerateIcon.ps1` on Windows. Both the executable's Windows icon
and the embedded resource used by the tray and editor windows use this file.
