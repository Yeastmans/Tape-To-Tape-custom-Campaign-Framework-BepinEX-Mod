using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace CampaignCore;

// Campaign copies are self-contained. Library edits never change another campaign.
public sealed class CustomRewardDefinition
{
    public const string Folder = "custom_rewards", Extension = ".reward";
    public const string CustomTalents = "Custom Talents", CustomChaosTalents = "Custom Chaos Talents", CustomGoalieTalents = "Custom Goalie Talents", CustomRelics = "Custom Relics";
    public static readonly string[] Kinds = { CustomTalents, CustomChaosTalents, CustomGoalieTalents, CustomRelics };
    public static readonly string[] Triggers = CustomRewardTriggers.All.Select(t => t.Name).ToArray();
    public static readonly string[] Stats = { "Shot Power", "Accuracy", "Speed", "Checking", "Overall", "Critical chance", "Critical shot power", "Pass speed", "Size", "Goalie skill", "Goalie recovery", "Goalie standing speed", "Goalie butterfly speed", "Goalie depth" };
    public static bool IsOverallStat(string stat) => stat == "Shot Power" || stat == "Accuracy" || stat == "Speed" || stat == "Checking";
    public static readonly string[] Targets = { "Triggering player", "Whole team" };
    public static readonly string[] Durations = { "Rest of run", "Rest of match", "Rest of period", "Seconds" };
    // Activations and stacks have no authored ceiling. The largest int is the
    // persisted "unlimited" value, so older runtimes read it as a huge cap.
    public const int Unlimited = int.MaxValue;
    public static bool IsUnlimited(int count) => count == Unlimited;
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public string Kind { get; set; } = CustomTalents;
    public string Description { get; set; } = "";
    public string Trigger { get; set; } = "Hat trick";
    public string Effect { get; set; } = CustomRewardEffects.StatChange;
    public int Radius { get; set; } = 5;
    public int TargetCount { get; set; } = 1;
    public int ShotDelayMilliseconds { get; set; } = 560;
    public int NormalPucks { get; set; } = 9;
    public int DoublePucks { get; set; } = 2;
    public int TriplePucks { get; set; } = 1;
    public string ResetOn { get; set; } = "";
    public string AmountBasis { get; set; } = "Fixed amount";
    public static readonly string[] AmountBases = { "Fixed amount", "Per other owned talent", "Per injured playing teammate", "Per goal trailing", "Per knocked-out skater on either team", "Per goal in the previous match", "Per knockout in the previous match", "If benched for the entire previous match", "Per liquidated talent" };
    public static bool IsPreviousMatchBasis(string basis) => AmountBases.Skip(5).Take(3).Contains(basis);
    public static IEnumerable<string> AmountBasesFor(string kind) => kind == CustomTalents ? AmountBases.Take(3).Concat(AmountBases.Skip(5).Take(3))
        : kind == CustomChaosTalents ? AmountBases.Take(2).Concat(new[] { "Per liquidated talent" }) : kind == CustomRelics ? new[] { AmountBases[0], AmountBases[3], AmountBases[4] } : AmountBases.Take(1);
    public string Stat { get; set; } = "Shot Power";
    public string Target { get; set; } = "Triggering player";
    public string Duration { get; set; } = "Rest of run";
    public int Amount { get; set; } = 10;
    public int Threshold { get; set; } = 1;
    public int Chance { get; set; } = 100;
    public int ActivationsPerMatch { get; set; } = 1;
    public int MaxStacks { get; set; } = 99;
    public float Seconds { get; set; } = 15;
    public float IntervalMaxSeconds { get; set; } = 6;
    public string IconPng { get; set; } = "";
    public bool UsesNamedIcon { get; set; } = true;
    public bool NeedsRedesign { get; set; }
    public bool IsRelic => Kind == CustomRelics;
    public bool IsGoalie => Kind == CustomGoalieTalents;
    public string NativePool => IsRelic ? "Relics" : IsGoalie ? "Goalie Talents" : Kind == CustomChaosTalents ? "Chaos Talents" : "Talents";
    public string CatalogKind => IsGoalie ? "Goalie Talents" : IsRelic ? "Relics" : "Talents";
    public string RulesText
    {
        get
        {
            if (Target == CustomRewardEffects.OtherTeammates || CustomRewardEffects.IsSkaterSelection(Target) || CustomRewardTriggers.IsCampaignEvent(Trigger) || Target == CustomRewardEffects.BothTeams || Target == CustomRewardEffects.OpposingTeam || CustomRewardTriggers.IsOpponentAction(Trigger) || Target == CustomRewardEffects.Opponent || CustomRewardTriggers.IsQuery(Trigger) || Chance != 100 || AmountBasis != "Fixed amount" || ResetOn.Length > 0 || Effect != CustomRewardEffects.StatChange || CustomRewardTriggers.IsCondition(Trigger) && Trigger != "While owned" || IsGoalie || Stat.StartsWith("Goalie "))
                return CustomRewardEffects.Describe(this);
            if (Trigger == "While owned")
                return $"{(IsRelic || Target == "Whole team" ? "Your skaters gain" : "This skater gains")} {Signed(Amount)} {Stat} while this {(IsRelic ? "relic" : "talent")} is owned.";
            string count = Threshold == 1 ? "a" : Threshold.ToString(CultureInfo.InvariantCulture);
            string Event(string singular, string plural) => count + " " + (Threshold == 1 ? singular : plural);
            string action = Trigger switch {
                "Goal" => "scores " + Event("goal", "goals"),
                "Hat trick" => "scores " + Event("hat trick", "hat tricks"),
                "Assist" => "records " + (Threshold == 1 ? "an assist" : Threshold + " assists"),
                "Check" => "lands " + Event("check", "checks"),
                "Blocked shot" => "blocks " + Event("shot", "shots"),
                "Interception" => "intercepts " + Event("pass", "passes"),
                "Knockout" => "knocks out " + (Threshold == 1 ? "an opponent" : Threshold + " opponents"),
                "Pass received" => "receives " + Event("pass", "passes"),
                "Puck pickup" => "picks up the puck" + (Threshold == 1 ? "" : " " + Threshold + " times"),
                _ => CustomRewardTriggers.Action(Trigger, Threshold)
            };
            string duration = Duration == "Seconds" ? $"for {Seconds} {(Seconds == 1 ? "second" : "seconds")}" : "for the " + Duration.ToLowerInvariant().Replace("of ", "of the ");
            string recipient = Target == "Whole team" ? "your skaters gain" : IsRelic ? "that skater gains" : "they gain";
            string limit = ActivationsPerMatch == 1 ? "once" : IsUnlimited(ActivationsPerMatch) ? "any number of times" : "up to " + ActivationsPerMatch + " times";
            string stacks = MaxStacks == 1 ? "Does not stack." : IsUnlimited(MaxStacks) ? "Stacks without limit." : $"Stacks up to {MaxStacks} times per recipient ({Signed((long)Amount * MaxStacks)} {Stat} maximum).";
            return $"When {(IsRelic ? "a teammate" : "this skater")} {action} in a match, {recipient} {Signed(Amount)} {Stat} {duration}.\n"
                + (IsRelic ? $"Each skater can trigger this {limit} per match. " : $"Activates {limit} per match. ")
                + stacks + (Duration == "Seconds" ? " Triggering it again refreshes the timer." : "");
        }
    }
    public string FullDescription => NeedsRedesign ? "Excluded from rewards until redesigned. This item came from the removed curse/payoff preview. Its identity and icon are preserved."
        : (Description.Length == 0 ? "" : Description + "\n") + RulesText;
    private static string Signed(long n) => (n >= 0 ? "+" : "") + n.ToString(CultureInfo.InvariantCulture);
    public void Validate(bool requireIcon = true)
    {
        if (!Guid.TryParseExact(Id, "D", out _)) throw new ArgumentException("The item needs a valid stable ID.");
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 80 || Name.Any(char.IsControl)) throw new ArgumentException("Enter an item name of 1–80 characters.");
        if (Description.Length > 1000 || Description.Any(c => c == '\r' || c == '\n')) throw new ArgumentException("Keep the optional description on one line, up to 1000 characters.");
        if (!Kinds.Contains(Kind) || !Triggers.Contains(Trigger) || !Stats.Contains(Stat) || !Durations.Contains(Duration)) throw new ArgumentException("Choose an available item type, trigger, stat and effect duration.");
        // Only structural limits remain: an activation needs at least one event,
        // a recipient and a stack, and a duration must be a finite time. Large
        // values that could loop or overflow are bounded by the game runtime.
        if (Threshold < 1 || ActivationsPerMatch < 1 || TargetCount < 1 || MaxStacks < 1)
            throw new ArgumentException("Events per activation, activations, recipients and stacks must each be at least 1.");
        if (float.IsNaN(Seconds) || float.IsInfinity(Seconds) || Seconds <= 0) throw new ArgumentException("Seconds must be a finite number greater than zero.");
        if (CustomRewardTriggers.IsCompletion(Trigger) && Duration != "Rest of run")
            throw new ArgumentException("A match-completion effect must last for the rest of the run.");
        if (Trigger == "Random fart timer" && (float.IsNaN(IntervalMaxSeconds) || float.IsInfinity(IntervalMaxSeconds) || IntervalMaxSeconds < Seconds)) throw new ArgumentException("Choose a finite maximum timer interval at least as long as the minimum.");
        CustomRewardEffects.Validate(this);
        if (Chance < 1 || Chance > 100 || Chance != 100 && (CustomRewardTriggers.IsCondition(Trigger) || CustomRewardTriggers.IsQuery(Trigger)))
            throw new ArgumentException("Activation chance is 1% to 100% for event rules; continuous and query effects use their own values.");
        if (AmountBasis == "Per liquidated talent" && (Stat != "Overall" || Target != CustomRewardEffects.Actor)) throw new ArgumentException("Liquidation's saved count supplies its owner's Overall only.");
        if (!AmountBasesFor(Kind).Contains(AmountBasis) || AmountBasis != "Fixed amount" && (Effect != CustomRewardEffects.StatChange || !CustomRewardTriggers.IsCondition(Trigger) || !IsOverallStat(Stat) && Stat != "Overall"))
            throw new ArgumentException("Choose a count belonging to this native item kind, a continuous condition and a rating attribute.");
        if (ResetOn.Length > 0 && (Effect != CustomRewardEffects.StatChange || CustomRewardTriggers.IsCondition(Trigger)
            || !CustomRewardTriggers.ResetChoices(Kind).Contains(ResetOn) || ResetOn == Trigger))
            throw new ArgumentException("Choose a different supported event to reset an earned stat bonus.");
        if (requireIcon || IconPng.Length > 0)
        {
            if (IconPng.Length > 5600000) throw new ArgumentException("The icon is too large.");
            byte[] png;
            try { png = Convert.FromBase64String(IconPng); } catch { throw new ArgumentException("The icon data is not valid PNG data."); }
            ValidatePng(png);
        }
    }
    public static void ValidatePng(byte[] png)
    {
        if (png.Length < 45 || png.Length > 4 * 1024 * 1024 || !png.Take(8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            throw new ArgumentException("The item needs a PNG icon of at most 4 MB, or a generated name rectangle.");
        uint Number(int i) => ((uint)png[i] << 24) | ((uint)png[i + 1] << 16) | ((uint)png[i + 2] << 8) | png[i + 3];
        if (Number(8) != 13 || System.Text.Encoding.ASCII.GetString(png, 12, 4) != "IHDR" || Number(16) < 1 || Number(16) > 2048 || Number(20) < 1 || Number(20) > 2048)
            throw new ArgumentException("Choose a PNG between 1 and 2048 pixels on each side.");
        bool data = false, end = false;
        for (int at = 8; at <= png.Length - 12;) {
            uint size = Number(at);
            if (size > png.Length - at - 12) throw new ArgumentException("The PNG is truncated.");
            string chunk = System.Text.Encoding.ASCII.GetString(png, at + 4, 4);
            data |= chunk == "IDAT" && size > 0;
            at += (int)size + 12;
            if (chunk == "IEND") { end = size == 0 && at == png.Length; break; }
        }
        if (!data || !end) throw new ArgumentException("The PNG is incomplete.");
    }
    public CampaignDocument ToDocument()
    {
        var d = new CampaignDocument("# Custom reward format 3\n");
        d.Set("Format", "3"); d.Set("Reward ID", Id); d.Set("Reward Name", Name); d.Set("Kind", Kind);
        d.Set("Effect", Effect); d.Set("Radius", Radius.ToString(CultureInfo.InvariantCulture));
        d.Set("Target Count", TargetCount.ToString(CultureInfo.InvariantCulture));
        if (AmountBasis != "Fixed amount") d.Set("Amount basis", AmountBasis);
        if (ResetOn.Length > 0) d.Set("Reset on", ResetOn);
        d.Set("Description", Description); d.Set("Trigger", Trigger); d.Set("Stat", Stat); d.Set("Target", Target); d.Set("Duration", Duration);
        void Number(string key, int n) => d.Set(key, n.ToString(CultureInfo.InvariantCulture));
        if (Chance != 100) Number("Chance", Chance);
        if (Trigger == "Random fart timer") d.Set("Maximum interval seconds", IntervalMaxSeconds.ToString(CultureInfo.InvariantCulture));
        if (Effect == CustomRewardEffects.RandomAutoShot) Number("Shot delay milliseconds", ShotDelayMilliseconds);
        if (Effect == CustomRewardEffects.LoadedPuckBag) {
            Number("Normal Pucks", NormalPucks); Number("Double Pucks", DoublePucks); Number("Triple Pucks", TriplePucks);
        }
        Number("Amount", Amount); Number("Threshold", Threshold); Number("Activations Per Match", ActivationsPerMatch); Number("Max Stacks", MaxStacks); d.Set("Seconds", Seconds.ToString(CultureInfo.InvariantCulture));
        d.Set("Icon PNG", IconPng); d.Set("Named Icon", UsesNamedIcon ? "yes" : "no");
        d.Set("Needs redesign", NeedsRedesign ? "yes" : "no");
        return d;
    }
    public static CustomRewardDefinition Parse(string text)
    {
        var d = new CampaignDocument(text);
        if (d.Get("Format") != "1" && d.Get("Format") != "2" && d.Get("Format") != "3") throw new ArgumentException("Unsupported custom reward format.");
        int Number(string key) => int.TryParse(d.Get(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : throw new ArgumentException("Invalid " + key + ".");
        var r = new CustomRewardDefinition { Id = d.Get("Reward ID"), Name = d.Get("Reward Name"), Kind = d.Get("Kind"), Description = d.Get("Description"),
            Trigger = d.Get("Trigger"), Stat = d.Get("Stat"), Target = d.Get("Target"), Duration = d.Get("Duration"), Amount = Number("Amount"), Threshold = Number("Threshold"),
            ActivationsPerMatch = Number("Activations Per Match"), MaxStacks = Number("Max Stacks"), Seconds = float.TryParse(d.Get("Seconds"), NumberStyles.Float, CultureInfo.InvariantCulture, out float seconds) ? seconds : throw new ArgumentException("Invalid Seconds."),
            IconPng = d.Get("Icon PNG"), UsesNamedIcon = d.Get("Named Icon") == "yes", NeedsRedesign = d.Get("Needs redesign") == "yes" };
        if (d.Get("Format") == "3") { r.Effect = d.Get("Effect"); r.Radius = Number("Radius"); }
        if (d.Fields.ContainsKey("Maximum interval seconds")) r.IntervalMaxSeconds = float.TryParse(d.Get("Maximum interval seconds"), NumberStyles.Float, CultureInfo.InvariantCulture, out var maximum) ? maximum : throw new ArgumentException("Invalid maximum timer interval.");
        if (d.Fields.ContainsKey("Chance")) r.Chance = Number("Chance");
        if (d.Fields.ContainsKey("Shot delay milliseconds")) r.ShotDelayMilliseconds = Number("Shot delay milliseconds");
        if (d.Fields.ContainsKey("Normal Pucks")) r.NormalPucks = Number("Normal Pucks");
        if (d.Fields.ContainsKey("Double Pucks")) r.DoublePucks = Number("Double Pucks");
        if (d.Fields.ContainsKey("Triple Pucks")) r.TriplePucks = Number("Triple Pucks");
        if (d.Fields.ContainsKey("Target Count")) r.TargetCount = Number("Target Count");
        r.ResetOn = d.Get("Reset on");
        if (d.Fields.ContainsKey("Amount basis")) r.AmountBasis = d.Get("Amount basis");
        // Preserve identity and the original Chaos talent pool. Removed curse
        // definitions retain an editable draft but stay excluded until redesigned.
        if (d.Get("Format") == "1") {
            if (r.Kind == "Chaos Relics") { r.NeedsRedesign = true; r.Trigger = "While owned"; r.Target = "Whole team"; r.Stat = d.Get("Payoff Stat"); r.Amount = Number("Payoff Amount"); }
            r.Kind = r.Kind == "Relics" || r.Kind == "Chaos Relics" ? CustomRelics : r.Kind == "Chaos Talents" ? CustomChaosTalents : r.Kind == "Talents" ? CustomTalents : r.Kind;
        }
        r.Validate(); return r;
    }
    public static IReadOnlyList<CustomRewardDefinition> LoadFolder(string directory)
    {
        if (!Directory.Exists(directory)) return Array.Empty<CustomRewardDefinition>();
        var result = new List<CustomRewardDefinition>();
        foreach (string file in Directory.EnumerateFiles(directory, "*" + Extension).OrderBy(f => f))
        {
            if (new FileInfo(file).Length > 5700000) throw new InvalidDataException("Custom reward is too large: " + Path.GetFileName(file));
            var r = Parse(File.ReadAllText(file));
            if (result.Any(v => v.Id.Equals(r.Id, StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("Duplicate custom reward ID: " + r.Name);
            result.Add(r);
        }
        return result;
    }
    public void Save(string directory)
    {
        Validate(); Directory.CreateDirectory(directory);
        ToDocument().Save(Path.Combine(directory, Id + Extension));
    }
    public CustomRewardDefinition Copy() => Parse(ToDocument().ToString());
}
