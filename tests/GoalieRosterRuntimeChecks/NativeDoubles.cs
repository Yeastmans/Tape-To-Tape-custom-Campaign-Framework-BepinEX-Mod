using CampaignCore;
using System.Text.Json;
namespace UnityEngine {
    public static class JsonUtility {
        static readonly JsonSerializerOptions Options = new() { IncludeFields = true };
        public static string ToJson(object value) => JsonSerializer.Serialize(value, value.GetType(), Options);
        public static T FromJson<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);
    }
    public static class Resources { public static T[] FindObjectsOfTypeAll<T>() => Array.Empty<T>(); }
}
namespace Rogue.BenchPowers { public class BenchPower { public string ID; } }
namespace Data {
    public class SkaterData : Il2CppSystem.Object { }
    public enum NumberFormat { Standard, Rounded }
    public struct DateDMY { public int Day, Month, Year; }
    public class ColorScheme { public string Value; }
    public class TeamColorsData { public string Value; }
    public class GoaltenderData : Il2CppSystem.Object {
        public string id, OriginalId, name, helmetAwaySkin, HomeLogo, AwayLogo, flavorTextLoc, perkFlavorTextLoc, powerupTextLoc;
        public int skill, heightInches, weightKg;
        public float passReadSkill;
        public NumberFormat numberFormat;
        public bool ignoreStatsGrouping, hideInCompendium, lockedWithSquadInCompendium, isCustomSkater, isFemale, HasGoldenHelmet;
        public DateDMY birthDate;
        public ColorScheme StickColor, SkatesColor;
        public Rogue.BenchPowers.BenchPower benchPower;
        public TeamColorsData colorSchemes;
        public List<string> powerups = new();
        public State.SkaterState Earned;
        public State.SkaterState SaveState() => Earned ?? new() { Id = id, HasGoldenHelmet = HasGoldenHelmet };
        public void LoadState(State.SkaterState state) { Earned = state; HasGoldenHelmet = state.HasGoldenHelmet; }
    }
    public class TeamData { public GoaltenderData goalie; }
    public class RunData : Il2CppSystem.Object { public string runID, squadId; public TeamData teamData; public State.TeamState teamState; }
}
namespace State {
    public class SkaterDataModelV2 : Il2CppSystem.Object { }
    [System.Text.Json.Serialization.JsonPolymorphic(TypeDiscriminatorPropertyName = "__type")]
    [System.Text.Json.Serialization.JsonDerivedType(typeof(CounterState), "counter")]
    public class PowerUpState { public string Id; }
    public class CounterState : PowerUpState { public int Earned; public List<int> History = new(); }
    public class SkaterState : Il2CppSystem.Object { public string Id; public PowerUpState abilityState; public List<PowerUpState> talentStates = new(); public bool HasGoldenHelmet; }
    public class GoaltenderDataModelV2 : Il2CppSystem.Object {
        public string id;
        public int skill;
        public Data.TeamColorsData colors;
        public List<string> powers;
    }
    public class SerializerV2 {
        public GoaltenderDataModelV2 SerializeToModel(Data.GoaltenderData g) => new() { id = g.id, skill = g.skill, colors = g.colorSchemes, powers = g.powerups };
    }
    public class DeserializerV2 : Il2CppSystem.Object {
        public object Talents, Relics, Abilities, Logos;
        public DeserializerV2() {}
        public DeserializerV2(object talents, object relics, object abilities, object logos) { Talents = talents; Relics = relics; Abilities = abilities; Logos = logos; }
        public Data.GoaltenderData ConvertGoaltenderDataModelToGoaltenderData(GoaltenderDataModelV2 m) => new() { id = m.id, skill = m.skill, colorSchemes = m.colors, powerups = m.powers };
    }
    public class TeamState : Il2CppSystem.Object { public SkaterState goaltenderState; public string[] forwardStates, relicStates; }
    public class TeamDataModelV2 : Il2CppSystem.Object { public List<GoaltenderDataModelV2> skaters; }
}
namespace EndlessMode {
    static class GoalieRecruitment {
        public static CampaignDocument Snapshot(Data.GoaltenderData goalie) {
            var d = new CampaignDocument("");
            d.Set("Recruit Pass Read", goalie.passReadSkill.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            d.Set("Recruit Number Format", ((int)goalie.numberFormat).ToString());
            d.Set("Recruit Away Helmet", goalie.helmetAwaySkin ?? ""); return d;
        }
    }
    static class JerseyLogoRuntime {
        public static CampaignDocument Snapshot(Data.GoaltenderData goalie) {
            var d = new CampaignDocument(""); d.Set("Jersey Home", goalie.HomeLogo ?? ""); d.Set("Jersey Away", goalie.AwayLogo ?? ""); return d;
        }
    }
    static class CampaignAppearanceRuntime {
        public static void RestoreRecruit(Data.GoaltenderData goalie, CampaignDocument d) { goalie.HomeLogo = d.Get("Jersey Home"); goalie.AwayLogo = d.Get("Jersey Away"); }
    }
}
