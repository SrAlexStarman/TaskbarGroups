using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace TaskbarGroups;

static class Native
{
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; public Rectangle Rectangle => Rectangle.FromLTRB(Left, Top, Right, Bottom); }
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string? title);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback, IntPtr data);
    delegate bool EnumProc(IntPtr h, IntPtr data);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder text, int count);
    [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr icon);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [StructLayout(LayoutKind.Sequential)] struct NativeBitmap { public int Type,Width,Height,Stride; public ushort Planes,BitsPerPixel; public IntPtr Bits; }
    [StructLayout(LayoutKind.Sequential)] struct BitmapHeader { public uint Size; public int Width,Height; public ushort Planes,BitCount; public uint Compression,SizeImage; public int XPelsPerMeter,YPelsPerMeter; public uint ColorsUsed,ColorsImportant; }
    [DllImport("gdi32.dll",EntryPoint="GetObjectW")] static extern int GetBitmapObject(IntPtr bitmap,int size,out NativeBitmap info);
    [DllImport("gdi32.dll",SetLastError=true)] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc,IntPtr obj);
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X,Y;public POINT(Point point){X=point.X;Y=point.Y;} }
    [StructLayout(LayoutKind.Sequential,Pack=1)] struct BlendFunction { public byte Operation,Flags,Opacity,AlphaFormat; }
    [DllImport("user32.dll",SetLastError=true)] static extern bool UpdateLayeredWindow(IntPtr hwnd,IntPtr target,ref POINT position,ref Size size,IntPtr source,ref POINT origin,uint color,ref BlendFunction blend,uint flags);
    [DllImport("user32.dll",SetLastError=true)] static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd,int command);
    [DllImport("user32.dll",EntryPoint="SystemParametersInfoW")] static extern bool SystemParametersInfo(uint action,uint parameter,out int value,uint flags);
    [DllImport("gdi32.dll")] static extern IntPtr CreateRectRgn(int left,int top,int right,int bottom);
    [DllImport("user32.dll",SetLastError=true)] static extern int SetWindowRgn(IntPtr hwnd,IntPtr region,bool redraw);
    [DllImport("gdi32.dll")] static extern int GetDIBits(IntPtr dc,IntPtr bitmap,uint start,uint count,[Out] byte[] pixels,ref BitmapHeader info,uint usage);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);
    [DllImport("dwmapi.dll")] public static extern int DwmInvalidateIconicBitmaps(IntPtr hwnd);
    [DllImport("dwmapi.dll")] static extern int DwmSetIconicThumbnail(IntPtr hwnd,IntPtr bitmap,uint flags);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hwnd,uint message,IntPtr wParam,IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
    public static void SetAlphaFrame(IntPtr hwnd,Bitmap bitmap,Point location) {
        var dc=CreateCompatibleDC(IntPtr.Zero);IntPtr handle=IntPtr.Zero,previous=IntPtr.Zero;
        if(dc==IntPtr.Zero)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        try {
            handle=bitmap.GetHbitmap(Color.FromArgb(0));previous=SelectObject(dc,handle);
            if(previous==IntPtr.Zero||previous==new IntPtr(-1))throw new InvalidOperationException("Windows could not select the popup outline bitmap.");
            var position=new POINT(location);var origin=new POINT(Point.Empty);var size=bitmap.Size;
            var blend=new BlendFunction {Opacity=255,AlphaFormat=1};
            if(!UpdateLayeredWindow(hwnd,IntPtr.Zero,ref position,ref size,dc,ref origin,0,ref blend,2))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        } finally {if(previous!=IntPtr.Zero&&previous!=new IntPtr(-1))SelectObject(dc,previous);if(handle!=IntPtr.Zero)DeleteObject(handle);DeleteDC(dc);}
    }
    public static void ShowAlphaFrame(IntPtr hwnd,IntPtr owner,bool visible) {
        if(visible) {
            // Both surfaces must stay above other apps; raising only the owned
            // outline can leave the ordinary WinForms content behind them.
            // Do not reorder WinForms' hidden owner along with these surfaces.
            SetWindowPos(owner,new IntPtr(-1),0,0,0,0,0x253);
            SetWindowPos(hwnd,new IntPtr(-1),0,0,0,0,0x253);
        } else ShowWindow(hwnd,0);
    }
    public static void MoveAlphaFrame(IntPtr hwnd,Point location)=>SetWindowPos(hwnd,IntPtr.Zero,location.X,location.Y,0,0,0x15);
    public static bool ClientAnimationsEnabled=>!SystemParametersInfo(0x1042,0,out int enabled,0)||enabled!=0;
    public static void SetWindowClip(IntPtr hwnd,Rectangle? clip) {
        var region=clip is Rectangle r?CreateRectRgn(r.Left,r.Top,r.Right,r.Bottom):IntPtr.Zero;
        if(clip.HasValue&&region==IntPtr.Zero)throw new System.ComponentModel.Win32Exception();
        // Windows takes ownership of the region when SetWindowRgn succeeds.
        if(SetWindowRgn(hwnd,region,true)==0){if(region!=IntPtr.Zero)DeleteObject(region);throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());}
    }
    [ComImport,Guid("56FDF342-FD6D-11D0-958A-006097C9A090"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface ITaskbarList {void HrInit();void AddTab(IntPtr hwnd);void DeleteTab(IntPtr hwnd);void ActivateTab(IntPtr hwnd);void SetActiveAlt(IntPtr hwnd);}
    public static void RegisterTaskbarWindow(IntPtr hwnd){object? instance=null;try{instance=Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("56FDF344-FD6D-11D0-958A-006097C9A090"))!);var list=(ITaskbarList)instance!;list.HrInit();list.AddTab(hwnd);}finally{if(instance!=null)Marshal.ReleaseComObject(instance);}}
    public static void EnableNativePreview(IntPtr hwnd){int yes=1;Marshal.ThrowExceptionForHR(DwmSetWindowAttribute(hwnd,7,ref yes,4));Marshal.ThrowExceptionForHR(DwmSetWindowAttribute(hwnd,10,ref yes,4));DwmSetWindowAttribute(hwnd,11,ref yes,4);}
    public static void SetNativePreview(IntPtr hwnd,Bitmap bitmap){var handle=bitmap.GetHbitmap();try{DwmSetIconicThumbnail(hwnd,handle,0);}finally{DeleteObject(handle);}}
    [DllImport("shell32.dll")] static extern int SHGetPropertyStoreForWindow(IntPtr h, ref Guid id, out IPropertyStore store);
    [DllImport("shell32.dll", CharSet=CharSet.Unicode)] static extern int SHGetPropertyStoreFromParsingName(string path, IntPtr ctx, uint flags, ref Guid id, out IPropertyStore store);
    [DllImport("shell32.dll", CharSet=CharSet.Unicode)] static extern int SHCreateItemFromParsingName(string path, IntPtr ctx, ref Guid id, out IShellItemImageFactory factory);
    [DllImport("ole32.dll")] static extern int PropVariantClear(ref Variant value);
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct FileInfo { public IntPtr Icon;public int Index;public uint Attributes;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)]public string DisplayName;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=80)]public string TypeName; }
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)] static extern IntPtr SHGetFileInfo(string path,uint attributes,out FileInfo info,uint size,uint flags);
    [StructLayout(LayoutKind.Sequential)] struct PropertyKey { public Guid Format; public uint Id; public PropertyKey(uint id) { Format=new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"); Id=id; } }
    [StructLayout(LayoutKind.Explicit, Size=24)] struct Variant { [FieldOffset(0)] public ushort Type; [FieldOffset(8)] public IntPtr Ptr; }
    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out Variant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref Variant value);
        [PreserveSig] int Commit();
    }
    [ComImport, Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItemImageFactory { [PreserveSig] int GetImage(Size size, uint flags, out IntPtr bitmap); }

    public static List<(IntPtr Handle, Rectangle Bounds)> Taskbars() {
        var result=new List<(IntPtr,Rectangle)>();
        EnumWindows((h,d)=> { var b=new StringBuilder(128); GetClassName(h,b,128); if (b.ToString() is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") { GetWindowRect(h,out var r); result.Add((h,r.Rectangle)); } return true; },IntPtr.Zero);
        return result;
    }
    static void Set(IPropertyStore store, uint id, string text) {
        var key=new PropertyKey(id); var value=new Variant { Type=31, Ptr=Marshal.StringToCoTaskMemUni(text) };
        try { Marshal.ThrowExceptionForHR(store.SetValue(ref key,ref value)); } finally { PropVariantClear(ref value); }
    }
    public static string AppId(Group group)=>"Personal.TaskbarGroups.NativeV2."+group.Id;
    public static string IconPath(Group group)=>Path.Combine(Program.Base,"icons",group.Id+"-"+Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(group.Name+group.Color+group.Symbol))).Substring(0,8)+".ico");
    public static void SetWindowIdentity(IntPtr h, Group group) {
        var id=typeof(IPropertyStore).GUID; Marshal.ThrowExceptionForHR(SHGetPropertyStoreForWindow(h,ref id,out var store));
        try { Set(store,5,AppId(group)); Set(store,2,"\""+Environment.ProcessPath+"\" --show "+group.Id); Set(store,4,group.Name); Set(store,3,IconPath(group)); } finally { Marshal.ReleaseComObject(store); }
    }
    public static void SetShortcutIdentity(string path, Group group) {
        var id=typeof(IPropertyStore).GUID; Marshal.ThrowExceptionForHR(SHGetPropertyStoreFromParsingName(path,IntPtr.Zero,2,ref id,out var store));
        try { Set(store,5,AppId(group)); Marshal.ThrowExceptionForHR(store.Commit()); } finally { Marshal.ReleaseComObject(store); }
    }
    public static Bitmap? GetIcon(string path) {
        try {if(File.Exists(path)&&Path.GetExtension(path).Equals(".png",StringComparison.OrdinalIgnoreCase)){using var png=Image.FromFile(path);return new Bitmap(png);}
            if(File.Exists(path)&&Path.GetExtension(path).Equals(".lnk",StringComparison.OrdinalIgnoreCase)){
                object? shell=null,shortcut=null;var iconPath=path;
                try{shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);shortcut=((dynamic)shell!).CreateShortcut(path);string location=((dynamic)shortcut!).IconLocation;var comma=location.LastIndexOf(',');if(comma>=0)location=location.Substring(0,comma);location=Environment.ExpandEnvironmentVariables(location.Trim('"'));iconPath=File.Exists(location)?location:(string)((dynamic)shortcut).TargetPath;}finally{if(shortcut!=null)Marshal.ReleaseComObject(shortcut);if(shell!=null)Marshal.ReleaseComObject(shell);}
                SHGetFileInfo(iconPath,0,out var info,(uint)Marshal.SizeOf<FileInfo>(),0x100);if(info.Icon!=IntPtr.Zero)try{return Icon.FromHandle(info.Icon).ToBitmap();}finally{DestroyIcon(info.Icon);}}
        }catch{}
        // Keep the shell: prefix: AppsFolder entries are virtual items, and the
        // bare ::{GUID} form is not accepted by SHCreateItemFromParsingName.
        var id=typeof(IShellItemImageFactory).GUID;
        try { if (SHCreateItemFromParsingName(path,IntPtr.Zero,ref id,out var factory)<0) return null;
            try { if (factory.GetImage(new Size(48,48),4,out var bitmap)<0) return null;
                try { return CopyShellBitmap(bitmap); } finally { DeleteObject(bitmap); }
            } finally { Marshal.ReleaseComObject(factory); }
        } catch { return null; }
    }
    internal static Bitmap CopyShellBitmap(IntPtr handle) {
        // FromHbitmap discards alpha. Request the Shell's 32-bit pixels in
        // top-down order; GetObject alone does not report the original orientation.
        if(GetBitmapObject(handle,Marshal.SizeOf<NativeBitmap>(),out var info)!=0&&info.BitsPerPixel==32) {
            int width=info.Width,height=info.Height,rowBytes=checked(width*4);
            var pixels=new byte[checked(rowBytes*height)];
            var header=new BitmapHeader {Size=(uint)Marshal.SizeOf<BitmapHeader>(),Width=width,Height=-height,Planes=1,BitCount=32};
            var dc=CreateCompatibleDC(IntPtr.Zero);int rows=0;
            try { if(dc!=IntPtr.Zero)rows=GetDIBits(dc,handle,0,(uint)height,pixels,ref header,0); }
            finally { if(dc!=IntPtr.Zero)DeleteDC(dc); }
            bool hasAlpha=false;
            for(int i=3;i<pixels.Length;i+=4)if(pixels[i]!=0){hasAlpha=true;break;}
            if(rows==height&&hasAlpha) {
                var result=new Bitmap(width,height,PixelFormat.Format32bppPArgb);
                try {
                    var data=result.LockBits(new Rectangle(0,0,width,height),ImageLockMode.WriteOnly,PixelFormat.Format32bppPArgb);
                    try { for(int y=0;y<height;y++)Marshal.Copy(pixels,y*rowBytes,IntPtr.Add(data.Scan0,y*data.Stride),rowBytes); }
                    finally { result.UnlockBits(data); }
                    return result;
                } catch { result.Dispose();throw; }
            }
        }
        // Older bitmaps may have no alpha channel; keep their opaque artwork.
        using var source=Image.FromHbitmap(handle);return new Bitmap(source);
    }
    public static Icon GroupIcon(Group group) {
        using var bitmap=new Bitmap(64,64); using var g=Graphics.FromImage(bitmap); g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var color=ColorTranslator.FromHtml(group.Color); using var fill=new SolidBrush(color); using var pen=new Pen(Color.FromArgb(22,25,39),4) { StartCap=System.Drawing.Drawing2D.LineCap.Round,EndCap=System.Drawing.Drawing2D.LineCap.Round };
        g.FillEllipse(fill,2,2,60,60);
        if(group.Symbol=="work") { g.DrawRectangle(pen,17,25,30,21); g.DrawRectangle(pen,25,18,14,7); g.DrawLine(pen,17,33,47,33); g.DrawLine(pen,32,31,32,36); }
        else if(group.Symbol=="games") { g.DrawArc(pen,13,21,38,28,170,200); g.DrawLine(pen,13,35,18,45); g.DrawLine(pen,18,45,26,38); g.DrawLine(pen,26,38,38,38); g.DrawLine(pen,38,38,46,45); g.DrawLine(pen,46,45,51,35); g.DrawLine(pen,21,28,21,36); g.DrawLine(pen,17,32,25,32); g.DrawEllipse(pen,38,28,1,1); g.DrawEllipse(pen,44,33,1,1); }
        else if(group.Symbol=="ai") { g.DrawRectangle(pen,21,21,22,22); g.DrawRectangle(pen,27,27,10,10); for(int i=25;i<=39;i+=7) { g.DrawLine(pen,i,15,i,21); g.DrawLine(pen,i,43,i,49); g.DrawLine(pen,15,i,21,i); g.DrawLine(pen,43,i,49,i); } }
        else if(group.Symbol=="folder") { g.DrawLines(pen,new[]{new Point(16,43),new Point(16,21),new Point(29,21),new Point(34,27),new Point(48,27),new Point(48,43),new Point(16,43)}); }
        else { using var font=new Font("Segoe UI",24,FontStyle.Bold); using var ink=new SolidBrush(Color.FromArgb(22,25,39)); var format=new StringFormat { Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center }; g.DrawString(group.Name.Substring(0,1).ToUpperInvariant(),font,ink,new RectangleF(0,0,64,64),format); }
        g.Flush();using var png=new MemoryStream();bitmap.Save(png,System.Drawing.Imaging.ImageFormat.Png);using var ico=new MemoryStream();using(var writer=new BinaryWriter(ico,Encoding.UTF8,true)){writer.Write((ushort)0);writer.Write((ushort)1);writer.Write((ushort)1);writer.Write((byte)64);writer.Write((byte)64);writer.Write((byte)0);writer.Write((byte)0);writer.Write((ushort)1);writer.Write((ushort)32);writer.Write((uint)png.Length);writer.Write((uint)22);writer.Write(png.ToArray());}ico.Position=0;return new Icon(ico,64,64);
    }
}
