using CampaignCore;
using System.Text.Json;
namespace UnityEngine {
    public struct Color {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color white => new(1, 1, 1, 1);
    }
    public static class JsonUtility {
        static readonly JsonSerializerOptions Options = new() { IncludeFields = true };
        public static string ToJson(object value) => JsonSerializer.Serialize(value, value.GetType(), Options);
        public static T FromJson<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);
    }
    public static class Resources { public static T[] FindObjectsOfTypeAll<T>() => Array.Empty<T>(); }
}
namespace Rogue.BenchPowers { public class BenchPower { public string ID; } }
namespace Data {
    public class SkaterData : Il2CppSystem.Object {
        public ColorScheme StickColor, SkatesColor;
        public string HomeLogo, AwayLogo;
    }
    public enum NumberFormat { Standard, Rounded }
    public struct DateDMY { public int Day, Month, Year; }
    public class ColorScheme { public UnityEngine.Color primaryColor, secondaryColor, tertiaryColor; }
    public class TeamColorsData {
        public string Value;
        public ColorScheme stickScheme = new(), skatesScheme = new();
        public static TeamColorsData Default { get; } = new();
    }
    public class GoaltenderData : SkaterData {
        public string FullName => id;
        public string id, OriginalId, name, helmetAwaySkin, flavorTextLoc, perkFlavorTextLoc, powerupTextLoc;
        public int skill, heightInches, weightKg;
        public float passReadSkill;
        public NumberFormat numberFormat;
        public bool ignoreStatsGrouping, hideInCompendium, lockedWithSquadInCompendium, isCustomSkater, isFemale, HasGoldenHelmet;
        public DateDMY birthDate;
        public Rogue.BenchPowers.BenchPower benchPower;
        public TeamColorsData colorSchemes;
        public List<string> powerups = new();
        public State.SkaterState Earned;
        // Native SaveState allocates a fresh state, not the last LoadState object.
        public State.SkaterState SaveState() {
            var options = new JsonSerializerOptions { IncludeFields = true };
            return JsonSerializer.Deserialize<State.SkaterState>(JsonSerializer.Serialize(Earned ?? new() { Id = id, HasGoldenHelmet = HasGoldenHelmet }, options), options);
        }
        public void LoadState(State.SkaterState state) { Earned = state; HasGoldenHelmet = state.HasGoldenHelmet; }
    }
    public partial class TeamData : Il2CppSystem.Object { public string id; public GoaltenderData goalie; }
    public class Point { public float x, y; }
    public partial class RunData : Il2CppSystem.Object { public string currentMap; public int CurrentAct; public Point currentNodePoint = new(); public string runID, squadId; public TeamData teamData; public State.TeamState teamState; }
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
    static class JerseyLogoRuntime {
        public static CampaignDocument Snapshot(Data.SkaterData goalie) {
            var d = new CampaignDocument(""); d.Set("Jersey Home", goalie.HomeLogo ?? ""); d.Set("Jersey Away", goalie.AwayLogo ?? ""); return d;
        }
        public static void Restore(Data.SkaterData goalie, CampaignDocument d) { goalie.HomeLogo = d.Get("Jersey Home"); goalie.AwayLogo = d.Get("Jersey Away"); }
    }
    // Actual RecruitSnapshot/RestoreRecruit/OwnColors/Scheme/Merge/Binding are
    // extracted each build. Only context/renderer/logo services are doubled.
    static partial class CampaignAppearanceRuntime {
        private static readonly Dictionary<IntPtr, Binding> Bindings = new();
        private static void CheckContext() { }
        private static void SyncLogo(Data.SkaterData player, Binding binding) { }
    }
}
namespace EndlessMode {
    class PlayerConfig { public CampaignCore.CampaignDocument Fields = new(""); }
    class TeamConfig { public PlayerConfig Goalie = new(); }
    static partial class SquadPoolRuntime {
        internal static readonly Dictionary<string, TeamConfig> Teams = new();
        public static TeamConfig Team(string squad) => squad != null && Teams.TryGetValue(squad, out var team) ? team : null;
    }
}
namespace Rogue { public class Handcuffs {} }
namespace Rogue.FreeAgents { public class FreeAgentManager { public State.CampaignState _campaignState; } }
namespace Data {
    public partial class TeamData {
        public bool NativeHandcuffs, FailProtection;
        public System.Type LastRelicQuery;
        public bool HaveRelicCached<T>() {
            LastRelicQuery = typeof(T);
            if (FailProtection) throw new InvalidOperationException("native protection unavailable");
            return typeof(T) == typeof(Rogue.Handcuffs) && NativeHandcuffs;
        }
    }
}
namespace HarmonyLib { public class HarmonyPrefix : System.Attribute {} }
