using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using CampaignCore;

namespace ModCompatibility;

public sealed record CompatibilityResult(string State,string Message,string? Backup=null);
public static class CompatibilityUpdater
{
    private static readonly object Gate=new();
    public static string Hash(string path) { using var stream=File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
    private static string BundledBaseline(string path)
    {
        using var file=File.OpenRead(path);using var pe=new PEReader(file);
        var metadata=pe.GetMetadataReader();
        foreach(var handle in metadata.ManifestResources)
        {
            var resource=metadata.GetManifestResource(handle);
            if(metadata.GetString(resource.Name)!="NativeCompatibility.json" || !resource.Implementation.IsNil) continue;
            var reader=pe.GetSectionData(pe.PEHeaders.CorHeader!.ResourcesDirectory.RelativeVirtualAddress).GetReader(checked((int)resource.Offset),pe.PEHeaders.CorHeader.ResourcesDirectory.Size-checked((int)resource.Offset));
            int length=reader.ReadInt32();
            return NativeCompatibilityProfile.Read(System.Text.Encoding.UTF8.GetString(reader.ReadBytes(length))).BaselineId;
        }
        throw new InvalidDataException("The bundled mod does not support compatibility updates.");
    }
    public static bool IsGameRunning(string game)
    {
        foreach(var process in Process.GetProcessesByName("Tape to Tape"))
        {
            using(process)
            {
                try { if(Path.GetFullPath(process.MainModule!.FileName).Equals(Path.Combine(Path.GetFullPath(game),"Tape to Tape.exe"),StringComparison.OrdinalIgnoreCase)) return true; }
                catch { return true; } // An inaccessible process is not permission to replace its mod.
            }
        }
        return false;
    }
    public static CompatibilityResult Ensure(string game,string package,IProgress<string>? progress=null)
    {
        lock(Gate)
        {
            using var installLock=new Mutex(false,"T2TCompatibility-"+NativeImage.Digest(Path.GetFullPath(game).ToUpperInvariant())[..24]);
            bool acquired;
            try { acquired=installLock.WaitOne(0); } catch(AbandonedMutexException) { acquired=true; }
            if(!acquired) return new("Deferred","Another Studio instance is checking compatibility. This window will retry automatically.");
            try {
            string plugins=Path.Combine(game,"BepInEx","plugins");
            string target=Path.Combine(plugins,"CustomCampaignFramework.dll");
            string profileFile=Path.Combine(plugins,"CustomCampaignFramework.compatibility.json");
            string? backup=null;
            try
            {
                if(!File.Exists(target)) return new("NotInstalled","The campaign mod is not installed. Run the suite installer first.");
                string baselineFile=Path.Combine(package,"native-baseline.json"), modFile=Path.Combine(package,"CustomCampaignFramework.dll");
                var baseline=CompatibilityBaseline.Read(baselineFile);
                string expectedMod=File.ReadAllText(Path.Combine(package,"runtime-sha256.txt")).Trim();
                if(!Regex.IsMatch(expectedMod,"^[A-F0-9]{64}$") || Hash(modFile)!=expectedMod) throw new InvalidDataException("Studio's bundled mod is incomplete. Reinstall the suite.");
                if(BundledBaseline(modFile)!=baseline.Id) throw new InvalidDataException("The bundled mod and compatibility verifier belong to different releases.");
                string gameFile=Path.Combine(game,"GameAssembly.dll"), metadata=Path.Combine(game,"Tape to Tape_Data/il2cpp_data/Metadata/global-metadata.dat");
                string gameHash=Hash(gameFile), metadataHash=Hash(metadata);
                string beforeMod=Hash(target), beforeProfile=File.Exists(profileFile)?Hash(profileFile):"";
                if(beforeMod!=expectedMod && !File.ReadAllLines(Path.Combine(package,"replaces-sha256.txt")).Contains(beforeMod,StringComparer.OrdinalIgnoreCase))
                    throw new InvalidDataException("The installed mod differs from this Studio release. Update the suite first; the installed mod was preserved.");
                if(beforeMod==expectedMod)
                {
                    NativeCompatibilityProfile? existing=null;
                    if(File.Exists(profileFile)) { try { existing=NativeCompatibilityProfile.Read(File.ReadAllText(profileFile)); } catch(JsonException) { } }
                    if(existing?.GameHash==gameHash && existing.MetadataHash==metadataHash && existing.BaselineId==baseline.Id)
                    { existing.Validate(gameHash,baseline.Id);return new("Current","Mod compatibility is current."); }
                }
                if(IsGameRunning(game)) return new("Deferred","Close Tape to Tape to update mod compatibility. Studio will retry automatically.");
                progress?.Report("Reading the new game build to update mod compatibility…");
                var image=new NativeImage(game);
                string manifest=Path.GetFullPath(Path.Combine(game,"..","..","appmanifest_1566200.acf"));
                string build=File.Exists(manifest)?Regex.Match(File.ReadAllText(manifest),"\"buildid\"\\s+\"(\\d+)\"").Groups[1].Value:"";
                if(build.Length==0) build=image.GameHash[..12];
                var profile=baseline.Verify(image,build,progress);
                if(profile.GameHash!=gameHash || profile.MetadataHash!=metadataHash || Hash(gameFile)!=gameHash || Hash(metadata)!=metadataHash)
                    throw new IOException("The game changed while it was being checked. Wait for Steam to finish, then retry.");
                if(IsGameRunning(game)) return new("Deferred","Close Tape to Tape to finish updating mod compatibility. Studio will retry automatically.");
                if(Hash(target)!=beforeMod || (File.Exists(profileFile)?Hash(profileFile):"")!=beforeProfile) throw new IOException("Another installer changed the mod. Retry after it finishes.");
                // Keep a recoverable copy. The profile goes first: old plugin versions ignore it.
                backup=Path.Combine(game,"BepInEx","compatibility-backups",DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")[..8]);
                Directory.CreateDirectory(backup);File.Copy(target,Path.Combine(backup,"CustomCampaignFramework.dll"));
                if(File.Exists(profileFile)) File.Copy(profileFile,Path.Combine(backup,"CustomCampaignFramework.compatibility.json"));
                string stagedProfile=profileFile+"."+Guid.NewGuid().ToString("N")+".tmp";
                string stagedMod=target+"."+Guid.NewGuid().ToString("N")+".tmp";
                bool replacedProfile=false;
                try
                {
                    File.WriteAllText(stagedProfile,JsonSerializer.Serialize(profile,CompatibilityBaseline.Json));
                    File.Copy(modFile,stagedMod);
                    if(Hash(stagedMod)!=expectedMod) throw new IOException("Staged plugin hash differs.");
                    if(IsGameRunning(game)) return new("Deferred","Close Tape to Tape to finish updating mod compatibility. Studio will retry automatically.",backup);
                    File.Move(stagedProfile,profileFile,true);replacedProfile=true;
                    File.Move(stagedMod,target,true);
                }
                catch
                {
                    if(replacedProfile)
                    {
                        string saved=Path.Combine(backup,"CustomCampaignFramework.compatibility.json");
                        if(File.Exists(saved)) File.Copy(saved,profileFile,true);else File.Delete(profileFile);
                    }
                    throw;
                }
                finally { if(File.Exists(stagedProfile)) File.Delete(stagedProfile);if(File.Exists(stagedMod)) File.Delete(stagedMod); }
                File.WriteAllText(Path.Combine(backup,"receipt.json"),JsonSerializer.Serialize(new { build,gameHash,metadataHash,modSha256=expectedMod,baseline=baseline.Id,sites=profile.Sites.Count,verifiedMethods=baseline.Methods.Count },CompatibilityBaseline.Json));
                return new("Updated",$"Mod compatibility updated for game {build}. Ready to launch.",backup);
            }
            catch(Exception ex) when(ex is not OutOfMemoryException)
            { return new("NeedsRepair","Mod compatibility could not be updated: "+ex.Message,backup); }
            } finally { installLock.ReleaseMutex(); }
        }
    }
}
