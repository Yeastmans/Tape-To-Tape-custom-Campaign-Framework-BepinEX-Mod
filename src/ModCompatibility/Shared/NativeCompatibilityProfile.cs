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
    public const string Contract = "t2t-native-v3-20261024";
    public string RuntimeContract { get; set; } = Contract;
    public string BaselineId { get; set; } = "";
    public string Build { get; set; } = "";
    public string GameHash { get; set; } = "";
    public string MetadataHash { get; set; } = "";
    public int SurfaceRva { get; set; }
    public string SurfaceBytes { get; set; } = "48895C24084889742410574881EC90000000";
    public int FlowRva { get; set; }
    public string FlowBytes { get; set; } = "400FB6C60A4758";
    public List<NativeComparisonSite> Sites { get; set; } = new();
    public List<RawNativeHookContract> RawHooks { get; set; } = new();
    public List<NamedManagedHookContract> NamedHooks { get; set; } = new();

    public void Validate(string gameHash, string baselineId)
    {
        if (RuntimeContract != Contract || BaselineId != baselineId || !GameHash.Equals(gameHash,StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Compatibility data belongs to another mod or game build. Open Campaign Studio to refresh it.");
        if (!Regex.IsMatch(GameHash, "^[a-fA-F0-9]{64}$") || !Regex.IsMatch(MetadataHash,"^[a-fA-F0-9]{64}$") || string.IsNullOrWhiteSpace(Build))
            throw new InvalidDataException("Incomplete compatibility identity.");
        if (SurfaceRva <= 0 || FlowRva <= 0 || SurfaceBytes != "48895C24084889742410574881EC90000000" || FlowBytes != "400FB6C60A4758")
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


// Explicit raw callback inventory. Harmony coverage alone cannot discover it.
public sealed class RawNativeHookContract
{
    public static void RequireExactSet(IEnumerable<RawNativeHookContract> expected, IEnumerable<RawNativeHookContract> approved, string feature)
    {
        if (expected == null || approved == null) throw new InvalidDataException("Missing raw native contracts.");
        var wanted = expected.OrderBy(c => c.Id, StringComparer.Ordinal).ToArray();
        var actual = approved.Where(c => c.Feature == feature).OrderBy(c => c.Id, StringComparer.Ordinal).ToArray();
        if (wanted.Length == 0 || wanted.Length != wanted.Select(c => c.Id).Distinct().Count() || wanted.Length != wanted.Select(c => c.Rva).Distinct().Count()
            || !wanted.Select(c => JsonSerializer.Serialize(c)).SequenceEqual(actual.Select(c => JsonSerializer.Serialize(c))))
            throw new InvalidDataException("Raw callbacks have not passed final compatibility integration: " + feature);
    }

    public string Feature { get; set; } = "";
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "";
    public int Scope { get; set; }
    public int Rva { get; set; }
    public int Size { get; set; }
    public string Sha256 { get; set; } = "";
    public string Prologue { get; set; } = "";
    public string Identity { get; set; } = "";
    public string NativeSignature { get; set; } = "";
    public int AliasCount { get; set; }
}

// Exact managed entry contract; distinct from unnamed/raw native callbacks.
public sealed class NamedManagedHookContract
{
    public string Feature { get; set; } = "";
    public string Id { get; set; } = "";
    public string Assembly { get; set; } = "";
    public string DeclaringType { get; set; } = "";
    public string Method { get; set; } = "";
    public bool IsStatic { get; set; }
    public string ReturnType { get; set; } = "";
    public string[] Parameters { get; set; } = Array.Empty<string>();
    public string Prefix { get; set; } = "";
    public string Postfix { get; set; } = "";
    public string Finalizer { get; set; } = "";

    public static void RequireExactSet(IEnumerable<NamedManagedHookContract> expected, IEnumerable<NamedManagedHookContract> approved, string feature)
    {
        if (expected == null || approved == null) throw new InvalidDataException("Missing named managed contracts.");
        var wanted = expected.OrderBy(c => c.Id, StringComparer.Ordinal).ToArray();
        var actual = approved.Where(c => c.Feature == feature).OrderBy(c => c.Id, StringComparer.Ordinal).ToArray();
        if (wanted.Length == 0 || wanted.Length != wanted.Select(c => c.Id).Distinct().Count()
            || !wanted.Select(c => JsonSerializer.Serialize(c)).SequenceEqual(actual.Select(c => JsonSerializer.Serialize(c))))
            throw new InvalidDataException("Named callbacks have not passed final compatibility integration: " + feature);
    }
}
