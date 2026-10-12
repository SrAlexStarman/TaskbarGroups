using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace TaskbarGroups;

static class RunningApps {
    internal sealed record Identity(string AppId,string Executable,string Product,string Company,bool Launcher);
    internal sealed record Window(string Executable,string AppId,string ProcessAppId,string[] Ancestors,string ClassName);
    internal sealed class Snapshot {
        readonly Window[] windows;
        public Snapshot(IEnumerable<Window> windows){this.windows=windows.ToArray();}
        public bool IsRunning(Identity app)=>windows.Any(window=>Matches(app,window));
    }
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string,(string Product,string Company)> products=new(StringComparer.OrdinalIgnoreCase);
    delegate bool EnumCallback(IntPtr window,IntPtr parameter);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumCallback callback,IntPtr parameter);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent,EnumCallback callback,IntPtr parameter);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr window,uint command);
    [DllImport("user32.dll",EntryPoint="GetWindowLongW")] static extern int GetWindowLong(IntPtr window,int index);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr window,StringBuilder name,int size);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr window,int attribute,out int value,int size);
    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access,bool inherit,uint process);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr process,uint flags,StringBuilder path,ref uint size);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern int GetApplicationUserModelId(IntPtr process,ref uint size,StringBuilder? id);
    [DllImport("kernel32.dll")] static extern IntPtr CreateToolhelp32Snapshot(uint flags,uint process);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern bool Process32First(IntPtr snapshot,ref ProcessEntry entry);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern bool Process32Next(IntPtr snapshot,ref ProcessEntry entry);
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct ProcessEntry {
        public uint Size,Usage,Id;public UIntPtr Heap;public uint Module,Threads,Parent;public int Priority;public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)] public string Name;
    }

    internal static string Key(AppEntry app)=>app.ResolvedTarget+"\0"+app.Arguments+"\0"+app.AppId;
    internal static Identity Resolve(AppEntry app) {
        string target=Environment.ExpandEnvironmentVariables(app.ResolvedTarget),id=app.AppId??"";
        object? shell=null,shortcut=null,folder=null,item=null;
        try {
            if(target.StartsWith("shell:AppsFolder\\",StringComparison.OrdinalIgnoreCase)) {
                id=target.Substring("shell:AppsFolder\\".Length);target="";
                // Packaged apps have a process identity. Desktop AppsFolder
                // entries can additionally expose their executable target.
                if(!id.Contains('!')) {
                    shell=Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!);
                    folder=((dynamic)shell!).NameSpace("shell:AppsFolder");item=((dynamic)folder!).ParseName(id);
                    target=((dynamic)item!).ExtendedProperty("System.Link.TargetParsingPath") as string??"";
                }
            } else if(Path.GetExtension(target).Equals(".lnk",StringComparison.OrdinalIgnoreCase)&&File.Exists(target)) {
                if(string.IsNullOrWhiteSpace(id))id=Native.ShortcutAppId(target);
                shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);shortcut=((dynamic)shell!).CreateShortcut(target);
                target=Environment.ExpandEnvironmentVariables((string)((dynamic)shortcut!).TargetPath);
                if(target.StartsWith("shell:AppsFolder\\",StringComparison.OrdinalIgnoreCase)){id=target.Substring("shell:AppsFolder\\".Length);target="";}
            }
            if(!Path.IsPathRooted(target))target=FindExecutable(target);
            if(!Path.GetExtension(target).Equals(".exe",StringComparison.OrdinalIgnoreCase))target="";
        } catch {target="";}
        finally {foreach(var instance in new[]{item,folder,shortcut,shell})if(instance!=null)Marshal.ReleaseComObject(instance);}
        var product=Product(target);
        bool launcher=Path.GetFileNameWithoutExtension(target).Contains("launcher",StringComparison.OrdinalIgnoreCase);
        return new Identity(id,target,product.Product,product.Company,launcher);
    }
    static string FindExecutable(string target) {
        if(string.IsNullOrWhiteSpace(target)||target.Contains(':')||target.IndexOfAny(Path.GetInvalidPathChars())>=0)return "";
        foreach(var directory in new[]{Program.Base,Environment.GetFolderPath(Environment.SpecialFolder.Windows),Environment.SystemDirectory}
            .Concat((Environment.GetEnvironmentVariable("PATH")??"").Split(Path.PathSeparator))) {
            if(string.IsNullOrWhiteSpace(directory))continue;
            try {var candidate=Path.Combine(directory.Trim('"'),target);if(File.Exists(candidate))return Path.GetFullPath(candidate);}catch{}
        }
        return "";
    }
    static (string Product,string Company) Product(string executable)=>products.GetOrAdd(executable,path=>{
        try {if(File.Exists(path)){var version=FileVersionInfo.GetVersionInfo(path);return (version.ProductName??"",version.CompanyName??"");}}catch{}
        return ("","");
    });
    internal static bool Matches(Identity app,Window window) {
        bool Same(string first,string second)=>!string.IsNullOrWhiteSpace(first)&&first.Equals(second,StringComparison.OrdinalIgnoreCase);
        if(Same(app.AppId,window.AppId)||Same(app.AppId,window.ProcessAppId))return true;
        if(app.AppId.Length>0&&window.AppId.Length>0)return false;
        if(app.Executable.Length==0)return false;
        if(Path.GetFileName(app.Executable).Equals("explorer.exe",StringComparison.OrdinalIgnoreCase)
            &&window.ClassName is not("CabinetWClass" or "ExploreWClass"))return false;
        if(Same(app.Executable,window.Executable))return true;
        var directory=Path.GetDirectoryName(app.Executable)??"";
        // A renderer can own the window while its parent owns the app. Require
        // the same installation tree so shared launchers cannot light up
        // unrelated apps (for example, every app started by Explorer).
        bool scoped=directory.Length>0&&!new[]{Environment.GetFolderPath(Environment.SpecialFolder.Windows),Environment.SystemDirectory,
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)}.Any(root=>Same(directory,root));
        if(scoped&&window.Executable.StartsWith(directory+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)
            &&window.Ancestors.Any(ancestor=>Same(app.Executable,ancestor)))return true;
        // Some bootstrap launchers exit after starting a sibling executable.
        // Match its product and company metadata within that installation directory.
        if(app.Launcher&&scoped&&Same(directory,Path.GetDirectoryName(window.Executable)??"")) {
            var product=Product(window.Executable);return Same(app.Product,product.Product)&&Same(app.Company,product.Company);
        }
        return false;
    }
    internal static Snapshot Capture() {
        var parents=Parents();var processes=new Dictionary<uint,(string Executable,string AppId)>();var windows=new List<Window>();
        (string Executable,string AppId) Process(uint id) {
            if(processes.TryGetValue(id,out var known))return known;
            string executable="",appId="";var handle=OpenProcess(0x1000,false,id);
            if(handle!=IntPtr.Zero)try {
                var path=new StringBuilder(32768);uint size=(uint)path.Capacity;
                if(QueryFullProcessImageName(handle,0,path,ref size))executable=path.ToString();
                uint length=0;if(GetApplicationUserModelId(handle,ref length,null)==122&&length>0&&length<32768) {
                    var text=new StringBuilder((int)length);if(GetApplicationUserModelId(handle,ref length,text)==0)appId=text.ToString();
                }
            }finally{CloseHandle(handle);}
            return processes[id]=(executable,appId);
        }
        void Add(uint id,string appId,string className) {
            var process=Process(id);var ancestors=new List<string>();var seen=new HashSet<uint>{id};uint current=id;
            for(int depth=0;depth<8&&parents.TryGetValue(current,out current)&&seen.Add(current);depth++) {
                var parent=Process(current);if(parent.Executable.Length>0)ancestors.Add(parent.Executable);
            }
            windows.Add(new Window(process.Executable,appId,process.AppId,ancestors.ToArray(),className));
        }
        EnumWindows((window,_)=>{
            if(!Native.IsWindowVisible(window))return true;
            int style=GetWindowLong(window,-20);bool appWindow=(style&0x40000)!=0;
            if(!appWindow&&((style&0x80)!=0||GetWindow(window,4)!=IntPtr.Zero))return true;
            if(DwmGetWindowAttribute(window,14,out int cloaked,4)==0&&cloaked!=0)return true;
            var name=new StringBuilder(256);GetClassName(window,name,name.Capacity);string className=name.ToString();
            if(className is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "Progman" or "WorkerW")return true;
            GetWindowThreadProcessId(window,out uint id);string appId=Native.WindowAppId(window);Add(id,appId,className);
            if(className=="ApplicationFrameWindow") {
                var seen=new HashSet<uint>{id};EnumChildWindows(window,(child,_)=>{
                    GetWindowThreadProcessId(child,out uint childId);if(childId!=0&&seen.Add(childId))Add(childId,appId,className);return true;
                },IntPtr.Zero);
            }
            return true;
        },IntPtr.Zero);
        return new Snapshot(windows);
    }
    static Dictionary<uint,uint> Parents() {
        var result=new Dictionary<uint,uint>();var snapshot=CreateToolhelp32Snapshot(2,0);
        if(snapshot==IntPtr.Zero||snapshot==new IntPtr(-1))return result;
        try {var entry=new ProcessEntry {Size=(uint)Marshal.SizeOf<ProcessEntry>(),Name=""};
            if(Process32First(snapshot,ref entry))do{result[entry.Id]=entry.Parent;}while(Process32Next(snapshot,ref entry));
        }finally{CloseHandle(snapshot);}return result;
    }
}
