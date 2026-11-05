using CampaignCore;
using EndlessMode;
using Skaters;
using UnityEngine;

int checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    checks++;
}

var opponent = new[] { new Forward("MrBeast", "YouTubers"), new Forward("PewDiePie", "YouTubers") };
var a = new Forward("Cinderella", "Funtime");
var b = new Forward("Humpty Dumpty", "Funtime");
var players = opponent.Concat(new[] { a, b }).ToArray();
foreach (var f in players) CustomRewardRuntime.BindForTest(f);
var ledger = CustomRewardRuntime.Ledger;
var buffering = new CustomRewardDefinition {
    Name = "Buffering", Kind = CustomRewardDefinition.CustomRelics, Trigger = "Opponent pass",
    Effect = CustomRewardEffects.StatChange, Target = CustomRewardEffects.Opponent, Stat = "Speed",
    Amount = -60, Duration = "Seconds", Seconds = 40, MaxStacks = 1, ActivationsPerMatch = CustomRewardDefinition.Unlimited
};
CustomRewardRuntime.InPlay = new[] { buffering };
void Sweep() => CustomRewardRuntime.SweepForTest();
void Award(CustomRewardDefinition d, Forward f, string owner = "team:YouTubers")
{
    ledger.Signal(d, d.Trigger, owner, owner, CustomRewardProgress.ScopedRecipient("player:" + f.Id, "team:" + f.TeamId), Time.time);
    Sweep();
}
foreach (var f in players) { f.stats.Updates = 0; f.stats.Reads = 0; }
for (int i = 0; i < 100; i++) Sweep();
Check(players.All(f => f.stats.Updates == 0 && f.stats.Reads == 0), "An idle event-only roster should issue no native refresh or getter queries");
for (int pass = 0; pass < 11; pass++) { Time.time = pass; Award(buffering, a); }
Check(a.stats.Updates == 1 && a.stats.GetSpeedCached() == 20 && players.Where(f => f != a).All(f => f.stats.Updates == 0), "Repeated Buffering must only refresh its passing opponent once");
Time.time = 49.9f; Sweep(); Check(a.stats.Updates == 1, "Renewed Buffering lasts until last pass plus 40 seconds");
Time.time = 50; Sweep(); Check(a.stats.Updates == 2 && a.stats.GetSpeedCached() == 80, "Exact timed expiry restores only its recipient");
Time.time = 60; Award(buffering, b);
CustomRewardRuntime.Owned = false; Sweep(); Check(b.stats.GetSpeedCached() == 80 && b.stats.Updates == 2, "Suppression and removal restore a still-timed effect");
CustomRewardRuntime.Owned = true; Sweep(); Check(b.stats.GetSpeedCached() == 20 && b.stats.Updates == 3, "Restoring ownership reapplies the active bonus");
ledger.Clear(); Sweep(); Check(b.stats.GetSpeedCached() == 80 && b.stats.Updates == 4, "A reset must clear the last cached contribution");
var permanent = new CustomRewardDefinition { Stat = "Accuracy", Kind = CustomRewardDefinition.CustomRelics, Amount = 10, Duration = "Rest of run", Trigger = "Goal" };
Award(permanent, a); Check(a.stats.GetAccuracyCached() == 90, "Run bonus takes effect on its intended recipient");
ledger.EndPeriod(); Sweep(); Check(a.stats.GetAccuracyCached() == 90, "Run bonuses survive the period boundary");
var serialized = ledger.Save(); ledger.Clear(); ledger.Load(serialized); ledger.BeginMatch();
Sweep(); Check(a.stats.GetAccuracyCached() == 90, "A restored run ledger keeps its cached stat");
var dynamic = new CustomRewardDefinition { Trigger = "While with puck", Stat = "Speed", Effect = CustomRewardEffects.StatChange };
CustomRewardRuntime.InPlay = new[] { dynamic };
a.DynamicSpeed = 7; Sweep(); Check(a.stats.GetSpeedCached() == 87, "Live conditional speed reacts");
a.DynamicSpeed = 0; Sweep(); Check(a.stats.GetSpeedCached() == 80, "Ending conditional speed restores the native cache");
var transform = new CustomRewardDefinition { Trigger = "While with puck", Stat = "Speed", Effect = CustomRewardEffects.BonusMultiplier };
CustomRewardRuntime.InPlay = new[] { transform };
a.Transform = true; a.NativeAdjustment = 5; Sweep(); Check(a.stats.GetSpeedCached() == 85, "Conditional native scaling updates speed");
int last = a.stats.Updates; Sweep(); Check(a.stats.Updates == last, "Unchanged native transformed attributes require no refresh");
a.stats.slowmoSpeedScale = 0.5f; Sweep(); Check(a.stats.Updates == last, "Native slow motion scales the getter, not the stored cache, and must not cause repeated refreshes");
a.NativeAdjustment = 9; Sweep(); Check(a.stats.GetSpeedCached() == 89, "An active transform observes changing native inputs");
a.Transform = false; Sweep(); Check(a.stats.GetSpeedCached() == 80, "Removing the transform restores cached speed");
var average = new CustomRewardDefinition { Trigger = "While with puck", Effect = CustomRewardEffects.BalanceAttributes };
CustomRewardRuntime.InPlay = new[] { average };
a.Transform = true; a.NativeAdjustment = 4; a.stats._isBalanced = true;
int beforePhysics = a.stats.Updates;
int beforeReads = a.stats.Reads;
CustomRewardRuntime.PeriodicForTest(a);
Check(a.stats.Updates == beforePhysics && a.stats.Reads == beforeReads, "A regular tick must leave enabled native forwards to their own FixedUpdate");
a.stats.FixedUpdate();
Check(a.stats.GetSpeedCached() == 84, "Native physics evaluates averaging and current inputs before the first simulation step");
a.isActiveAndEnabled = false; a.NativeAdjustment = 6;
CustomRewardRuntime.PeriodicForTest(a); Check(a.stats.GetSpeedCached() == 86 && a.stats.Updates == beforePhysics+1, "Disabled forwards retain a targeted periodic fallback for dynamic transforms");
Check(opponent.All(f => f.stats.Updates == 0), "Unchanged teammates and opponents should never receive cache updates");
a.Transform = false; a.stats._isBalanced = false;
CustomRewardRuntime.InPlay = new[] {
    new CustomRewardDefinition { Trigger = "While with puck", Effect = CustomRewardEffects.ChargeAccuracy },
    new CustomRewardDefinition { Effect = CustomRewardEffects.HotAttribute }
};
a.CapturedAccuracy = 8; CustomRewardRuntime.PeriodicForTest(a);
Check(a.stats.GetAccuracyCached() == 98, "Charge captured before release still reaches accuracy after possession changes");
a.CapturedAccuracy = 0; a.Temperature = -3; CustomRewardRuntime.PeriodicForTest(a);
Check(a.stats.GetAccuracyCached() == 87 && a.stats.GetSpeedCached() == 77, "Finished charge and changing temperature affect the proper cached stats");
Console.WriteLine($"PASS {checks} stat refresh checks covering Buffering, expiry, ownership, restored bonuses, transforms, native physics, captured charge and temperature.");
