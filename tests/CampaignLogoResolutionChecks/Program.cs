using CampaignCore;

int checks = 0;
void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; Console.WriteLine("PASS " + message); }
string root = Path.Combine(Path.GetTempPath(), "campaign-logo-resolution-" + Guid.NewGuid().ToString("N"));
string campaign = Path.Combine(root, "campaigns", "Example"), other = Path.Combine(root, "campaigns", "Other");
string global = Path.Combine(root, "CustomLogos");
foreach (string folder in new[] { Path.Combine(campaign, "logos"), Path.Combine(other, "logos"), global, Path.Combine(root, "_team_logos") })
    Directory.CreateDirectory(folder);
string own = Path.Combine(campaign, "logos", "Hogwarts.png");
string cached = LogoFiles.NativeFile(root, "Hogwarts");
File.WriteAllText(own, "campaign artwork");
File.WriteAllText(cached, "incorrect donor preview");
File.WriteAllText(Path.Combine(global, "Hogwarts.png"), "global artwork");
File.WriteAllText(Path.Combine(other, "logos", "Hogwarts.png"), "other campaign");
var method = typeof(LogoFiles).GetMethod("Resolve", new[] { typeof(string), typeof(string), typeof(string), typeof(string) });
Check(method != null, "Logo resolution accepts the active campaign context");
string Resolve(string source, string context = null) => (string)method!.Invoke(null, new object[] { root, global, source, context ?? campaign });
Check(Resolve("Hogwarts") == own, "Campaign artwork outranks an incorrect native preview cache");
Check(Resolve("logos/Hogwarts.png") == own, "Campaign-relative logo paths work");
Check(Resolve("Hogwarts.png") == own, "Named campaign PNG works");
Check(Resolve("Hogwarts", other) == Path.Combine(other, "logos", "Hogwarts.png"), "Campaigns with the same logo name stay independent");
Check(Resolve(Path.Combine(global, "Hogwarts.png")) == Path.Combine(global, "Hogwarts.png"), "Explicit absolute selection retains priority");
Check(Resolve("none") == null && Resolve("") == null, "No-logo selections remain empty");
Check(Resolve("Missing logo") == null, "Missing artwork never invents a file");
Check(LogoFiles.Resolve(root, global, "Hogwarts") == cached, "Calls without campaign context retain existing resolution");
Check(File.ReadAllText(cached) == "incorrect donor preview" && File.ReadAllText(own) == "campaign artwork", "Resolution does not modify cached or authored artwork");
File.WriteAllText(own, "updated campaign artwork");
Check(Resolve("Hogwarts") == own && File.ReadAllText(Resolve("Hogwarts")) == "updated campaign artwork", "Content replacement at the resolved campaign path is observable");
Check(File.ReadAllText(Resolve("Hogwarts", other)) == "other campaign", "Updating same-named artwork does not cross campaign contexts");
string liveCampaign = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "T2T Campaign Studio", "campaigns", "Yeastman's Wacky Crazy Funtime");
if (Directory.Exists(Path.Combine(liveCampaign, "logos")))
{
    string liveRoot = Path.GetDirectoryName(Path.GetDirectoryName(liveCampaign))!;
    foreach (string file in Directory.GetFiles(Path.Combine(liveCampaign, "logos"), "*.png"))
        Check((string)method!.Invoke(null, new object[] { liveRoot, global, Path.GetFileNameWithoutExtension(file), liveCampaign }) == file,
            "Read-only Funtime resolution: " + Path.GetFileName(file));
}
Console.WriteLine($"Passed {checks} campaign logo checks.");
