using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace TaskbarGroups;

static class GroupArtwork {
    internal const string RenderingVersion="large-v1";
    internal static Bitmap Load(string path) {
        if(!File.Exists(path))throw new FileNotFoundException("No se encuentra el archivo de icono.");
        var extension=Path.GetExtension(path).ToLowerInvariant();
        if(extension is not (".ico" or ".png" or ".jpg" or ".jpeg" or ".bmp"))throw new ArgumentException("Usa un archivo ICO, PNG, JPG o BMP.");
        using var source=extension==".ico"?LoadIcon(path):new Bitmap(path);
        var result=new Bitmap(64,64,PixelFormat.Format32bppArgb);
        try {
            using var graphics=Graphics.FromImage(result);
            graphics.InterpolationMode=InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode=PixelOffsetMode.HighQuality;
            var bounds=VisibleBounds(source);
            float scale=Math.Min(64f/bounds.Width,64f/bounds.Height);
            float width=bounds.Width*scale,height=bounds.Height*scale;
            graphics.DrawImage(source,new RectangleF((64-width)/2,(64-height)/2,width,height),bounds,GraphicsUnit.Pixel);
            return result;
        } catch {result.Dispose();throw;}
    }
    static Rectangle VisibleBounds(Bitmap bitmap) {
        int left=bitmap.Width,top=bitmap.Height,right=-1,bottom=-1;
        for(int y=0;y<bitmap.Height;y++)for(int x=0;x<bitmap.Width;x++)if(bitmap.GetPixel(x,y).A>=16){left=Math.Min(left,x);top=Math.Min(top,y);right=Math.Max(right,x);bottom=Math.Max(bottom,y);}
        // Leave one source pixel around the visible edge for antialiasing.
        return right<left?new Rectangle(Point.Empty,bitmap.Size):Rectangle.FromLTRB(Math.Max(0,left-1),Math.Max(0,top-1),Math.Min(bitmap.Width,right+2),Math.Min(bitmap.Height,bottom+2));
    }
    static Bitmap LoadIcon(string path) {using var icon=new Icon(path,64,64);return icon.ToBitmap();}
    internal static string Import(string path) {
        using var bitmap=Load(path);
        using var icon=ToIcon(bitmap);
        using var stream=new MemoryStream();icon.Save(stream);var bytes=stream.ToArray();
        var name=Path.GetFileNameWithoutExtension(path);if(name.Length>64)name=name.Substring(0,64);
        var relative=Path.Combine("AppIcons",name+"-"+Convert.ToHexString(SHA256.HashData(bytes))+".ico");
        var destination=Path.Combine(Program.Base,relative);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if(!File.Exists(destination))File.WriteAllBytes(destination,bytes);
        return relative;
    }
    internal static Icon ToIcon(Bitmap bitmap) {
        using var png=new MemoryStream();bitmap.Save(png,ImageFormat.Png);
        using var ico=new MemoryStream();
        using(var writer=new BinaryWriter(ico,Encoding.UTF8,true)) {
            writer.Write((ushort)0);writer.Write((ushort)1);writer.Write((ushort)1);
            writer.Write((byte)bitmap.Width);writer.Write((byte)bitmap.Height);writer.Write((byte)0);writer.Write((byte)0);
            writer.Write((ushort)1);writer.Write((ushort)32);writer.Write((uint)png.Length);writer.Write((uint)22);writer.Write(png.ToArray());
        }
        ico.Position=0;return new Icon(ico,bitmap.Width,bitmap.Height);
    }
}
