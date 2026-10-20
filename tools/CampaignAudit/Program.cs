using CampaignCore;
using CampaignCreator;
using System.Text.Json;

string root = Path.GetFullPath(args[0]), campaign = Path.GetFullPath(args[1]);
if (args.Contains("--fill"))
{
    string[] relicPriority = { "Corn dog-stic", "Fossilized Star", "Rubberband", "Brick Wall", "Green Marker", "Red Marker", "Express Delivery", "Benchwarmer Ovr Relic" };
    string[] talentPriority = { "Crit Boost", "Blue Marker Talent", "Green Marker Talent", "Red Marker Talent", "Puck Hunter", "Puck Rocket", "Puckless Rocket", "Sword", "Greater Cooldown", "Cannon Arm", "Growing Anger", "Momentum", "Target Practice", "Passing Domino", "Playmaker", "Goal Crit Accumulator", "Hat Trick", "Inflated Ego", "Silent Hero", "Pressure Cooker", "Point Sniper", "Fast Rebound", "Escalating Crit", "Defensive Deflect", "En Garde!", "Flawless Feeder", "Express Delivery", "Built Different", "ImperviousOnPass", "Spring Board", "Velcro", "Rebound Magnet", "Mega Deflect", "Pass Puncher", "Puck Puncher", "Poke Rage", "Enraged", "Feed", "Avenge Me!", "Enrage On Shot", "Board Bumper", "Blue Line Boost", "Backward Turbo", "Benchwarmer", "Bonecrusher", "Charge Shot", "Curve Ball", "Game Winner Bonus", "Hit Streak", "Zoomer", "Scrambled", "Wind Down", "Wind up", "Bouncy McBounce", "Deadzone", "ShartSlap", "slapShart" };
    var doc = new CampaignDocument("");
    var pools = new Dictionary<string, RewardPoolDefinition>();
    foreach (string kind in new[] { "Relics", "Talents", "Goalie Talents", "Abilities" }) pools[kind] = new() { UseBaseGame = false };
    foreach (string kind in pools.Keys)
    {
        var candidates = RewardCatalog.ReadEntries(root, kind).Values.Where(e => e.Level == 1 && RewardCatalog.OfferedInNormalRun(e.Id))
            .Where(e => !new[] { "Rogue.DoubleReward", "Rogue.StatRewardRelic", "Rogue.ButtRewardTalent" }.Contains(CampaignRewardBudget.Family(kind, e.Id)))
            .Where(e => kind != "Relics" || relicPriority.Contains(e.Asset, StringComparer.OrdinalIgnoreCase))
            .Where(e => kind != "Talents" || talentPriority.Contains(e.Asset, StringComparer.OrdinalIgnoreCase))
            .OrderBy(e => kind == "Relics" ? Array.FindIndex(relicPriority, a => a.Equals(e.Asset, StringComparison.OrdinalIgnoreCase))
                : kind == "Talents" ? Array.FindIndex(talentPriority, a => a.Equals(e.Asset, StringComparison.OrdinalIgnoreCase)) : 0)
            .ThenBy(e => e.Asset, StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var candidate in candidates)
        {
            var before = CampaignRewardBudget.Calculate(root, campaign, pools).Single(b => b.Kind == kind);
            if (before.Errors.Count == 0) break;
            pools[kind].Selected.Add(candidate.Id);
            var after = CampaignRewardBudget.Calculate(root, campaign, pools).Single(b => b.Kind == kind);
            if (after.EligibleFamilies == before.EligibleFamilies) pools[kind].Selected.Remove(candidate.Id);
        }
        pools[kind].Write(doc, kind);
    }
    var errors = CampaignRewardBudget.Calculate(root, campaign, pools).SelectMany(b => b.Errors).ToArray();
    if (errors.Length > 0) throw new Exception(string.Join("\n", errors));
    doc.Save(Path.Combine(campaign, "reward_pools.txt"));
}
var budgets = CampaignRewardBudget.Calculate(root, campaign);
Console.WriteLine(JsonSerializer.Serialize(budgets, new JsonSerializerOptions { WriteIndented = true }));
if (!args.Contains("--read-only")) File.WriteAllText(Path.Combine(campaign, "reward-budget.json"), JsonSerializer.Serialize(budgets, new JsonSerializerOptions { WriteIndented = true }));
var report = new {
    templates = SquadTemplateCatalog.All.Select(t => new { t.Id, t.Name, t.Description, relics = SquadTemplateCatalog.StartingRelics(t.Id), defaults = SquadBehaviors.All.Where(b => SquadBehaviors.Enabled(t.Id, b.Id, _ => "")).Select(b => b.Id) }),
    behaviors = SquadBehaviors.All
};
if (!args.Contains("--read-only")) File.WriteAllText(Path.Combine(campaign, "squad-audit.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
if (budgets.Any(b => b.Errors.Count > 0)) Environment.ExitCode = 1;
