using System.Reflection;
using System.Text.Json;
using CampaignCore;
using CampaignCreator;

// All source files and outputs live in a fresh fixture under the requested
// evidence directory. This check never calls the game save or user content paths.
string evidence = Path.GetFullPath(args.Length == 1 ? args[0] : "artifacts/four-features/play-now-roster-checks");
string root = Path.Combine(evidence, Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
Type studio = typeof(MainWindow).Assembly.GetType("CampaignCreator.ExportTranslation", true)!;
Type gameType = studio.Assembly.GetType("CampaignCreator.GameData", true)!;
Type? codec = typeof(CampaignDocument).Assembly.GetType("CampaignCore.PlayNowRosterCodec");
const BindingFlags methods = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
string[] positions = ["Left Wing", "Center", "Right Wing", "Left Defense", "Right Defense"];
string[] nativeSlots = ["leftWingerId", "centerId", "rightWingerId", "leftDefensemenId", "rightDefensemenId"];
int passed = 0, failed = 0;

void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    passed++;
}

void Case(string name, Action body)
{
    try { body(); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex.Message); }
}

object? Invoke(MethodInfo method, object? instance, params object?[] args)
{
    try { return method.Invoke(instance, args); }
    catch (TargetInvocationException ex) when (ex.InnerException != null)
    {
        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
        throw;
    }
}

(CampaignDocument Team, string File, string Root) Fixture(string label, bool enabled, bool opponent = false,
    bool stamina = true, int secondCount = 5)
{
    string folder = Path.Combine(root, label);
    string players = Path.Combine(folder, "players");
    Directory.CreateDirectory(players);
    string setting = opponent ? "Opponent Two Lines = yes" : "Squad Behavior two_lines = " + (enabled ? "On" : "Off");
    string teamText = "Team Name = Example\nSquad Template = Basic Squad\n" + setting
        + "\nSquad Behavior line_stamina = " + (stamina ? "On" : "Off") + "\n";
    string teamFile = Path.Combine(folder, "team.txt");
    File.WriteAllText(teamFile, teamText);
    for (int line = 0; line < 2; line++)
    for (int i = 0; i < (line == 0 ? 5 : secondCount); i++)
    {
        string position = (line == 0 ? "" : "Line 2 ") + positions[i];
        string name = $"Player {line * 5 + i + 1:00}";
        File.WriteAllText(Path.Combine(players, $"{position} - {name}.txt"),
            $"Name = {name}\nPosition = {position}\nFace = Faces/Anyteam/Black_Helmet\n");
    }
    File.WriteAllText(Path.Combine(players, "Goalie - Player 11.txt"), "Name = Player 11\nPosition = Goalie\nCatching = 50\n");
    return (new CampaignDocument(teamText), teamFile, folder);
}

(Dictionary<string, string> Saves, Dictionary<string, string> Overrides) Export((CampaignDocument Team, string File, string Root) fixture)
{
    object game = Activator.CreateInstance(gameType, fixture.Root)!;
    object package = Invoke(studio.GetMethod("BuildTeam", methods)!, null, fixture.Team, fixture.File, game, fixture.Root, null)!;
    Type type = package.GetType();
    return ((Dictionary<string, string>)type.GetProperty("Saves")!.GetValue(package)!,
        (Dictionary<string, string>)type.GetProperty("Overrides")!.GetValue(package)!);
}

bool ReadRoster(CampaignDocument sidecar, out object? roster, out string error)
{
    if (codec == null) throw new Exception("PlayNowRosterCodec is missing");
    object?[] values = [sidecar, null, null];
    bool success = (bool)Invoke(codec.GetMethod("TryRead", methods)!, null, values)!;
    roster = values[1]; error = (string)values[2]!;
    return success;
}

string[] Line(object roster, string property) => (string[])roster.GetType().GetProperty(property)!.GetValue(roster)!;
string IdName(Dictionary<string, string> sidecars, string id) => new CampaignDocument(sidecars[id + ".txt"]).Get("Name");

Case("CompleteTwoLineExport", () =>
{
    var package = Export(Fixture("two-lines", true));
    Check(package.Saves.Count == 12 && package.Overrides.Count == 12, "Expected eleven native players, eleven player sidecars, and one team of each");
    string teamJson = package.Saves.Single(p => p.Key.StartsWith("CustomTeam-")).Value;
    using var native = JsonDocument.Parse(teamJson);
    string[] first = nativeSlots.Select(slot => native.RootElement.GetProperty(slot).GetString()!).ToArray();
    string goalie = native.RootElement.GetProperty("goalieId").GetString()!;
    Check(first.Select(id => IdName(package.Overrides, id)).SequenceEqual(["Player 01", "Player 02", "Player 03", "Player 04", "Player 05"]), "Native first-line slots must remain in known position order");
    Check(IdName(package.Overrides, goalie) == "Player 11", "Native goalie remains the authored active goalie");
    Check(!native.RootElement.TryGetProperty("line2", out _), "Native team retains only its supported first-line fields");
    var sidecar = new CampaignDocument(package.Overrides.Single(p => p.Key.EndsWith(".team.txt")).Value);
    Check(ReadRoster(sidecar, out object? roster, out string error) && roster != null && error.Length == 0, "Complete sidecar extension must read");
    string[] line1 = Line(roster!, "Line1"), line2 = Line(roster!, "Line2");
    Check(line1.SequenceEqual(first), "Line 1 wire order must match native slot order when read in LW, C, RW, LD, RD order");
    Check(line2.Select(id => IdName(package.Overrides, id)).SequenceEqual(["Player 06", "Player 07", "Player 08", "Player 09", "Player 10"]), "Line 2 wire order must be LW, C, RW, LD, RD");
    Check(line2.All(id => !teamJson.Contains(id, StringComparison.OrdinalIgnoreCase)), "Native JSON must contain Line 1 and the goalie only");
    string[] all = line1.Concat(line2).Append(goalie).ToArray();
    Check(all.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 11, "All exported player identities must be unique");
    Check(all.All(id => package.Overrides.ContainsKey(id + ".txt") && package.Saves.Keys.Any(name => name.EndsWith(id + ".json"))), "Every roster identity needs both native and sidecar records");
    Check((bool)roster!.GetType().GetProperty("StaminaEnabled")!.GetValue(roster)!, "Authored On stamina must round-trip");
    var off = Export(Fixture("stamina-off", true, stamina: false));
    Check(ReadRoster(new CampaignDocument(off.Overrides.Single(p => p.Key.EndsWith(".team.txt")).Value), out object? offRoster, out _) && !(bool)offRoster!.GetType().GetProperty("StaminaEnabled")!.GetValue(offRoster)!, "Authored Off stamina must round-trip");
    var opponent = Export(Fixture("opponent-two-lines", false, opponent: true));
    Check(opponent.Saves.Count == 12, "Opponent two-line setting must also export its full roster");
});

Case("DisabledLineTwoIsNotExported", () =>
{
    var package = Export(Fixture("disabled", false));
    Check(package.Saves.Count == 7 && package.Overrides.Count == 7, "Unused explicit Line 2 must not export players or sidecars");
    var sidecar = new CampaignDocument(package.Overrides.Single(p => p.Key.EndsWith(".team.txt")).Value);
    Check(!ReadRoster(sidecar, out _, out string error) && error.Length == 0, "Single-line sidecar has no extension and needs no diagnostic");
});

Case("IncompleteLineRejectedBeforeWrite", () =>
{
    var fixture = Fixture("incomplete", true, secondCount: 4);
    string[] before = Directory.GetFiles(fixture.Root, "*", SearchOption.AllDirectories);
    try { Export(fixture); throw new Exception("Incomplete second line was accepted"); }
    catch (ArgumentException ex) { Check(ex.Message.Contains("Line 2", StringComparison.OrdinalIgnoreCase), "Error should identify the incomplete second line"); }
    Check(Directory.GetFiles(fixture.Root, "*", SearchOption.AllDirectories).SequenceEqual(before), "Incomplete roster must not write output into the source fixture");
});

Case("DuplicateOrWrongRoleRejected", () =>
{
    var duplicate = Fixture("duplicate", true);
    string players = Path.Combine(duplicate.Root, "players");
    File.WriteAllText(Path.Combine(players, "Extra Center.txt"), "Name = Extra Center\nPosition = Line 2 Center\nFace = Faces/Anyteam/Black_Helmet\n");
    try { Export(duplicate); throw new Exception("Duplicate Line 2 Center was accepted"); }
    catch (ArgumentException ex) { Check(ex.Message.Contains("Center", StringComparison.OrdinalIgnoreCase), "Duplicate slot diagnostic should name the position"); }
    var wrongRole = Fixture("wrong-role", true);
    string file = Path.Combine(wrongRole.Root, "players", "Line 2 Right Wing - Player 08.txt");
    File.AppendAllText(file, "Export Role = goalie\n");
    try { Export(wrongRole); throw new Exception("Goalie in a skater slot was accepted"); }
    catch (ArgumentException ex) { Check(ex.Message.Contains("goalie", StringComparison.OrdinalIgnoreCase), "Wrong-role diagnostic should identify goalie"); }
    var declaredSkater = Fixture("declared-skater-goalie-slot", true);
    File.AppendAllText(Path.Combine(declaredSkater.Root, "players", "Goalie - Player 11.txt"), "Export Role = skater\n");
    try { Export(declaredSkater); throw new Exception("Explicit skater role in a goalie slot was accepted"); }
    catch (ArgumentException ex) { Check(ex.Message.Contains("goalie", StringComparison.OrdinalIgnoreCase), "Goalie slot rejects an explicit skater role"); }
    var extraGoalie = Fixture("extra-goalie", true);
    File.WriteAllText(Path.Combine(extraGoalie.Root, "players", "Extra Goalie.txt"), "Name = Extra Goalie\nPosition = Goalie\nCatching = 50\n");
    try { Export(extraGoalie); throw new Exception("Second goalie was accepted"); }
    catch (ArgumentException ex) { Check(ex.Message.Contains("Goalie", StringComparison.OrdinalIgnoreCase), "Extra-goalie diagnostic should identify goalie"); }
});

Case("ExplicitPositionPrecedesFilename", () =>
{
    var fixture = Fixture("explicit-position", false);
    string players = Path.Combine(fixture.Root, "players");
    File.Delete(Path.Combine(players, "Center - Player 02.txt"));
    File.WriteAllText(Path.Combine(players, "Center - Goalie Smith.txt"),
        "Name = Goalie Smith\nPosition = Center\nFace = Faces/Anyteam/Black_Helmet\n");
    var package = Export(fixture);
    using var native = JsonDocument.Parse(package.Saves.Single(p => p.Key.StartsWith("CustomTeam-")).Value);
    string centerId = native.RootElement.GetProperty("centerId").GetString()!;
    Check(IdName(package.Overrides, centerId) == "Goalie Smith", "Explicit Center position exports as a skater even when filename contains goalie");
});

Case("LegacyPositionFallback", () =>
{
    var fixture = Fixture("legacy-position", true);
    string players = Path.Combine(fixture.Root, "players");
    File.Delete(Path.Combine(players, "Line 2 Right Wing - Player 08.txt"));
    string secondLine = Path.Combine(players, "second-line");
    Directory.CreateDirectory(secondLine);
    File.WriteAllText(Path.Combine(secondLine, "Right Wing - Player 08.txt"),
        "Name = Player 08\nFace = Faces/Anyteam/Black_Helmet\n");
    var package = Export(fixture);
    var sidecar = new CampaignDocument(package.Overrides.Single(p => p.Key.EndsWith(".team.txt")).Value);
    Check(ReadRoster(sidecar, out object? roster, out _) && roster != null, "Legacy fixture extension should read");
    Check(IdName(package.Overrides, Line(roster!, "Line2")[2]) == "Player 08", "Filename and second-line folder supply position when explicit Position is absent");
});

Case("UnknownRosterVersionUsesFallback", () =>
{
    var package = Export(Fixture("unknown-version", true));
    string nativeJson = package.Saves.Single(p => p.Key.StartsWith("CustomTeam-")).Value;
    var sidecar = new CampaignDocument(package.Overrides.Single(p => p.Key.EndsWith(".team.txt")).Value);
    sidecar.Set("Export Roster Version", "999");
    Check(!ReadRoster(sidecar, out _, out string error) && error.Contains("version", StringComparison.OrdinalIgnoreCase), "Unknown version must fail with a diagnostic");
    using var fallback = JsonDocument.Parse(nativeJson);
    string[] firstLine = nativeSlots.Select(slot => fallback.RootElement.GetProperty(slot).GetString()!).ToArray();
    Check(firstLine.Select(id => IdName(package.Overrides, id)).SequenceEqual(["Player 01", "Player 02", "Player 03", "Player 04", "Player 05"])
        && firstLine.All(id => package.Saves.ContainsKey("CustomForward-" + id + ".json")),
        "The native first line still references five valid player records after extension rejection");
});

Case("MalformedRosterAndRemap", () =>
{
    var package = Export(Fixture("codec", true));
    var sidecar = new CampaignDocument(package.Overrides.Single(p => p.Key.EndsWith(".team.txt")).Value);
    Check(ReadRoster(sidecar, out object? decoded, out _) && decoded != null, "Fixture extension must read before corruption checks");
    var duplicate = new CampaignDocument(sidecar.ToString());
    duplicate.Set("Export Line 2", duplicate.Get("Export Line 1"));
    Check(!ReadRoster(duplicate, out _, out string duplicateError) && duplicateError.Contains("duplicate", StringComparison.OrdinalIgnoreCase), "Repeated player IDs must reject the whole extension");
    var unsafeId = new CampaignDocument(sidecar.ToString());
    unsafeId.Set("Export Goalie", "../escape");
    Check(!ReadRoster(unsafeId, out _, out string unsafeError) && unsafeError.Contains("unsafe", StringComparison.OrdinalIgnoreCase), "Unsafe IDs must reject the whole extension");
    var partial = new CampaignDocument(sidecar.ToString());
    partial.Remove("Export Line 2");
    Check(!ReadRoster(partial, out _, out string partialError) && partialError.Length > 0, "Partial extension must have a diagnostic");
    string[] originalIds = PlayNowRosterCodec.PlayerIds((PlayNowRoster)decoded!);
    var mapped = PlayNowRosterCodec.Remap((PlayNowRoster)decoded!, id => "clone_" + id);
    Check(PlayNowRosterCodec.PlayerIds(mapped).SequenceEqual(originalIds.Select(id => "clone_" + id))
        && PlayNowRosterCodec.PlayerIds((PlayNowRoster)decoded!).SequenceEqual(originalIds), "Remap must preserve order and leave the source unchanged");
    try { PlayNowRosterCodec.Remap((PlayNowRoster)decoded!, _ => "same"); throw new Exception("Duplicate remap was accepted"); }
    catch (ArgumentException) { Check(true, "Remap rejects a collision"); }
    var preserved = new CampaignDocument("Unrelated = keep\nExport Roster Version = 999\n");
    PlayNowRosterCodec.Write(preserved, mapped);
    Check(preserved.Get("Unrelated") == "keep" && ReadRoster(preserved, out _, out _), "Write replaces only the extension fields");
});

Console.WriteLine($"{passed} assertions passed; {failed} cases failed. Isolated evidence: {root}");
return failed == 0 ? 0 : 1;
