using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;

namespace TaskbarGroups;

static class NativeTaskbar {
    // Explorer's XAML accessibility can omit buttons. Locate only our distinctive
    // icon artwork in a screenshot of the actual taskbar; never draw over it.
    public static TaskButton[] FindIcons(Group[] groups){
        var result=new List<(TaskButton Button,double Score)>();
        foreach(var bar in Native.Taskbars()){
            if(bar.Bounds.Width<1||bar.Bounds.Height<1)continue;
            using var bitmap=new Bitmap(bar.Bounds.Width,bar.Bounds.Height,PixelFormat.Format32bppArgb);
            using(var g=Graphics.FromImage(bitmap))g.CopyFromScreen(bar.Bounds.Location,Point.Empty,bitmap.Size);
            var data=bitmap.LockBits(new Rectangle(Point.Empty,bitmap.Size),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
            byte[] pixels=new byte[data.Stride*bitmap.Height];Marshal.Copy(data.Scan0,pixels,0,pixels.Length);bitmap.UnlockBits(data);
            foreach(var group in groups){
                var color=ColorTranslator.FromHtml(group.Color);int w=bitmap.Width,h=bitmap.Height;var mask=new bool[w*h];
                for(int y=0;y<h;y++)for(int x=0;x<w;x++){int p=y*data.Stride+x*4;bool accent=Math.Abs(pixels[p]-color.B)<=18&&Math.Abs(pixels[p+1]-color.G)<=18&&Math.Abs(pixels[p+2]-color.R)<=18;bool neutral=Math.Abs(pixels[p]-192)<=5&&Math.Abs(pixels[p+1]-192)<=5&&Math.Abs(pixels[p+2]-192)<=5;mask[y*w+x]=accent||neutral;}
                for(int start=0;start<mask.Length;start++){
                    if(!mask[start])continue;var queue=new Queue<int>();queue.Enqueue(start);mask[start]=false;int count=0,left=w,top=h,right=0,bottom=0;
                    while(queue.Count>0){int v=queue.Dequeue(),x=v%w,y=v/w;count++;left=Math.Min(left,x);right=Math.Max(right,x);top=Math.Min(top,y);bottom=Math.Max(bottom,y);foreach(int n in new[]{x>0?v-1:-1,x<w-1?v+1:-1,y>0?v-w:-1,y<h-1?v+w:-1})if(n>=0&&mask[n]){mask[n]=false;queue.Enqueue(n);}}
                    int iw=right-left+1,ih=bottom-top+1;if(count<70||iw<14||ih<14||iw>64||ih>64||Math.Abs(iw-ih)>5)continue;
                    using var icon=Native.GroupIcon(group);using var reference=icon.ToBitmap();double best=0;
                    int ideal=(int)Math.Round(iw*64.0/60.0);
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
}
