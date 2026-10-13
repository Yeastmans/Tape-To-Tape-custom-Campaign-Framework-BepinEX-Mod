using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CampaignCore;

namespace CampaignCreator;

/// <summary>Original extracted atlas regions, cached for the lifetime of one map window.</summary>
public sealed class MapNodeArtwork : IDisposable
{
    private readonly Dictionary<string, Bitmap> images = new();
    public static string Region(MapLayoutNode node, int act)
    {
        if (node.Type is "Elite" or "Boss")
        {
            var stadium = NodeSettingsWindow.Stadiums.FirstOrDefault(s => s.Id == node.Stadium && s.Image.Length > 0);
            if (stadium != null) return stadium.Image;
            // A random/template opponent cannot be resolved to a team stadium until runtime.
            return "Outdoor_Rink";
        }
        return node.Type switch
        {
            "Challenge" => act == 2 ? "Challenge_Act2" : "Outdoor_Rink",
            "Chaos" => "chaos",
            "Nurse" => "Infirmary",
            "Blademaster" => "blademaster",
            "FanNumber1" => "Fan",
            "Coach" => "Coach_house",
            "GeneralManager" => "GM",
            "RandomConditioning" or "PlayerTraining" or "TeamTraining" or "StatTraining" => "TrainingCenter",
            _ => ""
        };
    }
    public Bitmap? Get(MapLayoutNode node, int act)
    {
        string region = Region(node, act);
        if (region.Length == 0) return null;
        if (!images.TryGetValue(region, out var image))
        {
            // Atlas regions read from the player's install; null until the art cache exists.
            if (StudioArt.Bitmap("node-images", "regions", region + ".png") is not { } loaded) return null;
            images[region] = image = loaded;
        }
        return image;
    }
    public void Dispose() { foreach (var image in images.Values) image.Dispose(); images.Clear(); }
}
