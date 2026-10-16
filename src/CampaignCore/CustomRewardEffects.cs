using System;
using System.Collections.Generic;
using System.Linq;

namespace CampaignCore;

// Shared by Studio and the game. A visible choice is a runtime contract, not a
// native asset name. Explicit copy effects grant the named powers; other
// adapters do not grant an unrelated native talent or retain its trigger.
public static partial class CustomRewardEffects
{
    public const string StatChange = "Stat change", Enrage = "Enrage", Frog = "Turn into a frog",
        Knockout = "Knock out", BreakStick = "Break stick", RestoreStick = "Restore stick",
        KnockoutImmunity = "Knockout immunity", PokecheckImmunity = "Pokecheck immunity", StaggerImmunity = "Stagger immunity";
    public const string Actor = "Triggering player", Team = "Whole team", BothTeams = "Both teams", OtherTeammates = "Other playing teammates", Opponent = "Other player in the event", Nearby = "Nearby opponents";
    public const string RandomAllies = "Random teammates", RandomOpponents = "Random opponents", ClosestOpponent = "Closest opponent", OpponentCarrier = "Opposing puck carrier";
    public static bool IsRandomTarget(string target) => target == RandomAllies || target == RandomOpponents;
    public static bool IsForeignTeamTarget(string target) => target == OpposingTeam || target == BothTeams;
    public static bool StatTeamApplies(string target, string ownerTeam, string recipientTeam)
        => target == BothTeams || (target == OpposingTeam ? ownerTeam != recipientTeam : ownerTeam == recipientTeam);
    public const string PuckSpeed = "Puck speed", PuckLift = "Lift puck", Puck = "Current puck";
    public const string AttractPuck = "Attract current puck";
    public const string PuckGhost = "Puck passes through skaters", PuckCurve = "Curve puck", PuckSlow = "Slow puck in flight", PuckSpin = "Spin puck",
        Boost = "Skating boost", Burst = "Knockout burst";
    public sealed class Entry
    {
        public string Name { get; } public string Native { get; } public bool Timed { get; }
        public Entry(string name, string native, bool timed = false) { Name = name; Native = native; Timed = timed; }
    }
    public static readonly Entry[] All = new Entry[] {
        new(StatChange, "ForwardStats / GoaltenderStats modifier lists; Hat Trick Bonus, Momentum and The Impenetrable."),
        new(Enrage, "SkaterStatusEffectsManager.SetEnraged; Rage Switch, Rage Release and Cracked Mask.", true),
        new(Frog, "SkaterStatusEffectsManager.SetFrogged; Frog-Off and Swamp Scoops.", true),
        new(KnockoutImmunity, "StatusTag.ImmuneToKnockout; Passing Rule and Slapshot Immunity.", true),
        new(PokecheckImmunity, "StatusTag.ImmuneToPokecheck; Passing Rule.", true),
        new(StaggerImmunity, "StatusTag.ImmuneToStagger; native immunity status.", true),
        new(Knockout, "Forward.Knockout; Chexplosion and Boom Blocker."),
        new(BreakStick, "Forward.LoseStick; Glass Stick and Toothpick."),
        new(RestoreStick, "Forward.GainStick; the native stick restoration path."),
        new(PuckSpeed, "Modify the current puck velocity after the native shot, pass or rebound. 100% keeps its speed."),
        new(PuckLift, "Set upward velocity on the current puck after the native shot, pass or rebound; native gravity remains active."),
        new(PuckGhost, "Puck.IsDuringXRayShot and CancelCollision, scoped to the current ShootingArg."),
        new(PuckCurve, "CurveBallComponent.Initialize / OnShot / FixedUpdate, scoped to the current shot."),
        new(PuckSlow, "SlowBallComponent.OnShot / FixedUpdate, scoped to the current shot."),
        new(PuckSpin, "TrickShotComponent.Initialize / OnChangedState, scoped to the current shot."),
        new(AttractPuck, "PuckAttractor.StartAttracting / Update; Attract Rebound's steering component, without its original event subscription.", true),
        new(Boost, "SmoothBoostComponent with BoostTrigger.Manual; Blue Line Boost and pass boosts.", true),
        new(Burst, "Forward.Explode; Chexplosion / Big Slapshot, with teammates and the owner excluded.")
    }.Concat(FlowEntries).Concat(LifecycleEntries).ToArray();
    public static Entry Find(string name) => All.FirstOrDefault(e => e.Name == name);
    public static string Help(string effect) => effect switch {
        StatChange => "Change a skater or goalie attribute. Choose how long the gain lasts and whether it stacks.",
        Enrage => "Apply the game's enraged state, including its normal visuals and bonuses.",
        Frog => "Transform the selected skaters into frogs for the chosen duration.",
        KnockoutImmunity => "Protect the recipient from knockouts for the chosen duration.",
        PokecheckImmunity => "Protect the recipient from pokechecks for the chosen duration.",
        StaggerImmunity => "Prevent the recipient from staggering for the chosen duration.",
        Knockout => "Knock out vulnerable skaters. Existing immunity still protects them.",
        BreakStick => "Remove the recipient's current stick. They can pick up another normally.",
        RestoreStick => "Give a stick to a stickless skater when their rules allow one.",
        PuckSpeed => "Change the speed of this shot, pass or rebound. 100% keeps the current speed; 50% halves it.",
        PuckLift => "Send this shot, pass or rebound upward. Normal gravity brings it back down.",
        PuckGhost => "This shot passes through skaters using the game's X-Ray collision handling. Goalies still interact with it normally.",
        PuckCurve => "Curve this shot using the native curve behavior. Strength is a percentage of its base curve.",
        PuckSlow => "Apply the native mid-flight slowdown to this shot. Strength scales its braking force.",
        PuckSpin => "Spin this shot using the native trick-shot behavior. Strength scales the spin force.",
        AttractPuck => "Steer the current free puck toward the triggering skater using native magnet steering. Only a current shot, pass or rebound can start it. Range, duration and strength are editable; possession or leaving play ends the attraction.",
        Boost => "Propel the selected skaters in their skating direction, using the game's smooth boost. Force and duration are editable.",
        Burst => "Create the native knockout explosion around the triggering skater. Your teammates and the triggering skater are protected.",
        _ => FlowHelp(effect)
    };
    public static IEnumerable<string> ForTrigger(string kind, string trigger)
        => AtTrigger(kind, trigger).Where(effect => SupportsKind(effect, kind) && SupportsPossessionCondition(effect, trigger) && (effect != LoosePuckAttraction || trigger != "While holding puck" && trigger != "While team has puck" && trigger != "While opponent has puck") && (effect != RandomAutoShot || trigger == "While owned" || trigger == "While holding puck") && (!IsBat(effect) || trigger != "While holding puck" && trigger != "While team has puck" && trigger != "While opponent has puck") && ((effect != PunchShot && effect != PunchPass) || trigger != "While without puck" && trigger != "While opponent has puck") && (effect != ReturnPass || trigger != "While holding puck" && trigger != "While team has puck" && trigger != "While opponent has puck") && (trigger != "While Shot Power is at least 50" || effect != RandomShotPower && effect != BalanceAttributes && effect != ChargePower) && (effect != LockAttribute || trigger == "While owned") && (effect != HandPuck || trigger == "While stickless") && (effect != ClockSpeed || SupportsClockCondition(trigger)));
    private static bool SupportsPossessionCondition(string effect, string trigger)
    {
        if (effect == AiKarlson || effect == AiPrince || effect == PreventPredictivePoke)
            return trigger != "While without puck" && trigger != "While opponent has puck";
        if (!AiEffects.Contains(effect)) return true;
        if (trigger == "While holding puck") return false;
        if (effect == AiKnight || effect == AiHome || effect == AiMaraud || effect == AiSafety)
            return trigger != "While opponent has puck";
        if (effect == AiBlock || effect == AiRedirect) return trigger != "While team has puck";
        return true;
    }
    public static IEnumerable<string> TriggersForKind(string kind)
        => CustomRewardTriggers.ForKind(kind).Where(trigger => ForTrigger(kind, trigger).Any());
    private static IEnumerable<string> AtTrigger(string kind, string trigger)
    {
        if (!CustomRewardTriggers.ForKind(kind).Contains(trigger)) return Array.Empty<string>();
        if (CustomRewardTriggers.IsCampaignEvent(trigger)) return new[] { StatChange };
        if (trigger == "Opposing goalie save") return new[] { ClockTime, StatChange, Enrage };
        if (trigger == "Team knocks out referee") return new[] { Frog, Enrage, StatChange };
        if (trigger == "Referee knocked out") return new[] { StatChange, Enrage, Frog, Freeze, Dance, KnockoutImmunity, PokecheckImmunity, StaggerImmunity, RestoreStick, GainTalent, RandomMatchAbility };
        if (CustomRewardTriggers.IsQuery(trigger)) return QueryEffects(trigger);
        if (CustomRewardTriggers.IsCondition(trigger)) return new[] { StatChange }.Concat(AiEffects).Concat(ForwardConditionEffects).Concat(ScalingConditionEffects)
            .Concat((kind == CustomRewardDefinition.CustomRelics || kind == CustomRewardDefinition.CustomGoalieTalents)
                && CustomRewardTriggers.ForKind(CustomRewardDefinition.CustomGoalieTalents).Contains(trigger) ? GoalieConditionEffects : Array.Empty<string>());
        if (CustomRewardTriggers.IsCompletion(trigger)) return new[] { StatChange, UpgradeTalent, UpgradeAbility, UpgradeRelic };
        return All.Where(e => (!IsPuck(e.Name) || (IsShotFlight(e.Name) ? CustomRewardTriggers.HasShotFlow(trigger) : CustomRewardTriggers.HasPuckFlow(trigger)))
            && !IsQueryEffect(e.Name) && !AiEffects.Contains(e.Name) && !GoalieConditionEffects.Contains(e.Name) && !ForwardConditionEffects.Contains(e.Name) && !ScalingConditionEffects.Contains(e.Name)
            && (e.Name != BoardBounce || trigger == "Board collision")
            && (e.Name != DeflectLob || trigger == "Defensive deflection")
            && (e.Name != FlatPuck || CustomRewardTriggers.HasShotFlow(trigger) || (trigger == "Pass sent" || trigger == "Opponent pass"))
            && (e.Name != ShotRecoil || CustomRewardTriggers.HasShotFlow(trigger) && CustomRewardTriggers.HasForwardActor(kind, trigger))
            && (e.Name != PassUninterceptable || trigger == "Pass sent" || trigger == "Opponent pass")
            && (e.Name != GoalieHeadshot || trigger == "Goalie blocker rebound" || trigger == "Goalie glove rebound")
            && !IsUpgrade(e.Name)
            && (e.Name != AttractPuck || CustomRewardTriggers.HasPuckFlow(trigger) && CustomRewardTriggers.HasForwardActor(kind, trigger))
            && (e.Name != EggExplosion || CustomRewardTriggers.HasForwardActor(kind, trigger))
            && (e.Name != SonicKnockouts || CustomRewardTriggers.HasForwardActor(kind, trigger))
            && (e.Name != Burst || CustomRewardTriggers.HasForwardActor(kind, trigger))).Select(e => e.Name);
    }
    public static bool IsShotFlight(string effect) => effect == PuckGoalieGhost || effect == PuckGhost || effect == PuckCurve || effect == PuckSlow || effect == PuckSpin || IsImpactShot(effect);
    public static bool IsPuck(string effect) => effect == FlatPuck || effect == PuckSpeed || effect == PuckLift || IsShotFlight(effect);
    public static bool HasAmount(string effect) => effect == BlueLineLaunch || effect == KnockoutDuration || effect == ParticipationBonus || effect == OpeningInjuries || effect == CyclicInjuries || effect == PuckInjuries || effect == VoodooAttribute || IsTemperature(effect) || effect == OpeningCriticals || effect == ReboundCriticals || effect == HealedInjuryBonus || IsTransferHelmet(effect) || effect == RandomMatchAttribute || effect == CenterDefenderChecks || effect == PickupAttribute || effect == BossAttribute || effect == BossSize || effect == FartForce || effect == FartRange || IsCategoryGrant(effect) || effect == AlternateOverall || effect == RetainPowers || effect == SpikedInjury || effect == RandomAutoShot || effect == PlayedMatchContract || effect == ChargePower || effect == ChargeAccuracy || effect == ManualGoalDenial || effect == TrainingMultiplier || effect == ExtraCoachPicks || effect == RandomShotPower || effect == RandomShotAccuracy || effect == BoardBounce || effect == EnragedDuration || effect == HelmetMultiplier || effect == LockAttribute || effect == TurningDelay || effect == RefereeSize || effect == DenyGoal || effect == BenchMultiplier || IsUpgrade(effect) || ScalingConditionEffects.Contains(effect) || effect == InjuryMultiplier || effect == InjuryHealing || effect == GoalieHeadshot || effect == StatChange || effect == ShotRecoil || effect == Boost || effect == ClockTime || effect == TalentRewards || effect == RelicRewards || effect == Rerolls || effect == GoalieFetchSpeed || effect == ExtraSticks || effect == NetWidth || effect == NetHeight || effect == AttractPuck || IsShockwave(effect) || IsPuck(effect) && effect != PuckGhost && effect != PuckGoalieGhost && effect != FlatPuck && !IsImpactShot(effect);
    public static bool HasRadius(string effect) => effect == OctopusAttacks || effect == EggExplosion || effect == SonicKnockouts || effect == LoosePuckAttraction || effect == BatKnockout || effect == ImpactExplosion || effect == Burst || effect == AttractPuck || IsShockwave(effect);
    public static string AmountLabel(string effect) => effect == BlueLineLaunch ? "Native launch force" : effect == KnockoutDuration ? "Knockout duration (%)" : effect == ParticipationBonus ? "Overall bonus per qualifying win" : effect == OpeningInjuries ? "Opening knockouts" : effect == CyclicInjuries ? "Knockouts per injury cycle" : effect == PuckInjuries ? "Injury chance (%)" : effect == VoodooAttribute || IsTemperature(effect) ? "Overall magnitude" : effect == OpeningCriticals ? "Opening team shots" : effect == ReboundCriticals ? "Additional critical chance (%)" : effect == HealedInjuryBonus ? "Overall bonus per healed injury" : IsTransferHelmet(effect) ? "Attribute change while wearing this helmet" : effect == RandomMatchAttribute ? "Bonus per match roll" : effect == CenterDefenderChecks ? "Loaned Bonecrusher level" : effect == PickupAttribute || effect == BossAttribute ? "Permanent base attribute change" : effect == BossSize ? "Permanent native size steps" : effect == FartForce || effect == FartRange ? "Interaction strength (% of native)" : IsCategoryGrant(effect) ? "Granted talent level" : effect == AlternateOverall ? "Alternating Overall magnitude" : effect == RetainPowers ? "Powers retained per eligible benchwarmer" : effect == SpikedInjury ? "Outgoing injury chance (%)" : effect == RandomAutoShot ? "Automatic shot chance (%)" : effect == PlayedMatchContract ? "Played matches before departure" : effect == ChargePower || effect == ChargeAccuracy ? "Attribute bonus per charged second" : effect == ManualGoalDenial ? "Goal-reversal charges" : effect == TrainingMultiplier ? "Training rewards (% of normal)" : effect == ExtraCoachPicks ? "Additional Coach picks" : effect == RandomShotPower || effect == RandomShotAccuracy ? "Minimum random attribute value" : effect == BoardBounce ? "Strength (% of native speed)" : effect == EnragedDuration ? "Enraged duration (%)" : effect == HelmetMultiplier ? "Helmet effect multiplier" : effect == LockAttribute ? "Locked attribute value" : effect == RefereeSize ? "Size steps added / removed" : effect == DenyGoal ? "Chance to deny this goal (%)" : IsUpgrade(effect) ? "Number of different powers to upgrade" : ScalingConditionEffects.Contains(effect) || effect == InjuryMultiplier || effect == GoalieHeadshot || effect == BenchMultiplier ? "Multiplier (% of current value)" : effect == InjuryHealing ? "Recovery games added / removed" : effect == PuckSpeed ? "Puck speed (% of current speed)" : effect == PuckLift ? "Upward velocity (game units per second)"
        : effect == TurningDelay ? "Milliseconds added to turning" : effect == NetWidth || effect == NetHeight ? "Size (% of original dimension)" : IsShotFlight(effect) || IsShockwave(effect) || effect == GoalieFetchSpeed || effect == AttractPuck ? "Strength (% of native base effect)" : effect == ExtraSticks ? "Additional reserve sticks" : (effect == Boost || effect == ShotRecoil) ? "Boost force" : effect == ClockTime ? "Seconds added to the clock" : IsQueryEffect(effect) ? "Extra rewards / rerolls" : "Stat change";
    public static IEnumerable<string> Targets(string kind, string trigger, string effect)
    {
        if (CustomRewardTriggers.IsCampaignEvent(trigger)) return new[] { Team };
        if (trigger == "Random fart timer") return new[] { Actor };
        if (trigger == "Puck fart timer") return new[] { Puck };
        if (effect == FartKnockouts) return new[] { BothTeams, OpposingTeam };
        if (effect == PickupAttribute || effect == BossAttribute || effect == BossSize || effect == FogScoreboard) return new[] { Actor };
        if (effect == VoodooAttribute || effect == OpeningInjuries || effect == CyclicInjuries || effect == PuckInjuries) return new[] { OpposingTeam };
        if (IsCultist(effect) || effect == DefenseTeleport) return new[] { Actor };
        if (effect == BlueLineLaunch || effect == DefensiveBlueLineKnockout) return new[] { OpposingTeam };
        if (effect == OffensiveBlueLineKnockout) return new[] { Team };
        if (effect == KnockoutDuration) return new[] { OpposingTeam };
        if (effect == OpposingBonecrusher || effect == OpposingBriefcase) return new[] { OpposingTeam };
        if (effect == RefereeBlockTalent) return new[] { Actor };
        if (effect == BreakawayShotPenalty || effect == BreakawayShotFall) return new[] { OpposingTeam };
        if (effect == ReboundCriticals) return new[] { Actor };
        if (effect == HealedInjuryBonus) return new[] { RecoveringSkater };
        if (effect == CelebrationInjuries) return new[] { OpposingTeam };
        if (IsTransferHelmet(effect)) return new[] { Actor };
        if (effect == RandomMatchAttribute || effect == CenterDefenderChecks || effect == PreventPredictivePoke) return new[] { Actor };
        if (effect == LiquidateTalents) return new[] { Actor };
        if (effect == ScorelessExpiry || effect == UpgradeOwnTalents || effect == AlternateOverall || effect == RetainPowers) return new[] { Actor };
        if (effect == FrogHelmets || effect == OctopusAttacks) return new[] { OpposingTeam };
        if (effect == EggExplosion) return new[] { Actor };
        if (IsOpponentRelicEffect(effect)) return new[] { OpposingTeam };
        if (effect == GoalPlanks) return new[] { Rink };
        if (effect == SonicKnockouts) return trigger == "Pass intercepted" ? new[] { Actor, Puck } : new[] { Actor };
        if (effect == HungerRage) return new[] { Actor };
        if (effect == PossessionExplosion) return new[] { EitherCarrier };
        if (effect == LoosePuckAttraction) return new[] { Actor };
        if (effect == RandomAutoShot) return new[] { Actor };
        if (effect == PlayedMatchContract) return new[] { Actor };
        if (effect == DeflectLob) return new[] { Actor };
        if (CustomRewardTriggers.IsOpponentAction(trigger) && kind == CustomRewardDefinition.CustomRelics) {
            if (IsPuck(effect) || effect == PassUninterceptable) return new[] { Puck };
            if (effect == ClockTime) return new[] { Clock };
            if (effect == ShotRecoil || IsShockwave(effect)) return new[] { Opponent };
            if (effect == StatChange) return new[] { Team, Opponent };
            var targets = new[] { Opponent, Team, OpposingTeam, RandomAllies, RandomOpponents };
            return trigger == "Opponent knocked out" && (effect == Knockout || effect == Freeze || effect == Dance || effect == Boost || effect == ThrowStick)
                ? targets.Where(t => t != Opponent) : targets;
        }
        if (trigger == "Team knocks out referee") return effect == Frog ? new[] { OpposingTeam } : new[] { Team };
        if (trigger == "Opposing goalie save") return effect == ClockTime ? new[] { Clock } : new[] { Team };
        if (trigger == "Referee knocked out") return new[] { Team };
        if (IsBat(effect)) return new[] { Actor };
        if (IsCharge(effect)) return new[] { Actor };
        if (effect == LoadedPuckBag) return new[] { Puck };
        if (effect == BoardBounce) return new[] { Actor };
        if (effect == SoccerWall || effect == EnragedDuration) return new[] { Team };
        if (effect == GoldenHelmet || effect == HelmetMultiplier || effect == LockAttribute || effect == SpreadAttributeGains || effect == PreferRewards || effect == ExcludeRewards) return new[] { Actor };
        if (effect == ClockSpeed) return new[] { Clock };
        if (CustomRewardTriggers.IsShotCalculation(trigger)) return new[] { Actor, Team };
        if (IsRefereeSetup(effect)) return new[] { Referees };
        if (effect == ExtraGoal) return kind == CustomRewardDefinition.CustomRelics ? new[] { Team } : new[] { Actor, Team };
        if (effect == DenyGoal) return kind == CustomRewardDefinition.CustomRelics ? new[] { OpposingTeam } : new[] { Actor, OpposingTeam };
        if (IsUpgrade(effect) || effect == KeepFreeAgents) return new[] { Team };
        if (effect == BenchMultiplier) return kind == CustomRewardDefinition.CustomRelics ? new[] { Team } : new[] { Actor, Team };
        if (effect == GainTalent || effect == RandomMatchAbility) return CustomRewardTriggers.HasForwardActor(kind, trigger) ? new[] { Actor, Team, RandomAllies } : new[] { Team, RandomAllies };
        if (effect == PassUninterceptable || effect == GoalieHeadshot) return new[] { Puck };
        if (effect == InjuryMultiplier) return new[] { OpposingTeam, Team };
        if (effect == InjuryHealing || ScalingConditionEffects.Contains(effect)) return new[] { Actor, Team };
        if (IsRink(effect) || IsNet(effect)) return new[] { Rink };
        if (IsGoalieCopy(effect)) return new[] { Actor };
        if (effect == AttractPuck) return new[] { Actor };
        if (GoalieConditionEffects.Contains(effect)) return new[] { Actor };
        if (ForwardConditionEffects.Contains(effect)) return new[] { Actor, Team };
        if (IsInjuryPrevention(effect)) return kind == CustomRewardDefinition.CustomRelics || kind == CustomRewardDefinition.CustomGoalieTalents ? new[] { Team } : new[] { Actor, Team };
        if (IsFreeAgentCopy(effect)) return kind == CustomRewardDefinition.CustomRelics ? new[] { Team } : new[] { Actor, Team };
        if (effect == ClockTime) return new[] { Clock };
        if (IsQueryEffect(effect)) return new[] { Team };
        if (AiEffects.Contains(effect)) return CustomRewardTriggers.HasForwardActor(kind, trigger) ? new[] { Actor, Team } : new[] { Team };
        if (IsPuck(effect)) return new[] { Puck };
        if (IsShockwave(effect)) return new[] { Actor };
        if (effect == ShotRecoil) return new[] { Actor };
        if (effect == Burst) return new[] { Actor };
        if (effect == StatChange && kind == CustomRewardDefinition.CustomChaosTalents && CustomRewardTriggers.IsCondition(trigger)) return new[] { Actor, Team, OtherTeammates };
        if (effect == StatChange) return kind == CustomRewardDefinition.CustomRelics && trigger == "While owned" ? new[] { Actor, Team, OpposingTeam, BothTeams }
            : new[] { "Assist", "Goalie assist", "Pass sent", "Pass received" }.Contains(trigger) ? new[] { Actor, Team, Opponent } : new[] { Actor, Team };
        if (effect == Knockout || effect == BreakStick || effect == Frog || effect == Injure || effect == InjureArm || effect == Heal || effect == ThrowStick || effect == DropHelmet || effect == Freeze || effect == Dance) {
            var targets = new List<string>();
            bool needsMobileActor = effect == Knockout || effect == Freeze || effect == Dance || effect == ThrowStick;
            if (CustomRewardTriggers.HasForwardActor(kind, trigger)
                && !(needsMobileActor && (trigger == "Knocked out" || trigger == "Knockout landing"))
                && !((effect == BreakStick || effect == ThrowStick) && trigger == "Stick broken")) targets.Add(Actor);
            targets.Add(Team); targets.Add(RandomAllies); targets.Add(Nearby); targets.Add(OpposingTeam); targets.Add(RandomOpponents); targets.Add(ClosestOpponent); targets.Add(OpponentCarrier);
            if (CustomRewardTriggers.HasOtherPlayer(trigger)) targets.Add(Opponent);
            return targets;
        }
        if (effect == RestoreStick) return !CustomRewardTriggers.HasForwardActor(kind, trigger) ? new[] { Team } : new[] { Actor, Team };
        var ownTeam = CustomRewardTriggers.HasForwardActor(kind, trigger) && trigger != "Knocked out" || effect != Boost ? new[] { Actor, Team, RandomAllies } : new[] { Team, RandomAllies };
        return CustomRewardTriggers.HasOtherPlayer(trigger) ? ownTeam.Concat(new[] { Opponent }) : ownTeam;
    }
    public static IEnumerable<string> Stats(string kind, string target, string trigger = "While owned")
        => target == Opponent ? CustomRewardDefinition.Stats.Where(s => !s.StartsWith("Goalie ", StringComparison.Ordinal)) : CustomRewardTriggers.IsShotCalculation(trigger) ? new[] { trigger.EndsWith("crit calculation", StringComparison.Ordinal) ? "Critical chance" : "Shot Power" } : CustomRewardDefinition.Stats.Where(s => !(kind == CustomRewardDefinition.CustomRelics && CustomRewardTriggers.IsCondition(trigger)
                && !CustomRewardTriggers.ForKind(CustomRewardDefinition.CustomGoalieTalents).Contains(trigger) && s.StartsWith("Goalie ", StringComparison.Ordinal)))
            .Where(s => target == Team || target == OpposingTeam || target == BothTeams
            || kind == CustomRewardDefinition.CustomRelics && !CustomRewardTriggers.IsGoalieEvent(trigger) && (CustomRewardTriggers.IsCondition(trigger) || trigger == "Match started" || trigger == "Match completed" || trigger == "Match won" || trigger == "Pass sent" || trigger == "Enraged" || trigger == "Ability used")
            || s.StartsWith("Goalie ", StringComparison.Ordinal) == !CustomRewardTriggers.HasForwardActor(kind, trigger))
            .Where(s => (s != "Size" || !trigger.StartsWith("While size ", StringComparison.Ordinal)) && (trigger != "While Shot Power is at least 50" || s != "Shot Power" && s != "Overall"));
    public static void Validate(CustomRewardDefinition d)
    {
        if ((d.Target == BothTeams || d.Target == OpposingTeam) && d.Effect == StatChange && d.AmountBasis != "Fixed amount") throw new ArgumentException("An opposing-team or both-team attribute rule uses a fixed amount.");
        if (d.Effect == BlueLineLaunch && d.Amount < 1) throw new ArgumentException("Choose a positive native launch force.");
        if (d.Effect == KnockoutDuration && (d.Amount < 100 || d.Amount > 400)) throw new ArgumentException("Choose knockout duration from 100% to 400%.");
        if (d.Effect == ParticipationBonus && d.Amount < 1) throw new ArgumentException("Choose a positive participation bonus.");
        if ((d.Effect == OpeningInjuries || d.Effect == CyclicInjuries || d.Effect == VoodooAttribute || IsTemperature(d.Effect)) && d.Amount < 1 || d.Effect == PuckInjuries && (d.Amount < 1 || d.Amount > 100)) throw new ArgumentException("Use a positive count or magnitude, or an injury chance from 1% to 100%.");
        if ((d.Effect == OpeningCriticals || d.Effect == ReboundCriticals) && d.Amount < 1 || d.Effect == HealedInjuryBonus && d.Amount == 0) throw new ArgumentException("Choose positive opening-shot or critical-chance values, or a nonzero injury recovery bonus.");
        if (IsTransferHelmet(d.Effect) && (d.Amount == 0 || !EffectStats(d.Kind, d.Target, d.Trigger, d.Effect).Contains(d.Stat))) throw new ArgumentException("Choose a nonzero attribute from this native helmet.");
        if (d.Effect == RandomMatchAttribute && d.Amount < 1 || d.Effect == CenterDefenderChecks && (d.Amount < 1 || d.Amount > 2)) throw new ArgumentException("Choose a positive match bonus, or native Bonecrusher level 1 or 2.");
        if (d.Effect == DonateBenchAttribute && !EffectStats(d.Kind, d.Target, d.Trigger, d.Effect).Contains(d.Stat)) throw new ArgumentException("Choose one native skater attribute to transfer.");
        if ((d.Effect == PickupAttribute || d.Effect == BossAttribute) && (d.Amount == 0 || !EffectStats(d.Kind, d.Target, d.Trigger, d.Effect).Contains(d.Stat))) throw new ArgumentException("Choose a nonzero permanent base attribute change.");
        if (d.Effect == BossSize && (d.Amount == 0 || d.Amount < -5 || d.Amount > 5)) throw new ArgumentException("Choose nonzero native size steps from -5 to +5.");
        if ((d.Effect == FartForce || d.Effect == FartRange) && (d.Amount < 1 || d.Amount > 400)) throw new ArgumentException("Choose fart interaction strength from 1% to 400%.");
        if (IsCategoryGrant(d.Effect) && (d.Amount < 1 || d.Amount > 2)) throw new ArgumentException("Choose native talent level 1 or 2.");
        if (d.Effect == AlternateOverall && d.Amount < 1 || d.Effect == RetainPowers && (d.Amount < 1 || d.Amount > 10)) throw new ArgumentException("Choose a positive alternating bonus, or from 1 to 10 retained powers.");
        if (d.Effect == RandomAutoShot && (d.Amount < 1 || d.Amount > 100 || d.Seconds > 60 || d.ShotDelayMilliseconds < 0 || d.ShotDelayMilliseconds > 10000)) throw new ArgumentException("Choose a shot chance from 1% to 100%, an interval up to 60 seconds and a delay from 0 to 10000 milliseconds.");
        if (d.Effect == PlayedMatchContract && (d.Amount < 1 || d.Amount > 999)) throw new ArgumentException("Choose from 1 to 999 played matches for a contract.");
        if (!SupportsKind(d.Effect, d.Kind)) throw new ArgumentException("This effect does not belong to the selected item's native kind. Choose an effect from its list.");
        if (!ForTrigger(d.Kind, d.Trigger).Contains(d.Effect)) throw new ArgumentException("This effect cannot happen at the selected trigger. Choose an effect from its list.");
        if (!Targets(d.Kind, d.Trigger, d.Effect).Contains(d.Target)) throw new ArgumentException("This trigger and effect do not support that recipient.");
        if (IsCharge(d.Effect) && (d.Seconds > 60 || d.Effect != ChargeCritical && d.Amount < 1)) throw new ArgumentException("Choose a charge limit from 0.05 to 60 seconds and a positive per-second bonus.");
        if ((d.Effect == RandomShotPower || d.Effect == RandomShotAccuracy) && (d.Amount < 0 || d.Amount > (d.Effect == RandomShotPower ? 148 : 99))) throw new ArgumentException("Choose a minimum from 0 to 148 for Shot Power or 0 to 99 for Accuracy.");
        if (d.Effect == TrainingMultiplier && (d.Amount < 100 || d.Amount > 400)) throw new ArgumentException("Choose training reward strength from 100% to 400%.");
        if (d.Effect == ExtraCoachPicks && (d.Amount < 1 || d.Amount > 10)) throw new ArgumentException("Choose from 1 to 10 additional Coach picks.");
        if (d.Effect == ManualGoalDenial && (d.Amount < 1 || d.Amount > 10)) throw new ArgumentException("Choose from 1 to 10 goal-reversal charges.");
        if (d.Effect == LoadedPuckBag && (d.NormalPucks < 0 || d.NormalPucks > 999 || d.DoublePucks < 0 || d.DoublePucks > 999 || d.TriplePucks < 0 || d.TriplePucks > 999 || (long)d.NormalPucks + d.DoublePucks + d.TriplePucks == 0)) throw new ArgumentException("Choose 0 to 999 pucks of each value and at least one puck in the bag.");
        if (d.Effect == BoardBounce && (d.Amount < 1 || d.Amount > 400)) throw new ArgumentException("Choose board rebound strength from 1% to 400%.");
        if (d.Effect == EnragedDuration && (d.Amount < 100 || d.Amount > 400)) throw new ArgumentException("Choose an enraged duration multiplier from 100% to 400%.");
        if (d.Effect == HelmetMultiplier && (d.Amount < 1 || d.Amount > 10)) throw new ArgumentException("Choose a helmet multiplier from 1 to 10.");
        if (d.Effect == LockAttribute && (d.Amount < 0 || d.Amount > 100 || !EffectStats(d.Kind, d.Target, d.Trigger, d.Effect).Contains(d.Stat))) throw new ArgumentException("Choose Shot Power, Speed or Checking and a locked value from 0 to 100.");
        if (d.Effect == TurningDelay && d.Amount < 1) throw new ArgumentException("Choose additional turning time from 1 to 999 milliseconds.");
        if (d.Effect == RefereeSize && (d.Amount == 0 || d.Amount < -5 || d.Amount > 5)) throw new ArgumentException("Referee size uses nonzero native size steps from -5 to +5.");
        if ((d.Effect == StatusMultiplier || d.Effect == CooldownMultiplier) && d.Amount < 100) throw new ArgumentException("These native extension effects use multipliers from 100% to 400%.");
        if (d.Effect == DenyGoal && (d.Amount < 1 || d.Amount > 100)) throw new ArgumentException("Choose a denial chance from 1% to 100%.");
        if (d.Effect == BonusMultiplier && !EffectStats(d.Kind, d.Target, d.Trigger, d.Effect).Contains(d.Stat)) throw new ArgumentException("This attribute multiplier does not belong to the selected native item kind.");
        if ((ScalingConditionEffects.Contains(d.Effect) || d.Effect == SpikedInjury || d.Effect == InjuryMultiplier || d.Effect == GoalieHeadshot || d.Effect == BenchMultiplier) && (d.Amount < 1 || d.Amount > 400)) throw new ArgumentException("Choose a multiplier from 1% to 400%.");
        if (d.Effect == InjuryHealing && (d.Amount == 0 || d.Amount < -10 || d.Amount > 10)) throw new ArgumentException("Choose a nonzero recovery adjustment from -10 to 10 games.");
        if (IsUpgrade(d.Effect) && (d.Amount < 1 || d.Amount > 10)) throw new ArgumentException("Choose from 1 to 10 upgrades.");
        if (d.Effect == StatChange && (d.Amount == 0 || !Stats(d.Kind, d.Target, d.Trigger).Contains(d.Stat))) throw new ArgumentException("Choose a nonzero stat change for the selected recipient.");
        if (d.Effect == StatChange && d.Stat == "Size" && (d.Amount < -5 || d.Amount > 5)) throw new ArgumentException("Size changes use native size steps from -5 to +5; the native minimum and maximum still apply.");
        if ((d.Effect == TalentRewards || d.Effect == RelicRewards || d.Effect == Rerolls) && (d.Amount < 1 || d.Amount > 10)) throw new ArgumentException("Choose from 1 to 10 extra rewards or rerolls.");
        if (d.Effect == ClockTime && d.Amount == 0) throw new ArgumentException("Enter a nonzero clock adjustment.");
        if ((d.Target == Nearby || HasRadius(d.Effect)) && (d.Radius < 1 || d.Radius > 30)) throw new ArgumentException("Set a radius from 1 to 30 game units.");
        if (d.Effect == AttractPuck && (d.Amount < 1 || d.Amount > 400)) throw new ArgumentException("Attraction strength must be from 1% to 400%.");
        if (IsShockwave(d.Effect) && (d.Amount < 1 || d.Amount > 400)) throw new ArgumentException("Shockwave strength must be from 1% to 400%.");
        if (IsPuck(d.Effect) && HasAmount(d.Effect) && (d.Amount < 1 || d.Amount > (d.Effect == PuckLift ? 50 : 400))) throw new ArgumentException(d.Effect == PuckLift ? "Upward puck velocity must be from 1 to 50 game units per second." : "Puck effect strength must be from 1% to 400%.");
        if ((d.Effect == Boost || d.Effect == ShotRecoil) && (d.Amount < 1 || d.Amount > 100 || d.Seconds > 5)) throw new ArgumentException("Use boost force from 1 to 100 and duration from 0.05 to 5 seconds.");
        if (d.Effect == GoalieFetchSpeed && (d.Amount < 1 || d.Amount > 400)) throw new ArgumentException("Retrieval-speed strength must be from 1% to 400%.");
        if (d.Effect == ExtraSticks && (d.Amount < 1 || d.Amount > 10)) throw new ArgumentException("Choose from 1 to 10 additional reserve sticks.");
        if ((d.Effect == NetWidth || d.Effect == NetHeight) && (d.Amount < 25 || d.Amount > 300)) throw new ArgumentException("Goal dimensions must be from 25% to 300% of the original dimension.");
        if (d.Effect != StatChange && d.Duration != (IsUpgrade(d.Effect) ? "Rest of run" : "Seconds")) throw new ArgumentException("Use the duration supported by this effect: on-ice statuses use seconds and campaign upgrades remain in the run.");
    }
    public static string Summary(CustomRewardDefinition d) => d.Effect == EggExplosion ? "Explosion within " + d.Radius + " game units, affecting both teams and its source"
        : d.Effect == SonicKnockouts ? "Sonic knockout wave around " + (d.Target == Puck ? "the intercepted puck" : "the triggering skater") + ", radius " + d.Radius + ", affecting both teams except its source"
        : d.Effect == StatChange ? (d.Amount >= 0 ? "+" : "") + d.Amount + " " + d.Stat
        : d.Effect == BatKnockout ? d.Effect + " within " + d.Radius + " game units"
        : d.Effect == RandomAutoShot ? d.Amount + "% automatic-shot chance every " + d.Seconds + "s, after a " + d.ShotDelayMilliseconds + "ms warning"
        : IsCharge(d.Effect) ? (d.Effect == ChargeCritical ? "Guaranteed critical after " + d.Seconds + "s of charge" : d.Effect + ": +" + d.Amount + " per held second, up to " + d.Seconds + "s")
        : d.Effect == RandomShotPower || d.Effect == RandomShotAccuracy ? d.Effect + " (minimum " + d.Amount + ")"
        : d.Effect == LoadedPuckBag ? $"Shuffled puck bag: {d.NormalPucks} one-goal, {d.DoublePucks} two-goal, {d.TriplePucks} three-goal"
        : d.Effect == BoardBounce ? d.Effect + ": " + d.Amount + "% strength"
        : d.Effect == EnragedDuration ? "Enraged duration: " + d.Amount + "%"
        : d.Effect == HelmetMultiplier ? "Helmet effects × " + d.Amount
        : d.Effect == LockAttribute ? d.Stat + " locked at " + d.Amount
        : d.Effect == TurningDelay ? d.Effect + ": +" + d.Amount + " ms"
        : IsImpactShot(d.Effect) ? d.Effect + (d.Effect == ImpactExplosion ? " (radius " + d.Radius + ")" : "")
        : d.Effect == BonusMultiplier ? d.Stat + " bonuses × " + (d.Amount / 100f)
        : d.Effect == SpikedInjury || d.Effect == ClockSpeed || d.Effect == CritMultiplier || d.Effect == StatusMultiplier || d.Effect == CooldownMultiplier || d.Effect == InjuryMultiplier || d.Effect == GoalieHeadshot ? d.Effect + ": " + d.Amount + "%"
        : d.Effect == ClockTime ? (d.Amount >= 0 ? "+" : "") + d.Amount + "s on the clock"
        : HasAmount(d.Effect) && IsQueryEffect(d.Effect) ? "+" + d.Amount + " " + d.Effect.ToLowerInvariant()
        : d.Effect == PuckGoalieGhost ? PuckGoalieGhost : d.Effect == FlatPuck ? FlatPuck : d.Effect == PuckGhost ? PuckGhost : d.Effect == Burst ? Burst + " (radius " + d.Radius + ")"
        : IsShockwave(d.Effect) ? d.Effect + ": " + d.Amount + "% strength, radius " + d.Radius
        : d.Effect == LoosePuckAttraction ? d.Effect + ": range " + d.Radius
        : d.Effect == AttractPuck ? d.Effect + ": " + d.Amount + "% strength, range " + d.Radius
        : d.Effect == Boost || d.Effect == ShotRecoil ? d.Effect + ": force " + d.Amount + " for " + d.Seconds + "s"
        : IsPuck(d.Effect) ? d.Effect + ": " + d.Amount + (d.Effect == PuckLift ? " units/s" : "%")
        : d.Effect + (Find(d.Effect)?.Timed == true || d.Effect == Knockout ? " for " + d.Seconds + "s" : "");
    public static string Describe(CustomRewardDefinition d)
    {
        if (CustomRewardTriggers.IsCampaignEvent(d.Trigger)) return (d.Chance == 100 ? "" : d.Chance + "% chance: ") + "After every " + d.Threshold + " training choices, your team gains " + Summary(d) + " for the rest of the run while this relic is owned. Up to " + d.ActivationsPerMatch + " activations per run, at most " + d.MaxStacks + " stacks. Counters and earned bonuses persist through Continue. Previews and rerolls do not count.";
        if (CustomRewardTriggers.IsQuery(d.Trigger)) return QueryDescription(d);
        if (d.AmountBasis == "If benched for the entire previous match") return (d.Target == Team ? "Your skaters gain " : "This skater gains ") + Summary(d) + " " + CustomRewardTriggers.ConditionText(d.Trigger) + " if this owner never played in their last completed match while owning the item. No bonus before the first completed match. Playing at any point in that match clears eligibility; abandoned matches do not replace it.";
        if (CustomRewardDefinition.IsPreviousMatchBasis(d.AmountBasis)) return (d.Target == Team ? "Your skaters gain " : "This skater gains ") + Summary(d) + " " + d.AmountBasis.ToLowerInvariant()
            + " " + CustomRewardTriggers.ConditionText(d.Trigger) + ". Uses this talent owner's last completed match while owning the item; zero before the first. Replaces the count after each completed match. Abandoned matches do not replace it.";
        if (d.AmountBasis == "Per liquidated talent") return "This skater gains " + Summary(d) + " per talent removed by an owned Liquidation carrier. Uses that carrier's saved native count; no bonus without one. " + CustomRewardTriggers.ConditionText(d.Trigger) + ". Does not remove any talents or permanently change base attributes.";
        if (d.AmountBasis != "Fixed amount") return (d.Target == Team ? "Your skaters gain " : "This skater gains ") + Summary(d) + " " + d.AmountBasis.ToLowerInvariant() + " " + CustomRewardTriggers.ConditionText(d.Trigger) + ". Counts update during play. "
            + (d.AmountBasis == "Per other owned talent" ? "This reward is excluded from the talent count. " : d.AmountBasis == "Per injured playing teammate" ? "The owner is excluded from the teammate count. " : "") + "These are current bonuses, not permanent gains.";
        if (ScalingConditionEffects.Contains(d.Effect)) return Summary(d) + " " + CustomRewardTriggers.ConditionText(d.Trigger) + ". " + Help(d.Effect);
        if (IsUpgrade(d.Effect)) return "When " + (d.IsRelic ? "your team " : "this skater ") + CustomRewardTriggers.Action(d.Trigger, d.Threshold) + ", " + d.Effect.ToLowerInvariant() + " (up to " + d.Amount + " different eligible powers). " + Help(d.Effect);
        if (d.Effect == CatchSaves) return "Your goalie catches eligible saves " + CustomRewardTriggers.ConditionText(d.Trigger) + ". " + Help(CatchSaves);
        if (d.Effect == GoalieFetchSpeed) return "This goalie gains " + (6 * d.Amount / 100f) + " retrieval speed " + CustomRewardTriggers.ConditionText(d.Trigger) + ". " + Help(GoalieFetchSpeed);
        if (d.Effect == GoalieThrow) return "This goalie uses native stick throws " + CustomRewardTriggers.ConditionText(d.Trigger) + ". " + Help(GoalieThrow);
        if (ForwardConditionEffects.Contains(d.Effect)) return (d.IsRelic || d.Target == Team ? "Your skaters: " : "This skater: ")
            + (d.Effect == LoosePuckAttraction || d.Effect == SpikedInjury || d.Effect == RandomAutoShot || IsCharge(d.Effect) || d.Effect == BatKnockout || d.Effect == RandomShotPower || d.Effect == RandomShotAccuracy || d.Effect == LockAttribute || d.Effect == TurningDelay ? Summary(d) : d.Effect.ToLowerInvariant()) + (d.Effect == ExtraSticks ? " +" + d.Amount : "") + " " + CustomRewardTriggers.ConditionText(d.Trigger) + ". " + Help(d.Effect);
        string owner = d.IsGoalie ? "this goalie" : d.IsRelic ? "a teammate" : "this skater";
        string recipient = d.Target == Team ? (d.Stat.StartsWith("Goalie ") && d.Effect == StatChange ? "your goalie" : d.Effect == Enrage ? "your team, including the goalie" : "your skaters")
            : IsRandomTarget(d.Target) ? "up to " + d.TargetCount + " randomly chosen " + (d.Target == RandomAllies ? "teammates" : "opponents")
            : d.Target == OtherTeammates ? "the owning skater's other playing teammates"
            : d.Target == BothTeams ? (d.Stat.StartsWith("Goalie ") ? "both goalies" : "skaters on both teams")
            : d.Target == OpposingTeam ? (d.Stat.StartsWith("Goalie ") && d.Effect == StatChange ? "the opposing goalie" : "all opposing skaters") : d.Target == ClosestOpponent ? "the closest opposing skater" : d.Target == OpponentCarrier ? "the opposing skater holding the puck"
            : d.Target == Nearby ? "opposing skaters within " + d.Radius + " game units" : d.Target == Opponent ? "the other player involved" : owner;
        string effectName = d.Effect == Boost || d.Effect == ShotRecoil ? d.Effect + " (force " + d.Amount + ")" : IsShockwave(d.Effect) || d.Effect == EggExplosion || d.Effect == SonicKnockouts || d.Effect == AttractPuck || d.Effect == BoardBounce ? Summary(d) : d.Effect;
        string effect = d.Effect == StatChange ? recipient + " gain " + Summary(d) : d.Effect == ClockTime ? Summary(d)
            : IsPuck(d.Effect) ? Summary(d) + " on the current puck" : effectName + ": " + recipient;
        string condition = d.IsRelic && CustomRewardTriggers.IsOpponentAction(d.Trigger) ? "when " + (d.Trigger == "Opponent goal" ? "an opponent scores" : CustomRewardTriggers.Action(d.Trigger, d.Threshold)) : CustomRewardTriggers.IsCondition(d.Trigger) ? CustomRewardTriggers.ConditionText(d.Trigger) : "when " + owner + " " + CustomRewardTriggers.Action(d.Trigger, d.Threshold);
        if (CustomRewardTriggers.IsCondition(d.Trigger)) return effect + " " + condition + ". The item must be owned.";
        string duration = d.Effect == StatChange ? (d.Duration == "Seconds" ? "for " + d.Seconds + " seconds" : "for the " + d.Duration.ToLowerInvariant().Replace("of ", "of the "))
            : Find(d.Effect)?.Timed == true || d.Effect == Knockout ? "for " + d.Seconds + " seconds" : "immediately";
        return (d.Chance == 100 ? "" : d.Chance + "% chance: ") + char.ToUpperInvariant(condition[0]) + condition.Substring(1) + ", " + effect + " " + duration + ".\n"
            + "Up to " + d.ActivationsPerMatch + (d.IsRelic && (CustomRewardTriggers.IsOpponentAction(d.Trigger) || d.Trigger == "Referee knocked out" || d.Trigger == "Goal reversed") ? " activations per team per match. " : " activations per triggering player per match. ")
            + (d.Effect == StatChange ? "Maximum " + d.MaxStacks + " active stacks per recipient." : Find(d.Effect)?.Timed == true ? "Repeated activations refresh the duration." : "The effect happens at this event.")
            + (d.ResetOn.Length == 0 ? "" : " This item's active bonus resets when " + owner + " " + CustomRewardTriggers.Action(d.ResetOn, 1) + ". Activation limits are unchanged.");
    }
}
