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
        if (image.GameHash != "F756BCB8564C2D95C60A121C7A5EBB21DC1B0418421FAF03F4925139944E6142"
            || image.MetadataHash != "1AC5E68FBC382FDE344621BECC943371ED2EA43133C36ABAAEBB198C8CCB0875")
            throw new InvalidDataException("Four-feature native adapters require reviewed build 26372689.");
        foreach (var hook in RawHooks) {
            var bytes = image.Bytes((ulong)hook.Rva, hook.Size);
            if (Convert.ToHexString(SHA256.HashData(bytes)) != hook.Sha256
                || !bytes.Take(hook.Prologue.Length / 2).SequenceEqual(Convert.FromHexString(hook.Prologue)))
                throw new InvalidDataException("Raw native contract changed: " + hook.Id);
        }
    }
}
