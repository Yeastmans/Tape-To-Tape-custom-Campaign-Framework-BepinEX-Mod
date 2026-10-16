using System.Collections.Generic;

namespace CampaignCore;

public static partial class CustomRewardEffects
{
    public const string ScorelessExpiry = "Expire after a scoreless match";
    public const string UpgradeOwnTalents = "Upgrade all owned talents on acquisition";
    public const string GrantSpeedTalent = "Grant Speed-category talents";
    public const string GrantCheckingTalent = "Grant Checking-category talents";
    public const string GrantDefenseTalent = "Grant defensive-category talents";
    public const string GrantShootingTalent = "Grant shooting-category talents";
    public const string GrantSlapshotTalent = "Grant slapshot talents at run start";
    public const string ShareTalent = "Loan a teammate talent to the team";
    public const string FrogHelmets = "Frog opposing Golden Helmet wearers";
    public const string OctopusAttacks = "Tentacle attacks";
    public const string AlternateOverall = "Alternate Overall each match";
    public const string RetainPowers = "Retain bench powers between runs";
    public const string PuckGoalieGhost = "Puck passes through the goalie";
    public const string PostGoal = "Count first opposing-post hit as a goal";
    public const string FartForce = "Fart force multiplier";
    public const string FartRange = "Fart range multiplier";
    public const string FartKnockouts = "Farts cause knockouts";
    public const string PickupAttribute = "Permanent attribute on acquisition";
    public const string BossAttribute = "Permanent attribute after boss win";
    public const string BossSize = "Permanent size after boss win";
    public const string FogScoreboard = "Glitch scoreboard during controlled possession";
    public const string CultistFrog = "Cultist frog incantation";
    public const string CultistFreeze = "Cultist freeze incantation";
    public const string CultistRory = "Cultist transformation: Rory";
    public const string CultistDord = "Cultist transformation: Dord";
    public const string CultistJelly = "Cultist transformation: Jelly";
    public const string CultistCornelius = "Cultist transformation: Cornelius";
    public const string CultistPam = "Cultist transformation: Pam";
    public const string DefenseTeleport = "Teleport back on defensive possession";
    public static bool IsCultist(string effect) => CultistAsset(effect).Length > 0;
    public static string CultistAsset(string effect) => effect switch {
        CultistFrog => "54950cc8-5abb-41a5-9eed-148d50dfd43e",
        CultistFreeze => "b74b1d64-b508-4ab7-b976-e84243845098",
        CultistRory => "a1b2c3d4e5f6708192a3b4c5d6e7f802",
        CultistDord => "da0bcd09-1fe6-4c17-992a-eab8380185d2",
        CultistJelly => "b4437abd-0765-48da-aa8b-f45b32407924",
        CultistCornelius => "3556c58e-2987-40c6-89e2-ccce86a907bc",
        CultistPam => "a313c2d7-70e4-4249-97f8-b1d4cf1e17cd",
        _ => ""
    };
    public const string AngusZero = "Lock Angus at zero Overall";
    public const string OpposingBonecrusher = "Give opposing skaters Bonecrusher";
    public const string OpposingBriefcase = "Give the opposing team a goal reversal";
    public const string FreezeOwnRelics = "Freeze two eligible own relics";
    public const string FrogOwnPair = "Frog two own skaters";
    public const string MinimumTeamSize = "Set team skaters to minimum size";
    public const string BlueLineLaunch = "Launch opponents crossing a blue line";
    public const string DefensiveBlueLineKnockout = "Knock out an entering opposing puck carrier";
    public const string OffensiveBlueLineKnockout = "Knock out an entering own puck carrier";
    public static bool IsBlueLineKnockout(string effect) => effect == DefensiveBlueLineKnockout || effect == OffensiveBlueLineKnockout;
    public const string KnockoutDuration = "Opposing knockout duration";
    public const string ParticipationBonus = "Overall bonus for sitting out a win";
    public const string BreakawayShotPenalty = "Halve opposing breakaway shot strength";
    public const string BreakawayShotFall = "Trip an opponent after a breakaway shot";
    public const string RefereeBlockTalent = "Enable referee shot blocking";
    public const string OpeningInjuries = "Injuries on opening team knockouts";
    public const string CyclicInjuries = "Injuries on each team knockout cycle";
    public const string PuckInjuries = "Injury chance on puck knockouts";
    public const string VoodooAttribute = "Overall penalty to a random opponent";
    public const string HotAttribute = "Overall bonus to the hot skater";
    public const string ColdAttribute = "Overall penalty to the cold skater";
    public static bool IsTemperature(string effect) => effect == HotAttribute || effect == ColdAttribute;
    public const string OpeningCriticals = "Opening team shots are critical";
    public const string ReboundCriticals = "Recent team-shot critical chance";
    public const string HealedInjuryBonus = "Overall bonus when an injury heals";
    public const string CelebrationInjuries = "Injure a knocked-out celebrating scorer";
    public const string RecoveringSkater = "Skater whose injury healed";
    public const string HeavyHelmetAttribute = "Heavy Helmet attribute";
    public const string IronHelmetAttribute = "Iron Helmet attribute";
    public const string PropellerHelmetAttribute = "Propeller Helmet attribute";
    public const string LancelovHelmetAttribute = "Lancelov helmet attribute";
    public static bool IsTransferHelmet(string effect) => effect == HeavyHelmetAttribute || effect == IronHelmetAttribute || effect == PropellerHelmetAttribute || effect == LancelovHelmetAttribute;
    public const string BenchTalentSlot = "Grant the native talent bench reward";
    public const string BenchCapacity = "Add a bench slot on acquisition";
    public const string RandomMatchAttribute = "Gain a random attribute after each match";
    public const string CenterDefenderChecks = "Grant defence Bonecrusher while playing centre";
    public const string AiKarlson = "AI: Karlson puck carrying";
    public const string AiPrince = "AI: Prince puck carrying";
    public const string AiKnight = "AI: support a Prince carrier";
    public const string PreventPredictivePoke = "Prevent predictive pokechecks against carrier";
    public const string LiquidateTalents = "Remove eligible talents on acquisition";
    public const string DonateBenchAttribute = "Transfer a benchwarmer attribute";
    public const string DonateBenchTalents = "Transfer benchwarmer talents";
    public const string UpgradeAngusTalents = "Upgrade Angus talents on acquisition";
    public const string UpgradeAngusAbility = "Upgrade Angus ability on acquisition";
    private static Entry[] LifecycleEntries => new Entry[] {
        new(AngusZero, "CanadaCurseAngusZeroOvr separate native component."),
        new(OpposingBonecrusher, "CanadaCurseOpponentBonecrusher separate native component."),
        new(OpposingBriefcase, "CanadaCurseOpponentBriefcase separate native component."),
        new(FreezeOwnRelics, "CanadaCurseFreezeTwoRelics separate native component."),
        new(FrogOwnPair, "CanadaCurseFrogTwo separate native component."),
        new(MinimumTeamSize, "CanadaCurseXsTeam separate native component."),

        new(BlueLineLaunch, "BlueLineLaunchRelic native jump-knockout movement."),
        new(DefensiveBlueLineKnockout, "BlueLineKnockoutRelic defensive entry component.", true),
        new(OffensiveBlueLineKnockout, "BlueLineKnockoutRelic offensive entry component.", true),
        new(KnockoutDuration, "SnoozeAlarm native knockout time modifier."),
        new(ParticipationBonus, "ParticipationTrophyRelic native participation tracking and saved bonus."),
        new(BreakawayShotPenalty, "YipsRelic separate breakaway shot penalty."),
        new(BreakawayShotFall, "YipsRelic separate upgraded shot fall."),
        new(RefereeBlockTalent, "RefBlockShotTalent separate referee behavior."),
        new(CultistFrog, "Cultist separate native incantation."),
        new(CultistFreeze, "Cultist separate native incantation."),
        new(CultistRory, "Cultist separate native incantation."),
        new(CultistDord, "Cultist separate native incantation."),
        new(CultistJelly, "Cultist separate native incantation."),
        new(CultistCornelius, "Cultist separate native incantation."),
        new(CultistPam, "Cultist separate native incantation."),
        new(DefenseTeleport, "DefenseTeleportPowerup native possession transition."),
        new(OpeningInjuries, "NutJob native team counter and injury eligibility."),
        new(CyclicInjuries, "EggTimer native team counter and injury eligibility."),
        new(PuckInjuries, "HardBoiledEgg native puck knockout injury roll."),
        new(VoodooAttribute, "VoodooDoll native random playing opponent."),
        new(HotAttribute, "HotColdRelic separate hot recipient."),
        new(ColdAttribute, "HotColdRelic separate cold recipient."),
        new(OpeningCriticals, "DryNoodle separate opening shot counter."),
        new(ReboundCriticals, "FastRebound recent-team-shot crit modifier."),
        new(HealedInjuryBonus, "Stethoscope saved per-skater recovery bonus."),
        new(CelebrationInjuries, "SorestLoser native celebrating scorer injury handling."),
        new(HeavyHelmetAttribute, "HeavyHelmet separate equipment attribute."),
        new(IronHelmetAttribute, "IronHelmet separate equipment attribute."),
        new(PropellerHelmetAttribute, "PropellerHelmet separate equipment attribute."),
        new(LancelovHelmetAttribute, "LancelovHelmet separate equipment attribute."),
        new(BenchCapacity, "RandomTalentBenchBonus separate capacity component."),
        new(BenchTalentSlot, "RandomTalentBenchBonus native bench capacity and reward slot."),
        new(RandomMatchAttribute, "StatStuffer native saved attribute rolls."),
        new(CenterDefenderChecks, "CenterBonecrusherTalent native defender loan."),
        new(AiKarlson, "KarlsonBehavior separate native puck-carrying decisions."),
        new(AiPrince, "PrinceAI separate native carrier behavior."),
        new(AiKnight, "KnightAIPowerup native teammate role."),
        new(PreventPredictivePoke, "PrinceAI separate pokecheck-prediction exclusion."),
        new(LiquidateTalents, "Liquidation separate saved removal component."),
        new(DonateBenchAttribute, "BenchwarmerStrip separate attribute transfer."),
        new(DonateBenchTalents, "BenchwarmerStrip separate talent transfer."),
        new(UpgradeAngusTalents, "AngusAscension separate talent upgrades."),
        new(UpgradeAngusAbility, "AngusAscension separate ability upgrade."),
        new(FogScoreboard, "AlzheimerController scoreboard effect."),
        new(BossSize, "Evolution separate size component."),
        new(BossAttribute, "Evolution separate attribute component."),
        new(PickupAttribute, "ForwardStatBonusOnPickup."),
        new(FartKnockouts, "FartKnockout separate knockout interaction."),
        new(FartRange, "BeanDiet separate range component."),
        new(FartForce, "BeanDiet separate force component."),
        new(PostGoal, "FalseGoalOnPost native GoalPost transaction."),
        new(PuckGoalieGhost, "XRayShot separate goalie-collision component; XRayGoalie."),
        new(ScorelessExpiry, "MonkeysPaw separate scoreless-match expiry; attribute bonus removed."),
        new(UpgradeOwnTalents, "MoldPowerup separate pickup upgrades; attribute penalty removed."),
        new(GrantSpeedTalent, "BlackCoffee; AddPowerups."),
        new(GrantCheckingTalent, "CrashTestDummy; AddPowerups."),
        new(GrantDefenseTalent, "InkBottle; AddPowerups."),
        new(GrantShootingTalent, "CompanionPuck; AddPowerups."),
        new(GrantSlapshotTalent, "SlapshotPlushie."),
        new(ShareTalent, "HotCake native temporary talent sharing and removal."),
        new(FrogHelmets, "FrogGoldenHelmetRelic."),
        new(OctopusAttacks, "OctopusAttackRelic telegraphs and native burning launches."),
        new(AlternateOverall, "SplitPersonality saved alternating attribute state."),
        new(RetainPowers, "PowerUpRetentionTalent; BenchSnapshotService."),
    };
    public static bool IsCategoryGrant(string effect) => effect == GrantSpeedTalent || effect == GrantCheckingTalent || effect == GrantDefenseTalent || effect == GrantShootingTalent;
    public static bool IsLifecycleEffect(string effect) => effect == BlueLineLaunch || IsBlueLineKnockout(effect) || effect == AngusZero || effect == OpposingBonecrusher || effect == OpposingBriefcase || effect == FreezeOwnRelics || effect == FrogOwnPair || effect == MinimumTeamSize ||  effect == KnockoutDuration || effect == ParticipationBonus || effect == BreakawayShotPenalty || effect == BreakawayShotFall || effect == DefenseTeleport || effect == OpeningInjuries || effect == CyclicInjuries || effect == PuckInjuries || effect == VoodooAttribute || effect == OpeningCriticals || effect == ReboundCriticals || effect == HealedInjuryBonus || IsTransferHelmet(effect) || effect == BenchCapacity || effect == RandomMatchAttribute || effect == CenterDefenderChecks || effect == LiquidateTalents || effect == DonateBenchAttribute || effect == DonateBenchTalents || effect == UpgradeAngusTalents || effect == UpgradeAngusAbility || effect == PickupAttribute || effect == FogScoreboard || effect == PostGoal || effect == ScorelessExpiry || effect == UpgradeOwnTalents || effect == GrantSpeedTalent || effect == GrantCheckingTalent || effect == GrantDefenseTalent || effect == GrantShootingTalent || effect == GrantSlapshotTalent || effect == ShareTalent || effect == FrogHelmets || effect == OctopusAttacks || effect == AlternateOverall || effect == RetainPowers;
    private static string LifecycleHelp(string effect) => effect switch {
        _ when IsCultist(effect) => "Add this individual spell to the owner's native Cultist incantation cycle. Native AI control, possession, eligible targets, difficulty timing, casting interruption and spell cleanup apply. Frog and freeze can share a cycle; the latest acquired custom transformation selects its form. This does not add the other spells in a native Cultist bundle.",
        DefenseTeleport => "Use the native defensive teleport component when puck possession changes to its defensive state. Native positioning, movement and casting checks remain active. This is the possession-triggered talent, not an activated ability.",
        AngusZero => "Use the native Angus identity and zero-Overall restriction from Angus Goes Cold. This affects Angus only and does not subtract ratings from other skaters.",
        OpposingBonecrusher => "Use Bonecrusher Nation's native opponent grant and eligibility. Native roster and duplicate handling remain active; no attribute bonus is added.",
        OpposingBriefcase => "Use Canadian Insurance's native opposing Briefcase grant and duplicate handling. The opponent receives its native goal-reversal behavior; this does not reverse a goal immediately.",
        FreezeOwnRelics => "Use Deep Freeze's native two-relic selection and Frozen Relic wrappers. Native protected-relic and squad exclusions remain in force. This changes only this match's active relics.",
        FrogOwnPair => "Use Frog Duty's native selection and maintained frog status for two skaters on this team, including native roster selection and cleanup.",
        MinimumTeamSize => "Use Tiny Team's native reversible size adjustment to the minimum skater size. This changes no base attribute and does not affect the rink.",
        BlueLineLaunch => "Use the native jump-knockout movement when an opposing skater crosses either blue line, with the selected launch force. Native movement eligibility and recovery apply; possession is not required. This does not apply a smooth skating boost.",
        DefensiveBlueLineKnockout => "Use the native blue-line knockout controller when an opposing puck carrier crosses your defensive blue line. Native rink direction, puck possession, knockout, visuals and charge counter remain active. Choose the knockout duration.",
        OffensiveBlueLineKnockout => "Use the native offensive-entry variant: your own puck carrier is knocked out when crossing the opposing blue line. Native rink direction, possession, visuals and charge counter remain active. Choose the knockout duration.",
        KnockoutDuration => "Scale opponents' native knockout time using Broken Alarm. Native recovery overrides, movement states and injury animations remain active. 150% means one and a half times the normal duration.",
        ParticipationBonus => "After a win, give the chosen Overall bonus to each skater who sat out the entire match. Native participation tracking includes line changes. Totals are saved per player by this relic and disappear if it is removed; base attributes do not change.",
        BreakawayShotPenalty => "Use Jigglypuck's native half-strength shot calculation for an opposing breakaway shooter. Native breakaway tracking and shot accounting apply. The upgraded fall is a separate component.",
        BreakawayShotFall => "Use Jigglypuck's upgraded native stumble after an opponent shoots on a breakaway, including its native delay and movement checks. This component does not halve the shot strength.",
        RefereeBlockTalent => "Enable the native referee shot-blocking support supplied by Referee Angus's talent. The team and referee logic decide which referee can react. No size increase is bundled; this is separate from changing this skater's own AI role.",
        OpeningInjuries => "Use Nut Job's shared team counter for the first selected number of qualifying knockouts each match. Native source, target and injury eligibility still apply; this does not injure a player without a knockout.",
        CyclicInjuries => "Use Deca-Egg's shared team counter to force an injury attempt on each selected number of qualifying knockouts. Native knockout eligibility and injury prevention remain active; the counter resets each match.",
        PuckInjuries => "Roll the chosen injury chance through Hard Boiled's native puck-caused knockout path. Bodychecks do not use this roll. Native campaign injury eligibility and prevention still apply.",
        VoodooAttribute => "At match setup, use Missing Toe's native selection of one playing opponent and its removable Overall penalty. The selected player keeps the penalty for this match; base attributes do not change.",
        HotAttribute => "At match setup, pick two different playing skaters using Hot & Cold's selection scope. Give the first the chosen Overall bonus. Custom hot and cold components on this team share this pair, so they never choose the same skater. The cold penalty is separate; no permanent attributes change.",
        ColdAttribute => "Give the second skater in this team's shared Hot & Cold pair the chosen Overall penalty for the match. The pair consists of different skaters who were playing at setup and is retained across line changes. The hot bonus is separate; removing this relic removes its penalty.",
        OpeningCriticals => "Make the first chosen number of team shots critical through Dry Noodle's native opening-shot modifier and counter. The counter resets each match and is consumed at the actual critical-shot decision, not during an odds preview. Native critical strength remains in control.",
        ReboundCriticals => "Add critical chance when any current teammate shot within the chosen recent-time window, matching Crease Grease's native condition. This is checked before the current shot launches. A physical goalie rebound is not required by the native condition.",
        HealedInjuryBonus => "When a roster skater's injury heals, add the selected Overall amount to that skater's bonus stored by this relic. Native team membership, saved per-skater totals and recovery notifications apply. Removing the relic removes its bonuses, without changing base attributes.",
        CelebrationInjuries => "Use No-Fun Zone's native vulnerability and injury path when a celebrating opposing goal scorer is knocked out. Native scorer, celebration, knockout and campaign-injury checks remain active. This does not injure every celebrating teammate or cause a knockout by itself.",
        _ when IsTransferHelmet(effect) => "Equip the selected native helmet with only the chosen attribute change. The bonus belongs to that equipment: native knockoff, pickup, transfer, appearance and helmet multipliers still apply to its current wearer. The original bundled attribute changes are removed. This does not add a permanent base attribute change.",
        BenchCapacity => "On acquisition, add one bench slot using Comfy Cushion's native capacity increment. Continue does not add it again. The talent reward for the designated slot is a separate effect.",
        BenchTalentSlot => "Enable Comfy Cushion's native talent reward for its designated bench slot. The team must have that bench slot available. Native participation, eligible talent selection and notifications remain active. This does not increase bench capacity or grant a talent immediately.",
        RandomMatchAttribute => "After each match ends, use Stat Stuffer's native random choice among the four skater attributes. Add the selected amount to this talent's saved attribute totals, with native Gauntlet scaling and notifications. These bonuses remain attached to the talent, rather than changing the player's base attributes. Requires campaign progress.",
        CenterDefenderChecks => "While this owner plays centre, loan native Bonecrusher to the team's LD and RD using its native eligibility, level and roster refresh rules. Changing position, benching or removing this talent clears only its loans. This grants no attributes to the owner.",
        AiKarlson => "Use Karlson's native passing, shooting and steering decisions in the puck-carrying breakout state. Applies only during AI control and normal native play. The latest acquired applicable custom AI rule wins.",
        AiPrince => "Use Prince's native puck-carrying AI state and identify this carrier for teammate Knight support. Native game-mode and possession gates remain active. Predictive pokecheck protection is a separate effect. The latest acquired applicable custom AI rule wins.",
        AiKnight => "Use native Knight support while a teammate with Prince behavior carries the puck, on Veteran difficulty or higher. Without that carrier, normal AI continues. The latest acquired applicable custom AI rule wins.",
        PreventPredictivePoke => "Exclude this puck carrier from native predictive pokecheck decisions, as Prince does. This changes AI prediction only; it does not grant pokecheck immunity or change positioning.",
        LiquidateTalents => "On acquisition, remove the owner's eligible talents using Liquidation's native selection and saved one-time count. Quest talents and Liquidation carriers stay. No Overall bonus is included. A separate attribute rule can use the saved count with Per liquidated talent; it requires an owned Liquidation carrier.",
        DonateBenchAttribute => "On acquisition, choose up to two random skaters classified as benchwarmers, as Organ Donor does. Set their selected base attribute to zero and distribute its total evenly across all other roster skaters, rounded toward zero. Native gain and balancing restrictions still apply. Talents and other attributes stay unchanged.",
        DonateBenchTalents => "On acquisition, choose up to two random skaters classified as benchwarmers and transfer their non-quest talents to random other roster skaters. Quest talents, abilities and attributes stay unchanged. This moves the existing talents without acquiring them again, matching Organ Donor's transfer behavior.",
        UpgradeAngusTalents => "On acquisition, upgrade eligible native talents on skaters identified as Angus by the current squad. Uses native upgrade selection and notifications. No ability upgrade is included; loading Continue does not repeat acquisition.",
        UpgradeAngusAbility => "On acquisition, upgrade an eligible ability on skaters identified as Angus by the current squad. Uses native upgrade selection and notifications. No talents are upgraded; loading Continue does not repeat acquisition.",
        ScorelessExpiry => "Remove this talent after a completed match in which its owner did not score, using Purgatory's native saved score flag and expiry. This component supplies no attribute bonus.",
        UpgradeOwnTalents => "When acquired, upgrade every eligible native talent already owned by this skater, using the native upgrade rules. Custom items have no upgrade level. This component supplies no Overall penalty; loading Continue does not acquire the item again.",
        GrantSpeedTalent => "When acquired, give each roster skater a native Speed-category talent, using Speed Monthly's native eligibility and fallback selection. The award remains in the run; no separate attribute bonus is added.",
        GrantCheckingTalent => "When acquired, give each roster skater a native Checking-category talent, using Checking Monthly's native eligibility and fallback selection. The award remains in the run.",
        GrantDefenseTalent => "When acquired, give each roster skater a native defensive-category talent, using Defense Monthly's native eligibility and fallback selection. The award remains in the run.",
        GrantShootingTalent => "When acquired, give each roster skater a native shooting-category talent, using Companion Puck's native eligibility and fallback selection. The award remains in the run.",
        GrantSlapshotTalent => "At the start of a new run, use Slapshot Plushie's native grant of a slapshot-effect talent to each eligible starting skater. Native exclusions and duplicate handling apply; Continue does not repeat this grant.",
        ShareTalent => "During match setup, choose an eligible teammate and loan one of their native talents to compatible teammates using Hot Cake's selection rules. Native type exclusions, notifications and removal apply. The loan does not become a campaign reward.",
        FrogHelmets => "Keep opposing Golden Helmet wearers frogged using Golden Frog's native roster synchronization. Native line changes update recipients, and removing the relic removes its transformations. No player attribute bonus is added.",
        OctopusAttacks => "Use the native possession-based tentacle attack cycle against an advancing opposing carrier, including its warnings, tier patterns, pauses, cooldown and cleanup. Strikes launch vulnerable skaters in range with the native burning movement; either team can be hit. Strike radius is editable.",
        AlternateOverall => "Alternate this skater's positive and negative Overall bonus between completed matches using Split Personality's saved native state. Acquisition starts in its native positive state. The magnitude is editable; this component does not alter base attributes.",
        RetainPowers => "At run end, eligible benchwarmers keep a randomly selected number of eligible powers for a future run through the native bench snapshot system. Starting powers and native exclusions remain excluded. Custom definitions and icons are retained locally with their saved IDs. This does not add items to reward pools.",
        PuckGoalieGhost => "Disable the opposing goalie's puck collision for the native half-second window after this shot. This does not make the puck pass through skaters. Activation chance is separate and editable.",
        PostGoal => "Once per match, use Post Goal's native goal-post collision handling to award a goal for an eligible hit on the opposing post. Native scoring, replay and goal-denial handling remain in control.",
        FartForce => "Multiply the force of this team's native fart interactions. This does not change range or add a new fart trigger.",
        FartRange => "Multiply the range of this team's native fart interactions. This does not change force or add a new fart trigger.",
        FartKnockouts => "Add native fart knockouts, either for both teams or opponents only. Existing vulnerability, fart immunity and source attribution remain active. This does not cause a fart by itself.",
        PickupAttribute => "Apply the selected permanent base attribute change when this talent is acquired. The native pickup consumes the talent after applying its value; Continue does not apply it again. Native attribute limits, balancing and gain restrictions still apply.",
        BossAttribute => "After the selected boss win, permanently change this skater's selected base attribute using native gain and balancing rules. Removing the talent afterward does not undo attributes already earned. This component does not change size.",
        BossSize => "After the selected boss win, permanently change this skater's size by the chosen number of native steps, respecting native limits. Removing the talent afterward does not undo earlier growth. This component changes no attributes.",
        FogScoreboard => "Use Foggy Brain's native scoreboard glitch when the owner gains puck possession while controlled by a human. Losing possession or removing the talent clears its native visual effect. This adds no attributes or AI behavior.",
        _ => ""
    };
}
