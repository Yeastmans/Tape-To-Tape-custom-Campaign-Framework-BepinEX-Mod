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
        if (image.GameHash != "54982E5C98932B701B4549086BF08ECC792DA5DE43A5E3627F8804FE657AFF58"
            || image.MetadataHash != "9FB0F8B4CBB1D238AF057973C3E65BA7CDC27201C3D3E5EE0664C87FCEE38F58")
            throw new InvalidDataException("Four-feature native adapters require reviewed build 26188487.");
        foreach (var hook in RawHooks) {
            var bytes = image.Bytes((ulong)hook.Rva, hook.Size);
            if (Convert.ToHexString(SHA256.HashData(bytes)) != hook.Sha256
                || !bytes.Take(hook.Prologue.Length / 2).SequenceEqual(Convert.FromHexString(hook.Prologue)))
                throw new InvalidDataException("Raw native contract changed: " + hook.Id);
        }
    }
}
