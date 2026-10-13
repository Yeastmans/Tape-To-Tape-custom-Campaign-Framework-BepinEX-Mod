namespace CampaignCreator;

// Selectable goalie equipment from the installed game bundle; shader and match-state variants excluded.
// Source SHA-256: 9bfe0c9f9b58265618760ddf8d0784dd4fda981d32d372d32e02038e3af9e07d
internal static class GoalieEquipmentCatalog
{
    internal static IReadOnlyList<string> Values(string field) => field switch
    {
        "Helmet Skin" => [
            "Helmet/Helmet_Canadians",
            "Helmet/Helmet_Cheese",
            "Helmet/Helmet_Chicago",
            "Helmet/Helmet_Cultists",
            "Helmet/Helmet_Customization_colors",
            "Helmet/Helmet_Disco",
            "Helmet/Helmet_Figure_Skaters",
            "Helmet/Helmet_Golfers",
            "Helmet/Helmet_HockeyFC",
            "Helmet/Helmet_Knights",
            "Helmet/Helmet_Meatballs",
            "Helmet/Helmet_Mountaineers",
            "Helmet/Helmet_Princess",
            "Helmet/Helmet_Prisoners",
            "Helmet/Helmet_Referees",
            "Helmet/Helmet_Toronto",
            "Helmet/Helmet_Tycoons",
        ],
        "Skin" or "Skin Away" or "Goalie Pants Skin" or "Goalie Pants Away" => [
            "Body/Chicago",
            "Body/Customization_colors",
            "Body/Figure_Skaters",
            "Body/Golfers",
            "Body/HockeyFC",
            "Body/Knights",
            "Body/Mountaineers",
            "Body/Princess",
            "Body/Prisoners",
            "Body/Referees",
            "Body/Toronto",
            "Body/Tycoons",
        ],
        "Glove Skin" or "Glove Away" => [
            "Body_Glove/Brown",
            "Body_Glove/Chicago",
            "Body_Glove/Customization/Customization_colors",
            "Body_Glove/Figure_Skaters",
            "Body_Glove/Hockey_FC",
            "Body_Glove/Tycoons",
        ],
        "Blocker Skin" or "Blocker Away" => [
            "Body_Blocker/Brown",
            "Body_Blocker/Chicago",
            "Body_Blocker/Customization/Customization_colors",
            "Body_Blocker/Figure_Skaters",
            "Body_Blocker/Golfers",
            "Body_Blocker/Hockey_FC",
            "Body_Blocker/Knights",
            "Body_Blocker/Tycoons",
        ],
        "Pads Skin" or "Pads Away" => [
            "Body_Pads/Brown",
            "Body_Pads/Chicago",
            "Body_Pads/Customization/Customization_colors",
            "Body_Pads/Figure_Skaters",
            "Body_Pads/Golfers",
            "Body_Pads/Hockey_FC",
            "Body_Pads/Tycoons",
        ],
        "Stick Skin" or "Stick Away" => [
            "Body_Stick/Chicago",
            "Body_Stick/Customization/Customization_colors",
            "Body_Stick/Figure_Skaters",
            "Body_Stick/Tycoons",
        ],
        _ => []
    };
}
