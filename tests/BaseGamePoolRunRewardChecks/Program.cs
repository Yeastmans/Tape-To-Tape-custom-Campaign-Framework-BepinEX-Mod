using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using CampaignCore;

string output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
string root = Path.Combine(output, "fixture-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
var checks = new Checks();
if (args.Contains("--runtime")) RuntimeChecks.Run(root, checks);
else CoreChecks.Run(root, checks);
if (args.Length > 2 && Directory.Exists(args[2]))
{
    using var assembly = Mono.Cecil.AssemblyDefinition.ReadAssembly(Path.Combine(args[2], "MainScriptsAssembly.dll"));
    var deserialize = assembly.MainModule.Types.Single(t => t.FullName == "State.DeserializerV2").Methods.Single(m => m.Name == "DeserializeFromModel");
    checks.Check(!deserialize.IsStatic && deserialize.ReturnType.FullName == "System.Void"
        && deserialize.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(new[] { "Data.RunData", "State.RunDataModelV2" }),
        "Current generated native wrapper has the exact protected hydration signature");
    var model = assembly.MainModule.Types.Single(t => t.FullName == "State.RunDataModelV2");
    checks.Check(model.Properties.Single(p => p.Name == "runID").PropertyType.FullName == "System.String", "Current serialized native model exposes its own run ID before hydration");
    var reset = assembly.MainModule.Types.Single(t => t.FullName == "State.CampaignState").Methods.Single(m => m.Name == "ResetRunData");
    checks.Check(!reset.IsStatic && reset.ReturnType.FullName == "System.Void" && reset.Parameters.Count == 0,
        "Current generated native wrapper has the exact protected reset signature");
}
Console.WriteLine($"PASS {checks.Count} Base Game run reward checks; native execution is not simulated by the file roundtrip checks.");

internal sealed class Checks
{
    internal int Count;
    internal void Check(bool condition, string label) { if (!condition) throw new Exception("FAIL " + label); Count++; Console.WriteLine("PASS " + label); }
    internal void Reject(Action action, string label)
    {
        try { action(); }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException or IOException or FormatException) { Check(true, label); return; }
        throw new Exception("FAIL accepted " + label);
    }
}
internal static class Api
{
    internal static object Invoke(Type type, string method, params object[] args)
    {
        var target = type?.GetMethod(method, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        if (target == null) throw new Exception("FAIL missing run-snapshot behavior: " + method);
        try { return target.Invoke(null, args); }
        catch (TargetInvocationException ex) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException!).Throw(); throw; }
    }
    internal static Dictionary<string, CustomRewardDefinition> Load(string root, string run) => (Dictionary<string, CustomRewardDefinition>)Invoke(typeof(CampaignDocument).Assembly.GetType("CampaignCore.BaseGamePoolRunRewards"), "Load", root, run);
    internal static void Save(string root, string run, params CustomRewardDefinition[] definitions) => Invoke(typeof(CampaignDocument).Assembly.GetType("CampaignCore.BaseGamePoolRunRewards"), "Save", root, run, definitions);
    internal static string PathFor(string root, string run) => Path.Combine(root, "base_game_pool_run_rewards", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(run))) + ".txt");
    internal static CustomRewardDefinition Reward(string id, string name, int amount = 31) => new()
    {
        Id = id, Name = name, Amount = amount, Stat = "Speed", Description = "Saved independent rule", Trigger = "Goal", Target = "Triggering player", Duration = "Rest of run",
        IconPng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aNn8AAAAASUVORK5CYII="
    };
    internal const string FirstId = "31303ff4-8fa7-41ec-8c54-844f12898d01", SecondId = "31303ff4-8fa7-41ec-8c54-844f12898d02";
}
internal static class CoreChecks
{
    internal static void Run(string root, Checks c)
    {
        const string run = "native/run:alpha";
        c.Check(Api.Load(root, run).Count == 0 && !Directory.Exists(Path.Combine(root, "base_game_pool_run_rewards")), "No snapshot is an empty read without filesystem writes");
        var first = Api.Reward(Api.FirstId, "Acquired talent"); var queued = Api.Reward(Api.SecondId, "Pregenerated offer", 17);
        var pool = new BaseGamePoolSelection { Enabled = true };
        string dir = BaseGamePlayerPools.PoolDirectory(root, "superstars"); Directory.CreateDirectory(dir);
        var player = new CampaignDocument("Name = Snapshot candidate\n"); CustomRewardExport.Embed(player, [first, queued]); player.Save(Path.Combine(dir, "candidate.txt"));
        pool.Pools["superstars"] = ["candidate.txt"];
        foreach (string kind in new[] { "draft_pool", "free_agents" })
        {
            string native = BaseGamePlayerPools.PoolDirectory(root, kind); Directory.CreateDirectory(native);
            File.WriteAllText(Path.Combine(native, "native.txt"), "Name = Native candidate\n"); pool.Pools[kind] = ["native.txt"];
        }
        BaseGamePlayerPools.Save(root, pool);
        Api.Save(root, run, BaseGamePlayerPools.ReadCustomRewards(root).Values.ToArray());
        string path = Api.PathFor(root, run); byte[] original = File.ReadAllBytes(path);
        var loaded = Api.Load(root, run);
        c.Check(loaded.Count == 2 && loaded[first.Id].ToDocument().ToString() == first.ToDocument().ToString()
            && loaded[queued.Id].ToDocument().ToString() == queued.ToDocument().ToString(), "Full definitions including pending offers and icon pixels round-trip by native run ID");
        c.Check(Path.GetFileName(path).Length == 68 && Directory.GetFiles(Path.GetDirectoryName(path)!).Length == 1, "Unsafe path characters in a run ID stay inside its SHA-256 filename");
        var edited = first.Copy(); edited.Amount = 99; CustomRewardExport.Embed(player, [edited]); player.Save(Path.Combine(dir, "candidate.txt"));
        c.Check(Api.Load(root, run)[first.Id].Amount == 31 && Api.Load(root, run).ContainsKey(queued.Id), "Replacing a source pool cannot rewrite acquired or pregenerated run rules");
        pool.Enabled = false; BaseGamePlayerPools.Save(root, pool); File.Delete(Path.Combine(dir, "candidate.txt"));
        Api.Save(root, run);
        c.Check(Api.Load(root, run).Count == 2 && Api.Load(root, run)[first.Id].Amount == 31, "Disabling/removing source pools and later empty saves retain the run snapshot");
        c.Check(Api.Load(root, "another-run").Count == 0, "Different native runs are isolated");
        loaded[first.Id].Amount = 123;
        c.Check(Api.Load(root, run)[first.Id].Amount == 31, "Returned definitions cannot mutate persisted state");
        original = File.ReadAllBytes(path);
        c.Reject(() => Api.Save(root, run, first, edited), "Conflicting definitions in one save");
        c.Reject(() => Api.Save(root, run, edited), "An edited same-ID source cannot replace an existing run rule");
        c.Check(File.ReadAllBytes(path).SequenceEqual(original), "Rejected conflicts preserve the previous complete snapshot");
        string wrong = Api.PathFor(root, "wrong-run"); File.Copy(path, wrong);
        c.Reject(() => Api.Load(root, "wrong-run"), "A copied snapshot bound to another run");
        string text = File.ReadAllText(path);
        foreach (var corruption in new[] {
            "not a snapshot", text.Replace("Format = 1", "Format = 9"),
            text + "\nRun ID = different\n", text + "\nUnrecognized = junk\n",
            text.Replace(Convert.ToBase64String(Encoding.UTF8.GetBytes(first.ToDocument().ToString())), "not-base64"),
            string.Join("\n", text.Split('\n').Where(line => !line.Contains(CustomRewardExport.Prefix + queued.Id))) })
        {
            File.WriteAllText(path, corruption); c.Reject(() => Api.Load(root, run), "Corrupted, ambiguous or incomplete saved definitions");
            c.Reject(() => Api.Save(root, run, first), "A corrupted existing snapshot is never overwritten by save");
            c.Check(File.ReadAllText(path) == corruption, "Rejected load/save leaves the corrupt evidence intact");
            File.WriteAllBytes(path, original);
        }
        c.Reject(() => Api.Save(root, "\n", first), "Empty/control-character run identity");
        c.Reject(() => Api.Save(root, run, new CustomRewardDefinition { Name = "Invalid" }), "Invalid reward snapshot");
        c.Check(File.ReadAllBytes(path).SequenceEqual(original) && !Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp").Any(), "Validation failure cannot publish or leave temporary data");
        if (OperatingSystem.IsWindows())
        {
            using (var locked = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                c.Reject(() => Api.Save(root, run, first), "Failed atomic replacement");
            c.Check(File.ReadAllBytes(path).SequenceEqual(original) && !Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp").Any(), "Atomic replacement failure keeps the prior file and cleans staging");
        }
        Api.Save(root, "empty-run"); c.Check(Api.Load(root, "empty-run").Count == 0, "An explicitly saved empty run remains readable");
    }
}
