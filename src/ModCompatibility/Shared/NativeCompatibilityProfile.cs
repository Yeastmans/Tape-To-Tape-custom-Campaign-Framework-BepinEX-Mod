using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace CampaignCore;

// Data only: shared by Studio's verifier and the plugin's hash/byte guarded loader.
public sealed class NativeCompatibilityProfile
{
    public const string Contract = "t2t-native-v2-20261020";
    public string RuntimeContract { get; set; } = Contract;
    public string BaselineId { get; set; } = "";
    public string Build { get; set; } = "";
    public string GameHash { get; set; } = "";
    public string MetadataHash { get; set; } = "";
    public int SurfaceRva { get; set; }
    public string SurfaceBytes { get; set; } = "48895C24084889742410574881EC90000000";
    public int FlowRva { get; set; }
    public string FlowBytes { get; set; } = "410FB6C60A4758";
    public List<NativeComparisonSite> Sites { get; set; } = new();

    public void Validate(string gameHash, string baselineId)
    {
        if (RuntimeContract != Contract || BaselineId != baselineId || !GameHash.Equals(gameHash,StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Compatibility data belongs to another mod or game build. Open Campaign Studio to refresh it.");
        if (!Regex.IsMatch(GameHash, "^[a-fA-F0-9]{64}$") || !Regex.IsMatch(MetadataHash,"^[a-fA-F0-9]{64}$") || string.IsNullOrWhiteSpace(Build))
            throw new InvalidDataException("Incomplete compatibility identity.");
        if (SurfaceRva <= 0 || FlowRva <= 0 || SurfaceBytes != "48895C24084889742410574881EC90000000" || FlowBytes != "410FB6C60A4758")
            throw new InvalidDataException("Unsupported native hook ABI.");
        if (Sites.Count == 0 || Sites.Count > 2000 || Sites.Select(s=>s.Rva).Distinct().Count()!=Sites.Count)
            throw new InvalidDataException("Missing or repeated comparison sites.");
        foreach (var s in Sites)
        {
            var bytes = Convert.FromHexString(s.Bytes);
            bool inline = s.Kind == "inline_eq";
            bool valid = inline ? s.Inline != null && s.Inline.Validate(s.Rva,s.Target,bytes) : s.Inline == null && bytes.Length==5 && (bytes[0]==0xe8 || bytes[0]==0xe9) && (long)s.Rva+5+BitConverter.ToInt32(bytes,1)==s.Target && new[]{"query","eq","neq"}.Contains(s.Kind);
            if (s.Rva<=0 || s.Target<=0 || !valid || string.IsNullOrWhiteSpace(s.Feature) || string.IsNullOrWhiteSpace(s.Query) || string.IsNullOrWhiteSpace(s.Method))
                throw new InvalidDataException("Invalid native comparison site.");
        }
    }
    public static NativeCompatibilityProfile Read(string json) => JsonSerializer.Deserialize<NativeCompatibilityProfile>(json) ?? throw new InvalidDataException("Empty compatibility profile.");
}
public sealed class NativeComparisonSite
{
    public string Feature { get; set; } = "";
    public string Query { get; set; } = "";
    public string Kind { get; set; } = "";
    public int Rva { get; set; }
    public int Target { get; set; }
    public string Bytes { get; set; } = "";
    public string Method { get; set; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NativeInlineComparison Inline { get; set; } = null!;
}

public sealed class NativeInlineComparison
{
    public string LeftRegister { get; set; } = "";
    public int EqualBranchRva { get; set; }
    public string BlockBytes { get; set; } = "";
    public bool Validate(int rva,int resume,byte[] bytes)
    {
        if (LeftRegister is not ("rcx" or "rbx") || bytes.Length is not (5 or 9) || resume<=rva+bytes.Length || resume>rva+128 || EqualBranchRva<=0) return false;
        if(bytes[0]!=0x48 || bytes[1]!=0x3b || bytes[2]!=(LeftRegister=="rcx"?0xca:0xda)) return false;
        if(bytes.Length==5 ? bytes[3]!=0x74 || rva+5+(sbyte)bytes[4]!=EqualBranchRva : bytes[3]!=0x0f || bytes[4]!=0x84 || (long)rva+9+BitConverter.ToInt32(bytes,5)!=EqualBranchRva) return false;
        var block=Convert.FromHexString(BlockBytes);
        return block.Length==resume-rva && block.Take(bytes.Length).SequenceEqual(bytes);
    }
}

