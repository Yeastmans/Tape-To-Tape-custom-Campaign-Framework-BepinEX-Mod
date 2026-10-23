using UnityEngine;
namespace Data {
    public partial class SkaterData {
        public bool HasCustomStickColor => StickColor != null && StickColor.primaryColor.a > 0;
        public bool HasCustomSkatesColor => SkatesColor != null && SkatesColor.primaryColor.a > 0;
    }
    public partial class TeamData {
        public TeamColorsData homeColors = new(), awayColors = new(), homeGoalieColors, awayGoalieColors;
    }
    public partial class ColorScheme {
        public static ColorScheme CopyFrom(ColorScheme source) => source == null ? new() : new() {
            primaryColor = source.primaryColor, secondaryColor = source.secondaryColor, tertiaryColor = source.tertiaryColor };
    }
    public partial class TeamColorsData {
        public ColorScheme jerseyScheme = new(), numberScheme = new(), pantsScheme = new(), glovesScheme = new(), helmetScheme = new(), socksScheme = new();
        public static TeamColorsData CopyFrom(TeamColorsData source) => new() {
            jerseyScheme = ColorScheme.CopyFrom(source.jerseyScheme), numberScheme = ColorScheme.CopyFrom(source.numberScheme),
            pantsScheme = ColorScheme.CopyFrom(source.pantsScheme), glovesScheme = ColorScheme.CopyFrom(source.glovesScheme),
            helmetScheme = ColorScheme.CopyFrom(source.helmetScheme), socksScheme = ColorScheme.CopyFrom(source.socksScheme),
            stickScheme = ColorScheme.CopyFrom(source.stickScheme), skatesScheme = ColorScheme.CopyFrom(source.skatesScheme) };
        // Native boundary: Merge selects the entire nonempty per-piece scheme.
        public static TeamColorsData Merge(TeamColorsData team, TeamColorsData player) {
            var result = CopyFrom(team);
            if (player == null) return result;
            foreach (var field in typeof(TeamColorsData).GetFields().Where(f => f.FieldType == typeof(ColorScheme))) {
                var value = (ColorScheme)field.GetValue(player);
                if (value != null && (value.primaryColor.a > 0 || value.secondaryColor.a > 0 || value.tertiaryColor.a > 0))
                    field.SetValue(result, ColorScheme.CopyFrom(value));
            }
            return result;
        }
    }
}
namespace EndlessMode {
    static partial class CampaignAppearanceRuntime {
        private static Data.ColorScheme DefaultStickColors() => Data.TeamColorsData.Default.stickScheme;
    }
    static partial class PlayNowTranslation {
        internal static CampaignCore.CampaignDocument ReadPlayer(Data.SkaterData player) => null;
        private static bool TryKit(Data.TeamColorsData colors, out bool away) { away = false; return false; }
    }
}

namespace Tape2Tape.Customization.UI {
    public class SkaterPreviewInUI : Il2CppSystem.Object {
        public Tape2Tape.Hockey.UI.LineupMenu Parent; public bool m_IsHome;
        public T GetComponentInParent<T>(bool inactive) where T : class => Parent as T;
    }
}

public partial class GameManager {
    public static GameManager _instance;
    public static object HomeTeam, VisitorTeam;
}
namespace EndlessMode {
    static partial class LineupPreviewKit {
        // The unrelated ordinary-lineup kit resolver is outside this menu test.
        internal static bool TryResolve(Data.TeamData team, object homeTeam, object awayTeam, out bool home) { home = true; return false; }
    }
}

namespace Data { public class ForwardData : SkaterData { public TeamColorsData colorSchemes; } }
namespace EndlessMode {
    static class AppearanceDecalRuntime { internal static bool ClearIncompatibleLogo(Data.SkaterData player) => false; }
}
