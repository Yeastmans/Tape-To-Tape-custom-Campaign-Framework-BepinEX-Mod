using System.Security.Cryptography;
using System.Text.Json;
using CampaignCore;
namespace ModCompatibility;

// Release-owned inventory: manual Harmony targets and raw callbacks cannot be
// inferred from attributes. Native body contracts additionally cover callables.
internal sealed class ReviewedFeatureContracts
{
    public List<NamedManagedHookContract> NamedHooks { get; set; } = new();
    public List<RawNativeHookContract> RawHooks { get; set; } = new();
    public string[] ProtectedMethods { get; set; } = [];
    internal static ReviewedFeatureContracts Read()
    {
        using var stream = typeof(ReviewedFeatureContracts).Assembly.GetManifestResourceStream("FourFeatureContracts.json")!;
        return JsonSerializer.Deserialize<ReviewedFeatureContracts>(stream) ?? throw new InvalidDataException("Missing reviewed feature inventory.");
    }
    internal void Validate(NativeImage image, NativeCompatibilityProfile profile)
    {
        foreach (var feature in NamedHooks.Select(c => c.Feature).Distinct())
            NamedManagedHookContract.RequireExactSet(NamedHooks.Where(c => c.Feature == feature), profile.NamedHooks, feature);
        if (profile.NamedHooks.Count != NamedHooks.Count) throw new InvalidDataException("Unexpected named feature contracts.");
        RawNativeHookContract.RequireExactSet(RawHooks, profile.RawHooks, "jersey-logo");
        if (profile.RawHooks.Count != RawHooks.Count) throw new InvalidDataException("Unexpected raw feature contracts.");
        // Raw callbacks have reviewed absolute RVAs, closed generic identities,
        // and one explicitly unnamed helper. They cannot be relocated from names.
        if (image.GameHash != "0E1B540314D591A17215BBC46FD17C2605CCFA58A7D30418D62453FFF0FEB3B6"
            || image.MetadataHash != "DE0A546D36EB3C59C68178B5B523B56D30C4609E62D770FA78A9DA0DE93037DC")
            throw new InvalidDataException("Four-feature native adapters require reviewed build 26289478.");
        foreach (var hook in RawHooks) {
            var bytes = image.Bytes((ulong)hook.Rva, hook.Size);
            if (Convert.ToHexString(SHA256.HashData(bytes)) != hook.Sha256
                || !bytes.Take(hook.Prologue.Length / 2).SequenceEqual(Convert.FromHexString(hook.Prologue)))
                throw new InvalidDataException("Raw native contract changed: " + hook.Id);
        }
    }
}
