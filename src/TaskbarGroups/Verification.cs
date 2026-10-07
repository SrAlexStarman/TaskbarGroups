using System;
using System.Drawing;
using System.IO;
using System.Linq;

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
            Console.WriteLine("PASS: clean startup, persistence, backup, validation and preview scope.");return 0;
        } catch(Exception error) {Console.Error.WriteLine(error);return 1;}
        finally {foreach(var file in Directory.EnumerateFiles(directory))File.Delete(file);Directory.Delete(directory);}
    }
    static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
