using CampaignCore;

namespace CampaignCreator;

internal sealed class CustomRewardExportBundle(GameData game)
{
    private readonly Dictionary<string, CustomRewardDefinition> copies = new(StringComparer.OrdinalIgnoreCase);
    internal IEnumerable<CustomRewardDefinition> Items => copies.Values;
    internal string[] Map(IEnumerable<string> ids)
    {
        return ids.Select(id => {
            if (!game.CustomRewards.TryGetValue(id, out var item)) return id;
            CustomRewardExport.ValidatePlayNow(item);
            if (!copies.TryGetValue(id, out var copy)) {
                copy = item.Copy(); copy.Id = Guid.NewGuid().ToString(); copies[id] = copy;
            }
            return copy.Id;
        }).ToArray();
    }
    internal void Player(Dictionary<string, object> model) => model["talentIds"] = Map((string[])model["talentIds"]);
    internal void EmbedPlayer(CampaignDocument sidecar, Dictionary<string, object> model)
    {
        var ids = (string[])model["talentIds"];
        CustomRewardExport.Embed(sidecar, Items.Where(d => ids.Contains(d.Id, StringComparer.OrdinalIgnoreCase)));
        sidecar.Set("Talents", string.Join(",", ids)); sidecar.Set("Goalie Talents", "");
    }
}
