using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace TaskbarGroups;

static class GroupShortcuts {
    internal static void RefreshIcons(Group[] groups,string? pinnedRoot=null) {
        pinnedRoot??=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"Microsoft","Internet Explorer","Quick Launch","User Pinned");
        var paths=new List<string>();
        var taskbar=Path.Combine(pinnedRoot,"TaskBar");
        if(Directory.Exists(taskbar))paths.AddRange(Directory.EnumerateFiles(taskbar,"*.lnk"));
        // Explorer also keeps implicit, unpinned group shortcuts. Their icon
        // takes precedence over the window's current WM_SETICON artwork.
        var implicitRoot=Path.Combine(pinnedRoot,"ImplicitAppShortcuts");
        if(Directory.Exists(implicitRoot))foreach(var folder in Directory.EnumerateDirectories(implicitRoot))paths.AddRange(Directory.EnumerateFiles(folder,"*.lnk"));
        object? shell=null;
        try {
            foreach(var path in paths) {
                object? shortcut=null;
                try {
                    string id=Native.ShortcutAppId(path);var group=groups.FirstOrDefault(g=>id.Equals(Native.AppId(g),StringComparison.OrdinalIgnoreCase));
                    if(group==null)continue;
                    shell??=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);shortcut=((dynamic)shell!).CreateShortcut(path);
                    // The same group configuration can be copied into another
                    // installation. Only refresh shortcuts targeting this copy.
                    if(!string.Equals((string)((dynamic)shortcut!).TargetPath,Environment.ProcessPath,StringComparison.OrdinalIgnoreCase))continue;
                    string icon=Native.IconResource(group);
                    if(string.Equals((string)((dynamic)shortcut).IconLocation,icon,StringComparison.OrdinalIgnoreCase))continue;
                    ((dynamic)shortcut).IconLocation=icon;((dynamic)shortcut).Save();
                    Native.SetShortcutIdentity(path,group);Native.NotifyFileChanged(path);
                } catch(Exception error){Program.Log("Icono del acceso de grupo: "+error.Message);}
                finally {if(shortcut!=null)Marshal.ReleaseComObject(shortcut);}
            }
        } finally {if(shell!=null)Marshal.ReleaseComObject(shell);}
    }
}
