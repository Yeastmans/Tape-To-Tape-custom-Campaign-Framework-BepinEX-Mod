using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using CampaignCore;

namespace CampaignCreator;

internal sealed class PlayerEditorView : UserControl
{
    internal EditorAttention Attention { get; } = new();
    private readonly CampaignDocument doc;
    private CampaignDocument team;
    private readonly TeamGoalieEquipment? goalieDefaults;
    private readonly CampaignDocument noTeamGoalie;
    private readonly GameData data;
    private readonly string role;
    private readonly Window owner;
    private readonly Action changed;
    private readonly Dictionary<string, string> values;
    private readonly Dictionary<string, Func<string>> detailReaders = new();
    private readonly HashSet<string> equipment;
    private readonly StackPanel left = new() { Width = 220, Spacing = StudioTheme.SpaceMd }, right = new() { Name = "PlayerEquipment", Width = 220, Spacing = StudioTheme.SpaceMd };
    private readonly List<EquipmentStylePicker> stylePickers = [];
    private readonly TeamUniform uniform = new();
    private readonly Image head = new() { Height = 105, Stretch = Stretch.Uniform };
    private readonly ContentControl logoSlot = new() { Name = "PlayerLogo" };
    private Bitmap? headBitmap, logoBitmap;
    private string logoSource = "";
    private readonly ToggleSwitch inheritFromTeam;
    private bool hasTeam;
    private readonly bool superstar;
    private bool away;
    private bool Independent => inheritFromTeam.IsChecked != true;
    private static readonly IBrush Mint = StudioTheme.Accent, Muted = StudioTheme.Muted;

    internal PlayerEditorView(Window owner, CampaignDocument doc, GameData data, string file, string role,
        Action changed, Action<string, Func<string>> register, Action save, Action? back, Action home, Action export, TextBlock status, string? previewTeamFile = null)
    {
        this.owner = owner; this.doc = doc; this.data = data; this.role = role; this.changed = changed;
        string folder = Path.GetDirectoryName(file)!;
        superstar = Path.GetFileName(folder).Equals("superstars", StringComparison.OrdinalIgnoreCase);
        string teamFile = Path.Combine(Path.GetDirectoryName(folder)!, "team.txt");
        var previewTeams = new List<string>();
        if (superstar)
        {
            string poolParent = Path.GetDirectoryName(folder)!;
            previewTeams = Directory.GetDirectories(poolParent)
                .Select(directory => Path.Combine(directory, "team.txt")).Where(File.Exists)
                .OrderByDescending(File.GetLastWriteTimeUtc).ThenBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
            // Library pools can be nested inside the owning team's folder.
            string libraryTeam = Path.Combine(Path.GetDirectoryName(poolParent)!, "team.txt");
            if (File.Exists(libraryTeam) && !previewTeams.Contains(libraryTeam, StringComparer.OrdinalIgnoreCase)) previewTeams.Add(libraryTeam);
            teamFile = previewTeamFile != null && previewTeams.Contains(previewTeamFile, StringComparer.OrdinalIgnoreCase)
                ? previewTeamFile : previewTeams.FirstOrDefault() ?? teamFile;
        }
        hasTeam = File.Exists(teamFile);
        // No team: the saved default look stands in for the team, as it would for a new team.
        var defaultLook = DefaultPlayerLook.TeamLook(data.Root, out noTeamGoalie);
        team = hasTeam ? new CampaignDocument(File.ReadAllText(teamFile)) : defaultLook;
        goalieDefaults = hasTeam && role == "goalie" ? new TeamGoalieEquipment(teamFile) : null;
        // Files saved before the game-model split still carry kit colors; saving drops them.
        PlayerColorModel.Migrate(doc); AppearanceContract.PinSkates(doc);
        var assets = role == "goalie" ? FieldSchema.GoalieAssets : FieldSchema.SkaterAssets;
        equipment = assets.Where(k => k != "Face").Concat(PlayerColorModel.PlayerKeys)
            .Concat(new[] { "Logo From", "Logo Skin" }).ToHashSet(StringComparer.OrdinalIgnoreCase);
        values = doc.Fields;
        foreach (var key in equipment) values.TryAdd(key, "");
        bool custom = AppearanceContract.Independent(doc, team, role);
        inheritFromTeam = new ToggleSwitch { Name = "InheritFromTeam", Content = "Team equipment", IsChecked = !custom,
            OnContent = "On · team styles", OffContent = "Off · own styles", HorizontalAlignment = HorizontalAlignment.Stretch };
        ToolTip.SetTip(inheritFromTeam, "Use the team's equipment styles. Colors always come from the team's home and away kit, as in the base game; only stick and skate colors can be the player's own.");
        foreach (var key in equipment) register(key, () => Independent && (key is "Logo From" or "Logo Skin") && !AppearanceContract.SupportsJerseyLogo(Effective(), role) ? "" : Independent && role == "skater" && (key is "Helmet" or "Helmet Away")
            && !HeadAppearance.CanWearHelmet(values.GetValueOrDefault("Face", "")) ? "none" : values[key]);
        register("Override Team Settings", () => Independent ? "yes" : "no");
        foreach (string key in doc.Fields.Keys.Where(k => k.StartsWith("Export Logo ", StringComparison.OrdinalIgnoreCase)))
            register(key, () => !Independent || AppearanceContract.SupportsJerseyLogo(Effective(), role) && SelectionValue("Logo From") == doc.Get("Logo From") ? doc.Get(key) : "");
        // Position belongs to the roster slot; retain imports and default unassigned creations.
        register("Position", () => string.IsNullOrWhiteSpace(doc.Get("Position")) ? (role == "goalie" ? "Goalie" : "Center") : doc.Get("Position"));
        Background = StudioTheme.Background; Foreground = Brushes.White;
        var shell = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"), RowSpacing = StudioTheme.SpaceMd, Margin = new Thickness(20,12) };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"), ColumnSpacing = StudioTheme.SpaceMd }; StudioTheme.Header(header);
        header.Children.Add(new StackPanel { Children = { Label("TAPE TO TAPE · Campaign Studio",12), Label(role == "goalie" ? "Goalie editor" : "Player editor",26) } });
        var homeButton = Button("Home",home); Grid.SetColumn(homeButton,1); header.Children.Add(homeButton);
        if (back != null) { var backButton = Button("Back to team",back); Grid.SetColumn(backButton,2); header.Children.Add(backButton); }
        var helpButton = PageHelpButton.Create(owner); Grid.SetColumn(helpButton,3); header.Children.Add(helpButton); shell.Children.Add(header);
        var identity = new Grid { ColumnDefinitions = new ColumnDefinitions("300,100,180"), ColumnSpacing = StudioTheme.SpaceMd,
            HorizontalAlignment = HorizontalAlignment.Left };
        Control Field(string key, bool fillHeight = false)
        {
            var field = key == "Ability" && OpponentAbilityPolicy.IsOpponentPlayer(file)
                ? OpponentAbilityPicker.Create(doc.Get(key), data, changed)
                : FieldControls.Create(key,doc.Get(key),role,data,()=> { changed(); uniform?.InvalidateVisual(); },_=>{}, fillHeight: fillHeight,
                    talentsPopout: key is "Talents" or "Goalie Talents");
            field.control.Tag = key; register(key,field.read); detailReaders[key]=field.read;
            var wrapper = new Grid { RowDefinitions = new RowDefinitions(fillHeight ? "Auto,*" : "Auto,Auto"), RowSpacing = StudioTheme.SpaceXs };
            wrapper.Children.Add(Label(key.ToUpperInvariant())); Grid.SetRow(field.control, 1); wrapper.Children.Add(field.control);
            return wrapper;
        }
        identity.Children.Add(Field("Name")); var number = Field("Number"); Grid.SetColumn(number,1); identity.Children.Add(number);
        var numberStyle = new ComboBox { Name = "PlayerNumberStyle", Tag = "Number Style",
            ItemsSource = new[] { FieldSchema.InheritNumberStyle }.Concat(FieldSchema.NumberStyles).ToArray(),
            SelectedIndex = FieldSchema.PlayerNumberStyleIndex(doc.Get("Number Style")) + 1,
            HorizontalAlignment = HorizontalAlignment.Stretch };
        string ReadNumberStyle() => numberStyle.SelectedIndex <= 0 ? "" : FieldSchema.NumberStyles[numberStyle.SelectedIndex - 1];
        register("Number Style", ReadNumberStyle); detailReaders["Number Style"] = ReadNumberStyle;
        numberStyle.SelectionChanged += (_, _) => { changed(); uniform.InvalidateVisual(); };
        ToolTip.SetTip(numberStyle, "Inherit team follows Team look. An explicit style preserves this player's native digits, including a goalie's different style. This is independent of Team equipment.");
        var numberStyleField = new StackPanel { Spacing = StudioTheme.SpaceXs, Children = { Label("NUMBER STYLE"), numberStyle } };
        Grid.SetColumn(numberStyleField, 2); identity.Children.Add(numberStyleField);
        var teamContext = new StackPanel { Spacing = StudioTheme.SpaceXs, VerticalAlignment = VerticalAlignment.Center };
        if (superstar)
        {
            teamContext.Children.Add(Label("Kit colors come from the player team chosen for the run. The preview shows one of them.", 11));
            if (previewTeams.Count > 1)
            {
                var choices = previewTeams.Select(path => new TeamPreview(path, new CampaignDocument(File.ReadAllText(path)).Get("Team Name", Path.GetFileName(Path.GetDirectoryName(path)!)))).ToArray();
                var preview = new ComboBox { Name = "SuperstarPreviewTeam", ItemsSource = choices,
                    SelectedItem = choices.FirstOrDefault(option => option.Path.Equals(teamFile, StringComparison.OrdinalIgnoreCase)), HorizontalAlignment = HorizontalAlignment.Stretch };
                preview.SelectionChanged += (_, _) => {
                    if (preview.SelectedItem is not TeamPreview selection) return;
                    team = new CampaignDocument(File.ReadAllText(selection.Path)); hasTeam = true; effectiveCache = null;
                    uniform.TeamName = team.Get("Team Name"); uniform.Abbreviation = team.Get("Abbreviation", "TEAM");
                    BuildParts();
                };
                teamContext.Children.Add(preview);
            }
            else teamContext.Children.Add(Label(hasTeam ? "Preview: " + team.Get("Team Name") : "No player team yet · the preview shows your default look.", 11));
        }
        else teamContext.Children.Add(Label(hasTeam ? "Team: " + team.Get("Team Name") + " · kit colors come from the team" : "No team yet · the preview shows your default look until the player joins a team.",11));
        Grid.SetRow(identity,1); shell.Children.Add(identity);
        var appearanceMode = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = StudioTheme.SpaceLg };
        appearanceMode.Children.Add(teamContext);
        inheritFromTeam.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(inheritFromTeam,1); appearanceMode.Children.Add(inheritFromTeam);
        uniform.Goalie = role == "goalie"; uniform.ColorValue = DiagramColor; uniform.SkinValue = DiagramSkin;
        uniform.TeamName = team.Get("Team Name"); uniform.Abbreviation = team.Get("Abbreviation","TEAM");
        uniform.Logo = Crest;
        if (role != "goalie")
        {
            uniform.HeadPreview = () => headBitmap;
            uniform.Height += 105;
        }
        uniform.NumberText = () => {
            string number = detailReaders.TryGetValue("Number",out var read) ? read() : doc.Get("Number");
            return int.TryParse(number, out int value) ? FieldSchema.NumberText(value,
                FieldSchema.EffectiveNumberStyle(ReadNumberStyle(), team.Get("Number Style"))) : number;
        };
        uniform.PlayerName = () => detailReaders.TryGetValue("Name",out var read) ? read() : doc.Get("Name");
        var board = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = StudioTheme.SpaceLg };
        var leftColumn = new StackPanel { Spacing = StudioTheme.SpaceMd };
        var kit = new TabStrip { Name = "PlayerKitTabs", ItemsSource = new[] { "Home", "Away" }, SelectedIndex = 0 };
        kit.SelectionChanged += (_,_) => { away = kit.SelectedIndex == 1; BuildParts(); }; leftColumn.Children.Add(kit);
        var sides = new TabStrip { Name = "PlayerFacingTabs", ItemsSource = new[] { "Front", "Back" }, SelectedIndex = 0 };
        sides.SelectionChanged += (_, _) => { uniform.Back = sides.SelectedIndex == 1; uniform.InvalidateVisual(); };
        leftColumn.Children.Add(sides); leftColumn.Children.Add(Label("APPEARANCE")); leftColumn.Children.Add(left);
        board.Children.Add(new Viewbox { Child=leftColumn, Stretch=Stretch.Uniform, StretchDirection=StretchDirection.DownOnly, VerticalAlignment=VerticalAlignment.Top });
        var art = new Viewbox { Child=uniform, Stretch=Stretch.Uniform }; Grid.SetColumn(art,1); board.Children.Add(art);
        var rightFit = new Viewbox { Child=right, Stretch=Stretch.Uniform, StretchDirection=StretchDirection.DownOnly, VerticalAlignment=VerticalAlignment.Top }; Grid.SetColumn(rightFit,2); board.Children.Add(rightFit);
        var look = new Border { Height=540, Child=board, Padding=new Thickness(12), CornerRadius=new CornerRadius(16), Background=StudioTheme.Surface };
        var details = new StackPanel { Spacing = StudioTheme.SpaceMd };
        string[] roleDetails = role == "goalie"
            ? ["Skill", "Catching", "Glove", "Blocker", "Five Hole", "Standing Speed", "Butterfly Speed", "Control", "Recovery", "Pass Power", "Shot Power", "Poke Check", "Depth", "Pass Read", "Goalie Talents"]
            : ["Size", "Size Offset", "Left Handed", "Speed", "Shot Power", "Accuracy", "Checking", "Ability", "Talents"];
        // Each role owns its form; legacy keys remain in the document without
        // turning skater fields into goalie controls (or vice versa).
        Control attributes;
        if (role == "goalie")
        {
            var page = new Grid { ColumnDefinitions = new ColumnDefinitions("3*,2*"), ColumnSpacing = StudioTheme.SpaceLg, Margin = new Thickness(0,8,0,0) };
            var stats = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto"), ColumnSpacing = StudioTheme.SpaceMd, RowSpacing = 6, VerticalAlignment = VerticalAlignment.Top };
            var keys = roleDetails.Where(k => k != "Goalie Talents").ToArray();
            for (int i = 0; i < keys.Length; i++)
            {
                var field = Field(keys[i]); Grid.SetRow(field, i / 3); Grid.SetColumn(field, i % 3); stats.Children.Add(field);
            }
            stats.Width = 540;
            page.Children.Add(new Viewbox { Child = stats, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, VerticalAlignment = VerticalAlignment.Top });
            var talents = Field("Goalie Talents", true); Grid.SetColumn(talents, 1); page.Children.Add(talents);
            attributes = page;
        }
        else
        {
            var prince = new CheckBox { Name = "CrusaderBrawlPrince", Content = "Counts as Prince for Crusader brawls",
                IsChecked = PlayerBrawlOptions.CountsAsPrince(doc) };
            register(PlayerBrawlOptions.PrinceKey, () => prince.IsChecked == true ? "yes" : "no");
            prince.IsCheckedChanged += (_, _) => changed();
            details.Children.Add(prince);
            details.Children.Add(new TextBlock { Text = "With Bodycheck brawl enabled on the opponent team, checking this player during a charged-shot windup can offer a brawl. Other Prince behaviour is unchanged. The native Prince always qualifies.",
                TextWrapping = TextWrapping.Wrap, Foreground = Muted, FontSize = 12 });
            foreach (var key in roleDetails) details.Children.Add(Field(key));
            attributes = new ScrollViewer { Content = details };
        }
        var appearance = new StackPanel { Spacing = StudioTheme.SpaceMd };
        appearance.Children.Add(new Border { Child=appearanceMode, Padding=new Thickness(12), CornerRadius=new CornerRadius(16), Background=StudioTheme.Surface });
        if (role != "goalie") appearance.Children.Add(new TextBlock { Text = "Home Helmet controls starting equipment: Team colors equips the ordinary helmet, Wing equips native Wing equipment, and None removes the ordinary helmet. Talent, transformation and reward helmets still follow native rules. Helmet Away does not independently equip, remove or replace the physical helmet.",
            TextWrapping = TextWrapping.Wrap, Foreground = Muted, FontSize = 12 });
        appearance.Children.Add(look);
        var tabs = new TabControl { ItemsSource = new[] { new TabItem { Header="Appearance", Content=new ScrollViewer { Content=appearance } }, new TabItem { Header="Details & attributes", Content=attributes } } };
        Grid.SetRow(tabs,2); shell.Children.Add(tabs);
        var footer = new Grid { ColumnDefinitions=new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = StudioTheme.SpaceMd }; StudioTheme.Footer(footer);
        var message = Label("",12); message.Bind(TextBlock.TextProperty,new Avalonia.Data.Binding(nameof(TextBlock.Text)) { Source=status }); footer.Children.Add(message);
        var exportButton=Button("Export to Play Now",export); Grid.SetColumn(exportButton,1); footer.Children.Add(exportButton);
        var saveButton=Button("Save changes",save); StudioTheme.Primary(saveButton); Grid.SetColumn(saveButton,2); footer.Children.Add(saveButton); Grid.SetRow(footer,3); shell.Children.Add(footer);
        if (role != "goalie") { values["Face"] = HeadAppearance.Base(doc.Get("Face")); register("Face",()=>values["Face"]); }
        inheritFromTeam.IsCheckedChanged += (_,_) => { BuildParts(); changed(); };
        BuildParts(); Content=Attention.Wrap(shell, saveButton);
        DetachedFromVisualTree += (_,_) => { head.Source=null; headBitmap?.Dispose(); headBitmap=null; logoBitmap?.Dispose(); logoBitmap=null; };
    }
    private sealed record TeamPreview(string Path, string Name) { public override string ToString() => Name; }
    private string Inherited(string key)
    {
        string mapped = AppearanceContract.TeamKey(key);
        if (role == "goalie" && FieldSchema.GoalieAssets.Contains(key)) return goalieDefaults?.Get(key) ?? (hasTeam ? "" : noTeamGoalie.Get(key));
        return team.Get(mapped);
    }
    private string effectiveKey = "";
    private CampaignDocument? effectiveCache;
    private CampaignDocument Effective()
    {
        var details = detailReaders.ToDictionary(p => p.Key, p => p.Value());
        string key = Independent + "|" + string.Join("|", values.Select(p => p.Key + "=" + p.Value)) + "|" + string.Join("|", details.Select(p => p.Key + "=" + p.Value));
        if (effectiveCache != null && key == effectiveKey) return effectiveCache;
        var current = new CampaignDocument(doc.ToString());
        foreach (var pair in values) current.Set(pair.Key, pair.Value);
        foreach (var pair in details) current.Set(pair.Key, pair.Value);
        current.Set("Override Team Settings", Independent ? "yes" : "no");
        var goalie = new CampaignDocument("");
        if (goalieDefaults != null) foreach (string assetKey in FieldSchema.GoalieAssets) goalie.Set(assetKey, goalieDefaults.Get(assetKey));
        else if (!hasTeam) foreach (string assetKey in FieldSchema.GoalieAssets) goalie.Set(assetKey, noTeamGoalie.Get(assetKey));
        effectiveKey = key;
        return effectiveCache = AppearanceContract.Resolve(current, team, goalie, role);
    }
    private string Value(string key) => Effective().Get(key);
    private string SelectionValue(string key) => Independent ? values.GetValueOrDefault(key, "") : Inherited(key);
    private Bitmap? Crest()
    {
        string source = Value("Logo From");
        if (source != logoSource) { logoSource = source; logoBitmap?.Dispose(); logoBitmap = null; try { logoBitmap = LogoExport.Preview(data.Root, source); } catch { } }
        return logoBitmap;
    }
    internal void ApplyExportAppearance(CampaignDocument target)
    {
        var effective = Effective();
        // The Play Now team supplies the kit; a player carries only styles and own stick/skate colors.
        foreach (var key in AppearanceContract.PlayerKeys(role))
            target.Set(key, AppearanceContract.IsColor(key) ? values.GetValueOrDefault(key, "") : effective.Get(key));
        foreach (string key in target.Fields.Keys.Where(PlayerColorModel.IsTeamOnlyColor).ToArray()) target.Remove(key);
        target.Set("Override Team Settings", "yes");
        if (!AppearanceContract.SupportsJerseyLogo(effective, role)) AppearanceContract.ClearJerseyLogo(target);
    }
    private static TextBlock Label(string text,double size=12) => new() { Text=text,FontSize=size,Foreground=Muted,TextWrapping=TextWrapping.Wrap };
    private static Button Button(string text,Action action) { var b=new Button { Content=text,Padding = StudioTheme.ButtonPadding,HorizontalAlignment=HorizontalAlignment.Stretch }; b.Click+=(_,_)=>action(); return b; }
    private void BuildParts()
    {
        left.Children.Clear(); right.Children.Clear(); stylePickers.Clear();
        string a=away ? " Away" : "";
        // Base-game model: every kit color is the team's; a player owns only stick and skate colors.
        void Part(string title,string? skin,string[]? playerColors,StackPanel destination,bool teamPaletteOnly=false,bool ownStyle=false)
        {
            // ownStyle: the base game stores this style on the player (skater stick), whatever the team says.
            var panel=new StackPanel { Spacing=3, IsEnabled=Independent || ownStyle, Children={Label(title.ToUpperInvariant())} };
            var colorButton=new Button { Content="Uses the team's kit colors", Padding=StudioTheme.ButtonPadding, HorizontalAlignment=HorizontalAlignment.Stretch, IsHitTestVisible=false, Opacity=0.8 };
            // Own colors reach only the colorable style in game; a premade piece is fixed artwork.
            var colorSlot = playerColors != null ? new ContentControl() : null;
            Func<string>? style = null;
            void RefreshColors() { if (colorSlot != null) colorSlot.Content = PlayerColors(title, playerColors!, style != null && IsPremade(style())); }
            if (skin==null) panel.Children.Add(new TextBlock { Text="Colors come from the team's " + (away ? "away" : "home") + " kit.", FontSize=11, Foreground=Muted, TextWrapping=TextWrapping.Wrap });
            else
            {
                var catalog=data.Assets.Values(role,skin);
                if (catalog.Count == 0 && skin.EndsWith(" Away")) catalog=data.Assets.Values(role,AppearanceContract.HomeKey(skin));
                bool unsupportedHelmet = role == "skater" && (skin is "Helmet" or "Helmet Away") && !HeadAppearance.CanWearHelmet(values["Face"]);
                style = ()=>unsupportedHelmet ? "none" : ownStyle ? (values[skin].Length > 0 ? values[skin] : Value(skin)) : SelectionValue(skin);
                var picker = new EquipmentStylePicker(catalog,style,v=> { values[skin]=v; if(role=="skater" && skin is "Body" or "Body Away") { string sleeveKey=skin=="Body"?"Bicep":"Bicep Away"; string? sleeve=EquipmentStylePicker.MatchingSleeves(v,data.Assets.Values("skater",sleeveKey)); if(sleeve!=null) values[sleeveKey]=sleeve; } foreach(var other in stylePickers) other.Refresh(); RefreshLogo(); RefreshColors(); changed(); uniform.InvalidateVisual(); if(skin is "Helmet Skin" or "Helmet" or "Helmet Away") RefreshHead(); },colorButton, customLabel: "Team colors", canSelect: !unsupportedHelmet, allowNoHelmet: role == "skater" && skin is "Helmet" or "Helmet Away", disabledReason: "This head has no helmet-compatible version.", inherited: !Independent && !ownStyle, teamPaletteOnly: teamPaletteOnly,
                    emptyLabel: skin is "Socks" or "Socks Away" ? "Same as jersey" : null, emptyHint: skin is "Socks" or "Socks Away" ? "Socks follow the jersey style." : null);
                stylePickers.Add(picker); panel.Children.Add(picker);
                if (role == "skater" && title == "Jersey")
                {
                    string sleeveKey = skin == "Body" ? "Bicep" : "Bicep Away";
                    var sleeves = data.Assets.Values(role, sleeveKey);
                    if (sleeves.Count == 0) sleeves = data.Assets.Values(role, "Bicep");
                    picker.AddSleeves(sleeves, () => {
                        string value = SelectionValue(sleeveKey);
                        if (value.Length == 0 && away) value = SelectionValue("Bicep");
                        try { return data.Assets.Resolve(role, sleeveKey, value); } catch (ArgumentException) { return value; }
                    }, value => { values[sleeveKey] = value; changed(); uniform.InvalidateVisual(); });
                }
                if (unsupportedHelmet)
                {
                    colorButton.IsEnabled=false;
                    panel.Children.Add(new TextBlock { Text="Helmet style unavailable: this head has no fitted helmet version for the picker. Physical helmet equipment, talents and transformations still follow native rules.", FontSize=11, Foreground=Muted, TextWrapping=TextWrapping.Wrap });
                    ToolTip.SetTip(panel, "No fitted helmet version exists for this head.");
                }
            }
            destination.Children.Add(panel);
            if (colorSlot != null) { RefreshColors(); destination.Children.Add(colorSlot); }
        }
        Part(role=="goalie" ? "Goalie mask" : "Helmet",role=="goalie" ? "Helmet Skin" : "Helmet"+a,null,right);
        if (role=="goalie") Part("Mask shell",null,null,right);
        if (role == "goalie") right.Children.Add(head);
        RefreshHead();
        if (role != "goalie")
        {
            var face=FieldControls.Create("Face",values["Face"],role,data,()=> {},v=> { if(values["Face"]==v) return; values["Face"]=HeadAppearance.Base(v); BuildParts(); changed(); });
            face.control.Tag = "Face";
            right.Children.Add(new StackPanel { Spacing=3, Children={Label("HEAD"),face.control} });
        }
        Part(role=="goalie" ? "Goalie jersey" : "Jersey",role=="goalie" ? "Skin"+a : "Body"+a,null,right);
        Part("Pants",role=="goalie" ? away ? "Goalie Pants Away" : "Goalie Pants Skin" : "Pants"+a,null,right);
        Part(role=="goalie" ? "Goalie pads" : "Socks",role=="goalie" ? away ? "Pads Away" : "Pads Skin" : "Socks"+a,null,right);
        Part("Name & numbers",null,null,left);
        left.Children.Add(logoSlot); RefreshLogo();
        Part(role=="goalie" ? "Goalie stick" : "Stick",role=="goalie" ? away ? "Stick Away" : "Stick Skin" : "Stick",PlayerColorModel.StickKeys,left,role=="goalie",ownStyle: role!="goalie");
        Part(role=="goalie" ? "Catcher" : "Gloves",role=="goalie" ? away ? "Glove Away" : "Glove Skin" : "Gloves"+a,null,left);
        // The game's skates color set is the goalie's blocker (TeamColorsData "Forward Skates/Goalie Blocker").
        if (role=="goalie") Part("Blocker",away ? "Blocker Away" : "Blocker Skin",PlayerColorModel.SkatesKeys,left,true);
        else Part("Skates",null,PlayerColorModel.SkatesKeys,left); // colors only, as in the base game
        uniform.InvalidateVisual();
    }
    // The player's own stick/skate colors (SkaterData.StickColor/SkatesColor in game): one value
    // for both kits, available whatever the equipment-style toggle says, blank = the team's kit.
    private static bool IsPremade(string style) => style.Length > 0 && style != "none" && !EquipmentStylePicker.IsCustom(style);
    private Control PlayerColors(string title, string[] keys, bool premade = false)
    {
        // A premade style is never tinted in game, so its own colors are off and cleared
        // (as the player logo is under a premade jersey).
        if (premade) foreach (string k in keys) values[k] = "";
        bool own = keys.Any(k => values[k].Length > 0);
        // Base game: the stick has no team colors (a blank player uses the default stick colors);
        // skates follow the team kit unless the player sets their own.
        bool stick = keys.SequenceEqual(PlayerColorModel.StickKeys);
        string fallback = stick ? "Default stick colors" : "Team kit colors";
        var note = new TextBlock { Text = premade ? "Premade " + title.ToLowerInvariant() + ": its colors are part of the artwork" : own ? "Player's own colors (home and away)" : fallback, FontSize = 11, Foreground = Muted, TextWrapping = TextWrapping.Wrap };
        var set = Button(own ? "Edit player colors" : "Set player colors", async () => {
            await AppearanceColorsDialog.Show(owner, title, keys, false, k => values[k].Length > 0 ? values[k] : Value(k),
                (key, value) => { foreach (string k in keys) if (values[k].Length == 0) values[k] = Value(k); values[key] = value; }, () => { changed(); uniform.InvalidateVisual(); }, showKit: false);
            BuildParts();
        });
        set.Name = title.Replace(" ", "") + "PlayerColors";
        set.IsEnabled = !premade;
        var panel = new StackPanel { Spacing = 3, Children = { note, set } };
        if (own) panel.Children.Add(Button(stick ? "Use default stick colors" : "Use team kit colors", () => { foreach (string k in keys) values[k] = ""; changed(); BuildParts(); }));
        return panel;
    }
    // One player logo is shared by both kits. It needs a custom look with team-colour
    // jerseys home and away; choosing a premade jersey clears it (team logo untouched).
    private void RefreshLogo()
    {
        bool supported = AppearanceContract.SupportsJerseyLogo(Effective(), role);
        if (Independent && !supported) { values["Logo From"] = ""; values["Logo Skin"] = ""; }
        TextBlock Note(string text) => new() { Text = text, FontSize = 11, Foreground = Muted, TextWrapping = TextWrapping.Wrap };
        void Apply(string logo) { values["Logo From"] = logo; values["Logo Skin"] = ""; changed(); uniform.InvalidateVisual(); RefreshLogo(); }
        var panel = new StackPanel { Spacing = 3, Children = { Label("LOGO") } };
        if (!Independent) panel.Children.Add(Note("Team equipment is on: this player wears the team logo."));
        else if (!supported) panel.Children.Add(Note("Premade jersey: no player logo. Use team colours for the home and away jersey to choose one."));
        else
        {
            Func<string> read = () => values["Logo From"];
            var picker = LogoPicker.Create(data.Root, values["Logo From"], () => Apply(read()), "Team logo", "Choose player logo");
            read = picker.read;
            panel.Children.Add(picker.control);
            // Imported base-game crest artwork (Logo Skin) overrides the logo image in game.
            string crest = values["Logo Skin"];
            bool importedCrest = crest.StartsWith("Team_Logo/", StringComparison.Ordinal) && !crest.StartsWith("Team_Logo/Custom_", StringComparison.Ordinal);
            if (values["Logo From"].Length == 0 && importedCrest) panel.Children.Add(Note("Imported crest: " + FieldControls.Friendly(crest)));
            if (values["Logo From"].Length > 0 || importedCrest) panel.Children.Add(Button("Use team logo", () => Apply("")));
        }
        logoSlot.Content = panel;
    }
    private void RefreshHead()
    {
        head.Source=null; headBitmap?.Dispose(); headBitmap=null;
        string? path=data.HeadPreview(role,role=="goalie" ? Value("Helmet Skin") : HeadAppearance.Resolve(values["Face"], !HeadAppearance.IsNone(Value(away ? "Helmet Away" : "Helmet"))));
        if (path!=null) { headBitmap=new Bitmap(path); head.Source=headBitmap; }
        ToolTip.SetTip(head,headBitmap==null ? "No head preview available for this selection" : "Head preview");
        uniform.InvalidateVisual();
    }
    // Resolved like the game: team kit (or the default kit with no team), player stick/skates on top.
    private string DiagramColor(string token)
    {
        int channel = token[^1] - '0';
        string scheme = token[..^1] switch {
            "numbers" => "number", "bicep" => "jersey",
            "blocker" when role == "goalie" => "skates",
            var other => other
        };
        string key = AppearanceContract.ColorKey(scheme, channel, away);
        if (token.StartsWith("bicep") && channel == 1)
        {
            string bicep = Value(away ? "Bicep Away Color" : "Bicep Color");
            if (bicep.Length > 0) key = away ? "Bicep Away Color" : "Bicep Color";
        }
        return FieldSchema.TryRgb(Value(key), out byte r, out byte g, out byte b) ? $"#{r:X2}{g:X2}{b:X2}" : "";
    }

    private string DiagramSkin(string part)
    {
        if (!Independent && !hasTeam && role == "skater" && part == "stick")
            return "Sticks/Customization/Customization_colors";
        if (role == "skater" && part == "socks") return AppearanceContract.SockStyle(Value, away);
        string a=away?" Away":"";
        string key=role=="goalie" ? part switch {
            "jersey"=>"Skin"+a,"pants"=>away?"Goalie Pants Away":"Goalie Pants Skin","helmet"=>"Helmet Skin","gloves"=>away?"Glove Away":"Glove Skin",
            "socks"=>away?"Pads Away":"Pads Skin","blocker"=>away?"Blocker Away":"Blocker Skin","stick"=>away?"Stick Away":"Stick Skin",_=>""
        } : part switch {"jersey" or "socks"=>"Body"+a,"sleeves"=>"Bicep"+a,"helmet"=>"Helmet"+a,"gloves"=>"Gloves"+a,"pants"=>"Pants"+a,"skates"=>"Skates"+a,"stick"=>"Stick",_=>""};
        string value=Value(key);
        return value;
    }
}
