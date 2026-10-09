using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace TaskbarGroups;

// The layered window draws the outline and shields tile gaps during entrance.
// Controls remain transparent holes, preserving their normal input behavior.
sealed class PopupFrame : NativeWindow,IDisposable {
    readonly Form owner;
    readonly Func<RectangleF,GraphicsPath> outline;
    Bitmap? artwork;
    public int Inset=>(int)Math.Round(10*owner.DeviceDpi/96f);
    public PopupFrame(Form owner,Func<RectangleF,GraphicsPath> outline) {
        this.owner=owner;this.outline=outline;
        // Owned popup: layered, mouse-transparent, tool window, no activation.
        CreateHandle(new CreateParams {Caption="Contorno del grupo",Parent=owner.Handle,Style=unchecked((int)0x80000000),ExStyle=0x080800A0});
    }
    public void Render(bool coverGaps=false) {
        if(owner.ClientSize.Width<2||owner.ClientSize.Height<2)return;
        var next=new Bitmap(owner.ClientSize.Width,owner.ClientSize.Height,PixelFormat.Format32bppPArgb);
        try {
            using(var g=Graphics.FromImage(next)) {
                g.SmoothingMode=SmoothingMode.AntiAlias;
                using var path=outline(new RectangleF(.5f,.5f,next.Width-1,next.Height-1));
                using var fill=new SolidBrush(owner.BackColor);using var border=new Pen(Color.FromArgb(67,73,101));
                g.FillPath(fill,path);g.DrawPath(border,path);
                // During entrance, cover the panel's newly exposed gaps so its
                // pending background paint cannot appear through the outline.
                g.SmoothingMode=SmoothingMode.None;g.CompositingMode=CompositingMode.SourceCopy;
                if(coverGaps) {
                    foreach(Control control in owner.Controls) {
                        if(control is Panel panel) {
                            foreach(Control child in panel.Controls) {
                                var hole=Rectangle.Intersect(child.Bounds,panel.ClientRectangle);hole.Offset(panel.Location);
                                if(hole.Width>0&&hole.Height>0)g.FillRectangle(Brushes.Transparent,hole);
                            }
                        }else g.FillRectangle(Brushes.Transparent,control.Bounds);
                    }
                }else g.FillRectangle(Brushes.Transparent,Inset,Inset,next.Width-2*Inset,next.Height-2*Inset);
            }
            Native.SetAlphaFrame(Handle,next,owner.Location);
            artwork?.Dispose();artwork=next;
        } catch {next.Dispose();throw;}
    }
    public void Move()=>Native.MoveAlphaFrame(Handle,owner.Location);
    public void Clip(Rectangle? rectangle)=>Native.SetWindowClip(Handle,rectangle);
    public void Show(bool visible)=>Native.ShowAlphaFrame(Handle,owner.Handle,visible);
    public void DrawToBitmap(Bitmap bitmap,Rectangle bounds) {
        if(artwork==null)return;
        using var g=Graphics.FromImage(bitmap);
        using var outside=new Region(bounds);
        outside.Exclude(new Rectangle(bounds.Left+Inset,bounds.Top+Inset,bounds.Width-2*Inset,bounds.Height-2*Inset));
        g.SetClip(outside,CombineMode.Replace);g.CompositingMode=CompositingMode.SourceCopy;g.FillRectangle(Brushes.Transparent,bounds);
        g.ResetClip();g.CompositingMode=CompositingMode.SourceOver;
        if(bounds.Size==artwork.Size)g.DrawImageUnscaled(artwork,bounds.Location);else g.DrawImage(artwork,bounds);
    }
    protected override void WndProc(ref Message message) {
        if(message.Msg==0x84){message.Result=new IntPtr(-1);return;} // HTTRANSPARENT
        if(message.Msg==0x21){message.Result=new IntPtr(3);return;} // MA_NOACTIVATE
        base.WndProc(ref message);
    }
    public void Dispose(){artwork?.Dispose();artwork=null;if(Handle!=IntPtr.Zero)DestroyHandle();}
}
