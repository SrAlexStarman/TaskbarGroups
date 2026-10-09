using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Forms;

namespace TaskbarGroups;

public class AppEntry {
    public string Name {get;set;}="";
    public string Target {get;set;}="";
    public string Arguments {get;set;}="";
    public string IconPath {get;set;}="";
    public string AppId {get;set;}="";
    public override string ToString()=>Name;
    [System.Text.Json.Serialization.JsonIgnore] public string ResolvedTarget=>Path.IsPathRooted(Target)||Target.Contains(':') ? Target : Path.Combine(Program.Base,Target);
    [System.Text.Json.Serialization.JsonIgnore] public string ResolvedIconPath=>Path.IsPathRooted(IconPath)?IconPath:Path.Combine(Program.Base,IconPath);
    public void Launch() {
        if(Target.StartsWith("shell:AppsFolder\\",StringComparison.OrdinalIgnoreCase)) Process.Start(new ProcessStartInfo("explorer.exe") { Arguments=Target,UseShellExecute=true });
        else Process.Start(new ProcessStartInfo(ResolvedTarget) { Arguments=Arguments,UseShellExecute=true });
    }
}
public class Group {
    public string Id {get;set;}=Guid.NewGuid().ToString("N");
    public string Name {get;set;}="Nuevo grupo";
    public string Color {get;set;}="#6EADFF";
    public string Symbol {get;set;}="folder";
    public List<AppEntry> Apps {get;set;}=new();
    public override string ToString()=>Name;
}
public class Config {
    public int HoverDelayMs {get;set;}=180;
    public bool SuppressWindowsPreview {get;set;}=true;
    public List<Group> Groups {get;set;}=new();
    public void Validate() {
        if(Groups.Count>20) throw new Exception("Puedes crear hasta 20 grupos.");
        if(Groups.Select(g=>g.Id).Distinct().Count()!=Groups.Count) throw new Exception("Hay identificadores de grupo repetidos.");
        if(Groups.Select(g=>g.Name.Trim()).Distinct(StringComparer.CurrentCultureIgnoreCase).Count()!=Groups.Count) throw new Exception("Usa un nombre diferente para cada grupo.");
        foreach(var group in Groups) {
            if(string.IsNullOrWhiteSpace(group.Name)||group.Name.Length>40) throw new Exception("Cada grupo necesita un nombre de 1 a 40 caracteres.");
            if(group.Id.Length==0||group.Id.Any(c=>!char.IsAsciiLetterOrDigit(c))) throw new Exception("Identificador de grupo no válido.");
            if(group.Apps.Count>60) throw new Exception("Un grupo admite hasta 60 accesos.");
            _=ColorTranslator.FromHtml(group.Color);
            if(group.Apps.Any(a=>string.IsNullOrWhiteSpace(a.Name)||string.IsNullOrWhiteSpace(a.Target))) throw new Exception("Los accesos necesitan un nombre y un destino.");
        }
        HoverDelayMs=Math.Clamp(HoverDelayMs,80,1500);
    }
}
static class Program {
    public static readonly string Base=AppContext.BaseDirectory;
    public static readonly JsonSerializerOptions Json=new() { WriteIndented=true,PropertyNameCaseInsensitive=true };
    public static readonly string ConfigPath=Path.Combine(Base,"groups.json");
    public static readonly string Pipe="TaskbarGroups-"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Base+Environment.UserName))).Substring(0,20);
    public static Config Load(string? path=null) { path??=ConfigPath;if(!File.Exists(path))return new Config();var c=JsonSerializer.Deserialize<Config>(File.ReadAllText(path),Json)??throw new Exception("Configuración vacía."); c.Validate(); return c; }
    public static void Save(Config config,string? path=null) { config.Validate();path??=ConfigPath;var temp=path+".new"; File.WriteAllText(temp,JsonSerializer.Serialize(config,Json)); if(File.Exists(path)) File.Copy(path,path+".bak",true); File.Move(temp,path,true); }
    public static void Log(string text) { try { File.AppendAllText(Path.Combine(Base,"activity.log"),DateTime.Now.ToString("s")+" "+text+Environment.NewLine); } catch {} }
    [STAThread] static void Main(string[] args) {
        if(args.Contains("--config-test")){Environment.ExitCode=Verification.Run();return;}
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2); Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        if(args.Contains("--diagnose-hover")){NativePreviewBlocker.Diagnose();return;}
        if(args.Contains("--inspect-preview")){NativePreviewBlocker.InspectPreview();return;}
        bool test=args.Contains("--self-test");
        using var mutex=new Mutex(true,"Local\\"+Pipe+(test?"-test":""),out bool first);
        if(!first) { try { using var client=new NamedPipeClientStream(".",Pipe,PipeDirection.Out,PipeOptions.CurrentUserOnly); client.Connect(2000); using var w=new StreamWriter(client); w.WriteLine(args.Contains("--quit")?"quit":args.Contains("--edit")?"edit":args.Contains("--show")?"show:"+args.Last():"show:"); } catch(Exception e) { MessageBox.Show(e.Message,"Grupos de la barra"); } return; }
        if(args.Contains("--quit"))return;
        try { Application.Run(new Manager(test?Verification.DesktopConfig():Load(),test,args)); } catch(Exception e) { Environment.ExitCode=1;Log(e.ToString()); MessageBox.Show("No se pudo iniciar la utilidad.\n\n"+e.Message,"Grupos de la barra",MessageBoxButtons.OK,MessageBoxIcon.Error); }
    }
}
record TaskButton(string Id,string Name,Rectangle Bounds);

sealed class Manager : ApplicationContext {
    Config config;
    readonly Form dispatcher=new();
    readonly List<GroupWindow> windows=new();
    readonly Popup popup;
    readonly NotifyIcon tray;
    readonly NativePreviewBlocker previewBlocker;
    readonly System.Windows.Forms.Timer tick=new() {Interval=50};
    readonly CancellationTokenSource stop=new();
    volatile TaskButton[] buttons=Array.Empty<TaskButton>();
    Group[] scanGroups=Array.Empty<Group>();
    TaskButton? candidate;
    DateTime hoverStarted, lastInside;
    string? current;
    TaskButton? currentButton;
    readonly bool test;
    DateTime created=DateTime.UtcNow;
    bool editing,exiting;
    bool keyboardMode;
    public Manager(Config config,bool test,string[] args) {
        this.config=config; this.test=test; _=dispatcher.Handle;
        if(!test)Program.Log("Inicio 1.0.0; supresión de miniaturas="+config.SuppressWindowsPreview);
        popup=new Popup(this);
        previewBlocker=new NativePreviewBlocker(point=>!test&&ShouldSuppressPreview(point),()=>popup.Visible?popup.Bounds:null,point=>buttons.FirstOrDefault(b=>b.Bounds.Contains(point))?.Bounds,()=>this.config.Groups.Select(GroupWindow.Title).ToArray());
        tray=new NotifyIcon {Icon=AppIcon.Tray,Text="Grupos de la barra",Visible=!test};
        var menu=new ContextMenuStrip(); menu.Items.Add("Crear y editar grupos…",null,(_,_)=>Edit());
        foreach(var g in config.Groups) { var id=g.Id; menu.Items.Add("Abrir "+g.Name,null,(_,_)=>ShowGroup(id,true)); }
        menu.Items.Add(new ToolStripSeparator()); menu.Items.Add("Ver instrucciones",null,(_,_)=>Process.Start(new ProcessStartInfo(Path.Combine(Program.Base,"LEEME.txt")){UseShellExecute=true}));
        menu.Items.Add("Salir",null,(_,_)=>ExitThread()); tray.ContextMenuStrip=menu; tray.DoubleClick+=(_,_)=>Edit();
        Rebuild(); tick.Tick+=Tick; tick.Start();
        _=Task.Run(ScanLoop); if(!test) _=Task.Run(Listen);
        if(test) { var timer=new System.Windows.Forms.Timer {Interval=10000}; timer.Tick+=(_,_)=>{timer.Stop();RunSelfTest();timer.Dispose();}; timer.Start(); }
        else if(config.Groups.Count==0||args.Contains("--edit")) dispatcher.BeginInvoke(new Action(Edit));
        else if(args.Contains("--show")) dispatcher.BeginInvoke(new Action(()=>ShowGroup(args.Last(),true)));
    }
    void Rebuild() {
        popup.Hide(); current=null;currentButton=null; foreach(var window in windows) window.Dispose(); windows.Clear();
        Directory.CreateDirectory(Path.Combine(Program.Base,"icons"));
        Directory.CreateDirectory(Path.Combine(Program.Base,"Grupos"));
        foreach(var group in config.Groups) { using(var icon=Native.GroupIcon(group)) using(var file=File.Create(Native.IconPath(group))) icon.Save(file);
            var window=new GroupWindow(group,()=>ShowGroup(group.Id,true),()=>NativePreviewRequested(group.Id),ExitThread);windows.Add(window);window.Show();Native.RegisterTaskbarWindow(window.Handle);
            Editor.CreateShortcut(Path.Combine(Program.Base,"Grupos",group.Id+".lnk"),"--show "+group.Id,group); }
        scanGroups=config.Groups.ToArray(); buttons=Array.Empty<TaskButton>(); candidate=null;
        created=DateTime.UtcNow;
        tray.ContextMenuStrip?.Dispose();var menu=new ContextMenuStrip();menu.Items.Add("Crear y editar grupos…",null,(_,_)=>Edit());foreach(var g in config.Groups){var id=g.Id;menu.Items.Add("Abrir "+g.Name,null,(_,_)=>ShowGroup(id,true));}menu.Items.Add("Accesos de grupo para anclar…",null,(_,_)=>Process.Start(new ProcessStartInfo(Path.Combine(Program.Base,"Grupos")){UseShellExecute=true}));menu.Items.Add("Ver instrucciones",null,(_,_)=>Process.Start(new ProcessStartInfo(Path.Combine(Program.Base,"LEEME.txt")){UseShellExecute=true}));menu.Items.Add("Salir",null,(_,_)=>ExitThread());tray.ContextMenuStrip=menu;
    }
    async Task Listen() {
        while(!stop.IsCancellationRequested) try {
            using var pipe=new NamedPipeServerStream(Program.Pipe,PipeDirection.In,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
            await pipe.WaitForConnectionAsync(stop.Token); using var reader=new StreamReader(pipe); var message=await reader.ReadLineAsync(stop.Token);
            if(!stop.IsCancellationRequested) dispatcher.BeginInvoke(new Action(()=>{if(message=="quit"&&!editing)ExitThread();else if(message=="edit") Edit(); else if(message?.StartsWith("show:")==true) ShowGroup(message.Substring(5),true);}));
        } catch(OperationCanceledException) {break;} catch(Exception e) {Program.Log("IPC: "+e.Message);}
    }
    TaskButton[] Scan() {
        var result=new List<TaskButton>(); var groups=scanGroups; using var uia=new NativeUia();
        foreach(var bar in Native.Taskbars()) try {
            foreach(var element in uia.Elements(bar.Handle)) {
                var name=element.Name; var group=groups.FirstOrDefault(g=>name.Equals(GroupWindow.Title(g),StringComparison.OrdinalIgnoreCase)||name.StartsWith(GroupWindow.Title(g)+",",StringComparison.OrdinalIgnoreCase));
                if(group==null) continue; var r=element.Bounds; if(r.IsEmpty||r.Width<8||r.Height<8) continue;
                result.Add(new TaskButton(group.Id,name,r));
            }
        } catch(ElementNotAvailableException) {} catch(Exception e) {Program.Log("Exploración de la barra: "+e.Message);}
        try{foreach(var icon in NativeTaskbar.FindIcons(groups))if(!result.Any(b=>b.Id==icon.Id&&b.Bounds.IntersectsWith(icon.Bounds)))result.Add(icon);}catch(Exception e){Program.Log("Iconos de grupo: "+e.Message);}
        return result.ToArray();
    }
    async Task ScanLoop() {
        while(!stop.IsCancellationRequested) { try { buttons=Scan(); await Task.Delay(1000,stop.Token); } catch(OperationCanceledException) { break; } catch(Exception e) {Program.Log(e.Message);try{await Task.Delay(1000,stop.Token);}catch(OperationCanceledException){break;}} }
    }
    void Tick(object? sender,EventArgs args) {
        TrackPointer(Cursor.Position,DateTime.UtcNow);
    }
    void TrackPointer(Point point,DateTime now) {
        if(editing) return; var found=buttons.FirstOrDefault(b=>b.Bounds.Contains(point));
        if(!test&&previewBlocker.ContextMenuActive){popup.Hide();current=null;currentButton=null;candidate=null;return;}
        if(!test)previewBlocker.SuppressAt(point);
        if(found!=null) {
            lastInside=now;
            if(candidate?.Id!=found.Id||candidate.Bounds!=found.Bounds) {candidate=found;hoverStarted=now;}
            else if((now-hoverStarted).TotalMilliseconds>=config.HoverDelayMs&&(current!=found.Id||currentButton?.Bounds!=found.Bounds)) ShowGroup(found.Id,false,found,point);
        } else {
            candidate=null;
            var active=currentButton;
            if(popup.Visible&&((keyboardMode&&popup.ContainsFocus)||popup.Bounds.Contains(point)||(active!=null&&Bridge(active.Bounds,popup.Bounds).Contains(point)))) lastInside=now;
            else if(popup.Visible&&(now-lastInside).TotalMilliseconds>350) {popup.Hide();current=null;currentButton=null;}
        }
        if(popup.Visible&&NativeKeyEscape()) {popup.Hide();current=null;currentButton=null;}
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
    static bool NativeKeyEscape()=>(GetAsyncKeyState(27)&0x8000)!=0;
    static Rectangle Bridge(Rectangle button,Rectangle panel) {
        if(panel.Bottom<=button.Top) return Rectangle.FromLTRB(button.Left,panel.Bottom-2,button.Right,button.Bottom);
        if(panel.Top>=button.Bottom) return Rectangle.FromLTRB(button.Left,button.Top,button.Right,panel.Top+2);
        if(panel.Right<=button.Left) return Rectangle.FromLTRB(panel.Right-2,button.Top,button.Right,button.Bottom);
        return Rectangle.FromLTRB(button.Left,button.Top,panel.Left+2,button.Bottom);
    }
    TaskButton? ResolveButton(string id,Point point,TaskButton? origin=null){
        if(origin!=null&&origin.Id==id)return origin;
        var matching=buttons.Where(b=>b.Id==id).ToArray();var underPointer=matching.FirstOrDefault(b=>b.Bounds.Contains(point));if(underPointer!=null)return underPointer;
        var screen=Screen.FromPoint(point);return matching.FirstOrDefault(b=>screen.Bounds.IntersectsWith(b.Bounds));
    }
    bool ShouldSuppressPreview(Point point){
        if(editing||!config.SuppressWindowsPreview)return false;
        if(buttons.Any(b=>b.Bounds.Contains(point)))return true;
        return popup.Visible&&(popup.Bounds.Contains(point)||(currentButton!=null&&Bridge(currentButton.Bounds,popup.Bounds).Contains(point)));
    }
    public void ShowGroup(string id,bool activate,TaskButton? origin=null,Point? pointer=null) {
        keyboardMode=activate;
        var group=config.Groups.FirstOrDefault(g=>g.Id==id)??config.Groups.FirstOrDefault(); if(group==null){Edit();return;}
        var point=pointer??Cursor.Position;var button=ResolveButton(group.Id,point,origin);
        var screen=button!=null?Screen.FromRectangle(button.Bounds):Screen.FromPoint(point);var anchor=button?.Bounds??new Rectangle(point.X,screen.WorkingArea.Bottom,44,44);
        popup.PrepareMonitor(screen);popup.Render(group); popup.Place(anchor,screen); current=group.Id;currentButton=button; lastInside=DateTime.UtcNow; popup.ShowFromTaskbar(!test);
        if(activate) {popup.Activate();popup.FocusFirst();}
    }
    void NativePreviewRequested(string id,Point? pointer=null){
        var point=pointer??Cursor.Position;var bar=Native.Taskbars().FirstOrDefault(b=>b.Bounds.Contains(point));if(bar.Handle==IntPtr.Zero||editing)return;
        var detected=buttons.FirstOrDefault(b=>b.Bounds.Contains(point));if(detected!=null&&detected.Id!=id)return;
        int width=48;var record=detected??new TaskButton(id,"Vista previa nativa",new Rectangle(point.X-width/2,bar.Bounds.Top,width,bar.Bounds.Height));
        buttons=buttons.Where(b=>b.Id!=id||!b.Bounds.IntersectsWith(bar.Bounds)).Append(record).ToArray();
        if(current!=id||currentButton?.Bounds!=record.Bounds)ShowGroup(id,false,record,point);else lastInside=DateTime.UtcNow;
        if(!test)previewBlocker.SuppressAt(point);
    }
    public void Launch(AppEntry app) { popup.Hide();current=null; try {app.Launch();Program.Log("Abierto: "+app.Name);} catch(Exception e) {MessageBox.Show("No se pudo abrir "+app.Name+". Puedes corregir el acceso desde Editar grupos.\n\n"+e.Message,"Grupos de la barra");} }
    public void Edit() {
        if(editing) return; editing=true; popup.Hide();current=null;
        try { using var editor=new Editor(config); if(editor.ShowDialog()==DialogResult.OK) {config=editor.Result;Program.Save(config);Rebuild();} } catch(Exception e) {MessageBox.Show(e.Message,"No se pudieron guardar los grupos");} finally {editing=false;}
    }
    async void RunSelfTest() {
        try {
            var diagnostic=new List<string>();
            using var uia=new NativeUia(); foreach(var bar in Native.Taskbars()) {
                diagnostic.Add("Barra: "+bar.Handle+" "+bar.Bounds);
                try {foreach(var element in uia.Elements(bar.Handle))diagnostic.Add(element.Type+" | "+element.Name+" | "+element.Bounds);}catch(Exception e){diagnostic.Add(e.ToString());}
            }
            diagnostic.Add("Ventanas propias: "+string.Join("; ",windows.Select(w=>w.Text+" "+w.Handle+" "+w.Visible+" "+w.WindowState+" visible Win32="+Native.IsWindowVisible(w.Handle))));
            int capture=0;foreach(var bar in Native.Taskbars()){using var bitmap=new Bitmap(bar.Bounds.Width,bar.Bounds.Height);using(var g=Graphics.FromImage(bitmap))g.CopyFromScreen(bar.Bounds.Location,Point.Empty,bar.Bounds.Size);bitmap.Save(Path.Combine(Program.Base,"native-taskbar-"+capture+++".png"));}
            File.WriteAllLines(Path.Combine(Program.Base,"diagnostic.txt"),diagnostic);
            buttons=Scan(); var report=new List<string> {"Modo: Botones nativos","Grupos: "+config.Groups.Count,"Accesos: "+config.Groups.Sum(g=>g.Apps.Count),"Botones detectados: "+buttons.Length};
            foreach(var g in config.Groups) {var b=buttons.FirstOrDefault(b=>b.Id==g.Id);report.Add(g.Name+": "+(b==null?"NO DETECTADO":b.Name+" "+b.Bounds));}
            if(buttons.Select(b=>b.Id).Distinct().Count()<config.Groups.Count||windows.Count!=config.Groups.Count||windows.Any(w=>!w.ShowInTaskbar||!Native.IsWindowVisible(w.Handle))) throw new Exception(string.Join(Environment.NewLine,report));
            foreach(var app in config.Groups.SelectMany(g=>g.Apps)) {
                if(!app.Target.Contains(':')&&!File.Exists(app.ResolvedTarget)) throw new Exception("Destino ausente: "+app.Name);
                using var icon=Native.GetIcon(string.IsNullOrWhiteSpace(app.IconPath)?app.ResolvedTarget:app.ResolvedIconPath); report.Add("Icono "+app.Name+": "+(icon==null?"alternativo":"nativo"));
            }
            popup.Hide();current=null;candidate=null;var sample=buttons.First(b=>b.Id==config.Groups.First().Id);var center=new Point(sample.Bounds.Left+sample.Bounds.Width/2,sample.Bounds.Top+sample.Bounds.Height/2);var now=DateTime.UtcNow;
            report.Add("Se verifica la lógica usando las posiciones de los botones reales; el puntero físico no se modifica.");
            popup.Hide();current=null;candidate=null;now=DateTime.UtcNow;
            TrackPointer(center,now);if(popup.Visible)throw new Exception("El panel se abrió antes del retraso de hover.");TrackPointer(center,now.AddMilliseconds(config.HoverDelayMs+50));if(!popup.Visible||current!=sample.Id)throw new Exception("El hover no abrió el grupo.");report.Add("Hover sobre el icono abre el panel: PASS");
            TrackPointer(new Point(-50000,-50000),now.AddSeconds(2));if(popup.Visible)throw new Exception("El panel no se cerró al salir del área.");report.Add("Salir del icono y del panel lo cierra: PASS");ShowGroup(sample.Id,false);
            var sourceButtons=buttons.Where(b=>b.Id==sample.Id).ToArray();
            foreach(var source in sourceButtons){var point=new Point(source.Bounds.Left+source.Bounds.Width/2,source.Bounds.Top+source.Bounds.Height/2);var expected=Screen.FromRectangle(source.Bounds);var start=DateTime.UtcNow;TrackPointer(point,start);TrackPointer(point,start.AddMilliseconds(config.HoverDelayMs+50));if(!popup.Visible||currentButton?.Bounds!=source.Bounds||Screen.FromRectangle(popup.Bounds).DeviceName!=expected.DeviceName)throw new Exception("El panel se abrió en la pantalla incorrecta: "+expected.DeviceName);NativePreviewRequested(source.Id,point);if(currentButton?.Bounds!=source.Bounds||Screen.FromRectangle(popup.Bounds).DeviceName!=expected.DeviceName)throw new Exception("Una solicitud de miniatura cambió el panel de pantalla.");if(!ShouldSuppressPreview(point))throw new Exception("La supresión no reconoce el grupo de esta pantalla.");report.Add("Mismo grupo por hover y callback en "+expected.DeviceName+": PASS");}
            if(ShouldSuppressPreview(new Point(-50000,-50000)))throw new Exception("La supresión afecta fuera de los grupos.");var previewOption=config.SuppressWindowsPreview;config.SuppressWindowsPreview=false;if(sourceButtons.Any(b=>ShouldSuppressPreview(new Point(b.Bounds.Left+5,b.Bounds.Top+5))))throw new Exception("La opción de miniatura no se puede desactivar.");config.SuppressWindowsPreview=previewOption;
            if(!previewBlocker.HookInstalled)throw new Exception("No se instaló el observador de miniaturas de Explorer.");report.Add("Supresión: grupos en ambas pantallas, opción y exclusión de otras apps: PASS");
            var launcherRect=new Rectangle(100,100,320,180);var capturedPreview=new Rectangle(140,120,240,140);
            if(!NativePreviewBlocker.MatchesPreview("Xaml_WindowedPopupClass",true,capturedPreview,launcherRect,false))throw new Exception("El PopupHost de la captura no se reconoce como miniatura superpuesta.");
            if(NativePreviewBlocker.MatchesPreview("Xaml_WindowedPopupClass",true,new Rectangle(1000,1000,250,150),launcherRect,false)||NativePreviewBlocker.MatchesPreview("Xaml_WindowedPopupClass",false,capturedPreview,launcherRect,false)||NativePreviewBlocker.MatchesPreview("Xaml_WindowedPopupClass",true,capturedPreview,launcherRect,true)||NativePreviewBlocker.MatchesPreview("Xaml_WindowedPopupClass",true,capturedPreview,null,false))throw new Exception("La regla XAML afecta a otro panel o a un menú.");report.Add("PopupHost XAML de la captura: reconocido; menús y otras ventanas excluidos: PASS");
            var actualHost=new Rectangle(-1920,0,1920,1080);var actualPanel=new Rectangle(-1000,800,320,200);
            if(!NativePreviewBlocker.MatchesPreview("XamlExplorerHostIslandWindow",true,actualHost,actualPanel,false,true,true))throw new Exception("No se reconoce el contenedor de pantalla completa capturado en el equipo.");
            if(NativePreviewBlocker.MatchesPreview("XamlExplorerHostIslandWindow",true,actualHost,actualPanel,false,false,true)||NativePreviewBlocker.MatchesPreview("XamlExplorerHostIslandWindow",true,actualHost,actualPanel,false,true,false)||NativePreviewBlocker.MatchesPreview("XamlExplorerHostIslandWindow",true,actualHost,actualPanel,true,true,true))throw new Exception("Se confunde un panel ajeno o un menú con una miniatura propia.");
            report.Add("Host real de Windows 11: tamaño de pantalla; identidad Taskbar + grupo; exclusión de otros paneles: PASS");
            using(var fixture=new Form{ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new Point(-31000,-31000)}){fixture.Show();Application.DoEvents();if(!NativePreviewBlocker.ShowWindowAsync(fixture.Handle,0))throw new Exception("Windows rechazó ocultar la ventana de prueba.");await Task.Delay(120);if(Native.IsWindowVisible(fixture.Handle))throw new Exception("No se ocultó la ventana de prueba.");fixture.Close();}report.Add("Ocultar una ventana de prueba por la API de Windows: PASS");
            ShowGroup(sample.Id,false,sample,new Point(sample.Bounds.Left+10,sample.Bounds.Top+10));
            using(var bitmap=new Bitmap(popup.Width,popup.Height)) {popup.DrawToBitmap(bitmap,new Rectangle(Point.Empty,popup.Size)); bitmap.Save(Path.Combine(Program.Base,"preview-group.png"));}
            foreach(int count in new[]{0,1,60}){
                var variant=new Group{Name="Un nombre de grupo largo para comprobar",Color="#6EADFF",Apps=Enumerable.Range(0,count).Select(i=>new AppEntry{Name="Aplicación con un nombre largo "+(i+1),Target="missing-test-icon"}).ToList()};
                popup.Render(variant);Application.DoEvents();var body=popup.Controls.OfType<Panel>().Single();
                if(popup.Width<300||popup.Height>Screen.FromHandle(popup.Handle).WorkingArea.Height)throw new Exception("El panel se corta con "+count+" accesos.");
                if(count==60){var last=body.Controls.OfType<AppTile>().Last();body.ScrollControlIntoView(last);Application.DoEvents();if(!body.ClientRectangle.IntersectsWith(last.Bounds))throw new Exception("No se puede alcanzar el último acceso con desplazamiento.");body.AutoScrollPosition=Point.Empty;}
                using var bitmap=new Bitmap(popup.Width,popup.Height);popup.DrawToBitmap(bitmap,new Rectangle(Point.Empty,popup.Size));bitmap.Save(Path.Combine(Program.Base,"preview-"+count+"-apps.png"));
            }
            report.Add("Panel vacío, un acceso, nombres largos y 60 accesos desplazables: PASS");
            report.Add("Panel emergente visible: "+popup.Visible); report.Add("PASS");File.WriteAllLines(Path.Combine(Program.Base,"self-test.txt"),report);
            using(var editor=new Editor(config)){_=editor.Handle;editor.Show();Application.DoEvents();using var bitmap=new Bitmap(editor.Width,editor.Height);editor.DrawToBitmap(bitmap,new Rectangle(Point.Empty,editor.Size));bitmap.Save(Path.Combine(Program.Base,"preview-editor.png"));editor.VerifyEditing();editor.Close();}
            var clone=JsonSerializer.Deserialize<Config>(JsonSerializer.Serialize(config,Program.Json),Program.Json)!;var added=new Group{Name="Prueba editable",Symbol="folder"};clone.Groups.Add(added);clone.Validate();added.Name="Renombrado";added.Apps.Add(new AppEntry{Name="Explorador",Target="explorer.exe"});clone.Validate();clone.Groups.Remove(added);clone.Validate();File.AppendAllText(Path.Combine(Program.Base,"self-test.txt"),"Crear, renombrar, añadir acceso y eliminar: PASS\n");
            var testPath=Path.Combine(Program.Base,"self-test-config.json");Program.Save(clone,testPath);clone.HoverDelayMs=340;Program.Save(clone,testPath);var restored=JsonSerializer.Deserialize<Config>(File.ReadAllText(testPath),Program.Json)!;restored.Validate();if(restored.HoverDelayMs!=340||!File.Exists(testPath+".bak"))throw new Exception("No se guardó la configuración o la copia anterior.");File.AppendAllText(Path.Combine(Program.Base,"self-test.txt"),"Guardar, volver a leer y conservar copia: PASS\n");File.Delete(testPath);File.Delete(testPath+".bak");
        } catch(Exception e) {Environment.ExitCode=1;File.WriteAllText(Path.Combine(Program.Base,"self-test.txt"),"FAIL\n"+e);}
        ExitThread();
    }
    protected override void ExitThreadCore() {if(exiting)return;exiting=true;stop.Cancel();tick.Stop();tick.Dispose();previewBlocker.Dispose();tray.Visible=false;tray.Dispose();popup.Dispose();foreach(var w in windows)w.Dispose();dispatcher.Dispose();base.ExitThreadCore();}
}
sealed class GroupWindow : Form {
    readonly Action open;
    readonly Action hover;
    readonly Action close;
    readonly Group group;
    bool ready;
    public int NativePreviewRequests {get;private set;}
    public static string Title(Group group)=>"Grupos · "+group.Name;
    public GroupWindow(Group group,Action open,Action hover,Action close) {
        this.close=close;this.group=group;this.hover=hover;this.open=open;Text=Title(group);Icon=Native.GroupIcon(group);ShowInTaskbar=true;Size=new Size(320,200);StartPosition=FormStartPosition.Manual;Location=Screen.PrimaryScreen!.WorkingArea.Location;WindowState=FormWindowState.Minimized;
        _=Handle;Native.SetWindowIdentity(Handle,group);Native.EnableNativePreview(Handle);Native.SendMessage(Handle,0x80,new IntPtr(1),Icon.Handle);Native.SendMessage(Handle,0x80,IntPtr.Zero,Icon.Handle);ready=true;
        Shown+=(_,_)=>WindowState=FormWindowState.Minimized;
    }
    protected override bool ShowWithoutActivation=>true;
    protected override CreateParams CreateParams{get{var cp=base.CreateParams;cp.ExStyle|=0x40000;return cp;}}
    protected override void WndProc(ref Message m) {
        if(ready&&m.Msg==0x323){NativePreviewRequests++;int width=Math.Clamp((int)(((long)m.LParam>>16)&0xffff),1,280),height=Math.Clamp((int)((long)m.LParam&0xffff),1,100);using var bitmap=new Bitmap(width,height);using(var g=Graphics.FromImage(bitmap)){g.Clear(Color.FromArgb(28,31,47));using var font=new Font("Segoe UI",12,FontStyle.Bold);using var brush=new SolidBrush(ColorTranslator.FromHtml(group.Color));g.DrawString(group.Name,font,brush,8,8);}Native.SetNativePreview(Handle,bitmap);BeginInvoke(hover);return;}
        if(ready&&m.Msg==0x112&&((long)m.WParam&0xFFF0)==0xF120) {open();return;}
        if(ready&&m.Msg==0x112&&((long)m.WParam&0xFFF0)==0xF060) {BeginInvoke(close);return;}
        if(ready&&m.Msg==0x10){BeginInvoke(close);return;}
        base.WndProc(ref m);
        if(ready&&m.Msg==0x5&&(long)m.WParam==0&&WindowState!=FormWindowState.Minimized) BeginInvoke(new Action(()=>{WindowState=FormWindowState.Minimized;open();}));
    }
    protected override void Dispose(bool disposing) {var icon=Icon;base.Dispose(disposing);if(disposing)icon?.Dispose();}
}

sealed partial class Popup : Form {
    readonly Manager manager;
    readonly List<Image> images=new();
    readonly ToolTip hints=new(){ShowAlways=true,InitialDelay=600};
    PopupFrame? frame;
    string? renderedGroupId;
    Size availableSize=new(1920,1080);
    public Popup(Manager manager) {this.manager=manager;Text="Accesos del grupo";FormBorderStyle=FormBorderStyle.None;StartPosition=FormStartPosition.Manual;ShowInTaskbar=false;TopMost=true;BackColor=Color.FromArgb(28,31,47);ForeColor=Color.White;Font=new Font("Segoe UI",10);KeyPreview=true;AutoScaleMode=AutoScaleMode.None;DoubleBuffered=true;SetStyle(ControlStyles.ResizeRedraw,true);InitializeMotion();}
    protected override bool ShowWithoutActivation=>true;
    protected override CreateParams CreateParams {get {var cp=base.CreateParams;cp.ClassStyle&=~0x20000;cp.ExStyle|=0x02000080;return cp;}}
    public void PrepareMonitor(Screen screen){FinishEntrance();availableSize=screen.WorkingArea.Size;_=Handle;if(Screen.FromHandle(Handle).DeviceName!=screen.DeviceName){Hide();Location=new Point(screen.WorkingArea.Left+20,screen.WorkingArea.Top+20);}}
    public void Render(Group group) {
        // Build the next group while hidden, before starting its own entrance.
        if(Visible&&renderedGroupId!=group.Id)Hide();
        FinishEntrance();
        renderedGroupId=group.Id;
        SuspendLayout();hints.RemoveAll();foreach(Control c in Controls.Cast<Control>().ToArray())c.Dispose();Controls.Clear();foreach(var i in images)i.Dispose();images.Clear();
        int scale=DeviceDpi; int S(int n)=>(int)Math.Round(n*scale/96.0);
        int columns=Math.Clamp(group.Apps.Count<=4?group.Apps.Count:(int)Math.Ceiling(Math.Sqrt(group.Apps.Count*1.5)),1,6);
        columns=Math.Min(columns,Math.Max(1,(availableSize.Width-S(56))/S(94)));
        int rows=Math.Max(1,(int)Math.Ceiling(group.Apps.Count/(double)columns));
        int width=Math.Min(Math.Max(S(310),S(32+columns*94)),availableSize.Width-S(16));
        int visibleRows=Math.Min(rows,Math.Max(1,Math.Min(5,(availableSize.Height-S(94))/S(94))));
        int height=Math.Min(S(70+visibleRows*94),availableSize.Height-S(24));
        if(rows*S(94)>height-S(70))width=Math.Min(width+SystemInformation.VerticalScrollBarWidth,availableSize.Width-S(16));
        ClientSize=new Size(width,height);
        var heading=new Label {Text=group.Name,AutoEllipsis=true,UseMnemonic=false,Location=new Point(S(18),S(16)),Size=new Size(Width-S(36),S(28)),ForeColor=ColorTranslator.FromHtml(group.Color),Font=new Font("Segoe UI",12,FontStyle.Bold)};Controls.Add(heading);hints.SetToolTip(heading,group.Name);
        var body=new AppPanel {Location=new Point(S(10),S(54)),Size=new Size(Width-S(20),Height-S(70)),AutoScroll=true,BackColor=BackColor};body.Scroll+=(_,_)=>FinishEntrance();Controls.Add(body);
        bool scroll=rows*S(94)>body.Height;int bodyWidth=body.ClientSize.Width-(scroll?SystemInformation.VerticalScrollBarWidth:0);
        for(int index=0;index<group.Apps.Count;index++) {var app=group.Apps[index];var image=Native.GetIcon(string.IsNullOrWhiteSpace(app.IconPath)?app.ResolvedTarget:app.ResolvedIconPath);if(image!=null)images.Add(image);
            int row=index/columns,rowCount=Math.Min(columns,group.Apps.Count-row*columns);int left=(bodyWidth-rowCount*S(94))/2;
            var tile=new AppTile(app.Name,image,ColorTranslator.FromHtml(group.Color)) {Location=new Point(Math.Max(0,left)+(index%columns)*S(94)+S(3),row*S(94)),Size=new Size(S(88),S(88)),BackColor=BackColor,ForeColor=ForeColor,TabIndex=index};tile.Click+=(_,_)=>manager.Launch(app);body.Controls.Add(tile);hints.SetToolTip(tile,app.Name);}
        if(group.Apps.Count==0) body.Controls.Add(new Label {Text="Este grupo aún no tiene accesos.\nAñade aplicaciones desde el menú de la bandeja.",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleCenter,ForeColor=Color.FromArgb(181,188,207)});
        ResumeLayout();Invalidate(true);
    }
    public void Place(Rectangle anchor,Screen screen) {
        FinishEntrance();var area=screen.WorkingArea;
        int x=anchor.Left+(anchor.Width-Width)/2, y=anchor.Top-Height-8;
        if(anchor.Top<area.Top) y=anchor.Bottom+8;
        else if(anchor.Left<area.Left){x=anchor.Right+8;y=anchor.Top+(anchor.Height-Height)/2;}
        else if(anchor.Right>area.Right){x=anchor.Left-Width-8;y=anchor.Top+(anchor.Height-Height)/2;}
        Location=new Point(Math.Clamp(x,area.Left+4,Math.Max(area.Left+4,area.Right-Width-4)),Math.Clamp(y,area.Top+4,Math.Max(area.Top+4,area.Bottom-Height-4)));
        ConfigureEntrance(anchor,screen);
    }
    public void FocusFirst(){Controls.OfType<Panel>().SelectMany(p=>p.Controls.OfType<AppTile>()).FirstOrDefault()?.Focus();}
    GraphicsPath RoundedPath(RectangleF bounds) {
        const float cornerRadius=8f; // Windows 11 window and flyout radius at 96 DPI.
        float diameter=Math.Min(2*cornerRadius*DeviceDpi/96f,Math.Min(bounds.Width,bounds.Height));
        var path=new GraphicsPath();
        path.AddArc(bounds.Left,bounds.Top,diameter,diameter,180,90);
        path.AddArc(bounds.Right-diameter,bounds.Top,diameter,diameter,270,90);
        path.AddArc(bounds.Right-diameter,bounds.Bottom-diameter,diameter,diameter,0,90);
        path.AddArc(bounds.Left,bounds.Bottom-diameter,diameter,diameter,90,90);path.CloseFigure();return path;
    }
    void UpdateShape() {
        ApplyShape(Location);frame?.Render(entranceClip.HasValue);Invalidate();
    }
    void ApplyShape(Point location) {
        if(ClientSize.Width<2||ClientSize.Height<2)return;
        // Keep opaque pixels inside the outline; the alpha frame draws the edge.
        using var path=RoundedPath(new RectangleF(1,1,ClientSize.Width-2,ClientSize.Height-2));
        var next=new Region(path);Rectangle? clip=EntranceClip(location);if(clip.HasValue)next.Intersect(clip.Value);
        var previous=Region;Region=next;previous?.Dispose();frame?.Clip(clip);
    }
    protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);frame=new PopupFrame(this,RoundedPath);UpdateShape();}
    protected override void OnHandleDestroyed(EventArgs e){StopEntrance();frame?.Dispose();frame=null;base.OnHandleDestroyed(e);}
    protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);UpdateShape();}
    protected override void OnDpiChanged(DpiChangedEventArgs e){base.OnDpiChanged(e);UpdateShape();}
    protected override void OnLocationChanged(EventArgs e){base.OnLocationChanged(e);frame?.Move();}
    // WinForms finishes setting the native window order after VisibleChanged.
    // Raise the content and outline together after that operation completes.
    protected override void SetVisibleCore(bool visible){base.SetVisibleCore(visible);frame?.Show(visible);if(!visible)FinishEntrance();}
    protected override void OnActivated(EventArgs e){base.OnActivated(e);frame?.Show(Visible);}
    public new void DrawToBitmap(Bitmap bitmap,Rectangle bounds){base.DrawToBitmap(bitmap,bounds);frame?.DrawToBitmap(bitmap,bounds);}
    protected override void Dispose(bool disposing){if(disposing){StopEntrance();entranceTimer.Dispose();}base.Dispose(disposing);if(disposing){hints.Dispose();foreach(var i in images)i.Dispose();images.Clear();}}
}
// The tile gaps must be painted in the same buffered pass as their background
// when the bottom of the scrolling panel is revealed by the entrance animation.
sealed class AppPanel : Panel {
    public AppPanel(){DoubleBuffered=true;SetStyle(ControlStyles.AllPaintingInWmPaint|ControlStyles.ResizeRedraw,true);}
}
sealed class AppTile : Control {
    readonly Image? image; readonly Color accent; bool hover;
    public AppTile(string name,Image? image,Color accent){Text=name;Font=new Font("Segoe UI",8.5f);this.image=image;this.accent=accent;TabStop=true;Cursor=Cursors.Hand;AccessibleRole=AccessibleRole.PushButton;AccessibleName=name;SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);}
    protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);} protected override void OnMouseLeave(EventArgs e){hover=false;Invalidate();base.OnMouseLeave(e);}
    protected override void OnGotFocus(EventArgs e){Invalidate();base.OnGotFocus(e);} protected override void OnLostFocus(EventArgs e){Invalidate();base.OnLostFocus(e);}
    protected override void OnMouseDown(MouseEventArgs e){if(e.Button==MouseButtons.Left)Focus();base.OnMouseDown(e);}
    protected override void OnKeyDown(KeyEventArgs e){if(e.KeyCode is Keys.Enter or Keys.Space){OnClick(EventArgs.Empty);e.Handled=true;}base.OnKeyDown(e);}
    protected override void OnPaint(PaintEventArgs e){var g=e.Graphics;g.Clear(hover||Focused?Color.FromArgb(47,53,77):BackColor);int S(int n)=>(int)Math.Round(n*DeviceDpi/96f);int icon=S(34);int top=S(9);
        g.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;g.PixelOffsetMode=System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        if(image!=null){float ratio=Math.Min(icon/(float)image.Width,icon/(float)image.Height);int iw=(int)(image.Width*ratio),ih=(int)(image.Height*ratio);g.DrawImage(image,(Width-iw)/2,top+(icon-ih)/2,iw,ih);}else{using var brush=new SolidBrush(accent);g.FillEllipse(brush,(Width-icon)/2,top,icon,icon);using var font=new Font("Segoe UI",14,FontStyle.Bold);TextRenderer.DrawText(g,Text.Length>0?Text.Substring(0,1):"?",font,new Rectangle((Width-icon)/2,top,icon,icon),Color.FromArgb(24,28,43),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);}
        TextRenderer.DrawText(g,Text,Font,new Rectangle(S(3),top+icon+S(7),Width-S(6),Font.Height*2),ForeColor,TextFormatFlags.HorizontalCenter|TextFormatFlags.WordBreak|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix|TextFormatFlags.TextBoxControl);
        if(Focused){using var pen=new Pen(accent);g.DrawRectangle(pen,0,0,Width-1,Height-1);}
    }
}
