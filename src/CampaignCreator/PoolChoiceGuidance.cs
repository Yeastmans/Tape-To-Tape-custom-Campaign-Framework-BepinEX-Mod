namespace CampaignCreator;

internal static class PoolChoiceGuidance
{
    // Verified against game build 25837690: RewardsStep, CoachEvent,
    // GoalieTalentsSubEvent, PrototypeRewardPoolSubEvent and ChaosEvent3Choice.
    internal static string RewardOffer(string kind) => kind switch
    {
        "Relics" => "Normal offer: 3 choices. Custom selections replace the regular, boss and opening Coach relic pools. Include Chaos relics lets you explicitly offer a Chaos relic through these existing reward menus. " + CatalogExpansion,
        "Talents" => "Normal offer: 3 skater-talent choices. Recruitment, Fan and stat rewards can use this pool. The wizard uses the separate Chaos talents pool. Include Chaos talents expands the selectable catalog. " + CatalogExpansion,
        "Goalie Talents" => "Goalie-talent event: 3 choices from the eligible goalie talents.",
        "Abilities" => "A single ability node rolls 1 ability. Fan #1 can include one new ability among up to 3 mixed choices when its selected player has no ability. Blademaster can offer new abilities to eligible players. Explicit upgrades of owned abilities use the game's upgrade rules.",
        "Chaos Talents" => "Wizard / Chaos event: up to 2 talent choices for its selected skater. Base game pool uses the native Chaos category. Include regular talents expands the selectable catalog. " + CatalogExpansion,
        "Chaos Relics" => "Only applies when the game requests a Chaos relic. The current Wizard offers Chaos talents, so this pool does not add Wizard choices. To offer one through normal relic rewards, select it in Relics using Include Chaos relics. Creator items apply a team curse, then a lasting payoff; Spartan games do not count down the curse.",
        _ => throw new ArgumentException("Unknown reward pool", nameof(kind))
    };
    internal const string RewardEligibility = "Base game pool uses native rewards. Turn it off to draw random rewards only from checked entries. Custom pools keep the exact selected levels, respect owned rewards and skater/goalie roles, and never fall back to unchecked rewards. Explicit upgrades of owned rewards, such as Blademaster upgrades, use the game's upgrade rules separately from these random pools.";
    internal const string CatalogExpansion = "Each pool keeps its own selections. Expanding the catalog adds choices to this list without selecting or copying any rewards. Turning expansion off keeps selected rewards from the other category visible; uncheck those rewards to remove them. Base game eligibility is unchanged.";
    internal const string Bench = "Choose up to 9 draft candidates. The squad template decides how many can join the bench. An empty custom pool offers no draft candidates.";
    internal const string Superstars = "Fill all 3 slots for 3 different starting superstar choices. With 1–2 players, choices repeat; an empty selected pool skips the superstar screen. Each team's behavior controls can also turn the screen off. Offer order is randomized.";
    internal const string MatchRewards = "0–10 reward selections after this match, including Boss nodes. Each normal selection offers 3 choices. 0 skips that normal reward type. GM match reward mix or No-relic reward rules override these counts while On; turn them Off in the player's Squad behaviors to use the saved counts. Bonus-reward relics still apply.";
}
