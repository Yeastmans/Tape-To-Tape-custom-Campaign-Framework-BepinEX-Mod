using System.Globalization;
using System.Text.Json;
using ModCompatibility;

const string usage = """
CompatibilityTool baseline <game folder> <squad TSV> <Steam build> <flow RVA hex> <flow method identity> <baseline output> <runtime profile output>
CompatibilityTool verify <game folder> <baseline> <Steam build> <profile output>

Creates or verifies release compatibility metadata. Neither command installs files into the game.
Create a baseline only after reviewing that game build's native contracts.
""";

if (args.Length == 1 && args[0] is "--help" or "-h")
{
    Console.WriteLine(usage);
    return 0;
}
if (args.Length == 0 || args[0] is not ("baseline" or "verify")
    || args.Length != (args[0] == "baseline" ? 8 : 5))
{
    Console.Error.WriteLine(usage);
    return 2;
}

try
{
    string game = Path.GetFullPath(args[1]);
    if (!Directory.Exists(game)) throw new DirectoryNotFoundException("Game folder not found: " + game);
    string input = Path.GetFullPath(args[2]);
    if (!File.Exists(input)) throw new FileNotFoundException("Input file not found: " + input);
    if (!ulong.TryParse(args[3], NumberStyles.None, CultureInfo.InvariantCulture, out ulong build) || build == 0)
        throw new ArgumentException("Steam build must be a positive numeric build ID.");

    string[] outputs = (args[0] == "baseline" ? new[] { args[6], args[7] } : new[] { args[4] })
        .Select(Path.GetFullPath).ToArray();
    var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    string[] protectedInputs = [input, Path.Combine(game, "GameAssembly.dll"), Path.Combine(game, "UnityPlayer.dll"),
        Path.Combine(game, "Tape to Tape_Data", "il2cpp_data", "Metadata", "global-metadata.dat")];
    if (outputs.Distinct(comparer).Count() != outputs.Length || outputs.Any(output => protectedInputs.Contains(output, comparer)))
        throw new ArgumentException("Each output must have a distinct path and must not replace an input file.");
    if (outputs.Any(Directory.Exists)) throw new ArgumentException("An output path names an existing directory.");

    int flow = 0;
    if (args[0] == "baseline")
    {
        string value = args[4].StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? args[4][2..] : args[4];
        if (!int.TryParse(value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out flow) || flow <= 0)
            throw new ArgumentException("Flow RVA must be a positive hexadecimal 32-bit address.");
        if (string.IsNullOrWhiteSpace(args[5])) throw new ArgumentException("Flow method identity is required.");
    }

    var image = new NativeImage(game);
    if (args[0] == "baseline")
    {
        var baseline = CompatibilityBaseline.Create(image, input, args[3], flow, args[5]);
        string baselineJson = JsonSerializer.Serialize(baseline, CompatibilityBaseline.Json);
        string profileJson = JsonSerializer.Serialize(baseline.Profile, CompatibilityBaseline.Json);
        foreach (string output in outputs) Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(outputs[0], baselineJson);
        File.WriteAllText(outputs[1], profileJson);
        Console.WriteLine($"Baseline {baseline.Id}: {baseline.Methods.Count} methods, {baseline.Sites.Count} comparisons, {baseline.SquadCalls.Length} query calls");
    }
    else
    {
        var baseline = CompatibilityBaseline.Read(input);
        var profile = baseline.Verify(image, args[3], new ConsoleProgress());
        Directory.CreateDirectory(Path.GetDirectoryName(outputs[0])!);
        File.WriteAllText(outputs[0], JsonSerializer.Serialize(profile, CompatibilityBaseline.Json));
        Console.WriteLine("Compatibility verified for " + profile.Build);
    }
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine("CompatibilityTool: " + ex.Message);
    return 1;
}

sealed class ConsoleProgress : IProgress<string>
{
    public void Report(string value) => Console.WriteLine(value);
}
