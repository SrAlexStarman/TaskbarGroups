using System;
using System.Drawing;
using System.Windows.Forms;

namespace TaskbarGroups;

static class AppIcon
{
    public static Icon Window { get; } = Load(SystemInformation.IconSize);
    public static Icon Tray { get; } = Load(SystemInformation.SmallIconSize);

    static Icon Load(Size size)
    {
        using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream("TaskbarGroups.AppIcon.ico")
            ?? throw new InvalidOperationException("The application icon is missing from the build.");
        using var icon = new Icon(stream, size);
        return (Icon)icon.Clone();
    }
}
