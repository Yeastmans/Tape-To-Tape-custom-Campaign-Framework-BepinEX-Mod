using System;
using System.Collections.Generic;
using System.Linq;

namespace CampaignCore;

public sealed class SquadBehavior
{
    public string Id { get; }
    public string Name { get; }
    public string Description { get; }
    public string[] Templates { get; }
    public string Key => "Squad Behavior " + Id;
    public string Group => Id switch
    {
        "superstar_screen" or "bench_screen" or "gm_intro" or "oldschool_intro" or "auto_bench" => "Starting choices",
        "four_skaters" or "three_skaters" or "trio_draft" or "two_lines" or "line_stamina" or "no_bench_quests" or "no_bench_snapshots" or "solo_lock" or "gauntlet_draft" or "no_pause_lineup" => "Roster and bench",
        "speedrun" or "eliminator" or "goal_only_mode" or "no_injuries" or "solo_control" or "manual_poke" or "tchel_offside" or "retro_skating" or "violence" or "slapshot_ai" or "sinbin_goal" => "Squad match rules",
        "gm_contracts" or "gm_rewards" or "gm_reroll" or "reserve_goalie" or "stat_rewards" or "solo_rewards" or "no_ability_offers" or "slapshot_rewards" or "gauntlet_scaling" or "no_relic_rewards" => "Recruitment and rewards",
        "gm_map" or "no_generated_gm" or "oldschool_map" or "training_map" or "solo_map" or "gauntlet_opponents" or "referee_opponents" or "no_angus_curse" or "no_injury_curse" or "no_eliminator_curse" => "Maps and opponents",
        _ => "Presentation and identity"
    };
    public SquadBehavior(string id, string name, string description, params string[] templates)
    { Id = id; Name = name; Description = description; Templates = templates; }
}

// Missing settings deliberately inherit. A template change never discards an
// explicit override. Two-line flow enforces its required starting/lineup rules;
// no setting changes a squad's save/profile identity.
public static class SquadBehaviors
{
    public static readonly IReadOnlyList<SquadBehavior> All = new[] {
        new SquadBehavior("superstar_screen", "Superstar selection", "Show the starting superstar selection when the selected pool has players. An empty selected pool skips it.", "vanilla", "defense", "eliminator", "oldschool", "referee", "snes", "slapshot", "solo", "speedrun", "speedy", "statrewards", "tchel", "trio", "violence"),
        new SquadBehavior("bench_screen", "Bench draft", "Show the starting bench draft when the selected pool has players. An empty selected pool skips the draft while keeping Bench Size available for later recruits. With No Effects, turn this On and set Bench Size above zero to allow recruits on the bench; turning it Off on No Effects also sets capacity to zero. Two-line roster flow requires this Off.", "vanilla", "bum", "defense", "eliminator", "referee", "snes", "slapshot", "speedrun", "speedy", "statrewards", "tchel", "trio", "violence"),
        new SquadBehavior("gm_contracts", "Limited free-agent contracts", "Use native GM contract generation, countdown, expiry and replacement eligibility. Starting skaters receive four-match contracts except those recognized as Angus/lead by the squad; the goalie is excluded. New recruitment uses the Free agents player and talent pools. Start a new run to change starting contracts; this does not add GM map nodes or change match rewards.", "generalmanager"),
        new SquadBehavior("gm_rewards", "GM match reward mix", "On: replace the node's saved match rewards with 0 normal talent selections and 1 relic selection. Off: use the node's saved counts. Inherit: On for GM, Off for other presets. Bonus-reward relics still apply.", "generalmanager"),
        new SquadBehavior("gm_reroll", "Refresh rewards after recruitment", "Reroll skater node rewards after a GM signing.", "generalmanager"),
        new SquadBehavior("reserve_goalie", "Reserve goalie", "Allow this playable squad to keep a reserve goalie. Starts Off and applies only to this squad."),
        new SquadBehavior("gm_intro", "Skip squad-menu exit transition", "Skip the squad menu's exit transition when starting a run, as GM does. This does not enable contracts or decide which starting screens appear.", "generalmanager"),
        new SquadBehavior("gm_map", "GM map recruitment rules", "Keep GM recruitment nodes and native recruitment counts on inherited maps. Authored maps keep their saved nodes.", "generalmanager"),
        new SquadBehavior("stat_rewards", "Attribute rewards", "Convert skater rewards and matching Coach/Fan events to attribute rewards.", "statrewards"),
        new SquadBehavior("speedrun", "First-period deadline", "Use Speedrun match endings, winner rules and its opening Coach selection.", "speedrun"),
        new SquadBehavior("eliminator", "Eliminator match rules", "Use money-puck scoring and the associated goal, goalie, replay and match-ending flow.", "eliminator"),
        new SquadBehavior("goal_only_mode", "Goal-target mode only", "Offer only the goal-target game mode for this team. Eliminator and Speedrun require it; turning both rules off allows this restriction to be turned off too.", "eliminator", "speedrun"),
        new SquadBehavior("four_skaters", "Four active skaters", "Limit the active roster to four skaters.", "eliminator"),
        new SquadBehavior("three_skaters", "Three active skaters", "Limit the active roster to three skaters.", "trio"),
        new SquadBehavior("trio_draft", "Trio draft requirement", "Require three active and at least three benched skaters when confirming the starting draft.", "trio"),
        new SquadBehavior("two_lines", "Two-line roster flow", "Use two complete authored lines: ten skaters and a goalie are required before saving. Skips superstar and bench drafts, disables Oldschool's starting choice and intermission lineup changes. Both complete lines remain visible in read-only previews. Fill both lines in Team roster before enabling it.", "lines"),
        new SquadBehavior("line_stamina", "Line stamina", "Enable the native player-team stamina system.", "lines"),
        new SquadBehavior("no_bench_quests", "Disable bench quests", "Disable native bench quest tracking.", "lines"),
        new SquadBehavior("no_bench_snapshots", "Disable bench snapshots", "Disable native bench snapshot restoration.", "lines", "oldschool"),
        new SquadBehavior("no_injuries", "Disable injuries", "Use Oldschool's injury exemption in campaign matches.", "oldschool"),
        new SquadBehavior("oldschool_intro", "Oldschool starting choice", "Use Oldschool's opening superstar and draft sequence.", "oldschool"),
        new SquadBehavior("auto_bench", "Automatic starting bench", "Use the native automatic starting-bench flow.", "allangus", "gauntlet", "lines", "solo"),
        new SquadBehavior("no_generated_gm", "Omit generated GM nodes", "Omit automatic GM placement on inherited maps. Authored map nodes remain explicit.", "lines", "gauntlet", "oldschool"),
        new SquadBehavior("oldschool_map", "Oldschool node replacements", "Replace inherited special nodes using the native Oldschool map rules.", "oldschool"),
        new SquadBehavior("training_map", "Training-node replacements", "Replace inherited special nodes with training nodes.", "statrewards"),
        new SquadBehavior("solo_map", "Solo map rules", "Use the native Solo conversion rules on inherited maps.", "solo"),
        new SquadBehavior("solo_control", "Control only the lead skater", "Use Solo controller assignment and player control.", "solo"),
        new SquadBehavior("solo_camera", "Solo camera", "Use the Solo camera and its camera-setting restrictions.", "solo"),
        new SquadBehavior("solo_lock", "Keep the lead skater active", "Prevent benching the squad's designated Angus/lead skater.", "solo"),
        new SquadBehavior("solo_rewards", "Target the lead skater", "Use Solo targeting for Blademaster and Nurse rewards.", "solo"),
        new SquadBehavior("manual_poke", "Manual pokecheck", "Give skaters Tchel's Mega Pokecheck and manual pokecheck handling.", "tchel"),
        new SquadBehavior("tchel_offside", "Tchel offside rules", "Use Tchel's offside enforcement with its native Knockout penalty. This takes precedence over the ordinary penalty in campaign Game options.", "tchel"),
        new SquadBehavior("no_ability_offers", "Disable new ability offers", "Suppress new-ability choices in Blademaster and Fan prototype rewards.", "tchel"),
        new SquadBehavior("retro_skating", "SNES skating", "Use the native SNES turning behavior for the player team.", "snes"),
        new SquadBehavior("retro_audio", "SNES audio", "Use SNES music and crowd presentation.", "snes"),
        new SquadBehavior("retro_presentation", "SNES match presentation", "Use SNES replay, scoreboard, clock-adjacent penalty timers and skater-introduction behavior.", "snes"),
        new SquadBehavior("violence", "Violence collision rules", "Enable the player skaters' native Violence behavior.", "violence"),
        new SquadBehavior("slapshot_ai", "Slapshot AI", "Use Slapshot-specific breakaway shot choices. Weak non-slapshots and other relic-driven shot choices come from Slapshot Plushie in Team roster → Team relics.", "slapshot"),
        new SquadBehavior("slapshot_rewards", "Slapshot talent weighting", "Use the Slapshot squad's weighted talent preferences.", "slapshot"),
        new SquadBehavior("gauntlet_scaling", "Gauntlet stack scaling", "Scale positive stacking bonuses to 70 percent using the native rule.", "gauntlet"),
        new SquadBehavior("gauntlet_opponents", "Gauntlet opponent growth", "Add the native act-based overall bonus to opposing skaters.", "gauntlet"),
        new SquadBehavior("gauntlet_end", "Gauntlet end-of-match flow", "Use Gauntlet's end-of-match sequence.", "gauntlet"),
        new SquadBehavior("gauntlet_draft", "Gauntlet draft requirement", "Require a complete active roster and at least two benched skaters.", "gauntlet"),
        new SquadBehavior("all_angus", "Treat starting skaters as Angus", "Recognize starting-roster variants as Angus for native squad checks.", "allangus"),
        new SquadBehavior("all_angus_outro", "All Angus outro", "Use All Angus lead-skater selection and staging in the final comic.", "allangus"),
        new SquadBehavior("referee_look", "Referee uniforms", "Apply the native referee appearance to campaign skaters and goalie.", "referee"),
        new SquadBehavior("referee_opponents", "Referee opponent exception", "Use the Referee squad's act-one boss filtering.", "referee"),
        new SquadBehavior("no_angus_curse", "All Angus curse exemption", "Use the All Angus exclusion in the Canada curse roster rules.", "allangus"),
        new SquadBehavior("no_injury_curse", "Oldschool curse exemption", "Use the Oldschool exclusion in the Canada curse rules.", "oldschool"),
        new SquadBehavior("no_eliminator_curse", "Eliminator curse exemption", "Use the Eliminator exclusion in the Canada curse rules.", "eliminator"),
        new SquadBehavior("no_relic_rewards", "No-relic reward rules", "On: replace saved match counts with 2 talent selections for Elite/Boss, 1 for Challenge, and 0 normal relic selections. Off: use saved counts. Bonus-reward relics still apply. Cannot combine with GM match reward mix.", "norelics"),
        new SquadBehavior("sinbin_goal", "Send goal scorer to penalty box", "Use the game's Sin Bin goal rule.", "sinbin"),
        new SquadBehavior("no_pause_lineup", "Disable intermission lineup changes", "Prevent roster swaps between periods, as Double Decker and Sin Bin do. Both teams' lineups remain available as read-only previews.", "lines", "sinbin")
    };
    public static SquadBehavior Find(string id) => All.FirstOrDefault(b => b.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    public static bool Enabled(string template, string id, Func<string, string> read)
    {
        var behavior = Find(id) ?? throw new ArgumentException("Unknown squad behavior: " + id);
        return RequiredValue(template, behavior.Id, read) ?? ConfiguredValue(template, behavior, read);
    }
    public static bool? RequiredValue(string template, string id, Func<string, string> read)
    {
        bool required;
        switch (id.ToLowerInvariant())
        {
            case "superstar_screen":
            case "bench_screen":
            case "oldschool_intro": required = false; break;
            case "no_pause_lineup": required = true; break;
            default: return null;
        }
        return ConfiguredValue(template, Find("two_lines"), read) ? required : (bool?)null;
    }
    private static bool ConfiguredValue(string template, SquadBehavior behavior, Func<string, string> read)
    {
        string value = (read(behavior.Key) ?? "").Trim().ToLowerInvariant();
        if (value == "on" || value == "yes" || value == "true" || (behavior.Id == GoalieFeatureOptions.ReserveBehaviorId && value == "1")) return true;
        if (value == "off" || value == "no" || value == "false") return false;
        return behavior.Templates.Contains(SquadTemplatePolicy.Identity(template), StringComparer.OrdinalIgnoreCase);
    }
    public static IReadOnlyList<string> Conflicts(string template, Func<string, string> read)
    {
        bool On(string id) => Enabled(template, id, read);
        var errors = new List<string>();
        if (On("three_skaters") && On("four_skaters")) errors.Add("Turn off either Three active skaters or Four active skaters.");
        if (On("two_lines") && (On("three_skaters") || On("four_skaters"))) errors.Add("Two-line roster flow requires five skaters per line; turn off the three/four-skater limit.");
        if (On("two_lines") && (On("trio_draft") || On("gauntlet_draft"))) errors.Add("Two-line roster flow skips starting drafts; turn off Trio draft requirement and Gauntlet draft requirement.");
        if (On("trio_draft") && !On("three_skaters")) errors.Add("Trio draft requirement needs Three active skaters enabled.");
        if (On("trio_draft") && On("gauntlet_draft")) errors.Add("Turn off either Trio draft requirement or Gauntlet draft requirement.");
        if (On("eliminator") && On("speedrun")) errors.Add("Turn off either Eliminator match rules or First-period deadline.");
        if (On("eliminator") && !On("four_skaters")) errors.Add("Eliminator match rules enforce a four-skater roster in the game; enable Four active skaters or turn Eliminator match rules off.");
        if (On("no_relic_rewards") && On("gm_rewards")) errors.Add("Turn off either No-relic reward rules or GM match reward mix; both rewrite the same reward counts.");
        if (On("gauntlet_draft") && (On("three_skaters") || On("four_skaters"))) errors.Add("Gauntlet draft requirement checks for five active skaters; turn off the three/four-skater limit or Gauntlet draft requirement.");
        if ((On("eliminator") || On("speedrun")) && !On("goal_only_mode")) errors.Add("Eliminator and First-period deadline require Goal-target mode only.");
        if (On("gm_map") && On("no_generated_gm")) errors.Add("Turn off either GM map recruitment rules or Omit generated GM nodes.");
        int requiredBench = On("trio_draft") ? 3 : On("gauntlet_draft") ? 2 : 0;
        if (requiredBench > 0 && int.TryParse(read("Bench Size"), out int bench) && bench >= 0 && bench < requiredBench)
            errors.Add($"This draft requirement needs room for {requiredBench} bench skaters. Increase Bench Size or turn off the draft requirement.");
        if (On("two_lines") && !string.IsNullOrWhiteSpace(read("Starting Bench Positions")))
            errors.Add("Two-line roster flow needs its ten active line slots. Clear Starting Bench Positions before enabling it.");
        return errors;
    }
}
