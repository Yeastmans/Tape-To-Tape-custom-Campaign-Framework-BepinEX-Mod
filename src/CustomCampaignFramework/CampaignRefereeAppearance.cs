using System;
using CampaignCore;
using Data;
using Spine;
using Tape2Tape;

namespace EndlessMode;

// Called only from the existing SkinHelper.GetSkins postfix for SkaterType.Referee.
// The native official data, selection, uniforms, injuries and gameplay remain owned by the game.
internal static class CampaignRefereeAppearance
{
    private static string head = "", campaign = "", folder = "";
    private static bool warned;
    private static readonly string[] Directions = { "S", "N", "W", "E", "NE", "NW", "SW", "SE" };

    internal static void Reset()
    {
        head = campaign = folder = ""; warned = false;
    }
    internal static void Configure(CampaignDocument settings)
    {
        Reset();
        head = HeadAppearance.Base(settings.Get(FieldSchema.RefereeHeadKey).Trim());
        campaign = Plugin.ActiveCampaign; folder = Plugin.ModFolder;
    }
    internal static void Apply(SkeletonData skeleton, ForwardData referee, Il2CppSystem.Collections.Generic.List<Skin> skins)
    {
        if (Plugin.IsDefaultMode || head.Length == 0
            || Plugin.ActiveCampaign != campaign || Plugin.ModFolder != folder) return;
        try
        {
            if (GameManager.GameType != ScriptableStates.GameType.Rogue) return;
            for (int i = 0; i < skins.Count; i++)
            {
                var original = skins[i];
                if (original == null || !string.Equals(original.Name, referee.headSkin, StringComparison.Ordinal)) continue;
                // Custom families register after campaign configuration is read.
                var selected = CustomHeadRuntime.RefereeHead(skeleton, HeadAppearance.Base(head));
                if (selected == null) { Warn("Head is unavailable: " + head); return; }
                var replacement = Compose(skeleton, original, selected);
                if (replacement == null) { Warn("Head attachments are unavailable: " + head); return; }
                skins[i] = replacement;
                return;
            }
        }
        catch (Exception ex) { Warn(ex.ToString()); }
    }
    internal static Skin Compose(SkeletonData skeleton, Skin original, Skin selected)
    {
        int main = skeleton.FindSlot("Helmet")?.Index ?? -1;
        if (main < 0) return null;
        // Require every regular direction before replacing anything. Named native
        // lookups avoid unsafe IL2CPP SkinEntry enumeration.
        foreach (string direction in Directions)
            if (selected.GetAttachment(main, direction + "-Helmet") == null) return null;
        var result = new Skin(original.Name + "+CampaignReferee/" + selected.Name);
        result.AddSkin(original);
        foreach (string direction in Directions)
            result.SetAttachment(main, direction + "-Helmet", selected.GetAttachment(main, direction + "-Helmet"));
        result.RemoveAttachment(main, "N-Helmet2");
        var alternate = selected.GetAttachment(main, "N-Helmet2");
        if (alternate != null) result.SetAttachment(main, "N-Helmet2", alternate);
        int top = skeleton.FindSlot("Helmet_Top")?.Index ?? -1;
        if (top >= 0) foreach (string direction in Directions)
        {
            string name = direction + "-Helmet_top";
            result.RemoveAttachment(top, name);
            var attachment = selected.GetAttachment(top, name);
            if (attachment != null) result.SetAttachment(top, name, attachment);
        }
        return result;
    }
    private static void Warn(string detail)
    {
        if (warned) return;
        warned = true;
        Plugin.Log.LogWarning("[Referee head] Keeping the native referee appearance. " + detail);
    }
}
