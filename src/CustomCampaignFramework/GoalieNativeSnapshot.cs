using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using CampaignCore;
using Data;
using Rogue.BenchPowers;
using State;
using UnityEngine;

namespace EndlessMode;

internal static class GoalieNativeSnapshot
{
    // Supplied by the exact V2 repository or hydration boundary, with its injected
    // repositories. Never guess a repository or reconstruct earned state from
    // an authored candidate. Fresh save admission binds the native constructor too.
    internal static DeserializerV2 Deserializer;

    private sealed class GraphEnvelope
    {
        public int Version { get; set; } = 1;
        public string Profile { get; set; } = GoalieNativeGraph.Profile;
        public string Role { get; set; } = "Goaltender";
        public string Model { get; set; }
        public string State { get; set; }
    }
    internal sealed class Restored
    {
        internal GoaltenderData Goalie { get; }
        internal SkaterState State { get; }
        internal Restored(GoaltenderData goalie, SkaterState state) { Goalie = goalie; State = state; }
    }

    private sealed class MissingFields
    {
        public int Version { get; set; } = 1;
        public string OriginalId { get; set; }
        public string Name { get; set; }
        public bool IgnoreStatsGrouping { get; set; }
        public bool HideInCompendium { get; set; }
        public bool LockedWithSquad { get; set; }
        public bool Custom { get; set; }
        public bool Female { get; set; }
        public bool GoldenHelmet { get; set; }
        public int Height { get; set; }
        public int Weight { get; set; }
        public int BirthDay { get; set; }
        public int BirthMonth { get; set; }
        public int BirthYear { get; set; }
        public string Flavor { get; set; }
        public string PerkFlavor { get; set; }
        public string PowerupFlavor { get; set; }
        public string StickColor { get; set; }
        public string SkatesColor { get; set; }
        public string BenchPower { get; set; }
    }
    private static GoaltenderDataModelV2 Model(GoaltenderData goalie)
    {
        var model = new SerializerV2().SerializeToModel(goalie)?.TryCast<GoaltenderDataModelV2>();
        if (model == null || model.id != goalie.id) throw new InvalidDataException("Native goalie serialization changed identity.");
        return model;
    }
    internal static GoalieRecord Capture(GoaltenderData goalie)
    {
        if (goalie == null || string.IsNullOrWhiteSpace(goalie.id)) throw new InvalidDataException("A goalie snapshot needs stable ownership.");
        var supplement = GoalieRecruitment.Snapshot(goalie);
        // RecruitSnapshot can have no appearance binding on a native starter.
        // Both explicit logo bindings still belong to this goalie.
        foreach (var pair in JerseyLogoRuntime.Snapshot(goalie).Fields) supplement.Set(pair.Key, pair.Value);
        supplement.Set("Goalie Native Omitted Fields", JsonSerializer.Serialize(new MissingFields {
            OriginalId = goalie.OriginalId, Name = goalie.name, IgnoreStatsGrouping = goalie.ignoreStatsGrouping,
            HideInCompendium = goalie.hideInCompendium, LockedWithSquad = goalie.lockedWithSquadInCompendium,
            Custom = goalie.isCustomSkater, Female = goalie.isFemale, GoldenHelmet = goalie.HasGoldenHelmet,
            Height = goalie.heightInches, Weight = goalie.weightKg,
            BirthDay = goalie.birthDate.Day, BirthMonth = goalie.birthDate.Month, BirthYear = goalie.birthDate.Year,
            Flavor = goalie.flavorTextLoc, PerkFlavor = goalie.perkFlavorTextLoc, PowerupFlavor = goalie.powerupTextLoc,
            StickColor = goalie.StickColor == null ? null : JsonUtility.ToJson(goalie.StickColor),
            SkatesColor = goalie.SkatesColor == null ? null : JsonUtility.ToJson(goalie.SkatesColor), BenchPower = goalie.benchPower?.ID
        }));
        var state = goalie.SaveState();
        if (state == null || state.Id != goalie.id || state.HasGoldenHelmet != goalie.HasGoldenHelmet)
            throw new InvalidDataException("Native goalie earned state has different ownership.");
        return new GoalieRecord { PlayerId = goalie.id, NativePayload = JsonSerializer.Serialize(new GraphEnvelope {
            Model = GoalieNativeGraph.Capture(Model(goalie)), State = GoalieNativeGraph.Capture(state)
        }), Supplemental = supplement.ToString() };
    }
    // The caller publishes this pair together: TeamData.LoadState otherwise
    // overwrites the restored active with the old slot's earned state.
    internal static GoaltenderData Restore(GoalieRecord record) => RestorePair(record, Deserializer).Goalie;
    internal static Restored RestorePair(GoalieRecord record, DeserializerV2 deserializer)
    {
        record.Validate();
        var graph = Required<GraphEnvelope>(record.NativePayload);
        if (graph.Version != 1 || graph.Profile != GoalieNativeGraph.Profile || graph.Role != "Goaltender")
            throw new InvalidDataException("Unsupported goalie graph profile or role.");
        var model = GoalieNativeGraph.Restore<GoaltenderDataModelV2>(graph.Model);
        var state = GoalieNativeGraph.Restore<SkaterState>(graph.State);
        if (model == null || model.id != record.PlayerId) throw new InvalidDataException("Native goalie snapshot identity mismatch.");
        if (state == null || state.Id != record.PlayerId || state.talentStates == null)
            throw new InvalidDataException("Native goalie earned-state identity or collection mismatch.");
        var supplemental = new CampaignDocument(record.Supplemental);
        var omitted = Required<MissingFields>(supplemental.Get("Goalie Native Omitted Fields"));
        if (omitted == null || omitted.Version != 1) throw new InvalidDataException("Missing or unsupported goalie supplemental snapshot.");
        if (omitted.GoldenHelmet != state.HasGoldenHelmet) throw new InvalidDataException("Conflicting goalie golden helmet state.");
        if (!int.TryParse(supplemental.Get("Recruit Number Format"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int number)
            || number < 0 || number >= FieldSchema.NumberStyles.Length
            || !float.TryParse(supplemental.Get("Recruit Pass Read"), NumberStyles.Float, CultureInfo.InvariantCulture, out float passRead)
            || !float.IsFinite(passRead) || !supplemental.Fields.ContainsKey("Recruit Away Helmet"))
            throw new InvalidDataException("Invalid goalie supplemental values.");
        var stick = omitted.StickColor == null ? null : JsonUtility.FromJson<ColorScheme>(omitted.StickColor);
        var skates = omitted.SkatesColor == null ? null : JsonUtility.FromJson<ColorScheme>(omitted.SkatesColor);
        BenchPower bench = null;
        if (!string.IsNullOrEmpty(omitted.BenchPower))
        {
            var matches = Resources.FindObjectsOfTypeAll<BenchPower>().Where(p => p != null && p.ID == omitted.BenchPower).ToArray();
            if (matches.Length != 1) throw new InvalidDataException("The goalie's saved bench power cannot be resolved uniquely.");
            bench = matches[0];
        }
        var goalie = (deserializer ?? throw new InvalidOperationException("No native goalie deserializer is bound."))
            .ConvertGoaltenderDataModelToGoaltenderData(model);
        if (goalie == null || goalie.id != record.PlayerId)
            throw new InvalidDataException("Native goalie reconstruction changed identity.");
        goalie.LoadState(state);
        if (!GoalieNativeGraph.Equivalent(graph.Model, GoalieNativeGraph.Capture(Model(goalie)))
            || !GoalieNativeGraph.Equivalent(graph.State, GoalieNativeGraph.Capture(goalie.SaveState())))
            throw new InvalidDataException("Native goalie reconstruction lost model or earned-state fields.");
        goalie.OriginalId = omitted.OriginalId; goalie.name = omitted.Name;
        goalie.ignoreStatsGrouping = omitted.IgnoreStatsGrouping; goalie.hideInCompendium = omitted.HideInCompendium;
        goalie.lockedWithSquadInCompendium = omitted.LockedWithSquad; goalie.isCustomSkater = omitted.Custom;
        goalie.isFemale = omitted.Female; goalie.HasGoldenHelmet = omitted.GoldenHelmet;
        goalie.heightInches = omitted.Height; goalie.weightKg = omitted.Weight;
        var birth = goalie.birthDate; birth.Day = omitted.BirthDay; birth.Month = omitted.BirthMonth; birth.Year = omitted.BirthYear; goalie.birthDate = birth;
        goalie.flavorTextLoc = omitted.Flavor; goalie.perkFlavorTextLoc = omitted.PerkFlavor; goalie.powerupTextLoc = omitted.PowerupFlavor;
        goalie.StickColor = stick; goalie.SkatesColor = skates; goalie.benchPower = bench;
        goalie.numberFormat = (NumberFormat)number; goalie.passReadSkill = passRead;
        goalie.helmetAwaySkin = supplemental.Get("Recruit Away Helmet");
        CampaignAppearanceRuntime.RestoreRecruit(goalie, supplemental);
        // Separate native goalieStats/runScorersStats remain native-owned. The
        // stable id, OriginalId and grouping flag above preserve their ownership.
        return new Restored(goalie, state);
    }
    private static T Required<T>(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || typeof(T).GetProperties().Any(p => !document.RootElement.TryGetProperty(p.Name, out _)))
                throw new InvalidDataException("Incomplete goalie snapshot envelope.");
            return JsonSerializer.Deserialize<T>(json);
        }
        catch (JsonException ex) { throw new InvalidDataException("Malformed goalie snapshot envelope.", ex); }
    }
    internal static bool Matches(GoalieRecord record, GoaltenderDataModelV2 model, SkaterState state)
    {
        if (model == null || state == null || model.id != record.PlayerId || state.Id != record.PlayerId) return false;
        var graph = Required<GraphEnvelope>(record.NativePayload);
        return GoalieNativeGraph.Equivalent(graph.Model, GoalieNativeGraph.Capture(model))
            && GoalieNativeGraph.Equivalent(graph.State, GoalieNativeGraph.Capture(state));
    }
}
