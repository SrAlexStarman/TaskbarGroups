using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace TaskbarGroups;

// Hide Explorer's thumbnail flyout only while the pointer belongs to one of
// our groups. No registry settings, Explorer injection, or global preview change.
sealed class NativePreviewBlocker : IDisposable {
    readonly Func<Point,bool> allowed;
    readonly Func<Rectangle?> panel;
    readonly Func<Point,Rectangle?> button;
    readonly Func<string[]> groupTitles;
    readonly Dictionary<uint,string> threadNames=new();
    readonly WinEvent callback;
    IntPtr hook,positionHook,menuHook;
    bool menuOpen,rightClickPending;
    DateTime rightClickTime;
    DateTime menuTime;
    Rectangle? contextAnchor;
    uint explorerId;
    DateTime explorerChecked;
    DateTime lastFailure;
    readonly HashSet<IntPtr> pendingHidden=new();
    public bool HookInstalled=>hook!=IntPtr.Zero;
    public int HiddenCount {get;private set;}
    delegate void WinEvent(IntPtr hook,uint evt,IntPtr hwnd,int objectId,int childId,uint thread,uint time);
    delegate bool EnumCallback(IntPtr hwnd,IntPtr data);
    [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min,uint max,IntPtr module,WinEvent callback,uint process,uint thread,uint flags);
    [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumCallback callback,IntPtr data);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent,EnumCallback callback,IntPtr data);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent,IntPtr after,string? cls,string? title);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd,StringBuilder text,int max);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd,StringBuilder name,int max);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint process);
    [DllImport("kernel32.dll")] static extern IntPtr OpenThread(uint access,bool inherit,uint thread);
    [DllImport("kernel32.dll")] static extern int GetThreadDescription(IntPtr thread,out IntPtr description);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr handle);
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr hwnd,uint flags);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
    [StructLayout(LayoutKind.Sequential)]struct GuiThreadInfo {public uint Size,Flags;public IntPtr Active,Focus,Capture,MenuOwner,MoveSize,Caret;public Native.RECT CaretRect;}
    [DllImport("user32.dll")]static extern bool GetGUIThreadInfo(uint thread,ref GuiThreadInfo info);
    [DllImport("user32.dll",SetLastError=true)] public static extern bool ShowWindowAsync(IntPtr hwnd,int command);
    public NativePreviewBlocker(Func<Point,bool> allowed,Func<Rectangle?> panel,Func<Point,Rectangle?>? button=null,Func<string[]>? groupTitles=null){this.allowed=allowed;this.panel=panel;this.button=button??(_=>null);this.groupTitles=groupTitles??(()=>Array.Empty<string>());callback=OnEvent;hook=SetWinEventHook(0x8002,0x8003,IntPtr.Zero,callback,0,0,2);positionHook=SetWinEventHook(0x800B,0x800B,IntPtr.Zero,callback,0,0,2);menuHook=SetWinEventHook(4,7,IntPtr.Zero,callback,0,0,2);}
    internal static bool MatchesPreview(string cls,bool explorer,Rectangle bounds,Rectangle? panel,bool menu,bool taskbarHost=false,bool ownPreview=false){
        if(!explorer||menu)return false;
        if(cls=="TaskListThumbnailWnd")return true;
        if(!panel.HasValue||!bounds.IntersectsWith(panel.Value))return false;
        // The modern thumbnail lives in a monitor-sized transparent host, NOT
        // in a thumbnail-sized HWND. Verify the Taskbar thread and group scope.
        if(cls=="XamlExplorerHostIslandWindow")return taskbarHost&&ownPreview;
        return cls=="Xaml_WindowedPopupClass"&&bounds.Width>20&&bounds.Height>20&&bounds.Width<1200&&bounds.Height<900;
    }
    bool ExplorerWindow(IntPtr hwnd){if((DateTime.UtcNow-explorerChecked).TotalSeconds>1){var bar=Native.Taskbars().FirstOrDefault();GetWindowThreadProcessId(bar.Handle,out explorerId);explorerChecked=DateTime.UtcNow;}GetWindowThreadProcessId(hwnd,out uint process);return explorerId!=0&&process==explorerId;}
    public bool ContextMenuActive=>PauseForMenu();
    bool PauseForMenu(){
        var point=System.Windows.Forms.Cursor.Position;
        if((GetAsyncKeyState(2)&0x8000)!=0&&allowed(point)&&button(point) is Rectangle anchor){rightClickPending=true;rightClickTime=DateTime.UtcNow;contextAnchor=anchor;}
        // A thumbnail is also a XAML popup, so its mere presence must never
        // keep right-click suspension latched indefinitely.
        if(rightClickPending&&(DateTime.UtcNow-rightClickTime).TotalMilliseconds>800&&contextAnchor?.Contains(point)!=true)rightClickPending=false;
        if(menuOpen&&(DateTime.UtcNow-menuTime).TotalSeconds>1&&!HasNativeMenu()&&contextAnchor?.Contains(point)!=true)menuOpen=false;
        return menuOpen||rightClickPending;
    }
    bool HasNativeMenu(){bool open=false;EnumWindows((h,d)=>{var cls=new StringBuilder(128);GetClassName(h,cls,cls.Capacity);if(cls.ToString()=="#32768"&&Native.IsWindowVisible(h)&&ExplorerWindow(h)){open=true;return false;}return true;},IntPtr.Zero);return open;}
    bool IsExplorerThumbnail(IntPtr hwnd){
        if(hwnd==IntPtr.Zero||!Native.IsWindowVisible(hwnd))return false;var name=new StringBuilder(128);GetClassName(hwnd,name,name.Capacity);if(name.ToString() is not("TaskListThumbnailWnd" or "Xaml_WindowedPopupClass" or "XamlExplorerHostIslandWindow")||!ExplorerWindow(hwnd))return false;
        uint thread=GetWindowThreadProcessId(hwnd,out uint process);var info=new GuiThreadInfo{Size=(uint)Marshal.SizeOf<GuiThreadInfo>()};bool menu=menuOpen&&GetGUIThreadInfo(thread,ref info)&&(info.Flags&0x1C)!=0;
        bool taskbarHost=false,ownPreview=false;
        if(name.ToString()=="XamlExplorerHostIslandWindow"){
            if(!threadNames.TryGetValue(thread,out var description)){
                description="";var handle=OpenThread(0x800,false,thread);if(handle!=IntPtr.Zero)try{if(GetThreadDescription(handle,out var ptr)>=0){description=Marshal.PtrToStringUni(ptr)??"";LocalFree(ptr);}}finally{CloseHandle(handle);}threadNames[thread]=description;
            }
            taskbarHost=description=="Taskbar";if(!taskbarHost)return false;
            // The host's identity was verified with UIA during diagnosis.
            // Avoid querying Explorer's UIA provider from our UI thread while
            // Explorer can synchronously request our thumbnail on that thread.
            // Runtime scope is the pointer over our button/panel and a visible
            // launcher. Task View and other shell threads remain excluded.
            ownPreview=groupTitles().Length>0&&allowed(System.Windows.Forms.Cursor.Position)&&panel().HasValue;
        }
        Native.GetWindowRect(hwnd,out var bounds);return MatchesPreview(name.ToString(),true,bounds.Rectangle,panel(),menu,taskbarHost,ownPreview);
    }
    void Hide(IntPtr hwnd){if(IsExplorerThumbnail(hwnd)&&Native.IsWindowVisible(hwnd)){bool accepted=ShowWindowAsync(hwnd,0);if(accepted){HiddenCount++;pendingHidden.Add(hwnd);}else if((DateTime.UtcNow-lastFailure).TotalSeconds>=5){Program.Log("Windows rechazó ocultar la miniatura: "+Marshal.GetLastWin32Error());lastFailure=DateTime.UtcNow;}}}
    void ConfirmHidden(){foreach(var hwnd in pendingHidden.ToArray())if(!Native.IsWindowVisible(hwnd)){var cls=new StringBuilder(128);GetClassName(hwnd,cls,cls.Capacity);Program.Log("Miniatura de Windows oculta: "+cls);pendingHidden.Remove(hwnd);}}
    void OnEvent(IntPtr unused,uint evt,IntPtr hwnd,int objectId,int childId,uint thread,uint time){try{
        if(evt is 4 or 6){if(ExplorerWindow(hwnd)&&((DateTime.UtcNow-rightClickTime).TotalSeconds<1||HasNativeMenu())){menuOpen=true;menuTime=DateTime.UtcNow;}return;}if(evt is 5 or 7){menuOpen=false;rightClickPending=false;return;}
        if(evt==0x8003||PauseForMenu()||!allowed(System.Windows.Forms.Cursor.Position))return;
        Hide(hwnd);var root=GetAncestor(hwnd,2);if(root!=IntPtr.Zero&&root!=hwnd)Hide(root);
    }catch(Exception e){Program.Log("Miniatura de grupo: "+e.Message);}}
    public void SuppressAt(Point point){ConfirmHidden();if(!allowed(point)||PauseForMenu())return;
        // EnumWindows omits some modern shell hosts. FindWindowEx also reaches
        // the desktop children used by the current Windows 11 taskbar.
        foreach(var cls in new[]{"XamlExplorerHostIslandWindow","Xaml_WindowedPopupClass","TaskListThumbnailWnd"}){
            IntPtr h=IntPtr.Zero;var seen=new HashSet<IntPtr>();while(seen.Count<64&&(h=FindWindowEx(IntPtr.Zero,h,cls,null))!=IntPtr.Zero&&seen.Add(h))Hide(h);
        }
    }
    public void Dispose(){foreach(var handle in new[]{hook,positionHook,menuHook})if(handle!=IntPtr.Zero)UnhookWinEvent(handle);hook=positionHook=menuHook=IntPtr.Zero;GC.KeepAlive(callback);}
    internal static void Diagnose(){
        var lines=new List<string>();var until=DateTime.UtcNow.AddSeconds(65);var lastCapture=DateTime.MinValue;int index=0;
        var inventory=new List<string>();EnumWindows((h,d)=>{var cls=new StringBuilder(256);var title=new StringBuilder(256);GetClassName(h,cls,cls.Capacity);GetWindowText(h,title,title.Capacity);GetWindowThreadProcessId(h,out uint pid);string process="";try{process=System.Diagnostics.Process.GetProcessById((int)pid).ProcessName;}catch{}if(process=="explorer"||cls.ToString().Contains("Popup")||cls.ToString().Contains("Thumbnail")){Native.GetWindowRect(h,out var r);inventory.Add("hwnd="+h+" process="+process+" cls="+cls+" title="+title+" rect="+r.Rectangle+" visible="+Native.IsWindowVisible(h));}return true;},IntPtr.Zero);System.IO.File.WriteAllLines(System.IO.Path.Combine(Program.Base,"hover-inventory.txt"),inventory);
        while(DateTime.UtcNow<until){
            var point=System.Windows.Forms.Cursor.Position;var bars=Native.Taskbars();var bar=bars.FirstOrDefault(b=>b.Bounds.Contains(point));
            if(bar.Handle!=IntPtr.Zero){
                lines.Add(DateTime.Now.ToString("O")+" Pointer="+point);
                var region=new Rectangle(point.X-450,bar.Bounds.Top-500,900,550);
                void Inspect(IntPtr h){Native.GetWindowRect(h,out var rect);
                    var cls=new StringBuilder(256);var title=new StringBuilder(256);GetClassName(h,cls,cls.Capacity);GetWindowText(h,title,title.Capacity);uint tid=GetWindowThreadProcessId(h,out uint pid);var info=new GuiThreadInfo{Size=(uint)Marshal.SizeOf<GuiThreadInfo>()};GetGUIThreadInfo(tid,ref info);
                    GetWindowThreadProcessId(bar.Handle,out uint explorer);if(pid!=explorer)return;
                    lines.Add("hwnd="+h+" root="+GetAncestor(h,2)+" pid="+pid+" cls="+cls+" title="+title+" rect="+rect.Rectangle+" gui="+info.Flags+" visible="+Native.IsWindowVisible(h));
                }
                EnumWindows((h,d)=>{Inspect(h);GetWindowThreadProcessId(h,out uint pid);GetWindowThreadProcessId(bar.Handle,out uint explorer);if(pid==explorer)EnumChildWindows(h,(child,v)=>{Inspect(child);return true;},IntPtr.Zero);return true;},IntPtr.Zero);
                IntPtr sibling=IntPtr.Zero;var seen=new HashSet<IntPtr>();while((sibling=FindWindowEx(IntPtr.Zero,sibling,null,null))!=IntPtr.Zero&&seen.Add(sibling)){Inspect(sibling);GetWindowThreadProcessId(sibling,out uint pid);GetWindowThreadProcessId(bar.Handle,out uint explorer);if(pid==explorer)EnumChildWindows(sibling,(child,v)=>{Inspect(child);return true;},IntPtr.Zero);}
                if((DateTime.UtcNow-lastCapture).TotalSeconds>3){var screen=System.Windows.Forms.Screen.FromPoint(point).Bounds;var crop=Rectangle.Intersect(screen,region);using var bmp=new Bitmap(crop.Width,crop.Height);using(var g=Graphics.FromImage(bmp))g.CopyFromScreen(crop.Location,Point.Empty,crop.Size);bmp.Save(System.IO.Path.Combine(Program.Base,"hover-diagnostic-"+index+++".png"));lastCapture=DateTime.UtcNow;}
                System.IO.File.WriteAllLines(System.IO.Path.Combine(Program.Base,"hover-diagnostic.txt"),lines);
            }
            System.Threading.Thread.Sleep(200);
        }
    }
    internal static void InspectPreview(){
        var lines=new List<string>();using var uia=new NativeUia();IntPtr h=IntPtr.Zero;var seen=new HashSet<IntPtr>();
        while((h=FindWindowEx(IntPtr.Zero,h,"XamlExplorerHostIslandWindow",null))!=IntPtr.Zero&&seen.Add(h)){
            uint thread=GetWindowThreadProcessId(h,out uint pid);Native.GetWindowRect(h,out var r);lines.Add("HWND="+h+" pid="+pid+" rect="+r.Rectangle+" visible="+Native.IsWindowVisible(h));
            var handle=OpenThread(0x800,false,thread);if(handle!=IntPtr.Zero){try{if(GetThreadDescription(handle,out var ptr)>=0){lines.Add("thread="+Marshal.PtrToStringUni(ptr));LocalFree(ptr);}}finally{CloseHandle(handle);}}
            try{foreach(var e in uia.Elements(h))lines.Add("UIA "+e.Type+" | "+e.Name+" | "+e.Bounds);}catch(Exception e){lines.Add(e.Message);}
        }
        System.IO.File.WriteAllLines(System.IO.Path.Combine(Program.Base,"preview-host.txt"),lines);
    }
}
