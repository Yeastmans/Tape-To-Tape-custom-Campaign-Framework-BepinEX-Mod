using Avalonia.Threading;
using Avalonia.Controls;
using GameArt;
using ModCompatibility;

namespace CampaignCreator;

public sealed partial class MainWindow
{
    internal static bool UpdateModCompatibilityOnStart;
    private DispatcherTimer? compatibilityTimer;
    private string lastCompatibilityFiles="";
    private Task? compatibilityTask;
    private string compatibilityStatus="";
    private string compatibilityState="";
    private TextBlock? compatibilityHomeStatus;
    private void SetCompatibilityStatus(string message)
    {
        compatibilityStatus=message;status.Text=message;
        if(compatibilityHomeStatus!=null) compatibilityHomeStatus.Text=message;
    }
    private void StartCompatibilityUpdates()
    {
        if(!UpdateModCompatibilityOnStart) return;
        compatibilityTimer=new DispatcherTimer { Interval=TimeSpan.FromSeconds(30) };
        compatibilityTimer.Tick+=async (_,_)=> { if(!readingGameArt && releaseCheck==null && !updateInstallerStarted) await EnsureModCompatibility(); };
        compatibilityTimer.Start();
        Closed+=(_,_)=>compatibilityTimer.Stop();
    }
    private async Task EnsureModCompatibility(bool force=false)
    {
        if(compatibilityTask!=null) { await compatibilityTask;return; }
        compatibilityTask=CheckModCompatibility(force);
        try { await compatibilityTask; } finally { compatibilityTask=null; }
    }
    private async Task CheckModCompatibility(bool force)
    {
        if(!UpdateModCompatibilityOnStart) return;
        string? game=ArtCache.FindGame(Path.Combine(root,"game-folder.txt"));
        if(game==null) { compatibilityState="NotInstalled"; SetCompatibilityStatus("Choose the game folder with Read game artwork before launching."); return; }
        string package=Path.Combine(AppContext.BaseDirectory,"mod-runtime");
        string Stamp(string path) { var f=new FileInfo(path); return f.Exists?path+":"+f.Length+":"+f.LastWriteTimeUtc.Ticks:path+":missing"; }
        string Fingerprint()=>string.Join("|",new[]{Path.Combine(game,"GameAssembly.dll"),Path.Combine(game,"Tape to Tape_Data/il2cpp_data/Metadata/global-metadata.dat"),
            Path.Combine(game,"BepInEx/plugins/CustomCampaignFramework.dll"),Path.Combine(game,"BepInEx/plugins/CustomCampaignFramework.compatibility.json"),Path.Combine(package,"runtime-sha256.txt")}.Select(Stamp));
        string files=Fingerprint();
        if(!force && files==lastCompatibilityFiles) return;
        try
        {
            var progress=new Progress<string>(SetCompatibilityStatus);
            var result=await Task.Run(()=>CompatibilityUpdater.Ensure(game,package,progress));
            compatibilityState=result.State;
            SetCompatibilityStatus(result.Message);
            if(result.State!="Deferred") lastCompatibilityFiles=Fingerprint();
            string report=Path.Combine(root,"mod-compatibility-status.json");
            File.WriteAllText(report,System.Text.Json.JsonSerializer.Serialize(new { result.State,result.Message,result.Backup,CheckedAtUtc=DateTime.UtcNow }));
            if(result.State is "NeedsRepair" or "NotInstalled") await CheckForStudioUpdates();
        }
        catch(Exception ex) { compatibilityState="NeedsRepair"; SetCompatibilityStatus("Mod compatibility check failed: "+ex.Message); }
    }
}
