# Application icon

`AppIcon.png` is the approved artwork created with the built-in ImageGen tool.
The concept is four app tiles in a floating popup above a taskbar launcher,
with a cyan-to-indigo rounded square, softly shaded white forms and transparent corners.

`AppIcon.ico` contains transparent 32-bit PNG frames at 16, 20, 24, 32, 40,
48, 64, 96, 128 and 256 pixels. Regenerate it from the PNG by running
`./scripts/GenerateIcon.ps1` on Windows. Both the executable's Windows icon
and the embedded resource used by the tray and editor windows use this file.

## WORK group icon

`WorkIcon.png` was created with the built-in ImageGen tool in the same style:
a large white briefcase, rounded handle and red clasp on a scarlet-to-crimson
red rounded-square background, with transparent corners. The latest ImageGen
edit removes glossy highlights, reflective rims and glow for a matte finish,
while preserving the geometry and gentle diffuse shadows.

`WorkIcon.ico` contains the same ten Windows icon sizes as the application icon.
Regenerate it with `./scripts/GenerateIcon.ps1 -IconName WorkIcon`.

## GAMES group icon

`GamesIcon.png` was created with the built-in ImageGen tool in the same style:
a white game controller with a green directional pad and four green buttons
on a green-to-emerald rounded-square background, with transparent corners.
The latest ImageGen edit removes glossy highlights, reflective rims and glow
for the same matte finish as WORK, preserving the geometry and diffuse shadows.

`GamesIcon.ico` contains the same ten Windows icon sizes as the application icon.
Regenerate it with `./scripts/GenerateIcon.ps1 -IconName GamesIcon`.

## IA group icon

`IAIcon.png` was created with the built-in ImageGen tool using WORK as a matte
style reference: a white rounded microchip with three pins on each side and
a violet processor die on a violet-to-purple rounded-square background.
It uses gentle diffuse shadows without glossy highlights, reflective rims
or glow, and preserves transparent outer corners.

`IAIcon.ico` contains the same ten Windows icon sizes as the application icon.
Regenerate it with `./scripts/GenerateIcon.ps1 -IconName IAIcon`.
