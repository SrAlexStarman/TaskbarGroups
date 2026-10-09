using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace TaskbarGroups;

sealed partial class Popup {
    const double EntranceMilliseconds=250;
    readonly Timer entranceTimer=new(){Interval=15};
    readonly Stopwatch entranceClock=new();
    Rectangle entranceArea;
    Rectangle? entranceClip;
    Point entranceStart,entranceEnd;

    void InitializeMotion()=>entranceTimer.Tick+=(_,_)=>AdvanceEntrance();

    void ConfigureEntrance(Rectangle anchor,Screen screen) {
        entranceArea=screen.WorkingArea;
        entranceEnd=Location;
        entranceStart=anchor.Top<entranceArea.Top?new Point(Left,entranceArea.Top-Height)
            :anchor.Left<entranceArea.Left?new Point(entranceArea.Left-Width,Top)
            :anchor.Right>entranceArea.Right?new Point(entranceArea.Right,Top)
            :new Point(Left,entranceArea.Bottom);
    }

    public void ShowFromTaskbar(bool animate) {
        // Render hides a changed group; repeat requests for the same open group
        // should keep it in place rather than restart an ongoing entrance.
        if(Visible||!animate||!Native.ClientAnimationsEnabled){Show();return;}
        frame?.Render(coverGaps:true);
        entranceClip=entranceArea;
        MoveEntrance(entranceStart);
        Show();
        Refresh();
        if(!entranceClip.HasValue)return;
        entranceClock.Restart();
        entranceTimer.Start();
    }

    void AdvanceEntrance() {
        if(!entranceClip.HasValue){entranceTimer.Stop();return;}
        double progress=Math.Min(1,entranceClock.Elapsed.TotalMilliseconds/EntranceMilliseconds);
        if(progress>=1){FinishEntrance();return;}
        // Windows' direct entrance curve: cubic-bezier(0,0,0,1).
        double t=Math.Cbrt(progress),ease=3*t*t-2*progress;
        MoveEntrance(new Point(
            (int)Math.Round(entranceStart.X+(entranceEnd.X-entranceStart.X)*ease),
            (int)Math.Round(entranceStart.Y+(entranceEnd.Y-entranceStart.Y)*ease)));
    }

    void MoveEntrance(Point location) {
        // Clip both HWNDs before moving, so the popup never paints over the
        // taskbar while emerging. Its normal controls remain live throughout.
        ApplyShape(location);
        Location=location;
        // Commit the newly exposed content, including child controls, before
        // another animation frame can expose an unpainted background.
        if(Visible)Refresh();
    }

    Rectangle? EntranceClip(Point location)=>entranceClip is Rectangle area
        ?Rectangle.Intersect(new Rectangle(Point.Empty,ClientSize),
            new Rectangle(area.Left-location.X,area.Top-location.Y,area.Width,area.Height))
        :null;

    void FinishEntrance() {
        if(!entranceClip.HasValue)return;
        entranceTimer.Stop();entranceClock.Reset();entranceClip=null;
        Location=entranceEnd;
        ApplyShape(Location);
        if(Visible)Refresh();
        frame?.Render();
    }

    void StopEntrance(){entranceTimer?.Stop();entranceClock?.Reset();entranceClip=null;}
}
