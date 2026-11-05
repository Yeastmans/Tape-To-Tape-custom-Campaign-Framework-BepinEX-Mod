using CampaignCore;

namespace CampaignCreator;

internal sealed class CustomRewardExportBundle(GameData game, ExportReport? report = null)
{
    private readonly PlayNowRewardPolicy policy = report?.RewardPolicy ?? PlayNowRewardPolicy.Strict;
    private readonly Dictionary<string, CustomRewardDefinition> copies = new(StringComparer.OrdinalIgnoreCase);
    internal IEnumerable<CustomRewardDefinition> Items => copies.Values;
    internal string[] Map(IEnumerable<string> ids)
    {
        return ids.Select(id => {
            if (!game.CustomRewards.TryGetValue(id, out var item)) return id;
            item.Validate();
            if (!CustomRewardExport.IsPlayNowCompatible(item)) {
                report?.UnsupportedReward(item);
                if (policy == PlayNowRewardPolicy.Strict) CustomRewardExport.ValidatePlayNow(item);
                if (policy == PlayNowRewardPolicy.SkipUnsupported) return "";
            }
            if (!copies.TryGetValue(id, out var copy)) {
                copy = item.Copy(); copy.Id = Guid.NewGuid().ToString(); copies[id] = copy;
            }
            return copy.Id;
        }).Where(id => id.Length > 0).ToArray();
    }
    internal void Player(Dictionary<string, object> model) => model["talentIds"] = Map((string[])model["talentIds"]);
    internal void EmbedPlayer(CampaignDocument sidecar, Dictionary<string, object> model)
    {
        var ids = (string[])model["talentIds"];
        CustomRewardExport.EmbedPlayNow(sidecar, Items.Where(d => ids.Contains(d.Id, StringComparer.OrdinalIgnoreCase)), policy);
        sidecar.Set("Talents", string.Join(",", ids)); sidecar.Set("Goalie Talents", "");
        // Team talents are already baked into talentIds. Relics are carried by
        // the exported team, not dangling original IDs on a player companion.
        sidecar.Set("Team Talents", ""); sidecar.Set("Relics", ""); sidecar.Set("Team Relics", "");
    }
}
