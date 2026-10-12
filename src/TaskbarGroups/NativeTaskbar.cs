using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;

namespace TaskbarGroups;

static class NativeTaskbar {
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr window);
    internal static Group? MatchAccessibleGroup(string name,string automationId,Group[] groups) {
        const string idPrefix="Appid: ";
        string appId=automationId.StartsWith(idPrefix,StringComparison.OrdinalIgnoreCase)?automationId.Substring(idPrefix.Length):automationId;
        var identified=groups.FirstOrDefault(g=>appId.Equals(Native.AppId(g),StringComparison.OrdinalIgnoreCase));
        if(identified!=null)return identified;
        // An explicit identity for a different taskbar app must not match its label.
        if(automationId.StartsWith(idPrefix,StringComparison.OrdinalIgnoreCase)||appId.StartsWith("Personal.TaskbarGroups.",StringComparison.OrdinalIgnoreCase))return null;
        // Explorer uses RelaunchDisplayName, plus a localized running-window count,
        // instead of the group window title. Prefer longer names when they overlap.
        return groups.OrderByDescending(g=>g.Name.Length).FirstOrDefault(g=>new[]{g.Name,GroupWindow.Title(g)}.Any(label=>
            name.Equals(label,StringComparison.OrdinalIgnoreCase)||new[]{":",","," -"}.Any(separator=>name.StartsWith(label+separator,StringComparison.OrdinalIgnoreCase))));
    }
    // Explorer's XAML accessibility can omit buttons. Locate only our distinctive
    // icon artwork in a screenshot of the actual taskbar; never draw over it.
    public static TaskButton[] FindIcons(Group[] groups,TaskButton[]? knownButtons=null){
        var result=new List<(TaskButton Button,double Score)>();
        foreach(var bar in Native.Taskbars()){
            if(bar.Bounds.Width<1||bar.Bounds.Height<1)continue;
            using var bitmap=new Bitmap(bar.Bounds.Width,bar.Bounds.Height,PixelFormat.Format32bppArgb);
            using(var g=Graphics.FromImage(bitmap))g.CopyFromScreen(bar.Bounds.Location,Point.Empty,bitmap.Size);
            var data=bitmap.LockBits(new Rectangle(Point.Empty,bitmap.Size),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
            byte[] pixels=new byte[data.Stride*bitmap.Height];Marshal.Copy(data.Scan0,pixels,0,pixels.Length);bitmap.UnlockBits(data);
            foreach(var group in groups){
                if(knownButtons?.Any(b=>b.Id==group.Id&&b.Bounds.IntersectsWith(bar.Bounds))==true)continue;
                if(group.UseCustomIcon) {
                    using var icon=Native.GroupIcon(group);using var reference=icon.ToBitmap();
                    foreach(var match in MatchCustomIcon(pixels,bitmap.Width,bitmap.Height,data.Stride,reference,(int)GetDpiForWindow(bar.Handle))) {
                        var rect=match.Bounds;rect.Offset(bar.Bounds.Location);
                        var center=new Point(rect.Left+rect.Width/2,rect.Top+rect.Height/2);
                        if(knownButtons?.Any(b=>b.Bounds.Contains(center))!=true)result.Add((new TaskButton(group.Id,GroupWindow.Title(group),rect),match.Score));
                    }
                    continue;
                }
                var color=ColorTranslator.FromHtml(group.Color);int w=bitmap.Width,h=bitmap.Height;var mask=new bool[w*h];
                for(int y=0;y<h;y++)for(int x=0;x<w;x++){int p=y*data.Stride+x*4;bool accent=Math.Abs(pixels[p]-color.B)<=18&&Math.Abs(pixels[p+1]-color.G)<=18&&Math.Abs(pixels[p+2]-color.R)<=18;bool neutral=Math.Abs(pixels[p]-192)<=5&&Math.Abs(pixels[p+1]-192)<=5&&Math.Abs(pixels[p+2]-192)<=5;mask[y*w+x]=accent||neutral;}
                for(int start=0;start<mask.Length;start++){
                    if(!mask[start])continue;var queue=new Queue<int>();queue.Enqueue(start);mask[start]=false;int count=0,left=w,top=h,right=0,bottom=0;
                    while(queue.Count>0){int v=queue.Dequeue(),x=v%w,y=v/w;count++;left=Math.Min(left,x);right=Math.Max(right,x);top=Math.Min(top,y);bottom=Math.Max(bottom,y);foreach(int n in new[]{x>0?v-1:-1,x<w-1?v+1:-1,y>0?v-w:-1,y<h-1?v+w:-1})if(n>=0&&mask[n]){mask[n]=false;queue.Enqueue(n);}}
                    int iw=right-left+1,ih=bottom-top+1;if(count<70||iw<14||ih<14||iw>64||ih>64||Math.Abs(iw-ih)>5)continue;
                    using var icon=Native.GroupIcon(group);using var reference=icon.ToBitmap();double best=0;
                    int ideal=iw;
                    for(int size=Math.Max(16,ideal-2);size<=ideal+3;size++){
                        using var scaled=new Bitmap(reference,new Size(size,size));var samples=new List<(int X,int Y,bool Ink)>();
                        for(int y=0;y<size;y++)for(int x=0;x<size;x++){var e=scaled.GetPixel(x,y);if(e.A<230)continue;bool ink=e.R<70&&e.G<70&&e.B<80;if(!ink&&Math.Max(e.R,Math.Max(e.G,e.B))<170)continue;samples.Add((x,y,ink));}
                        for(int dx=-2;dx<=2;dx++)for(int dy=-2;dy<=2;dy++){int ox=left-(size-iw)/2+dx,oy=top-(size-ih)/2+dy,inkCount=0,inkMatches=0,fillCount=0,fillMatches=0;
                            foreach(var sample in samples){int x=ox+sample.X,y=oy+sample.Y;if(x<0||x>=w||y<0||y>=h)continue;int p=y*data.Stride+x*4;bool dark=Math.Max(pixels[p],Math.Max(pixels[p+1],pixels[p+2]))<155;if(sample.Ink){inkCount++;if(dark)inkMatches++;}else{fillCount++;if(!dark)fillMatches++;}}
                            if(inkCount>8&&fillCount>30)best=Math.Max(best,0.65*inkMatches/(double)inkCount+0.35*fillMatches/(double)fillCount);
                        }
                    }
                    if(best<0.82)continue;int targetWidth=Math.Max(36,iw*2);var rect=new Rectangle(bar.Bounds.Left+(left+right)/2-targetWidth/2,bar.Bounds.Top,targetWidth,bar.Bounds.Height);result.Add((new TaskButton(group.Id,GroupWindow.Title(group),rect),best));
                }
            }
        }
        return result.GroupBy(b=>b.Button.Bounds).Select(g=>g.OrderByDescending(b=>b.Score).ToArray()).Where(g=>g.Length==1||g[0].Score-g[1].Score>0.025).Select(g=>g[0].Button).ToArray();
    }
    // Custom artwork has no fixed accent color. Compare opaque artwork samples
    // near the taskbar's icon row, rejecting candidates before the full comparison.
    internal static List<(Rectangle Bounds,double Score)> MatchCustomIcon(byte[] pixels,int width,int height,int stride,Bitmap reference,int dpi) {
        var matches=new List<(Rectangle Bounds,double Score)>();double scale=Math.Max(96,dpi)/96.0;
        if(width<12||height<12)return matches;
        int buttonWidth=(int)Math.Round(48*scale),verticalTolerance=(int)Math.Round(6*scale);
        // Explorer can use smaller/intermediate icon sizes independently of the
        // taskbar HWND's DPI. Search the captured pixel sizes, not only DPI multiples.
        var sizes=Enumerable.Range(12,Math.Min(96,height)-11);
        foreach(int size in sizes) {
            if(size>height||size>width)continue;
            using var scaled=new Bitmap(reference,new Size(size,size));
            var samples=new List<(int X,int Y,Color Color)>();
            int step=Math.Max(1,size/8);
            for(int y=step/2;y<size;y+=step)for(int x=step/2;x<size;x+=step){var color=scaled.GetPixel(x,y);if(color.A>=240)samples.Add((x,y,color));}
            if(samples.Count<12)continue;
            var probes=Enumerable.Range(0,6).Select(i=>samples[i*(samples.Count-1)/5]).ToArray();
            int firstY=Math.Max(0,(height-size)/2-verticalTolerance),lastY=Math.Min(height-size,(height-size)/2+verticalTolerance);
            for(int y=firstY;y<=lastY;y++)for(int x=0;x<=width-size;x++) {
                int probeMatches=0;
                // At small sizes Windows blends pixels along symbol edges;
                // allow edge probes to differ, then check the complete artwork.
                foreach(var probe in probes)if(Difference(probe.Color,pixels,(y+probe.Y)*stride+(x+probe.X)*4)<=42)probeMatches++;
                if(probeMatches<4)continue;
                int error=0,close=0;
                foreach(var sample in samples){int difference=Difference(sample.Color,pixels,(y+sample.Y)*stride+(x+sample.X)*4);error+=difference;if(difference<=42)close++;}
                double average=error/(double)samples.Count;
                if(average>28||close<samples.Count*0.8)continue;
                var rect=new Rectangle(x+size/2-buttonWidth/2,0,buttonWidth,height);
                matches.Add((rect,1-average/255));
            }
        }
        var result=new List<(Rectangle Bounds,double Score)>();
        foreach(var match in matches.OrderByDescending(m=>m.Score))if(!result.Any(m=>m.Bounds.IntersectsWith(match.Bounds)))result.Add(match);
        return result;
    }
    static int Difference(Color color,byte[] pixels,int index)=>Math.Max(Math.Abs(color.B-pixels[index]),Math.Max(Math.Abs(color.G-pixels[index+1]),Math.Abs(color.R-pixels[index+2])));
}
