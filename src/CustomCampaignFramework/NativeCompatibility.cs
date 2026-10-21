using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using CampaignCore;

namespace EndlessMode;

internal static class NativeCompatibility
{
    private static NativeCompatibilityProfile cached;
    internal static NativeCompatibilityProfile Load(string gameAssembly)
    {
        if(cached!=null) return cached;
        using var stream=typeof(NativeCompatibility).Assembly.GetManifestResourceStream("NativeCompatibility.json");
        using var reader=new StreamReader(stream ?? throw new FileNotFoundException("NativeCompatibility.json"));
        var baseline=NativeCompatibilityProfile.Read(reader.ReadToEnd());
        string actualHash=Hash(gameAssembly);
        string profileFile=Path.Combine(Path.GetDirectoryName(typeof(NativeCompatibility).Assembly.Location),"CustomCampaignFramework.compatibility.json");
        var selected=baseline;
        if(File.Exists(profileFile))
        {
            var candidate=NativeCompatibilityProfile.Read(File.ReadAllText(profileFile));
            if(candidate.GameHash.Equals(actualHash,StringComparison.OrdinalIgnoreCase) && candidate.BaselineId==baseline.BaselineId) selected=candidate;
        }
        selected.Validate(actualHash,baseline.BaselineId);
        string metadata=Path.Combine(Path.GetDirectoryName(gameAssembly),"Tape to Tape_Data","il2cpp_data","Metadata","global-metadata.dat");
        if(Hash(metadata)!=selected.MetadataHash) throw new InvalidDataException("Game metadata changed; open Campaign Studio to refresh mod compatibility.");
        var expected=baseline.Sites.Select(s=>s.Feature+"|"+s.Query+"|"+s.Kind+"|"+s.Method).OrderBy(s=>s).ToArray();
        var current=selected.Sites.Select(s=>s.Feature+"|"+s.Query+"|"+s.Kind+"|"+s.Method).OrderBy(s=>s).ToArray();
        if(!expected.SequenceEqual(current)) throw new InvalidDataException("Compatibility comparison coverage differs from the installed mod.");
        if (System.Text.Json.JsonSerializer.Serialize(baseline.RawHooks.OrderBy(c => c.Id)) != System.Text.Json.JsonSerializer.Serialize(selected.RawHooks.OrderBy(c => c.Id)))
            throw new InvalidDataException("Raw native callback coverage differs from the installed mod.");
        if (System.Text.Json.JsonSerializer.Serialize(baseline.NamedHooks.OrderBy(c => c.Id)) != System.Text.Json.JsonSerializer.Serialize(selected.NamedHooks.OrderBy(c => c.Id)))
            throw new InvalidDataException("Named managed callback coverage differs from the installed mod.");
        return cached=selected;
    }
    private static string Hash(string file) { using var data=File.OpenRead(file);using var sha=SHA256.Create();return Convert.ToHexString(sha.ComputeHash(data)); }
}
