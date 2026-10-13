using System;

namespace CampaignCore;

public static class PlayerBrawlOptions
{
    public const string PrinceKey = "Crusader Brawl Prince";
    public const string Consumer = "Fighting.BrawlChallengeTriggers.NotifyBodycheckLanded";
    public static bool CountsAsPrince(CampaignDocument player) =>
        player != null && (player.Get(PrinceKey).Equals("yes", StringComparison.OrdinalIgnoreCase)
            || player.Get(PrinceKey).Equals("true", StringComparison.OrdinalIgnoreCase)
            || player.Get(PrinceKey).Equals("on", StringComparison.OrdinalIgnoreCase));
}
