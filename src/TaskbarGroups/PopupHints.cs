using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Windows.Forms;

namespace TaskbarGroups;

// A mouse-transparent layered tooltip keeps its rounded edges smooth without
// taking focus or interrupting the app button underneath it.
sealed class PopupHints : NativeWindow,IDisposable {
    readonly Form owner;
    readonly Dictionary<Control,string> captions=new();
    readonly Timer delay=new(){Interval=600};
    readonly Timer lifetime=new(){Interval=5000};
    Control? hovered;
    DateTime lastShown=DateTime.MinValue;
    bool disposed;

    public PopupHints(Form owner) {
        this.owner=owner;
        delay.Tick+=(_,_)=>{
            delay.Stop();
            if(hovered is Control control&&!control.IsDisposed&&control.Visible&&owner.Visible
                &&control.RectangleToScreen(control.ClientRectangle).Contains(Cursor.Position))ShowHint(control);
            else Hide();
        };
        lifetime.Tick+=(_,_)=>Hide();
        owner.VisibleChanged+=OwnerChanged;owner.LocationChanged+=OwnerChanged;
        owner.SizeChanged+=OwnerChanged;owner.DpiChanged+=OwnerChanged;
        owner.HandleDestroyed+=OwnerHandleDestroyed;
    }
    public void SetToolTip(Control control,string text) {
        if(!captions.ContainsKey(control)) {
            control.MouseEnter+=MouseEntered;control.MouseLeave+=MouseLeft;
            control.MouseDown+=MousePressed;control.Disposed+=ControlDisposed;
        }
        captions[control]=text;
    }
    public string GetToolTip(Control control)=>captions.TryGetValue(control,out var text)?text:"";
    void MouseEntered(object? sender,EventArgs e) {
        Hide();
        if(sender is not Control control||string.IsNullOrWhiteSpace(GetToolTip(control)))return;
        hovered=control;delay.Interval=(DateTime.UtcNow-lastShown).TotalMilliseconds<1000?100:600;delay.Start();
    }
    void MouseLeft(object? sender,EventArgs e){if(sender==hovered)Hide();}
    void MousePressed(object? sender,MouseEventArgs e)=>Hide();
    void OwnerChanged(object? sender,EventArgs e) {
        var control=hovered;Hide();
        // If the popup is still moving under the pointer, wait until its
        // position settles rather than losing the hover for that control.
        if(owner.Visible&&control!=null&&!control.IsDisposed&&control.Visible
            &&control.RectangleToScreen(control.ClientRectangle).Contains(Cursor.Position)) {
            hovered=control;delay.Interval=600;delay.Start();
        }
    }
    void OwnerHandleDestroyed(object? sender,EventArgs e){Hide();if(Handle!=IntPtr.Zero)DestroyHandle();}
    void ControlDisposed(object? sender,EventArgs e) {
        if(sender is not Control control)return;
        if(control==hovered)Hide();Unregister(control);captions.Remove(control);
    }
    void Unregister(Control control) {
        control.MouseEnter-=MouseEntered;control.MouseLeave-=MouseLeft;
        control.MouseDown-=MousePressed;control.Disposed-=ControlDisposed;
    }
    public void RemoveAll() {
        Hide();foreach(var control in captions.Keys)Unregister(control);captions.Clear();
    }
    public void Hide() {
        delay.Stop();lifetime.Stop();hovered=null;
        if(Handle!=IntPtr.Zero)Native.ShowAlphaFrame(Handle,IntPtr.Zero,false);
    }
    void ShowHint(Control control) {
        if(disposed||owner.IsDisposed||!owner.Visible||control.IsDisposed||!control.Visible)return;
        var text=GetToolTip(control);if(string.IsNullOrWhiteSpace(text))return;
        var anchor=control.RectangleToScreen(control.ClientRectangle);
        if(control.Parent is Control parent)anchor=Rectangle.Intersect(anchor,parent.RectangleToScreen(parent.ClientRectangle));
        if(anchor.IsEmpty)return;
        var area=Screen.FromRectangle(anchor).WorkingArea;
        using var artwork=CreateArtwork(text,control.DeviceDpi,area.Size);
        var location=Position(anchor,artwork.Size,area,control.DeviceDpi);
        if(Handle==IntPtr.Zero)CreateHandle(new CreateParams {
            Caption="Nombre de la aplicación",Parent=owner.Handle,
            Style=unchecked((int)0x80000000),ExStyle=0x080800A0
        });
        hovered=control;
        Native.SetAlphaFrame(Handle,artwork,location);Native.ShowAlphaFrame(Handle,owner.Handle,true);
        lastShown=DateTime.UtcNow;lifetime.Start();
    }
    internal static Point Position(Rectangle anchor,Size size,Rectangle area,int dpi) {
        int gap=(int)Math.Round(8*dpi/96f),margin=(int)Math.Round(4*dpi/96f);
        int x=anchor.Left+(anchor.Width-size.Width)/2,y=anchor.Top-size.Height-gap;
        if(y<area.Top+margin)y=anchor.Bottom+gap;
        return new Point(Math.Clamp(x,area.Left+margin,Math.Max(area.Left+margin,area.Right-size.Width-margin)),
            Math.Clamp(y,area.Top+margin,Math.Max(area.Top+margin,area.Bottom-size.Height-margin)));
    }
    internal static Bitmap CreateArtwork(string text,int dpi,Size available) {
        int S(int n)=>(int)Math.Round(n*dpi/96f);
        int inset=S(1),paddingX=S(8),paddingY=S(6);
        int maxWidth=Math.Max(S(32),Math.Min(S(360),available.Width-S(16)));
        int maxHeight=Math.Max(S(28),available.Height-S(16));
        using var font=new Font("Segoe UI",10f,FontStyle.Bold,GraphicsUnit.Point);
        using var format=(StringFormat)StringFormat.GenericTypographic.Clone();
        format.FormatFlags|=StringFormatFlags.LineLimit;format.Trimming=StringTrimming.EllipsisWord;
        using var measure=new Bitmap(1,1);measure.SetResolution(dpi,dpi);
        SizeF textSize;using(var g=Graphics.FromImage(measure)){g.TextRenderingHint=TextRenderingHint.AntiAliasGridFit;textSize=g.MeasureString(text,font,maxWidth-2*(inset+paddingX),format);}
        int width=Math.Min(maxWidth,(int)Math.Ceiling(textSize.Width)+2*(inset+paddingX));
        int height=Math.Min(maxHeight,(int)Math.Ceiling(textSize.Height)+2*(inset+paddingY));
        var bitmap=new Bitmap(width,height,PixelFormat.Format32bppPArgb);bitmap.SetResolution(dpi,dpi);
        try {
            using var g=Graphics.FromImage(bitmap);g.SmoothingMode=SmoothingMode.AntiAlias;
            var bounds=new RectangleF(inset+.5f,inset+.5f,width-2*inset-1,height-2*inset-1);
            var shadowBounds=bounds;shadowBounds.Offset(0,inset);
            using var shadow=PopupShape.RoundedPath(shadowBounds,dpi,3f);
            using var shadowBrush=new SolidBrush(Color.FromArgb(80,0,0,0));g.FillPath(shadowBrush,shadow);
            using var path=PopupShape.RoundedPath(bounds,dpi,3f);
            using var fill=new SolidBrush(Color.FromArgb(43,43,43));g.FillPath(fill,path);
            using var border=new Pen(Color.FromArgb(55,55,55));g.DrawPath(border,path);
            g.TextRenderingHint=TextRenderingHint.AntiAliasGridFit;
            g.DrawString(text,font,Brushes.White,new RectangleF(inset+paddingX,inset+paddingY,
                width-2*(inset+paddingX),height-2*(inset+paddingY)),format);
            return bitmap;
        }catch{bitmap.Dispose();throw;}
    }
    protected override void WndProc(ref Message message) {
        if(message.Msg==0x84){message.Result=new IntPtr(-1);return;} // HTTRANSPARENT
        if(message.Msg==0x21){message.Result=new IntPtr(3);return;} // MA_NOACTIVATE
        base.WndProc(ref message);
    }
    public void Dispose() {
        if(disposed)return;disposed=true;RemoveAll();delay.Dispose();lifetime.Dispose();
        owner.VisibleChanged-=OwnerChanged;owner.LocationChanged-=OwnerChanged;
        owner.SizeChanged-=OwnerChanged;owner.DpiChanged-=OwnerChanged;
        owner.HandleDestroyed-=OwnerHandleDestroyed;if(Handle!=IntPtr.Zero)DestroyHandle();
    }
}
