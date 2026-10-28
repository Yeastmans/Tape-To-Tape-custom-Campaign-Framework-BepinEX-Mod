using CampaignCore;
using CampaignCreator;

internal static class FreeAgentNameChecks
{
    internal static void Run(string output)
    {
        string root = Path.Combine(Path.GetFullPath(output), "fixture-" + Guid.NewGuid().ToString("N"));
        int checks = 0;
        void Check(bool value, string message)
        {
            if (!value) throw new Exception(message);
            checks++; Console.WriteLine("PASS " + message);
        }
        void Write(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }
        CampaignDocument Read(string file) => new(File.ReadAllText(file));
        void Reject(Action action, string message)
        {
            try { action(); } catch (ArgumentException) { Check(true, message); return; }
            throw new Exception(message);
        }

        string campaign = Path.Combine(root, "campaigns", "Two solid players");
        Write(Path.Combine(campaign, "campaign.txt"), "Act Sequence = 1\n");
        string source = Path.Combine(campaign, "teams", "Campaign team", "players", "Brent Burns.txt");
        const string original = "Name = Brent Burns\nPosition = Right Defense\nSpeed = 71\nNumber = 8\nFuture Field = keep\n";
        Write(source, original);
        string Squad(string name)
        {
            string squad = Path.Combine(campaign, "player_teams", name);
            Write(Path.Combine(squad, "team.txt"), "Team Name = " + name + "\nSquad Template = Double Decker\nUse Own Player Pools = yes\n");
            return squad;
        }

        string first = Squad("First squad"), second = Squad("Second squad");
        string firstCopy = PlayerPoolImport.IntoFreeAgents(root, first, [source]).Single();
        Console.WriteLine("Imported campaign player name: " + Read(firstCopy).Get("Name"));
        Check(Read(firstCopy).Get("Name") == "Brent Burns", "Squad free agent retains the campaign player's name");
        Check(Read(firstCopy).Get("Speed") == "71" && Read(firstCopy).Get("Future Field") == "keep", "Free-agent copy retains stats and authored fields");
        Check(File.ReadAllText(source) == original, "Import leaves the campaign-team original unchanged");
        CreatorContent.ValidatePlayerName(firstCopy, "Brent Burns", firstCopy);
        Check(true, "Imported same-name free agent remains editable");

        string secondCopy = PlayerPoolImport.IntoFreeAgents(root, second, [source]).Single();
        Check(Read(secondCopy).Get("Name") == "Brent Burns", "A second squad keeps its own unnumbered free agent");
        Check(SelectedPlayerPool.PlayerId(Path.GetDirectoryName(firstCopy)!, Path.GetFileName(firstCopy))
            != SelectedPlayerPool.PlayerId(Path.GetDirectoryName(secondCopy)!, Path.GetFileName(secondCopy)), "Same-name squad copies keep distinct runtime identities");
        string repeated = PlayerPoolImport.IntoFreeAgents(root, first, [source]).Single();
        Check(Read(repeated).Get("Name") == "Brent Burns 2" && Read(firstCopy).Get("Name") == "Brent Burns", "Repeated import within one pool numbers only the new copy");
        Reject(() => CreatorContent.ValidatePlayerName(repeated, "brent burns", repeated), "Renaming cannot collide with another player in the same pool");

        string shared = FreeAgentContent.Create(root, campaign, "Brent Burns", source);
        Check(Read(shared).Get("Name") == "Brent Burns", "Single shared-pool import ignores team and squad copies");
        Check(FreeAgentContent.SelectedCustom(campaign).Contains(FreeAgentContent.SelectionKey(campaign, shared)), "Shared import remains selected");
        string sharedRepeated = PlayerPoolImport.IntoFreeAgents(root, campaign, [source]).Single();
        Check(Read(sharedRepeated).Get("Name") == "Brent Burns 2", "Single and bulk imports share numbering within their destination pool");

        string repairSquad = Squad("Existing numbered copy");
        string repair = Path.Combine(repairSquad, "player_teams", "free_agents", "keep-this-identity.txt");
        Write(repair, original.Replace("Name = Brent Burns", "Name = Brent Burns 2"));
        string repairPool = Path.GetDirectoryName(repair)!;
        SelectedPlayerPool.Save(repairPool, [Path.GetFileName(repair)]);
        string identity = SelectedPlayerPool.PlayerId(repairPool, Path.GetFileName(repair));
        CreatorContent.ValidatePlayerName(repair, "Brent Burns", repair);
        var repaired = Read(repair); repaired.Set("Name", "Brent Burns"); repaired.Save(repair);
        Check(Read(repair).Get("Name") == "Brent Burns" && Read(repair).Get("Speed") == "71", "Existing numbered copy can be renamed to its campaign original");
        Check(SelectedPlayerPool.Read(repairPool)!.Single() == repair && identity == SelectedPlayerPool.PlayerId(repairPool, Path.GetFileName(repair)), "Rename preserves the file identity and selection");

        string numberedSource = Path.Combine(campaign, "teams", "Campaign team", "players", "Player 2.txt");
        Write(numberedSource, "Name = Player 2\nPosition = Center\n");
        string numberedCopy = PlayerPoolImport.IntoFreeAgents(root, second, [numberedSource]).Single();
        Check(Read(numberedCopy).Get("Name") == "Player 2", "Authored numeric names are preserved literally");
        Reject(() => CreatorContent.ValidatePlayerName(source, "Brent Burns"), "Team creation retains campaign-wide name validation");
        Check(CreatorContent.AvailablePlayerName(Path.GetDirectoryName(source)!, "Brent Burns") == "Brent Burns 3", "Roster copies retain existing campaign-wide numbering");
        Check(File.ReadAllText(source) == original, "All import and rename checks preserve the source player");
        Console.WriteLine($"PASS: {checks} free-agent name and identity checks. Fixture: {root}");
    }
}
