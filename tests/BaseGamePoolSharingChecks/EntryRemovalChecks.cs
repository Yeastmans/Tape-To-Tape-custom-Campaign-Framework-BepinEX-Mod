using System.Reflection;
using System.IO.Compression;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CampaignCore;
using CampaignCreator;

internal static class EntryRemovalChecks
{
    private static void Pump(Func<bool> ready)
    {
        var end = DateTime.UtcNow.AddSeconds(10);
        do { Dispatcher.UIThread.RunJobs(); if (ready()) return; Thread.Sleep(10); } while (DateTime.UtcNow < end);
        throw new Exception("Removal UI did not finish.");
    }
    private static Button FindButton(Control view, string name) => view.GetLogicalDescendants().OfType<Button>().First(b => b.Name == name);
    private static void Click(Control view, string name) => FindButton(view, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    internal static void Run(string sandbox, Action<bool, string> check)
    {
        var removal = typeof(App).Assembly.GetType("CampaignCreator.ContentRemoval");
        check(removal != null, "local entries have a scoped removal operation");
        string root = Path.Combine(sandbox, "removal"), library = Path.Combine(root, "library", "players"); Directory.CreateDirectory(library);
        string original = Path.Combine(library, "Original.txt"); File.WriteAllText(original, "Name = Original\nPosition = Center\n");
        string pool = Path.Combine(root, "pool"); Directory.CreateDirectory(pool);
        string old = Path.Combine(pool, "old.txt"), other = Path.Combine(pool, "other.txt"); File.Copy(original, old); File.Copy(original, other);
        SelectedPlayerPool.Save(pool, ["old.txt", "other.txt"]);
        object? Call(string name, params object[] args) => removal!.GetMethod(name, BindingFlags.Public | BindingFlags.Static)!.Invoke(null, args);
        foreach (string name in new[] { "_Player.txt", ".Player.txt" })
        {
            File.Copy(original, Path.Combine(pool, name));
            bool accepted = true;
            try { Call("ArchiveFile", pool, name, (Action)(() => { })); }
            catch (TargetInvocationException) { accepted = false; }
            check(accepted && !File.Exists(Path.Combine(pool, name)), "valid existing player names can be removed: " + name);
        }
        string archived = (string)Call("RemovePoolFile", pool, "old.txt", new[] { "old.txt", "other.txt" })!;
        check(!File.Exists(old) && File.Exists(archived) && File.Exists(original) && File.Exists(other), "removal archives only the chosen pool copy and keeps its original and neighbors");
        check(SelectedPlayerPool.Read(pool)!.Select(Path.GetFileName).SequenceEqual(["other.txt"]), "removal persists a selection without the removed file");
        File.Copy(original, old);
        try { Call("ArchiveFile", pool, "old.txt", (Action)(() => throw new IOException("write failed"))); throw new Exception("Expected persistence rejection"); }
        catch (TargetInvocationException ex) when (ex.InnerException is IOException) { }
        check(File.Exists(old) && File.ReadAllText(old) == File.ReadAllText(original), "failed selection persistence restores the removed copy");
        SelectedPlayerPool.Save(pool, ["old.txt", "other.txt"]);
        string slotsFile = Path.Combine(pool, "custom-slots.list"); File.WriteAllLines(slotsFile, ["old.txt", "other.txt", ""]);
        byte[] slotsBefore = File.ReadAllBytes(slotsFile);
        using (var held = new FileStream(Path.Combine(pool, SelectedPlayerPool.Manifest), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            try
            {
                Call("ArchiveFile", pool, "old.txt", (Action)(() => { File.WriteAllLines(slotsFile, ["", "other.txt", ""]); SelectedPlayerPool.Save(pool, ["other.txt"]); }));
                throw new Exception("Expected locked manifest failure");
            }
            catch (TargetInvocationException) { }
        }
        check(File.Exists(old) && File.ReadAllBytes(slotsFile).SequenceEqual(slotsBefore), "a locked unchanged selection manifest cannot prevent rollback of modified slot metadata");
        SelectedPlayerPool.Save(pool, ["other.txt"]);
        foreach (string path in new[] { "../library/players/Original.txt", original, "missing.txt" })
        {
            bool rejected = false;
            try { Call("ArchiveFile", pool, path, (Action)(() => { })); }
            catch (TargetInvocationException ex) when (ex.InnerException is ArgumentException or IOException) { rejected = true; }
            check(rejected && File.Exists(original), "removal rejects paths outside the owned list or missing entries: " + path);
        }

        var selection = new BaseGamePoolSelection { Enabled = true };
        foreach (string kind in BaseGamePlayerPools.Kinds)
        {
            string directory = BaseGamePlayerPools.PoolDirectory(root, kind); Directory.CreateDirectory(directory);
            File.Copy(original, Path.Combine(directory, "one.txt")); File.Copy(original, Path.Combine(directory, "two.txt"));
            selection.Pools[kind] = ["one.txt", "two.txt"];
        }
        BaseGamePlayerPools.Save(root, selection);
        var profile = new BaseGamePoolsContent(root, null);
        var owner = new Window { Width = 1200, Height = 900 };
        var view = new BaseGamePoolsView(profile, () => null, () => Task.FromResult<string?>(null), _ => { }, () => { });
        owner.Content = view; owner.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            Click(view, "BaseGamePoolRemove_draft_pool_0"); Pump(() => owner.OwnedWindows.Any());
            var confirmation = owner.OwnedWindows.Single();
            check(FindButton(confirmation, "CancelRemoveEntry").IsDefault, "entry removal defaults to Cancel");
            Click(confirmation, "CancelRemoveEntry"); Pump(() => !owner.OwnedWindows.Any());
            check(profile.Candidates("draft_pool").Length == 2 && BaseGamePlayerPools.Load(root).Pools["draft_pool"].Length == 2, "cancelling a removal preserves pool rows and saved selection");
            Click(view, "BaseGamePoolRemove_draft_pool_0"); Pump(() => owner.OwnedWindows.Any());
            Click(owner.OwnedWindows.Single(), "ConfirmRemoveEntry");
            Pump(() => !owner.OwnedWindows.Any()); Dispatcher.UIThread.RunJobs();
            check(profile.Candidates("draft_pool").SequenceEqual(["two.txt"]) && new BaseGamePoolsContent(root, null).Candidates("draft_pool").SequenceEqual(["two.txt"]), "confirmed removal disappears from the pool and stays removed after reopening");
            check(BaseGamePlayerPools.Load(root).Pools["draft_pool"].SequenceEqual(["two.txt"]) && File.Exists(original), "confirmed pool removal updates selection and preserves the library original");
            Click(view, "BaseGamePoolRemove_draft_pool_0"); Pump(() => owner.OwnedWindows.Any());
            Click(owner.OwnedWindows.Single(), "ConfirmRemoveEntry"); Pump(() => !owner.OwnedWindows.Any()); Dispatcher.UIThread.RunJobs();
            check(profile.Candidates("draft_pool").SequenceEqual(["two.txt"]) && BaseGamePlayerPools.Load(root).Pools["draft_pool"].Length == 1,
                "removing the last selected Base Game candidate is rejected without data loss");
        }
        finally { owner.Close(); }

        string unused = Path.Combine(BaseGamePlayerPools.PoolDirectory(root, "free_agents"), "unchecked.txt"); File.Copy(original, unused);
        profile.RemoveCandidate("free_agents", "unchecked.txt");
        check(!profile.Candidates("free_agents").Contains("unchecked.txt") && BaseGamePlayerPools.Load(root).Pools["free_agents"].Length == 2,
            "unchecked pool entries can be removed without changing active candidates");
        string stars = BaseGamePlayerPools.PoolDirectory(root, "superstars");
        var native = new CampaignDocument(File.ReadAllText(Path.Combine(stars, "one.txt")));
        native.Set(VanillaCopyData.PlayerId, "native-test"); native.Set(VanillaCopyData.PlayerAsset, "Native test"); native.Save(Path.Combine(stars, "one.txt")); native.Save(Path.Combine(stars, "older-native.txt"));
        profile.RemoveCandidate("superstars", "one.txt");
        check(profile.Candidates("superstars").SequenceEqual(["two.txt"]), "removing a native candidate also removes its hidden unused older copies");

        var poolOwner = new Window { Width = 1100, Height = 850 }; poolOwner.Show();
        try
        {
            var nativeEditor = new NativePlayerPoolEditor(poolOwner, root, () => pool, _ => { }, () => { }); poolOwner.Content = nativeEditor; Dispatcher.UIThread.RunJobs();
            foreach (string key in new[] { "old.txt", "other.txt" })
            {
                nativeEditor.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "RemoveNativePoolCandidate" && (string?)b.Tag == key).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Pump(() => poolOwner.OwnedWindows.Any()); Click(poolOwner.OwnedWindows.Single(), "ConfirmRemoveEntry"); Pump(() => !poolOwner.OwnedWindows.Any()); Dispatcher.UIThread.RunJobs();
                check(!File.Exists(Path.Combine(pool, key)), "native candidate lists remove checked or unchecked entries: " + key);
            }
            check(SelectedPlayerPool.Read(pool)!.Length == 0, "campaign candidate lists retain an explicitly empty selection after removing their last entry");
            string squad = Path.Combine(root, "squad"), agents = Path.Combine(squad, "player_teams", "free_agents"); Directory.CreateDirectory(agents);
            File.Copy(original, Path.Combine(agents, "copy.txt")); SelectedPlayerPool.Save(agents, ["copy.txt"]);
            var squadType = typeof(App).Assembly.GetType("CampaignCreator.SquadFreeAgentEditor")!;
            var squadEditor = (Control)Activator.CreateInstance(squadType, BindingFlags.Instance | BindingFlags.NonPublic, null,
                [poolOwner, root, squad, (Action<string>)(_ => { }), (Action)(() => { })], null)!;
            poolOwner.Content = squadEditor; Dispatcher.UIThread.RunJobs(); Click(squadEditor, "RemoveSquadFreeAgent");
            Pump(() => poolOwner.OwnedWindows.Any()); Click(poolOwner.OwnedWindows.Single(), "ConfirmRemoveEntry"); Pump(() => !poolOwner.OwnedWindows.Any());
            check(!File.Exists(Path.Combine(agents, "copy.txt")) && SelectedPlayerPool.Read(agents)!.Length == 0, "a squad's own free-agent removal persists an empty pool and removes its local copy");
            string slotPool = Path.Combine(squad, "player_teams", "superstars"); Directory.CreateDirectory(slotPool);
            File.Copy(original, Path.Combine(slotPool, "copy.txt")); SelectedPlayerPool.Save(slotPool, ["copy.txt"]);
            var slots = new StartingPoolEditor(poolOwner, root, () => squad, "superstars", _ => { }, () => { });
            poolOwner.Content = slots; Dispatcher.UIThread.RunJobs();
            slots.GetLogicalDescendants().OfType<Button>().Single(b => b.Content?.ToString() == "Remove…").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump(() => poolOwner.OwnedWindows.Any()); Click(poolOwner.OwnedWindows.Single(), "ConfirmRemoveEntry"); Pump(() => !poolOwner.OwnedWindows.Any());
            check(!File.Exists(Path.Combine(slotPool, "copy.txt")) && SelectedPlayerPool.Read(slotPool)!.Length == 0 && File.ReadAllLines(Path.Combine(slotPool, "custom-slots.list")).All(string.IsNullOrEmpty),
                "starting-slot removal clears the saved slot and its copied player file");
        }
        finally { foreach (var window in poolOwner.OwnedWindows.ToArray()) window.Close(); poolOwner.Close(); }

        string campaign = Path.Combine(root, "campaigns", "Removal campaign"), teams = Path.Combine(campaign, "player_teams");
        Directory.CreateDirectory(teams);
        File.WriteAllText(Path.Combine(campaign, "campaign.txt"), "Act Sequence = 1\nMaps = Basic\nUse Player Teams = yes\nCustom Squads Only = no\n");
        foreach (string name in new[] { "First", "Second", "Third" })
        {
            string teamFolder = Path.Combine(teams, name); Directory.CreateDirectory(teamFolder);
            File.WriteAllText(Path.Combine(teamFolder, "team.txt"), "Team Name = " + name + "\nSquad Template = Basic Squad\n");
        }
        Call("ArchiveTeam", teams, "First");
        check(CampaignNavigation.Load(campaign).Single(n => n.DisplayName == "Custom Squads").Children.Select(n => n.DisplayName).SequenceEqual(["Second", "Third"]),
            "removed teams and their archive container stay out of campaign navigation");
        check(!Directory.Exists(Path.Combine(teams, "_removed_entries")), "team archives are outside the native runtime's player-team discovery directory");
        var campaignOwner = new Window { Width = 1000, Height = 850 }; campaignOwner.Show();
        try
        {
            var workspace = new CampaignWorkspace(campaignOwner, root, campaign, () => { }, _ => { }, () => Task.CompletedTask);
            var roster = (Control)typeof(CampaignWorkspace).GetMethod("RosterPage", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(workspace, [true, (Func<Task>)(() => Task.CompletedTask)])!;
            campaignOwner.Content = roster; Dispatcher.UIThread.RunJobs();
            Button RemoveTeam(string name) => roster.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "RemoveCampaignPlayerTeam" && (string?)b.Tag == Path.Combine(teams, name, "team.txt"));
            RemoveTeam("Second").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(() => campaignOwner.OwnedWindows.Any());
            Click(campaignOwner.OwnedWindows.Single(), "CancelRemoveEntry"); Pump(() => !campaignOwner.OwnedWindows.Any());
            check(Directory.Exists(Path.Combine(teams, "Second")), "cancelling campaign player-team removal leaves its folder intact");
            RemoveTeam("Second").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(() => campaignOwner.OwnedWindows.Any());
            Click(campaignOwner.OwnedWindows.Single(), "ConfirmRemoveEntry"); Pump(() => !campaignOwner.OwnedWindows.Any()); Dispatcher.UIThread.RunJobs();
            check(!Directory.Exists(Path.Combine(teams, "Second")) && CampaignNavigation.Load(campaign).Single(n => n.DisplayName == "Custom Squads").Children.Count == 1,
                "confirmed player-team removal updates the campaign list and preserves its other team");
            var config = new CampaignDocument(File.ReadAllText(Path.Combine(campaign, "campaign.txt"))); config.Set("Custom Squads Only", "yes"); config.Save(Path.Combine(campaign, "campaign.txt"));
            workspace = new CampaignWorkspace(campaignOwner, root, campaign, () => { }, _ => { }, () => Task.CompletedTask);
            roster = (Control)typeof(CampaignWorkspace).GetMethod("RosterPage", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(workspace, [true, (Func<Task>)(() => Task.CompletedTask)])!;
            campaignOwner.Content = roster; Dispatcher.UIThread.RunJobs();
            RemoveTeam("Third").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            check(!campaignOwner.OwnedWindows.Any() && Directory.Exists(Path.Combine(teams, "Third")), "Custom teams only protects the last remaining playable team");
            Directory.CreateDirectory(Path.Combine(teams, "Fourth")); File.WriteAllText(Path.Combine(teams, "Fourth", "team.txt"), "Team Name = Fourth\nSquad Template = Basic Squad\n");
            workspace = new CampaignWorkspace(campaignOwner, root, campaign, () => { }, _ => { }, () => Task.CompletedTask);
            roster = (Control)typeof(CampaignWorkspace).GetMethod("RosterPage", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(workspace, [true, (Func<Task>)(() => Task.CompletedTask)])!;
            campaignOwner.Content = roster; Dispatcher.UIThread.RunJobs();
            Call("ArchiveTeam", teams, "Fourth"); // Another editor removes the other team after this page was drawn.
            RemoveTeam("Third").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(() => campaignOwner.OwnedWindows.Any());
            Click(campaignOwner.OwnedWindows.Single(), "ConfirmRemoveEntry"); Pump(() => !campaignOwner.OwnedWindows.Any()); Dispatcher.UIThread.RunJobs();
            check(Directory.Exists(Path.Combine(teams, "Third")), "a stale campaign page cannot remove the last team after another editor removes its neighbor");
        }
        finally { foreach (var window in campaignOwner.OwnedWindows.ToArray()) window.Close(); campaignOwner.Close(); }
        string zip = Path.Combine(root, "removed-entries.zip");
        typeof(App).Assembly.GetType("CampaignCreator.CampaignSharing")!.GetMethod("Zip", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [campaign, zip, root]);
        using (var archive = ZipFile.OpenRead(zip)) check(!archive.Entries.Any(e => e.FullName.Contains("_removed_entries", StringComparison.OrdinalIgnoreCase)), "campaign uploads exclude removal archives");
        var imported = ContentImport.Entries(root, "team", ContentImport.CampaignCustom);
        check(imported.Count(e => e.Name.StartsWith("First") || e.Name.StartsWith("Second")) == 0, "removed teams do not reappear in the import catalog");
        string skaterAgents = Path.Combine(teams, "free_agents"), goalieAgents = GoalieFreeAgents.DirectoryPath(campaign);
        Directory.CreateDirectory(skaterAgents); Directory.CreateDirectory(goalieAgents);
        File.Copy(original, Path.Combine(skaterAgents, "copy.txt"));
        File.WriteAllText(Path.Combine(goalieAgents, "keeper.txt"), "Name = Keeper\nPosition = Goalie\nCatching = 71\n");
        var agentOwner = new Window { Width = 1100, Height = 900 }; agentOwner.Show();
        try
        {
            var workspace = new CampaignWorkspace(agentOwner, root, campaign, () => { }, _ => { }, () => Task.CompletedTask);
            var page = (Control)typeof(CampaignWorkspace).GetMethod("FreeAgentsPage", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(workspace, null)!;
            agentOwner.Content = page; Dispatcher.UIThread.RunJobs();
            foreach (var item in new[] { ("RemoveCampaignFreeAgent", skaterAgents, "copy.txt"), ("RemoveCampaignGoalieFreeAgent", goalieAgents, "keeper.txt") })
            {
                Click(page, item.Item1); Pump(() => agentOwner.OwnedWindows.Any());
                Click(agentOwner.OwnedWindows.Single(), "ConfirmRemoveEntry"); Pump(() => !agentOwner.OwnedWindows.Any()); Dispatcher.UIThread.RunJobs();
                check(!File.Exists(Path.Combine(item.Item2, item.Item3)) && SelectedPlayerPool.Read(item.Item2)!.Length == 0, "campaign recruitment list removes its saved copy and selection: " + item.Item1);
            }
        }
        finally { foreach (var window in agentOwner.OwnedWindows.ToArray()) window.Close(); agentOwner.Close(); }
        foreach (string name in new[] { "_Team", ".Team", "_removed_entries" })
        {
            Directory.CreateDirectory(Path.Combine(teams, name)); File.WriteAllText(Path.Combine(teams, name, "team.txt"), "Team Name = " + name + "\n");
        }
        check(CampaignNavigation.Load(campaign).Single(n => n.DisplayName == "Custom Squads").Children.Any(n => n.DisplayName == "_removed_entries"),
            "a real team named like the archive stays visible unless it carries the archive marker");
        string namesZip = Path.Combine(root, "valid-team-names.zip");
        typeof(App).Assembly.GetType("CampaignCreator.CampaignSharing")!.GetMethod("Zip", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [campaign, namesZip, root]);
        using (var archive = ZipFile.OpenRead(namesZip))
            check(archive.Entries.Any(e => e.FullName.EndsWith("/player_teams/_removed_entries/team.txt")) && !archive.Entries.Any(e => e.FullName.EndsWith("/.entry-removal.lock")),
                "campaign sharing keeps real archive-named teams while excluding local removal locks");
        foreach (string name in new[] { "_Team", ".Team", "_removed_entries" })
        {
            Call("ArchiveTeam", teams, name);
            check(!Directory.Exists(Path.Combine(teams, name)), "valid existing team names can be removed: " + name);
        }
    }
}
