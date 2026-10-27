using System.Collections.Generic;
using System.Linq;

namespace CampaignCore;

public static partial class CustomRewardEffects
{
    public const string EggExplosion = "Explosion affecting both teams";
    public const string CopyOpponentRelics = "Copy opposing relics", StealOpponentRelics = "Steal opposing relics", FreezeOpponentRelic = "Freeze an opposing relic";
    public static bool IsOpponentRelicEffect(string effect) => effect == CopyOpponentRelics || effect == StealOpponentRelics || effect == FreezeOpponentRelic;
    public const string GoalPlanks = "Defensive goal planks", BreakawayAd = "Breakaway advertisement";
    public const string SonicKnockouts = "Sonic knockout wave";
    public const string HungerRage = "Enrage after time without puck", PossessionExplosion = "Knock out puck carrier after delay", EitherCarrier = "Puck carrier on either team";
    public const string LoosePuckAttraction = "Attract loose pucks continuously";
    public const string FasterChecks = "Faster bodycheck recovery", SpeedOneTimerAccuracy = "Add Speed bonuses to one-timer Accuracy", AttributeRewardChoice = "Add attribute reward choices";
    public const string SpikedRetaliation = "Bodycheckers fall flat", SpikedInjury = "Outgoing injury multiplier", FragileBody = "Fragile injury response";
    public const string RandomAutoShot = "Randomly shoot at the net";
    public const string PlayedMatchContract = "Leave after played matches";
    public const string DeflectLob = "Lob deflected puck toward teammate";
    public const string ChargePower = "Charged slapshot power", ChargeAccuracy = "Charged slapshot accuracy", ChargeCritical = "Full-charge critical shot";
    public static bool IsCharge(string effect) => effect == ChargePower || effect == ChargeAccuracy || effect == ChargeCritical;
    public const string RiggedFaceoffs = "Rigged faceoffs", PlayableReferees = "Recruit playable referees";
    public const string BatAim = "Improved bat aiming", BatLift = "Uncapped bat lift", BatKnockout = "Bat knocks out nearby skaters";
    public static bool IsBat(string effect) => effect == BatAim || effect == BatLift || effect == BatKnockout;
    public const string SlapshotSlowMotion = "Slow motion during slapshot windup";
    public const string ManualGoalDenial = "Manual goal reversal";
    public const string SecondScorerHelmet = "Golden Helmet for second scorer", TrainingMultiplier = "Training attribute multiplier", ExtraCoachPicks = "Extra Coach picks";
    public const string RandomShotPower = "Randomize shot power", RandomShotAccuracy = "Randomize shot accuracy";
    public const string BorrowTalent = "Borrow a benched teammate's talent";
    public const string PunchPass = "Pass puck when knocked out", FasterWindup = "Faster slapshot windup", FasterShotTurning = "Faster turning during slapshot windup";
    public const string LoadedPuckBag = "Shared shuffled puck values";
    public const string BoardBounce = "Bounce off boards", SoccerWall = "Soccer defensive wall";
    public const string ReturnPass = "Prepare return pass", PunchShot = "Punch puck when knocked out";
    public const string ConvertSpeedBonuses = "Convert Speed bonuses to Shot Power", ConvertSpeedTraining = "Convert Speed training to Shot Power";
    public const string IgnoreInjuryPenalties = "Ignore injury attribute penalties", InjureArm = "Cause an arm injury", RefereePursuit = "Referees check opponents";
    public const string PhysicalBallChain = "Attach physical ball and chain";
    public const string PossessionOvertime = "Keep playing at zero with possession";
    public const string WinByTwo = "Opponents must win by two goals";
    public const string ClockTime = "Adjust match clock", Clock = "Match clock", Rink = "Rink";
    public const string ClockSpeed = "Match clock speed";
    public static bool SupportsClockCondition(string trigger) => new[] { "While owned", "While leading", "While trailing", "While score tied", "While in period 1", "While in period 2", "While in period 3" }.Contains(trigger);
    public const string ExtraReferee = "Add a second referee", RefereeBlock = "Referees block opposing shots", RefereeSize = "Referee size", Referees = "Referees";
    public static bool IsRefereeSetup(string effect) => effect == PlayableReferees || effect == RefereePursuit || effect == ExtraReferee || effect == RefereeBlock || effect == RefereeSize;
    public const string PassUninterceptable = "Uninterceptable pass", GoalieHeadshot = "Rebound at shooter's head";
    public const string BonusMultiplier = "Attribute bonus multiplier", CritMultiplier = "Critical chance multiplier";
    public const string InjuryMultiplier = "Injury chance multiplier", InjuryHealing = "Adjust injury healing games", OpposingTeam = "Opposing team";
    public const string GainTalent = "Gain a random talent for the run", UpgradeTalent = "Upgrade random talent", UpgradeAbility = "Upgrade random ability", UpgradeRelic = "Upgrade random relic", KeepFreeAgents = "Keep free agents";
    public const string BenchMultiplier = "Bench bonus multiplier";
    public const string FlatPuck = "Flat puck launch", ShotRecoil = "Shot recoil";
    public const string CarryingCheck = "Bodycheck while carrying puck", GuaranteedCheck = "Guaranteed bodycheck knockout",
        SpinOnCheck = "Spin instead of bodycheck knockout", CollectOnHit = "Collect puck on contact", QuickRecovery = "Quicker knockout recovery";
    public const string KickPuck = "Handle puck without a stick", PostAim = "Aim shots at posts", ForceRebound = "Shots rebound off goalie";
    public const string InstantRecovery = "Instant knockout recovery", DeflectPucks = "Deflect opposing shots";
    public const string DeflectSticks = "Deflect thrown sticks", TurningDelay = "Additional turning time";
    public const string LockAttribute = "Lock attribute value";
    public const string BalanceAttributes = "Average live attributes", SpreadAttributeGains = "Spread permanent attribute gains";
    public const string PreferRewards = "Prefer this player for rewards", ExcludeRewards = "Exclude this player from rewards";
    public const string GoldenHelmet = "Extra Golden Helmet", HelmetMultiplier = "Helmet effect multiplier";
    public const string EnragedDuration = "Enraged duration multiplier";
    public const string ImpactKnockout = "Shot knocks out struck skater", ImpactExplosion = "Shot explodes on skater contact";
    public static bool IsImpactShot(string effect) => effect == ImpactKnockout || effect == ImpactExplosion;
    public const string AnchorProtection = "Anchor knockout protection", ForbidKnockouts = "Cannot knock out skaters", HandPuck = "Handle puck with hands", ForceOffside = "Enforce offside";
    public const string StatusMultiplier = "Status duration multiplier", CooldownMultiplier = "Ability recharge multiplier";
    public const string RandomMatchAbility = "Random ability for this match";
    public const string DenyGoal = "Deny this goal";
    public const string ExtraGoal = "One extra goal";
    public const string ExtraGoals = "Extra goals";
    public static bool IsUpgrade(string effect) => effect == UpgradeTalent || effect == UpgradeAbility || effect == UpgradeRelic;
    public static bool IsCampaignAction(string effect) => effect == GainTalent || IsUpgrade(effect);
    public static IEnumerable<string> ScalingConditionEffects => new[] { BonusMultiplier, CritMultiplier, StatusMultiplier, CooldownMultiplier, ClockSpeed };
    public static IEnumerable<string> EffectStats(string kind, string target, string trigger, string effect)
        => effect == HeavyHelmetAttribute ? new[] { "Checking", "Speed" } : effect == PropellerHelmetAttribute ? new[] { "Speed", "Accuracy" } : effect == IronHelmetAttribute || effect == LancelovHelmetAttribute ? new[] { "Shot Power", "Speed" } : effect == DonateBenchAttribute ? new[] { "Shot Power", "Accuracy", "Speed", "Checking" } : effect == PickupAttribute || effect == BossAttribute ? new[] { "Shot Power", "Accuracy", "Speed", "Checking", "Overall" } : effect == LockAttribute ? new[] { "Shot Power", "Speed", "Checking" } : effect == BonusMultiplier ? kind == CustomRewardDefinition.CustomRelics ? new[] { "Shot Power" } : new[] { "Accuracy", "Speed", "Checking" } : Stats(kind, target, trigger);
    public const string Injure = "Cause a random injury", Heal = "Heal injuries", ThrowStick = "Throw stick", DropHelmet = "Drop helmet";
    public const string AiChase = "AI: chase puck", AiCherry = "AI: cherry pick", AiSafety = "AI: protect own net", AiBlock = "AI: block shots",
        AiRedirect = "AI: redirect shots", AiHome = "AI: check in defensive zone", AiMaraud = "AI: check in offensive zone";
    public const string GolfRink = "Golf rink", SoccerRink = "Soccer rink", TycoonsRink = "Tycoons rink";
    public const string NetWidth = "Goal width", NetHeight = "Goal height", SoccerNetLook = "Soccer net appearance";
    public const string TalentRewards = "Extra talent rewards", RelicRewards = "Extra relic rewards", Rerolls = "Extra rerolls";
    public const string CopyFreeAgentStats = "Copy random free-agent attributes", CopyFreeAgentLook = "Copy random free-agent appearance";
    public const string Freeze = "Freeze skaters", Dance = "Make skaters dance";
    public const string PushSkaters = "Shockwave: push skaters", PushPuck = "Shockwave: push puck", PushSticks = "Shockwave: push loose sticks";
    public const string PreventInjury = "Prevent all injuries", PreventEye = "Prevent eye injuries", PreventShoulder = "Prevent shoulder injuries",
        PreventLeg = "Prevent leg injuries", PreventArm = "Prevent arm injuries";
    public const string CatchSaves = "Goalie catches saves";
    public const string GoalieFetchSpeed = "Goalie puck retrieval speed", GoalieThrow = "Goalie throws stick at puck carrier";
    public const string CopyGoalieAbility = "Copy opposing goalie's ability", CopyGoalieTalents = "Copy opposing goalie's talents";
    public static bool IsGoalieCopy(string effect) => effect == CopyGoalieAbility || effect == CopyGoalieTalents;
    public static IEnumerable<string> GoalieConditionEffects => new[] { CatchSaves, GoalieFetchSpeed, GoalieThrow };
    public const string ForbidStick = "Cannot use sticks", ExtraSticks = "Extra reserve sticks", ForbidCheck = "Cannot bodycheck", RepelPuck = "Repel nearby puck";
    public const string AiBerserk = "AI: seek bodychecks";
    public static IEnumerable<string> ForwardConditionEffects => new[] { RefereeBlockTalent, PreventPredictivePoke, LoosePuckAttraction, SpikedRetaliation, SpikedInjury, FragileBody, RandomAutoShot, SlapshotSlowMotion, BatAim, BatLift, BatKnockout, ChargePower, ChargeAccuracy, ChargeCritical, RandomShotPower, RandomShotAccuracy, BorrowTalent, PunchPass, FasterWindup, FasterShotTurning, PhysicalBallChain, IgnoreInjuryPenalties, ReturnPass, PunchShot, BalanceAttributes, LockAttribute, ForbidStick, ExtraSticks, ForbidCheck, RepelPuck, CarryingCheck, GuaranteedCheck, SpinOnCheck, CollectOnHit, QuickRecovery, KickPuck, PostAim, ForceRebound, InstantRecovery, DeflectPucks, DeflectSticks, TurningDelay, AnchorProtection, ForbidKnockouts, HandPuck, ForceOffside };
    public static IEnumerable<string> AiEffects => new[] { AiKarlson, AiPrince, AiKnight, AiChase, AiCherry, AiSafety, AiBlock, AiRedirect, AiHome, AiMaraud, AiBerserk };
    private static IEnumerable<Entry> FlowEntries => new[] {
        new Entry(EggExplosion, "ExplosionOnLanding; Forward.Explode with Eggsplosion, own-team contact and exploder contact enabled."),
        new Entry(CopyOpponentRelics, "RelicStealer Copy mode; native pre-initialization eligibility, copied instances and affected-relic notifications."),
        new Entry(StealOpponentRelics, "RelicStealer Steal mode; native pre-initialization transfer and affected-relic notifications."),
        new Entry(FreezeOpponentRelic, "RelicFreezerController; native FrozenRelic wrapper, ownership refresh and notifications."),
        new Entry(GoalPlanks, "GoalPlankRelicController; native plank prefab placement, goalie collision exclusions and goal/reset lifecycle."),
        new Entry(BreakawayAd, "AnnoyingAdController; native opposing-breakaway UI, arena-side placement and end cleanup."),
        new Entry(SonicKnockouts, "SonicPass / SonicInterception; SkaterAbility.SonicSlap radial knockouts, excludes source and includes teammates."),
        new Entry(HungerRage, "FeedController / FeedComponent; native possession timer and owned enrage state."),
        new Entry(PossessionExplosion, "HotPotatoController / HotPotatoMonoBehaviour; native possession duration, carrier knockout and warning colors."),
        new Entry(LoosePuckAttraction, "LightningRodController; continuous loose-puck magnet with native state, sound and VFX lifecycle."),
        new Entry(FasterChecks, "FastShoulder / BodycheckState.OnKnockout; native animation recovery after a successful knockout."),
        new Entry(SpeedOneTimerAccuracy, "Needle in ForwardStats.GetOneTimerAccuracy; adds live Speed minus saved base Speed before native accuracy mapping."),
        new Entry(AttributeRewardChoice, "StatRewardRelic in RogueUtils.GetRandomTalentRewards; native attribute choice construction."),
        new Entry(SpikedRetaliation, "SpikedArmor in HasAntiBodycheck / HandleAntiBodycheck; separate retaliation without injury multiplier."),
        new Entry(SpikedInjury, "SpikedArmor.InjuryMultiplier in InjurySystem.HandleInjury/HandleFragileInjury; separate outgoing multiplier."),
        new Entry(FragileBody, "FragileTalent in InjurySystem.TryInjuryAttempt; native fragile injury probability and pacing."),
        new Entry(RandomAutoShot, "SpasmController native possession timer, chance, warning and delayed shot; LiveWire fixed Spasm component without Shot Power."),
        new Entry(PlayedMatchContract, "TemporaryContract / ShortContract separate MaxGamesCondition; native completed-match and departure handling."),
        new Entry(DeflectLob, "DefensiveDeflect upgraded HandleDefensiveDeflect component; native CanStickyPickup, RetrievePuck and MegaLob."),
        new Entry(ChargePower, "ChargeShot.GetChargedDuration / ChargeShotController.ChargeShotRelease; separate pre-launch Shot Power bonus."),
        new Entry(ChargeAccuracy, "ChargeShot.GetChargedDuration / ChargeShotController.ChargeShotRelease; separate pre-launch Accuracy bonus."),
        new Entry(ChargeCritical, "ChargeShot.IsFullyCharged in the native slapshot coroutine; separate guaranteed critical outcome."),
        new Entry(RiggedFaceoffs, "RiggedFaceoff in FaceoffWithReferee.StartFaceoff; native rigged outcome and opposing-team arbitration."),
        new Entry(PlayableReferees, "PlayableRefereeController.AddEffect; referee team assignment and native player selection."),
        new Entry(BatAim, "Slugger query in SkaterAbility.Bat; separate native bat aim branch."),
        new Entry(BatLift, "Slugger first query in SkaterAbility._Bat.MoveNext; separate vertical-speed cap bypass."),
        new Entry(BatKnockout, "Slugger second query in SkaterAbility._Bat.MoveNext; native radial knockout of nearby skaters, including teammates."),
        new Entry(SlapshotSlowMotion, "SlapshotSlowmo in SlapshotState.DoBeforeMove / StopSlowmo; separate global time and sound component."),
        new Entry(ManualGoalDenial, "GoalDenierComponent native input, replay window and DenyGoal transaction; separate from GoalDenierEmpty stat payoff."),
        new Entry(SecondScorerHelmet, "GoldenHelmetSecondScorerRelic in GoldenHelmetManager holder ranking and saved-player helmet queries."),
        new Entry(TrainingMultiplier, "VanillaTraining.TrainingMultiplier / TrainingEvent.GetModifiedStatBonus; native rounding and preview path."),
        new Entry(ExtraCoachPicks, "DoubleCoaching / CoachEvent.NextChoices; native Coach choice regeneration."),
        new Entry(RandomShotPower, "GamblerController.GenerateShotPower / GetShotPower; native pre-shot roll and attribute replacement."),
        new Entry(RandomShotAccuracy, "GamblerController.GenerateAccuracy / GetAccuracy; independent native pre-shot roll and attribute replacement."),
        new Entry(BorrowTalent, "BenchBorrowerController.Borrow / Return; separate temporary talent loan with native candidate eligibility."),
        new Entry(PunchPass, "PassPuncherController.Execute in Forward.Knockout; native possession and teammate selection, without upgraded stat bonuses."),
        new Entry(FasterWindup, "SlapshotState.HasFasterWindup; SlapshotSlowmo upgraded windup component."),
        new Entry(FasterShotTurning, "SlapshotState.HasFasterTurningTime; SlapshotSlowmo separate turning component."),
        new Entry(LoadedPuckBag, "LoadedPuckController.SyncQueue / EliminatorPuckQueue; separate shared scoring bag, native colors and goal prediction."),
        new Entry(WinByTwo, "TieDomi in GameManager.WinningTeam and MaxGoal.OnAfterReplay; native match-mode winner resolution."),
        new Entry(PossessionOvertime, "LastStand.CanKeepPlayingAtZero; native final-whistle, possession, pass and buzzer-shot handling."),
        new Entry(PhysicalBallChain, "BallAndChainController / BallAndChainComponent; native constrained physics and faceoff/reset lifecycle."),
        new Entry(IgnoreInjuryPenalties, "SadoMaso's live GetStatAfterInjuryCheck bypass, separate from injury bonuses and healing duration."),
        new Entry(InjureArm, "EggShellHands' specific arm injury, through native injury eligibility and storage."),
        new Entry(RefereePursuit, "Referee.Start / SwitchTarget / PursueHitting; RefHitOtherTeam's AI component without its upgraded talent grant."),
        new Entry(ConvertSpeedBonuses, "QuantumSextant's live Team.HaveRelicCached query in native Speed and Shot Power calculations."),
        new Entry(ConvertSpeedTraining, "QuantumSextant's saved-team queries in ConditioningNPC training choices and gains."),
        new Entry(ReturnPass, "PassBack capability on the receiving skater in the native pass coroutine; return direction setup."),
        new Entry(PunchShot, "PuckPuncher.Execute inside Forward.Knockout; native possession and knockout gates, without the stat bonus."),
        new Entry(BoardBounce, "BoardBumperController.OnSkaterHitBoard; native reflection, angle limit, speed and feedback, without its event subscription."),
        new Entry(SoccerWall, "SoccerWallController's defensive wall creation, positioning and removal; a team rink prop."),
        new Entry(EnragedDuration, "ProlongedRage / EnragedStatusEffectController.CheckForDurationMultiplier; separate from other statuses."),
        new Entry(GoldenHelmet, "GoldenHelmetTalent's saved-player helmet ownership queries."),
        new Entry(HelmetMultiplier, "HelmetStackOvrTalent.GetHelmetPowerupMultiplier; native helmet bonus, bench and grant calculations."),
        new Entry(PreferRewards, "AlwaysRewarded / RogueUtils.PICK_ME_TALENT_TYPE; native player-choice priority."),
        new Entry(ExcludeRewards, "NeverRewarded / RogueUtils.NEVER_ME_TALENT_TYPE; native player reward and event eligibility."),
        new Entry(BalanceAttributes, "BalancedTalent's ForwardStats._isBalanced averaging path, separate from permanent gain conversion."),
        new Entry(SpreadAttributeGains, "BalancedTalent's ForwardData.IncrementStats quarter-and-round conversion into all four native attributes."),
        new Entry(LockAttribute, "StatLockTalent / Railgun / Bedrock / TopGear data queries; native attribute calculation, gain blocking and setters."),
        new Entry(DeflectSticks, "WonderWall's separate thrown-stick collision branch in Pools.Stick.TryHit."),
        new Entry(TurningDelay, "HyperDrive.minTimeTurningIncrease in the three native turning states; separate from Speed."),
        new Entry(ImpactKnockout, "DestroyerShot's level-one current-shot contact outcome; native vulnerability, force and knockout handling."),
        new Entry(ImpactExplosion, "DestroyerShot's upgraded current-shot explosion outcome; editable contact explosion radius."),
        new Entry(ClockSpeed, "TimeRemaining.DecreaseTime; WhistleOfTimeTimer's clock-rate component, preserving native pause and clock modes."),
        new Entry(AnchorProtection, "Anchor's defensive capability queries in CanBeKnockedOut / CanBeKnockedOutBy / OnPunched, separate from CanKnockout."),
        new Entry(ForbidKnockouts, "Anchor's offensive CanKnockout capability query, separate from its protection."),
        new Entry(HandPuck, "RefHands' Forward.actAsReferee handling flag; stick restriction is a separate effect."),
        new Entry(ForceOffside, "OffsideController.AddForceSource / RemoveForceSource; Linesman's independently owned contribution."),
        new Entry(AiBerserk, "AIStateManager.SetBerserkState; Charge's bodycheck-seeking behavior without its stick restriction or Checking bonus."),
        new Entry(InstantRecovery, "SpringBoard capability queries in KnockoutState; native landing and recovery transitions."),
        new Entry(DeflectPucks, "DefensiveDeflect capability in Puck.OnTriggerEnter / HandleDefensiveDeflect; base deflection without the upgraded lob."),
        new Entry(ExtraReferee, "NormalMatchGameModeController.OnInitializeForwards; SecondReferee's separate spawn."),
        new Entry(RefereeBlock, "Referee.isBlockingShots / teamBlocking; RefBlockShot positioning, without its growth."),
        new Entry(RefereeSize, "ForwardData.SetSizeEffect on referees; RefBlockShot's separate growth component."),
        new Entry(CarryingCheck, "OffensiveBodycheck capability queries in CanBodycheck and the bodycheck/punch states."),
        new Entry(GuaranteedCheck, "Onepunch capability query in SkaterCollision; native target immunity still applies."),
        new Entry(SpinOnCheck, "FigureCheck capability query in SkaterCollision.HandleHitted."),
        new Entry(CollectOnHit, "Velcro capability queries in the native puck-contact handlers."),
        new Entry(QuickRecovery, "KnockoutState.HasQuickerRecovery; PlayerGetUpFaster's recovery component."),
        new Entry(KickPuck, "Kick capability queries; stickless handling and animations, with its attribute bonus removed."),
        new Entry(PostAim, "PingKing capability queries in the native shot coroutines before launch."),
        new Entry(ForceRebound, "ReboundBouncer capability query in SavingState.OnPuckCollision; base rebound component."),
        new Entry(FlatPuck, "CantLeverLaPuck launch component; zero vertical launch velocity."),
        new Entry(ShotRecoil, "SmoothBoostComponent.StartBoost with negative horizontal shotDirection; ShartRecoil without the fart component.", true),
        new Entry(StatusMultiplier, "SkaterStatusEffectsManager.AddStatusEffect; StatusExtender duration component."),
        new Entry(CooldownMultiplier, "AbilityControllerBase.Update; GreaterCooldown/GreaterCooldownRelic recharge component."),
        new Entry(RandomMatchAbility, "RandomAbilityController / Skater.AddAbility; reversible ability replacement for the current match."),
        new Entry(DenyGoal, "GoalScoredArg.goalType before dispatch; NoGoalTalent/Padlock and GoalDenier denial component."),
        new Entry(ExtraGoal, "GoldenPuck capability inside GameStats.OnGoalScored / OnDeniedGoal; native goal and assist accounting."),
        new Entry(ExtraGoals, "GoldenPuck branch of GameStats.OnGoalScored, repeating its native per-goal AddGoal helper (team, scorer, assist and persistent goal statistics); OnDeniedGoal repeats CancelStats to reverse every added goal."),
        new Entry(GainTalent, "PerformanceBonusController.PickRandomTalent / Skater.TryAddTalent; explicit random talent grant."),
        new Entry(UpgradeTalent, "UpgradeHelper.GetUpgradeablePowerups / UpgradeableTalent.Upgrade; RandomUpgrade's talent component."),
        new Entry(UpgradeAbility, "UpgradeHelper.GetUpgradeablePowerups / UpgradeableAbility.Upgrade; RandomUpgrade's ability component."),
        new Entry(UpgradeRelic, "UpgradeHelper.GetUpgradeablePowerups / UpgradeableRelic.Upgrade; RandomUpgrade's relic component."),
        new Entry(KeepFreeAgents, "FreeAgentManager.HandleFreeAgentsLeaving; Handcuffs' contract-expiry protection."),
        new Entry(BenchMultiplier, "BenchBonusController.GetBenchBonusMultiplier; DoubleBenchTalent, ButtBenchTalent and DoubleBenchBonus."),
        new Entry(PassUninterceptable, "PassingArg.passType / Puck.IsDuringNonInterceptedPass; InvisibleThread's pass component."),
        new Entry(GoalieHeadshot, "Puck.HeadShot; GoalieHeadShot's targeted rebound without its blocker-only subscription."),
        new Entry(BonusMultiplier, "ForwardStats.GetStats; OriginalGlove/OriginalSkate/OriginalStick/OriginalPuck bonus scaling."),
        new Entry(CritMultiplier, "CriticalShot.GetCriticalOdds; CriticalMass's crit chance scaling."),
        new Entry(InjuryMultiplier, "InjurySystem.CalculateInjuryChance; Fragile / EnragedInjuryRelic probability scaling."),
        new Entry(InjuryHealing, "InjurySystem.AddInjuryToForward; SadoMaso's separate healing-time component."),
        new Entry(ClockTime, "TimeRemaining.AddTime; Head Start Clock, Injured Clock and Spiteful Clock."),
        new Entry(Injure, "InjurySystem.AddRandomInjury; native injury eligibility, healing and save path."),
        new Entry(Heal, "InjurySystem.HealAllInjuries; native healed-injury events and save path."),
        new Entry(ThrowStick, "Forward.ThrowStick in the skater's current facing direction; Fury Stick."),
        new Entry(DropHelmet, "Forward.DebugDropHelmet / native equipment drop path."),
        new Entry(AiChase, "AIStateManager.SetBallChasingState; Ball Chaser."),
        new Entry(AiCherry, "AIStateManager.SetCherryPickState; Cherry Picker."),
        new Entry(AiSafety, "AIStateManager.SetSafetyFirstState; Safety First."),
        new Entry(AiBlock, "AIStateManager.SetBlockShotState; Human Shield."),
        new Entry(AiRedirect, "AIStateManager.SetRedirectorState; Redirector."),
        new Entry(AiHome, "AIStateManager.SetHomeWreckerState; Homewrecker's positioning without its stat bonus."),
        new Entry(AiMaraud, "AIStateManager.SetMarauderState; Marauder's positioning without its stat bonus."),
        new Entry(GolfRink, "IceSetup.SwitchIceType(Golf); native baked rink and friction."),
        new Entry(SoccerRink, "IceSetup.SwitchIceType(Soccer); native baked rink and friction."),
        new Entry(TycoonsRink, "IceSetup.SwitchIceType(Tycoons); native rink surface."),
        new Entry(NetWidth, "Goal.SetGoalieNet; Soccer Net's width component, separate from height and material."),
        new Entry(NetHeight, "Goal.SetGoalieNet; Soccer Net's height component, separate from width and material."),
        new Entry(SoccerNetLook, "Goal.soccerNetMaterial; Soccer Net's appearance component, without changing dimensions."),
        new Entry(TalentRewards, "RewardCounter.SetTalentRewardCount; separate from relic reward counts."),
        new Entry(RelicRewards, "RewardCounter.SetRelicRewardCount; separate from talent reward counts."),
        new Entry(Rerolls, "Reward and event menu InitData, after the native count is copied."),
        new Entry(CopyFreeAgentStats, "CampaignState.GetRandomUnlockedFreeAgent; Faceless's four base attributes, separately from appearance."),
        new Entry(CopyFreeAgentLook, "CampaignState.CopyFreeAgentData; Faceless's appearance replacement, preserving attributes, position, rarity and powers."),
        new Entry(Freeze, "MovementStateManager.SetFreezingState; Ice Slapshot's freeze without its shot trigger.", true),
        new Entry(Dance, "MovementStateManager.SetDancingFreezeState; Goalie Dance's movement effect.", true),
        new Entry(PushSkaters, "FartObject with only ShouldPushForwards; Wind Up / Wind Down."),
        new Entry(PushPuck, "FartObject with only ShouldPushPucks; Wind Up / Wind Down."),
        new Entry(PushSticks, "FartObject with only ShouldPushSticks; Wind Up / Wind Down."),
        new Entry(PreventInjury, "InjurySystem.CanBeInjured; Frozen Peas, separately from healing existing injuries."),
        new Entry(PreventEye, "InjurySystem.CanHaveInjuryType(Eye); native injury prevention."),
        new Entry(PreventShoulder, "InjurySystem.CanHaveInjuryType(Shoulder); native injury prevention."),
        new Entry(PreventLeg, "InjurySystem.CanHaveInjuryType(Leg); native injury prevention."),
        new Entry(PreventArm, "InjurySystem.CanHaveInjuryType(Arm); native injury prevention."),
        new Entry(CatchSaves, "SavingState.HandlePossession; Infinimitt's native catch branches and animations."),
        new Entry(GoalieFetchSpeed, "Goaltender.GetMaxSpeedFetch; The Falcon's separate +6 retrieval-speed component."),
        new Entry(GoalieThrow, "FollowingPuckStanding.ThrowStick; Tomahawk's native timer, chance, range, equipment checks and throwing state."),
        new Entry(CopyGoalieAbility, "MusicalNetsController.OnInitialize / Skater.AddAbility; copy only the opposing goalie's ability."),
        new Entry(CopyGoalieTalents, "MusicalNetsController.IsCopyable / Skater.TryAddTalent; copy only eligible opposing goalie talents."),
        new Entry(ForbidStick, "Forward.AddForbidStickSource / RemoveForbidStickSource; No Stick's reversible restriction."),
        new Entry(ExtraSticks, "Forward.AddStickCountContributor / RemoveStickCountContributor; Stick Reserve's separate contribution."),
        new Entry(ForbidCheck, "MovementManager.CanBodycheck; No Hit's bodycheck restriction."),
        new Entry(RepelPuck, "PuckRepellentComponent; Puck Repellent's native distance-based force.")
    };
    public static bool IsRink(string effect) => effect == GolfRink || effect == SoccerRink || effect == TycoonsRink;
    public static bool IsNet(string effect) => effect == NetWidth || effect == NetHeight || effect == SoccerNetLook;
    public static bool IsFreeAgentCopy(string effect) => effect == CopyFreeAgentStats || effect == CopyFreeAgentLook;
    public static bool IsShockwave(string effect) => effect == PushSkaters || effect == PushPuck || effect == PushSticks;
    public static bool IsInjuryPrevention(string effect) => effect == PreventInjury || effect == PreventEye || effect == PreventShoulder || effect == PreventLeg || effect == PreventArm;
    private static bool IsQueryEffect(string effect) => IsCultist(effect) || IsTemperature(effect) || effect == CelebrationInjuries || effect == BenchTalentSlot || effect == BossAttribute || effect == BossSize || effect == FartForce || effect == FartRange || effect == FartKnockouts || IsLifecycleEffect(effect) || IsOpponentRelicEffect(effect) || effect == GoalPlanks || effect == BreakawayAd || effect == HungerRage || effect == PossessionExplosion || effect == FasterChecks || effect == SpeedOneTimerAccuracy || effect == AttributeRewardChoice || effect == PlayedMatchContract || effect == RiggedFaceoffs || effect == ManualGoalDenial || effect == SecondScorerHelmet || effect == TrainingMultiplier || effect == ExtraCoachPicks || effect == LoadedPuckBag || effect == WinByTwo || effect == PossessionOvertime || effect == ConvertSpeedBonuses || effect == ConvertSpeedTraining || effect == SoccerWall || effect == EnragedDuration || effect == GoldenHelmet || effect == HelmetMultiplier || effect == PreferRewards || effect == ExcludeRewards || effect == SpreadAttributeGains || IsRefereeSetup(effect) || effect == ExtraGoal || effect == ExtraGoals || effect == DenyGoal || IsRink(effect) || IsNet(effect) || effect == TalentRewards || effect == RelicRewards || effect == Rerolls || IsFreeAgentCopy(effect) || IsInjuryPrevention(effect) || effect == InjuryMultiplier || effect == InjuryHealing || effect == KeepFreeAgents || effect == BenchMultiplier;
    private static IEnumerable<string> QueryEffects(string trigger) => trigger switch {
        "Cultist incantation cycle" => new[] { CultistFrog, CultistFreeze, CultistRory, CultistDord, CultistJelly, CultistCornelius, CultistPam },
        "Defensive possession teleport" => new[] { DefenseTeleport },
        "Native blue-line crossing" => new[] { BlueLineLaunch, DefensiveBlueLineKnockout, OffensiveBlueLineKnockout },
        "Knockout duration calculation" => new[] { KnockoutDuration },
        "Unused-player win reward" => new[] { ParticipationBonus },
        "Opposing breakaway shot" => new[] { BreakawayShotPenalty, BreakawayShotFall },
        "Opening team knockouts" => new[] { OpeningInjuries },
        "Team knockout injury cycle" => new[] { CyclicInjuries },
        "Puck knockout injury" => new[] { PuckInjuries },
        "Opening team shots" => new[] { OpeningCriticals },
        "Recent team shot" => new[] { ReboundCriticals },
        "Injury recovery" => new[] { HealedInjuryBonus },
        "Celebrating scorer knockout" => new[] { CelebrationInjuries },
        "Completed-match attribute roll" => new[] { RandomMatchAttribute },
        "Bench talent reward" => new[] { BenchTalentSlot },
        "Centre defender loans" => new[] { CenterDefenderChecks },
        "Controlled possession display" => new[] { FogScoreboard },
        "Boss won" or "Act 1 boss won" or "Act 2 boss won" => new[] { BossAttribute, BossSize },
        "Random fart timer" => new[] { PushSkaters, PushPuck, PushSticks },
        "Puck fart timer" => new[] { PushSkaters, PushSticks },
        "Goal-post collision" => new[] { PostGoal },
        "Fart interaction" => new[] { FartForce, FartRange, FartKnockouts },
        "Talent expiry" => new[] { ScorelessExpiry },
        "Item acquired" => new[] { BenchCapacity, LiquidateTalents, DonateBenchAttribute, DonateBenchTalents, UpgradeAngusTalents, UpgradeAngusAbility, PickupAttribute, UpgradeOwnTalents, GrantSpeedTalent, GrantCheckingTalent, GrantDefenseTalent, GrantShootingTalent },
        "Match cycle" => new[] { AlternateOverall },
        "Bench run retention" => new[] { RetainPowers },
        "Opposing possession timer" => new[] { OctopusAttacks },
        "Breakaway display" => new[] { BreakawayAd },
        "Without puck timer" => new[] { HungerRage },
        "Puck possession timer" => new[] { PossessionExplosion },
        "Bodycheck movement" => new[] { FasterChecks },
        "Talent reward choices" => new[] { AttributeRewardChoice },
        "Player contract" => new[] { PlayedMatchContract },
        "Faceoff setup" => new[] { RiggedFaceoffs },
        "Opponent goal reversal window" => new[] { ManualGoalDenial },
        "Golden Helmet ranking" => new[] { SecondScorerHelmet },
        "Training attribute rewards" => new[] { TrainingMultiplier },
        "Coach choices" => new[] { ExtraCoachPicks },
        "Shared puck value" => new[] { LoadedPuckBag },
        "Match winner calculation" => new[] { WinByTwo },
        "Final whistle" => new[] { PossessionOvertime },
        "Attribute bonus conversion" => new[] { ConvertSpeedBonuses, SpeedOneTimerAccuracy },
        "Training attribute conversion" => new[] { ConvertSpeedTraining },
        "Enraged duration calculation" => new[] { EnragedDuration },
        "Helmet equipment" => new[] { HeavyHelmetAttribute, IronHelmetAttribute, PropellerHelmetAttribute, LancelovHelmetAttribute, GoldenHelmet },
        "Helmet effect calculation" => new[] { HelmetMultiplier },
        "Player reward eligibility" => new[] { PreferRewards, ExcludeRewards },
        "Permanent attribute change" => new[] { SpreadAttributeGains },
        "Goal verification" => new[] { DenyGoal },
        _ when CustomRewardTriggers.IsGoalValueCalculation(trigger) => new[] { ExtraGoal, ExtraGoals },
        "Match setup" => new[] { AngusZero, OpposingBonecrusher, OpposingBriefcase, FreezeOwnRelics, FrogOwnPair, MinimumTeamSize, VoodooAttribute, HotAttribute, ColdAttribute, ShareTalent, FrogHelmets, CopyOpponentRelics, StealOpponentRelics, FreezeOpponentRelic, GoalPlanks, PlayableReferees, SoccerWall, GolfRink, SoccerRink, TycoonsRink, NetWidth, NetHeight, SoccerNetLook, ExtraReferee, RefereeBlock, RefereeSize, RefereePursuit },
        "Match rewards" => new[] { TalentRewards, RelicRewards },
        "Reward selection" or "Map event choices" => new[] { Rerolls },
        "Run started" => new[] { GrantSlapshotTalent, CopyFreeAgentStats, CopyFreeAgentLook },
        "Injury attempted" => new[] { PreventInjury, PreventEye, PreventShoulder, PreventLeg, PreventArm, InjuryMultiplier },
        "New injury" => new[] { InjuryHealing },
        "Contract expiry" => new[] { KeepFreeAgents },
        "Bench bonus" => new[] { BenchMultiplier },
        _ when CustomRewardTriggers.IsShotCalculation(trigger) => new[] { StatChange },
        _ => System.Array.Empty<string>()
    };
    private static string FlowHelp(string effect) => effect switch {
        EggExplosion => "Create Eggsplosion's native blast around the triggering skater. It can affect teammates, opponents and the source skater; native vulnerability still applies. Radius is editable. This is separate from the explosion that protects its source and teammates.",
        CopyOpponentRelics => "Before the match initializes, copy eligible opposing relics using native Relic Copier selection. Copies retain the original relic mechanics; the opponent keeps theirs. The native count is one, or two on Expert. Native exclusions, including protected starting relics, still apply.",
        StealOpponentRelics => "Before the match initializes, transfer up to two eligible opposing relics to your match team using native Relic Stealer selection. The opponent loses those match copies. Native eligibility and initialization order remain active; this does not author or award a permanent campaign relic.",
        FreezeOpponentRelic => "At match start, freeze one eligible opposing relic using the native Frozen Relic wrapper. The original relic remains represented by that wrapper while its match effect is removed. Native eligibility, player refreshes and notifications remain active.",
        GoalPlanks => "Place the native defensive planks at this team's goal. Native orientation, goalie collision exclusions, goal reactions and controller cleanup remain active. This rink prop belongs to the team relic and grants no player attributes.",
        BreakawayAd => "Display the native Annoying Ads overlay when an opponent starts a breakaway, on the rink side selected by the owning team. Native breakaway-end, ownership and controller cleanup hide the overlay. Does not change a skater's attributes or AI.",
        SonicKnockouts => "Send a native sonic wave from the triggering skater, knocking down nearby skaters on both teams except that source. On an intercepted pass, the current puck can be the wave center instead, as Sonic Interception does. Native vulnerability and knockout force apply. This differs from the teammate-safe explosion.",
        HungerRage => "Use Cold Turkey's native timer while this skater is without the puck. After the chosen delay, maintain native enrage until puck pickup resets the timer and removes the enrage supplied by this component. Native gameplay pauses and reset events remain active. No attribute bonus is added separately.",
        PossessionExplosion => "Use Hot Potato's native warning color and knockout after a skater carries the puck longer than the selected delay. This shared puck rule affects either team, including your own skaters. Passing or losing possession resets the carrier's timer. Native knockout force, immunity and cleanup remain active.",
        LoosePuckAttraction => "While playing and the puck is loose, attract it using Lightning Rod's native magnet steering, sound and visual effect. Shots, passes and carried pucks keep their normal flow. The widest active custom range applies; an owned native Lightning Rod keeps its own behavior. Benching, loss of ownership or the condition ending removes the custom magnet.",
        FasterChecks => "After a skater knocks someone out during a bodycheck, speed up the remainder of that check animation to the native four-times rate. Missed checks keep their normal recovery. Does not change skating speed, Checking or knockout chance.",
        SpeedOneTimerAccuracy => "Add current Speed minus saved base Speed to the Accuracy used for a one-timer, before native accuracy mapping. Native Speed injuries, locks and conversions remain effective. Does not change regular shot Accuracy or consume the Speed bonus. Native and custom copies share one conversion.",
        AttributeRewardChoice => "Include the native additional attribute option when talent rewards are generated. The game constructs eligible player and attribute choices normally. This does not add another reward selection or change relic rewards. Requires campaign rewards.",
        SpikedRetaliation => "Use Spiked Armor's native response to bodychecks: qualifying bodycheckers fall flat through the normal contact and immunity rules. The separate outgoing injury multiplier is not included.",
        SpikedInjury => "Scale this skater's native outgoing injury multiplier, including fragile injury attempts. This does not make bodycheckers fall flat or cause an injury without a qualifying native injury attempt. Requires campaign injuries.",
        FragileBody => "Use Fragile's native injury response when knocked out, with the game's fixed odds and injury pacing. Native prevention and injury eligibility still apply. This adds no attribute bonus and requires campaign injuries.",
        RandomAutoShot => "While carrying the puck, periodically roll the chosen chance to shoot automatically at the opposing net. Native Spasms supplies the warning and delayed choice of wrist shot or slapshot. Shot Power is a separate effect. Only the owner receives this behavior.",
        PlayedMatchContract => "Use native contract terms and departure handling. Benched matches do not count. Ordinary contracts register before match counting, preserve an existing contract and protect Angus. Chaos contracts register when acquired and retain Short Contract's native owner, remaining-game counter and contract-removal behavior, with its attribute bonus removed. Handcuffs can prevent expiry departures. Registering a contract does not create a reversible stat bonus.",
        DeflectLob => "After a defensive deflection, retrieve and lob that puck using the native teammate selection and pass. Native pickup eligibility applies. This effect does not grant the separate ability to deflect; it requires an actual defensive deflection. A native upgraded Defensive Deflect already performs this lob and is not repeated.",
        ChargePower => "Hold a slapshot using the native charge windup. Add the chosen Shot Power per held second, up to the selected charge limit, rounded toward zero. The released bonus lasts for the native half-second shot window. Accuracy and a guaranteed full-charge critical are separate effects.",
        ChargeAccuracy => "Hold a slapshot using the native charge windup. Add the chosen Accuracy per held second, up to the selected charge limit, rounded toward zero. The released bonus lasts for the native half-second shot window. Shot Power and a guaranteed full-charge critical are separate effects.",
        ChargeCritical => "Hold a slapshot using the native charge windup. Reaching the selected charge time makes this slapshot critical through the game's normal critical-shot calculation. Shorter charges keep their normal critical chance. No extra Shot Power or Accuracy is granted. Native Charge Shot still retains its own full behavior when also owned.",
        RiggedFaceoffs => "Use the native Rigged Faceoff outcome. The native game decides the result when both teams own it, handles the referee-team exception, and retains the regular practice flow. Does not grant a faceoff stat bonus or a goal.",
        PlayableReferees => "Assign the match referees to the owning team and allow native player switching to them, as Whistle Blower does. Latest acquired custom recruitment wins between custom owners. Existing native recruitment remains in control. Removing this contribution restores only its own team and control changes.",
        BatAim => "Use Slugger's native aimed batting branch while this condition holds. Native target choice, Accuracy and bat strength still apply. The upward-speed cap and surrounding knockouts are separate effects.",
        BatLift => "Keep the native bat trajectory's upward speed instead of applying the ordinary bat cap. Does not add upward velocity or change aiming. Nearby knockouts are separate.",
        BatKnockout => "When a bat launches, knock out skaters within the selected radius through Slugger's native contact loop and force. This includes teammates and opponents; the batter is excluded. Native immunity still applies. Bat aiming and upward-speed rules remain separate.",
        SlapshotSlowMotion => "Use the native 40% game speed during the slapshot windup, including its sound snapshot. Shooting or leaving the state ends the contribution. Losing this talent or its condition also ends it. Faster windup and faster turning are separate effects. Native slow-motion ownership and external pauses remain respected.",
        ManualGoalDenial => "Offer the native No Goal input prompt after an opposing goal, with the native replay window and timing. Each activation spends one charge and reverses that goal, its assists and pending custom goal rewards. Charges persist across a campaign; Play Now starts with the configured charges. CPU teams cannot use the manual input. The exhausted relic remains owned with zero charges and grants no automatic attribute payoff.",
        SecondScorerHelmet => "Use Spare Bucket's native campaign scorer ranking to award the second Golden Helmet. Native cumulative goals, tie-breaking and equipment updates remain active. Multiple copies share the same second helmet. Requires campaign scoring history and cannot export to Play Now.",
        TrainingMultiplier => "Multiply attribute rewards from training, including their choice previews. 150% grants one and a half times the native reward before native rounding. Custom multipliers combine with Can of Worms. Training conversion, locked attributes and recipient eligibility still apply.",
        ExtraCoachPicks => "After each Coach selection, offer another native choice until the extra picks are used. Custom picks add to Coach Coupon's native extra pick. The Coach generates each choice normally; this does not add ordinary match rewards.",
        RandomShotPower => "Roll Wild Shot's native Shot Power before each wrist shot, slapshot or bat: two random integers from 0 to 74 added together, with the chosen minimum. This replaces the native starting Shot Power; bonuses, balancing, injuries, fatigue and locks then apply normally. Accuracy is a separate effect. The last roll remains until the next shot or this condition ends.",
        RandomShotAccuracy => "Roll Wild Shot's native Accuracy before each wrist shot, slapshot or bat: a random integer from 0 to 99, with the chosen minimum. This replaces the native starting Accuracy; bonuses, balancing, injuries and fatigue then apply normally. Shot Power is a separate effect. The last roll remains until the next shot or this condition ends.",
        BorrowTalent => "While on the ice and this condition holds, borrow one random eligible talent from a benched teammate. Curses, another borrowing rule and already-owned talents are excluded. Returning to the bench, losing this rule or ending the match returns only the borrowed copy. Nothing is removed from the donor or permanently added to the run.",
        PunchPass => "When knocked out while possessing the puck, punch a pass to an eligible nearby teammate using native target selection and pass handling. This does not grant the separate upgraded receiving bonus. Native and custom copies share the same action.",
        FasterWindup => "Use the native faster slapshot windup animation while this condition holds. Shot power, faster turning and slow motion are separate effects.",
        FasterShotTurning => "Use the native faster turning time while winding up a slapshot. This does not speed up the windup animation or change match time.",
        LoadedPuckBag => "While the owner is on the ice, use a shuffled bag of one-, two- and three-goal pucks. The shared puck value applies to either team. Native puck colors, upcoming values and winning-goal prediction remain active. One native or custom queue can be active; the first active queue keeps control. Accepted goals consume one puck; denied goals keep it. This changes the team score without extra personal goals or assists.",
        WinByTwo => "Apply Tied Homie's native rule requiring the opposing team to win by at least two goals. Native match modes, score-limit continuation and both-team ownership are resolved by the game's winner calculation. This does not add goals or change goal value.",
        PossessionOvertime => "Use Last Stand's native final-whistle rule: when the clock would end the match, keep playing while the owning team retains possession, including eligible passes and buzzer shots. Losing possession ends the extension. Native clock-mode and match-ending checks still apply.",
        PhysicalBallChain => "Attach the native physical ball and chain while this condition holds. Native player/AI physics, ball collisions, faceoffs and releases remain active. One physical chain per skater, including a native chain already owned. Removing the condition or item removes only this custom attachment.",
        IgnoreInjuryPenalties => "Prevent native injuries from reducing this skater's live rating attributes while the condition holds. Injuries still exist and heal normally. Injury bonuses and extra healing games are separate components. Requires campaign injuries and cannot export to Play Now.",
        InjureArm => "Apply the native arm (Checking) injury if the target can receive it. Existing arm injuries, native prevention and disabled-injury match rules are respected. Recovery uses the native on-ice duration; requires campaign injuries.",
        RefereePursuit => "Have referees pursue and bodycheck the owning team's opponents using native target selection and its six-second refresh. Does not grant the upgraded Bonecrusher talent. The latest acquired custom referee AI rule wins over other custom referee AI rules.",
        ConvertSpeedBonuses => "Apply Quantum Sextant's native conversion of live Speed bonuses into Shot Power. Base attributes stay intact, and native balancing, locks, injuries and fatigue still apply. Multiple copies share the same conversion. Training conversion is a separate campaign effect.",
        ConvertSpeedTraining => "Convert native Speed training choices and their permanent gains to Shot Power using Quantum Sextant's training behavior. Live bonuses are unchanged by this component. Requires a campaign training event and cannot export to Play Now.",
        ReturnPass => "Prepare a one-timer back toward the sender when this skater is selected to receive a pass. Uses the native Pass Back checks, direction and team one-timer setup; does not create a puck or grant a stat bonus.",
        PunchShot => "Punch the possessed puck toward the opposing net during a qualifying knockout, using Puck Puncher's native execution. The native knockout and possession checks still apply. Its separate Shot Power bonus is not included.",
        BoardBounce => "Bounce away from the board using its actual contact normal. Strength scales native added speed and minimum rebound speed. Native reflection, angle limits and cooldown remain; only board collisions supply the required direction.",
        SoccerWall => "Create the native defensive soccer wall for the owning team during match setup. Its wall players and positioning use the native wall behavior. One wall per team; owning the native wall already satisfies this effect. The wall is removed when ownership or the match ends.",
        EnragedDuration => "Scale your team's native enraged duration when that status starts or refreshes. Other statuses are unchanged. Custom and native Short Fuse factors combine. Explicit duration overrides keep the game's existing override behavior.",
        GoldenHelmet => "Equip Golden Throne's additional Golden Helmet while this talent is owned. Native helmet appearance, eligibility and bonuses recognize it without moving the team's original Golden Helmet or granting another talent.",
        HelmetMultiplier => "Multiply native helmet effects for this owner. This includes the helmet bonuses and grants that the native Helmet Stack calculation controls. Custom factors combine with the native factor. Equipping an extra Golden Helmet is a separate effect.",
        PreferRewards => "Give this owner Coach's Favorite's native player-choice priority. The native screen still checks active-player eligibility and available choices. Native or custom exclusion takes precedence; this does not create extra rewards.",
        ExcludeRewards => "Give this owner Healthy Scratch's native exclusion from player reward choices and matching campaign event selections. Native fallback rules still apply where the game requires a minimum number of choices. Does not remove owned rewards.",
        BalanceAttributes => "Use Balanced's native averaging of live Shot Power, Accuracy, Speed and Checking. Each calculation keeps native injury, fatigue and lock handling. Does not redistribute permanent gains or rewrite saved base attributes.",
        SpreadAttributeGains => "Convert each native permanent change to one attribute into one quarter of that amount on all four attributes, using native rounding. Native stat locks still apply. This also distributes negative changes. Existing attributes and temporary bonuses are unchanged; live averaging is a separate effect.",
        LockAttribute => "Keep the selected native attribute at this value while the talent is owned. The game's attribute calculations and permanent-gain checks use the same lock. This is an owner-only Chaos effect, available only while owned; it does not rewrite other attributes or grant another talent.",
        DeflectSticks => "Use Wonder Wall's native deflection when a thrown stick contacts this skater. This does not also deflect pucks or change appearance.",
        TurningDelay => "Add milliseconds to native forward and backward turning time, as Instant Ramen does. Native limits still apply. This does not grant its separate Speed bonus.",
        ImpactKnockout => "Give this shot Destroyer Shot's direct knockout on skater contact. Native target vulnerability and shot-power-based launch force remain. It creates no explosion. Native and custom copies share the same contact outcome.",
        ImpactExplosion => "Give this shot Destroyer Shot's contact explosion with the selected radius. Native target eligibility and explosion handling remain. Multiple custom impact rules share one outcome; the explosion takes precedence over a direct hit. A native Destroyer Shot retains its own outcome.",
        ClockSpeed => "Scale the native countdown's speed while this team condition holds. 200% counts down twice as fast. Pauses and elapsed-time clocks are unchanged. Active custom multipliers combine with the native clock modifier.",
        AnchorProtection => "Use Dead Weight's native knockout protection, including its punch response. This does not remove this skater's ability to knock out others; that is a separate effect.",
        ForbidKnockouts => "Disable this skater's native CanKnockout capability, as Dead Weight does. This grants no protection from being knocked out.",
        HandPuck => "Use native referee-style handling and throwing while this skater is stickless. Does not remove a stick; select the separate stick restriction if desired. This effect only supports the stickless condition.",
        ForceOffside => "Enforce native offside rules while an eligible owner is on the ice and the condition holds. Removing this contribution leaves native settings and other offside sources intact.",
        AiBerserk => "AI seeks bodychecks using Berserk's native behavior while without possession. The stick restriction and Checking bonus are separate effects. The most recently acquired applicable custom AI choice wins.",
        InstantRecovery => "Use Springboard's native instant recovery after landing from a knockout. Native injury and brawl-finisher transitions still apply. This does not prevent the hit or its injury.",
        DeflectPucks => "Use Defensive Deflect's native collision handling to deflect opposing shots. The puck must reach this skater; possession and contact eligibility remain native. Upgraded lob height and extra puck speed are separate effects.",
        ExtraReferee => "Spawn the native second referee during normal match setup. Uses the game's referee roster and initialization. Native and custom copies share the same second referee; they do not keep adding officials.",
        RefereeBlock => "Referees use the native behavior that blocks shots from this relic owner's opponents. Their size is a separate effect. The most recently acquired applicable custom referee behavior wins.",
        RefereeSize => "Change referees' size by native size steps for this match. Native minimum and maximum still apply. This does not grant shot-blocking behavior. The latest applicable custom size rule wins; other native size contributions remain.",
        CarryingCheck => "Allow native bodychecks while carrying the puck. Other movement restrictions and bodycheck prohibitions still apply. This does not force a check to succeed.",
        GuaranteedCheck => "Use the native guaranteed-knockout bodycheck outcome. Contact, target eligibility and immunity still apply; this does not permit checking in an otherwise blocked movement state.",
        SpinOnCheck => "Use Figure Check's native spin response when bodychecked instead of the usual bodycheck knockout. Other sources of knockouts keep their normal behavior.",
        CollectOnHit => "Use Puck Sponge's native possession behavior when the puck contacts this skater. The game's pickup and contact rules still apply.",
        QuickRecovery => "Use Lifeless Branch's native quicker recovery when getting up from a knockout. This does not cancel a knockout, its injury, or a brawl finisher.",
        KickPuck => "Use the native stickless puck handling, kicking and animations when this skater has no stick. It does not remove a stick or add Dribbler's separate Shot Power bonus.",
        PostAim => "Use Ping King's native post targeting while a shot is being aimed. The rule must be active before launch; it cannot retarget an already fired shot.",
        ForceRebound => "Use Bouncy McBounce's native goalie-rebound decision for this skater's shots. This does not add its upgraded rebound-speed change. The game still decides which goalie contacts qualify.",
        FlatPuck => "Launch this shot or pass along the ice by removing its upward velocity. Later collisions still follow native puck physics.",
        ShotRecoil => "Propel the shooter backward along this shot's horizontal direction using Backdraft's native boost. Its separate fart is not included. Requires the current shot; force and duration are editable.",
        StatusMultiplier => "Extend newly applied native status effects using Status Extender's native duration multiplier. 150% makes the native duration one and a half times as long. Explicit duration overrides keep the same native override behavior; existing effects are not rewritten.",
        CooldownMultiplier => "Speed up native ability recharging while this condition holds. 150% recharges one and a half times as fast. Native pause, bench, charge-limit and ability eligibility rules remain in control. Does not spend or use an ability.",
        RandomMatchAbility => "Replace the recipient's ability with a random native skater ability until this match ends or the source item is lost. The latest custom replacement is active; removing it restores the preceding owned replacement or original ability. Later ability changes from other sources are preserved. Frogged players cannot receive this effect. This creates no custom ability.",
        DenyGoal => "Roll this chance before the game accepts a goal. Denied goals use the native no-goal result, so they cannot grant score, assists, hat tricks or custom goal bonuses. A talent can deny its owner's goal or an opponent's goal while its owner is playing. A relic protects its team. This is automatic denial; it does not add a manual reversal button.",
        ExtraGoal => "Add one goal through Hidden Ace's native scoring component. Native score, scorer and assist accounting apply, and a later native denial reverses it. This component is shared with native Hidden Ace and other custom copies, so those copies do not stack. Other native extra-goal components keep their own behavior.",
        ExtraGoals => "Each qualifying goal also adds the chosen number of extra goals, through Hidden Ace's native per-goal scoring: team score, the scorer's goals, every assist and the persistent goal statistics count each one, and a later native goal denial removes them all. Different custom Extra goals items add together; One extra goal and native Hidden Ace add their single shared goal on top. At most 9,999 extra goals are added to one goal.",
        GainTalent => "Grant an eligible random regular talent at this event and retain it in the current campaign. Uses the native Performance Bonus selection, excluding owned talents, curses and pickup-only grants. This changes the recipient's loadout and cannot be exported to Play Now.",
        UpgradeTalent => "Upgrade this many different eligible talents after the match. Native upgrade eligibility applies; custom items without an upgrade are excluded. The upgrades remain in the run.",
        UpgradeAbility => "Upgrade this many different eligible abilities after the match. Native upgrade eligibility applies. The upgrades remain in the run.",
        UpgradeRelic => "Upgrade this many different eligible team relics after the match. Native upgrade eligibility applies; custom items without an upgrade are excluded. The upgrades remain in the run.",
        KeepFreeAgents => "Prevent free agents from leaving at contract expiry while this relic is owned, using Handcuffs' native retention behavior. Contracts are not rewritten; without this relic, expired contracts can end normally.",
        BenchMultiplier => "Scale earned native bench attribute bonuses. 200% doubles them. A talent can scale its owner's bench bonus, or help the team while its owner is playing. A relic scales the team's bench bonuses. Native bench eligibility still applies.",
        PassUninterceptable => "Make this pass uninterceptable by opposing skaters using the native pass type. Teammates can receive it normally. Ends with this pass; does not affect a later pass.",
        GoalieHeadshot => "Send this free rebound toward the original shooter's head using the native targeted-puck coroutine. Requires a current opposing shot and a skater shooter. Strength scales the rebound speed.",
        BonusMultiplier => "Scale the selected attribute's bonuses, not its base value. 150% adds half of the bonus again. Applies before native injuries and fatigue; stat locks still apply. Multiple custom multipliers multiply together.",
        CritMultiplier => "Scale the final critical-shot chance, including native and custom bonuses. 200% doubles it. Multiple custom multipliers multiply together.",
        InjuryMultiplier => "Scale the native probability of a new on-ice injury to the selected team. 200% doubles it. Native immunity and injury eligibility still apply. Does not create injuries during campaign events.",
        InjuryHealing => "Add or subtract games from each new injury's recovery time, including campaign-event injuries. At least one game remains. Existing injuries are unchanged; this does not apply Masochist's separate attribute bonus.",
        ClockTime => "Add or remove time on the current match clock. This cannot affect a completed match.",
        Injure => "Apply one eligible random injury using the native injury system. Native prevention still applies; injuries retain their normal healing rules.",
        Heal => "Heal the recipient's current injuries through the native healing system.",
        ThrowStick => "Throw the selected skater's stick in their facing direction. A stickless player cannot throw one.",
        DropHelmet => "Drop a removable helmet through the native equipment system.",
        TalentRewards => "Add this many talent reward selections at the end of a match while this item is owned.",
        RelicRewards => "Add this many relic reward selections at the end of a match while this item is owned.",
        Rerolls => "Add rerolls on the selected reward or map-event screen while this item is owned.",
        NetWidth => "Set both goals' width during match setup as a percentage of their original width. Native Soccer Net uses 135%. Height and appearance are separate effects. The most recently acquired applicable custom width rule wins.",
        NetHeight => "Set both goals' height during match setup as a percentage of their original height. Native Soccer Net uses 122%. Width and appearance are separate effects. The most recently acquired applicable custom height rule wins.",
        SoccerNetLook => "Use the native soccer-net material on both goals during match setup. Dimensions and the rink surface are separate effects.",
        CopyFreeAgentStats => "Replace the recipient's four base attributes with those of a random unlocked free agent at the start of a new run. Keep their identity, appearance and owned powers. The native free-agent eligibility rules apply.",
        CopyFreeAgentLook => "Adopt a random unlocked free agent's face, equipment, surname and number at the start of a new run. Keep the recipient's first name, identity, attributes, position, size and owned powers. This does not also apply Faceless's oversized helmet or size change.",
        Freeze => "Freeze the selected skaters using the game's frozen movement state for the chosen duration. Native animation, puck loss and thawing remain active.",
        Dance => "Make the selected skaters dance for the chosen duration. Only skaters that can currently move can enter this state.",
        CatchSaves => "Catch eligible saves with the native Infinimitt behavior. The puck must hit the goalie during a save; native pickup restrictions and excluded save animations still apply. This does not stop shots that miss the goalie.",
        GoalieFetchSpeed => "Increase the goalie's speed while retrieving a free puck. Strength is a percentage of The Falcon's native +6 retrieval-speed bonus. This does not change normal positioning speed.",
        GoalieThrow => "Use Tomahawk's native stick-throwing decisions while the condition holds. The native timer, chance, range, facing and stick checks still apply. The goalie throws through its normal animation and state transition.",
        CopyGoalieAbility => "Replace this goalie's ability with a copy of the opposing goalie's current ability at this event. Keep the copied ability through the native loadout path; no change if the opponent has none. Talents are not copied by this effect.",
        CopyGoalieTalents => "Add copies of the opposing goalie's eligible talents at this event. Native curses, hidden talents and Musical Nets are excluded. Existing native talent types and existing custom talent IDs are not duplicated. The ability is not copied by this effect.",
        ForbidStick => "Prevent the selected skaters from using or picking up sticks while this condition holds. Remove only this item's restriction when it ends; other native restrictions remain.",
        ExtraSticks => "Add reserve sticks while this condition holds, using the native stick inventory and graphics. Remove only this item's contribution when it ends.",
        ForbidCheck => "Prevent the selected skaters from initiating bodychecks while this condition holds. This does not grant immunity to being hit.",
        RepelPuck => "Apply Puck Repellent's native distance-based puck force around each selected skater while the condition holds. Native range, force and game-mode rules apply.",
        _ when IsInjuryPrevention(effect) => "Prevent the selected new injury while the item is owned. This follows the native injury eligibility check, including campaign event injuries, and does not heal existing injuries.",
        _ when IsShockwave(effect) => "Create the native shockwave around the triggering player, affecting only the selected object type. Force is a percentage of native strength; radius is in game units. Other owned fart bonuses can modify the wave as usual.",
        _ when IsRink(effect) => "Select the native surface during match setup. Rink effects belong to team relics. The most recently acquired active rink relic takes priority for that match.",
        _ when effect == AiKarlson || effect == AiPrince || effect == AiKnight => LifecycleHelp(effect),
        _ when AiEffects.Contains(effect) => "Use this native positioning behavior while the chosen condition holds. It affects AI control only. The most recently acquired applicable custom item takes priority. Normal possession and match-state rules still apply.",
        _ => LifecycleHelp(effect)
    };
    private static string BriefQueryDescription(CustomRewardDefinition d)
    {
        string team = d.Target == OpposingTeam ? "the opposing team" : "your team";
        string owner = d.IsGoalie ? "this goalie" : "this skater";
        string skaters = d.Target == Actor ? owner : d.Target == OpposingTeam ? "opposing skaters" : "your skaters";
        string opponent = d.Target == OpposingTeam ? "your team" : "opponents";
        if (d.Trigger == "Puck fart timer") return "Every " + d.Seconds + "s, the puck releases " + Summary(d).ToLowerInvariant() + ".";
        if (d.Trigger == "Random fart timer") return "Every " + d.Seconds + "–" + d.IntervalMaxSeconds + "s: " + Summary(d) + " around " + owner + ".";
        if (CustomRewardTriggers.IsShotCalculation(d.Trigger)) return Capital(RecipientText(d, owner)) + ": " + Summary(d) + " for this shot (" + d.Trigger.Replace(" calculation", "").ToLowerInvariant() + ").";
        if (IsCultist(d.Effect)) return "AI casts " + d.Effect.ToLowerInvariant().Replace("cultist ", "") + " during this skater's spell cycle.";
        if (IsTransferHelmet(d.Effect)) return "Equip " + d.Effect.Replace(" attribute", "") + ": " + (d.Amount >= 0 ? "+" : "") + d.Amount + " " + d.Stat + " for its current wearer.";
        if (d.Effect == ExtraGoal || d.Effect == ExtraGoals) {
            string goals = d.Trigger switch {
                "Goal value calculation" => "Each goal",
                "Critical goal value calculation" => "Each critical goal",
                "Enraged goal value calculation" => "Each goal scored while enraged",
                "Injured goal value calculation" => "Each goal scored while injured",
                "First goal value calculation" => "The match's opening goal",
                "Team first goal value calculation" => "The first goal each match",
                _ => "Each goal in the " + d.Trigger.Replace(" goal value calculation", "").ToLowerInvariant()
            };
            string scorer = d.Target == Actor ? owner : team;
            string subject = d.Trigger == "First goal value calculation" ? "If " + scorer + " scores the match's opening goal, it"
                : d.Trigger == "Team first goal value calculation" ? d.Target == Actor ? "If " + owner + " scores your team's first goal of the match, it" : Capital(team) + "'s first goal each match"
                : goals + " by " + scorer;
            return subject + " counts as " + (d.Effect == ExtraGoal ? 2 : (long)System.Math.Min(d.Amount, 9999) + 1) + " goals. "
                + (d.Effect == ExtraGoal ? "Copies do not stack." : "Extra goals stack; goals and assists count each one.");
        }
        if (IsCategoryGrant(d.Effect)) {
            string category = d.Effect == GrantSpeedTalent ? "Speed" : d.Effect == GrantCheckingTalent ? "Checking" : d.Effect == GrantDefenseTalent ? "defensive" : d.Effect == GrantShootingTalent ? "shooting" : "slapshot";
            return "Each skater gains a " + category + " talent " + (d.Effect == GrantSlapshotTalent ? "at the start of the run" : "when acquired") + " (level " + d.Amount + " where available).";
        }
        if (IsRink(d.Effect)) return "Play on the " + d.Effect.ToLowerInvariant() + ". Latest acquired rink relic wins.";
        if (IsInjuryPrevention(d.Effect)) return d.Effect + " for " + skaters + ". Existing injuries remain.";
        return d.Effect switch {
            PickupAttribute => "Permanently gain " + (d.Amount >= 0 ? "+" : "") + d.Amount + " " + d.Stat + " when acquired. Consumes the talent.",
            BossAttribute => "Permanently gain " + (d.Amount >= 0 ? "+" : "") + d.Amount + " " + d.Stat + " after " + (d.Trigger == "Boss won" ? "each boss win" : "the " + d.Trigger.ToLowerInvariant().Replace("won", "win")) + ".",
            BossSize => "Permanently change size by " + (d.Amount >= 0 ? "+" : "") + d.Amount + " steps after " + (d.Trigger == "Boss won" ? "each boss win" : "the " + d.Trigger.ToLowerInvariant().Replace("won", "win")) + ".",
            AlternateOverall => "Alternate +" + d.Amount + " / -" + d.Amount + " Overall after each completed match, starting positive.",
            RetainPowers => "Eligible benchwarmers retain up to " + d.Amount + " random powers for the next run. Starting powers are excluded.",
            DefenseTeleport => "Teleport back when the opponent gains possession.",
            LiquidateTalents => "When acquired, remove eligible talents and save their count. Quest talents and Liquidation carriers stay.",
            UpgradeOwnTalents => "Upgrade all eligible talents on this skater when acquired.",
            ScorelessExpiry => "Lose this talent after a completed match without scoring.",
            RandomMatchAttribute => "+" + d.Amount + " to a random attribute after each match, with Gauntlet scaling. Bonuses last while owned.",
            CenterDefenderChecks => "While playing centre, lend Bonecrusher level " + d.Amount + " to LD and RD.",
            FogScoreboard => "Glitch the scoreboard while this human-controlled skater has the puck.",
            ReboundCriticals => "+" + d.Amount + "% critical chance if a teammate shot in the last " + d.Seconds + "s.",
            HungerRage => "Enrage after " + d.Seconds + "s without the puck. Picking it up ends enrage and resets the timer.",
            GoldenHelmet => "Equip an extra Golden Helmet.",
            HelmetMultiplier => "This skater's helmet effects × " + d.Amount + ".",
            PreferRewards => "Prioritize this skater for player rewards. Reward exclusions take priority.",
            ExcludeRewards => "Exclude this skater from player rewards where other eligible choices exist.",
            SpreadAttributeGains => "Split each permanent attribute gain or loss equally across all four attributes.",
            PlayedMatchContract => "Leave after " + d.Amount + " played matches. Benched matches do not count; Handcuffs prevents departure.",
            LoadedPuckBag => Summary(d) + ". While on the ice, puck values affect either team. Adds team score only; denied goals keep the puck. First active bag wins.",
            AngusZero => "Angus has 0 Overall.",
            OpposingBonecrusher => "Opposing skaters gain Bonecrusher for the match.",
            OpposingBriefcase => "The opposing team gains Briefcase's goal reversal for the match.",
            FreezeOwnRelics => "Freeze two eligible relics on your team for the match.",
            FrogOwnPair => "Two of your skaters become frogs for the match.",
            MinimumTeamSize => "Your skaters shrink to the smallest size for the match.",
            BlueLineLaunch => "Launch opponents when they cross either blue line (force " + d.Amount + ").",
            DefensiveBlueLineKnockout => "Knock out opposing puck carriers for " + d.Seconds + "s when they cross your blue line.",
            OffensiveBlueLineKnockout => "Knock out your puck carrier for " + d.Seconds + "s when they cross the opposing blue line.",
            KnockoutDuration => "Opponents stay knocked out for " + d.Amount + "% of the normal duration.",
            ParticipationBonus => "Skaters who sat out the entire match get +" + d.Amount + " Overall after each win, while this relic is owned.",
            BreakawayShotPenalty => "Opponents shoot at half strength on breakaways.",
            BreakawayShotFall => "Opponents stumble after shooting on a breakaway.",
            OpeningInjuries => "Your team's first " + d.Amount + " qualifying knockouts each match cause injury attempts.",
            CyclicInjuries => "Every " + d.Amount + " qualifying team knockouts cause an injury attempt. Resets each match.",
            PuckInjuries => "Puck-caused knockouts have a " + d.Amount + "% injury chance.",
            VoodooAttribute => "One random playing opponent gets -" + d.Amount + " Overall for the match.",
            HotAttribute => "One random playing skater gets +" + d.Amount + " Overall for the match. A different skater receives any Cold penalty.",
            ColdAttribute => "One random playing skater gets -" + d.Amount + " Overall for the match. A different skater receives any Hot bonus.",
            OpeningCriticals => "Your team's first " + d.Amount + " shots each match are critical.",
            HealedInjuryBonus => "Skaters gain " + (d.Amount >= 0 ? "+" : "") + d.Amount + " Overall per healed injury while this relic is owned.",
            CelebrationInjuries => "Knocking out a celebrating opposing scorer can injure them.",
            BenchCapacity => "Add one bench slot when acquired.",
            BenchTalentSlot => "The Comfy Cushion bench slot awards a random talent. Requires that slot.",
            DonateBenchAttribute => "When acquired, take all " + d.Stat + " from up to two random benchwarmers and divide it among the other skaters.",
            DonateBenchTalents => "When acquired, move non-quest talents from up to two random benchwarmers to other skaters.",
            UpgradeAngusTalents => "Upgrade Angus's eligible talents when acquired.",
            UpgradeAngusAbility => "Upgrade Angus's eligible ability when acquired.",
            GrantSlapshotTalent => "Each eligible skater gains a slapshot talent at the start of the run.",
            ShareTalent => "At match start, loan one teammate's eligible talent to compatible teammates for the match.",
            FrogHelmets => "Opposing Golden Helmet wearers become frogs.",
            OctopusAttacks => "Tentacles attack advancing opposing puck carriers. Strikes hit either team within " + d.Radius + " units.",
            PostGoal => "A shot off the opposing post can score once per match.",
            FartForce => "Your team's fart force × " + (d.Amount / 100f) + ".",
            FartRange => "Your team's fart range × " + (d.Amount / 100f) + ".",
            FartKnockouts => "Fart bursts knock out " + (d.Target == BothTeams ? "skaters on both teams" : "opponents") + " in range.",
            CopyOpponentRelics => "Copy one eligible opposing relic for the match; two on Expert. Opponents keep theirs.",
            StealOpponentRelics => "Steal up to two eligible opposing relics for the match.",
            FreezeOpponentRelic => "Disable one eligible opposing relic for the match.",
            GoalPlanks => "Defensive planks protect your goal.",
            BreakawayAd => "Show an ad over the rink during opposing breakaways.",
            PossessionExplosion => "Carrying the puck for " + d.Seconds + "s knocks out its carrier on either team. Passing resets the timer.",
            FasterChecks => "A successful knockout speeds up the rest of that bodycheck animation 4×.",
            SpeedOneTimerAccuracy => "Speed bonuses also add to one-timer Accuracy.",
            AttributeRewardChoice => "Talent rewards include an additional attribute choice.",
            RiggedFaceoffs => Capital(team) + " wins faceoffs. Opposing faceoff effects and referee-team rules still apply.",
            PlayableReferees => "Referees join your team and can be controlled. Latest acquired custom recruitment wins.",
            ManualGoalDenial => d.Amount + " manual goal reversals during replays. Charges last across the run; unusable at zero charges. Human teams only.",
            SecondScorerHelmet => "Your team's second-highest scorer also gets a Golden Helmet. Copies share one helmet.",
            TrainingMultiplier => "Training attribute rewards × " + (d.Amount / 100f) + ".",
            ExtraCoachPicks => "+" + d.Amount + " " + (d.Amount == 1 ? "pick" : "picks") + " at each Coach.",
            WinByTwo => Capital(opponent) + " must win by at least two goals.",
            PossessionOvertime => "Keep playing after the buzzer while " + team + " has possession, including eligible passes and buzzer shots.",
            ConvertSpeedBonuses => "Speed bonuses become Shot Power bonuses. Base attributes stay intact.",
            ConvertSpeedTraining => "Speed training becomes Shot Power training.",
            SoccerWall => "A soccer wall defends your goal. One wall per team.",
            EnragedDuration => Capital(team) + "'s enraged duration × " + (d.Amount / 100f) + ".",
            ExtraReferee => "Add a second referee. Copies share the same referee.",
            RefereeBlock => "Referees try to block opposing shots. Latest acquired custom referee behavior wins.",
            RefereePursuit => "Referees bodycheck opponents. The latest acquired custom referee behavior wins.",
            RefereeSize => "Referee size " + (d.Amount >= 0 ? "+" : "") + d.Amount + " steps for the match. Latest custom size rule wins.",
            NetWidth => "Both goals are " + d.Amount + "% of their normal width. Latest custom width rule wins.",
            NetHeight => "Both goals are " + d.Amount + "% of their normal height. Latest custom height rule wins.",
            SoccerNetLook => "Both goals look like soccer nets.",
            DenyGoal => d.Amount + "% chance to deny " + (d.Target == Actor ? "this skater's goal" : "an opposing goal") + " before it counts." + (d.IsRelic ? "" : " Active while this skater plays."),
            InjuryMultiplier => Capital(skaters) + "' injury chance × " + (d.Amount / 100f) + ". Injury prevention still applies.",
            InjuryHealing => "New injuries to " + skaters + " take " + d.Amount + " extra recovery games (minimum one). Existing injuries are unchanged.",
            KeepFreeAgents => "Your free agents never leave while this relic is owned.",
            BenchMultiplier => Capital(d.Target == Actor ? owner : team) + "'s bench bonuses × " + (d.Amount / 100f) + "." + (!d.IsRelic && d.Target == Team ? " Active while this skater plays." : ""),
            CopyFreeAgentStats => "At run start, " + (d.Target == Actor ? owner : "each skater") + " copies a random unlocked free agent's four base attributes.",
            CopyFreeAgentLook => "At run start, " + (d.Target == Actor ? owner : "each skater") + " copies a random unlocked free agent's face, gear, surname and number.",
            TalentRewards => "+" + d.Amount + " talent reward " + (d.Amount == 1 ? "choice" : "choices") + " after each match.",
            RelicRewards => "+" + d.Amount + " relic reward " + (d.Amount == 1 ? "choice" : "choices") + " after each match.",
            Rerolls => "+" + d.Amount + " rerolls " + (d.Trigger == "Map event choices" ? "at map events" : "when choosing rewards") + ".",
            _ => QueryDescription(d)
        };
    }

    private static string QueryDescription(CustomRewardDefinition d) => d.Trigger == "Random fart timer" ? "Every " + d.Seconds + " to " + d.IntervalMaxSeconds + " seconds: " + Summary(d) + " around this skater. Native timer pauses and fart interactions apply; only the selected interaction is enabled."
        : d.Trigger == "Puck fart timer" ? "Every " + d.Seconds + " seconds: " + Summary(d) + " around the puck. Uses the native puck warning color, pause handling and cleanup; only the selected interaction is enabled."
        : d.Effect == PickupAttribute || d.Effect == BossAttribute ? (d.Amount > 0 ? "+" : "") + d.Amount + " " + d.Stat + " at " + d.Trigger.ToLowerInvariant() + ". " + Help(d.Effect)
        : d.Effect == BossSize ? (d.Amount > 0 ? "+" : "") + d.Amount + " size steps at " + d.Trigger.ToLowerInvariant() + ". " + Help(d.Effect)
        : d.Effect == AlternateOverall ? "Alternate +" + d.Amount + " / -" + d.Amount + " Overall. " + Help(d.Effect)
        : d.Effect == RetainPowers ? "Retain up to " + d.Amount + " powers. " + Help(d.Effect)
        : d.Effect == OctopusAttacks ? "Strike radius: " + d.Radius + ". " + Help(d.Effect)
        : d.Effect == FartForce || d.Effect == FartRange ? d.Amount + "% of native interaction. " + Help(d.Effect)
        : d.Effect == FartKnockouts ? "Fart knockouts affect " + d.Target.ToLowerInvariant() + ". " + Help(d.Effect)
        : IsCategoryGrant(d.Effect) ? "Grant level " + d.Amount + " talents where that upgrade exists. " + Help(d.Effect)
        : d.Effect == DonateBenchAttribute ? "Transfer " + d.Stat + ". " + Help(d.Effect)
        : d.Effect == BlueLineLaunch ? "Launch force " + d.Amount + ". " + Help(d.Effect)
        : IsBlueLineKnockout(d.Effect) ? d.Seconds + " seconds knockout duration. " + Help(d.Effect)
        : d.Effect == KnockoutDuration ? d.Amount + "% of native knockout time. " + Help(d.Effect)
        : d.Effect == ParticipationBonus ? "+" + d.Amount + " Overall per qualifying win. " + Help(d.Effect)
        : d.Effect == OpeningInjuries ? "First " + d.Amount + " qualifying knockouts. " + Help(d.Effect)
        : d.Effect == CyclicInjuries ? "Every " + d.Amount + " qualifying knockouts. " + Help(d.Effect)
        : d.Effect == PuckInjuries ? d.Amount + "% chance. " + Help(d.Effect)
        : d.Effect == VoodooAttribute || IsTemperature(d.Effect) ? (d.Effect == HotAttribute ? "+" : "-") + d.Amount + " Overall. " + Help(d.Effect)
        : d.Effect == OpeningCriticals ? "First " + d.Amount + " team shots each match. " + Help(d.Effect)
        : d.Effect == ReboundCriticals ? "+" + d.Amount + "% critical chance within " + d.Seconds + " seconds. " + Help(d.Effect)
        : d.Effect == HealedInjuryBonus ? (d.Amount > 0 ? "+" : "") + d.Amount + " Overall per healed injury. " + Help(d.Effect)
        : d.Effect == CelebrationInjuries ? Help(d.Effect)
        : IsTransferHelmet(d.Effect) ? d.Effect + ": " + (d.Amount > 0 ? "+" : "") + d.Amount + " " + d.Stat + ". " + Help(d.Effect)
        : d.Effect == RandomMatchAttribute ? "+" + d.Amount + " per random attribute roll. " + Help(d.Effect)
        : d.Effect == CenterDefenderChecks ? "Loan Bonecrusher level " + d.Amount + ". " + Help(d.Effect)
        : IsCultist(d.Effect) || d.Effect == BenchTalentSlot || IsLifecycleEffect(d.Effect) ? Help(d.Effect)
        : IsOpponentRelicEffect(d.Effect) ? Help(d.Effect)
        : d.Effect == GoalPlanks || d.Effect == BreakawayAd ? Help(d.Effect)
        : d.Effect == HungerRage || d.Effect == PossessionExplosion ? "After " + d.Seconds + " seconds. " + Help(d.Effect)
        : d.Effect == FasterChecks || d.Effect == SpeedOneTimerAccuracy || d.Effect == AttributeRewardChoice ? Help(d.Effect)
        : d.Effect == PlayedMatchContract ? "Leave after " + d.Amount + " played matches. " + Help(d.Effect)
        : d.Effect == ManualGoalDenial ? d.Amount + " manual goal-reversal charges. " + Help(d.Effect)
        : d.Effect == SecondScorerHelmet || d.Effect == RiggedFaceoffs ? Help(d.Effect)
        : d.Effect == TrainingMultiplier ? "Training attributes × " + (d.Amount / 100f) + ". " + Help(d.Effect)
        : d.Effect == ExtraCoachPicks ? "At each Coach, " + d.Amount + " additional picks. " + Help(d.Effect)
        : d.Effect == LoadedPuckBag ? Summary(d) + ". " + Help(d.Effect)
        : CustomRewardTriggers.IsShotCalculation(d.Trigger)
        ? (d.Target == Team ? "Your skaters gain " : "This skater gains ") + Summary(d) + " during " + d.Trigger.ToLowerInvariant() + ", before this shot is launched. This calculation does not create a lasting bonus."
        : d.Effect == WinByTwo || d.Effect == PossessionOvertime || d.Effect == ConvertSpeedBonuses || d.Effect == ConvertSpeedTraining ? Help(d.Effect)
        : d.Effect == SoccerWall ? "At match setup, " + Help(d.Effect)
        : IsRefereeSetup(d.Effect) ? "At match setup, " + d.Effect.ToLowerInvariant() + (d.Effect == RefereeSize ? " " + (d.Amount > 0 ? "+" : "") + d.Amount + " steps" : "") + ". " + Help(d.Effect)
        : IsRink(d.Effect)
        ? "Use the " + d.Effect.ToLowerInvariant() + " when setting up a match while this item is owned."
        : IsNet(d.Effect) ? (d.Effect == SoccerNetLook ? "Apply soccer-net appearance to both goals" : "Set both goals' " + (d.Effect == NetWidth ? "width" : "height") + " to " + d.Amount + "% of their original size") + " at match setup while this relic is owned. " + FlowHelp(d.Effect)
        : d.Effect == EnragedDuration ? "Your team's native enraged duration × " + (d.Amount / 100f) + ". " + Help(d.Effect)
        : d.Effect == HelmetMultiplier ? "This skater's native helmet effects × " + d.Amount + ". " + Help(d.Effect)
        : d.Effect == GoldenHelmet || d.Effect == PreferRewards || d.Effect == ExcludeRewards ? Help(d.Effect)
        : d.Effect == SpreadAttributeGains ? "For this skater, " + Help(SpreadAttributeGains)
        : d.Effect == DenyGoal ? "At goal verification, " + d.Amount + "% chance to deny " + (d.Target == Actor ? "this skater's goal" : "an opposing goal") + ". " + Help(DenyGoal)
        : d.Effect == ExtraGoal ? "At " + d.Trigger.ToLowerInvariant() + ", add one goal for " + (d.Target == Actor ? "this skater" : d.Target == OpposingTeam ? "the opposing team" : "your team") + ". " + Help(ExtraGoal)
        : d.Effect == ExtraGoals ? "At " + d.Trigger.ToLowerInvariant() + ", each goal by " + (d.Target == Actor ? "this skater" : d.Target == OpposingTeam ? "the opposing team" : "your team") + " counts as " + ((long)d.Amount + 1) + " goals (+" + d.Amount + "). " + Help(ExtraGoals)
        : d.Effect == InjuryMultiplier ? "While this relic is owned, scale new on-ice injury chance for the " + d.Target.ToLowerInvariant() + " to " + d.Amount + "% of its native value. Native prevention still applies."
        : d.Effect == InjuryHealing ? "While owned, adjust each new injury's recovery for " + (d.Target == Team ? "your skaters" : "this skater") + " by " + d.Amount + " games (minimum one). Existing injuries are unchanged."
        : d.Effect == KeepFreeAgents ? Help(KeepFreeAgents)
        : d.Effect == BenchMultiplier ? "Scale " + (d.Target == Actor ? "this skater's" : "your team's") + " native bench attribute bonuses to " + d.Amount + "%. " + Help(BenchMultiplier)
        : IsInjuryPrevention(d.Effect) ? d.Effect + " for " + (d.IsRelic || d.Target == Team ? "your skaters" : "this skater") + " while this item is owned. Existing injuries remain."
        : IsFreeAgentCopy(d.Effect) ? "At the start of a new run, " + (d.Target == Team ? "your skaters each " : "this skater ") + d.Effect.ToLowerInvariant() + ". " + FlowHelp(d.Effect)
        : "While owned, give your team " + d.Amount + " " + d.Effect.ToLowerInvariant().Replace("extra ", "additional ") + " at " + d.Trigger.ToLowerInvariant() + ".";
}
