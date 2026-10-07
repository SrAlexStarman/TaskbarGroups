using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;

namespace TaskbarGroups;

sealed class Editor : Form {
    public Config Result {get;}
    readonly ListBox groups=new(),apps=new(),available=new();
    readonly TextBox name=new(),search=new();
    readonly ComboBox symbol=new();
    readonly Button color=new();
    readonly NumericUpDown delay=new();
    readonly CheckBox startup=new();
    readonly CheckBox suppressPreview=new();
    readonly List<AppEntry> catalog;
    bool binding;
    Group? Selected=>groups.SelectedItem as Group;
    readonly string startupPath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup),"Grupos de la barra.lnk");
    public Editor(Config config) {
        Result=JsonSerializer.Deserialize<Config>(JsonSerializer.Serialize(config,Program.Json),Program.Json)!;
        Text="Crear y editar grupos";ClientSize=new Size(940,634);FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;StartPosition=FormStartPosition.CenterScreen;Font=new Font("Segoe UI",10);AutoScaleMode=AutoScaleMode.Dpi;AutoScaleDimensions=new SizeF(96,96);BackColor=Color.FromArgb(245,247,251);Icon=SystemIcons.Application;
        LabelAt("TUS GRUPOS",20,18,180,24,true);
        groups.SetBounds(20,50,180,390);groups.IntegralHeight=false;groups.DrawMode=DrawMode.OwnerDrawFixed;groups.ItemHeight=22;groups.DrawItem+=(_,e)=>{e.DrawBackground();if(e.Index>=0&&e.Index<groups.Items.Count)TextRenderer.DrawText(e.Graphics,groups.Items[e.Index].ToString(),groups.Font,e.Bounds,e.ForeColor,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);e.DrawFocusRectangle();};Controls.Add(groups);groups.SelectedIndexChanged+=(_,_)=>BindGroup();
        ButtonAt("+ Crear grupo",20,452,180,34,()=>AddGroup());ButtonAt("Eliminar",20,494,86,32,()=>DeleteGroup());ButtonAt("↑",114,494,39,32,()=>MoveGroup(-1));ButtonAt("↓",161,494,39,32,()=>MoveGroup(1));
        LabelAt("Nombre",225,18,240,24);name.SetBounds(225,46,250,30);Controls.Add(name);name.MaxLength=40;
        name.TextChanged+=(_,_)=>{if(!binding&&Selected!=null){Selected.Name=name.Text;groups.Invalidate();}};
        LabelAt("Color",490,18,110,24);color.SetBounds(490,46,112,30);color.Text="Elegir color";Controls.Add(color);color.Click+=(_,_)=>{if(Selected==null)return;using var dialog=new ColorDialog {Color=ColorTranslator.FromHtml(Selected.Color),FullOpen=true};if(dialog.ShowDialog(this)==DialogResult.OK){Selected.Color=ColorTranslator.ToHtml(dialog.Color);color.BackColor=dialog.Color;}};
        LabelAt("Símbolo",620,18,275,24);symbol.SetBounds(620,46,275,30);symbol.DropDownStyle=ComboBoxStyle.DropDownList;
        symbol.Items.AddRange(new object[]{new Symbol("work","Maletín"),new Symbol("games","Mando de juegos"),new Symbol("ai","Chip / IA"),new Symbol("folder","Carpeta"),new Symbol("letter","Inicial del grupo")});Controls.Add(symbol);
        symbol.SelectedIndexChanged+=(_,_)=>{if(!binding&&Selected!=null&&symbol.SelectedItem is Symbol s)Selected.Symbol=s.Id;};
        LabelAt("ACCESOS DEL GRUPO",225,101,285,24,true);apps.SetBounds(225,132,285,295);apps.IntegralHeight=false;apps.AllowDrop=true;Controls.Add(apps);
        apps.DoubleClick+=(_,_)=>EditApp();apps.DragEnter+=(_,e)=>e.Effect=e.Data?.GetDataPresent(DataFormats.FileDrop)==true?DragDropEffects.Copy:DragDropEffects.None;
        apps.DragDrop+=(_,e)=>{if(e.Data?.GetData(DataFormats.FileDrop) is string[] paths)foreach(var path in paths)AddFile(path);};
        ButtonAt("Quitar",225,439,80,32,()=>RemoveApp());ButtonAt("↑",312,439,38,32,()=>MoveApp(-1));ButtonAt("↓",357,439,38,32,()=>MoveApp(1));ButtonAt("Editar acceso",402,439,108,32,()=>EditApp());
        LabelAt("APLICACIONES DISPONIBLES",570,101,325,24,true);search.SetBounds(570,132,325,30);search.PlaceholderText="Buscar una aplicación…";Controls.Add(search);search.TextChanged+=(_,_)=>FilterCatalog();
        available.SetBounds(570,173,325,254);available.IntegralHeight=false;Controls.Add(available);available.DoubleClick+=(_,_)=>AddAvailable();
        ButtonAt("←",522,260,36,40,()=>AddAvailable());ButtonAt("Añadir seleccionada",570,439,160,32,()=>AddAvailable());ButtonAt("Buscar archivo…",738,439,157,32,()=>Browse());
        LabelAt("Arrastra accesos o archivos a la lista. Usa Editar acceso para cambiar su nombre o destino.",225,482,670,40);
        LabelAt("Espera al pasar el ratón (ms)",225,537,240,26);delay.SetBounds(470,534,80,29);delay.Minimum=80;delay.Maximum=1500;delay.Increment=20;delay.Value=Result.HoverDelayMs;Controls.Add(delay);
        startup.SetBounds(570,534,325,30);startup.Text="Iniciar con Windows";startup.Checked=File.Exists(startupPath);Controls.Add(startup);
        suppressPreview.SetBounds(225,578,430,32);suppressPreview.Text="Ocultar miniatura de Windows en estos grupos";suppressPreview.Checked=Result.SuppressWindowsPreview;Controls.Add(suppressPreview);
        var cancel=ButtonAt("Cancelar",665,578,108,35,()=>{DialogResult=DialogResult.Cancel;Close();});CancelButton=cancel;
        var save=ButtonAt("Guardar cambios",785,578,130,35,()=>Save());save.BackColor=Color.FromArgb(46,103,209);save.ForeColor=Color.White;save.FlatStyle=FlatStyle.Flat;
        catalog=LoadCatalog();FilterCatalog();RefreshGroups(Result.Groups.FirstOrDefault());
    }
    sealed record Symbol(string Id,string Name){public override string ToString()=>Name;}
    void LabelAt(string text,int x,int y,int w,int h,bool bold=false){Controls.Add(new Label {Text=text,Location=new Point(x,y),Size=new Size(w,h),Font=new Font(Font,bold?FontStyle.Bold:FontStyle.Regular),ForeColor=Color.FromArgb(48,58,78)});}
    Button ButtonAt(string text,int x,int y,int w,int h,Action action){var b=new Button {Text=text,Location=new Point(x,y),Size=new Size(w,h)};b.Click+=(_,_)=>action();Controls.Add(b);return b;}
    void RefreshGroups(Group? select){binding=true;groups.Items.Clear();groups.Items.AddRange(Result.Groups.Cast<object>().ToArray());groups.SelectedItem=select;binding=false;BindGroup();}
    void BindGroup(){if(binding)return;binding=true;var g=Selected;name.Enabled=symbol.Enabled=color.Enabled=apps.Enabled=g!=null;name.Text=g?.Name??"";if(g!=null){color.BackColor=ColorTranslator.FromHtml(g.Color);symbol.SelectedItem=symbol.Items.Cast<Symbol>().FirstOrDefault(s=>s.Id==g.Symbol);}apps.Items.Clear();if(g!=null)apps.Items.AddRange(g.Apps.Cast<object>().ToArray());binding=false;}
    void RefreshApps(int index=-1){apps.Items.Clear();if(Selected!=null)apps.Items.AddRange(Selected.Apps.Cast<object>().ToArray());if(index>=0&&index<apps.Items.Count)apps.SelectedIndex=index;}
    void AddGroup(){if(Result.Groups.Count>=20){MessageBox.Show("Puedes crear hasta 20 grupos.");return;}var g=new Group();Result.Groups.Add(g);RefreshGroups(g);name.Focus();name.SelectAll();}
    void DeleteGroup(){var g=Selected;if(g==null)return;if(MessageBox.Show(this,"¿Eliminar el grupo «"+g.Name+"»? Los programas se conservan.","Eliminar grupo",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;Result.Groups.Remove(g);RefreshGroups(Result.Groups.FirstOrDefault());}
    void MoveGroup(int offset){var g=Selected;if(g==null)return;int i=Result.Groups.IndexOf(g),j=i+offset;if(j<0||j>=Result.Groups.Count)return;Result.Groups.RemoveAt(i);Result.Groups.Insert(j,g);RefreshGroups(g);}
    void AddAvailable(){if(Selected==null||available.SelectedItem is not AppEntry entry)return;if(Selected.Apps.Any(a=>a.Target==entry.Target)){MessageBox.Show("Ese acceso ya está en el grupo.");return;}Selected.Apps.Add(JsonSerializer.Deserialize<AppEntry>(JsonSerializer.Serialize(entry,Program.Json),Program.Json)!);RefreshApps(Selected.Apps.Count-1);}
    void RemoveApp(){if(Selected==null||apps.SelectedIndex<0)return;int index=apps.SelectedIndex;Selected.Apps.RemoveAt(index);RefreshApps(Math.Min(index,Selected.Apps.Count-1));}
    void MoveApp(int offset){if(Selected==null)return;int i=apps.SelectedIndex,j=i+offset;if(i<0||j<0||j>=Selected.Apps.Count)return;var app=Selected.Apps[i];Selected.Apps.RemoveAt(i);Selected.Apps.Insert(j,app);RefreshApps(j);}
    void EditApp(){if(Selected==null||apps.SelectedItem is not AppEntry app)return;using var dialog=new AccessEditor(app);if(dialog.ShowDialog(this)==DialogResult.OK){Selected.Apps[apps.SelectedIndex]=dialog.Result;RefreshApps(apps.SelectedIndex);}}
    void Browse(){using var dialog=new OpenFileDialog {Filter="Accesos y programas|*.lnk;*.exe;*.url|Todos los archivos|*.*",Multiselect=true,Title="Añadir accesos al grupo"};if(dialog.ShowDialog(this)==DialogResult.OK)foreach(var file in dialog.FileNames)AddFile(file);}
    void AddFile(string file){if(Selected==null||!File.Exists(file))return;var target=file;if(Path.GetExtension(file).Equals(".lnk",StringComparison.OrdinalIgnoreCase)||Path.GetExtension(file).Equals(".url",StringComparison.OrdinalIgnoreCase)){var relative=Path.Combine("Accesos",Guid.NewGuid().ToString("N")+Path.GetExtension(file));Directory.CreateDirectory(Path.Combine(Program.Base,"Accesos"));File.Copy(file,Path.Combine(Program.Base,relative));target=relative;}Selected.Apps.Add(new AppEntry {Name=Path.GetFileNameWithoutExtension(file),Target=target});RefreshApps(Selected.Apps.Count-1);}
    void FilterCatalog(){available.Items.Clear();available.Items.AddRange(catalog.Where(a=>a.Name.Contains(search.Text,StringComparison.CurrentCultureIgnoreCase)).Cast<object>().ToArray());}
    static List<AppEntry> LoadCatalog(){
        var result=new List<AppEntry>();
        foreach(var root in new[]{Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu)})try{
            foreach(var path in Directory.EnumerateFiles(root,"*.lnk",SearchOption.AllDirectories)){var name=Path.GetFileNameWithoutExtension(path);if(!result.Any(a=>a.Name.Equals(name,StringComparison.OrdinalIgnoreCase)))result.Add(new AppEntry {Name=name,Target=path});}
        }catch{}
        object? shell=null,folder=null,items=null;
        try {shell=Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!);folder=((dynamic)shell!).NameSpace("shell:AppsFolder");items=((dynamic)folder!).Items();
            int count=((dynamic)items!).Count;for(int i=0;i<count;i++){object item=((dynamic)items).Item(i);try{string name=((dynamic)item).Name;string? id=((dynamic)item).ExtendedProperty("System.AppUserModel.ID") as string;if(!string.IsNullOrWhiteSpace(id)&&!result.Any(a=>a.Name.Equals(name,StringComparison.OrdinalIgnoreCase)))result.Add(new AppEntry {Name=name,Target="shell:AppsFolder\\"+id,AppId=id});}finally{Marshal.ReleaseComObject(item);}}
        }catch(Exception e){Program.Log("Catálogo: "+e.Message);}finally{if(items!=null)Marshal.ReleaseComObject(items);if(folder!=null)Marshal.ReleaseComObject(folder);if(shell!=null)Marshal.ReleaseComObject(shell);}
        return result.OrderBy(a=>a.Name,StringComparer.CurrentCultureIgnoreCase).ToList();
    }
    void Save(){
        try {Result.HoverDelayMs=(int)delay.Value;Result.SuppressWindowsPreview=suppressPreview.Checked;Result.Validate();
            if(startup.Checked&&!File.Exists(startupPath))CreateShortcut(startupPath,"",null);
            else if(!startup.Checked&&File.Exists(startupPath))File.Delete(startupPath);
            DialogResult=DialogResult.OK;Close();
        }catch(Exception e){MessageBox.Show(this,e.Message,"Revisa los grupos",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
    }
    internal void VerifyEditing(){
        var before=Result.Groups.Count;AddGroup();var created=Selected??throw new Exception("No se seleccionó el grupo nuevo.");
        if(Result.Groups.Count!=before+1)throw new Exception("No se creó el grupo.");name.Text="Grupo de prueba editable";symbol.SelectedIndex=2;
        if(created.Name!=name.Text||created.Symbol!="ai")throw new Exception("El editor no actualizó el nombre o símbolo.");
        available.SelectedIndex=0;AddAvailable();if(created.Apps.Count!=1)throw new Exception("El editor no añadió el acceso.");
        apps.SelectedIndex=0;RemoveApp();if(created.Apps.Count!=0)throw new Exception("El editor no quitó el acceso.");
        Result.Groups.Remove(created);RefreshGroups(Result.Groups.FirstOrDefault());Result.Validate();
    }
    public static void CreateShortcut(string path,string args,Group? group){
        object shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;object shortcut=((dynamic)shell).CreateShortcut(path);
        try {((dynamic)shortcut).TargetPath=Environment.ProcessPath;((dynamic)shortcut).Arguments=args;((dynamic)shortcut).WorkingDirectory=Program.Base;((dynamic)shortcut).Description="Grupos de la barra";
            if(group!=null)((dynamic)shortcut).IconLocation=Native.IconPath(group);((dynamic)shortcut).Save();
        }finally{Marshal.ReleaseComObject(shortcut);Marshal.ReleaseComObject(shell);}if(group!=null)Native.SetShortcutIdentity(path,group);
    }
}
sealed class AccessEditor : Form {
    public AppEntry Result {get;}
    public AccessEditor(AppEntry original){
        Result=JsonSerializer.Deserialize<AppEntry>(JsonSerializer.Serialize(original,Program.Json),Program.Json)!;
        Text="Editar acceso";ClientSize=new Size(620,300);StartPosition=FormStartPosition.CenterParent;Font=new Font("Segoe UI",10);FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;
        TextBox Field(string title,string value,int y){Controls.Add(new Label {Text=title,Location=new Point(18,y),Size=new Size(580,24)});var t=new TextBox {Text=value,Location=new Point(18,y+27),Size=new Size(580,30)};Controls.Add(t);return t;}
        var name=Field("Nombre",Result.Name,15);var target=Field("Destino (archivo, acceso, URL o shell:AppsFolder…)",Result.Target,86);var arguments=Field("Argumentos (opcional)",Result.Arguments,157);
        var cancel=new Button {Text="Cancelar",Location=new Point(365,246),Size=new Size(110,34),DialogResult=DialogResult.Cancel};Controls.Add(cancel);CancelButton=cancel;
        var save=new Button {Text="Aceptar",Location=new Point(486,246),Size=new Size(112,34)};Controls.Add(save);AcceptButton=save;save.Click+=(_,_)=>{if(string.IsNullOrWhiteSpace(name.Text)||string.IsNullOrWhiteSpace(target.Text)){MessageBox.Show("Escribe un nombre y un destino.");return;}Result.Name=name.Text.Trim();Result.Target=target.Text.Trim();Result.Arguments=arguments.Text;DialogResult=DialogResult.OK;};
    }
}
