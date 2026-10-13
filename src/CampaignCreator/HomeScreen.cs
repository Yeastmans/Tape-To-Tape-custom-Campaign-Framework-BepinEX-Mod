using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;

namespace CampaignCreator;

public sealed partial class MainWindow
{
    private Control editorShell = null!;
    private (string Kind, string File)? homeDraft;
    private sealed record ContentChoice(string Label, string Path) { public override string ToString() => Label; }
    public void ShowHome()
    {
        DiscardEditorChangesForNavigation();
        data = new GameData(root);
        campaignWorkspace = null;
        var muted = StudioTheme.Muted;
        var shell = new Grid { Margin = StudioTheme.PagePadding, RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = StudioTheme.SpaceLg };
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), RowDefinitions = new RowDefinitions("Auto") }; StudioTheme.Header(top);
        top.Children.Add(new StackPanel { Spacing = 5, Children = {
            new TextBlock { Text = "TAPE TO TAPE", FontSize = 23, FontWeight = FontWeight.Bold, Foreground = StudioTheme.Accent },
            new TextBlock { Text = "Campaign Studio", FontSize = 21, Foreground = muted }
        }});
        var campaignSelector = BuildHomeCampaignSelector(muted);
        campaignSelector.Width = 430;
        campaignSelector.VerticalAlignment = VerticalAlignment.Top;
        campaignSelector.Margin = new Thickness(24, 0, 0, 0);
        Grid.SetColumn(campaignSelector, 1); top.Children.Add(campaignSelector);
        shell.Children.Add(top);
        var center = new StackPanel { Spacing = StudioTheme.SpaceLg, VerticalAlignment = VerticalAlignment.Center };
        center.Children.Add(new StackPanel { Spacing = StudioTheme.SpaceSm, Children = {
            new TextBlock { Text = "What would you like to create?", FontSize = 30, FontWeight = FontWeight.Bold },
            new TextBlock { Text = "Start something new or edit your existing content.", FontSize = 18, Foreground = muted }
        }});
        var cards = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), ColumnSpacing = StudioTheme.SpaceLg };
        string[] titles = ["Campaigns", "Players", "Teams"];
        string[] descriptions = ["Build your journey, squads and map.", "Edit appearance, equipment and skills.", "Build playable squads and campaign opponents."];
        for (int i = 0; i < titles.Length; i++)
        {
            string title = titles[i];
            var choices = i switch {
                1 => new[] { new ContentChoice("Skater", "skater"), new ContentChoice("Goalie", "goalie") },
                2 => new[] { new ContentChoice("Player team", "squad"), new ContentChoice("Campaign opponent", "team") },
                _ => new[] { new ContentChoice("Campaign", "campaign") }
            };
            string selectedKind = choices[0].Path;
            string Kind() => selectedKind;
            var edit = Button("Edit existing", async () => await ChooseContent(Kind(), false));
            void RefreshEdit() => edit.IsVisible = Kind() == "squad" ? PlayerTeamChoices().Count > 0 : LibraryContent(Kind()).Count > 0 || CampaignChoices().Any(c => Kind() == "campaign" || ExistingItems(c, Kind()).Count > 0);
            RefreshEdit();
            var type = new Grid { ColumnDefinitions = new ColumnDefinitions(choices.Length == 1 ? "*" : "*,*"), ColumnSpacing = StudioTheme.SpaceXs, MinHeight = 58 };
            var switches = new List<Avalonia.Controls.Primitives.ToggleButton>();
            foreach (var choice in choices)
            {
                var toggle = new Avalonia.Controls.Primitives.ToggleButton {
                    Name = "HomeType" + title + choice.Path,
                    Content = new TextBlock { Text = choice.Label, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, FontSize = 15 },
                    Padding = new Thickness(8, 8), MinHeight = 58,
                    HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center, IsChecked = choice.Path == selectedKind
                };
                toggle.Classes.Add("segment");
                void RefreshSwitches()
                {
                    foreach (var item in switches)
                    {
                        bool selected = item.Name == "HomeType" + title + selectedKind;
                        item.IsChecked = selected;
                    }
                }
                toggle.Click += (_, _) => { selectedKind = choice.Path; RefreshSwitches(); RefreshEdit(); };
                Grid.SetColumn(toggle, switches.Count); switches.Add(toggle); type.Children.Add(toggle); RefreshSwitches();
            }
            var stack = new StackPanel { Spacing = StudioTheme.SpaceMd };
            stack.Children.Add(new TextBlock { Text = title, FontSize = 28, FontWeight = FontWeight.Bold });
            stack.Children.Add(new TextBlock { Text = descriptions[i], Foreground = muted, FontSize = 15, TextWrapping = TextWrapping.Wrap, Height = 44 });
            stack.Children.Add(i == 0 ? new Border { MinHeight = 58 } : type);
            var create = Button("Create new", async () => await CreateFromHome(Kind()));
            create.Name = "HomeCreate" + title;
            StudioTheme.Primary(create); create.FontSize = 18; create.FontWeight = FontWeight.Bold;
            create.HorizontalContentAlignment = HorizontalAlignment.Center; create.VerticalContentAlignment = VerticalAlignment.Center; create.MinHeight = 44; create.Padding = new Thickness(12, 8);
            edit.Name = "HomeEdit" + title;
            edit.HorizontalContentAlignment = HorizontalAlignment.Center; edit.VerticalContentAlignment = VerticalAlignment.Center; edit.FontSize = 18; edit.MinHeight = 44; edit.Padding = new Thickness(12, 8);

            stack.Children.Add(create); stack.Children.Add(edit);
            if (i != 0)
            {
                var importButton = Button("Import", async () =>
                {
                    string kind = Kind();
                    if (kind == "skater")
                    {
                        string? source = await ContentImport.Pick(this, root, kind);
                        if (source != null) await CreateFromHome(kind, source);
                        return;
                    }
                    await ChooseContent(kind, true, import: true);
                });
                importButton.Name = "HomeImport" + title; importButton.HorizontalContentAlignment = HorizontalAlignment.Center;
                stack.Children.Add(importButton);
            }
            else
            {
                var copyVanilla = Button("Copy vanilla campaign", async () => await CopyVanillaFromHome());
                copyVanilla.Name = "HomeCopyVanillaCampaign"; copyVanilla.HorizontalContentAlignment = HorizontalAlignment.Center;
                stack.Children.Add(copyVanilla);
                var importCampaign = Button("Import", async () => await ShowCampaignImport());
                importCampaign.Name = "HomeImportCampaigns";
                var uploadCampaign = Button("Upload", async () => await ShowCampaignUpload());
                uploadCampaign.Name = "HomeUploadCampaigns"; uploadCampaign.IsEnabled = CampaignChoices().Count > 0;
                ToolTip.SetTip(uploadCampaign, "Share a campaign in the community folder");
                foreach (var b in new[] { importCampaign, uploadCampaign }) b.HorizontalContentAlignment = HorizontalAlignment.Center;
                stack.Children.Add(new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = StudioTheme.SpaceSm, Children = { importCampaign, WithColumn(uploadCampaign, 1) } });
            }
            var card = new Border { Background = StudioTheme.Surface, BorderBrush = StudioTheme.Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(16), Child = stack };
            Grid.SetColumn(card, i); cards.Children.Add(card);
        }
        center.Children.Add(cards);
        var defaults = Button("Set default · look for new players and teams", ShowDefaultLook);
        defaults.Name = "HomeSetDefault";
        defaults.FontSize = 18;
        defaults.MinHeight = 48;
        center.Children.Add(defaults);
        var heads = Button("Head Editor · create and edit head PNGs", async () =>
        {
            var setup = new HeadProjectStartWindow(root);
            if (await setup.ShowDialog<HeadProject?>(this) is { } project)
                Content = new HeadEditorView(this, root, ShowHome, project);
        });
        heads.Name = "HomeHeadEditor";
        heads.FontSize = 18;
        heads.MinHeight = 48;
        center.Children.Add(heads);
        var scaled = new ScrollViewer { Content = center, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        center.MaxWidth = 1000;
        Grid.SetRow(scaled, 1); shell.Children.Add(scaled);
        var help = Button("Help", ShowPageHelp); help.Name = "PageHelp";
        var learning = new WrapPanel { Orientation = Orientation.Horizontal };
        var guide = Button("Studio guide", () => new StudioGuideWindow().Show(this)); guide.Name = "HomeGuide";
        var bug = Button("Report a bug", async () => await BugReport.Show(this, root)); bug.Name = "HomeReportBug";
        var linkStatus = new TextBlock { Name = "HomeLinkStatus", IsVisible = false, TextWrapping = TextWrapping.Wrap, Foreground = StudioTheme.Muted };
        void OpenCommunityLink(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                linkStatus.IsVisible = false;
            }
            catch (Exception)
            {
                linkStatus.Text = "Could not open your browser. Open this link manually: " + url;
                linkStatus.IsVisible = true;
            }
        }
        var discord = Button("Join Discord for help", () => OpenCommunityLink(BugReport.Discord)); discord.Name = "HomeDiscord";
        var github = Button("GitHub", () => OpenCommunityLink(BugReport.Repository)); github.Name = "HomeGitHub";
        var readArt = Button("Read game artwork", async () => await EnsureGameArt(force: true)); readArt.Name = "HomeReadGameArt";
        var updateMod = Button("Update mod compatibility", async () => { if(!readingGameArt) await EnsureModCompatibility(force: true); }); updateMod.Name = "HomeUpdateModCompatibility";
        var updates = Button("Check for updates", async () => await CheckForStudioUpdates(manual: true)); updates.Name = "HomeCheckForUpdates";
        ToolTip.SetTip(updates, "Check GitHub for a newer Studio and mod installer, including beta updates. Installation always asks first.");
        ToolTip.SetTip(readArt, "Rebuild heads, logos, equipment, stadiums and map pictures from your installed copy of Tape to Tape");
        ToolTip.SetTip(bug, "Open a pre-filled bug report on the mod's GitHub page");
        ToolTip.SetTip(discord, "Open the community Discord invite in your browser for help with the mod and Studio");
        ToolTip.SetTip(github, "Open the project's GitHub repository in your browser for downloads, documentation and issues");
        foreach (var action in new[] { help, guide, bug, discord, github, readArt, updateMod, updates }) { action.Margin = new Thickness(0, 0, 12, 8); learning.Children.Add(action); }
        compatibilityHomeStatus = new TextBlock { Name="HomeModCompatibilityStatus",Text=compatibilityStatus.Length>0?compatibilityStatus:"Game compatibility is checked using the installed mod. Check for updates downloads newer Studio and mod releases from GitHub.",TextWrapping=TextWrapping.Wrap,Foreground=StudioTheme.Muted };
        releaseHomeStatus = new TextBlock { Name="HomeReleaseUpdateStatus",Text=releaseStatus,TextWrapping=TextWrapping.Wrap,Foreground=StudioTheme.Muted };
        var bottom = new Border { BorderBrush = StudioTheme.Border, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 16, 0, 0), Child = new StackPanel { Spacing=StudioTheme.SpaceSm,Children={learning,linkStatus,releaseHomeStatus,compatibilityHomeStatus} } };
        Grid.SetRow(bottom, 2); shell.Children.Add(bottom); Content = shell;
    }

    private List<ContentChoice> CampaignChoices() => Directory.Exists(Path.Combine(root, "campaigns"))
        ? Directory.GetDirectories(Path.Combine(root, "campaigns")).Where(d => File.Exists(Path.Combine(d, "campaign.txt"))).Order().Select(d => new ContentChoice(Path.GetFileName(d), d)).ToList() : [];

    private Control BuildHomeCampaignSelector(IBrush muted)
    {
        var choices = new List<ContentChoice> { new("None (base game)", "default") };
        bool IsBaseGame(string value) => new[] { "default", "none", "off", "base game" }.Contains(value, StringComparer.OrdinalIgnoreCase);
        choices.AddRange(CampaignChoices().Where(c => !IsBaseGame(c.Label)).Select(c => new ContentChoice(c.Label, c.Label)));
        string activePath = Path.Combine(root, "active.txt");
        var message = new TextBlock { Foreground = muted, TextWrapping = TextWrapping.Wrap,
            Text = "Used on the next game launch. Restart the game after changing campaigns." };
        void SaveSelection(string value)
        {
            Directory.CreateDirectory(root);
            string temporary = activePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, "# Campaign to play; default uses the base game.\nActive Campaign = " + value + "\n");
                File.Move(temporary, activePath, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        string active = "default";
        try
        {
            string? line = File.Exists(activePath) ? File.ReadLines(activePath).Select(l => l.Trim())
                .FirstOrDefault(l => l.Length > 0 && !l.StartsWith('#')) : null;
            if (line != null) active = line[(line.IndexOf('=') + 1)..].Trim();
            if (string.IsNullOrWhiteSpace(active)) active = "default";
            if (line == null) SaveSelection("default");
        }
        catch (Exception ex) { message.Text = "Could not read or save the campaign selection: " + ex.Message; }
        if (IsBaseGame(active)) active = "default";
        var selected = choices.FirstOrDefault(c => c.Path.Equals(active, StringComparison.OrdinalIgnoreCase));
        if (selected == null)
        {
            selected = new ContentChoice(active + " (unavailable)", active);
            choices.Add(selected);
            message.Text = "The saved campaign is unavailable in this content folder. Choose a campaign or None (base game).";
        }
        var picker = new ComboBox { Name = "HomeSelectCampaign", ItemsSource = choices, SelectedItem = selected,
            HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 44, FontSize = 18 };
        bool restoring = false;
        picker.SelectionChanged += (_, _) =>
        {
            if (restoring || picker.SelectedItem is not ContentChoice choice) return;
            try
            {
                SaveSelection(choice.Path);
                selected = choice;
                message.Text = "Saved: " + choice.Label + ". Restart the game to use this selection.";
            }
            catch (Exception ex)
            {
                restoring = true; picker.SelectedItem = selected; restoring = false;
                message.Text = "Could not save the campaign selection: " + ex.Message;
            }
        };
        var launch = Button("Launch Tape to Tape", async () => await LaunchGameWithUpdateCheck());
        launch.Name = "HomeLaunchGame";
        return new StackPanel { Spacing = StudioTheme.SpaceSm, Children = {
            new TextBlock { Text = "Select campaign", FontSize = 20, FontWeight = FontWeight.SemiBold }, picker, message,
            launch
        }};
    }

    private async Task CreateFromHome(string kind, string source = "")
    {
        if (kind == "campaign") { await ChooseContent(kind, true); return; }
        string draft = Path.Combine(root, ".studio-drafts", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(draft);
        if (kind != "squad") File.WriteAllText(Path.Combine(draft, "campaign.txt"), "Squad Template = Basic Squad\n");
        string name = kind switch { "squad" => "New player team", "team" => "New opponent team", "goalie" => "New goalie", _ => "New skater" };
        string file = kind == "squad"
            ? CreatorContent.Create(draft, "player-team", name, "", null, null, defaultLookRoot: root)
            : CreatorContent.Create(root, kind == "team" ? "team" : "player", source.Length > 0 ? new CampaignCore.CampaignDocument(File.ReadAllText(source)).Get("Name") : name, source, draft, null, initialPlayerPosition: kind == "goalie" ? "Goalie" : "Center");
        if (kind is "skater" or "goalie")
        {
            var doc = new CampaignCore.CampaignDocument(File.ReadAllText(file));
            if (source.Length == 0) doc.Set("Position", kind == "goalie" ? "Goalie" : "Center"); doc.Save(file);
        }
        campaignWorkspace = null;
        homeDraft = (kind, file);
        paths = CampaignNavigation.Load(draft); search.Text = ""; FilterFiles();
        OpenFile(file); Content = editorShell; MarkDirty();
        status.Text = kind is "squad" or "team" or "skater" or "goalie" or "player" ? "New draft · save to your library, then import into a campaign." : "New draft · edit now, choose a campaign when you save.";
    }

    private List<ContentChoice> LibraryContent(string kind)
    {
        if (kind is not ("player" or "skater" or "goalie" or "team")) return new();
        string folder = Path.Combine(root, "library", kind == "team" ? "teams" : "players");
        return Directory.Exists(folder)
            ? Directory.GetFiles(folder, kind == "team" ? "team.txt" : "*.txt", SearchOption.AllDirectories)
                .Where(f => ContentImport.Matches(f, kind))
                .Select(f => new ContentChoice(new CampaignCore.CampaignDocument(File.ReadAllText(f)).Get(kind == "team" ? "Team Name" : "Name"), f)).ToList()
            : new();
    }
    private static List<ContentChoice> ExistingItems(ContentChoice c, string kind) => CampaignNavigation.Load(c.Path).SelectMany(n => n.Descendants())
            .Where(n => n.FilePath != null && (kind is "team" or "squad" ? (kind == "team" ? n.Context.StartsWith("Opponent ") : n.Context == "Custom squad") && Path.GetFileName(n.FilePath).Equals("team.txt", StringComparison.OrdinalIgnoreCase)
                : !Path.GetFileName(n.FilePath).Equals("team.txt", StringComparison.OrdinalIgnoreCase) && Path.GetDirectoryName(n.FilePath) != c.Path))
            .Where(n => !(kind is "player" or "skater" or "goalie") || ContentImport.Matches(n.FilePath!, kind))
            .Select(n => new ContentChoice($"{n.DisplayName} · {n.Context} · {c.Label}", n.FilePath!)).ToList();

    private async Task ChooseContent(string kind, bool create, string? preferredTeam = null, bool import = false, string? draftSource = null)
    {
        if (kind == "squad") { await ChooseLibraryPlayerTeam(create, import, draftSource); return; }
        if (kind == "campaign" && create)
        {
            OpenCampaignDraft(Path.Combine(root, ".studio-drafts", Guid.NewGuid().ToString("N")));
            return;
        }
        bool isPlayer = kind is "player" or "skater" or "goalie";
        string? importSource = draftSource ?? (import ? await ContentImport.Pick(this, root, isPlayer ? kind : "team") : null);
        if (import && importSource == null) return;
        string kindLabel = kind switch { "team" => "campaign opponent team", "squad" => "player team", _ => kind };
        var dialog = new Window { Title = (draftSource != null ? "Save " : import ? "Import " : create ? "New " : "Edit ") + kindLabel, Width = 560, Height = 530, MinWidth = 480, MinHeight = 480, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Background };
        var choices = CampaignChoices();
        const string baseGameSource = "::base-game::";
        const string librarySource = "::content-library::";
        if (isPlayer && !create) choices.Insert(0, new ContentChoice("Base game", baseGameSource));
        if (!create && LibraryContent(kind).Count > 0) choices.Insert(0, new ContentChoice(isPlayer ? "Player library" : "Opponent team library", librarySource));
        var working = campaignWorkspace is { IsNew: true } draft ? new ContentChoice("Unsaved campaign", draft.WorkingDirectory) : null;
        if (kind != "campaign" && working != null) choices.Insert(0, working);
        if (kind == "team" && Content == campaignWorkspace && campaignWorkspace != null)
            choices = choices.Where(c => c.Path == campaignWorkspace.WorkingDirectory).ToList();
        var campaignPicker = new ComboBox { ItemsSource = choices, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        if (kind != "campaign" && campaigns.SelectedItem is string currentCampaign)
            campaignPicker.SelectedItem = choices.FirstOrDefault(c => c.Label == currentCampaign) ?? choices.FirstOrDefault();
        if (kind != "campaign" && working != null) campaignPicker.SelectedItem = working;
        var itemPicker = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        var destination = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        var template = new ComboBox { ItemsSource = SquadTemplateCatalog.All, HorizontalAlignment = HorizontalAlignment.Stretch };
        var name = new TextBox { Watermark = "Name your " + kindLabel, Text = importSource == null ? "" : new CampaignCore.CampaignDocument(System.IO.File.ReadAllText(importSource)).Get(isPlayer ? "Name" : "Team Name") };
        var feedback = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brush.Parse("#FFD18C") };
        void Refresh()
        {
            if (campaignPicker.SelectedItem is not ContentChoice c) return;
            if (kind == "campaign") return;
            if (!create)
            {
                itemPicker.ItemsSource = c.Path == librarySource ? LibraryContent(kind)
                    : c.Path == baseGameSource ? ContentImport.Entries(root, kind, ContentImport.BaseGame).Select(e => new ContentChoice(e.Name, e.File)).ToList()
                    : ExistingItems(c, kind);
                itemPicker.SelectedIndex = 0;
            }
            if (c.Path == librarySource || c.Path == baseGameSource) return;
            if (kind == "squad" && create)
            {
                try { template.SelectedItem = SquadTemplateCatalog.Find(SquadTemplateCatalog.ResolveCampaign(c.Path)); feedback.Text = "Choose this team's starting preset. Its behavior controls can be changed in the team editor."; }
                catch (InvalidOperationException ex) { template.SelectedItem = null; feedback.Text = ex.Message; }
                template.IsEnabled = true;
            }
            destination.ItemsSource = new[] { new ContentChoice("Free agents", "") }.Concat(CampaignNavigation.Load(c.Path).SelectMany(n => n.Descendants()).Where(n => n.FilePath != null && Path.GetFileName(n.FilePath) == "team.txt").Select(n => new ContentChoice(n.DisplayName, Path.GetDirectoryName(n.FilePath!)!))).ToList();
            destination.SelectedItem = (destination.ItemsSource as IEnumerable<ContentChoice>)?.FirstOrDefault(c => c.Path == preferredTeam);
            if (destination.SelectedItem == null) destination.SelectedIndex = 0;
        }
        campaignPicker.SelectionChanged += (_, _) => Refresh(); Refresh();
        var panel = new StackPanel { Margin = new Thickness(28), Spacing = StudioTheme.SpaceMd };
        panel.Children.Add(new TextBlock { Text = dialog.Title, FontSize = 26, FontWeight = FontWeight.Bold });
        panel.Children.Add(new TextBlock { Text = create ? "Name your new " + kindLabel + " and choose where to save it." : "Choose the content you want to edit.", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = "Campaign" }); panel.Children.Add(campaignPicker);
        if (kind != "campaign" && !create) { panel.Children.Add(new TextBlock { Text = "Choose " + kindLabel }); panel.Children.Add(itemPicker); }
        if (create && kind == "squad") { panel.Children.Add(new TextBlock { Text = "Team template" }); panel.Children.Add(template); }
        if (create && isPlayer) { panel.Children.Add(new TextBlock { Text = "Add to" }); panel.Children.Add(destination); }
        if (create) panel.Children.Add(name);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = StudioTheme.SpaceMd };
        actions.Children.Add(Button("Cancel", () => dialog.Close()));
        actions.Children.Add(Button(draftSource != null ? "Save " + kindLabel : create ? "Create " + kindLabel : "Open " + kindLabel, async () =>
        {
            try
            {
                if (campaignPicker.SelectedItem is not ContentChoice campaign) throw new ArgumentException("Choose a campaign first.");
                string file;
                if (kind == "campaign") file = Path.Combine(campaign.Path, "campaign.txt");
                else file = create ? "" : (itemPicker.SelectedItem as ContentChoice)?.Path ?? throw new ArgumentException("Choose a " + kind + " first.");
                if (campaign.Path == librarySource)
                {
                    campaignWorkspace = null;
                    OpenFile(file); Content = editorShell; dialog.Close(); return;
                }
                if (campaign.Path == baseGameSource)
                {
                    await CreateFromHome(kind, file);
                    status.Text = "Custom copy of a base-game player · save to your player library.";
                    dialog.Close(); return;
                }
                string campaignPath = campaign.Path;
                if (create)
                {
                    string? teamPath = (destination.SelectedItem as ContentChoice)?.Path;
                    file = kind == "squad"
                        ? Path.Combine(SquadCopy.Create(importSource == null ? "" : Path.GetDirectoryName(importSource)!, campaign.Path, CreatorContent.ValidateName(name.Text ?? ""),
                            (template.SelectedItem as SquadTemplateOption)?.Name ?? throw new ArgumentException("Choose this team's template preset."), root), "team.txt")
                        : CreatorContent.Create(root, isPlayer ? "player" : kind, name.Text ?? "", importSource == null ? "" : isPlayer ? importSource : Path.GetDirectoryName(importSource)!, campaign.Path, string.IsNullOrEmpty(teamPath) ? null : teamPath, initialPlayerPosition: kind == "goalie" ? "Goalie" : "Center");
                    if (isPlayer && importSource == null) { var doc = new CampaignCore.CampaignDocument(System.IO.File.ReadAllText(file)); doc.Set("Position", kind == "goalie" ? "Goalie" : ""); doc.Save(file); }
                    if (kind == "campaign") campaignPath = Path.GetDirectoryName(file)!;
                }
                if (working != null && campaignPath == working.Path)
                {
                    paths = CampaignNavigation.Load(campaignPath); FilterFiles(); search.Text = "";
                    OpenFile(file); Content = editorShell; dialog.Close(); return;
                }
                if (draftSource != null)
                {
                    homeDraft = null; dirty = false;
                    campaigns.IsEnabled = files.IsEnabled = true;
                }
                loading = true;
                campaigns.ItemsSource = CampaignChoices().Select(c => c.Label).ToArray();
                campaigns.SelectedItem = Path.GetFileName(campaignPath); loading = false;
                await LoadCampaign();
                search.Text = ""; OpenFile(file); if (kind != "campaign") Content = editorShell; dialog.Close();
            }
            catch (Exception ex) { feedback.Text = ex.Message; }
        }));
        panel.Children.Add(actions); panel.Children.Add(feedback); dialog.Content = new ScrollViewer { Content = panel };
        await dialog.ShowDialog(this);
    }
}




