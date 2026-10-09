using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace TaskbarGroups;

static class Verification {
    internal static Config DesktopConfig()=>new() {
        Groups=new() {new Group {
            Id="selftest",Name="Test group",Symbol="folder",Color="#6EADFF",
            Apps=new() {new AppEntry {
                Name="File Explorer",Target=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"explorer.exe")
            }}
        }}
    };

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
            Require(restored.Groups.Single().Name=="Renamed group"&&restored.HoverDelayMs==340,"Saved edits must survive a reload.");
            Require(Program.Load(path+".bak").Groups.Single().Name=="Test group","Saving must preserve the previous configuration.");
            var saved=File.ReadAllText(path);config.Groups[0].Name="";
            bool rejected=false;try {Program.Save(config,path);}catch {rejected=true;}
            Require(rejected&&File.ReadAllText(path)==saved,"An invalid edit must not overwrite saved groups.");
            var host=new Rectangle(-1920,0,1920,1080);var panel=new Rectangle(-1000,800,320,200);
            Require(NativePreviewBlocker.MatchesPreview("XamlExplorerHostIslandWindow",true,host,panel,false,true,true),"A monitor-sized Taskbar preview host must be recognized.");
            Require(!NativePreviewBlocker.MatchesPreview("XamlExplorerHostIslandWindow",true,host,panel,false,false,true),"Other shell threads must be excluded.");
            Require(!NativePreviewBlocker.MatchesPreview("XamlExplorerHostIslandWindow",true,host,panel,false,true,false),"The pointer must belong to a group.");
            Require(!NativePreviewBlocker.MatchesPreview("XamlExplorerHostIslandWindow",true,host,panel,true,true,true),"Context menus must be excluded.");
            VerifyIconBitmaps();VerifyRegisteredAppIcon();
            Console.WriteLine("PASS: clean startup, persistence, backup, validation, preview scope and app icons.");return 0;
        } catch(Exception error) {Console.Error.WriteLine(error);return 1;}
        finally {foreach(var file in Directory.EnumerateFiles(directory))File.Delete(file);Directory.Delete(directory);}
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
    static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
