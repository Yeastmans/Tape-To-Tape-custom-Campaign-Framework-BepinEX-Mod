using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using CampaignCore;

namespace CampaignCreator;

internal sealed class CustomRewardCreator : UserControl
{
    private readonly Window owner;
    private readonly string root;
    private readonly Func<string> campaign;
    private readonly Action saved;
    private readonly bool libraryOnly;
    private CustomRewardDefinition draft = new();
    private byte[]? png;
    private bool loading;
    private string previousEffect = CustomRewardEffects.StatChange;
    private readonly List<IDisposable> libraryImages = new();
    internal bool HasChanges { get; private set; }
    private readonly TextBox title = new() { Name = "CustomRewardName", MaxLength = 80, Watermark = "Talent or relic name" };
    private readonly TextBox description = new() { Name = "CustomRewardDescription", MaxLength = 1000, Watermark = "Optional extra description" };
    private readonly ComboBox kind = Choice(CustomRewardDefinition.Kinds);
    private readonly ComboBox trigger = Choice(CustomRewardDefinition.Triggers);
    private readonly ComboBox stat = Choice(CustomRewardDefinition.Stats);
    private readonly ComboBox effect = Choice(CustomRewardEffects.All.Select(e => e.Name));
    // Authored values have no arbitrary ceiling; the shared validator keeps
    // only sign, chance and native-level limits.
    private readonly NumericUpDown radius = Number(1, int.MaxValue, 5);
    private readonly NumericUpDown chance = Number(1, 100, 100);
    private readonly NumericUpDown targetCount = Number(1, int.MaxValue, 1);
    private readonly NumericUpDown normalPucks = Number(0, int.MaxValue, 9), doublePucks = Number(0, int.MaxValue, 2), triplePucks = Number(0, int.MaxValue, 1);
    private readonly NumericUpDown shotDelay = Number(0, int.MaxValue, 560);
    private readonly NumericUpDown intervalMax = new() { Minimum = .01m, Maximum = MaxSeconds, Value = 6, Increment = .05m, FormatString = "0.##", HorizontalAlignment = HorizontalAlignment.Stretch };
    private Control intervalMaxField = null!;
    private string previousTrigger = "";
    private Control shotDelayField = null!;
    private Control puckBagField = null!;
    private readonly ComboBox target = Choice(CustomRewardDefinition.Targets);
    private readonly ComboBox duration = Choice(CustomRewardDefinition.Durations);
    private readonly ComboBox amountBasis = Choice(CustomRewardDefinition.AmountBases);
    private readonly ComboBox resetOn = Choice(new[] { "Never" });
    private readonly NumericUpDown amount = Number(int.MinValue, int.MaxValue, 10), threshold = Number(1, int.MaxValue, 1), activations = Number(1, int.MaxValue, 1), stacks = Number(1, int.MaxValue, 99);
    private readonly CheckBox unlimitedActivations = new() { Name = "CustomRewardUnlimitedActivations", Content = "Unlimited" }, unlimitedStacks = new() { Name = "CustomRewardUnlimitedStacks", Content = "Unlimited" };
    // Seconds are stored as a float; this is the largest decimal it can show.
    private static readonly decimal MaxSeconds = 1000000000000m;
    private static decimal Seconds(float value) => value >= (float)MaxSeconds ? MaxSeconds : value <= .01f ? .01m : (decimal)value;
    private readonly NumericUpDown seconds = new() { Minimum = .01m, Maximum = MaxSeconds, Value = 15, Increment = .05m, FormatString = "0.##", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ContentControl icon = new();
    private readonly TextBlock rules = new() { Name = "CustomRewardRules", TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock status = new() { Name = "CustomRewardStatus", TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel library = new() { Spacing = 8 }, normal = new() { Spacing = 8 };
    private readonly TextBlock triggerHelp = new() { Name = "CustomRewardTriggerHelp", TextWrapping = TextWrapping.Wrap, Foreground = StudioTheme.Muted };
    private readonly TextBox search = new() { Watermark = "Search your custom items" };
    private readonly ComboBox filter = Choice(new[] { "All types" }.Concat(CustomRewardDefinition.Kinds));
    private readonly StackPanel conditional = new() { Spacing = 8 };
    private Control statField = null!, amountField = null!, durationField = null!, stacksField = null!, secondsField = null!, radiusField = null!, targetCountField = null!, resetField = null!, basisField = null!;
    private readonly TextBlock effectHelp = new() { TextWrapping = TextWrapping.Wrap, Foreground = StudioTheme.Muted };
    private readonly TextBlock ownershipHelp = new() { Name = "CustomRewardOwnership", TextWrapping = TextWrapping.Wrap, Foreground = StudioTheme.Muted };
    private static ComboBox Choice(IEnumerable<string> choices) => new() { ItemsSource = choices.ToArray(), HorizontalAlignment = HorizontalAlignment.Stretch };
    private static NumericUpDown Number(int min, int max, int value) => new() { Minimum = min, Maximum = max, Value = value, Increment = 1, FormatString = "0", HorizontalAlignment = HorizontalAlignment.Stretch };
    private static Control Field(string name, Control control) => new StackPanel { Spacing = 4, Children = { new TextBlock { Text = name, Foreground = StudioTheme.Muted, TextWrapping = TextWrapping.Wrap }, control } };
    private static Grid Pair(Control left, Control right) {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 12 };
        grid.Children.Add(left); Grid.SetColumn(right, 1); grid.Children.Add(right); return grid;
    }
    private Button Button(string label, Action action)
    {
        var button = new Button { Content = label, Padding = StudioTheme.ButtonPadding };
        button.Click += (_, _) => { try { action(); } catch (Exception ex) { status.Text = ex.Message; } };
        return button;
    }
    internal CustomRewardCreator(Window owner, string root, Func<string> campaign, Action saved, bool libraryOnly = false)
    {
        this.owner = owner; this.root = root; this.campaign = campaign; this.saved = saved; this.libraryOnly = libraryOnly;
        kind.Name = "CustomRewardKind"; trigger.Name = "CustomRewardTrigger"; stat.Name = "CustomRewardStat";
        effect.Name = "CustomRewardEffect"; target.Name = "CustomRewardTarget"; duration.Name = "CustomRewardDuration";
        intervalMax.Name = "CustomRewardIntervalMax";
        shotDelay.Name = "CustomRewardShotDelay";
        amount.Name = "CustomRewardAmount"; seconds.Name = "CustomRewardSeconds";
        targetCount.Name = "CustomRewardTargetCount";
        chance.Name = "CustomRewardChance"; resetOn.Name = "CustomRewardResetOn"; amountBasis.Name = "CustomRewardAmountBasis";
        trigger.ItemTemplate = new FuncDataTemplate<string>((value, _) => new TextBlock { Text = value == "While owned" ? "Always active while owned" : value });
        var shell = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,5*"), ColumnSpacing = 20 };
        var left = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*"), RowSpacing = 8 };
        var newItem = Button("New talent / relic", () => { if (CanSwitch()) Load(new()); });
        left.Children.Add(newItem); Grid.SetRow(filter, 1); left.Children.Add(filter); Grid.SetRow(search, 2); left.Children.Add(search);
        var scroll = RewardRow.ListBox(library); Grid.SetRow(scroll, 3); left.Children.Add(scroll); shell.Children.Add(left);
        var form = new StackPanel { Spacing = 12 };
        form.Children.Add(new TextBlock { Text = "Talent and relic creator", FontSize = 22, FontWeight = FontWeight.SemiBold });
        form.Children.Add(Pair(Field("Name", title), Field("Item type and reward pool", kind)));
        form.Children.Add(ownershipHelp);
        var appearance = new StackPanel { Spacing = 10 };
        appearance.Children.Add(Field("Optional description", description)); appearance.Children.Add(icon);
        var choosePng = new Button { Content = "Choose PNG…" };
        choosePng.Click += async (_, _) => {
            try {
                var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Choose item icon", AllowMultiple = false,
                    FileTypeFilter = new[] { new FilePickerFileType("PNG image") { Patterns = new[] { "*.png" } } } });
                if (files.Count == 0) return;
                png = CustomRewardIcon.ReadPng(files[0].TryGetLocalPath() ?? throw new ArgumentException("Choose a local PNG file.")); Changed();
            } catch (Exception ex) { status.Text = ex.Message; }
        };
        appearance.Children.Add(new WrapPanel { Orientation = Orientation.Horizontal, Children = { choosePng, Button("Base-game relic icon…", ChooseNativeIcon), Button("Use name rectangle", () => { png = null; Changed(); }) } });
        normal.Children.Add(Pair(Field("Activates when", trigger), Field("Effect", effect)));
        normal.Children.Add(triggerHelp); normal.Children.Add(effectHelp);
        normal.Children.Add(Pair(Field("Who receives the effect", target), statField = Field("Stat", stat)));
        normal.Children.Add(targetCountField = Field("Maximum randomly selected skaters", targetCount));
        normalPucks.Name = "CustomRewardNormalPucks"; doublePucks.Name = "CustomRewardDoublePucks"; triplePucks.Name = "CustomRewardTriplePucks";
        normal.Children.Add(puckBagField = new StackPanel { Spacing = 8, Children = {
            Field("One-goal pucks in the shuffled bag", normalPucks),
            Pair(Field("Two-goal pucks", doublePucks), Field("Three-goal pucks", triplePucks)) } });
        normal.Children.Add(Pair(amountField = Field("Stat change", amount), radiusField = Field("Radius (game units)", radius)));
        normal.Children.Add(basisField = Field("Calculate the attribute bonus", amountBasis));
        conditional.Children.Add(Field("Chance each time the event threshold is reached (%)", chance));
        shotDelayField = Field("Shot warning delay (milliseconds)", shotDelay); normal.Children.Add(shotDelayField);
        conditional.Children.Add(Pair(Field("Events per activation", threshold), Field("Activations per player per match", new StackPanel { Spacing = 4, Children = { activations, unlimitedActivations } })));
        conditional.Children.Add(Pair(stacksField = Field("Maximum active stacks", new StackPanel { Spacing = 4, Children = { stacks, unlimitedStacks } }), durationField = Field("How long the stat change lasts", duration)));
        conditional.Children.Add(resetField = Field("Clear this item's earned bonus on", resetOn));
        normal.Children.Add(conditional);
        // Charge limits and automatic-shot intervals are continuous-effect
        // parameters, so they must remain outside the hidden event controls.
        normal.Children.Add(secondsField = Field("Duration (seconds)", seconds)); normal.Children.Add(intervalMaxField = Field("Maximum interval (seconds)", intervalMax)); form.Children.Add(normal);
        form.Children.Add(new Border { Background = StudioTheme.Input, Padding = new Thickness(12), Child = rules });
        form.Children.Add(new Expander { Header = "Description and icon", Content = appearance, HorizontalAlignment = HorizontalAlignment.Stretch });
        form.Children.Add(new TextBlock { Text = libraryOnly ? "Saved items are available to add to your campaigns and players. Each campaign and Play Now export keeps its own copy, including the icon." : "This saves an independent campaign copy, including its icon. Shared campaigns and Play Now exports carry the rules and icon. Other campaigns keep their existing copies.", TextWrapping = TextWrapping.Wrap, Foreground = StudioTheme.Muted });
        var right = new Grid { RowDefinitions = new RowDefinitions("*,Auto,Auto"), RowSpacing = 8 };
        right.Children.Add(new ScrollViewer { Name = "CustomRewardForm", Content = form,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto });
        var save = Button(libraryOnly ? "Save item to library" : "Save item to library + campaign", Save); save.Name = "SaveCustomReward"; StudioTheme.Primary(save);
        var discard = Button("Discard draft", () => Load(new())); discard.Name = "DiscardCustomReward"; discard.Classes.Add("danger");
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = StudioTheme.SpaceMd, Children = { save, discard } };
        Grid.SetRow(actions, 1); right.Children.Add(actions); Grid.SetRow(status, 2); right.Children.Add(status);
        Grid.SetColumn(right, 1); shell.Children.Add(right); Content = shell;
        title.TextChanged += (_, _) => Changed(); description.TextChanged += (_, _) => Changed();
        foreach (var box in new[] { kind, trigger, effect, stat, target, duration, resetOn, amountBasis }) box.SelectionChanged += (_, _) => Changed();
        foreach (var number in new[] { amount, threshold, activations, stacks, seconds, radius, targetCount, chance, normalPucks, doublePucks, triplePucks, shotDelay, intervalMax }) number.ValueChanged += (_, _) => Changed();
        foreach (var box in new[] { unlimitedActivations, unlimitedStacks }) box.IsCheckedChanged += (_, _) => Changed();
        search.TextChanged += (_, _) => RefreshLibrary(); filter.SelectionChanged += (_, _) => RefreshLibrary(); filter.SelectedIndex = 0;
        owner.Closed += (_, _) => {
            if (icon.Content is Image { Source: IDisposable image }) image.Dispose();
            icon.Content = null;
            foreach (var bitmap in libraryImages) bitmap.Dispose(); libraryImages.Clear();
        };
        Load(new()); RefreshLibrary();
    }
    internal bool CanSwitch()
    {
        if (!HasChanges) return true;
        status.Text = "Save this item before opening another, or use Discard draft."; return false;
    }
    private CustomRewardDefinition Read()
    {
        string Text(ComboBox c) => c.SelectedItem as string ?? "";
        int N(NumericUpDown n) => decimal.ToInt32(n.Value ?? 0);
        return new CustomRewardDefinition { Id = draft.Id, Name = title.Text?.Trim() ?? "", Kind = Text(kind), Description = description.Text?.Trim() ?? "",
            NormalPucks = N(normalPucks), DoublePucks = N(doublePucks), TriplePucks = N(triplePucks),
            Trigger = Text(trigger), Effect = Text(effect), AmountBasis = Text(amountBasis), ResetOn = Text(resetOn) == "Never" ? "" : Text(resetOn), Radius = N(radius), TargetCount = N(targetCount), Stat = Text(stat), Target = Text(target), Duration = Text(duration), Amount = N(amount), Threshold = N(threshold),
            IntervalMaxSeconds = (float)(intervalMax.Value ?? 0), ShotDelayMilliseconds = N(shotDelay), Chance = N(chance), ActivationsPerMatch = unlimitedActivations.IsChecked == true ? CustomRewardDefinition.Unlimited : N(activations), MaxStacks = unlimitedStacks.IsChecked == true ? CustomRewardDefinition.Unlimited : N(stacks), Seconds = (float)(seconds.Value ?? 0), UsesNamedIcon = png == null, NeedsRedesign = draft.NeedsRedesign };
    }
    private void Changed()
    {
        if (loading) return;
        RefreshChoices();
        var current = Read();
        current.IconPng = png == null ? draft.IconPng : Convert.ToBase64String(png);
        HasChanges = current.ToDocument().ToString() != draft.ToDocument().ToString();
        RefreshPreview();
    }
    private void RefreshPreview()
    {
        if (icon.Content is Image { Source: IDisposable oldImage }) oldImage.Dispose();
        var d = Read(); icon.Content = CustomRewardIcon.Preview(d.Name, png);
        ownershipHelp.Text = d.Kind switch {
            CustomRewardDefinition.CustomRelics => "Owned by the team. Offered in ordinary relic rewards. Rink effects change the whole rink.",
            CustomRewardDefinition.CustomChaosTalents => "Owned by the assigned skater. Offered in the Wizard's Chaos talent pool.",
            CustomRewardDefinition.CustomGoalieTalents => "Owned by the assigned goalie. Offered in goalie talent rewards.",
            _ => "Owned by the assigned skater. Offered in regular talent rewards."
        };
        ownershipHelp.Text += " Effects are limited to this kind's native behaviors.";
        triggerHelp.Text = "Base-game example: " + CustomRewardTriggers.Help(d.Trigger);
        effectHelp.Text = CustomRewardEffects.Help(d.Effect);
        activations.IsEnabled = unlimitedActivations.IsChecked != true; stacks.IsEnabled = unlimitedStacks.IsChecked != true;
        ((TextBlock)((StackPanel)activations.Parent!.Parent!).Children[0]).Text = CustomRewardTriggers.IsCampaignEvent(d.Trigger) ? "Activations per team per run" : d.IsRelic && CustomRewardTriggers.IsOpponentAction(d.Trigger) ? "Activations per team per match" : "Activations per player per match";
        bool completion = CustomRewardTriggers.IsCompletion(d.Trigger), statsEffect = d.Effect == CustomRewardEffects.StatChange;
        duration.IsEnabled = !completion;
        conditional.IsVisible = !CustomRewardTriggers.IsCondition(d.Trigger) && !CustomRewardTriggers.IsQuery(d.Trigger);
        basisField.IsVisible = statsEffect && !CustomRewardEffects.IsForeignTeamTarget(d.Target) && CustomRewardDefinition.AmountBasesFor(d.Kind).Skip(1).Any() && CustomRewardTriggers.IsCondition(d.Trigger) && (CustomRewardDefinition.IsOverallStat(d.Stat) || d.Stat == "Overall");
        statField.IsVisible = statsEffect || CustomRewardEffects.IsTransferHelmet(d.Effect) || d.Effect == CustomRewardEffects.DonateBenchAttribute || d.Effect == CustomRewardEffects.PickupAttribute || d.Effect == CustomRewardEffects.BossAttribute || d.Effect == CustomRewardEffects.BonusMultiplier || d.Effect == CustomRewardEffects.LockAttribute;
        stacksField.IsVisible = durationField.IsVisible = statsEffect;
        resetField.IsVisible = statsEffect && !CustomRewardTriggers.IsCondition(d.Trigger);
        amountField.IsVisible = CustomRewardEffects.HasAmount(d.Effect);
        shotDelayField.IsVisible = d.Effect == CustomRewardEffects.RandomAutoShot;
        puckBagField.IsVisible = d.Effect == CustomRewardEffects.LoadedPuckBag;
        ((TextBlock)((StackPanel)amountField).Children[0]).Text = CustomRewardEffects.AmountLabel(d.Effect);
        radiusField.IsVisible = d.Target == CustomRewardEffects.Nearby || CustomRewardEffects.HasRadius(d.Effect);
        targetCountField.IsVisible = CustomRewardEffects.IsRandomTarget(d.Target);
        ((TextBlock)((StackPanel)secondsField).Children[0]).Text = d.Trigger == "Random fart timer" ? "Minimum interval (seconds)" : d.Trigger == "Puck fart timer" ? "Puck fart interval (seconds)" : d.Effect == CustomRewardEffects.HungerRage || d.Effect == CustomRewardEffects.PossessionExplosion ? "Possession delay (seconds)" : d.Effect == CustomRewardEffects.RandomAutoShot ? "Chance interval (seconds)" : CustomRewardEffects.IsCharge(d.Effect) ? "Charge limit (seconds)" : "Seconds";
        intervalMaxField.IsVisible = d.Trigger == "Random fart timer";
        secondsField.IsVisible = d.Effect == CustomRewardEffects.ReboundCriticals || d.Trigger == "Random fart timer" || d.Trigger == "Puck fart timer" || (statsEffect ? d.Duration == "Seconds" : d.Effect == CustomRewardEffects.HungerRage || d.Effect == CustomRewardEffects.PossessionExplosion || d.Effect == CustomRewardEffects.RandomAutoShot || CustomRewardEffects.IsCharge(d.Effect) || CustomRewardEffects.Find(d.Effect)?.Timed == true || d.Effect == CustomRewardEffects.Knockout);
        rules.Text = (draft.NeedsRedesign ? "Excluded until redesigned and saved. Proposed rule:\n" : "") + d.RulesText;
    }
    private void RefreshChoices()
    {
        loading = true;
        try {
            void Set(ComboBox box, IEnumerable<string> values) {
                var items = values.ToArray(); var selected = box.SelectedItem as string;
                if (!box.Items.Cast<string>().SequenceEqual(items)) box.ItemsSource = items;
                box.SelectedItem = selected != null && items.Contains(selected) ? selected : items.FirstOrDefault();
            }
            string type = kind.SelectedItem as string ?? CustomRewardDefinition.CustomTalents;
            Set(trigger, CustomRewardEffects.TriggersForKind(type));
            string when = trigger.SelectedItem as string ?? "While owned";
            Set(effect, CustomRewardEffects.ForTrigger(type, when));
            if (previousTrigger != when && when == "Random fart timer") { seconds.Value = 3; intervalMax.Value = 6; amount.Value = 80; }
            if (previousTrigger != when && when == "Puck fart timer") { seconds.Value = 9; amount.Value = 100; }
            previousTrigger = when;
            string currentEffect = effect.SelectedItem as string ?? "";
            if ((currentEffect == CustomRewardEffects.Boost || currentEffect == CustomRewardEffects.ShotRecoil) && previousEffect != currentEffect && seconds.Value > 5) seconds.Value = 1;
            if (previousEffect != currentEffect && (currentEffect == CustomRewardEffects.NetWidth || currentEffect == CustomRewardEffects.NetHeight))
                amount.Value = currentEffect == CustomRewardEffects.NetWidth ? 135 : 122;
            if (previousEffect != currentEffect && (CustomRewardEffects.ScalingConditionEffects.Contains(currentEffect) || currentEffect == CustomRewardEffects.SpikedInjury || currentEffect == CustomRewardEffects.InjuryMultiplier || currentEffect == CustomRewardEffects.GoalieHeadshot || currentEffect == CustomRewardEffects.BenchMultiplier)) amount.Value = 150;
            if (previousEffect != currentEffect && CustomRewardEffects.IsUpgrade(currentEffect)) amount.Value = 1;
            if (previousEffect != currentEffect && currentEffect == CustomRewardEffects.TrainingMultiplier) amount.Value = 150;
            if (previousEffect != currentEffect && (currentEffect == CustomRewardEffects.ExtraCoachPicks || currentEffect == CustomRewardEffects.ManualGoalDenial)) amount.Value = 1;
            if (previousEffect != currentEffect && currentEffect == CustomRewardEffects.DenyGoal) amount.Value = 3;
            if (previousEffect != currentEffect && currentEffect == CustomRewardEffects.RefereeSize) amount.Value = 2;
            if (previousEffect != currentEffect && currentEffect == CustomRewardEffects.InjuryHealing) amount.Value = 2;
            if (previousEffect != currentEffect && currentEffect == CustomRewardEffects.LockAttribute) amount.Value = 100;
            if (previousEffect != currentEffect && currentEffect == CustomRewardEffects.HelmetMultiplier) amount.Value = 2;
            if (previousEffect != currentEffect && currentEffect == CustomRewardEffects.EnragedDuration) amount.Value = 200;
            if (previousEffect != currentEffect && (currentEffect == CustomRewardEffects.TurningDelay || currentEffect == CustomRewardEffects.BoardBounce)) amount.Value = 100;
            if (previousEffect != currentEffect && (currentEffect == CustomRewardEffects.FartForce || currentEffect == CustomRewardEffects.FartRange)) amount.Value = 150;
            if (previousEffect != currentEffect && CustomRewardEffects.IsCategoryGrant(currentEffect)) amount.Value = 1;
            if (previousEffect != currentEffect && currentEffect == CustomRewardEffects.BlueLineLaunch) amount.Value = 10;
            if (previousEffect != currentEffect && CustomRewardEffects.IsBlueLineKnockout(currentEffect)) seconds.Value = 2;
            if (previousEffect != currentEffect && currentEffect == CustomRewardEffects.KnockoutDuration) amount.Value = 150;
            if (previousEffect != currentEffect && currentEffect == CustomRewardEffects.OpeningInjuries) amount.Value = 1;
            if (previousEffect != currentEffect && currentEffect == CustomRewardEffects.CyclicInjuries) amount.Value = 10;
            if (previousEffect != currentEffect && currentEffect == CustomRewardEffects.PuckInjuries) amount.Value = 25;
            if (previousEffect != currentEffect && currentEffect == CustomRewardEffects.OpeningCriticals) amount.Value = 3;
            if (previousEffect != currentEffect && currentEffect == CustomRewardEffects.ReboundCriticals) { amount.Value = 20; seconds.Value = 2; }
            if (previousEffect != currentEffect && currentEffect == CustomRewardEffects.CenterDefenderChecks) amount.Value = 1;
            if (previousEffect != currentEffect && currentEffect == CustomRewardEffects.RandomMatchAttribute) amount.Value = 5;
            if (previousEffect != currentEffect && currentEffect == CustomRewardEffects.BossSize) amount.Value = 2;
            if (previousEffect != currentEffect && currentEffect == CustomRewardEffects.AlternateOverall) amount.Value = 25;
            if (previousEffect != currentEffect && currentEffect == CustomRewardEffects.RetainPowers) amount.Value = 1;
            if (previousEffect != currentEffect && currentEffect == CustomRewardEffects.OctopusAttacks) radius.Value = 3;
            if (previousEffect != currentEffect && currentEffect == CustomRewardEffects.HungerRage) seconds.Value = 20;
            if (previousEffect != currentEffect && currentEffect == CustomRewardEffects.PossessionExplosion) seconds.Value = 3;
            if (previousEffect != currentEffect && currentEffect == CustomRewardEffects.LoosePuckAttraction) radius.Value = 20;
            if (previousEffect != currentEffect && currentEffect == CustomRewardEffects.RandomAutoShot) { amount.Value = 50; seconds.Value = 2; shotDelay.Value = 560; }
            if (previousEffect != currentEffect && CustomRewardEffects.IsCharge(currentEffect)) seconds.Value = 2.5m;
            previousEffect = currentEffect;
            Set(target, CustomRewardEffects.Targets(type, when, effect.SelectedItem as string ?? ""));
            Set(stat, CustomRewardEffects.EffectStats(type, target.SelectedItem as string ?? "", when, currentEffect));
            Set(amountBasis, currentEffect == CustomRewardEffects.StatChange && !CustomRewardEffects.IsForeignTeamTarget(target.SelectedItem as string) && CustomRewardTriggers.IsCondition(when) && (CustomRewardDefinition.IsOverallStat(stat.SelectedItem as string) || stat.SelectedItem as string == "Overall") ? CustomRewardDefinition.AmountBasesFor(type).Where(b => b != "Per liquidated talent" || (stat.SelectedItem as string == "Overall" && target.SelectedItem as string == CustomRewardEffects.Actor)) : new[] { "Fixed amount" });
            Set(resetOn, new[] { "Never" }.Concat(currentEffect == CustomRewardEffects.StatChange && !CustomRewardTriggers.IsCondition(when) ? CustomRewardTriggers.ResetChoices(type).Where(t => t != when) : Array.Empty<string>()));
            if (CustomRewardTriggers.IsCondition(when) || CustomRewardTriggers.IsQuery(when)) chance.Value = 100;
            if (CustomRewardTriggers.IsCompletion(when)) duration.SelectedItem = "Rest of run";
            else if (effect.SelectedItem as string != CustomRewardEffects.StatChange) duration.SelectedItem = "Seconds";
        } finally { loading = false; }
    }
    private void Load(CustomRewardDefinition d)
    {
        loading = true; draft = d; title.Text = d.Name; description.Text = d.Description;
        kind.SelectedItem = d.Kind; trigger.ItemsSource = CustomRewardEffects.TriggersForKind(d.Kind).ToArray(); trigger.SelectedItem = d.Trigger;
        effect.ItemsSource = CustomRewardEffects.ForTrigger(d.Kind, d.Trigger).ToArray(); effect.SelectedItem = d.Effect;
        previousEffect = d.Effect; previousTrigger = d.Trigger;
        amountBasis.ItemsSource = CustomRewardDefinition.AmountBasesFor(d.Kind); amountBasis.SelectedItem = d.AmountBasis;
        resetOn.ItemsSource = new[] { "Never" }.Concat(CustomRewardTriggers.ResetChoices(d.Kind).Where(t => t != d.Trigger)).ToArray();
        resetOn.SelectedItem = d.ResetOn.Length == 0 ? "Never" : d.ResetOn;
        targetCount.Value = d.TargetCount; chance.Value = d.Chance;
        normalPucks.Value = d.NormalPucks; doublePucks.Value = d.DoublePucks; triplePucks.Value = d.TriplePucks;
        target.ItemsSource = CustomRewardEffects.Targets(d.Kind, d.Trigger, d.Effect).ToArray(); target.SelectedItem = d.Target;
        stat.ItemsSource = CustomRewardEffects.EffectStats(d.Kind, d.Target, d.Trigger, d.Effect).ToArray(); stat.SelectedItem = d.Stat; duration.SelectedItem = d.Duration; radius.Value = d.Radius;
        intervalMax.Value = Seconds(d.IntervalMaxSeconds); shotDelay.Value = d.ShotDelayMilliseconds; amount.Value = d.Amount; threshold.Value = d.Threshold; activations.Value = CustomRewardDefinition.IsUnlimited(d.ActivationsPerMatch) ? 1 : d.ActivationsPerMatch; stacks.Value = CustomRewardDefinition.IsUnlimited(d.MaxStacks) ? 99 : d.MaxStacks; seconds.Value = Seconds(d.Seconds);
        unlimitedActivations.IsChecked = CustomRewardDefinition.IsUnlimited(d.ActivationsPerMatch); unlimitedStacks.IsChecked = CustomRewardDefinition.IsUnlimited(d.MaxStacks);
        png = !d.UsesNamedIcon && d.IconPng.Length > 0 ? Convert.FromBase64String(d.IconPng) : null;
        loading = false; HasChanges = false; status.Text = ""; RefreshPreview();
    }
    private async void ChooseNativeIcon()
    {
        try {
            var bytes = await CustomRewardIcon.PickNative(owner);
            if (bytes != null) { png = bytes; Changed(); }
        } catch (Exception ex) { status.Text = ex.Message; }
    }
    private void Save()
    {
        var d = Read(); d.Validate(false);
        if (draft.NeedsRedesign && d.Trigger == draft.Trigger && d.Effect == draft.Effect && d.Stat == draft.Stat
            && d.Target == draft.Target && d.Amount == draft.Amount && d.Duration == draft.Duration && d.Seconds == draft.Seconds)
            throw new ArgumentException("This legacy item is excluded. Choose its new trigger, effect or values before saving the redesign; changing its name or icon alone does not enable it.");
        d.NeedsRedesign = false;
        var existing = CustomRewardDefinition.LoadFolder(Path.Combine(root, "library", "rewards"))
            .Concat(libraryOnly ? Array.Empty<CustomRewardDefinition>() : CustomRewardDefinition.LoadFolder(Path.Combine(campaign(), CustomRewardDefinition.Folder)))
            .Where(r => r.Id == d.Id);
        if (existing.Any(r => r.Kind != d.Kind)) throw new ArgumentException("An existing item's type cannot change. Create a new item for another pool.");
        d.IconPng = Convert.ToBase64String(png ?? CustomRewardIcon.NamedRectangle(d.Name));
        d.Save(Path.Combine(root, "library", "rewards")); if (!libraryOnly) d.Save(Path.Combine(campaign(), CustomRewardDefinition.Folder));
        draft = d; HasChanges = false; saved(); RefreshLibrary(); status.Text = "Saved. Enable " + d.Name + " in the " + d.Kind + " pool to offer it as a reward.";
    }
    private void RefreshLibrary()
    {
        library.Children.Clear();
        foreach (var image in libraryImages) image.Dispose(); libraryImages.Clear();
        try {
            var local = libraryOnly ? new Dictionary<string, CustomRewardDefinition>() : CustomRewardDefinition.LoadFolder(Path.Combine(campaign(), CustomRewardDefinition.Folder)).ToDictionary(d => d.Id);
            var shared = CustomRewardDefinition.LoadFolder(Path.Combine(root, "library", "rewards")).ToDictionary(d => d.Id);
            var all = new Dictionary<string, CustomRewardDefinition>(shared);
            foreach (var pair in local) all[pair.Key] = pair.Value;
            foreach (var d in all.Values.Where(d => (filter.SelectedIndex <= 0 || d.Kind == filter.SelectedItem as string)
                && d.Name.Contains(search.Text ?? "", StringComparison.OrdinalIgnoreCase)).OrderBy(d => d.Name)) {
                var preview = CustomRewardIcon.Preview(d.Name, d.UsesNamedIcon ? null : Convert.FromBase64String(d.IconPng));
                if (preview is Image { Source: IDisposable bitmap }) libraryImages.Add(bitmap);
                var row = new StackPanel { Spacing = 6, Children = { preview,
                    new TextBlock { Text = d.Name + " · " + d.Kind, TextWrapping = TextWrapping.Wrap },
                    new TextBlock { Text = d.NeedsRedesign ? "Excluded until redesigned" : local.ContainsKey(d.Id) ? "Campaign copy" : "Available in every campaign · disabled until selected", Foreground = StudioTheme.Muted, TextWrapping = TextWrapping.Wrap } } };
                row.Children.Add(Button("Edit", () => { if (CanSwitch()) Load(d.Copy()); }));
                if (!d.IsRelic && shared.ContainsKey(d.Id))
                    row.Children.Add(Button("Delete from library", () => DeleteFromLibrary(d)));
                if (!libraryOnly && !local.ContainsKey(d.Id)) row.Children.Add(Button("Copy into campaign", () => { d.Save(Path.Combine(campaign(), CustomRewardDefinition.Folder)); saved(); RefreshLibrary(); }));
                library.Children.Add(new Border { BorderBrush = StudioTheme.Border, BorderThickness = new Thickness(1), Padding = new Thickness(10), Child = row });
            }
        } catch (Exception ex) { status.Text = ex.Message; }
    }

    private async void DeleteFromLibrary(CustomRewardDefinition item)
    {
        try {
            var dialog = new Window { Title = "Delete custom talent", Width = 480,
                SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var cancel = new Button { Content = "Cancel" };
            var remove = new Button { Content = "Delete from library", Name = "ConfirmDeleteCustomTalent" };
            cancel.Click += (_, _) => dialog.Close(false);
            remove.Click += (_, _) => dialog.Close(true);
            dialog.Content = new StackPanel { Margin = new Thickness(24), Spacing = 16, Children = {
                new TextBlock { Text = "Delete “" + item.Name + "” from the shared library?", TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = "Existing campaign and player copies will be kept. Saving an open copy later will add it back to the library.", TextWrapping = TextWrapping.Wrap },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { cancel, remove } }
            } };
            if (!await dialog.ShowDialog<bool>(owner)) return;
            if (!Guid.TryParse(item.Id, out _)) throw new InvalidDataException("Invalid custom talent ID.");
            File.Delete(Path.Combine(root, "library", "rewards", item.Id + CustomRewardDefinition.Extension));
            if (draft.Id == item.Id && !HasChanges) Load(new CustomRewardDefinition());
            RefreshLibrary();
            status.Text = "Deleted “" + item.Name + "” from the shared library. Existing campaign and player copies were kept.";
        } catch (Exception ex) { status.Text = ex.Message; }
    }
}
