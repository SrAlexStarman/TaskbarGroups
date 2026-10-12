using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TaskbarGroups;

sealed partial class Popup {
    readonly Timer runningTimer=new(){Interval=1000};
    readonly List<(string Key,AppEntry App,AppTile Tile)> runningTiles=new();
    readonly Dictionary<string,bool> runningStates=new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string,RunningApps.Identity> runningIdentities=new(StringComparer.OrdinalIgnoreCase);
    int runningGeneration;
    bool runningScan;
    void InitializeRunningState() {
        runningTimer.Tick+=(_,_)=>RefreshRunningState();
        VisibleChanged+=(_,_)=>{
            if(Visible){runningTimer.Start();RefreshRunningState();}
            else {runningTimer.Stop();runningGeneration++;}
        };
    }
    void WatchRunningApp(AppEntry app,AppTile tile) {
        var copy=new AppEntry {Target=app.Target,Arguments=app.Arguments,AppId=app.AppId};string key=RunningApps.Key(copy);
        tile.IsRunning=runningStates.TryGetValue(key,out bool running)&&running;
        runningTiles.Add((key,copy,tile));
    }
    async void RefreshRunningState() {
        if(runningScan||IsDisposed||!Visible||runningTiles.Count==0)return;
        runningScan=true;int generation=runningGeneration;var tiles=runningTiles.ToArray();
        try {
            var states=await Task.Run(()=>{
                var snapshot=RunningApps.Capture();return tiles.Select(item=>{
                    string target=item.App.ResolvedTarget;
                    string key=item.Key+"\0"+(File.Exists(target)?File.GetLastWriteTimeUtc(target).Ticks:0);
                    if(!runningIdentities.TryGetValue(key,out var identity))runningIdentities[key]=identity=RunningApps.Resolve(item.App);
                    return snapshot.IsRunning(identity);
                }).ToArray();
            });
            if(IsDisposed||!Visible||generation!=runningGeneration)return;
            for(int index=0;index<tiles.Length;index++) {
                runningStates[tiles[index].Key]=states[index];
                if(!tiles[index].Tile.IsDisposed)tiles[index].Tile.IsRunning=states[index];
            }
        }catch(Exception error){Program.Log("Estado de aplicaciones: "+error.Message);}
        finally {runningScan=false;if(!IsDisposed&&Visible&&generation!=runningGeneration)RefreshRunningState();}
    }
    void DisposeRunningState(){runningGeneration++;runningTimer.Stop();runningTimer.Dispose();}
}
