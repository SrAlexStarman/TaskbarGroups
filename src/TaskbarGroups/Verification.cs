using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TaskbarGroups;

static class Verification {
    internal static Config DesktopConfig() {
        const string artworkPath="self-test-group.ico";
        using(var artwork=CustomTestArtwork())using(var icon=GroupArtwork.ToIcon(artwork))using(var file=File.Create(Path.Combine(Program.Base,artworkPath)))icon.Save(file);
        return new Config {Groups=new() {new Group {
            Id="selftest",Name="Test group",Symbol="folder",Color="#6EADFF",UseCustomIcon=true,IconPath=artworkPath,
            Apps=new() {new AppEntry {Name="File Explorer",Target=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"explorer.exe")}}
        }}};
    }

    internal static int Run() {
        var directory=Path.Combine(Path.GetTempPath(),"TaskbarGroups-tests-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path=Path.Combine(directory,"groups.json");
        try {
            var empty=Program.Load(path);
            Require(empty.Groups.Count==0&&empty.SuppressWindowsPreview,"First launch must have no groups and keep preview suppression enabled.");
            Require(!File.Exists(path),"Loading must not import or write a personal configuration.");
            var config=new Config();config.Groups.Add(new Group {Id="test",Name="Test group",Symbol="folder"});
            config.Groups[0].Apps.Add(new AppEntry{Name="Example app",Target="example.exe"});
            Program.Save(config,path);
            config.Groups[0].Name="Renamed group";config.HoverDelayMs=340;Program.Save(config,path);
            var restored=Program.Load(path);
            Require(restored.Groups.Single().Name=="Renamed group"&&restored.HoverDelayMs==340&&!restored.Groups.Single().UseCustomIcon,"Saved edits and existing symbol-based groups must survive a reload.");
            Require(Program.Load(path+".bak").Groups.Single().Name=="Test group","Saving must preserve the previous configuration.");
            var saved=File.ReadAllText(path);config.Groups[0].Name="";
            bool rejected=false;try {Program.Save(config,path);}catch {rejected=true;}
            Require(rejected&&File.ReadAllText(path)==saved,"An invalid edit must not overwrite saved groups.");
            var host=new Rectangle(-1920,0,1920,1080);var panel=new Rectangle(-1000,800,320,200);
            Require(NativePreviewBlocker.MatchesPreview("XamlExplorerHostIslandWindow",true,host,panel,false,true,true),"A monitor-sized Taskbar preview host must be recognized.");
            Require(!NativePreviewBlocker.MatchesPreview("XamlExplorerHostIslandWindow",true,host,panel,false,false,true),"Other shell threads must be excluded.");
            Require(!NativePreviewBlocker.MatchesPreview("XamlExplorerHostIslandWindow",true,host,panel,false,true,false),"The pointer must belong to a group.");
            Require(!NativePreviewBlocker.MatchesPreview("XamlExplorerHostIslandWindow",true,host,panel,true,true,true),"Context menus must be excluded.");
            Require(NativePreviewBlocker.MatchesTaskbarThread(8,100,8,100),"Taskbar preview ownership must not depend on permission to open Explorer's thread.");
            Require(!NativePreviewBlocker.MatchesTaskbarThread(9,100,8,100)&&!NativePreviewBlocker.MatchesTaskbarThread(8,200,8,100)&&!NativePreviewBlocker.MatchesTaskbarThread(0,100,0,100)&&!NativePreviewBlocker.MatchesTaskbarThread(8,0,8,0),"Other shell threads, processes and missing window identities must be excluded.");
            VerifyTaskbarButtonNames();
            Require(Program.InstancePipe(directory,Environment.UserName)==Program.InstancePipe(directory.ToLowerInvariant()+Path.DirectorySeparatorChar,Environment.UserName.ToUpperInvariant()),"Launching the same Windows path with different capitalization must reach the running instance.");
            VerifyIconBitmaps();VerifyRegisteredAppIcon();VerifyRunningAppIdentities();VerifyGroupArtwork(directory);VerifyCustomIconSizes();VerifyTaskbarIconRefresh(directory);VerifyShellShortcutRefresh(directory);
            Console.WriteLine("PASS: clean startup, persistence, backup, validation, preview scope, app icons, running app identities, custom group icons, native icon sizes, editor appearance selection and taskbar icon refresh.");return 0;
        } catch(Exception error) {Console.Error.WriteLine(error);return 1;}
        finally {foreach(var file in Directory.EnumerateFiles(directory,"*",SearchOption.AllDirectories))File.Delete(file);foreach(var folder in Directory.EnumerateDirectories(directory,"*",SearchOption.AllDirectories).OrderByDescending(p=>p.Length))Directory.Delete(folder);Directory.Delete(directory);}
    }
    static void VerifyTaskbarButtonNames() {
        var work=new Group{Id="work",Name="Trabajo"};var longer=new Group{Id="longer",Name="Trabajo: proyectos"};var groups=new[]{work,longer};
        foreach(var label in new[]{"Trabajo","Trabajo: 1 ventana en ejecución","Trabajo: 2 running windows","Grupos · Trabajo, 1 ventana en ejecución","Trabajo - 1 ventana en ejecución"})
            Require(NativeTaskbar.MatchAccessibleGroup(label,"",groups)==work,"The actual Explorer taskbar label must identify the group: "+label);
        Require(NativeTaskbar.MatchAccessibleGroup("Trabajo: proyectos: 1 ventana en ejecución","",groups)==longer,"Groups sharing a name prefix must remain distinct.");
        Require(NativeTaskbar.MatchAccessibleGroup("A renamed shell label","Appid: "+Native.AppId(work),groups)==work,"The taskbar app identity must take priority over its displayed name.");
        Require(NativeTaskbar.MatchAccessibleGroup("Trabajo: 1 ventana en ejecución","Appid: Other.Application",groups)==null,"A different app with the same label must remain excluded.");
        foreach(var label in new[]{"Trabajador: 1 ventana en ejecución","Documento Trabajo","Trabajo personal: 1 ventana en ejecución"})
            Require(NativeTaskbar.MatchAccessibleGroup(label,"",groups)==null,"Unrelated taskbar apps must not match a group: "+label);
    }
    static void VerifyTaskbarIconRefresh(string directory) {
        var group=new Group{Id="iconrefresh"+Guid.NewGuid().ToString("N"),Name="Icon refresh",Symbol="ai",Color="#6EADFF"};
        var exported=new System.Collections.Generic.List<string>();var shortcutPath=Path.Combine(directory,"icon-refresh.lnk");
        using var window=new Form{ShowInTaskbar=false};
        try {
            Native.WriteGroupIcon(group);exported.Add(Native.IconPath(group));Native.SetWindowIdentity(window.Handle,group);
            var generated=Native.WindowIconResource(window.Handle);
            Require(generated==Native.IconResource(group)&&generated.EndsWith(",0",StringComparison.Ordinal),"The taskbar icon reference must include the ICO resource index.");
            var artworkPath=Path.Combine(directory,"replacement.png");using(var artwork=CustomTestArtwork())artwork.Save(artworkPath,ImageFormat.Png);
            group.UseCustomIcon=true;group.IconPath=artworkPath;Native.WriteGroupIcon(group);exported.Add(Native.IconPath(group));Native.SetWindowIdentity(window.Handle,group);
            var replacement=Native.WindowIconResource(window.Handle);
            Require(replacement==Native.IconResource(group)&&replacement!=generated&&Native.WindowAppId(window.Handle)==Native.AppId(group),"Changing artwork must update Windows' icon resource while keeping the same group identity.");
            Editor.CreateShortcut(shortcutPath,"--show "+group.Id,group);
            object? shell=null,shortcut=null;
            try {
                shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);shortcut=((dynamic)shell!).CreateShortcut(shortcutPath);
                Require(string.Equals((string)((dynamic)shortcut!).IconLocation,replacement,StringComparison.OrdinalIgnoreCase)&&Native.ShortcutAppId(shortcutPath)==Native.AppId(group),"The group shortcut must use the same updated artwork and identity as its taskbar window.");
            } finally {if(shortcut!=null)Marshal.ReleaseComObject(shortcut);if(shell!=null)Marshal.ReleaseComObject(shell);}
            group.UseCustomIcon=false;Native.SetWindowIdentity(window.Handle,group);
            Require(Native.WindowIconResource(window.Handle)==generated,"Switching back to a symbol must refresh the taskbar icon reference again.");
        } finally {foreach(var file in exported.Distinct())if(File.Exists(file))File.Delete(file);}
    }
    static void VerifyShellShortcutRefresh(string directory) {
        var group=new Group{Id="shortcutrefresh"+Guid.NewGuid().ToString("N"),Name="Shortcut refresh",Color="#6EADFF",Symbol="ai"};
        var other=new Group{Id="other"+Guid.NewGuid().ToString("N"),Name="Unrelated"};
        var root=Path.Combine(directory,"pinned");var pinned=Path.Combine(root,"TaskBar");var implicitFolder=Path.Combine(root,"ImplicitAppShortcuts","example");
        Directory.CreateDirectory(pinned);Directory.CreateDirectory(implicitFolder);
        string pinnedLink=Path.Combine(pinned,"group.lnk"),implicitLink=Path.Combine(implicitFolder,"group.lnk"),unrelatedLink=Path.Combine(pinned,"other.lnk"),otherCopyLink=Path.Combine(implicitFolder,"other-copy.lnk");
        Editor.CreateShortcut(pinnedLink,"--show "+group.Id,group);Editor.CreateShortcut(implicitLink,"--show "+group.Id,group);Editor.CreateShortcut(unrelatedLink,"--show "+other.Id,other);Editor.CreateShortcut(otherCopyLink,"--show "+group.Id,group);
        object? shell=null,shortcut=null;
        try {
            shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);shortcut=((dynamic)shell!).CreateShortcut(otherCopyLink);((dynamic)shortcut).TargetPath=Path.Combine(directory,"another-installation.exe");((dynamic)shortcut).Save();Marshal.ReleaseComObject(shortcut);shortcut=null;
            var untouched=File.ReadAllBytes(unrelatedLink);var otherCopy=File.ReadAllBytes(otherCopyLink);var oldIcon=Native.IconResource(group);group.Color="#D84AFF";var newIcon=Native.IconResource(group);
            Require(oldIcon!=newIcon,"Changing group artwork must provide a new icon resource.");
            GroupShortcuts.RefreshIcons(new[]{group},root);
            foreach(var path in new[]{pinnedLink,implicitLink}) {
                shortcut=((dynamic)shell).CreateShortcut(path);
                Require(string.Equals((string)((dynamic)shortcut).IconLocation,newIcon,StringComparison.OrdinalIgnoreCase)&&Native.ShortcutAppId(path)==Native.AppId(group),"Pinned and implicit shortcuts must follow the group's current icon without changing its identity.");
                Marshal.ReleaseComObject(shortcut);shortcut=null;
            }
            Require(untouched.SequenceEqual(File.ReadAllBytes(unrelatedLink))&&otherCopy.SequenceEqual(File.ReadAllBytes(otherCopyLink)),"Other apps and other installations must keep their shortcuts untouched.");
        } finally {if(shortcut!=null)Marshal.ReleaseComObject(shortcut);if(shell!=null)Marshal.ReleaseComObject(shell);}
    }
    [StructLayout(LayoutKind.Sequential)] struct TestBitmapHeader {
        public uint Size;public int Width,Height;public ushort Planes,BitCount;
        public uint Compression,SizeImage;public int XPelsPerMeter,YPelsPerMeter;public uint ColorsUsed,ColorsImportant;
    }
    [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr dc,ref TestBitmapHeader info,uint usage,out IntPtr bits,IntPtr section,uint offset);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr bitmap);
    static void VerifyIconBitmaps() {
        byte[] top={0,0,255,255,0,64,0,128},bottom={0,0,0,0,255,0,0,255};
        foreach(int height in new[]{2,-2}) {
            var header=new TestBitmapHeader {Size=(uint)Marshal.SizeOf<TestBitmapHeader>(),Width=2,Height=height,Planes=1,BitCount=32};
            var handle=CreateDIBSection(IntPtr.Zero,ref header,0,out var bits,IntPtr.Zero,0);
            Require(handle!=IntPtr.Zero,"The test icon bitmap must be created.");
            try {
                Marshal.Copy(height>0?bottom:top,0,bits,8);Marshal.Copy(height>0?top:bottom,0,IntPtr.Add(bits,8),8);
                using(var icon=Native.CopyShellBitmap(handle)) {
                    Require(icon.GetPixel(0,0).ToArgb()==Color.Red.ToArgb()&&icon.GetPixel(1,1).ToArgb()==Color.Blue.ToArgb(),$"Icon rows must retain their orientation (height {height}, top-left {icon.GetPixel(0,0).ToArgb():X8}, bottom-right {icon.GetPixel(1,1).ToArgb():X8}).");
                    Require(icon.GetPixel(0,1).A==0&&icon.GetPixel(1,0).A==128&&Math.Abs(icon.GetPixel(1,0).G-128)<=1,"Icon transparency and premultiplied colors must be preserved.");
                }
                // Older 32-bit bitmaps use zero alpha for opaque artwork.
                byte[] opaque={0,0,255,0,0,255,0,0,255,0,0,0,255,255,255,0};Marshal.Copy(opaque,0,bits,opaque.Length);
                using var legacy=Native.CopyShellBitmap(handle);
                Require(legacy.GetPixel(0,0).A==255&&legacy.GetPixel(1,1).A==255,"Bitmaps without an alpha channel must remain visible.");
            } finally { DeleteObject(handle); }
        }
    }
    static void VerifyRegisteredAppIcon() {
        object? shell=null,folder=null,items=null;bool foundId=false;
        try {
            shell=Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!);
            folder=((dynamic)shell!).NameSpace("shell:AppsFolder");items=((dynamic)folder!).Items();
            int count=((dynamic)items!).Count;
            for(int i=0;i<count;i++) {
                object item=((dynamic)items).Item(i);
                try {
                    string? id=((dynamic)item).ExtendedProperty("System.AppUserModel.ID") as string;
                    if(string.IsNullOrWhiteSpace(id))continue;
                    foundId=true;
                    using var icon=Native.GetIcon("shell:AppsFolder\\"+id);
                    if(icon!=null)return;
                } finally { Marshal.ReleaseComObject(item); }
            }
            Require(!foundId,"Registered Windows apps must resolve through their shell:AppsFolder address.");
            Console.WriteLine("SKIP: no registered app IDs are available on this computer.");
        } finally {
            if(items!=null)Marshal.ReleaseComObject(items);if(folder!=null)Marshal.ReleaseComObject(folder);if(shell!=null)Marshal.ReleaseComObject(shell);
        }
    }
    static void VerifyRunningAppIdentities() {
        var desktop=new RunningApps.Identity("",@"C:\Apps\Example\app.exe","","",false);
        var open=new RunningApps.Window(@"c:\apps\example\APP.EXE","","",Array.Empty<string>(),"MainWindow");
        Require(new RunningApps.Snapshot(new[]{open}).IsRunning(desktop),"An open app must match its executable without case sensitivity.");
        Require(!new RunningApps.Snapshot(Array.Empty<RunningApps.Window>()).IsRunning(desktop),"A closed app must not be marked running.");
        Require(!RunningApps.Matches(desktop,open with {Executable=@"C:\Other\app.exe"}),"An unrelated executable with the same name must not be marked running.");
        var optionalId=RunningApps.Resolve(new AppEntry {Target=desktop.Executable,AppId=null!});
        Require(RunningApps.Matches(optionalId,open),"An optional null app identity must still allow executable matching.");
        var packaged=new RunningApps.Identity("Example_123!App","","","",false);
        Require(RunningApps.Matches(packaged,open with {ProcessAppId="Example_123!App"}),"Store apps must match their process application identity.");
        Require(RunningApps.Matches(packaged,open with {AppId="Example_123!App"}),"Hosted Store windows must match their window application identity.");
        Require(!RunningApps.Matches(packaged,open with {ProcessAppId="Other_123!App"}),"Other Store apps must not share a running marker.");
        var identified=desktop with {AppId="Specific.WebApp"};
        Require(!RunningApps.Matches(identified,open with {AppId="Other.WebApp"}),"Different app identities using the same executable must stay distinct.");
        var renderer=open with {Executable=@"C:\Apps\Example\bin\renderer.exe",Ancestors=new[]{desktop.Executable}};
        Require(RunningApps.Matches(desktop,renderer),"A renderer window must match its app's parent executable in the same installation.");
        Require(!RunningApps.Matches(desktop,renderer with {Executable=@"C:\Other\renderer.exe"}),"A shared parent must not mark unrelated installations as running.");
        var explorer=new RunningApps.Identity("",Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"explorer.exe"),"","",false);
        Require(!RunningApps.Matches(explorer,open with {Executable=explorer.Executable,ClassName="Progman"}),"The Explorer desktop must not count as an open File Explorer window.");
        Require(RunningApps.Matches(explorer,open with {Executable=explorer.Executable,ClassName="CabinetWClass"}),"File Explorer folder windows must count as running.");
    }
    static void VerifyGroupArtwork(string directory) {
        var source=Path.Combine(directory,"group-artwork.png");
        using(var bitmap=new Bitmap(128,64)) {
            using(var graphics=Graphics.FromImage(bitmap)){graphics.Clear(Color.Transparent);graphics.FillRectangle(Brushes.Red,32,16,32,32);graphics.FillRectangle(Brushes.Blue,64,16,32,32);}
            bitmap.Save(source,ImageFormat.Png);
        }
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);Application.EnableVisualStyles();
        var original=new Config {Groups=new(){new Group {Id="artwork",Name="Grupo de prueba"}}};
        using var editor=new Editor(original);editor.VerifyAppearance(source);
        var group=editor.Result.Groups.Single();var imported=group.ResolvedIconPath;
        try {
            Require(!original.Groups.Single().UseCustomIcon&&original.Groups.Single().IconPath=="","Editing appearance must leave the original configuration untouched until saved.");
            File.Delete(source);
            var path=Path.Combine(directory,"artwork.json");Program.Save(editor.Result,path);var restored=Program.Load(path).Groups.Single();
            Require(restored.UseCustomIcon&&restored.IconPath==group.IconPath&&restored.IconName==Path.GetFileName(source),"The selected icon mode, displayed filename and imported file must survive a reload and deletion of the source image.");
            using var icon=Native.GroupIcon(restored);using var artwork=icon.ToBitmap();
            Require(artwork.GetPixel(0,0).A==0&&artwork.GetPixel(16,32).R>240&&artwork.GetPixel(48,32).B>240,"Imported artwork must keep transparency, proportions and colors without the generated symbol background.");
            Require(artwork.GetPixel(2,32).R>240&&artwork.GetPixel(61,32).B>240,"Transparent source margins must be removed so custom group artwork fills the available width.");
            var customPath=Native.IconPath(restored);restored.UseCustomIcon=false;
            Require(Native.IconPath(restored)!=customPath,"Switching appearance must change the exported taskbar icon identity.");
            restored.UseCustomIcon=true;
            foreach(int dpi in new[]{96,144,192}) {
                int height=dpi/2,size=dpi/4;using var taskbar=new Bitmap(240,height,PixelFormat.Format32bppArgb);
                using(var graphics=Graphics.FromImage(taskbar)){graphics.Clear(Color.FromArgb(28,31,47));graphics.DrawImage(artwork,100,(height-size)/2,size,size);}
                var data=taskbar.LockBits(new Rectangle(Point.Empty,taskbar.Size),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
                var pixels=new byte[data.Stride*height];try{Marshal.Copy(data.Scan0,pixels,0,pixels.Length);}finally{taskbar.UnlockBits(data);}
                var matches=NativeTaskbar.MatchCustomIcon(pixels,taskbar.Width,height,data.Stride,artwork,dpi);
                Require(matches.Count==1&&matches[0].Bounds.Contains(100+size/2,height/2),"Custom artwork must be found in the taskbar at "+dpi+" DPI.");
                Array.Fill(pixels,(byte)30);
                Require(NativeTaskbar.MatchCustomIcon(pixels,taskbar.Width,height,data.Stride,artwork,dpi).Count==0,"An unrelated taskbar must not match a custom icon.");
            }
            var saved=File.ReadAllText(path);File.Delete(imported);bool rejected=false;
            try {Program.Save(editor.Result,path);}catch {rejected=true;}
            Require(rejected&&File.ReadAllText(path)==saved,"Missing icon artwork must not overwrite a saved configuration.");
            var invalid=Path.Combine(directory,"invalid.png");File.WriteAllText(invalid,"not an image");
            bool invalidRejected=false;try{_=GroupArtwork.Import(invalid);}catch{invalidRejected=true;}
            Require(invalidRejected,"Invalid image files must be rejected before assignment.");
        } finally {if(File.Exists(imported))File.Delete(imported);}
    }
    static void VerifyCustomIconSizes() {
        using var artwork=CustomTestArtwork();
        using var icon=GroupArtwork.ToIcon(artwork);
        foreach(int dpi in new[]{96,144,192})foreach(int size in new[]{16,20,24,28,32,36,40,48}) {
            int height=Math.Max(dpi/2,size+8);using var taskbar=new Bitmap(240,height,PixelFormat.Format32bppArgb);
            // Use the native icon renderer used by Windows, including its resampling.
            using(var graphics=Graphics.FromImage(taskbar)){graphics.Clear(Color.FromArgb(28,31,47));graphics.DrawIcon(icon,new Rectangle(100,(height-size)/2,size,size));}
            var data=taskbar.LockBits(new Rectangle(Point.Empty,taskbar.Size),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
            var pixels=new byte[data.Stride*height];try{Marshal.Copy(data.Scan0,pixels,0,pixels.Length);}finally{taskbar.UnlockBits(data);}
            var matches=NativeTaskbar.MatchCustomIcon(pixels,taskbar.Width,height,data.Stride,artwork,dpi);
            Require(matches.Count==1&&matches[0].Bounds.Contains(100+size/2,height/2),"Native custom icons must be detected at "+size+" pixels with taskbar DPI "+dpi+".");
        }
        using var other=new Bitmap(64,64);
        using(var graphics=Graphics.FromImage(other)) {
            using var fill=new System.Drawing.Drawing2D.LinearGradientBrush(new Rectangle(0,0,64,64),Color.Red,Color.DarkRed,45);
            graphics.FillEllipse(fill,1,1,62,62);using var pen=new Pen(Color.White,7);graphics.DrawLine(pen,18,18,46,46);graphics.DrawLine(pen,18,46,46,18);
        }
        using var unrelated=new Bitmap(240,48,PixelFormat.Format32bppArgb);
        using(var graphics=Graphics.FromImage(unrelated)){graphics.Clear(Color.FromArgb(28,31,47));graphics.DrawImage(other,100,12,24,24);}
        var unrelatedData=unrelated.LockBits(new Rectangle(Point.Empty,unrelated.Size),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
        var unrelatedPixels=new byte[unrelatedData.Stride*unrelated.Height];try{Marshal.Copy(unrelatedData.Scan0,unrelatedPixels,0,unrelatedPixels.Length);}finally{unrelated.UnlockBits(unrelatedData);}
        Require(NativeTaskbar.MatchCustomIcon(unrelatedPixels,unrelated.Width,unrelated.Height,unrelatedData.Stride,artwork,96).Count==0,"Other applications with the same icon colors must retain their Windows preview.");
    }
    static Bitmap CustomTestArtwork() {
        var artwork=new Bitmap(64,64);
        using var graphics=Graphics.FromImage(artwork);graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var fill=new System.Drawing.Drawing2D.LinearGradientBrush(new Rectangle(0,0,64,64),Color.Red,Color.DarkRed,45);
        graphics.FillEllipse(fill,1,1,62,62);graphics.FillRectangle(Brushes.White,15,25,34,23);
        using var pen=new Pen(Color.White,5);graphics.DrawRectangle(pen,25,17,14,10);
        return artwork;
    }
    static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
