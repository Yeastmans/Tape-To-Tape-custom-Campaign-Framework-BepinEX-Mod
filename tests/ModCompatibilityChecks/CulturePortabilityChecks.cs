using System.Globalization;
using System.Text.Json;
using Iced.Intel;
using ModCompatibility;

internal static class CulturePortabilityChecks
{
    // Both reviewed Steam builds declare this default. A decimal-comma locale must
    // still resolve the exact default, without accepting a different overload.
    private const string FloatMethod = "Animations.SkatingAnimationManager$$SetSlapShotWindupAnimationWithTurning";
    private const string FloatSignature = "Spine.TrackEntry SetSlapShotWindupAnimationWithTurning(System.Boolean isNorth, System.Single timeRemaining, System.Single maxTimeTurning = 0.12)";
    private const string FloatIdentity = FloatMethod + "::" + FloatSignature;

    public static void Run(CompatibilityBaseline baseline, NativeImage image, Action<bool, string> check,
        Action<Action, string> reject)
    {
        VerifyCultures(baseline, image, check, reject);
        VerifyInventory(baseline, image, check, reject);
        VerifyInstructionGuard(baseline, image, reject);
    }

    public static void VerifyCultures(CompatibilityBaseline baseline, NativeImage image, Action<bool, string> check,
        Action<Action, string> reject)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        string reviewedProfile = JsonSerializer.Serialize(baseline.Profile, CompatibilityBaseline.Json);
        var floatMethod = image.Find(FloatMethod).Single();
        try
        {
            foreach (string name in new[] { "en-US", "fr-FR", "de-DE", "tr-TR", "sv-SE", "ar-SA", "ja-JP", "" })
            {
                var culture = name.Length == 0 ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo(name);
                string label = name.Length == 0 ? "invariant" : name;
                CultureInfo.CurrentCulture = culture;
                CultureInfo.CurrentUICulture = culture;
                check(image.Signature(floatMethod) == FloatSignature,
                    "Reviewed floating-point default retains its exact signature under " + label);
                check(image.Identity(image.Resolve(FloatIdentity)) == FloatIdentity,
                    "Exact floating-point overload resolves under " + label);
                reject(() => image.Resolve(FloatIdentity.Replace(" = 0.12)", " = 0.13)", StringComparison.Ordinal)),
                    "Different floating-point default cannot alias the reviewed method under " + label);
                try
                {
                    var verified = baseline.Verify(image, baseline.Profile.Build);
                    check(JsonSerializer.Serialize(verified, CompatibilityBaseline.Json) == reviewedProfile,
                        "Unchanged game verifies the complete reviewed runtime profile under " + label);
                }
                catch (InvalidDataException ex)
                {
                    throw new Exception("Unchanged game must verify under " + label, ex);
                }
                check(ReferenceEquals(CultureInfo.CurrentCulture, culture)
                    && ReferenceEquals(CultureInfo.CurrentUICulture, culture),
                    "Successful and rejected native verification preserve caller cultures under " + label);
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
        check(ReferenceEquals(CultureInfo.CurrentCulture, originalCulture)
            && ReferenceEquals(CultureInfo.CurrentUICulture, originalUiCulture),
            "Culture regression checks restore both original caller cultures");
    }

    public static void VerifyInventory(CompatibilityBaseline baseline, NativeImage image, Action<bool, string> check,
        Action<Action, string> reject)
    {
        const string duplicate = "MatchPreviewMenu$$RefreshUnknownRewards|norelics";
        const string otherDuplicate = "STS.Map.TemplateMapGenerator$$GetMap|oldschool";
        const string unique = "FaceoffWithReferee$$OnEndOfReplay|eliminator";
        var actual = image.SquadCallInventory();
        check(actual.Length == 109 && actual.Count(s => s == unique) == 1,
            "Reviewed inventory retains all 109 calls and the referee replay query");
        foreach (string entry in new[] { duplicate, otherDuplicate, "UI.Scoreboard.ScoreboardCanvasController$$Start|snes" })
            check(actual.Count(s => s == entry) == 2, "Both real query occurrences remain protected: " + entry);

        var reordered = Copy(baseline);
        Array.Reverse(reordered.SquadCalls);
        var verified = reordered.Verify(image, baseline.Profile.Build);
        check(JsonSerializer.Serialize(verified, CompatibilityBaseline.Json)
            == JsonSerializer.Serialize(baseline.Profile, CompatibilityBaseline.Json),
            "Equivalent inventory ordering preserves the complete reviewed runtime profile");

        void RejectInventory(Func<string[], string[]> change, string label)
        {
            var altered = Copy(baseline);
            altered.SquadCalls = change(altered.SquadCalls);
            reject(() => altered.Verify(image, "inventory-mutation"), label);
        }
        RejectInventory(rows => rows.Where(row => row != unique).ToArray(),
            "Missing real squad query requires repair");
        RejectInventory(rows => [.. rows, "FaceoffWithReferee$$OnEndOfReplay|tchel"],
            "Extraneous squad query requires repair");
        RejectInventory(rows => rows.Select(row => row == unique ? "FaceoffWithReferee$$OnEndOfReplay|tchel" : row).ToArray(),
            "Changed real squad literal requires repair despite unchanged call count");
        RejectInventory(rows => rows.Where((_, index) => index != Array.IndexOf(rows, duplicate)).ToArray(),
            "Missing occurrence of a duplicated real query requires repair");
        RejectInventory(rows => [.. rows, duplicate],
            "Extra occurrence of a duplicated real query requires repair");
        RejectInventory(rows =>
        {
            rows[Array.IndexOf(rows, duplicate)] = otherDuplicate;
            return rows;
        }, "Changed duplicate multiplicity requires repair even when total count and distinct queries match");
    }

    private static void VerifyInstructionGuard(CompatibilityBaseline baseline, NativeImage image,
        Action<Action, string> reject)
    {
        // Mutate the decoded real instruction, not the expected baseline hash.
        // This fails if native fingerprint validation is accidentally bypassed.
        var body = image.Instructions(image.Resolve(FloatIdentity).Rva);
        var original = body[0];
        var decoder = Decoder.Create(64, new ByteArrayCodeReader(new byte[] { 0x90 }));
        decoder.IP = original.IP;
        decoder.Decode(out var changed);
        try
        {
            body[0] = changed;
            reject(() => baseline.Verify(image, "instruction-mutation"),
                "Altered real native instruction still requires repair after culture normalization");
        }
        finally
        {
            body[0] = original;
        }
    }

    private static CompatibilityBaseline Copy(CompatibilityBaseline baseline)
        => JsonSerializer.Deserialize<CompatibilityBaseline>(JsonSerializer.Serialize(baseline))!;
}
