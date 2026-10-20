using CampaignCore;

int count = 0;
void Check(bool ok, string message) { if (!ok) throw new Exception(message); count++; }
// The old parser silently dropped these channels and ignored explicit inheritance.
foreach (string key in FieldSchema.EquipmentColors().Concat(FieldSchema.PlayerColors))
    Check(AppearanceFields.ColorKey(key) == key, "Untranslated offered player channel: " + key);
Check(AppearanceFields.ColorKey("gloves color 3") == "Gloves Tertiary Color", "Legacy channel alias");
Check(AppearanceFields.ColorKey("helmet secondary color away") == "Helmet Away Secondary Color", "Legacy away alias");
Check(AppearanceFields.ColorKey("Number Color Home") == "Number Tertiary Color", "Native number fill channel");
Check(!AppearanceFields.OverridesTeam(new CampaignDocument("Override Team Settings = no")), "Explicit team inheritance");
Check(AppearanceFields.OverridesTeam(new CampaignDocument("Override Team Settings = yes")), "Explicit player appearance");
Check(!AppearanceFields.OverridesTeam(new CampaignDocument("Helmet Color = 1,2,3")), "Missing choice defaults to team colors while retaining authored values");
foreach (var item in new[] { "Body/Toronto", "Body/Chicago", "Body_Blocker/Hockey_FC", "Body_Pads/Golfers", "Helmet/Helmet_Chicago" })
    Check(!EquipmentCompatibility.IsSelectable(item) && EquipmentCompatibility.Issue(item).Contains("goalie view"), "Incomplete goalie asset: " + item);
foreach (var item in new[] { "Body/Customization_colors", "Body/Customization/Customization_colors", "Body_Gloves/Fist_White", "Body_Pads/Chicago", "Faces/Helmets/Helmet_Wing" })
    Check(EquipmentCompatibility.IsSelectable(item), "Current complete asset rejected: " + item);
foreach (var item in new[] { "Faces/Custom/Helmet_Reference", "Faces/Custom/Helmet_Face", "Helmet/Helmet_Customization_Face", "Body/RemovedOldEditorAsset", "Body/Customization/Customization_iso" })
    Check(!EquipmentCompatibility.IsSelectable(item), "Stale/support artwork reintroduced: " + item);
foreach (var item in new[] {
    "Helmet/Helmet_Enraged", "Faces/Helmets/Helmet_Enrage", "Faces/Helmets/Helmet_Frog",
    "Faces/Helmets/Helmet_Golden",
    "Faces/Anyteam/Heavy_Helmet", "Faces/Anyteam/Propeller_Helmet",
    "Faces/Helmets/Heavy_Helmet", "Faces/Helmets/Propeller_Helmet",
    "Body/Crusaders/GSpiked_Armor", "Body_Bicep/Crusaders_Spiked",
    "Body_Bicep/Injured", "Body_Gloves/Injured", "Body_Skates/Injured", "Body_Skates/Winged" })
    Check(!EquipmentCompatibility.IsSelectable(item) && EquipmentCompatibility.Issue(item).Contains("Gameplay effect"),
        "Match effect is not a customization choice: " + item);
var independent = new CampaignDocument("Override Team Settings = yes\nFace = Faces/Toronto/Mathieu\nBody = Body/HockeyFC/HockeyFC\nJersey Color = 10,20,30\nStick Secondary Color = 5,6,7");
var team = new CampaignDocument("Body = Body/Customization/Customization_colors\nJersey Primary = 1,2,3");
var effective = AppearanceContract.Resolve(independent, team, new CampaignDocument(""), "skater");
Check(effective.Get("Body Away") == "Body/Customization/Customization_colors", "Blank away body uses its own customizable style");
// Base-game model: SkaterData.StickColor has no home/away split, and kit colors are the team's.
Check(effective.Get("Stick Secondary Color") == "5,6,7" && effective.Get("Stick Away Secondary Color") == "5,6,7", "Player stick color is one value for both kits");
Check(effective.Get("Jersey Color") == "1,2,3", "Player kit color is ignored; the team's kit applies even with an own look");
var goalieStyles = new CampaignDocument("Override Team Settings = yes\nSkin = Body/Chicago\nGoalie Pants Skin = Body/Golfers");
var goalieLook = AppearanceContract.Resolve(goalieStyles, new CampaignDocument(""), new CampaignDocument(""), "goalie");
Check(goalieLook.Get("Skin") == "Body/Chicago" && goalieLook.Get("Goalie Pants Skin") == "Body/Golfers", "Goalie jersey and pants use separate style keys");
Check(goalieLook.Get("Skin Away") == "Body/Customization_colors" && goalieLook.Get("Goalie Pants Away") == "Body/Customization_colors", "Blank away goalie styles do not copy home styles");
independent.Set("Override Team Settings", "no");
effective = AppearanceContract.Resolve(independent, team, new CampaignDocument(""), "skater");
Check(effective.Get("Body") == "Body/Customization/Customization_colors" && effective.Get("Jersey Color") == "1,2,3", "Saved independent appearance must not override team mode");
team.Set("Logo From", "Winnipeg"); team.Set("Logo Skin", "Team_Logo/Custom_R");
foreach (string role in new[] { "skater", "goalie" })
{
    string bodyKey = role == "goalie" ? "Skin" : "Body";
    string custom = AppearanceContract.DefaultSkin(role, bodyKey);
    string premade = role == "goalie" ? "Body/Figure_Skaters" : "Body/HockeyFC/HockeyFC";
    foreach (var kits in new[] { (premade, ""), (premade, custom), (custom, premade), (premade, premade) })
    {
        var player = new CampaignDocument("Override Team Settings = yes\nExport Logo PNG = stale\nExport Logo ID = stale");
        player.Set(bodyKey, kits.Item1); player.Set(bodyKey + " Away", kits.Item2);
        var resolved = AppearanceContract.Resolve(player, team, new CampaignDocument(""), role);
        Check(resolved.Get("Logo From") == "Winnipeg" && resolved.Get("Logo Skin") == "Team_Logo/Custom_R", role + " premade leaves identity logo intact");
        Check(resolved.Get("Export Logo PNG") == "stale", role + " premade leaves identity payload intact");
        Check(resolved.Get("Jersey Logo From") == (kits.Item1 == custom ? "Winnipeg" : "")
            && resolved.Get("Jersey Logo Away From") == (kits.Item2 == custom || kits.Item2 == "" ? "Winnipeg" : ""), role + " per-kit logo compatibility");
        Check(team.Get("Logo From") == "Winnipeg", "Team identity unchanged");
    }
    var compatible = new CampaignDocument("Override Team Settings = yes"); compatible.Set(bodyKey, custom);
    Check(AppearanceContract.Resolve(compatible, team, new CampaignDocument(""), role).Get("Logo From") == "Winnipeg", role + " compatible player still inherits logo");
}
Console.WriteLine($"PASS {count} appearance translation and compatibility checks");
