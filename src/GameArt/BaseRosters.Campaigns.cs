using System.Text.Json;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace GameArt;

public sealed partial class BaseRosters
{
    private static void WriteCampaignCatalog(UnityGame game, string output, BehaviourReader reader,
        Dictionary<string, Native> records, Dictionary<string, string> teamFolders, Dictionary<string, string> playerFiles)
    {
        var all = reader.All().ToArray();
        string Ref(AssetsFileInstance file, AssetTypeValueField pointer) => reader.Ref(file, pointer)
            ?? throw new InvalidDataException("A required native campaign reference is empty.");
        Native Read(string key)
        {
            if (records.TryGetValue(key, out var existing)) return existing;
            var target = reader.Find(key) ?? throw new InvalidDataException("Missing native campaign asset: " + key);
            return new Native(key, target.Script.Class, target.File, reader.Read(target.File, target.Info, target.Script));
        }
        string Id(string key) => Read(key)["id"].AsString;
        string[] Ids(Native owner, string field) => Items(owner[field]).Select(p => Id(Ref(owner.File,field == "m_RelicsData" ? Field(p,"Relic") : p))).ToArray();
        var stateSource = all.Single(x => x.Script.Class == "CampaignState");
        var state = Read(Key(stateSource.File.name,stateSource.Info.PathId));
        string[] freeAgents = Items(state["freeAgents"]).Select(p => Ref(state.File,p)).ToArray();
        var benchSource=all.Single(x=>x.Script.Class=="BenchConfig");
        var benchConfig=Read(Key(benchSource.File.name,benchSource.Info.PathId));
        string[] bench=benchConfig.Value.Children.Where(f=>!f["original"].IsDummy)
            .Select(f=>Ref(benchConfig.File,f["original"])).ToArray();
        // OldSquadMenu.GenerateSuperStarSkaters iterates CampaignState.freeAgents,
        // then applies unlock, squad and current-roster eligibility at runtime.
        string[] stars = freeAgents;
        var rewardCategories = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        void Rewards(AssetsFileInstance file, AssetTypeValueField field)
        {
            if (!field["m_PathID"].IsDummy)
            {
                string? key = reader.Ref(file,field);
                if (key == null || reader.Find(key) is not {} target) return;
                var value = reader.Read(target.File,target.Info,target.Script);
                if (!value["id"].IsDummy && !value["category"].IsDummy && (!value["relicName"].IsDummy || !value["powerupName"].IsDummy))
                    rewardCategories[value["id"].AsString] = value["category"].AsLong;
                return;
            }
            foreach (var child in field.Children) Rewards(file,child);
        }
        foreach (var repo in all.Where(x => x.Script.Class is "RelicRepository" or "TalentRepository" or "AbilityRepository"))
            Rewards(repo.File,reader.Read(repo.File,repo.Info,repo.Script));
        var campaigns = new List<object>();
        foreach (var source in all.Where(x => x.Script.Class == "RunSquadScriptableObject"))
        {
            var squad = Read(Key(source.File.name,source.Info.PathId));
            string starting = Ref(squad.File,squad["startingTeam"]);
            if (!teamFolders.ContainsKey(starting)) throw new InvalidDataException("Native starting team has no editable snapshot: " + starting);
            var opponents = Read(Ref(squad.File,squad["campaignOpponents"]));
            var groups = new Dictionary<string,string[]>();
            foreach (string field in Enumerable.Range(1,3).SelectMany(act => new[] {$"act{act}Elites",$"act{act}Bosses"}))
                groups[field] = Items(opponents[field]).Select(entry => Ref(opponents.File,Field(entry,"team"))).ToArray();
            // Challenge teams are selected by the game's challenge flow, outside the elite/boss pools.
            groups["challenges"] = new[] {records.Values.Single(t => t.Class == "TeamData" && t.Name == "challengeTeam").Key};
            string[] customBench = Items(squad["m_CustomSwapPool"]).Select(p => Ref(squad.File,p)).ToArray();
            string[] candidates = customBench.Length > 0 ? customBench : bench;
            foreach (string key in groups.Values.SelectMany(x => x))
                if (!teamFolders.ContainsKey(key)) throw new InvalidDataException("Native opponent has no editable snapshot: " + key);
            foreach (string key in candidates.Concat(stars).Concat(freeAgents))
                if (!playerFiles.ContainsKey(key)) throw new InvalidDataException("Native pool player has no editable snapshot: " + key);
            campaigns.Add(new {
                id=squad["id"].AsString,name=squad["squadName"].AsString,startingTeam=starting,opponents=groups,
                bench=candidates,superstars=stars,freeAgents,startingRelics=Ids(squad,"m_RelicsData"),
                excludedRelics=Ids(squad,"excludedRelics"),excludedTalents=Ids(squad,"excludedTalents"),excludedAbilities=Ids(squad,"excludedAbilities"),
                excludedRelicCategory=squad["excludedRelicCategory"].AsLong,excludedTalentCategory=squad["excludedTalentCategory"].AsLong,
                maximumBenchwarmers=Number(squad["m_MaxBenchwarmersOnBench"]),usesSuperstars=Truthy(squad["m_UsesFreeAgentIntro"])
            });
        }
        var catalog = new {
            version=1,gameSha256=Png.Sha256(Path.Combine(game.Root,"GameAssembly.dll")),campaigns,
            teams=teamFolders.ToDictionary(p=>p.Key,p=>new {folder=p.Value,id=Id(p.Key),asset=Read(p.Key).Name,name=Read(p.Key)["teamName"].AsString}),
            players=playerFiles.ToDictionary(p=>p.Key,p=>new {file=p.Value,id=Id(p.Key),name=NameOf(Read(p.Key))}),rewardCategories
        };
        File.WriteAllText(Path.Combine(output,"catalog.json"),JsonSerializer.Serialize(catalog,new JsonSerializerOptions {WriteIndented=true}));
    }
}
