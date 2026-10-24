using Avalonia.Controls;
using CampaignCore;

namespace CampaignCreator;

public sealed partial class MapEditorView
{
    private async Task ChooseCustomCoach(int layer, bool newLayer, MapLayoutNode? placeholder)
    {
        try {
            var originalMap = map;
            var result = await CustomCoachEditor.Choose(detailsDialog ?? DialogOwner, contentRoot, campaignDir);
            if (result == null || map == null || !ReferenceEquals(originalMap, map)) return;
            CustomCoachCatalog.Validate(contentRoot, campaignDir, result);
            if (placeholder != null) {
                if (!map.Nodes.Contains(placeholder)) return;
                Checkpoint(); placeholder.Type = "Coach"; placeholder.CustomCoach = result.Copy();
                placeholder.Team = ""; placeholder.Stadium = ""; selected = placeholder;
                Draw(); Inspect(); Validate();
            } else AddLayerNode(layer, newLayer, "Coach", result);
        } catch (Exception ex) { message.Text = "Could not use Custom Coach: " + ex.Message; Inspect(); }
    }
    private async Task EditCustomCoach(MapLayoutNode node)
    {
        try {
            var originalMap = map;
            var result = await new CustomCoachEditor(contentRoot, campaignDir, node.CustomCoach, false)
                .ShowDialog<CustomCoachDefinition?>(detailsDialog ?? DialogOwner);
            if (result == null || map == null || !ReferenceEquals(originalMap, map) || !map.Nodes.Contains(node)) return;
            Checkpoint(); node.CustomCoach = result.Copy(); selected = node; Draw(); Inspect(); Validate();
        } catch (Exception ex) { message.Text = "Could not edit Custom Coach: " + ex.Message; }
    }
    private static string NodeTitle(MapLayoutNode node) => node.Type == "Coach" && node.CustomCoach != null ? node.CustomCoach.Name : NodeName(node.Type);
}
