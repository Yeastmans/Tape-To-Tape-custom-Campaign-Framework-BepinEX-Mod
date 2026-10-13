using CampaignCore;

namespace GameArt;

public sealed partial class BaseRosters
{
    // Native TeamData classifier constants, verified against build25899138.
    // These describe the editor baseline only; unchanged runtime copies retain
    // the native predicates, including consumers outside the editable effects.
    private static readonly Dictionary<string,string> NativeOpponentPresets = new()
    {
        ["2288b0e3-c8b4-4546-8e9a-8d2b3b48ef21"]="Calaveras",["1ca285e4-4945-4406-8c23-c3f2ea4838ee"]="Calaveras",
        ["26bcb9b9-7e32-461b-9090-3de829377995"]="Cheese",
        ["3fcb6dc8-f1a6-421c-9645-b14a4566f79b"]="Crusaders",["838e00e5-41e3-4160-be18-92ed6c66f676"]="Cultists",
        ["04f9e958-f6d1-4716-98f2-42c923cacd65"]="Disco",["8b051989-cc49-4913-847e-41e5a34af960"]="Golfers",
        ["23b70b6c-b757-4445-a75b-b5c1fc3bc103"]="Lettuce",["82dde0f4-4d27-4db4-af49-76a67d8233a8"]="Meatballs",
        ["ca082292-2446-44ff-a0bb-7c4a6a846057"]="Mountaineers",["fae1eb4a-a488-426b-852f-7dc6d4449e51"]="Princess",
        ["b33c1c56-7b11-4cdf-90b6-360d1aaa2022"]="Prisoners",["0e6340e1-e1f7-48ff-97c6-fc3384c8e44a"]="Referees",
        ["4d86e4a6-f2e9-4316-a5a3-94d61264a992"]="Referees",["0ded14f7-eb60-4d7d-896e-fba94f91cc0a"]="Shooting Stars",
        ["fe29ad3e-ebc0-4c5d-b21f-b1b669673567"]="Hockey FC",["330786e8-67cd-40aa-98b9-f6e46933acb4"]="Tycoons"
    };
    private static IEnumerable<(string,string)> NativeOpponentFields(Native team,BehaviourReader reader,string relics)
    {
        string id=team["id"].AsString;
        string preset=team["nickname"].AsString=="SPT"?"Spartans":NativeOpponentPresets.GetValueOrDefault(id,"Standard");
        string Ai(string field)
        {
            string? key=reader.Ref(team.File,team[field]);
            if(key==null)return "Standard";
            var asset=reader.Find(key)??throw new InvalidDataException("Missing native formation: "+key);
            return reader.Read(asset.File,asset.Info,asset.Script)["m_Name"].AsString;
        }
        yield return ("Opponent Preset",preset);
        yield return ("Goal Sound Volume","100");
        yield return ("Opponent Effects",OpponentEffects.Serialize(OpponentEffects.FromPreset(preset)));
        yield return ("Opponent Offense",Ai("offensiveAI"));yield return ("Opponent Defense",Ai("defensiveAI"));
        yield return ("Match Puck",preset=="Hockey FC"?"Soccer ball":preset=="Golfers"?"Golf ball":"Hockey puck");
        yield return ("Match Surface",preset=="Hockey FC"?"Soccer field":preset=="Golfers"?"Golf ice":preset=="Tycoons"?"Tycoons ice":"Standard ice");
        yield return ("Golf Fairway",relics.Contains("71c7036b-ba18-46e0-8fef-d25217d5ca17",StringComparison.OrdinalIgnoreCase)?"yes":"no");
        yield return ("Tycoons Sponsors",preset=="Tycoons"?"yes":"no");
        yield return ("Canada Curses",id=="8652786b-036e-4fd8-9960-380eeaa7adb7"?"yes":"no");
        yield return ("Opponent Two Lines",Items(team["lines"]).Count()>1?"yes":"no");
        foreach(var interaction in RelicOpponentInteractions.All)
            yield return (interaction.SettingKey,OpponentEffects.FromPreset(preset).Contains(interaction.EffectKey)?"Auto":"Off");
    }
}
