using System.IO.Compression;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CampaignCore;
using CampaignCreator;

string output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
AppDomain.CurrentDomain.UnhandledException += (_, e) => Console.Error.WriteLine("UNHANDLED " + e.ExceptionObject);
int checks = 0;
void Check(bool value, string label) { if (!value) throw new Exception(label); checks++; Console.WriteLine("PASS " + label); }
void Reject(Action action, string label) { try { action(); } catch (ArgumentException) { Check(true, label); return; } throw new Exception(label); }
var briefGoals = new CustomRewardDefinition { Name = "Forty Goal Puck", Kind = CustomRewardDefinition.CustomRelics, Trigger = "Goal value calculation", Effect = CustomRewardEffects.ExtraGoals, Target = CustomRewardEffects.Team, Amount = 39, Duration = "Seconds", Description = "A big score." };
Check(briefGoals.RulesText.Length <= 180 && briefGoals.RulesText.Contains("40 goals") && !briefGoals.RulesText.Contains("native"), "Relic scoring text states the goal value without implementation notes");
Check(briefGoals.FullDescription == "A big score.\n" + briefGoals.RulesText && briefGoals.ToDocument().Get("Description") == briefGoals.Description, "Short relic rules preserve authored descriptions and saved definitions");
var briefStat = new CustomRewardDefinition { Name = "Brief slow", Kind = CustomRewardDefinition.CustomRelics, Trigger = "Opponent shot", Effect = CustomRewardEffects.StatChange, Target = CustomRewardEffects.OpposingTeam, Amount = -10, Stat = "Speed", Chance = 25, Duration = "Seconds", Seconds = 3, MaxStacks = 2, ActivationsPerMatch = 4 };
Check(briefStat.RulesText.Length <= 240 && briefStat.RulesText.Contains("25%") && briefStat.RulesText.Contains("-10 Speed") && briefStat.RulesText.Contains("3s") && briefStat.RulesText.Contains("4") && briefStat.RulesText.Contains("2 stacks") && briefStat.RulesText.Contains("per team per match"), "Short event text retains chance, recipient limits, duration and stacking");
var briefTalent = new CustomRewardDefinition { Name = "Golden talent", Effect = CustomRewardEffects.GoldenHelmet, Trigger = "Helmet equipment", Duration = "Seconds" };
Check(briefTalent.RulesText.Length <= 100 && briefTalent.RulesText.Contains("extra Golden Helmet") && !briefTalent.RulesText.Contains("native"), "Talents use the same concise base-game style as relics");
foreach (var blueEffect in new[] { CustomRewardEffects.BlueLineLaunch, CustomRewardEffects.DefensiveBlueLineKnockout, CustomRewardEffects.OffensiveBlueLineKnockout }) {
    var blue = new CustomRewardDefinition { Name=blueEffect, Kind=CustomRewardDefinition.CustomRelics, Trigger="Native blue-line crossing", Effect=blueEffect, Target=blueEffect == CustomRewardEffects.OffensiveBlueLineKnockout ? CustomRewardEffects.Team : CustomRewardEffects.OpposingTeam, Amount=12, Seconds=3.5f, Duration="Seconds" };
    blue.Validate(false); CustomRewardExport.ValidatePlayNow(blue);
    Check(blue.RulesText.Contains(blueEffect == CustomRewardEffects.BlueLineLaunch ? "force 12" : "3.5s"), "Native blue-line component exposes its own parameter: " + blueEffect);
    Check(!CustomRewardEffects.SupportsKind(blueEffect, CustomRewardDefinition.CustomTalents), "Native blue-line relic stays out of talent kinds: " + blueEffect);
    blue.Trigger="Match won"; Reject(() => blue.Validate(false), "Blue-line movement cannot defer from a completed match: " + blueEffect);
}
var refFrog = new CustomRewardDefinition { Name="Referee response", Kind=CustomRewardDefinition.CustomRelics, Trigger="Team knocks out referee", Effect=CustomRewardEffects.Frog, Target=CustomRewardEffects.OpposingTeam, Duration="Seconds" };
refFrog.Validate(false); CustomRewardExport.ValidatePlayNow(refFrog);
Check(CustomRewardEffects.Targets(refFrog.Kind, refFrog.Trigger, refFrog.Effect).SequenceEqual(new[] { CustomRewardEffects.OpposingTeam }), "Frogeree flow targets the opposing team after a credited referee knockout");
refFrog.Target=CustomRewardEffects.Actor; Reject(() => refFrog.Validate(false), "Team referee event never chooses an arbitrary triggering skater");
var saveClock = new CustomRewardDefinition { Name="Save clock", Kind=CustomRewardDefinition.CustomRelics, Trigger="Opposing goalie save", Effect=CustomRewardEffects.ClockTime, Target=CustomRewardEffects.Clock, Amount=5, Duration="Seconds" };
saveClock.Validate(false); CustomRewardExport.ValidatePlayNow(saveClock);
Check(!CustomRewardEffects.ForTrigger(saveClock.Kind, saveClock.Trigger).Contains(CustomRewardEffects.PuckCurve), "Successful-save team signal supplies no current shot for a flight effect");
var snooze = new CustomRewardDefinition { Name="Longer knockouts", Kind=CustomRewardDefinition.CustomRelics, Trigger="Knockout duration calculation", Effect=CustomRewardEffects.KnockoutDuration, Target=CustomRewardEffects.OpposingTeam, Amount=150, Duration="Seconds" };
snooze.Validate(false); CustomRewardExport.ValidatePlayNow(snooze);
Check(snooze.RulesText.Contains("150%") && snooze.RulesText.Contains("Opponents stay knocked out"), "Knockout duration names its recipients and portable value");
snooze.Amount=90; snooze.Validate(false); Check(true, "Unclamped: knockout duration may shorten native knockouts (90%)");
snooze.Amount=0; Reject(() => snooze.Validate(false), "Knockout duration still rejects 0%");
foreach (var condition in new[] { "While classified as a benchwarmer", "While wearing Golden Helmet against a boss", "While in blue-line shot range" }) {
    var conditionalRule = new CustomRewardDefinition { Name=condition, Kind=condition == "While in blue-line shot range" ? CustomRewardDefinition.CustomTalents : CustomRewardDefinition.CustomRelics, Trigger=condition, Stat="Overall", Target=CustomRewardEffects.Team };
    conditionalRule.Validate(false);
    Check(CustomRewardEffects.ForTrigger(conditionalRule.Kind, condition).Contains(CustomRewardEffects.StatChange), "Specific native stat eligibility remains selectable: " + condition);
}
var refereeSupport = new CustomRewardDefinition { Name="Referee talent", Duration="Seconds", Effect=CustomRewardEffects.RefereeBlockTalent, Trigger="While owned", Target=CustomRewardEffects.Actor };
refereeSupport.Validate(false); CustomRewardExport.ValidatePlayNow(refereeSupport);
Check(!CustomRewardEffects.SupportsKind(refereeSupport.Effect, CustomRewardDefinition.CustomRelics), "Referee support talent stays separate from referee setup relics");
var liquidationBonus = new CustomRewardDefinition { Name="Liquidation payoff", Kind=CustomRewardDefinition.CustomChaosTalents, Stat="Overall", Trigger="While owned", AmountBasis="Per liquidated talent", Target=CustomRewardEffects.Actor };
liquidationBonus.Validate(false);
Check(liquidationBonus.RulesText.Contains("saved count") && liquidationBonus.RulesText.Contains("Liquidation is owned") && !CustomRewardDefinition.IsPreviousMatchBasis(liquidationBonus.AmountBasis), "Liquidation payoff uses its own saved tally while the carrier is owned");
Reject(() => CustomRewardExport.ValidatePlayNow(liquidationBonus), "Saved Liquidation tally rejects Play Now");
liquidationBonus.Stat="Speed"; Reject(() => liquidationBonus.Validate(false), "Liquidation payoff keeps native Overall only");
Check(!CustomRewardDefinition.AmountBasesFor(CustomRewardDefinition.CustomTalents).Contains("Per owned talent"), "Native StatPerTalent excludes itself; unsupported self-inclusive basis stays unavailable");
foreach (var helmetEffect in new[] { CustomRewardEffects.HeavyHelmetAttribute, CustomRewardEffects.IronHelmetAttribute, CustomRewardEffects.PropellerHelmetAttribute, CustomRewardEffects.LancelovHelmetAttribute }) {
    var helmetRule = new CustomRewardDefinition { Name=helmetEffect, Effect=helmetEffect, Trigger="Helmet equipment", Target=CustomRewardEffects.Actor, Duration="Seconds", Stat="Speed", Amount=-23 };
    helmetRule.Validate(false); CustomRewardExport.ValidatePlayNow(helmetRule);
    Check(helmetRule.RulesText.Contains("-23 Speed") && helmetRule.RulesText.Contains("current wearer"), "Helmet rule explains the separate transferable attribute: " + helmetEffect);
    helmetRule.Stat="Overall"; Reject(() => helmetRule.Validate(false), "Helmet rejects attributes outside its native components: " + helmetEffect);
    Check(!CustomRewardEffects.ForTrigger(CustomRewardDefinition.CustomRelics, "Helmet equipment").Contains(helmetEffect), "Helmet ownership remains a native talent: " + helmetEffect);
}
foreach (var aiFlow in new[] { (CustomRewardEffects.AiKarlson, "While without puck"), (CustomRewardEffects.AiPrince, "While opponent has puck"), (CustomRewardEffects.AiKnight, "While holding puck"), (CustomRewardEffects.AiBlock, "While team has puck"), (CustomRewardEffects.AiHome, "While opponent has puck"), (CustomRewardEffects.PreventPredictivePoke, "While without puck") })
    Check(!CustomRewardEffects.ForTrigger(CustomRewardDefinition.CustomTalents, aiFlow.Item2).Contains(aiFlow.Item1), "Impossible possession/AI flow is blocked: " + aiFlow);
foreach (var aiEffect in new[] { CustomRewardEffects.AiKarlson, CustomRewardEffects.AiPrince, CustomRewardEffects.AiKnight, CustomRewardEffects.PreventPredictivePoke }) {
    var aiRule = new CustomRewardDefinition { Name=aiEffect, Effect=aiEffect, Trigger="While owned", Target=CustomRewardEffects.Actor, Duration="Seconds" };
    aiRule.Validate(false); CustomRewardExport.ValidatePlayNow(aiRule);
    Check(!CustomRewardEffects.ForTrigger(CustomRewardDefinition.CustomRelics, "While owned").Contains(aiEffect), "Specialist AI keeps native talent ownership: " + aiEffect);
}
foreach (var item in new[] {
    (CustomRewardEffects.ParticipationBonus, "Unused-player win reward", CustomRewardEffects.Team, CustomRewardDefinition.CustomRelics, false),
    (CustomRewardEffects.BreakawayShotPenalty, "Opposing breakaway shot", CustomRewardEffects.OpposingTeam, CustomRewardDefinition.CustomRelics, true),
    (CustomRewardEffects.BreakawayShotFall, "Opposing breakaway shot", CustomRewardEffects.OpposingTeam, CustomRewardDefinition.CustomRelics, true),
    (CustomRewardEffects.AngusZero, "Match setup", CustomRewardEffects.Team, CustomRewardDefinition.CustomRelics, false),
    (CustomRewardEffects.OpposingBonecrusher, "Match setup", CustomRewardEffects.OpposingTeam, CustomRewardDefinition.CustomRelics, true),
    (CustomRewardEffects.OpposingBriefcase, "Match setup", CustomRewardEffects.OpposingTeam, CustomRewardDefinition.CustomRelics, true),
    (CustomRewardEffects.FreezeOwnRelics, "Match setup", CustomRewardEffects.Team, CustomRewardDefinition.CustomRelics, false),
    (CustomRewardEffects.FrogOwnPair, "Match setup", CustomRewardEffects.Team, CustomRewardDefinition.CustomRelics, true),
    (CustomRewardEffects.MinimumTeamSize, "Match setup", CustomRewardEffects.Team, CustomRewardDefinition.CustomRelics, true),
    (CustomRewardEffects.OpeningCriticals, "Opening team shots", CustomRewardEffects.Team, CustomRewardDefinition.CustomRelics, true),
    (CustomRewardEffects.ReboundCriticals, "Recent team shot", CustomRewardEffects.Actor, CustomRewardDefinition.CustomTalents, true),
    (CustomRewardEffects.HealedInjuryBonus, "Injury recovery", CustomRewardEffects.RecoveringSkater, CustomRewardDefinition.CustomRelics, false),
    (CustomRewardEffects.CelebrationInjuries, "Celebrating scorer knockout", CustomRewardEffects.OpposingTeam, CustomRewardDefinition.CustomRelics, false),
    (CustomRewardEffects.OpeningInjuries, "Opening team knockouts", CustomRewardEffects.OpposingTeam, CustomRewardDefinition.CustomRelics, false),
    (CustomRewardEffects.CyclicInjuries, "Team knockout injury cycle", CustomRewardEffects.OpposingTeam, CustomRewardDefinition.CustomRelics, false),
    (CustomRewardEffects.PuckInjuries, "Puck knockout injury", CustomRewardEffects.OpposingTeam, CustomRewardDefinition.CustomRelics, false),
    (CustomRewardEffects.VoodooAttribute, "Match setup", CustomRewardEffects.OpposingTeam, CustomRewardDefinition.CustomRelics, true),
    (CustomRewardEffects.HotAttribute, "Match setup", CustomRewardEffects.Team, CustomRewardDefinition.CustomRelics, true),
    (CustomRewardEffects.ColdAttribute, "Match setup", CustomRewardEffects.Team, CustomRewardDefinition.CustomRelics, true),
    (CustomRewardEffects.CultistFrog, "Cultist incantation cycle", CustomRewardEffects.Actor, CustomRewardDefinition.CustomTalents, true),
    (CustomRewardEffects.CultistFreeze, "Cultist incantation cycle", CustomRewardEffects.Actor, CustomRewardDefinition.CustomTalents, true),
    (CustomRewardEffects.CultistRory, "Cultist incantation cycle", CustomRewardEffects.Actor, CustomRewardDefinition.CustomTalents, true),
    (CustomRewardEffects.CultistDord, "Cultist incantation cycle", CustomRewardEffects.Actor, CustomRewardDefinition.CustomTalents, true),
    (CustomRewardEffects.CultistJelly, "Cultist incantation cycle", CustomRewardEffects.Actor, CustomRewardDefinition.CustomTalents, true),
    (CustomRewardEffects.CultistCornelius, "Cultist incantation cycle", CustomRewardEffects.Actor, CustomRewardDefinition.CustomTalents, true),
    (CustomRewardEffects.CultistPam, "Cultist incantation cycle", CustomRewardEffects.Actor, CustomRewardDefinition.CustomTalents, true),
    (CustomRewardEffects.DefenseTeleport, "Defensive possession teleport", CustomRewardEffects.Actor, CustomRewardDefinition.CustomTalents, true),
    (CustomRewardEffects.RandomMatchAttribute, "Completed-match attribute roll", CustomRewardEffects.Actor, CustomRewardDefinition.CustomTalents, false),
    (CustomRewardEffects.CenterDefenderChecks, "Centre defender loans", CustomRewardEffects.Actor, CustomRewardDefinition.CustomTalents, true),
    (CustomRewardEffects.BenchCapacity, "Item acquired", CustomRewardEffects.Team, CustomRewardDefinition.CustomRelics, false),
    (CustomRewardEffects.BenchTalentSlot, "Bench talent reward", CustomRewardEffects.Team, CustomRewardDefinition.CustomRelics, false),
    (CustomRewardEffects.LiquidateTalents, "Item acquired", CustomRewardEffects.Actor, CustomRewardDefinition.CustomChaosTalents, false),
    (CustomRewardEffects.DonateBenchAttribute, "Item acquired", CustomRewardEffects.Team, CustomRewardDefinition.CustomRelics, false),
    (CustomRewardEffects.DonateBenchTalents, "Item acquired", CustomRewardEffects.Team, CustomRewardDefinition.CustomRelics, false),
    (CustomRewardEffects.UpgradeAngusTalents, "Item acquired", CustomRewardEffects.Team, CustomRewardDefinition.CustomRelics, false),
    (CustomRewardEffects.UpgradeAngusAbility, "Item acquired", CustomRewardEffects.Team, CustomRewardDefinition.CustomRelics, false),
    (CustomRewardEffects.ScorelessExpiry, "Talent expiry", CustomRewardEffects.Actor, CustomRewardDefinition.CustomChaosTalents, false),
    (CustomRewardEffects.UpgradeOwnTalents, "Item acquired", CustomRewardEffects.Actor, CustomRewardDefinition.CustomChaosTalents, false),
    (CustomRewardEffects.AlternateOverall, "Match cycle", CustomRewardEffects.Actor, CustomRewardDefinition.CustomChaosTalents, false),
    (CustomRewardEffects.RetainPowers, "Bench run retention", CustomRewardEffects.Actor, CustomRewardDefinition.CustomTalents, false),
    (CustomRewardEffects.GrantSpeedTalent, "Item acquired", CustomRewardEffects.Team, CustomRewardDefinition.CustomRelics, false),
    (CustomRewardEffects.GrantCheckingTalent, "Item acquired", CustomRewardEffects.Team, CustomRewardDefinition.CustomRelics, false),
    (CustomRewardEffects.GrantDefenseTalent, "Item acquired", CustomRewardEffects.Team, CustomRewardDefinition.CustomRelics, false),
    (CustomRewardEffects.GrantShootingTalent, "Item acquired", CustomRewardEffects.Team, CustomRewardDefinition.CustomRelics, false),
    (CustomRewardEffects.GrantSlapshotTalent, "Run started", CustomRewardEffects.Team, CustomRewardDefinition.CustomRelics, false),
    (CustomRewardEffects.ShareTalent, "Match setup", CustomRewardEffects.Team, CustomRewardDefinition.CustomRelics, true),
    (CustomRewardEffects.FrogHelmets, "Match setup", CustomRewardEffects.OpposingTeam, CustomRewardDefinition.CustomRelics, true),
    (CustomRewardEffects.OctopusAttacks, "Opposing possession timer", CustomRewardEffects.OpposingTeam, CustomRewardDefinition.CustomRelics, true) }) {
    var lifecycle = new CustomRewardDefinition { Name = item.Item1, Effect = item.Item1, Trigger = item.Item2, Target = item.Item3, Kind = item.Item4, Amount = 2, Radius = 4, Duration = "Seconds" };
    lifecycle.Validate(false);
    Check(CustomRewardEffects.ForTrigger(item.Item4, item.Item2).Contains(item.Item1), "Lifecycle is selectable at its actual native timing: " + item.Item1);
    Check(!CustomRewardEffects.ForTrigger(item.Item4, "Shot").Contains(item.Item1) && !CustomRewardEffects.ForTrigger(item.Item4, "Match won").Contains(item.Item1), "Lifecycle is never deferred from an unrelated event: " + item.Item1);
    Check(CustomRewardDefinition.Kinds.Where(k => k != item.Item4).All(k => !CustomRewardEffects.SupportsKind(item.Item1, k)), "Lifecycle retains its native kind: " + item.Item1);
    Check(lifecycle.RulesText.Length <= 180 && !lifecycle.RulesText.Contains("native"), "Lifecycle has concise wording without implementation notes: " + item.Item1);
    if (item.Item5) { CustomRewardExport.ValidatePlayNow(lifecycle); Check(true, "Live lifecycle can travel to Play Now: " + item.Item1); }
    else Reject(() => CustomRewardExport.ValidatePlayNow(lifecycle), "Campaign lifecycle cannot travel to Play Now: " + item.Item1);
}
foreach (var flow in new[] {
    (CustomRewardEffects.PuckGoalieGhost, "Shot", CustomRewardEffects.Puck, CustomRewardDefinition.CustomTalents),
    (CustomRewardEffects.PostGoal, "Goal-post collision", CustomRewardEffects.Team, CustomRewardDefinition.CustomRelics),
    (CustomRewardEffects.FartForce, "Fart interaction", CustomRewardEffects.Team, CustomRewardDefinition.CustomRelics),
    (CustomRewardEffects.FartRange, "Fart interaction", CustomRewardEffects.Team, CustomRewardDefinition.CustomRelics),
    (CustomRewardEffects.FartKnockouts, "Fart interaction", CustomRewardEffects.OpposingTeam, CustomRewardDefinition.CustomRelics),
    (CustomRewardEffects.PushSkaters, "Random fart timer", CustomRewardEffects.Actor, CustomRewardDefinition.CustomTalents),
    (CustomRewardEffects.PushPuck, "Random fart timer", CustomRewardEffects.Actor, CustomRewardDefinition.CustomTalents),
    (CustomRewardEffects.PushSticks, "Random fart timer", CustomRewardEffects.Actor, CustomRewardDefinition.CustomTalents),
    (CustomRewardEffects.PushSkaters, "Puck fart timer", CustomRewardEffects.Puck, CustomRewardDefinition.CustomRelics),
    (CustomRewardEffects.PushSticks, "Puck fart timer", CustomRewardEffects.Puck, CustomRewardDefinition.CustomRelics) }) {
    var rule = new CustomRewardDefinition { Name=flow.Item1, Effect=flow.Item1, Trigger=flow.Item2, Target=flow.Item3, Kind=flow.Item4, Duration="Seconds", Amount=150, Seconds=2.5f, IntervalMaxSeconds=7.25f, Radius=6,
        IconPng="iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aNn8AAAAASUVORK5CYII=" };
    rule.Validate(); CustomRewardExport.ValidatePlayNow(rule);
    var parsed = CustomRewardDefinition.Parse(rule.ToDocument().ToString());
    Check(parsed.Effect == flow.Item1 && parsed.Trigger == flow.Item2 && parsed.Target == flow.Item3 && parsed.Seconds == 2.5f, "Live interaction survives a portable snapshot: " + flow.Item2 + "/" + flow.Item1);
    if (flow.Item2 == "Random fart timer") {
        Check(parsed.IntervalMaxSeconds == 7.25f && parsed.RulesText.Contains("7.25"), "Random timer preserves and describes its independent maximum");
        parsed.IntervalMaxSeconds=1; Reject(() => parsed.Validate(), "Timer rejects a maximum below its minimum");
    }
}
Check(!CustomRewardEffects.ForTrigger(CustomRewardDefinition.CustomRelics, "Random fart timer").Any()
    && !CustomRewardEffects.ForTrigger(CustomRewardDefinition.CustomTalents, "Puck fart timer").Any(), "Timer sources retain native talent and relic ownership");
Check(!CustomRewardEffects.ForTrigger(CustomRewardDefinition.CustomRelics, "Puck fart timer").Contains(CustomRewardEffects.PushPuck), "Puck-origin timer cannot push its own source puck");
Check(!CustomRewardEffects.ForTrigger(CustomRewardDefinition.CustomTalents, "Pass sent").Contains(CustomRewardEffects.PuckGoalieGhost), "Goalie collision bypass requires a shot");
foreach (var query in new[] { "Boss won", "Act 1 boss won", "Act 2 boss won" }) {
    foreach (var component in new[] { CustomRewardEffects.BossAttribute, CustomRewardEffects.BossSize }) {
        var rule = new CustomRewardDefinition { Name="Boss component", Trigger=query, Effect=component, Duration="Seconds", Target=CustomRewardEffects.Actor, Amount=2, Stat="Accuracy" };
        rule.Validate(false);
        Check(rule.RulesText.Contains(query.ToLowerInvariant().Replace("won", "win")) && rule.RulesText.Contains("permanently", StringComparison.OrdinalIgnoreCase), "Boss rule describes actual event and permanent result: " + query + "/" + component);
        Reject(() => CustomRewardExport.ValidatePlayNow(rule), "Boss progression rejects Play Now: " + query + "/" + component);
        Check(!CustomRewardEffects.ForTrigger(CustomRewardDefinition.CustomRelics, query).Contains(component), "Evolution components retain regular talent ownership");
    }
}
var acquisitionStat = new CustomRewardDefinition { Name="Pickup attribute", Effect=CustomRewardEffects.PickupAttribute, Trigger="Item acquired", Duration="Seconds", Stat="Overall", Amount=3 };
acquisitionStat.Validate(false); Check(acquisitionStat.RulesText.Contains("Consumes the talent") && acquisitionStat.RulesText.Contains("Overall") && acquisitionStat.RulesText.Contains("Permanently"), "Pickup reward describes consumed item and permanent attribute");
Reject(() => CustomRewardExport.ValidatePlayNow(acquisitionStat), "An unapplied pickup reward cannot travel as a Play Now talent");
acquisitionStat.Stat="Critical chance"; Reject(() => acquisitionStat.Validate(false), "Permanent pickup accepts only native base attributes");
var fog = new CustomRewardDefinition { Name="Fog", Effect=CustomRewardEffects.FogScoreboard, Trigger="Controlled possession display", Duration="Seconds" };
fog.Validate(false); CustomRewardExport.ValidatePlayNow(fog); Check(fog.RulesText.Contains("scoreboard") && fog.RulesText.Contains("controlled"), "Fog uses native controlled possession display without invented gameplay changes");
var retentionBounds = new CustomRewardDefinition { Name="Retention", Effect=CustomRewardEffects.RetainPowers, Trigger="Bench run retention", Duration="Seconds", Amount=11 };
retentionBounds.Validate(false); Check(true, "Unclamped: retention accepts the old ceiling + 1 (11)");
retentionBounds.Amount=0; Reject(() => retentionBounds.Validate(false), "Retention still needs at least one power");
var categoryBounds = new CustomRewardDefinition { Name="Categories", Kind=CustomRewardDefinition.CustomRelics, Effect=CustomRewardEffects.GrantSpeedTalent, Trigger="Item acquired", Target=CustomRewardEffects.Team, Duration="Seconds", Amount=3 };
Reject(() => categoryBounds.Validate(false), "Category grants reject nonexistent talent levels");
foreach (var item in new[] {
    (CustomRewardEffects.CopyOpponentRelics, "Match setup", CustomRewardEffects.OpposingTeam, CustomRewardDefinition.CustomRelics),
    (CustomRewardEffects.StealOpponentRelics, "Match setup", CustomRewardEffects.OpposingTeam, CustomRewardDefinition.CustomRelics),
    (CustomRewardEffects.FreezeOpponentRelic, "Match setup", CustomRewardEffects.OpposingTeam, CustomRewardDefinition.CustomRelics),
    (CustomRewardEffects.GoalPlanks, "Match setup", CustomRewardEffects.Rink, CustomRewardDefinition.CustomRelics),
    (CustomRewardEffects.BreakawayAd, "Breakaway display", CustomRewardEffects.Team, CustomRewardDefinition.CustomRelics),
    (CustomRewardEffects.SonicKnockouts, "Pass intercepted", CustomRewardEffects.Puck, CustomRewardDefinition.CustomTalents),
    (CustomRewardEffects.EggExplosion, "Knockout landing", CustomRewardEffects.Actor, CustomRewardDefinition.CustomTalents),
    (CustomRewardEffects.FasterChecks, "Bodycheck movement", CustomRewardEffects.Team, CustomRewardDefinition.CustomRelics),
    (CustomRewardEffects.SpeedOneTimerAccuracy, "Attribute bonus conversion", CustomRewardEffects.Team, CustomRewardDefinition.CustomRelics),
    (CustomRewardEffects.AttributeRewardChoice, "Talent reward choices", CustomRewardEffects.Team, CustomRewardDefinition.CustomRelics),
    (CustomRewardEffects.SpikedRetaliation, "While owned", CustomRewardEffects.Actor, CustomRewardDefinition.CustomTalents),
    (CustomRewardEffects.SpikedInjury, "While owned", CustomRewardEffects.Actor, CustomRewardDefinition.CustomTalents),
    (CustomRewardEffects.FragileBody, "While owned", CustomRewardEffects.Actor, CustomRewardDefinition.CustomChaosTalents),
    (CustomRewardEffects.LoosePuckAttraction, "While owned", CustomRewardEffects.Actor, CustomRewardDefinition.CustomTalents),
    (CustomRewardEffects.HungerRage, "Without puck timer", CustomRewardEffects.Actor, CustomRewardDefinition.CustomTalents),
    (CustomRewardEffects.PossessionExplosion, "Puck possession timer", CustomRewardEffects.EitherCarrier, CustomRewardDefinition.CustomRelics) }) {
    var component = new CustomRewardDefinition { IconPng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aNn8AAAAASUVORK5CYII=", Name = "Native component", Duration = "Seconds", Kind = item.Item4, Effect = item.Item1, Trigger = item.Item2, Target = item.Item3, Amount = 200, Seconds = 7.25f, Radius = 20 };
    component.Validate();
    var parsed = CustomRewardDefinition.Parse(component.ToDocument().ToString());
    Check(parsed.Effect == item.Item1 && parsed.Target == item.Item3 && parsed.Seconds == 7.25f && parsed.Radius == 20, "Native component preserves its timing, target and range: " + item.Item1);
    Check(!CustomRewardEffects.ForTrigger(item.Item4, "Match won").Contains(item.Item1), "Live and query components cannot be deferred from a win: " + item.Item1);
    if (item.Item1 is CustomRewardEffects.AttributeRewardChoice or CustomRewardEffects.SpikedInjury or CustomRewardEffects.FragileBody)
        Reject(() => CustomRewardExport.ValidatePlayNow(component), "Campaign component blocks Play Now: " + item.Item1);
    else { CustomRewardExport.ValidatePlayNow(component); Check(true, "Match component permits Play Now: " + item.Item1); }
}
Check(!CustomRewardEffects.ForTrigger(CustomRewardDefinition.CustomTalents, "While holding puck").Contains(CustomRewardEffects.LoosePuckAttraction)
    && !CustomRewardEffects.ForTrigger(CustomRewardDefinition.CustomTalents, "While opponent has puck").Contains(CustomRewardEffects.LoosePuckAttraction), "A continuous loose-puck magnet cannot use conditions requiring possession");
Check(!CustomRewardEffects.SupportsKind(CustomRewardEffects.AttributeRewardChoice, CustomRewardDefinition.CustomTalents)
    && !CustomRewardEffects.SupportsKind(CustomRewardEffects.LoosePuckAttraction, CustomRewardDefinition.CustomRelics)
    && !CustomRewardEffects.SupportsKind(CustomRewardEffects.SpikedRetaliation, CustomRewardDefinition.CustomChaosTalents), "New native components retain talent, Chaos and relic boundaries");
var armor = new CustomRewardDefinition { Name="Armor injury", Duration="Seconds", Effect=CustomRewardEffects.SpikedInjury, Trigger="While owned", Amount=200 };
Check(armor.RulesText.Contains("200%"), "Separate injury component describes its configured multiplier");
armor.Amount=-1; Reject(() => armor.Validate(false), "Negative injury multiplier is rejected");

var d = new CustomRewardDefinition { Name = "Hat trick cannon", Trigger = "Goal", Threshold = 3, MaxStacks = 2 };
Check(d.RulesText.Contains("scores 3 goals") && d.RulesText.Contains("for this run") && d.RulesText.Contains("+10 Shot Power") && d.RulesText.Contains("2 stacks"), "Descriptions explain the trigger, value, duration and stack cap");
var timedText = new CustomRewardDefinition { Name="Pickup", Trigger="Puck pickup", Duration="Seconds", Seconds=1, MaxStacks=1 };
Check(timedText.RulesText.Contains("picks up the puck") && timedText.RulesText.Contains("for 1s.") && timedText.RulesText.Contains("Does not stack.") && timedText.RulesText.Contains("Refreshes duration"), "Timed descriptions retain event wording, duration and refresh behavior");
Check(CustomRewardDefinition.Kinds.SequenceEqual(new[] { "Custom Talents", "Custom Chaos Talents", "Custom Goalie Talents", "Custom Relics" }), "Creator has four matching custom pools");
Check(CustomRewardEffects.ForTrigger(CustomRewardDefinition.CustomTalents, "Match rewards").SequenceEqual(new[] { CustomRewardEffects.TalentRewards }),
    "Regular talents retain native Butt Reward talent picks without acquiring relic reward picks");
Check(!CustomRewardEffects.SupportsKind(CustomRewardEffects.Rerolls, CustomRewardDefinition.CustomTalents)
    && !CustomRewardEffects.SupportsKind(CustomRewardEffects.ClockTime, CustomRewardDefinition.CustomTalents)
    && !CustomRewardEffects.SupportsKind(CustomRewardEffects.Freeze, CustomRewardDefinition.CustomTalents),
    "Relic rerolls, clock changes and freezing are excluded from regular talents");
Check(!CustomRewardEffects.SupportsKind(CustomRewardEffects.PuckCurve, CustomRewardDefinition.CustomRelics)
    && !CustomRewardEffects.SupportsKind(CustomRewardEffects.CopyFreeAgentLook, CustomRewardDefinition.CustomRelics)
    && !CustomRewardEffects.SupportsKind(CustomRewardEffects.AiChase, CustomRewardDefinition.CustomRelics),
    "Relics cannot take regular-talent flight, Faceless or positioning effects");
Check(!CustomRewardEffects.SupportsKind(CustomRewardEffects.Enrage, CustomRewardDefinition.CustomChaosTalents)
    && CustomRewardEffects.SupportsKind(CustomRewardEffects.BreakStick, CustomRewardDefinition.CustomChaosTalents)
    && CustomRewardEffects.SupportsKind(CustomRewardEffects.Injure, CustomRewardDefinition.CustomChaosTalents),
    "Chaos choices follow their native components, including stick breaking and injuries");
Check(CustomRewardDefinition.Kinds.Where(k => k != CustomRewardDefinition.CustomGoalieTalents)
    .All(k => !CustomRewardEffects.SupportsKind(CustomRewardEffects.CatchSaves, k)), "Native goalie catching remains exclusive to goalie talents");
Check(CustomRewardEffects.GoalieConditionEffects.All(e => CustomRewardEffects.ForTrigger(CustomRewardDefinition.CustomGoalieTalents, "While owned").Contains(e)
    && !CustomRewardEffects.ForTrigger(CustomRewardDefinition.CustomGoalieTalents, "Goalie receives shot").Contains(e)
    && !CustomRewardEffects.SupportsKind(e, CustomRewardDefinition.CustomRelics)),
    "Goalie catching, retrieval speed and stick decisions are goalie-only conditional mechanics");
foreach (string effectName in new[] { CustomRewardEffects.CopyGoalieAbility, CustomRewardEffects.CopyGoalieTalents }) {
    Check(CustomRewardEffects.ForTrigger(CustomRewardDefinition.CustomGoalieTalents, "Match started").Contains(effectName)
        && !CustomRewardEffects.ForTrigger(CustomRewardDefinition.CustomGoalieTalents, "While owned").Contains(effectName)
        && !CustomRewardEffects.ForTrigger(CustomRewardDefinition.CustomGoalieTalents, "Match won").Contains(effectName),
        "Goalie copying is a live event action, separate from conditions and campaign completion: " + effectName);
    Check(CustomRewardEffects.BaseTargets(CustomRewardDefinition.CustomGoalieTalents, "Match started", effectName).SequenceEqual(new[] { CustomRewardEffects.Actor })
        && CustomRewardDefinition.Kinds.Where(k => k != CustomRewardDefinition.CustomGoalieTalents).All(k => !CustomRewardEffects.SupportsKind(effectName, k)),
        "Goalie copying stays on the owning goalie and native kind: " + effectName);
}
Check(CustomRewardDefinition.Kinds.All(k => CustomRewardEffects.ForwardConditionEffects.Where(e => CustomRewardEffects.SupportsKind(e, k))
    .All(e => CustomRewardEffects.TriggersForKind(k).Any(t => CustomRewardTriggers.IsCondition(t) && CustomRewardEffects.ForTrigger(k, t).Contains(e)) && !CustomRewardEffects.ForTrigger(k, "Stick broken").Contains(e))),
    "Equipment and bodycheck restrictions require an ongoing condition, not a one-off event");
Check(CustomRewardEffects.ForTrigger(CustomRewardDefinition.CustomTalents, "Shot rebounds off goalie").Contains(CustomRewardEffects.AttractPuck)
    && !CustomRewardEffects.ForTrigger(CustomRewardDefinition.CustomTalents, "Match started").Contains(CustomRewardEffects.AttractPuck)
    && !CustomRewardEffects.ForTrigger(CustomRewardDefinition.CustomTalents, "Pass received").Contains(CustomRewardEffects.AttractPuck)
    && !CustomRewardEffects.ForTrigger(CustomRewardDefinition.CustomRelics, "Shot rebounds off goalie").Contains(CustomRewardEffects.AttractPuck),
    "Attraction requires a current free puck and stays in its native talent kind");
Check(!CustomRewardEffects.ForTrigger(CustomRewardDefinition.CustomTalents, "Shot rebounds off goalie").Contains(CustomRewardEffects.PuckCurve),
    "A rebound cannot start an effect that requires a fresh shot");
Check(CustomRewardEffects.ForTrigger(CustomRewardDefinition.CustomRelics, "While owned").Contains(CustomRewardEffects.ExtraSticks)
    && !CustomRewardEffects.ForTrigger(CustomRewardDefinition.CustomRelics, "While owned").Contains(CustomRewardEffects.ForbidCheck),
    "Relics retain native reserve-stick mechanics without acquiring No Hit");
Check(CustomRewardDefinition.Kinds.All(k => CustomRewardEffects.TriggersForKind(k).All(t => CustomRewardEffects.ForTrigger(k, t).Any())),
    "Creator trigger lists contain no empty effect choices after native-kind filtering");
Check(CustomRewardAssignments.TeamTalents(new CampaignDocument("Talents = first, shared\nTeam Talents = shared, second, none")).SequenceEqual(new[] { "first", "shared", "second" }),
    "Campaign and export share team talent inheritance, combining both fields without duplicates or None");
Check(CustomRewardDefinition.Kinds.Where(k => k != CustomRewardDefinition.CustomRelics).All(k => !CustomRewardTriggers.ForKind(k).Contains("Match setup")), "Rink setup belongs exclusively to team relics, never player talents");
Check(CustomRewardTriggers.ForKind(CustomRewardDefinition.CustomGoalieTalents).Contains("Goalie faces shot on net")
    && !CustomRewardTriggers.ForKind(CustomRewardDefinition.CustomTalents).Contains("Goalie faces shot on net")
    && !CustomRewardEffects.ForTrigger(CustomRewardDefinition.CustomGoalieTalents, "Goalie faces shot on net").Any(CustomRewardEffects.IsPuck),
    "Facing a registered shot remains a goalie event and cannot change a completed puck launch");
Check(CustomRewardTriggers.ForKind(CustomRewardDefinition.CustomGoalieTalents).Contains("Period started")
    && CustomRewardTriggers.ForKind(CustomRewardDefinition.CustomGoalieTalents).Contains("Opponent breakaway"),
    "Native period and opposing-breakaway timing is available to goalie rules");
Check(CustomRewardEffects.ForTrigger(CustomRewardDefinition.CustomRelics, "Injury attempted").Count(CustomRewardEffects.IsInjuryPrevention) == 5
    && !CustomRewardEffects.ForTrigger(CustomRewardDefinition.CustomTalents, "Injury received").Any(CustomRewardEffects.IsInjuryPrevention),
    "Injury prevention acts before an injury and is never offered after the injury happened");
Check(CustomRewardEffects.BaseTargets(CustomRewardDefinition.CustomRelics, "Injury attempted", CustomRewardEffects.PreventLeg).SequenceEqual(new[] { CustomRewardEffects.Team }),
    "An injury-prevention relic protects the team");
Check(CustomRewardEffects.ForTrigger(CustomRewardDefinition.CustomGoalieTalents, "While owned").Contains(CustomRewardEffects.CatchSaves)
    && !CustomRewardEffects.ForTrigger(CustomRewardDefinition.CustomTalents, "While owned").Contains(CustomRewardEffects.CatchSaves)
    && !CustomRewardEffects.ForTrigger(CustomRewardDefinition.CustomGoalieTalents, "Goalie receives shot").Contains(CustomRewardEffects.CatchSaves),
    "Goalie catching is an eligible-save rule, not a skater effect or a late shot-hit reaction");
Check(!CustomRewardEffects.ForTrigger(CustomRewardDefinition.CustomRelics, "While skating backward").Contains(CustomRewardEffects.CatchSaves)
    && !CustomRewardEffects.Stats(CustomRewardDefinition.CustomRelics, CustomRewardEffects.Team, "While injured").Any(s => s.StartsWith("Goalie ")),
    "Forward-only conditions cannot expose rules that require the goalie to satisfy them");
foreach (string status in new[] { CustomRewardEffects.Freeze, CustomRewardEffects.Dance, CustomRewardEffects.ThrowStick })
    Check(!CustomRewardEffects.Targets(CustomRewardDefinition.CustomTalents, "Knocked out", status).Contains(CustomRewardEffects.Actor), "An unconscious actor cannot perform this immediate effect: " + status);
var progress = new CustomRewardProgress();
progress.ObserveOwnership("team:A", new[] { "rink:first", "rink:second" });
Check(progress.Acquired("team:A", "rink:second") > progress.Acquired("team:A", "rink:first"), "Pre-existing items retain their native loadout order for priority");
progress.ObserveOwnership("team:A", new[] { "rink:first", "rink:second", "rink:latest" });
progress.ObserveOwnership("team:B", new[] { "rink:opponent" });
Check(progress.Acquired("team:A", "rink:latest") > progress.Acquired("team:B", "rink:opponent"), "A newly observed opponent loadout does not replace the actual latest acquisition");
string order = progress.Save(); progress.Clear(); progress.Load(order);
Check(progress.Acquired("team:A", "rink:latest") > progress.Acquired("team:A", "rink:second"), "Acquisition priority survives save and Continue");
progress.ObserveOwnership("team:A", new[] { "rink:second", "rink:latest" });
progress.ObserveOwnership("team:A", new[] { "rink:second", "rink:latest", "rink:first" });
Check(progress.Acquired("team:A", "rink:first") > progress.Acquired("team:A", "rink:latest"), "Reacquiring a removed item gives it the latest priority");
progress.Clear();
bool Fire(string actor = "player:A", string recipient = "player:A", double time = 0) => progress.Signal(d, d.Trigger, actor, actor, recipient, time);
int Bonus(string actor = "player:A", string stat = "Shot Power", double time = 0, bool owned = true) => progress.Bonus(actor, "team:A", stat, time, (_, _) => owned);
Check(!progress.Signal(d, "Assist", "player:A", "player:A", "player:A", 0), "Different events do not activate a rule");
Check(!Fire() && !Fire() && Fire() && Bonus() == 10, "Three goals activate the holder's reward exactly once");
Check(!Fire() && Bonus("player:B") == 0 && Bonus(stat: "Speed") == 0 && Bonus(owned: false) == 0, "Limits, recipient, stat, and ownership are enforced");
progress.BeginMatch(); Check(Fire() == false && !Fire() && Fire() && Bonus() == 20, "Run effects persist and activation counts reset between matches");
progress.BeginMatch(); Fire(); Fire(); Fire(); Check(Bonus() == 20, "Stack cap prevents unlimited gains");
string saved = progress.Save(); progress.Clear(); progress.Load(saved); Check(Bonus() == 20, "Saved run effects reload");
Reject(() => progress.Load("bad"), "Malformed progress is rejected"); Check(Bonus() == 20, "Failed load preserves current progress");
progress.Clear(); d.Duration = "Seconds"; d.Threshold = 1; d.ActivationsPerMatch = 9; d.Seconds = 5;
Fire(time: 10); Check(Bonus(time: 14) == 10 && Bonus(time: 15) == 0, "Timed effect expires at its boundary");
Fire(time: 20); Check(Bonus(time: 20) == 10, "Expired stacks do not return on reactivation");
Fire(time: 23); Check(Bonus(time: 26) == 20 && Bonus(time: 28) == 0, "Reactivation refreshes the active stack duration");
progress.Clear(); d.Duration = "Rest of period"; Fire(recipient: "team:A");
Check(Bonus("player:B") == 10, "Whole-team recipient includes teammates"); progress.EndPeriod(); Check(Bonus() == 0, "Period effects expire");
d.Duration = "Rest of match"; Fire(); Check(!progress.Save().Contains("E\t"), "Temporary effects are not persisted"); progress.BeginMatch(); Check(Bonus() == 0, "Match effects expire");
d.Kind = CustomRewardDefinition.CustomRelics; d.Duration = "Rest of run";
progress.Signal(d, d.Trigger, "player:A", "team:A", "player:A", 0);
Check(progress.Bonus("player:A", "team:B", "Shot Power", 0, (_, _) => true) == 0,
    "A relic effect does not follow a recipient onto an opposing team");

AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
void Pump() { for (int i = 0; i < 4; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); } Dispatcher.UIThread.RunJobs(); }
IEnumerable<T> Find<T>(Window w) where T : Visual => w.GetVisualDescendants().OfType<T>();
void Click(Button b) { b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(); }
void Frame(Window w, string name) { Pump(); using var frame = w.CaptureRenderedFrame(); frame!.Save(Path.Combine(output, name + ".png")); }
const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
string root = Path.Combine(output, "fixture-" + Guid.NewGuid().ToString("N")), campaign = Path.Combine(root, "campaigns", "Creator Cup");
Directory.CreateDirectory(campaign); File.WriteAllText(Path.Combine(campaign, "campaign.txt"), "Act Sequence = 1\nSquad Template = No Effects\n");
var window = new Window { Width = 1320, Height = 860 };
var workspace = new CampaignWorkspace(window, root, campaign, () => { }, _ => { }, () => Task.CompletedTask);
window.Content = workspace; window.Show();
typeof(CampaignWorkspace).GetMethod("ShowSection", Private)!.Invoke(workspace, [5]); Pump();
var tabs = Find<TabControl>(window).Single(t => t.Name == "RewardSections");
Check(tabs.Items.OfType<TabItem>().Select(t => t.Header!.ToString()).SequenceEqual(new[] { "Relics", "Regular talents", "Goalie Talents", "Skater abilities", "Chaos talents", "Chaos Relics", "Custom Talents", "Custom Chaos Talents", "Custom Goalie Talents", "Custom Relics", "Creator" }), "Creator, four custom pools and six native pools coexist");
tabs.SelectedIndex = 10; Pump();
var creator = (Control)((TabItem)tabs.Items[10]!).Content!;
var creatorType = creator.GetType();
TextBox Name() => Find<TextBox>(window).Single(t => t.Name == "CustomRewardName");
ComboBox Kind() => Find<ComboBox>(window).Single(t => t.Name == "CustomRewardKind");
Button Save() => Find<Button>(window).Single(t => t.Name == "SaveCustomReward");
Check(Kind().Items.Cast<string>().SequenceEqual(CustomRewardDefinition.Kinds), "Creator includes goalie talents and excludes abilities and Chaos relics");
ComboBox Trigger() => Find<ComboBox>(window).Single(t => t.Name == "CustomRewardTrigger");
ComboBox Effect() => Find<ComboBox>(window).Single(t => t.Name == "CustomRewardEffect");
ComboBox Target() => Find<ComboBox>(window).Single(t => t.Name == "CustomRewardTarget");
Trigger().SelectedItem = "Match won"; Pump();
Check(Effect().Items.Cast<string>().SequenceEqual(new[] { CustomRewardEffects.StatChange }), "Match wins cannot offer on-ice effects or silently defer them");
Trigger().SelectedItem = "Check"; Effect().SelectedItem = CustomRewardEffects.Knockout; Pump();
Check(Target().Items.Cast<string>().Contains(CustomRewardEffects.Opponent), "Contact triggers offer their actual event target");
Effect().SelectedItem = CustomRewardEffects.Boost; Pump();
Check(Find<NumericUpDown>(window).Single(n => n.Name == "CustomRewardSeconds").Value <= 5, "Choosing skating boost starts with a valid native duration");
Effect().SelectedItem = CustomRewardEffects.Knockout; Pump();
Trigger().SelectedItem = "Hat trick"; Pump();
Check(!Target().Items.Cast<string>().Contains(CustomRewardEffects.Opponent) && Target().Items.Cast<string>().Contains(CustomRewardEffects.Nearby), "Hat trick offers nearby opponents without inventing a contact target");
Kind().SelectedItem = CustomRewardDefinition.CustomGoalieTalents; Pump();
Check(Trigger().Items.Cast<string>().Contains("Goalie receives shot") && !Trigger().Items.Cast<string>().Contains("Hat trick"), "Goalie triggers follow goalie events and ownership");
Trigger().SelectedItem = "Goalie receives shot"; Effect().SelectedItem = CustomRewardEffects.Enrage; Target().SelectedItem = CustomRewardEffects.Team; Pump();
Frame(window, "goalie-flow-1320");
Click(Find<Button>(window).Single(b => b.Content?.ToString() == "Discard draft"));
Name().Text = "Hat trick cannon"; Pump();
Check(workspace.HasUnsavedChanges, "An unfinished Creator draft participates in campaign dirty tracking");
Frame(window, "creator-1320"); window.Width = 960; window.Height = 620; Pump(); Frame(window, "creator-960");
foreach (var tab in tabs.Items.OfType<TabItem>()) {
    var point = tab.TranslatePoint(default, window);
    Check(point != null && point.Value.X >= 0 && point.Value.X + tab.Bounds.Width <= window.ClientSize.Width + 1, "Tab remains within window: " + tab.Header);
}
Click(Save());
string rewardFolder = Path.Combine(campaign, CustomRewardDefinition.Folder);
var authored = CustomRewardDefinition.LoadFolder(rewardFolder).Single();
Check(authored.Name == "Hat trick cannon" && authored.Trigger == "Hat trick" && authored.UsesNamedIcon, "Save creates a native-trigger definition with a named fallback icon");
Check(CustomRewardDefinition.LoadFolder(Path.Combine(root, "library", "rewards")).Single().Id == authored.Id, "Library and campaign copies share stable identity");
File.WriteAllBytes(Path.Combine(output, "named-icon.png"), Convert.FromBase64String(authored.IconPng));
var copy = authored.Copy(); copy.Name = "Edited library name"; copy.Save(Path.Combine(root, "library", "rewards"));
Check(CustomRewardDefinition.LoadFolder(rewardFolder).Single().Name == "Hat trick cannon", "Library edits do not change existing campaign copies");
tabs.SelectedIndex = 6; Pump();
var rewardCheck = Find<CheckBox>(window).Single(c => c.Tag as string == authored.Id);
Check(rewardCheck.IsEnabled && rewardCheck.IsChecked != true, "Custom item is listed unchecked by default and can be ticked directly");
rewardCheck.IsChecked = true; Pump();
Check(rewardCheck.IsChecked == true, "Ticking the custom item enables it");
tabs.SelectedIndex = 1; Pump();
Check(Find<CheckBox>(window).Single(c => c.Name == "BaseGamePool").IsChecked == false, "Custom opt-in switches normal talents to explicit choices");
Check(!Find<CheckBox>(window).Any(c => c.Tag as string == authored.Id), "Native pool rows remain separate from custom rows");
tabs.SelectedIndex = 4; Pump();
Check(Find<CheckBox>(window).Single(c => c.Name == "BaseGamePool").IsChecked == true, "Ordinary custom talents do not opt in at the Wizard");
tabs.SelectedIndex = 1; Pump();
Find<CheckBox>(window).Single(c => c.Name == "BaseGamePool").IsChecked = true; Pump();
tabs.SelectedIndex = 6; Pump();
rewardCheck = Find<CheckBox>(window).Single(c => c.Tag as string == authored.Id);
Check(rewardCheck.IsChecked != true && rewardCheck.IsEnabled, "Selecting Base game clears custom ticks while leaving them editable");
rewardCheck.IsChecked = true; Pump();
Console.WriteLine("CHECKPOINT rendering selected pool");
Frame(window, "pool-960");
Console.WriteLine("CHECKPOINT saving selected pool");
Click(Find<Button>(window).Single(b => b.Content?.ToString() == "Save campaign"));
Check(File.ReadAllText(Path.Combine(campaign, "reward_pools.txt")).Contains(authored.Id), "Pool save preserves custom reward ID");

tabs.SelectedIndex = 10; Pump();
Click(Find<Button>(window).Single(b => b.Content?.ToString() == "New talent / relic"));
Name().Text = "Victory Cannon"; Kind().SelectedItem = CustomRewardDefinition.CustomRelics;
Find<ComboBox>(window).Single(c => c.Name == "CustomRewardTrigger").SelectedItem = "Match won"; Pump();
window.Width = 1320; window.Height = 860; Frame(window, "relic-creator-1320");
Click(Save());
var victory = CustomRewardDefinition.LoadFolder(rewardFolder).Single(r => r.IsRelic);
Check(victory.Trigger == "Match won" && victory.Duration == "Rest of run", "Match wins save as a lasting reward rule");
tabs.SelectedIndex = 9; Pump();
Find<CheckBox>(window).Single(c => c.Tag as string == victory.Id).IsChecked = true; Pump();
Click(Find<Button>(window).Single(b => b.Content?.ToString() == "Save campaign"));
var configuredPools = new CampaignDocument(File.ReadAllText(Path.Combine(campaign, "reward_pools.txt")));
Check(configuredPools.Get("Selected Custom Relics").Contains(victory.Id) && !configuredPools.Get("Selected Relics").Contains(victory.Id), "Custom relic selections save in their own pool");
tabs.SelectedIndex = 10; Pump();
Click(Find<Button>(window).Single(b => b.Content?.ToString() == "New talent / relic")); Name().Text = "PNG relic"; Kind().SelectedItem = CustomRewardDefinition.CustomRelics;
var iconType = typeof(CampaignWorkspace).Assembly.GetType("CampaignCreator.CustomRewardIcon")!;
byte[] png = (byte[])iconType.GetMethod("ReadPng", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [Path.Combine(output, "named-icon.png")])!;
creatorType.GetField("png", Private)!.SetValue(creator, png); Click(Save());
var withPng = CustomRewardDefinition.LoadFolder(rewardFolder).Single(r => r.Name == "PNG relic");
Check(!withPng.UsesNamedIcon && Convert.FromBase64String(withPng.IconPng).SequenceEqual(png), "Imported PNG is retained byte for byte");
Reject(() => CustomRewardDefinition.ValidatePng(png[..24]), "Truncated PNG rejected");
var huge = (byte[])png.Clone(); huge[16] = 127; Reject(() => CustomRewardDefinition.ValidatePng(huge), "Oversized PNG dimensions rejected");
var invalid = authored.Copy(); invalid.Amount = 0; Reject(() => invalid.Validate(), "Zero stat change rejected");
invalid = authored.Copy(); invalid.Kind = "Abilities"; Reject(() => invalid.Validate(), "Unsupported creation type rejected");
invalid = authored.Copy(); invalid.Name = "Bad\nName"; Reject(() => invalid.Validate(), "Multiline item name rejected");
invalid = authored.Copy(); invalid.Trigger = "Match won"; invalid.Effect = CustomRewardEffects.Knockout;
Reject(() => invalid.Validate(), "Imported incompatible trigger/effect definitions are rejected by the shared runtime contract");
invalid = authored.Copy(); invalid.Trigger = "Hat trick"; invalid.Effect = CustomRewardEffects.Knockout; invalid.Target = CustomRewardEffects.Opponent; invalid.Duration = "Seconds";
Reject(() => invalid.Validate(), "An event without another player cannot manufacture an event target");
foreach (string shotEffect in new[] { CustomRewardEffects.PuckGhost, CustomRewardEffects.PuckCurve, CustomRewardEffects.PuckSlow, CustomRewardEffects.PuckSpin }) {
    Check(CustomRewardEffects.ForTrigger(CustomRewardDefinition.CustomTalents, "Shot").Contains(shotEffect)
        && !CustomRewardEffects.ForTrigger(CustomRewardDefinition.CustomTalents, "Pass sent").Contains(shotEffect)
        && !CustomRewardEffects.ForTrigger(CustomRewardDefinition.CustomTalents, "Goal").Contains(shotEffect), "Shot-flight capability requires a current shot: " + shotEffect);
}
Check(CustomRewardEffects.ForTrigger(CustomRewardDefinition.CustomRelics, "Match setup").Contains(CustomRewardEffects.GolfRink)
    && !CustomRewardEffects.ForTrigger(CustomRewardDefinition.CustomRelics, "Match won").Any(CustomRewardEffects.IsRink), "Rink changes are restricted to native match setup");
foreach (string netEffect in new[] { CustomRewardEffects.NetWidth, CustomRewardEffects.NetHeight, CustomRewardEffects.SoccerNetLook }) {
    Check(CustomRewardEffects.ForTrigger(CustomRewardDefinition.CustomRelics, "Match setup").Contains(netEffect)
        && !CustomRewardEffects.ForTrigger(CustomRewardDefinition.CustomRelics, "Match started").Contains(netEffect)
        && CustomRewardDefinition.Kinds.Where(k => k != CustomRewardDefinition.CustomRelics).All(k => !CustomRewardEffects.SupportsKind(netEffect, k)),
        "Net setup component stays separate and relic-only: " + netEffect);
}
Check(CustomRewardEffects.ForTrigger(CustomRewardDefinition.CustomRelics, "Match rewards").SequenceEqual(new[] { CustomRewardEffects.TalentRewards, CustomRewardEffects.RelicRewards }), "Talent and relic reward counts are separate effects");
var fraction = authored.Copy(); fraction.Duration = "Seconds"; fraction.Seconds = .05f;
foreach (string scaling in CustomRewardEffects.ScalingConditionEffects) {
    Check(CustomRewardDefinition.Kinds.All(k => CustomRewardEffects.ForTrigger(k, "While owned").Contains(scaling) == CustomRewardEffects.SupportsKind(scaling, k)
        && !CustomRewardEffects.ForTrigger(k, "Goal").Contains(scaling)), "Scaling is a native-kind condition, not a delayed goal effect: " + scaling);
}
var scalingRule = fraction.Copy(); scalingRule.Kind = CustomRewardDefinition.CustomChaosTalents; scalingRule.Trigger = "While owned";
scalingRule.Effect = CustomRewardEffects.BonusMultiplier; scalingRule.Stat = "Checking"; scalingRule.Amount = 150;
scalingRule.Validate(); Check(CustomRewardDefinition.Parse(scalingRule.ToDocument().ToString()).Amount == 150, "Chaos bonus multiplier retains its editable strength");
scalingRule.Stat = "Shot Power"; Reject(() => scalingRule.Validate(), "Original Puck's shot-power multiplier cannot become a Chaos talent");
scalingRule.Kind = CustomRewardDefinition.CustomRelics; scalingRule.Validate();
scalingRule.Stat = "Speed"; Reject(() => scalingRule.Validate(), "Original Skate's speed multiplier cannot become a relic");
Check(CustomRewardEffects.ForTrigger(CustomRewardDefinition.CustomRelics, "Pass sent").Contains(CustomRewardEffects.PassUninterceptable)
    && !CustomRewardEffects.ForTrigger(CustomRewardDefinition.CustomRelics, "Pass received").Contains(CustomRewardEffects.PassUninterceptable)
    && CustomRewardEffects.ForTrigger(CustomRewardDefinition.CustomTalents, "Pass sent").Contains(CustomRewardEffects.PassUninterceptable)
    && !CustomRewardEffects.ForTrigger(CustomRewardDefinition.CustomChaosTalents, "Pass sent").Contains(CustomRewardEffects.PassUninterceptable), "Flawless Feeder and Invisible Thread permit uninterceptable passes only at sending and in their native kinds");
Check(CustomRewardEffects.ForTrigger(CustomRewardDefinition.CustomGoalieTalents, "Goalie blocker rebound").Contains(CustomRewardEffects.GoalieHeadshot)
    && !CustomRewardEffects.ForTrigger(CustomRewardDefinition.CustomGoalieTalents, "Goalie picks up puck").Contains(CustomRewardEffects.GoalieHeadshot), "Targeted goalie rebounds require a free rebound, not a caught puck");
var recovery = fraction.Copy(); recovery.Effect = CustomRewardEffects.InjuryHealing; recovery.Trigger = "New injury"; recovery.Amount = -1;
recovery.Validate(); Reject(() => CustomRewardExport.ValidatePlayNow(recovery), "Campaign injury recovery rules block Play Now export");
recovery.Amount = 0; Reject(() => recovery.Validate(), "No-op injury recovery adjustments are rejected");
Check(CustomRewardEffects.Targets(CustomRewardDefinition.CustomRelics, "Injury attempted", CustomRewardEffects.InjuryMultiplier).SequenceEqual(new[] { CustomRewardEffects.OpposingTeam, CustomRewardEffects.Team }), "Injury odds explicitly choose the victim team");
Check(CustomRewardDefinition.Parse(fraction.ToDocument().ToString()).Seconds == .05f, "Short native durations round-trip without truncation");
var invalidNativeKind = fraction.ToDocument(); invalidNativeKind.Set("Kind", CustomRewardDefinition.CustomRelics);
invalidNativeKind.Set("Trigger", "Shot"); invalidNativeKind.Set("Effect", CustomRewardEffects.PuckCurve); invalidNativeKind.Set("Target", CustomRewardEffects.Puck);
Reject(() => CustomRewardDefinition.Parse(invalidNativeKind.ToString()), "A serialized definition cannot bypass native effect-kind restrictions");
var snapshots = new Dictionary<string, CustomRewardDefinition>(StringComparer.OrdinalIgnoreCase);
CustomRewardExport.AddSnapshot(snapshots, fraction); CustomRewardExport.AddSnapshot(snapshots, fraction.Copy());
Check(snapshots.Count == 1, "Identical portable definitions share their stable identity");
var conflictingSnapshot = fraction.Copy(); conflictingSnapshot.Amount++;
Reject(() => CustomRewardExport.AddSnapshot(snapshots, conflictingSnapshot), "Conflicting portable definitions never silently replace one another");
Check(snapshots[fraction.Id].Amount == fraction.Amount, "A rejected portable conflict preserves the earlier definition");
var legacyCurse = authored.ToDocument(); legacyCurse.Set("Format", "1"); legacyCurse.Set("Kind", "Chaos Relics"); legacyCurse.Set("Payoff Stat", "Speed"); legacyCurse.Set("Payoff Amount", "10");
var excluded = CustomRewardDefinition.Parse(legacyCurse.ToString());
Check(excluded.NeedsRedesign && excluded.Id == authored.Id && excluded.IsRelic, "Legacy curse remains identifiable and excluded until redesigned");
var excludedPool = new CampaignDocument("Selected Relics = " + excluded.Id + "\nSelected Chaos Relics = " + excluded.Id);
Check(CustomRewardPoolSelection.Read(excludedPool, CustomRewardDefinition.CustomRelics, new[] { excluded }).Selected.Count == 0, "Legacy selected curse cannot re-enter offers through old native selection keys");
Reject(() => CustomRewardExport.ValidatePlayNow(victory), "Play Now blocks a campaign-only completion rule");
Reject(() => CustomRewardExport.ValidatePlayNow(authored), "Play Now blocks run-duration event gains");
CustomRewardExport.ValidatePlayNow(fraction);
var exportDocument = new CampaignDocument(""); CustomRewardExport.Embed(exportDocument, new[] { fraction });
var portable = CustomRewardExport.Read(new CampaignDocument(exportDocument.ToString())).Single();
Check(portable.ToDocument().ToString() == fraction.ToDocument().ToString(), "Export companion includes the complete rule and embedded PNG without library access");
var tampered = new CampaignDocument(""); tampered.Set(CustomRewardExport.Prefix + Guid.NewGuid(), exportDocument.Fields.Single().Value);
Reject(() => CustomRewardExport.Read(tampered).ToArray(), "Export refuses mismatched embedded identities");
var overall = authored.Copy(); overall.Stat = "Overall"; overall.Trigger = "Goal"; overall.Threshold = 1;
progress.Clear(); progress.Signal(overall, "Goal", "A", "A", "A", 0);
Check(progress.Bonus("A", "T", "Speed", 0, (_, _) => true) == overall.Amount
    && progress.Bonus("A", "T", "Critical chance", 0, (_, _) => true) == 0
    && progress.Bonus("A", "T", "Size", 0, (_, _) => true) == 0, "Overall changes only the four native rating attributes");
var instant = authored.Copy(); instant.Effect = CustomRewardEffects.BreakStick; instant.Duration = "Seconds"; instant.Threshold = 1; instant.ActivationsPerMatch = 1;
progress.Clear();
Check(!progress.Signal(instant, instant.Trigger, "A", "A", "A", 0, () => false), "A native effect with no valid recipient does not claim activation");
Check(progress.Signal(instant, instant.Trigger, "A", "A", "A", 1, () => true) && progress.Effects.Count == 0, "Immediate effects consume their limit without becoming hidden stat effects");
Check(!progress.Signal(instant, instant.Trigger, "A", "A", "A", 2, () => true), "Native effects honor per-match limits");
var legacyChaos = authored.ToDocument(); legacyChaos.Set("Format", "1"); legacyChaos.Set("Kind", "Chaos Talents");
Check(CustomRewardDefinition.Parse(legacyChaos.ToString()).Kind == CustomRewardDefinition.CustomChaosTalents, "Legacy Chaos talent identity and pool remain distinct");
foreach (string poolKind in CustomRewardDefinition.Kinds) {
    var item = authored.Copy(); item.Kind = poolKind;
    if (item.IsGoalie) { item.Trigger = "Shutout"; item.Target = CustomRewardEffects.Team; }
    Check(CustomRewardDefinition.Parse(item.ToDocument().ToString()).Kind == poolKind && CustomRewardPoolSelection.ForNative(item.NativePool) == poolKind, "Each saved kind resolves only its matching native pool: " + poolKind);
}

Directory.CreateDirectory(Path.Combine(campaign, "custom_reward_progress")); File.WriteAllText(Path.Combine(campaign, "custom_reward_progress", "private.txt"), "private run state");
var sharing = typeof(CampaignWorkspace).Assembly.GetType("CampaignCreator.CampaignSharing")!;
string zipPath = Path.Combine(output, "creator-campaign.zip");
sharing.GetMethod("Zip", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [campaign, zipPath, root]);
using (var zip = ZipFile.OpenRead(zipPath)) {
    Check(zip.Entries.Count(e => e.FullName.EndsWith(CustomRewardDefinition.Extension)) == 3, "Campaign ZIP bundles all custom definitions and embedded icons");
    Check(!zip.Entries.Any(e => e.FullName.Contains("custom_reward_progress")), "Campaign ZIP excludes private run progress");
}
sharing.GetMethod("Install", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [zipPath, root, "Imported Creator Cup"]);
Check(CustomRewardDefinition.LoadFolder(Path.Combine(root, "campaigns", "Imported Creator Cup", CustomRewardDefinition.Folder)).Count == 3, "Imported campaign resolves its bundled definitions without the author's library");
new UpgradeCleanup().CleanDirectory(root);
Check(CustomRewardDefinition.LoadFolder(rewardFolder).Count == 3, "Upgrade cleanup leaves custom definitions intact");
Check(new CampaignDocument(File.ReadAllText(Path.Combine(campaign, "reward_pools.txt"))).Get("Selected Custom Relics").Contains(victory.Id), "Upgrade cleanup preserves custom selections");
window.Close();
// A separate existing campaign sees the whole library without inheriting any selections.
string otherCampaign = Path.Combine(root, "campaigns", "Other Cup"); Directory.CreateDirectory(otherCampaign);
File.WriteAllText(Path.Combine(otherCampaign, "campaign.txt"), "Act Sequence = 1\nSquad Template = No Effects\n");
var otherWindow = new Window { Width = 1320, Height = 860 };
var otherWorkspace = new CampaignWorkspace(otherWindow, root, otherCampaign, () => { }, _ => { }, () => Task.CompletedTask);
otherWindow.Content = otherWorkspace; otherWindow.Show();
typeof(CampaignWorkspace).GetMethod("ShowSection", Private)!.Invoke(otherWorkspace, [5]); Pump();
var otherTabs = Find<TabControl>(otherWindow).Single(t => t.Name == "RewardSections");
foreach (int index in new[] { 6, 9 }) {
    otherTabs.SelectedIndex = index; Pump();
    var rows = Find<CheckBox>(otherWindow).Where(c => c.Tag is string).ToArray();
    Check(rows.Length == (index == 6 ? 1 : 2) && rows.All(c => c.IsChecked != true && c.IsEnabled), "Every library item appears unticked and editable in another campaign: " + index);
}
Click(Find<Button>(otherWindow).Single(b => b.Content?.ToString() == "Save campaign"));
Check(CustomRewardDefinition.LoadFolder(Path.Combine(otherCampaign, CustomRewardDefinition.Folder)).Count == 3, "Saving another campaign bundles the whole library: " + ((TextBlock)typeof(CampaignWorkspace).GetField("message", Private)!.GetValue(otherWorkspace)!).Text);
var otherPools = new CampaignDocument(File.Exists(Path.Combine(otherCampaign, "reward_pools.txt")) ? File.ReadAllText(Path.Combine(otherCampaign, "reward_pools.txt")) : "");
Check(CustomRewardDefinition.Kinds.All(k => { var pool = CustomRewardPoolSelection.Read(otherPools, k, new[] { authored, victory, withPng }); return pool.UseBaseGame && pool.Selected.Count == 0; }), "Bundling definitions never enables their rewards");
if (args.Length > 1) {
    string art = Path.GetFullPath(args[1]);
    var studioArt = typeof(CampaignWorkspace).Assembly.GetType("CampaignCreator.StudioArt")!;
    studioArt.GetMethod("Use")!.Invoke(null, [art]);
    var pick = (Task<byte[]?>)iconType.GetMethod("PickNative", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [otherWindow])!;
    Pump(); var picker = otherWindow.OwnedWindows.Single(w => w.Title == "Choose a base-game relic icon");
    using var nativeIndex = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(art, "relic-icons", "index.json")));
    var chosen = nativeIndex.RootElement[0]; string name = chosen.GetProperty("name").GetString()!;
    Check(nativeIndex.RootElement.EnumerateArray().All(row => !row.GetProperty("name").GetString()!.StartsWith("relics/")), "Native icon names are readable labels, not localization keys");
    Find<TextBox>(picker).Single(t => t.Name == "NativeRelicIconSearch").Text = name; Pump();
    Frame(picker, "native-relic-icon-picker");
    Click(Find<Button>(picker).First(b => b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == name)));
    Check(pick.IsCompletedSuccessfully && pick.Result!.SequenceEqual(File.ReadAllBytes(Path.Combine(art, "relic-icons", chosen.GetProperty("file").GetString()!))), "Native icon picker searches and returns the exact extracted PNG");
    studioArt.GetMethod("Use")!.Invoke(null, [null]);
}
otherWindow.Close();
// Exercise every custom tab against its actual native pool, including reset.
string separationCampaign = Path.Combine(root, "campaigns", "Separated Pools"); Directory.CreateDirectory(separationCampaign);
File.WriteAllText(Path.Combine(separationCampaign, "campaign.txt"), "Act Sequence = 1\nSquad Template = No Effects\n");
var separationWindow = new Window { Width = 1320, Height = 860 };
var separationWorkspace = new CampaignWorkspace(separationWindow, root, separationCampaign, () => { }, _ => { }, () => Task.CompletedTask);
separationWindow.Content = separationWorkspace; separationWindow.Show();
typeof(CampaignWorkspace).GetMethod("ShowSection", Private)!.Invoke(separationWorkspace, [5]); Pump();
var separatedTabs = Find<TabControl>(separationWindow).Single(t => t.Name == "RewardSections");
foreach (string newKind in new[] { CustomRewardDefinition.CustomChaosTalents, CustomRewardDefinition.CustomGoalieTalents }) {
    separatedTabs.SelectedIndex = 10; Pump();
    Click(Find<Button>(separationWindow).Single(b => b.Content?.ToString() == "New talent / relic"));
    Find<TextBox>(separationWindow).Single(t => t.Name == "CustomRewardName").Text = "Pool check " + newKind;
    Find<ComboBox>(separationWindow).Single(c => c.Name == "CustomRewardKind").SelectedItem = newKind;
    Pump(); Click(Find<Button>(separationWindow).Single(b => b.Name == "SaveCustomReward"));
}
var allKinds = CustomRewardDefinition.LoadFolder(Path.Combine(root, "library", "rewards"));
Check(CustomRewardDefinition.Kinds.All(k => allKinds.Any(d => d.Kind == k)), "The creator saves all four item kinds independently");
foreach (var (customTab, nativeTab, customKind) in new[] {
    (6, 1, CustomRewardDefinition.CustomTalents), (7, 4, CustomRewardDefinition.CustomChaosTalents),
    (8, 2, CustomRewardDefinition.CustomGoalieTalents), (9, 0, CustomRewardDefinition.CustomRelics) }) {
    separatedTabs.SelectedIndex = customTab; Pump();
    var rows = Find<CheckBox>(separationWindow).Where(c => c.Tag is string).ToArray();
    var expected = allKinds.Where(d => d.Kind == customKind).Select(d => d.Id).ToHashSet();
    Check(expected.SetEquals(rows.Select(c => (string)c.Tag!)), "Only matching items appear in " + customKind);
    rows[0].IsChecked = true; Pump();
    foreach (int nativeTabToCheck in new[] { 0, 1, 2, 3, 4, 5 }) {
        separatedTabs.SelectedIndex = nativeTabToCheck; Pump();
        Check(Find<CheckBox>(separationWindow).Single(c => c.Name == "BaseGamePool").IsChecked == (nativeTabToCheck != nativeTab),
            customKind + " changes only its matching native pool; native tab " + nativeTabToCheck);
    }
    separatedTabs.SelectedIndex = nativeTab; Pump();
    Find<CheckBox>(separationWindow).Single(c => c.Name == "BaseGamePool").IsChecked = true; Pump();
    separatedTabs.SelectedIndex = customTab; Pump();
    Check(Find<CheckBox>(separationWindow).Where(c => c.Tag is string).All(c => c.IsChecked != true), "Base game clears only the matching custom selections: " + customKind);
}
separatedTabs.SelectedIndex = 10; Pump();
var separationCreator = (Control)((TabItem)separatedTabs.Items[10]!).Content!;
creatorType.GetMethod("Load", Private)!.Invoke(separationCreator, [authored.Copy()]);
Find<ComboBox>(separationWindow).Single(c => c.Name == "CustomRewardKind").SelectedItem = CustomRewardDefinition.CustomRelics; Pump();
Click(Find<Button>(separationWindow).Single(b => b.Name == "SaveCustomReward"));
Check(Find<TextBlock>(separationWindow).Single(t => t.Name == "CustomRewardStatus").Text!.Contains("type cannot change")
    && CustomRewardDefinition.LoadFolder(Path.Combine(root, "library", "rewards")).Single(d => d.Id == authored.Id).Kind == CustomRewardDefinition.CustomTalents,
    "Editing a library-only item from another campaign cannot turn its talent ID into a relic");
separatedTabs.SelectedIndex = 10; Pump();
Click(Find<Button>(separationWindow).Single(b => b.Content?.ToString() == "New talent / relic"));
Find<ComboBox>(separationWindow).Single(c => c.Name == "CustomRewardKind").SelectedItem = CustomRewardDefinition.CustomTalents;
Find<ComboBox>(separationWindow).Single(c => c.Name == "CustomRewardTrigger").SelectedItem = "While owned"; Pump();
foreach (var timedEffect in new[] { CustomRewardEffects.ChargePower, CustomRewardEffects.ChargeCritical, CustomRewardEffects.RandomAutoShot }) {
    Find<ComboBox>(separationWindow).Single(c => c.Name == "CustomRewardEffect").SelectedItem = timedEffect; Pump();
    var secondsControl = Find<NumericUpDown>(separationWindow).Single(c => c.Name == "CustomRewardSeconds");
    Check(secondsControl.GetVisualAncestors().OfType<Control>().All(c => c.IsVisible), "Continuous effect timing remains visible outside event-only fields: " + timedEffect);
}
Find<TextBox>(separationWindow).Single(t => t.Name == "CustomRewardName").Text = "Native shot timer";
Find<NumericUpDown>(separationWindow).Single(c => c.Name == "CustomRewardSeconds").Value = 1.25m;
Find<NumericUpDown>(separationWindow).Single(c => c.Name == "CustomRewardShotDelay").Value = 730; Pump();
Frame(separationWindow, "automatic-shot-timing");
Click(Find<Button>(separationWindow).Single(b => b.Name == "SaveCustomReward"));
var savedTimer = CustomRewardDefinition.LoadFolder(Path.Combine(root, "library", "rewards")).Single(d => d.Name == "Native shot timer");
Check(savedTimer.Effect == CustomRewardEffects.RandomAutoShot && savedTimer.Seconds == 1.25f && savedTimer.ShotDelayMilliseconds == 730 && savedTimer.Amount == 50, "Creator saves independent native shot interval, warning delay and chance");
foreach (var timerChoice in new[] {
    (CustomRewardEffects.HungerRage, "Without puck timer", CustomRewardDefinition.CustomTalents, CustomRewardEffects.Actor),
    (CustomRewardEffects.PossessionExplosion, "Puck possession timer", CustomRewardDefinition.CustomRelics, CustomRewardEffects.EitherCarrier) }) {
    var timerRule = savedTimer.Copy(); timerRule.Id = Guid.NewGuid().ToString(); timerRule.Name = timerChoice.Item1;
    timerRule.Effect = timerChoice.Item1; timerRule.Trigger = timerChoice.Item2; timerRule.Kind = timerChoice.Item3; timerRule.Target = timerChoice.Item4; timerRule.Seconds = 7.25f;
    creatorType.GetMethod("Load", Private)!.Invoke(separationCreator, [timerRule]); Pump();
    var timingInput = Find<NumericUpDown>(separationWindow).Single(c => c.Name == "CustomRewardSeconds");
    Check(timingInput.GetVisualAncestors().OfType<Control>().All(c => c.IsVisible) && timingInput.Value == 7.25m, "Native possession timer remains visible with saved delay: " + timerChoice.Item1);
    timingInput.Value = 4.5m; Click(Find<Button>(separationWindow).Single(b => b.Name == "SaveCustomReward"));
    var writtenTimer = CustomRewardDefinition.LoadFolder(Path.Combine(root, "library", "rewards")).Single(r => r.Id == timerRule.Id);
    Check(writtenTimer.Seconds == 4.5f && writtenTimer.Target == timerChoice.Item4, "Creator writes the native possession timer and its actual recipient: " + timerChoice.Item1);
}
Frame(separationWindow, "possession-timer-flow");
foreach (var choice in new[] {
    (CustomRewardEffects.RetainPowers, "Bench run retention", CustomRewardDefinition.CustomTalents, CustomRewardEffects.Actor, 3),
    (CustomRewardEffects.GrantSpeedTalent, "Item acquired", CustomRewardDefinition.CustomRelics, CustomRewardEffects.Team, 2) }) {
    var lifecycle = savedTimer.Copy(); lifecycle.Id = Guid.NewGuid().ToString(); lifecycle.Name = choice.Item1;
    lifecycle.Effect = choice.Item1; lifecycle.Trigger = choice.Item2; lifecycle.Kind = choice.Item3; lifecycle.Target = choice.Item4; lifecycle.Amount = choice.Item5;
    creatorType.GetMethod("Load", Private)!.Invoke(separationCreator, [lifecycle]); Pump();
    var value = Find<NumericUpDown>(separationWindow).Single(c => c.Name == "CustomRewardAmount");
    Check(value.GetVisualAncestors().OfType<Control>().All(c => c.IsVisible) && value.Value == choice.Item5, "Saved lifecycle parameter is visible: " + choice.Item1);
    Click(Find<Button>(separationWindow).Single(b => b.Name == "SaveCustomReward"));
    var written = CustomRewardDefinition.LoadFolder(Path.Combine(root, "library", "rewards")).Single(r => r.Id == lifecycle.Id);
    Check(written.Amount == choice.Item5 && written.Trigger == choice.Item2 && written.Target == choice.Item4, "Creator retains acquisition and retention parameters: " + choice.Item1);
}
Frame(separationWindow, "acquisition-flow");
var fartTimer = savedTimer.Copy(); fartTimer.Id=Guid.NewGuid().ToString(); fartTimer.Name="Separate timed puck push";
fartTimer.Trigger="Random fart timer"; fartTimer.Effect=CustomRewardEffects.PushPuck; fartTimer.Target=CustomRewardEffects.Actor;
fartTimer.Seconds=2.5f; fartTimer.IntervalMaxSeconds=7.25f; fartTimer.Amount=100; fartTimer.Kind=CustomRewardDefinition.CustomTalents;
creatorType.GetMethod("Load", Private)!.Invoke(separationCreator, [fartTimer]); Pump();
var maxTimer = Find<NumericUpDown>(separationWindow).Single(c => c.Name == "CustomRewardIntervalMax");
Check(maxTimer.GetVisualAncestors().OfType<Control>().All(c => c.IsVisible) && maxTimer.Value == 7.25m, "Random timer exposes its independent maximum in the creator");
maxTimer.Value=8.5m;
Click(Find<Button>(separationWindow).Single(b => b.Name == "SaveCustomReward"));
var savedFartTimer=CustomRewardDefinition.LoadFolder(Path.Combine(root, "library", "rewards")).Single(r => r.Id == fartTimer.Id);
Check(savedFartTimer.Seconds == 2.5f && savedFartTimer.IntervalMaxSeconds == 8.5f && savedFartTimer.Effect == CustomRewardEffects.PushPuck, "Creator saves separate timer bounds without bundling other interactions");
Frame(separationWindow, "random-fart-timer-flow");
var pickupForm = savedTimer.Copy(); pickupForm.Id=Guid.NewGuid().ToString(); pickupForm.Name="Permanent pickup attribute";
pickupForm.Effect=CustomRewardEffects.PickupAttribute; pickupForm.Trigger="Item acquired"; pickupForm.Kind=CustomRewardDefinition.CustomTalents; pickupForm.Target=CustomRewardEffects.Actor; pickupForm.Stat="Accuracy"; pickupForm.Amount=17;
creatorType.GetMethod("Load", Private)!.Invoke(separationCreator, [pickupForm]); Pump();
var pickupStatField=Find<ComboBox>(separationWindow).Single(c => c.Name == "CustomRewardStat");
Check(pickupStatField.GetVisualAncestors().OfType<Control>().All(c => c.IsVisible) && (string?)pickupStatField.SelectedItem == "Accuracy", "Permanent pickup keeps its attribute picker visible");
Click(Find<Button>(separationWindow).Single(b => b.Name == "SaveCustomReward"));
var pickupSaved=CustomRewardDefinition.LoadFolder(Path.Combine(root, "library", "rewards")).Single(r => r.Id == pickupForm.Id);
Check(pickupSaved.Stat == "Accuracy" && pickupSaved.Amount == 17 && pickupSaved.Trigger == "Item acquired", "Creator saves permanent pickup separately from owned bonuses");
Frame(separationWindow, "permanent-pickup-flow");
var helmetForm = pickupSaved.Copy(); helmetForm.Id=Guid.NewGuid().ToString(); helmetForm.Name="Separate helmet attribute";
helmetForm.Effect=CustomRewardEffects.PropellerHelmetAttribute; helmetForm.Trigger="Helmet equipment"; helmetForm.Stat="Speed"; helmetForm.Amount=-23;
creatorType.GetMethod("Load", Private)!.Invoke(separationCreator, [helmetForm]); Pump();
Check(pickupStatField.GetVisualAncestors().OfType<Control>().All(c => c.IsVisible) && (string?)pickupStatField.SelectedItem == "Speed", "Transferable helmet exposes the selected equipment attribute");
Click(Find<Button>(separationWindow).Single(b => b.Name == "SaveCustomReward"));
var helmetSaved=CustomRewardDefinition.LoadFolder(Path.Combine(root, "library", "rewards")).Single(r => r.Id == helmetForm.Id);
Check(helmetSaved.Amount == -23 && helmetSaved.Effect == CustomRewardEffects.PropellerHelmetAttribute, "Helmet form saves its signed component without the original bundle");
Frame(separationWindow, "transferable-helmet-flow");
var spellForm=helmetSaved.Copy(); spellForm.Id=Guid.NewGuid().ToString(); spellForm.Name="Separate Cultist spell"; spellForm.Trigger="Cultist incantation cycle"; spellForm.Effect=CustomRewardEffects.CultistRory;
creatorType.GetMethod("Load", Private)!.Invoke(separationCreator, [spellForm]); Pump();
Click(Find<Button>(separationWindow).Single(b => b.Name == "SaveCustomReward"));
var spellSaved=CustomRewardDefinition.LoadFolder(Path.Combine(root, "library", "rewards")).Single(r => r.Id == spellForm.Id);
Check(spellSaved.Effect == CustomRewardEffects.CultistRory && spellSaved.Trigger == "Cultist incantation cycle" && spellSaved.Target == CustomRewardEffects.Actor, "Cultist form saves one native spell with its owner and timing");
CustomRewardExport.ValidatePlayNow(spellSaved);
Frame(separationWindow, "cultist-component-flow");



separationWindow.Close();
string portableCampaign = Path.Combine(root, "campaigns", "Portable Player"); Directory.CreateDirectory(portableCampaign);
File.WriteAllText(Path.Combine(portableCampaign, "campaign.txt"), "Act Sequence = 1\nSquad Template = No Effects\n");
var embeddedOnly = fraction.Copy(); embeddedOnly.Id = Guid.NewGuid().ToString(); embeddedOnly.Name = "Imported player's rule";
var embeddedPlayer = new CampaignDocument("Name = Portable Player\nTalents = " + embeddedOnly.Id);
CustomRewardExport.Embed(embeddedPlayer, new[] { embeddedOnly }); embeddedPlayer.Save(Path.Combine(portableCampaign, "player.txt"));
string portableZip = Path.Combine(output, "portable-player-campaign.zip");
sharing.GetMethod("Zip", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [portableCampaign, portableZip, root]);
using (var zip = ZipFile.OpenRead(portableZip)) {
    Check(zip.Entries.Any(e => e.FullName.EndsWith(embeddedOnly.Id + CustomRewardDefinition.Extension)), "Campaign sharing bundles a player's embedded rule even before a campaign reward copy exists");
}
var wrongCampaignPlayer = new CampaignDocument(embeddedPlayer.ToString()); wrongCampaignPlayer.Set("Talents", ""); wrongCampaignPlayer.Set("Relics", embeddedOnly.Id);
wrongCampaignPlayer.Save(Path.Combine(portableCampaign, "player.txt"));
byte[] lastGoodZip = File.ReadAllBytes(portableZip);
try { sharing.GetMethod("Zip", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [portableCampaign, portableZip, root]); throw new Exception("Wrong-slot campaign ZIP should fail"); }
catch (TargetInvocationException ex) when (ex.InnerException is ArgumentException a && a.Message.Contains(embeddedOnly.Name)) {
    Check(File.ReadAllBytes(portableZip).SequenceEqual(lastGoodZip), "Wrong-slot custom items block campaign packaging before replacing a previous ZIP");
}
string invalidZip = Path.Combine(root, "wrong-slot-campaign.zip");
using (var zip = ZipFile.Open(invalidZip, ZipArchiveMode.Create)) {
    zip.CreateEntryFromFile(Path.Combine(portableCampaign, "campaign.txt"), "Wrong Slot/campaign.txt");
    zip.CreateEntryFromFile(Path.Combine(portableCampaign, "player.txt"), "Wrong Slot/player.txt");
}
try { sharing.GetMethod("Install", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [invalidZip, root, "Rejected Wrong Slot"]); throw new Exception("Wrong-slot campaign import should fail"); }
catch (TargetInvocationException ex) when (ex.InnerException is ArgumentException a && a.Message.Contains(embeddedOnly.Name)) {
    Check(!Directory.Exists(Path.Combine(root, "campaigns", "Rejected Wrong Slot")), "Wrong-slot custom items cannot enter the campaign library through ZIP import");
}
embeddedPlayer.Save(Path.Combine(portableCampaign, "player.txt"));
var conflictingPlayer = new CampaignDocument(embeddedPlayer.ToString());
var changedEmbedded = embeddedOnly.Copy(); changedEmbedded.Amount++;
CustomRewardExport.Embed(conflictingPlayer, new[] { changedEmbedded }); conflictingPlayer.Save(Path.Combine(portableCampaign, "second-player.txt"));
try { sharing.GetMethod("Zip", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [portableCampaign, portableZip, root]); throw new Exception("Conflicting embedded rules should fail"); }
catch (TargetInvocationException ex) when (ex.InnerException is ArgumentException a && a.Message.Contains("Conflicting embedded")) {
    Check(File.ReadAllBytes(portableZip).SequenceEqual(lastGoodZip), "Conflicting embedded rules block campaign packaging without replacing the last good ZIP");
}
embeddedOnly.Save(Path.Combine(portableCampaign, CustomRewardDefinition.Folder));
sharing.GetMethod("Zip", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [portableCampaign, portableZip, root]);
using (var zip = ZipFile.OpenRead(portableZip)) {
    using var reader = new StreamReader(zip.Entries.Single(e => e.FullName.EndsWith(embeddedOnly.Id + CustomRewardDefinition.Extension)).Open());
    Check(CustomRewardDefinition.Parse(reader.ReadToEnd()).Amount == embeddedOnly.Amount, "An explicit campaign edit remains authoritative over older embedded player snapshots");
}
var sampled = fraction.Copy(); sampled.Trigger = "Slapshot"; sampled.Kind = CustomRewardDefinition.CustomRelics; sampled.Effect = CustomRewardEffects.Freeze;
sampled.Target = CustomRewardEffects.RandomOpponents; sampled.TargetCount = 2; sampled.Validate();
Check(CustomRewardDefinition.Parse(sampled.ToDocument().ToString()).TargetCount == 2 && sampled.RulesText.Contains("2 randomly chosen opponents"), "Random opponent count survives saving and appears in the generated rule");
var oldSample = sampled.ToDocument(); oldSample.Remove("Target Count");
Check(CustomRewardDefinition.Parse(oldSample.ToString()).TargetCount == 1, "Existing format-3 rules default the new recipient count without changing identities");
sampled.TargetCount = 0; Reject(() => sampled.Validate(), "Zero random recipients cannot be saved");
foreach (var upgrade in new[] { CustomRewardEffects.UpgradeTalent, CustomRewardEffects.UpgradeAbility, CustomRewardEffects.UpgradeRelic }) {
    var rule = fraction.Copy(); rule.Kind = CustomRewardDefinition.CustomRelics; rule.Trigger = "Match won"; rule.Effect = upgrade; rule.Target = CustomRewardEffects.Team; rule.Amount = 2; rule.Duration = "Rest of run";
    rule.Validate(); Check(CustomRewardDefinition.Parse(rule.ToDocument().ToString()).Effect == upgrade, "Separate native upgrade component round-trips: " + upgrade);
    Reject(() => CustomRewardExport.ValidatePlayNow(rule), "Campaign upgrade cannot be exported to Play Now: " + upgrade);
    rule.Trigger = "Shot"; Reject(() => rule.Validate(), "A campaign upgrade cannot mutate a live shot: " + upgrade);
}
var grant = fraction.Copy(); grant.Effect = CustomRewardEffects.GainTalent; grant.Trigger = "Hat trick"; grant.Validate();
Reject(() => CustomRewardExport.ValidatePlayNow(grant), "Permanent random talent grants require campaign progress");
Check(CustomRewardEffects.ForTrigger(CustomRewardDefinition.CustomRelics, "Contract expiry").SequenceEqual(new[] { CustomRewardEffects.KeepFreeAgents })
    && !CustomRewardEffects.TriggersForKind(CustomRewardDefinition.CustomTalents).Contains("Contract expiry"), "Native free-agent retention belongs only to relics at contract expiry");
Check(CustomRewardEffects.ForTrigger(CustomRewardDefinition.CustomTalents, "Bench bonus").Contains(CustomRewardEffects.BenchMultiplier)
    && !CustomRewardEffects.ForTrigger(CustomRewardDefinition.CustomChaosTalents, "Bench bonus").Any(), "Bench multipliers retain their native talent and relic kinds");
Check(CustomRewardEffects.Targets(CustomRewardDefinition.CustomGoalieTalents, "Pass sent", CustomRewardEffects.Boost).Contains(CustomRewardEffects.Opponent), "A goalie pass can propel its named receiving teammate");
var resetting = fraction.Copy(); resetting.Trigger = "Shot on net"; resetting.ResetOn = "Goal";
resetting.ActivationsPerMatch = 2; resetting.MaxStacks = 5; resetting.Duration = "Rest of run"; resetting.Validate();
Check(CustomRewardDefinition.Parse(resetting.ToDocument().ToString()).ResetOn == "Goal" && resetting.RulesText.Contains("resets"), "Reset event and description round-trip with the rule");
progress.Clear();
progress.Signal(resetting, resetting.Trigger, "A", "A", "A", 0);
progress.Signal(resetting, resetting.Trigger, "B", "B", "B", 0);
Check(!progress.ResetBonus(resetting, "Assist", "A", "A"), "Unrelated events do not clear an earned bonus");
Check(progress.ResetBonus(resetting, "Goal", "A", "A") && progress.Effects.Count == 1 && progress.Effects[0].Owner == "B", "Reset clears only this item, owner and recipient");
progress.Signal(resetting, resetting.Trigger, "A", "A", "A", 1);
Check(progress.ResetBonus(resetting, "Goal", "A", "A") && !progress.Signal(resetting, resetting.Trigger, "A", "A", "A", 2), "Resetting a bonus cannot bypass its per-match activation limit");
var resumedReset = new CustomRewardProgress(); resumedReset.Load(progress.Save());
Check(resumedReset.Effects.Count == 1 && resumedReset.Effects[0].Owner == "B", "Saved progress does not resurrect a cleared run-long bonus");
resetting.ResetOn = resetting.Trigger; Reject(() => resetting.Validate(), "A rule cannot gain and reset on the same event");
resetting.ResetOn = "Goal"; resetting.Trigger = "While owned"; Reject(() => resetting.Validate(), "A permanent condition cannot carry a hidden reset event");
var legacyReset = fraction.ToDocument(); legacyReset.Remove("Reset on");
Check(CustomRewardDefinition.Parse(legacyReset.ToString()).ResetOn == "", "Older rules keep their existing no-reset behavior");
var probabilistic = fraction.Copy(); probabilistic.Chance = 25; probabilistic.Threshold = 2; probabilistic.ActivationsPerMatch = 1;
probabilistic.Validate(); progress.Clear(); int rolls = 0;
Check(!progress.Signal(probabilistic, probabilistic.Trigger, "A", "A", "A", 0, roll: () => { rolls++; return true; }) && rolls == 0, "Probability is evaluated only after reaching the event threshold");
Check(!progress.Signal(probabilistic, probabilistic.Trigger, "A", "A", "A", 1, roll: () => { rolls++; return false; }) && rolls == 1, "Failed chance consumes the threshold without granting a bonus");
progress.Signal(probabilistic, probabilistic.Trigger, "A", "A", "A", 2, roll: () => true);
Check(progress.Signal(probabilistic, probabilistic.Trigger, "A", "A", "A", 3, roll: () => true), "A failed chance does not consume a successful activation limit");
Check(!progress.Signal(probabilistic, probabilistic.Trigger, "A", "A", "A", 4, roll: () => true), "A successful random activation respects the match limit");
Check(CustomRewardDefinition.Parse(probabilistic.ToDocument().ToString()).Chance == 25 && probabilistic.RulesText.StartsWith("25% chance"), "Probability persists with a clear rule description");
probabilistic.Trigger = "While owned"; Reject(() => probabilistic.Validate(), "Continuous conditions cannot reroll every frame");
var counted = fraction.Copy(); counted.Trigger = "While owned"; counted.AmountBasis = "Per other owned talent"; counted.Validate();
Check(CustomRewardDefinition.Parse(counted.ToDocument().ToString()).AmountBasis == counted.AmountBasis && counted.RulesText.Contains("Excludes this talent"), "Counted bonuses preserve their native self-exclusion and serialized basis");
counted.Kind = CustomRewardDefinition.CustomRelics; Reject(() => counted.Validate(), "Per-talent attribute mechanics do not leak into relics");
counted.Kind = CustomRewardDefinition.CustomTalents; counted.Trigger = "Goal"; Reject(() => counted.Validate(), "Current-count bonuses cannot masquerade as earned event bonuses");
var denial = fraction.Copy(); denial.Effect = CustomRewardEffects.DenyGoal; denial.Trigger = "Goal verification"; denial.Target = CustomRewardEffects.OpposingTeam; denial.Amount = 3; denial.Validate();
CustomRewardExport.ValidatePlayNow(denial);
Check(CustomRewardDefinition.Parse(denial.ToDocument().ToString()).Amount == 3, "Pre-goal denial travels with compatible Play Now exports");
denial.Trigger = "Opponent goal"; Reject(() => denial.Validate(), "Already accepted goals cannot trigger the pre-goal denial effect");
Check(!CustomRewardEffects.SupportsKind(CustomRewardEffects.DenyGoal, CustomRewardDefinition.CustomGoalieTalents), "Goal denial stays in its native talent and relic kinds");
foreach (string when in new[] { "Slapshot power calculation", "Wrist-shot power calculation", "Slapshot crit calculation", "Wrist-shot crit calculation", "One-timer crit calculation" }) {
    var calculation = fraction.Copy(); calculation.Trigger = when;
    calculation.Stat = when.Contains("crit") ? "Critical chance" : "Shot Power";
    calculation.Validate(); CustomRewardExport.ValidatePlayNow(calculation);
    Check(calculation.RulesText.Contains("for this shot") && !calculation.RulesText.Contains("for this run"), "Shot calculation changes the current shot and remains portable: " + when);
    calculation.Stat = "Speed"; Reject(() => calculation.Validate(), "Shot calculation rejects unrelated skating stats: " + when);
}
var assisted = fraction.Copy(); assisted.Trigger = "Assist"; assisted.Target = CustomRewardEffects.Opponent; assisted.Validate();
Check(assisted.RulesText.Contains("other player involved"), "Assists and passes can award the named teammate rather than the owner");
progress.Clear(); progress.Signal(assisted, "Assist", "passer", "passer", "scorer", 0);
Check(progress.Bonus("scorer", "team", assisted.Stat, 0, (_, _) => true) == assisted.Amount
    && progress.Bonus("passer", "team", assisted.Stat, 0, (_, _) => true) == 0, "A teammate's earned bonus belongs to the recipient while ownership remains with the source");
assisted.ResetOn = "Goal"; progress.Signal(assisted, "Assist", "passer2", "passer2", "scorer", 0);
Check(progress.ResetBonus(assisted, "Goal", "passer", null) && progress.Effects.Count == 1 && progress.Effects[0].Owner == "passer2", "A source reset clears its transferred bonuses without clearing another owner's contribution");
counted.Trigger = "While owned"; counted.AmountBasis = "Per injured playing teammate";
Reject(() => CustomRewardExport.ValidatePlayNow(counted), "An injury-count rule cannot be exported into a mode without campaign injuries");
foreach (var effect in new[] { CustomRewardEffects.CarryingCheck, CustomRewardEffects.GuaranteedCheck, CustomRewardEffects.SpinOnCheck, CustomRewardEffects.CollectOnHit, CustomRewardEffects.KickPuck, CustomRewardEffects.PostAim, CustomRewardEffects.ForceRebound }) {
    var rule = fraction.Copy(); rule.Trigger = "While owned"; rule.Effect = effect; rule.Validate(); CustomRewardExport.ValidatePlayNow(rule);
    Check(CustomRewardDefinition.Parse(rule.ToDocument().ToString()).Effect == effect, "Native capability travels with exported talent ownership: " + effect);
    rule.Trigger = "Match won"; Reject(() => rule.Validate(), "A native capability cannot silently become a later-action grant: " + effect);
    rule.Kind = CustomRewardDefinition.CustomGoalieTalents; rule.Trigger = "While owned"; Reject(() => rule.Validate(), "Skater capability does not leak into goalie talents: " + effect);
}
var recoil = fraction.Copy(); recoil.Trigger = "Shot"; recoil.Effect = CustomRewardEffects.ShotRecoil; recoil.Seconds = 1; recoil.Validate();
Check(recoil.RulesText.Contains("force " + recoil.Amount), "Recoil preview states both force and duration");
recoil.Trigger = "Pass sent"; Reject(() => recoil.Validate(), "Shot recoil requires the current shot direction");
var flat = fraction.Copy(); flat.Effect = CustomRewardEffects.FlatPuck; flat.Target = CustomRewardEffects.Puck; flat.Trigger = "Pass sent"; flat.Validate();
Check(!flat.RulesText.Contains("10%"), "A flat launch does not display an unused percentage");
flat.Trigger = "Shot rebounds off goalie"; Reject(() => flat.Validate(), "Flat launch cannot be applied to a later rebound");
foreach (string when in new[] { "Goal value calculation", "Critical goal value calculation", "Enraged goal value calculation", "First goal value calculation" }) {
    var rule = fraction.Copy(); rule.Trigger = when; rule.Effect = CustomRewardEffects.ExtraGoal; rule.Validate(); CustomRewardExport.ValidatePlayNow(rule);
    Check(rule.RulesText.Contains("counts as 2 goals") && rule.RulesText.Contains("do not stack"), "Goal value uses its immediate scoring flow and explicit native stacking: " + when);
    rule.Trigger = "Goal"; Reject(() => rule.Validate(), "An already accepted-goal event cannot change its scoring value: " + when);
}
var injuredGoal = fraction.Copy(); injuredGoal.Trigger = "Injured goal value calculation"; injuredGoal.Effect = CustomRewardEffects.ExtraGoal; injuredGoal.Validate();
Reject(() => CustomRewardExport.ValidatePlayNow(injuredGoal), "Injury-based goal values cannot export to Play Now");
foreach (var effect in new[] { CustomRewardEffects.ExtraReferee, CustomRewardEffects.RefereeBlock, CustomRewardEffects.RefereeSize }) {
    var rule = fraction.Copy(); rule.Kind = CustomRewardDefinition.CustomRelics; rule.Trigger = "Match setup"; rule.Effect = effect; rule.Target = CustomRewardEffects.Referees; rule.Amount = 2;
    rule.Validate(); CustomRewardExport.ValidatePlayNow(rule);
    Check(CustomRewardDefinition.Parse(rule.ToDocument().ToString()).Target == CustomRewardEffects.Referees, "Referee setup is a portable, explicit official-target effect: " + effect);
    rule.Trigger = "Goal"; Reject(() => rule.Validate(), "Referee setup cannot alter a live goal: " + effect);
    rule.Trigger = "Match setup"; rule.Kind = CustomRewardDefinition.CustomTalents; Reject(() => rule.Validate(), "Referee setup is owned by a team relic: " + effect);
}
foreach (var basis in new[] { "Per goal trailing", "Per knocked-out skater on either team" }) {
    var rule = fraction.Copy(); rule.Kind = CustomRewardDefinition.CustomRelics; rule.Trigger = "While owned"; rule.Target = CustomRewardEffects.Team; rule.AmountBasis = basis;
    rule.Validate(); CustomRewardExport.ValidatePlayNow(rule);
    Check(CustomRewardDefinition.Parse(rule.ToDocument().ToString()).AmountBasis == basis && !rule.RulesText.Contains("owning skater is excluded"), "Native relic counts round-trip and describe their actual scope: " + basis);
    rule.Kind = CustomRewardDefinition.CustomTalents; Reject(() => rule.Validate(), "Relic count mechanics do not leak into talents: " + basis);
}
foreach (string when in new[] { "While in defensive zone", "While on a breakaway", "While size XS or S", "While size L or XL", "While playing against Elite", "While playing against a boss", "While team has three or fewer skaters" }) {
    var rule = fraction.Copy(); rule.Trigger = when; rule.Validate(); CustomRewardExport.ValidatePlayNow(rule);
    Check(CustomRewardDefinition.Parse(rule.ToDocument().ToString()).Trigger == when, "Live match condition is persisted and portable: " + when);
    if (when.StartsWith("While size")) { rule.Stat = "Size"; rule.Amount = 1; Reject(() => rule.Validate(), "Size conditions cannot repeatedly toggle their own defining size"); }
}
var injuredTeam = fraction.Copy(); injuredTeam.Trigger = "While team has an injured skater"; injuredTeam.Validate();
Reject(() => CustomRewardExport.ValidatePlayNow(injuredTeam), "Team injury conditions block incompatible Play Now exports");
foreach (var effect in new[] { CustomRewardEffects.InstantRecovery, CustomRewardEffects.DeflectPucks }) {
    var rule = fraction.Copy(); rule.Trigger = "While owned"; rule.Effect = effect; rule.Validate(); CustomRewardExport.ValidatePlayNow(rule);
    Check(!CustomRewardEffects.HasAmount(effect), "Native marker has no invented amount: " + effect);
    rule.Trigger = "Match won"; Reject(() => rule.Validate(), "Native contact capabilities cannot silently wait for a future match: " + effect);
}
foreach (var effect in new[] { CustomRewardEffects.AnchorProtection, CustomRewardEffects.ForbidKnockouts }) {
    var rule = fraction.Copy(); rule.Kind = CustomRewardDefinition.CustomChaosTalents; rule.Trigger = "While owned"; rule.Effect = effect; rule.Validate(); CustomRewardExport.ValidatePlayNow(rule);
    Check(!CustomRewardEffects.SupportsKind(effect, CustomRewardDefinition.CustomTalents) && !CustomRewardEffects.SupportsKind(effect, CustomRewardDefinition.CustomRelics), "Anchor's separate components stay in Chaos talents: " + effect);
}
var handRule = fraction.Copy(); handRule.Trigger = "While stickless"; handRule.Effect = CustomRewardEffects.HandPuck; handRule.Validate(); CustomRewardExport.ValidatePlayNow(handRule);
handRule.Trigger = "While owned"; Reject(() => handRule.Validate(), "Hand handling requires an explicit stickless flow without silently removing a stick");
var clockRule = fraction.Copy(); clockRule.Kind = CustomRewardDefinition.CustomRelics; clockRule.Trigger = "While leading"; clockRule.Effect = CustomRewardEffects.ClockSpeed; clockRule.Target = CustomRewardEffects.Clock; clockRule.Amount = 200;
clockRule.Validate(); CustomRewardExport.ValidatePlayNow(clockRule);
Check(clockRule.RulesText.Contains("200%") && clockRule.RulesText.Contains("while leading"), "Clock-rate description states its actual value and team condition");
clockRule.Trigger = "While holding puck"; Reject(() => clockRule.Validate(), "Clock-rate rules cannot invent a skater owner for a team clock");
foreach (string when in new[] { "Scoreless match with an assist", "Match with a broken stick" }) {
    var rule = fraction.Copy(); rule.Trigger = when; rule.Duration = "Rest of run"; rule.Validate();
    Reject(() => CustomRewardExport.ValidatePlayNow(rule), "Completed-match progress cannot travel to Play Now: " + when);
    rule.Effect = CustomRewardEffects.Knockout; Reject(() => rule.Validate(), "Completion triggers cannot start on-ice actions: " + when);
}
foreach (string when in new[] { "Final 20 seconds goal value calculation", "Final 45 seconds goal value calculation" }) {
    var rule = fraction.Copy(); rule.Trigger = when; rule.Effect = CustomRewardEffects.ExtraGoal; rule.Validate(); CustomRewardExport.ValidatePlayNow(rule);
    Check(CustomRewardDefinition.Parse(rule.ToDocument().ToString()).Trigger == when, "Immediate goal valuation preserves its native clock window: " + when);
}
Check(CustomRewardEffects.ForTrigger(CustomRewardDefinition.CustomChaosTalents, "Non-critical shot").Contains(CustomRewardEffects.BreakStick)
    && CustomRewardEffects.ForTrigger(CustomRewardDefinition.CustomChaosTalents, "Teammate knockout").Contains(CustomRewardEffects.Injure), "Chaos stick-breaking and teammate-injury components have their actual native events");
foreach (var impactEffect in new[] { CustomRewardEffects.ImpactKnockout, CustomRewardEffects.ImpactExplosion }) {
    var rule = fraction.Copy(); rule.Trigger = "Shot"; rule.Effect = impactEffect; rule.Target = CustomRewardEffects.Puck;
    rule.Validate(); CustomRewardExport.ValidatePlayNow(rule);
    Check(CustomRewardDefinition.Parse(rule.ToDocument().ToString()).Effect == impactEffect && !CustomRewardEffects.HasAmount(impactEffect), "Contact shot component exports without an unused percentage: " + impactEffect);
    rule.Trigger = "Pass received"; Reject(() => rule.Validate(), "Impact shot cannot attach itself to a later shot: " + impactEffect);
    rule.Trigger = "Shot"; rule.Kind = CustomRewardDefinition.CustomRelics; Reject(() => rule.Validate(), "Destroyer contact mechanics retain their native talent kind: " + impactEffect);
}
var stickShield = fraction.Copy(); stickShield.Trigger = "While owned"; stickShield.Effect = CustomRewardEffects.DeflectSticks; stickShield.Validate(); CustomRewardExport.ValidatePlayNow(stickShield);
Check(!CustomRewardEffects.HasAmount(stickShield.Effect), "Thrown-stick deflection is a separate marker, not an invented force");
var turning = fraction.Copy(); turning.Kind = CustomRewardDefinition.CustomChaosTalents; turning.Trigger = "While owned"; turning.Effect = CustomRewardEffects.TurningDelay; turning.Amount = 150; turning.Validate(); CustomRewardExport.ValidatePlayNow(turning);
Check(turning.RulesText.Contains("150 ms"), "Turning-time component describes its configured duration");
turning.Kind = CustomRewardDefinition.CustomTalents; Reject(() => turning.Validate(), "Instant Ramen's turning component stays in Chaos talents");
foreach (string statName in new[] { "Shot Power", "Speed", "Checking" }) {
    var rule = fraction.Copy(); rule.Kind = CustomRewardDefinition.CustomChaosTalents; rule.Trigger = "While owned"; rule.Effect = CustomRewardEffects.LockAttribute; rule.Stat = statName; rule.Amount = 70;
    rule.Validate(); CustomRewardExport.ValidatePlayNow(rule);
    Check(rule.RulesText.Contains(statName + " locked at 70") && CustomRewardDefinition.Parse(rule.ToDocument().ToString()).Stat == statName, "Attribute locks preserve their actual stat and value across export: " + statName);
    rule.Trigger = "While leading"; Reject(() => rule.Validate(), "Permanent gain locking cannot silently depend on a later score: " + statName);
    rule.Trigger = "While owned"; rule.Stat = "Accuracy"; Reject(() => rule.Validate(), "Chaos attribute locks do not invent an unsupported native attribute");
}
var averaged = fraction.Copy(); averaged.Trigger = "While owned"; averaged.Effect = CustomRewardEffects.BalanceAttributes; averaged.Validate(); CustomRewardExport.ValidatePlayNow(averaged);
Check(!averaged.RulesText.Contains("10%"), "Live attribute averaging does not expose an unused amount");
averaged.Effect = CustomRewardEffects.SpreadAttributeGains; Reject(() => averaged.Validate(), "Permanent gain conversion requires an actual native change flow");
averaged.Trigger = "Permanent attribute change"; averaged.Validate();
Reject(() => CustomRewardExport.ValidatePlayNow(averaged), "Permanent campaign attribute conversion cannot travel to Play Now");
foreach (string eligibility in new[] { CustomRewardEffects.PreferRewards, CustomRewardEffects.ExcludeRewards }) {
    var rule = fraction.Copy(); rule.Trigger = "Player reward eligibility"; rule.Effect = eligibility; rule.Validate();
    Reject(() => CustomRewardExport.ValidatePlayNow(rule), "Player reward eligibility requires native campaign choices: " + eligibility);
    rule.Kind = CustomRewardDefinition.CustomRelics; Reject(() => rule.Validate(), "Player eligibility remains a native owner talent: " + eligibility);
}
foreach (var helmet in new[] { (CustomRewardEffects.GoldenHelmet, "Helmet equipment"), (CustomRewardEffects.HelmetMultiplier, "Helmet effect calculation") }) {
    var rule = fraction.Copy(); rule.Trigger = helmet.Item2; rule.Effect = helmet.Item1; rule.Amount = 2; rule.Validate(); CustomRewardExport.ValidatePlayNow(rule);
    Check(CustomRewardDefinition.Parse(rule.ToDocument().ToString()).Effect == helmet.Item1, "Native helmet rules travel with the exported owner: " + helmet.Item1);
    rule.Target = CustomRewardEffects.Team; Reject(() => rule.Validate(), "An owner's helmet rule cannot silently move or multiply the team's original helmet");
    rule.Target = CustomRewardEffects.Actor; rule.Trigger = "Goal"; Reject(() => rule.Validate(), "Helmet queries cannot become implicit later equipment grants");
}
foreach (string basis in CustomRewardDefinition.AmountBases.Where(CustomRewardDefinition.IsPreviousMatchBasis)) {
    var rule = fraction.Copy(); rule.Trigger = "While owned"; rule.AmountBasis = basis; rule.Validate();
    Reject(() => CustomRewardExport.ValidatePlayNow(rule), "Previous-match snapshots block standalone Play Now export: " + basis);
    Check(rule.RulesText.Contains("last completed match") && !rule.RulesText.Contains("Counts update during play"), "Previous-match rules describe replacement rather than cumulative current-match bonuses: " + basis);
    rule.Kind = CustomRewardDefinition.CustomRelics; Reject(() => rule.Validate(), "Previous-match performance counts keep their native talent kind");
}
var snapshotProgress = new CustomRewardProgress(); string snapshotReward = Guid.NewGuid().ToString();
snapshotProgress.ObserveOwnership("player:A", new[] { snapshotReward });
Check(snapshotProgress.PreviousMatchCount("player:A", snapshotReward) == 0, "A newly owned previous-match talent starts at zero");
snapshotProgress.SetPreviousMatchCount("player:A", snapshotReward, 3); string acceptedSnapshot = snapshotProgress.Save();
snapshotProgress.BeginMatch(); Check(snapshotProgress.PreviousMatchCount("player:A", snapshotReward) == 3, "Starting a match retains the prior completed count");
snapshotProgress.SetPreviousMatchCount("player:A", snapshotReward, 8); snapshotProgress.Load(acceptedSnapshot);
Check(snapshotProgress.PreviousMatchCount("player:A", snapshotReward) == 3, "Abandon rollback restores the previous completed-match snapshot");
snapshotProgress.SetPreviousMatchCount("player:A", snapshotReward, 0); snapshotProgress.Load(snapshotProgress.Save());
Check(snapshotProgress.PreviousMatchCount("player:A", snapshotReward) == 0, "A completed zero-count match replaces the preceding bonus");
snapshotProgress.SetPreviousMatchCount("player:A", snapshotReward, 2); snapshotProgress.ObserveOwnership("player:A", Array.Empty<string>());
snapshotProgress.ObserveOwnership("player:A", new[] { snapshotReward });
Check(snapshotProgress.PreviousMatchCount("player:A", snapshotReward) == 0, "Reacquisition cannot revive a discarded prior-match count");
Reject(() => snapshotProgress.SetPreviousMatchCount("player:A", snapshotReward, -1), "Invalid prior-match statistics are rejected");
string validSnapshots = snapshotProgress.Save(); Reject(() => snapshotProgress.Load(validSnapshots + "\nP\tYQ==\tYg==\t-1"), "Malformed snapshot data cannot replace valid progress");
var rageDuration = fraction.Copy(); rageDuration.Kind = CustomRewardDefinition.CustomRelics; rageDuration.Trigger = "Enraged duration calculation"; rageDuration.Effect = CustomRewardEffects.EnragedDuration; rageDuration.Target = CustomRewardEffects.Team; rageDuration.Amount = 200;
rageDuration.Validate(); CustomRewardExport.ValidatePlayNow(rageDuration);
Check(rageDuration.RulesText.Contains("× 2") && !rageDuration.RulesText.Contains("extra rewards"), "Relic rage duration describes its own multiplier and exports to Play Now");
rageDuration.Trigger = "Goal"; Reject(() => rageDuration.Validate(), "Rage duration only changes a status at its native duration calculation");
rageDuration.Trigger = "Enraged duration calculation"; rageDuration.Kind = CustomRewardDefinition.CustomTalents; Reject(() => rageDuration.Validate(), "Short Fuse duration stays separate from the native talent's all-status extension");
var boardBounce = fraction.Copy(); boardBounce.Trigger = "Board collision"; boardBounce.Effect = CustomRewardEffects.BoardBounce; boardBounce.Amount = 150;
boardBounce.Validate(); CustomRewardExport.ValidatePlayNow(boardBounce);
Check(boardBounce.RulesText.Contains("150% strength") && CustomRewardDefinition.Parse(boardBounce.ToDocument().ToString()).Amount == 150, "Board bounce retains its force scale in descriptions and portable rules");
boardBounce.Trigger = "Goal"; Reject(() => boardBounce.Validate(), "Board bounce requires the current collision normal");
boardBounce.Trigger = "Board collision"; boardBounce.Target = CustomRewardEffects.Team; Reject(() => boardBounce.Validate(), "Board bounce cannot invent contacts for the whole team");
boardBounce.Target = CustomRewardEffects.Actor; boardBounce.Kind = CustomRewardDefinition.CustomRelics; Reject(() => boardBounce.Validate(), "Board Bumper retains its native talent kind");
var soccerWall = fraction.Copy(); soccerWall.Kind = CustomRewardDefinition.CustomRelics; soccerWall.Effect = CustomRewardEffects.SoccerWall; soccerWall.Trigger = "Match setup"; soccerWall.Target = CustomRewardEffects.Team;
soccerWall.Validate(); CustomRewardExport.ValidatePlayNow(soccerWall);
Check(!CustomRewardEffects.HasAmount(soccerWall.Effect) && soccerWall.RulesText.Contains("One wall per team"), "Soccer wall is one native team prop with no unused quantity");
soccerWall.Kind = CustomRewardDefinition.CustomTalents; Reject(() => soccerWall.Validate(), "A soccer wall cannot be a per-skater talent");
soccerWall.Kind = CustomRewardDefinition.CustomRelics; soccerWall.Trigger = "Match won"; Reject(() => soccerWall.Validate(), "A completed match cannot install a wall for an implicit later match");
foreach (string contactEffect in new[] { CustomRewardEffects.ReturnPass, CustomRewardEffects.PunchShot }) {
    var rule = fraction.Copy(); rule.Effect = contactEffect; rule.Trigger = "While owned"; rule.Validate(); CustomRewardExport.ValidatePlayNow(rule);
    Check(!CustomRewardEffects.HasAmount(contactEffect), "Native passing capability has no unrelated stat payoff: " + contactEffect);
    rule.Kind = CustomRewardDefinition.CustomRelics; Reject(() => rule.Validate(), "Passing capability remains a native talent: " + contactEffect);
    rule.Kind = CustomRewardDefinition.CustomTalents; rule.Trigger = "Match won"; Reject(() => rule.Validate(), "Passing capability cannot be enabled by an incompatible completion trigger: " + contactEffect);
}
foreach (string skatingTrigger in new[] { "Skating backward started", "Straight skating for 1.75 seconds" }) {
    var rule = fraction.Copy(); rule.Trigger = skatingTrigger; rule.Effect = CustomRewardEffects.Boost; rule.Amount = 25; rule.Seconds = .15f; rule.Validate(); CustomRewardExport.ValidatePlayNow(rule);
    Check(CustomRewardDefinition.Parse(rule.ToDocument().ToString()).Trigger == skatingTrigger, "Immediate native skating event survives export: " + skatingTrigger);
    rule.Effect = CustomRewardEffects.PuckGhost; rule.Target = CustomRewardEffects.Puck; Reject(() => rule.Validate(), "A skating event does not supply a current shot: " + skatingTrigger);
    rule.Kind = CustomRewardDefinition.CustomGoalieTalents; Reject(() => rule.Validate(), "Forward skating events are not offered for goalie talents");
}
var liveConversion = fraction.Copy(); liveConversion.Kind = CustomRewardDefinition.CustomRelics; liveConversion.Target = CustomRewardEffects.Team;
liveConversion.Trigger = "Attribute bonus conversion"; liveConversion.Effect = CustomRewardEffects.ConvertSpeedBonuses;
liveConversion.Validate(); CustomRewardExport.ValidatePlayNow(liveConversion);
Check(liveConversion.RulesText.Contains("Base attributes stay intact"), "Live Speed conversion describes its separate native scope");
liveConversion.Trigger = "Training attribute conversion"; Reject(() => liveConversion.Validate(), "Live conversion does not implicitly convert permanent training");
liveConversion.Effect = CustomRewardEffects.ConvertSpeedTraining; liveConversion.Validate();
Reject(() => CustomRewardExport.ValidatePlayNow(liveConversion), "Campaign training conversion blocks standalone Play Now export");
liveConversion.Kind = CustomRewardDefinition.CustomTalents; Reject(() => liveConversion.Validate(), "Quantum Sextant components remain team relics");
foreach (string injuryCondition in new[] { "While eye injured", "While shoulder injured", "While leg injured", "While arm injured" }) {
    var rule = fraction.Copy(); rule.Trigger = injuryCondition; rule.Validate();
    Reject(() => CustomRewardExport.ValidatePlayNow(rule), "Specific campaign injury conditions block Play Now: " + injuryCondition);
}
var penaltyImmunity = fraction.Copy(); penaltyImmunity.Trigger = "While owned"; penaltyImmunity.Effect = CustomRewardEffects.IgnoreInjuryPenalties; penaltyImmunity.Validate();
Reject(() => CustomRewardExport.ValidatePlayNow(penaltyImmunity), "An injury-penalty component needs a campaign even with a continuous trigger");
penaltyImmunity.Kind = CustomRewardDefinition.CustomRelics; Reject(() => penaltyImmunity.Validate(), "Masochist's penalty immunity stays in the talent pool");
var armInjury = fraction.Copy(); armInjury.Kind = CustomRewardDefinition.CustomRelics; armInjury.Effect = CustomRewardEffects.InjureArm; armInjury.Trigger = "Injury caused"; armInjury.Target = CustomRewardEffects.Opponent; armInjury.Validate();
Reject(() => CustomRewardExport.ValidatePlayNow(armInjury), "A specific injury cannot silently disappear in Play Now");
armInjury.Trigger = "Match won"; Reject(() => armInjury.Validate(), "Specific injuries need a current on-ice recipient");
var refereePursuit = fraction.Copy(); refereePursuit.Kind = CustomRewardDefinition.CustomRelics; refereePursuit.Trigger = "Match setup"; refereePursuit.Target = CustomRewardEffects.Referees; refereePursuit.Effect = CustomRewardEffects.RefereePursuit;
refereePursuit.Validate(); CustomRewardExport.ValidatePlayNow(refereePursuit);
Check(refereePursuit.RulesText.Contains("latest acquired") && !CustomRewardEffects.HasAmount(refereePursuit.Effect), "Referee AI describes conflict priority without an unused amount");
var physicalChain = fraction.Copy(); physicalChain.Trigger = "While owned"; physicalChain.Effect = CustomRewardEffects.PhysicalBallChain; physicalChain.Validate(); CustomRewardExport.ValidatePlayNow(physicalChain);
Check(physicalChain.RulesText.Contains("One physical chain per skater"), "The ball-and-chain component states its native attachment scope");
physicalChain.Kind = CustomRewardDefinition.CustomRelics; Reject(() => physicalChain.Validate(), "Ball and chain remains a native talent component");
var overtime = fraction.Copy(); overtime.Kind = CustomRewardDefinition.CustomRelics; overtime.Target = CustomRewardEffects.Team; overtime.Trigger = "Final whistle"; overtime.Effect = CustomRewardEffects.PossessionOvertime;
overtime.Validate(); CustomRewardExport.ValidatePlayNow(overtime);
Check(overtime.RulesText.Contains("eligible passes and buzzer shots"), "Possession overtime describes the complete native final-whistle flow");
overtime.Trigger = "Match won"; Reject(() => overtime.Validate(), "Possession overtime cannot revive a completed match");
overtime.Trigger = "Final whistle"; overtime.Kind = CustomRewardDefinition.CustomTalents; Reject(() => overtime.Validate(), "Last Stand stays in the relic pool");
var marginRule = fraction.Copy(); marginRule.Kind = CustomRewardDefinition.CustomRelics; marginRule.Target = CustomRewardEffects.Team; marginRule.Trigger = "Match winner calculation"; marginRule.Effect = CustomRewardEffects.WinByTwo;
marginRule.Validate(); CustomRewardExport.ValidatePlayNow(marginRule);
Check(marginRule.RulesText.Contains("at least two goals"), "Win-by-two rules describe the actual native margin");
marginRule.Kind = CustomRewardDefinition.CustomTalents; Reject(() => marginRule.Validate(), "Tied Homie stays in the native relic pool");
var mercyRule = fraction.Copy(); mercyRule.Trigger = "Match won by mercy"; mercyRule.Duration = "Rest of run"; mercyRule.Validate();
Reject(() => CustomRewardExport.ValidatePlayNow(mercyRule), "Mercy-win progression cannot export to Play Now");
mercyRule.Effect = CustomRewardEffects.Knockout; Reject(() => mercyRule.Validate(), "Mercy completion cannot act on players after the match");
var impossiblePunch = fraction.Copy(); impossiblePunch.Effect = CustomRewardEffects.PunchShot; impossiblePunch.Trigger = "While without puck";
Reject(() => impossiblePunch.Validate(), "A puckless condition cannot punch a possessed puck");
impossiblePunch.Effect = CustomRewardEffects.ReturnPass; impossiblePunch.Trigger = "While holding puck";
Reject(() => impossiblePunch.Validate(), "A receiving-pass capability cannot require possession at pass launch");
var loadedBag = fraction.Copy(); loadedBag.Trigger = "Shared puck value"; loadedBag.Effect = CustomRewardEffects.LoadedPuckBag; loadedBag.Target = CustomRewardEffects.Puck;
loadedBag.NormalPucks = 4; loadedBag.DoublePucks = 2; loadedBag.TriplePucks = 1;
loadedBag.Validate(); CustomRewardExport.ValidatePlayNow(loadedBag);
var copiedBag = CustomRewardDefinition.Parse(loadedBag.ToDocument().ToString());
Check(copiedBag.NormalPucks == 4 && copiedBag.DoublePucks == 2 && copiedBag.TriplePucks == 1 && copiedBag.RulesText.Contains("4 one-goal") && copiedBag.RulesText.Contains("either team"), "Shared puck bag preserves editable counts, scoring scope and exported description");
loadedBag.NormalPucks = loadedBag.DoublePucks = loadedBag.TriplePucks = 0; Reject(() => loadedBag.Validate(), "A shared puck bag cannot be empty");
loadedBag.TriplePucks = -1; Reject(() => loadedBag.Validate(), "A shared puck bag cannot contain a negative count");
loadedBag.TriplePucks = int.MaxValue; loadedBag.Validate(); Check(true, "Unclamped: huge puck bags are authorable; the runtime scales the native queue to 10,000 pucks");
loadedBag.TriplePucks = 1; loadedBag.Validate();
loadedBag.Trigger = "Goal"; Reject(() => loadedBag.Validate(), "Scoring a goal cannot implicitly install a later puck-value queue");
loadedBag.Trigger = "Shared puck value"; loadedBag.Kind = CustomRewardDefinition.CustomRelics; Reject(() => loadedBag.Validate(), "Loaded Puck stays in its native talent kind");
foreach (string capability in new[] { CustomRewardEffects.PunchPass, CustomRewardEffects.FasterWindup, CustomRewardEffects.FasterShotTurning, CustomRewardEffects.BorrowTalent }) {
    var rule = fraction.Copy(); rule.Trigger = "While owned"; rule.Effect = capability;
    rule.Validate(); CustomRewardExport.ValidatePlayNow(rule);
    string capabilityScope = capability == CustomRewardEffects.PunchPass ? "knocked out with the puck" : capability == CustomRewardEffects.BorrowTalent ? "benched teammate" : "slapshot";
    Check(!CustomRewardEffects.HasAmount(capability) && rule.RulesText.Contains(capabilityScope), "Native component has no unused amount and includes its scope: " + capability);
    rule.Kind = CustomRewardDefinition.CustomRelics; Reject(() => rule.Validate(), "Native talent component cannot become a relic: " + capability);
    rule.Kind = CustomRewardDefinition.CustomTalents; rule.Trigger = "Match won"; Reject(() => rule.Validate(), "Native capability cannot start after play ends: " + capability);
}
var punchWithoutPuck = fraction.Copy(); punchWithoutPuck.Effect = CustomRewardEffects.PunchPass; punchWithoutPuck.Trigger = "While without puck";
Reject(() => punchWithoutPuck.Validate(), "A puckless condition cannot punch a pass");
var thresholdBonus = fraction.Copy(); thresholdBonus.Kind = CustomRewardDefinition.CustomRelics; thresholdBonus.Trigger = "While Shot Power is at least 50"; thresholdBonus.Stat = "Speed"; thresholdBonus.Effect = CustomRewardEffects.StatChange; thresholdBonus.Validate(); CustomRewardExport.ValidatePlayNow(thresholdBonus);
Check(CustomRewardEffects.Stats(thresholdBonus.Kind, thresholdBonus.Target, thresholdBonus.Trigger).Contains("Speed"), "Fusilli's live threshold can award Speed");
thresholdBonus.Stat = "Shot Power"; Reject(() => thresholdBonus.Validate(), "A Shot Power threshold cannot award its own qualifying attribute");
thresholdBonus.Stat = "Overall"; Reject(() => thresholdBonus.Validate(), "A threshold cannot indirectly award its qualifying attribute through Overall");
var benchQualification = fraction.Copy(); benchQualification.Trigger = "While owned"; benchQualification.Effect = CustomRewardEffects.StatChange; benchQualification.AmountBasis = "If benched for the entire previous match"; benchQualification.Stat = "Overall";
benchQualification.Validate(); Reject(() => CustomRewardExport.ValidatePlayNow(benchQualification), "Prior-match bench qualification blocks standalone export");
Check(benchQualification.RulesText.Contains("never played") && benchQualification.Copy().AmountBasis == benchQualification.AmountBasis, "Bench qualification survives campaign serialization and requires the entire previous match");
benchQualification.Kind = CustomRewardDefinition.CustomRelics; Reject(() => benchQualification.Validate(), "Bench Juice's history remains a talent behavior");
foreach (var component in new[] { (CustomRewardEffects.RandomShotPower, 148), (CustomRewardEffects.RandomShotAccuracy, 99) }) {
    var rule = fraction.Copy(); rule.Trigger = "While owned"; rule.Effect = component.Item1; rule.Amount = 0;
    rule.Validate(); CustomRewardExport.ValidatePlayNow(rule);
    Check(rule.RulesText.Contains("minimum 0") && rule.Copy().Amount == 0, "Random shot component retains a zero minimum: " + component.Item1);
    rule.Amount = component.Item2; rule.Validate(); rule.Amount++; rule.Validate(); Check(true, "Unclamped: random minimum may exceed the native roll range: " + component.Item1);
    rule.Amount = -1; Reject(() => rule.Validate(), "Random minimum still rejects negatives: " + component.Item1);
    rule.Amount = 10; rule.Trigger = "Shot"; Reject(() => rule.Validate(), "Random attributes must be enabled before launch: " + component.Item1);
    rule.Trigger = "While owned"; rule.Kind = CustomRewardDefinition.CustomRelics; Reject(() => rule.Validate(), "Gambler components remain native talents: " + component.Item1);
}
foreach (var component in new[] { (CustomRewardEffects.SecondScorerHelmet, "Golden Helmet ranking", 1), (CustomRewardEffects.TrainingMultiplier, "Training attribute rewards", 150), (CustomRewardEffects.ExtraCoachPicks, "Coach choices", 1) }) {
    var rule = fraction.Copy(); rule.Kind = CustomRewardDefinition.CustomRelics; rule.Target = CustomRewardEffects.Team; rule.Effect = component.Item1; rule.Trigger = component.Item2; rule.Amount = component.Item3;
    rule.Validate(); Reject(() => CustomRewardExport.ValidatePlayNow(rule), "Campaign-only component blocks incompatible Play Now export: " + component.Item1);
    string detail = component.Item1 == CustomRewardEffects.SecondScorerHelmet ? "second-highest scorer" : component.Item1 == CustomRewardEffects.TrainingMultiplier ? "× 1.5" : "+1 pick at each Coach";
    Check(rule.Copy().Effect == component.Item1 && rule.RulesText.Contains(detail) && rule.RulesText.Length <= 120, "Campaign component keeps its key rule in concise wording: " + component.Item1);
    rule.Kind = CustomRewardDefinition.CustomTalents; Reject(() => rule.Validate(), "Campaign relic component cannot become a player talent: " + component.Item1);
}
var manualDenial = fraction.Copy(); manualDenial.Kind = CustomRewardDefinition.CustomRelics; manualDenial.Target = CustomRewardEffects.Team; manualDenial.Trigger = "Opponent goal reversal window"; manualDenial.Effect = CustomRewardEffects.ManualGoalDenial; manualDenial.Amount = 2;
manualDenial.Validate(); CustomRewardExport.ValidatePlayNow(manualDenial);
Check(manualDenial.Copy().Amount == 2 && manualDenial.RulesText.Contains("zero charges"), "Manual reversal retains its charge capacity and separate exhausted behavior");
manualDenial.Amount = 0; Reject(() => manualDenial.Validate(), "Manual reversal needs a positive capacity");
manualDenial.Amount = 2; manualDenial.Kind = CustomRewardDefinition.CustomTalents; Reject(() => manualDenial.Validate(), "Manual No Goal input remains a native relic component");
var chargeProgress = new CustomRewardProgress(); chargeProgress.ObserveOwnership("team:A", new[] { "manual" });
Check(chargeProgress.SpendCharge("team:A", "manual", 2) && chargeProgress.ChargesSpent("team:A", "manual") == 1, "A manual action consumes exactly one owned charge");
string savedCharge = chargeProgress.Save(); var loadedCharge = new CustomRewardProgress(); loadedCharge.Load(savedCharge);
Check(loadedCharge.ChargesSpent("team:A", "manual") == 1 && loadedCharge.SpendCharge("team:A", "manual", 2) && !loadedCharge.SpendCharge("team:A", "manual", 2), "Charges survive Continue without over-spending");
loadedCharge.Load(savedCharge); Check(loadedCharge.ChargesSpent("team:A", "manual") == 1, "Abandonment snapshot can roll back a spent charge");
Reject(() => loadedCharge.Load(savedCharge + "\nU\tdGVhbTpB\tbWFudWFs\t1"), "Duplicate charge rows cannot replace valid progress");
Check(loadedCharge.ChargesSpent("team:A", "manual") == 1, "Rejected charge progress preserves the prior valid state");
loadedCharge.ObserveOwnership("team:A", Array.Empty<string>()); loadedCharge.ObserveOwnership("team:A", new[] { "manual" });
Check(loadedCharge.ChargesSpent("team:A", "manual") == 0, "Removed and reacquired items get a fresh charge state");
var reversedGoal = fraction.Copy(); reversedGoal.Kind = CustomRewardDefinition.CustomRelics; reversedGoal.Trigger = "Goal reversed"; reversedGoal.Effect = CustomRewardEffects.StatChange; reversedGoal.Target = CustomRewardEffects.Team; reversedGoal.Duration = "Rest of run"; reversedGoal.Validate();
Reject(() => CustomRewardExport.ValidatePlayNow(reversedGoal), "Run-long reversal payoffs block standalone exports");
reversedGoal.Duration = "Rest of match"; CustomRewardExport.ValidatePlayNow(reversedGoal);
Check(CustomRewardEffects.ForTrigger(reversedGoal.Kind, reversedGoal.Trigger).Contains(CustomRewardEffects.StatChange), "Reversal payoff is authored separately from manual denial");
foreach (var component in new[] { CustomRewardEffects.ChargePower, CustomRewardEffects.ChargeAccuracy, CustomRewardEffects.ChargeCritical }) {
    var rule = fraction.Copy(); rule.Trigger = "While owned"; rule.Target = CustomRewardEffects.Actor; rule.Effect = component; rule.Seconds = 2.5f; rule.Amount = 15;
    rule.Validate(); CustomRewardExport.ValidatePlayNow(rule);
    Check(rule.Copy().Seconds == 2.5f && rule.RulesText.Contains("2.5"), "Charged component preserves the selected cap or threshold: " + component);
    rule.Trigger = "Shot"; Reject(() => rule.Validate(), "A charged calculation cannot be activated after launch: " + component);
    rule.Trigger = "While owned"; rule.Kind = CustomRewardDefinition.CustomRelics; Reject(() => rule.Validate(), "Charge Shot components remain regular talents: " + component);
    rule.Kind = CustomRewardDefinition.CustomTalents; rule.Seconds = 61; rule.Validate(); Check(true, "Unclamped: charge time beyond 60 s: " + component);
    rule.Seconds = 0; Reject(() => rule.Validate(), "Charge time must still be above zero: " + component); rule.Seconds = 2.5f;
}
foreach (var effect in new[] { CustomRewardEffects.RandomShotPower, CustomRewardEffects.BalanceAttributes, CustomRewardEffects.ChargePower }) {
    var rule = fraction.Copy(); rule.Trigger = "While Shot Power is at least 50"; rule.Effect = effect;
    Reject(() => rule.Validate(), "Threshold cannot change its own qualifying Shot Power through " + effect);
}
foreach (var component in new[] { (CustomRewardEffects.RiggedFaceoffs, "Faceoff setup", CustomRewardEffects.Team), (CustomRewardEffects.PlayableReferees, "Match setup", CustomRewardEffects.Referees) }) {
    var rule = fraction.Copy(); rule.Kind = CustomRewardDefinition.CustomRelics; rule.Trigger = component.Item2; rule.Target = component.Item3; rule.Effect = component.Item1;
    rule.Validate(); CustomRewardExport.ValidatePlayNow(rule);
    Check(rule.RulesText.Contains(component.Item1 == CustomRewardEffects.RiggedFaceoffs ? "wins faceoffs" : "can be controlled") && !CustomRewardEffects.HasAmount(rule.Effect), "Native referee flow describes its outcome without an unused amount: " + component.Item1);
    rule.Kind = CustomRewardDefinition.CustomTalents; Reject(() => rule.Validate(), "Referee setup stays relic-only: " + component.Item1);
}
var refereeReaction = fraction.Copy(); refereeReaction.Kind = CustomRewardDefinition.CustomRelics; refereeReaction.Trigger = "Referee knocked out"; refereeReaction.Target = CustomRewardEffects.Team; refereeReaction.Effect = CustomRewardEffects.Enrage;
refereeReaction.Validate(); CustomRewardExport.ValidatePlayNow(refereeReaction);
Check(!CustomRewardEffects.ForTrigger(refereeReaction.Kind, refereeReaction.Trigger).Contains(CustomRewardEffects.PuckGhost), "A referee knockout has no current shot flow");
refereeReaction.Target = CustomRewardEffects.Actor; Reject(() => refereeReaction.Validate(), "A team notification cannot select an arbitrary triggering player");
refereeReaction.Target = CustomRewardEffects.Team; refereeReaction.Kind = CustomRewardDefinition.CustomTalents; Reject(() => refereeReaction.Validate(), "Referee Rumble's team event belongs to relics");
foreach (var component in new[] { CustomRewardEffects.BatAim, CustomRewardEffects.BatLift, CustomRewardEffects.BatKnockout, CustomRewardEffects.SlapshotSlowMotion }) {
    var rule = fraction.Copy(); rule.Trigger = "While owned"; rule.Target = CustomRewardEffects.Actor; rule.Effect = component; rule.Radius = 4;
    rule.Validate(); CustomRewardExport.ValidatePlayNow(rule);
    string shotDetail = component == CustomRewardEffects.BatAim ? "aim bat hits" : component == CustomRewardEffects.BatLift ? "upward speed" : component == CustomRewardEffects.BatKnockout ? "either team within 4 units" : "40% game speed";
    Check(rule.RulesText.Contains(shotDetail) && !CustomRewardEffects.HasAmount(component), "Separate native shot component describes its actual scope without an unused amount: " + component);
    rule.Kind = CustomRewardDefinition.CustomRelics; Reject(() => rule.Validate(), "Shot component stays with its native talent kind: " + component);
    rule.Kind = CustomRewardDefinition.CustomTalents; rule.Trigger = "Shot"; Reject(() => rule.Validate(), "Shot capability must be enabled before launch: " + component);
    if (CustomRewardEffects.IsBat(component)) { rule.Trigger = "While holding puck"; Reject(() => rule.Validate(), "A free-puck bat cannot require possession: " + component); }
}
Check(CustomRewardEffects.HasRadius(CustomRewardEffects.BatKnockout) && CustomRewardEffects.Help(CustomRewardEffects.BatKnockout).Contains("teammates and opponents"), "Bat contacts expose radius and native friendly-fire scope");
var batFlight = fraction.Copy(); batFlight.Trigger = "Bat"; batFlight.Target = CustomRewardEffects.Puck; batFlight.Effect = CustomRewardEffects.PuckGhost; batFlight.Validate(); CustomRewardExport.ValidatePlayNow(batFlight);
Check(CustomRewardTriggers.HasShotFlow("Bat"), "An actual bat supplies a current shot for independent flight effects");
foreach (var sample in new[] {
    ("Opponent shot", CustomRewardEffects.Knockout, CustomRewardEffects.Opponent),
    ("Opponent slapshot", CustomRewardEffects.BreakStick, CustomRewardEffects.Opponent),
    ("Opponent pass", CustomRewardEffects.BreakStick, CustomRewardEffects.Opponent),
    ("Opponent ability used", CustomRewardEffects.Frog, CustomRewardEffects.Opponent),
    ("Opponent knocked out", CustomRewardEffects.StatChange, CustomRewardEffects.Opponent),
    ("Opponent crosses blue line", CustomRewardEffects.Knockout, CustomRewardEffects.Opponent),
    ("Opponent goal", CustomRewardEffects.Frog, CustomRewardEffects.Opponent) }) {
    var rule = fraction.Copy(); rule.Kind = CustomRewardDefinition.CustomRelics; rule.Trigger = sample.Item1; rule.Effect = sample.Item2; rule.Target = sample.Item3; rule.Seconds = 1;
    rule.Validate(); CustomRewardExport.ValidatePlayNow(rule);
    Check(rule.RulesText.Contains("opponent") && rule.RulesText.Contains("per team per match"), "Opposing event retains actual source and team-wide activation limits: " + sample.Item1);
    rule.Target = CustomRewardEffects.Actor; Reject(() => rule.Validate(), "Opposing event cannot target an arbitrary teammate as its actor: " + sample.Item1);
}
var opponentPassGhost = fraction.Copy(); opponentPassGhost.Kind = CustomRewardDefinition.CustomRelics; opponentPassGhost.Trigger = "Opponent pass"; opponentPassGhost.Effect = CustomRewardEffects.PuckGhost; opponentPassGhost.Target = CustomRewardEffects.Puck;
Reject(() => opponentPassGhost.Validate(), "Opponent pass does not supply a shot-only flight");
opponentPassGhost.Trigger = "Opponent shot"; opponentPassGhost.Validate(); CustomRewardExport.ValidatePlayNow(opponentPassGhost);
var foreignProgress = new CustomRewardProgress(); var foreignRule = fraction.Copy(); foreignRule.Kind = CustomRewardDefinition.CustomRelics; foreignRule.Trigger = "Opponent knocked out"; foreignRule.Target = CustomRewardEffects.Opponent; foreignRule.Effect = CustomRewardEffects.StatChange; foreignRule.Amount = -3; foreignRule.Duration = "Rest of run";
foreignRule.Validate(); foreignProgress.Signal(foreignRule, foreignRule.Trigger, "team:A", "team:A", CustomRewardProgress.ScopedRecipient("player:copy", "team:B"), 0);
Check(foreignProgress.Bonus("player:copy", "team:B", foreignRule.Stat, 0, (_, _) => true) == -3 && foreignProgress.Bonus("player:copy", "team:A", foreignRule.Stat, 0, (_, _) => true) == 0, "An explicit opponent stat effect distinguishes players with the same ID on different teams");
Check(foreignProgress.Bonus("player:copy", "team:B", foreignRule.Stat, 0, (_, _) => false) == 0 && foreignProgress.Bonus("player:other", "team:B", foreignRule.Stat, 0, (_, _) => true) == 0, "Foreign stat effects require original ownership and exact recipient");
string foreignSaved = foreignProgress.Save(); foreignProgress.Clear(); foreignProgress.Load(foreignSaved);
Check(foreignProgress.Bonus("player:copy", "team:B", foreignRule.Stat, 0, (_, _) => true) == -3, "A qualified opposing recipient survives progress serialization");
Reject(() => CustomRewardExport.ValidatePlayNow(foreignRule), "Run-long opposing progression remains incompatible with Play Now");
foreach (var target in new[] { CustomRewardEffects.OpposingTeam, CustomRewardEffects.BothTeams }) {
    var rule = fraction.Copy(); rule.Kind = CustomRewardDefinition.CustomRelics; rule.Trigger = "While owned"; rule.Target = target; rule.Stat = target == CustomRewardEffects.BothTeams ? "Critical chance" : "Size"; rule.Amount = target == CustomRewardEffects.BothTeams ? 20 : -1;
    rule.Validate(); CustomRewardExport.ValidatePlayNow(rule);
    Check(rule.RulesText.Contains(target == CustomRewardEffects.BothTeams ? "both teams" : "opposing"), "Foreign passive stat description names its recipients: " + target);
    Check(CustomRewardEffects.StatTeamApplies(target, "A", "B") && CustomRewardEffects.StatTeamApplies(target, "B", "A") && CustomRewardEffects.StatTeamApplies(target, "A", "A") == (target == CustomRewardEffects.BothTeams), "Passive stat ownership is applied independently on each team: " + target);
    if (target == CustomRewardEffects.OpposingTeam) {
        // Symmetric targets: an opposing aura is authorable on talents and on
        // any condition; the talent condition is checked on its owner.
        rule.Kind = CustomRewardDefinition.CustomTalents; rule.Validate(); Check(rule.RulesText.Contains("opposing"), "Opposing passive stats are also talent effects");
        rule.Kind = CustomRewardDefinition.CustomRelics; rule.Trigger = "While holding puck"; rule.Validate(); Check(true, "Opposing relic stats accept any condition, checked on the affected skater");
    } else {
        rule.Kind = CustomRewardDefinition.CustomTalents; Reject(() => rule.Validate(), "Both-team passive stats remain native relic effects");
        rule.Kind = CustomRewardDefinition.CustomRelics; rule.Trigger = "While holding puck"; Reject(() => rule.Validate(), "Both-team passive stats cannot imply an undefined source condition");
    }
}
var backdraft = fraction.Copy(); backdraft.Kind = CustomRewardDefinition.CustomRelics; backdraft.Trigger = "Opponent shot"; backdraft.Target = CustomRewardEffects.Opponent; backdraft.Effect = CustomRewardEffects.ShotRecoil; backdraft.Seconds = 1; backdraft.Validate(); CustomRewardExport.ValidatePlayNow(backdraft);
Check(CustomRewardEffects.KindEvidence(backdraft.Effect).Contains("OpponentGainShartRecoil"), "Backdraft recoil is a fixed native relic component");
var redundantKnockout = foreignRule.Copy(); redundantKnockout.Effect = CustomRewardEffects.Knockout; Reject(() => redundantKnockout.Validate(), "A knocked-out event cannot immediately knock out the same unconscious opponent");
var lobRule = fraction.Copy(); lobRule.Effect = CustomRewardEffects.DeflectLob; lobRule.Trigger = "Defensive deflection"; lobRule.Target = CustomRewardEffects.Actor;
lobRule.Validate(); CustomRewardExport.ValidatePlayNow(lobRule);
Check(!CustomRewardEffects.HasAmount(lobRule.Effect) && CustomRewardEffects.Help(lobRule.Effect).Contains("actual defensive deflection"), "Deflection lob is a separate action with native pickup eligibility");
lobRule.Trigger = "Goal"; Reject(() => lobRule.Validate(), "A goal does not supply a defensive rebound to lob");
lobRule.Trigger = "Defensive deflection"; lobRule.Kind = CustomRewardDefinition.CustomRelics; Reject(() => lobRule.Validate(), "Deflection lob remains a native regular-talent component");
foreach (string kind in new[] { CustomRewardDefinition.CustomTalents, CustomRewardDefinition.CustomChaosTalents }) {
    var contract = fraction.Copy(); contract.Kind = kind; contract.Trigger = "Player contract"; contract.Effect = CustomRewardEffects.PlayedMatchContract; contract.Target = CustomRewardEffects.Actor; contract.Amount = 3;
    contract.Validate();
    Check(CustomRewardDefinition.Parse(contract.ToDocument().ToString()).Amount == 3 && contract.RulesText.Contains("3 played matches"), "Native contract terms serialize and describe departure separately from stats: " + kind);
    Reject(() => CustomRewardExport.ValidatePlayNow(contract), "Played-match contracts block Play Now export: " + kind);
    contract.Amount = 0; Reject(() => contract.Validate(), "Contract cannot depart before any played match: " + kind);
    contract.Amount = 3; contract.Kind = CustomRewardDefinition.CustomRelics; Reject(() => contract.Validate(), "Player departure contracts cannot be authored as team relics");
}
var trainingRule = fraction.Copy(); trainingRule.Kind = CustomRewardDefinition.CustomRelics; trainingRule.Trigger = "Training chosen"; trainingRule.Effect = CustomRewardEffects.StatChange; trainingRule.Target = CustomRewardEffects.Team; trainingRule.Duration = "Rest of run"; trainingRule.Amount = 3; trainingRule.Threshold = 2; trainingRule.ActivationsPerMatch = 2; trainingRule.MaxStacks = 2;
trainingRule.Validate(); Reject(() => CustomRewardExport.ValidatePlayNow(trainingRule), "Training-choice progression cannot export to Play Now");
Check(CustomRewardEffects.ForTrigger(trainingRule.Kind, trainingRule.Trigger).SequenceEqual(new[] { CustomRewardEffects.StatChange }), "Training choice offers only an immediately applicable campaign bonus");
Check(trainingRule.RulesText.Contains("per run") && !trainingRule.RulesText.Contains("per match"), "Training counters describe their actual run lifetime");
var trainingProgress = new CustomRewardProgress(); trainingProgress.ObserveOwnership("team:training", new[] { trainingRule.Id });
bool Train() => trainingProgress.Signal(trainingRule, trainingRule.Trigger, "team:training", "team:training", "team:training", 0);
Check(!Train(), "First training choice advances a two-choice threshold without granting stats");
string trainingSaved = trainingProgress.Save(); trainingProgress.Clear(); trainingProgress.Load(trainingSaved); trainingProgress.BeginMatch();
Check(Train() && trainingProgress.Bonus("player:x", "team:training", trainingRule.Stat, 0, (_, _) => true) == 3, "Training threshold survives Continue and a new match before its earned bonus");
Check(!Train() && Train() && !Train() && !Train(), "Training activation cap spans the run instead of resetting each match");
Check(trainingProgress.Bonus("player:x", "team:training", trainingRule.Stat, 0, (_, _) => true) == 6 && trainingProgress.Bonus("player:x", "team:other", trainingRule.Stat, 0, (_, _) => true) == 0, "Training rewards stack only on the owning team");
trainingProgress.ObserveOwnership("team:training", Array.Empty<string>()); trainingProgress.ObserveOwnership("team:training", new[] { trainingRule.Id });
Check(trainingProgress.Bonus("player:x", "team:training", trainingRule.Stat, 0, (_, _) => true) == 0 && !Train() && Train(), "Removing and reacquiring a training relic resets its own earned bonus and counter");
var qualifiedTalentProgress = new CustomRewardProgress(); var qualifiedTalent = fraction.Copy(); qualifiedTalent.Trigger = "Goal"; qualifiedTalent.Target = CustomRewardEffects.Team; qualifiedTalent.Duration = "Rest of run";
string qualifiedOwner = CustomRewardProgress.ScopedRecipient("player:same", "team:A");
qualifiedTalentProgress.Signal(qualifiedTalent, "Goal", qualifiedOwner, qualifiedOwner, "team:A", 0);
Check(qualifiedTalentProgress.Bonus("player:same", "team:A", qualifiedTalent.Stat, 0, (_, owner) => owner == qualifiedOwner) == qualifiedTalent.Amount && qualifiedTalentProgress.Bonus("player:same", "team:B", qualifiedTalent.Stat, 0, (_, _) => true) == 0, "A talent's team-scoped owner can buff its own team without bleeding to a duplicate player ID");
Check(qualifiedTalentProgress.Bonus("player:same", "team:A", qualifiedTalent.Stat, 0, (_, _) => false) == 0, "Qualified talent gains still require the exact owner's talent");
foreach (string kind in new[] { CustomRewardDefinition.CustomTalents, CustomRewardDefinition.CustomChaosTalents }) {
    var automaticShot = fraction.Copy(); automaticShot.Kind = kind; automaticShot.Trigger = "While owned"; automaticShot.Target = CustomRewardEffects.Actor; automaticShot.Effect = CustomRewardEffects.RandomAutoShot; automaticShot.Amount = 50; automaticShot.Seconds = 2; automaticShot.ShotDelayMilliseconds = 560;
    automaticShot.Validate(); CustomRewardExport.ValidatePlayNow(automaticShot);
    Check(automaticShot.Copy().ShotDelayMilliseconds == 560 && automaticShot.RulesText.Contains("50%") && automaticShot.RulesText.Contains("560ms"), "Automatic shots retain separate native values and export with the owner: " + kind);
    automaticShot.Trigger = "While holding puck"; automaticShot.Validate();
    automaticShot.Trigger = "Shot"; Reject(() => automaticShot.Validate(), "An already launched shot cannot start a persistent automatic-shot controller");
    automaticShot.Trigger = "While trailing"; Reject(() => automaticShot.Validate(), "Automatic-shot adapter does not claim unsupported conditional controller timing");
    automaticShot.Trigger = "While owned"; automaticShot.Kind = CustomRewardDefinition.CustomRelics; Reject(() => automaticShot.Validate(), "Native automatic shooting belongs to regular and Chaos talents");
    automaticShot.Kind = kind; automaticShot.ShotDelayMilliseconds = -1; Reject(() => automaticShot.Validate(), "Native shot warning cannot have a negative delay");
}
var shellfish = fraction.Copy(); shellfish.Kind = CustomRewardDefinition.CustomChaosTalents; shellfish.Trigger = "While playing"; shellfish.Effect = CustomRewardEffects.StatChange; shellfish.Target = CustomRewardEffects.OtherTeammates; shellfish.Amount = 30;
shellfish.Validate(); CustomRewardExport.ValidatePlayNow(shellfish);
Check(shellfish.RulesText.Contains("other playing teammates"), "Shellfish's separate team payoff excludes its owner and benched recipients");
shellfish.Kind = CustomRewardDefinition.CustomRelics; Reject(() => shellfish.Validate(), "An owner-excluding talent contribution is not a team relic recipient");
shellfish.Kind = CustomRewardDefinition.CustomChaosTalents; shellfish.Trigger = "Goal"; Reject(() => shellfish.Validate(), "Other playing teammates is an explicitly continuous contribution");
foreach (string direction in new[] { "While moving toward own end", "While moving toward opposing end", "While outside period 1" }) {
    var rule = fraction.Copy(); rule.Kind = CustomRewardDefinition.CustomChaosTalents; rule.Trigger = direction; rule.Target = CustomRewardEffects.Actor; rule.Effect = CustomRewardEffects.StatChange; rule.Stat = "Speed"; rule.Validate(); CustomRewardExport.ValidatePlayNow(rule);
    Check(CustomRewardTriggers.ForKind(rule.Kind).Contains(direction) && !CustomRewardTriggers.ForKind(CustomRewardDefinition.CustomRelics).Contains(direction), "Chaos condition keeps native ownership and exports with its player: " + direction);
}
Check(CustomRewardTriggers.EndDirection(true, 1, -1) == 1 && CustomRewardTriggers.EndDirection(true, -1, 1) == 1 && CustomRewardTriggers.EndDirection(true, -1, 0) == -1, "Tailwind's native own-end priority uses both inertia and intended movement");
Check(CustomRewardTriggers.EndDirection(false, -1, 1) == 1 && CustomRewardTriggers.EndDirection(false, 1, 0) == -1 && CustomRewardTriggers.EndDirection(false, 0, 0) == 0, "Tailwind reverses with rink direction and grants no stationary direction");
var trenchcoat = fraction.Copy(); trenchcoat.Kind = CustomRewardDefinition.CustomChaosTalents; trenchcoat.Trigger = "While owned"; trenchcoat.Effect = CustomRewardEffects.StatChange; trenchcoat.Target = CustomRewardEffects.Actor; trenchcoat.AmountBasis = "Per other owned talent"; trenchcoat.Amount = -10;
trenchcoat.Validate(); CustomRewardExport.ValidatePlayNow(trenchcoat);
Check(trenchcoat.RulesText.Contains("other owned talent"), "Trenchcoat's counted Chaos penalty can be authored and exported independently");
var fever = trenchcoat.Copy(); fever.Kind = CustomRewardDefinition.CustomTalents; fever.Trigger = "While outside period 1"; fever.AmountBasis = "Fixed amount"; fever.Validate();
Check(CustomRewardEffects.ForTrigger(fever.Kind, fever.Trigger).Contains(CustomRewardEffects.StatChange), "First Period Fever exposes its separate non-first-period penalty to regular talents");
foreach (var triggerName in new[] { "While wearing a special helmet", "While in final 20 seconds", "While team has no opposing knockouts", "While size L, XL or XXL" }) {
    var rule = fraction.Copy(); rule.Kind = CustomRewardDefinition.CustomRelics; rule.Trigger = triggerName; rule.Effect = CustomRewardEffects.StatChange; rule.Target = CustomRewardEffects.Team; rule.Validate(); CustomRewardExport.ValidatePlayNow(rule);
    Check(CustomRewardEffects.ForTrigger(rule.Kind, triggerName).Contains(CustomRewardEffects.StatChange), "Native relic condition is selectable and match compatible: " + triggerName);
}
Check(CustomRewardEffects.Targets(CustomRewardDefinition.CustomTalents, "Pass intercepted", CustomRewardEffects.SonicKnockouts).Contains(CustomRewardEffects.Puck)
    && !CustomRewardEffects.Targets(CustomRewardDefinition.CustomTalents, "Goal", CustomRewardEffects.SonicKnockouts).Contains(CustomRewardEffects.Puck), "Only an intercepted pass supplies the sonic wave's remote puck center");
// ---- Unclamped values, symmetric targets and Extra goals (2026-10-19) ----
string IconFor(CustomRewardDefinition r) => authored.IconPng;
CustomRewardDefinition RoundTrip(CustomRewardDefinition r) { r.IconPng = IconFor(r); var back = CustomRewardDefinition.Parse(r.ToDocument().ToString()); Check(back.ToDocument().ToString() == r.ToDocument().ToString(), "Save/Parse round-trip is exact: " + r.Name); return back; }
// Old clamp edges are now accepted.
var edge = new CustomRewardDefinition { Name = "Edge", Trigger = "Goal", Stat = "Speed", Target = CustomRewardEffects.Actor, Duration = "Seconds", IconPng = authored.IconPng };
foreach (var (label, set) in new (string, Action<CustomRewardDefinition>)[] {
    ("amount 1000", r => r.Amount = 1000), ("amount -1000", r => r.Amount = -1000), ("amount int.MaxValue", r => r.Amount = int.MaxValue), ("amount int.MinValue", r => r.Amount = int.MinValue),
    ("threshold 1000", r => r.Threshold = 1000), ("activations 1000", r => r.ActivationsPerMatch = 1000), ("unlimited activations", r => r.ActivationsPerMatch = CustomRewardDefinition.Unlimited),
    ("stacks 1000", r => r.MaxStacks = 1000), ("unlimited stacks", r => r.MaxStacks = CustomRewardDefinition.Unlimited), ("seconds 3601", r => r.Seconds = 3601), ("seconds 0.01", r => r.Seconds = .01f),
    ("seconds 1e30", r => r.Seconds = 1e30f) }) {
    var r = edge.Copy(); set(r); r.Validate(false); Check(true, "Old clamp edge now valid: " + label); RoundTrip(r);
}
var size = edge.Copy(); size.Stat = "Size"; size.Amount = 6; size.Validate(false); Check(true, "Size steps beyond ±5 are authorable; the runtime clamps to the six native sizes");
foreach (var (label, set) in new (string, Action<CustomRewardDefinition>)[] {
    ("zero amount", r => r.Amount = 0), ("zero seconds", r => r.Seconds = 0), ("negative seconds", r => r.Seconds = -1), ("NaN seconds", r => r.Seconds = float.NaN), ("infinite seconds", r => r.Seconds = float.PositiveInfinity),
    ("zero threshold", r => r.Threshold = 0), ("zero activations", r => r.ActivationsPerMatch = 0), ("zero stacks", r => r.MaxStacks = 0), ("chance 0", r => r.Chance = 0), ("chance 101", r => r.Chance = 101) }) {
    var r = edge.Copy(); set(r); Reject(() => r.Validate(false), "Real limit kept: " + label);
}
var unlimitedEdge = edge.Copy(); unlimitedEdge.ActivationsPerMatch = CustomRewardDefinition.Unlimited; unlimitedEdge.MaxStacks = CustomRewardDefinition.Unlimited;
Check(unlimitedEdge.RulesText.Contains("Unlimited activations") && unlimitedEdge.RulesText.Contains("Unlimited stacks"), "Unlimited activations and stacks read as unlimited");
foreach (var (effect, kind, trigger, target, amount, radius) in new[] {
    (CustomRewardEffects.KnockoutDuration, CustomRewardDefinition.CustomRelics, "Knockout duration calculation", CustomRewardEffects.OpposingTeam, 401, 5),
    (CustomRewardEffects.TrainingMultiplier, CustomRewardDefinition.CustomRelics, "Training attribute rewards", CustomRewardEffects.Team, 401, 5),
    (CustomRewardEffects.EnragedDuration, CustomRewardDefinition.CustomRelics, "Enraged duration calculation", CustomRewardEffects.Team, 50, 5),
    (CustomRewardEffects.ExtraCoachPicks, CustomRewardDefinition.CustomRelics, "Coach choices", CustomRewardEffects.Team, 11, 5),
    (CustomRewardEffects.Boost, CustomRewardDefinition.CustomTalents, "Shot", CustomRewardEffects.Actor, 101, 5),
    (CustomRewardEffects.PushSkaters, CustomRewardDefinition.CustomTalents, "Shot", CustomRewardEffects.Actor, 401, 31),
    (CustomRewardEffects.Knockout, CustomRewardDefinition.CustomTalents, "Shot", CustomRewardEffects.Nearby, 0, 31) }) {
    var r = new CustomRewardDefinition { Name = effect, Kind = kind, Trigger = trigger, Effect = effect, Target = target, Amount = amount, Radius = radius, Duration = "Seconds", Seconds = 6 };
    r.Validate(false); Check(true, "Old per-effect clamp removed: " + effect + " " + amount + (radius > 30 ? " radius " + radius : ""));
}
// Symmetric targets: every kind × trigger × effect with a your-side player
// recipient offers each opposing equivalent, or records why not.
int combos = 0, exclusions = 0; var reasons = new Dictionary<string, int>();
foreach (var kind in CustomRewardDefinition.Kinds)
    foreach (var trigger in CustomRewardEffects.TriggersForKind(kind))
        foreach (var effect in CustomRewardEffects.ForTrigger(kind, trigger)) {
            var baseTargets = CustomRewardEffects.BaseTargets(kind, trigger, effect).ToArray();
            var targets = CustomRewardEffects.Targets(kind, trigger, effect).ToArray();
            if (!baseTargets.All(targets.Contains)) throw new Exception("Previously valid recipient removed: " + kind + "/" + trigger + "/" + effect);
            if (!baseTargets.Any(CustomRewardEffects.OwnSidePlayerTargets.Contains)) continue;
            combos++;
            foreach (var opposing in CustomRewardEffects.OpposingPlayerTargets) {
                if (opposing == CustomRewardEffects.Opponent && !CustomRewardTriggers.HasOtherPlayer(trigger)) continue;
                if (targets.Contains(opposing)) continue;
                string reason = CustomRewardEffects.OpposingExclusion(kind, trigger, effect, opposing);
                if (string.IsNullOrWhiteSpace(reason) && !baseTargets.Contains(opposing)) throw new Exception("Opposing recipient missing without a recorded reason: " + kind + "/" + trigger + "/" + effect + "/" + opposing);
                exclusions++; if (!string.IsNullOrWhiteSpace(reason)) reasons[reason] = reasons.GetValueOrDefault(reason) + 1;
            }
            // Every offered recipient yields a valid rule with some stat/value.
            foreach (var t in targets.Where(CustomRewardEffects.IsOpposingSide)) {
                var stats = CustomRewardEffects.EffectStats(kind, t, trigger, effect).ToArray();
                if (effect == CustomRewardEffects.StatChange && stats.Length == 0) throw new Exception("No stat for opposing stat target: " + kind + "/" + trigger + "/" + t);
            }
        }
foreach (var pair in reasons.OrderByDescending(p => p.Value)) Console.WriteLine("EXCLUSION " + pair.Value + " | " + pair.Key);
Check(combos > 300, $"Symmetric targets checked across {combos} kind/trigger/effect combinations; {exclusions} opposing omissions all carry a recorded reason");
foreach (var (trigger, effect) in new[] { ("Pass sent", CustomRewardEffects.StatChange), ("Knockout", CustomRewardEffects.StatChange), ("Shot", CustomRewardEffects.Enrage), ("Check", CustomRewardEffects.Frog),
    ("Goal", CustomRewardEffects.KnockoutImmunity), ("Shot", CustomRewardEffects.Boost), ("Shot", CustomRewardEffects.SonicKnockouts), ("Shot", CustomRewardEffects.PushSkaters), ("Shot", CustomRewardEffects.Burst),
    ("Shot", CustomRewardEffects.EggExplosion), ("Shot", CustomRewardEffects.RandomMatchAbility), ("While owned", CustomRewardEffects.StatChange), ("While holding puck", CustomRewardEffects.ForbidCheck),
    ("While owned", CustomRewardEffects.AiChase), ("While owned", CustomRewardEffects.CooldownMultiplier), ("Slapshot power calculation", CustomRewardEffects.StatChange) }) {
    var targets = CustomRewardEffects.Targets(CustomRewardDefinition.CustomTalents, trigger, effect).ToArray();
    foreach (var opposing in new[] { CustomRewardEffects.OpposingTeam, CustomRewardEffects.RandomOpponents, CustomRewardEffects.ClosestOpponent, CustomRewardEffects.OpponentCarrier, CustomRewardEffects.Nearby })
        Check(targets.Contains(opposing), "Talent " + trigger + " → " + effect + " offers " + opposing);
    if (CustomRewardTriggers.HasOtherPlayer(trigger)) Check(targets.Contains(CustomRewardEffects.Opponent), "Talent " + trigger + " → " + effect + " offers the other player in the event");
}
Check(!CustomRewardEffects.Targets(CustomRewardDefinition.CustomTalents, "Match won", CustomRewardEffects.StatChange).Contains(CustomRewardEffects.OpposingTeam)
    && CustomRewardEffects.OpposingExclusion(CustomRewardDefinition.CustomTalents, "Match won", CustomRewardEffects.StatChange, CustomRewardEffects.OpposingTeam) == CustomRewardEffects.AfterMatchEnds, "After-match rules record why opponents are excluded");
Check(CustomRewardEffects.Targets(CustomRewardDefinition.CustomRelics, "Final whistle", CustomRewardEffects.PossessionOvertime).SequenceEqual(new[] { CustomRewardEffects.Team, CustomRewardEffects.OpposingTeam })
    && CustomRewardEffects.OpposingExclusion(CustomRewardDefinition.CustomRelics, "Final whistle", CustomRewardEffects.PossessionOvertime, CustomRewardEffects.Nearby) == CustomRewardEffects.TeamLevel, "Whole-team native rules extend to the opposing team only as a team");
// Melted Cheese: Pass sent, 25%, closest opponent, Speed -100 for 10 s, unlimited.
var cheese = new CustomRewardDefinition { Name = "Melted Cheese", Kind = CustomRewardDefinition.CustomTalents, Trigger = "Pass sent", Effect = CustomRewardEffects.StatChange, Chance = 25,
    Target = CustomRewardEffects.ClosestOpponent, Stat = "Speed", Amount = -100, Duration = "Seconds", Seconds = 10, ActivationsPerMatch = CustomRewardDefinition.Unlimited, MaxStacks = 1 };
cheese.Validate(false); var cheeseBack = RoundTrip(cheese);
Check(cheeseBack.RulesText.Contains("25% chance") && cheeseBack.RulesText.Contains("closest to this skater") && cheeseBack.RulesText.Contains("-100 Speed") && cheeseBack.RulesText.Contains("Unlimited activations"), "Melted Cheese describes its chance, recipient, value and unlimited activations");
Check(!CustomRewardEffects.Stats(cheese.Kind, cheese.Target, cheese.Trigger).Any(s => s.StartsWith("Goalie ")), "Selected opposing skaters never receive goalie attributes");
// Timed stats on several opposing recipients stack and expire independently.
var cheeseProgress = new CustomRewardProgress();
Check(cheeseProgress.Signal(cheese, "Pass sent", "a", "team:A|player:a", () => new[] { "team:B|player:x", "team:B|player:y" }, 0, roll: () => true), "One activation reaches every selected recipient");
Check(cheeseProgress.Bonus("player:x", "team:B", "Speed", 5, (_, _) => true) == -100 && cheeseProgress.Bonus("player:y", "team:B", "Speed", 5, (_, _) => true) == -100
    && cheeseProgress.Bonus("player:a", "team:A", "Speed", 5, (_, _) => true) == 0, "Opposing timed stat reaches only the selected opponents");
Check(cheeseProgress.Bonus("player:x", "team:B", "Speed", 10.01, (_, _) => true) == 0, "Opposing timed stat expires after its seconds");
Check(!cheeseProgress.Signal(cheese, "Pass sent", "a", "team:A|player:a", () => Array.Empty<string>(), 11, roll: () => true), "An activation without any opposing recipient is not spent");
for (int i = 0; i < 1500; i++) cheeseProgress.Signal(cheese, "Pass sent", "a", "team:A|player:a", () => new[] { "team:B|player:x" }, 20 + i, roll: () => true);
Check(cheeseProgress.Bonus("player:x", "team:B", "Speed", 1519.5, (_, _) => true) == -100, "Unlimited activations keep firing past the old 999 cap, refreshing one stack");
var teamProgress = new CustomRewardProgress();
var relicTeamStat = new CustomRewardDefinition { Name = "Opposing slow", Kind = CustomRewardDefinition.CustomRelics, Trigger = "Goal", Target = CustomRewardEffects.OpposingTeam, Stat = "Speed", Amount = -5, Duration = "Seconds", Seconds = 3 };
relicTeamStat.Validate(false);
teamProgress.Signal(relicTeamStat, "Goal", "team:A|player:a", "team:A", () => new[] { "team:B" }, 0, roll: () => true);
Check(teamProgress.Bonus("player:x", "team:B", "Speed", 1, (_, _) => true) == -5 && teamProgress.Bonus("player:a", "team:A", "Speed", 1, (_, _) => true) == 0, "A relic's opposing-team stat reaches the opposing team, not its owner");
var big = new CustomRewardDefinition { Name = "Big run", Trigger = "Goal", Stat = "Shot Power", Target = CustomRewardEffects.Actor, Amount = 5000, MaxStacks = 2000, Duration = "Rest of run" };
var bigProgress = new CustomRewardProgress(); for (int i = 0; i < 1001; i++) { bigProgress.BeginMatch(); bigProgress.Signal(big, "Goal", "p", "team:A|player:p", "team:A|player:p", i); }
var bigSaved = bigProgress.Save(); bigProgress.Load(bigSaved);
Check(bigProgress.Bonus("player:p", "team:A", "Shot Power", 0, (_, _) => true) == 5000 * 1001, "Run progress with amounts and stacks above the old 999 limits saves and loads");
// Extra goals: Forty Goal Puck relic and talent variants.
var fortyGoal = new CustomRewardDefinition { Name = "Forty Goal Puck", Kind = CustomRewardDefinition.CustomRelics, Trigger = "Goal value calculation", Effect = CustomRewardEffects.ExtraGoals, Target = CustomRewardEffects.Team, Amount = 39, Duration = "Seconds" };
fortyGoal.Validate(false); var fortyBack = RoundTrip(fortyGoal);
Check(fortyBack.RulesText.Contains("counts as 40 goals") && CustomRewardEffects.HasAmount(CustomRewardEffects.ExtraGoals), "Forty Goal Puck reads as one goal counting as 40");
Check(CustomRewardEffects.Targets(CustomRewardDefinition.CustomRelics, "Goal value calculation", CustomRewardEffects.ExtraGoals).SequenceEqual(new[] { CustomRewardEffects.Team, CustomRewardEffects.OpposingTeam })
    && CustomRewardEffects.Targets(CustomRewardDefinition.CustomTalents, "Critical goal value calculation", CustomRewardEffects.ExtraGoals).SequenceEqual(new[] { CustomRewardEffects.Actor, CustomRewardEffects.Team, CustomRewardEffects.OpposingTeam }), "Extra goals: relic Team/Opposing team, talent Actor/Team/Opposing team");
foreach (var trigger in CustomRewardTriggers.All.Select(t => t.Name).Where(CustomRewardTriggers.IsGoalValueCalculation))
    Check(CustomRewardEffects.ForTrigger(CustomRewardDefinition.CustomRelics, trigger).Contains(CustomRewardEffects.ExtraGoals) && CustomRewardEffects.ForTrigger(CustomRewardDefinition.CustomTalents, trigger).Contains(CustomRewardEffects.ExtraGoals), "Extra goals is offered at " + trigger);
Check(!CustomRewardEffects.ForTrigger(CustomRewardDefinition.CustomTalents, "Goal").Contains(CustomRewardEffects.ExtraGoals) && !CustomRewardEffects.SupportsKind(CustomRewardEffects.ExtraGoals, CustomRewardDefinition.CustomGoalieTalents), "Extra goals stays on goal-value calculations of skater talents and relics");
fortyGoal.Amount = 0; Reject(() => fortyGoal.Validate(false), "Extra goals needs at least one extra goal");
fortyGoal.Amount = int.MaxValue; fortyGoal.Validate(false); Check(true, "Extra goals has no authored ceiling (runtime adds at most 9,999 per goal)");
CustomRewardExport.ValidatePlayNow(fortyBack); Check(true, "Forty Goal Puck can travel to Play Now");
var fortyTalent = new CustomRewardDefinition { Name = "Forty talent", Kind = CustomRewardDefinition.CustomTalents, Trigger = "Goal value calculation", Effect = CustomRewardEffects.ExtraGoals, Target = CustomRewardEffects.Actor, Amount = 39, Duration = "Seconds" };
fortyTalent.Validate(false); RoundTrip(fortyTalent);
CustomRewardDefinition.Parse(CustomRewardDefinition.Parse(cheese.ToDocument().ToString()).ToDocument().ToString()).Validate();
Check(true, "Unlimited and new-target definitions survive repeated parse");
// Base-game trigger gaps found by the 26067234 coverage audit.
Check(CustomRewardEffects.TriggersForKind(CustomRewardDefinition.CustomGoalieTalents).Contains("Goalie save") && CustomRewardEffects.TriggersForKind(CustomRewardDefinition.CustomRelics).Contains("Goalie save")
    && !CustomRewardEffects.TriggersForKind(CustomRewardDefinition.CustomTalents).Contains("Goalie save"), "Goalie Dance / Rubbermade: own goalie's successful save is a goalie-talent and relic trigger");
var dance = new CustomRewardDefinition { Name = "Dance save", Kind = CustomRewardDefinition.CustomGoalieTalents, Trigger = "Goalie save", Effect = CustomRewardEffects.Dance, Target = CustomRewardEffects.Nearby, Radius = 6, Duration = "Seconds", Seconds = 3 };
dance.Validate(false); Check(true, "Goalie Dance can be rebuilt: save makes nearby opponents dance");
foreach (var trigger in new[] { "Team first goal value calculation", "Final 30 seconds goal value calculation", "Final 60 seconds goal value calculation" })
    Check(CustomRewardTriggers.IsGoalValueCalculation(trigger) && CustomRewardEffects.ForTrigger(CustomRewardDefinition.CustomRelics, trigger).Contains(CustomRewardEffects.ExtraGoal), "Remarkable Rock+ / Buzzer Beater+ goal value trigger: " + trigger);
var foiled = new CustomRewardDefinition { Name = "Foiled", Kind = CustomRewardDefinition.CustomRelics, Trigger = "Injured by an opponent", Effect = CustomRewardEffects.Injure, Target = CustomRewardEffects.Opponent, Duration = "Seconds" };
foiled.Validate(false); Check(CustomRewardTriggers.HasOtherPlayer(foiled.Trigger), "Foiled: the injuring opponent is the other player in the event");
var injuredClock = new CustomRewardDefinition { Name = "Injured Clock", Kind = CustomRewardDefinition.CustomRelics, Trigger = "Any skater injured", Effect = CustomRewardEffects.ClockTime, Target = CustomRewardEffects.Clock, Amount = 10, Duration = "Seconds" };
injuredClock.Validate(false); Check(true, "Injured Clock: any skater's injury on either team adjusts the clock");
Console.WriteLine($"{checks} technical checks passed; gameplay confirmation remains pending.");
