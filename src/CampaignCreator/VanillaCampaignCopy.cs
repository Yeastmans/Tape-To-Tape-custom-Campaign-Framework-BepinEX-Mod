using System.Text.Json;
using CampaignCore;

namespace CampaignCreator;

/// <summary>Build a complete editable draft from exact installed native assets.</summary>
public static class VanillaCampaignCopy
{
    public static string CreateDraft(string root, string cache, string squadId)
    {
        string source = Path.Combine(cache,"base-campaigns");
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(source,"catalog.json")));
        var catalog = json.RootElement;
        if (catalog.GetProperty("version").GetInt32()!=1) throw new InvalidDataException("Unsupported base campaign catalog. Read game artwork again.");
        var squad = catalog.GetProperty("campaigns").EnumerateArray().Single(s => s.GetProperty("id").GetString()==squadId);
        var template = SquadTemplateCatalog.Find(squadId) ?? throw new ArgumentException("Choose a native squad.");
        var nativeMaps = MapCatalog.Load(Path.Combine(cache,"base-maps","_game_maps.txt"));
        var maps = Enumerable.Range(0,3).Select(act => nativeMaps.Single(m => SquadTemplatePolicy.RuntimeIdentity(m.BaseSquad)==squadId && m.BaseMap==act && m.Template==0)).ToArray();
        string drafts=Path.GetFullPath(Path.Combine(root,".studio-drafts"));
        string draft = Path.GetFullPath(Path.Combine(drafts,Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(draft);
        string GetSource(string relative)
        {
            string path=Path.GetFullPath(Path.Combine(source,relative));
            if (!path.StartsWith(Path.GetFullPath(source)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Invalid native content path.");
            return path;
        }
        void Snapshot(string folder)
        {
            foreach (string file in Directory.GetFiles(folder,"*",SearchOption.AllDirectories).Where(f=>f.EndsWith(".txt")||f.EndsWith(".cfg")))
                File.Copy(file,file+".native");
        }
        void Team(string key,string target)
        {
            var metadata=catalog.GetProperty("teams").GetProperty(key);
            string from=GetSource(metadata.GetProperty("folder").GetString()!);
            foreach (string file in Directory.GetFiles(from,"*",SearchOption.AllDirectories))
            {
                string to=Path.Combine(target,Path.GetRelativePath(from,file));Directory.CreateDirectory(Path.GetDirectoryName(to)!);File.Copy(file,to);
            }
        }
        try
        {
            var settings=new CampaignDocument("# Editable copy of the installed base campaign\n");
            settings.Set("Squad Template",template.Name);settings.Set("Maps",template.Name);settings.Set("Act Sequence","1,2,3");
            settings.Set("Use Player Teams","yes");settings.Set("Custom Squads Only","yes");settings.Set(VanillaCopyData.SquadId,squadId);
            settings.Set("Vanilla Source Game SHA256",catalog.GetProperty("gameSha256").GetString()!);settings.Save(Path.Combine(draft,"campaign.txt"));
            string player=Path.Combine(draft,"player_teams","Copied "+squadId);
            Team(squad.GetProperty("startingTeam").GetString()!,player);
            var team=new CampaignDocument(File.ReadAllText(Path.Combine(player,"team.txt")));
            team.Set("Squad Template",template.Name);team.Set(VanillaCopyData.SquadId,squadId);
            team.Set("Vanilla Starting Relics",string.Join(", ",squad.GetProperty("startingRelics").EnumerateArray().Select(x=>x.GetString())));
            team.Save(Path.Combine(player,"team.txt"));Snapshot(player);
            var roles=new Dictionary<string,List<string>>();
            foreach (var group in squad.GetProperty("opponents").EnumerateObject())
                foreach (var item in group.Value.EnumerateArray())
                {
                    string key=item.GetString()!;
                    if (!roles.TryGetValue(key,out var uses)) roles[key]=uses=[];
                    uses.Add(group.Name);
                }
            foreach (var (key,uses) in roles)
            {
                var metadata=catalog.GetProperty("teams").GetProperty(key);
                string folder=Path.Combine(draft,"teams",Path.GetFileName(metadata.GetProperty("folder").GetString()!));
                Team(key,folder);
                var opponent=new CampaignDocument(File.ReadAllText(Path.Combine(folder,"team.txt")));
                opponent.Set(VanillaCopyData.OpponentId,metadata.GetProperty("id").GetString()!);
                // The copied baseline expresses the native immunity state;
                // choosing Auto later is an explicit editable opponent rule.
                foreach(var interaction in RelicOpponentInteractions.All)
                    opponent.Set(interaction.SettingKey,OpponentEffects.FromPreset(opponent.Get("Opponent Preset")).Contains(interaction.EffectKey)?"Auto":"Off");
                opponent.Set("Vanilla Opponent Roles",string.Join(", ",uses));opponent.Save(Path.Combine(folder,"team.txt"));Snapshot(folder);
            }
            foreach (var (key,folder) in new[] {("bench","draft_pool"),("superstars","superstars"),("freeAgents","free_agents")})
            {
                string to=Path.Combine(draft,"player_teams",folder);Directory.CreateDirectory(to);var files=new List<string>();
                foreach (var item in squad.GetProperty(key).EnumerateArray())
                {
                    var metadata=catalog.GetProperty("players").GetProperty(item.GetString()!);
                    string file="native-"+files.Count.ToString("D3")+".txt";
                    var doc=new CampaignDocument(File.ReadAllText(GetSource(metadata.GetProperty("file").GetString()!)));
                    // Candidate equipment inherits the selected team exactly as native recruitment does.
                    doc.Set("Override Team Settings","no");doc.Save(Path.Combine(to,file));files.Add(file);
                }
                SelectedPlayerPool.Save(to,files);File.WriteAllLines(Path.Combine(to,VanillaCopyData.PoolManifest),files);Snapshot(to);
            }
            var rewards=new CampaignDocument("# Native defaults remain native until a pool is edited.\n");
            foreach (string kind in RewardPoolDefinition.Kinds)
            {
                string excludedField=kind=="Relics"?"excludedRelics":kind=="Abilities"?"excludedAbilities":"excludedTalents";
                var excluded=squad.GetProperty(excludedField).EnumerateArray().Select(x=>x.GetString()!).ToHashSet(StringComparer.OrdinalIgnoreCase);
                long mask=squad.GetProperty(kind=="Relics"?"excludedRelicCategory":"excludedTalentCategory").GetInt64();
                var pool=new RewardPoolDefinition {UseBaseGame=true};
                foreach (string id in RewardCatalog.Load(root,kind).Keys.Where(id=>RewardCatalog.OfferedInBasePool(kind,id)&&!excluded.Contains(id)))
                {
                    // Local dumps also contain registered custom rewards. Only the
                    // installed native catalog defines a vanilla campaign's defaults.
                    if (!catalog.GetProperty("rewardCategories").TryGetProperty(id,out var category)) continue;
                    if ((category.GetInt64()&mask)==0) pool.Selected.Add(id);
                }
                pool.Write(rewards,kind);rewards.Set("Native Defaults "+kind,string.Join(", ",pool.Selected.Order(StringComparer.OrdinalIgnoreCase)));
            }
            rewards.Save(Path.Combine(draft,"reward_pools.txt"));
            Directory.CreateDirectory(Path.Combine(draft,"map_layouts"));
            for (int i=0;i<maps.Length;i++) {maps[i].VanillaRandom=true;maps[i].LookAct=i+1;File.WriteAllText(Path.Combine(draft,"map_layouts",$"map-{i+1}.txt"),maps[i].Serialize());}
            File.Copy(Path.Combine(source,"catalog.json"),Path.Combine(draft,"native-copy-source.json"));
            return draft;
        }
        catch
        {
            // The directory is a fresh, owned GUID under .studio-drafts.
            if(Path.GetDirectoryName(draft)==drafts)Directory.Delete(draft,true);throw;
        }
    }
}
