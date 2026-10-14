using System;
using System.Collections.Generic;
using System.Linq;

namespace CampaignCore;

/// <summary>Opt-in per-piece home/away palettes. Unmarked legacy kit colors stay inherited.</summary>
public static class PlayerKitColors
{
    public static readonly string[] Schemes = { "jersey", "helmet", "gloves", "pants", "socks", "number" };
    public static string ModeKey(string scheme, bool away) => "Custom " + scheme + (away ? " Away" : "") + " Colors";
    public static IEnumerable<string> ModeKeys
    {
        get { foreach (string s in Schemes) { yield return ModeKey(s, false); yield return ModeKey(s, true); } }
    }
    public static IEnumerable<string> Keys
    {
        get
        {
            foreach (string key in ModeKeys) yield return key;
            foreach (string s in Schemes) foreach (bool away in new[] { false, true })
                for (int c = 0; c < 3; c++) yield return AppearanceContract.ColorKey(s, c, away);
        }
    }
    public static bool Enabled(Func<string, string> read, string scheme, bool away)
        => read(ModeKey(scheme, away)).Equals("yes", StringComparison.OrdinalIgnoreCase);
    public static bool Owns(Func<string, string> read, string key)
    {
        foreach (string s in Schemes) foreach (bool a in new[] { false, true })
            if (Enabled(read, s, a)) for (int c = 0; c < 3; c++)
                if (AppearanceContract.ColorKey(s, c, a).Equals(key, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
    public static CampaignDocument Palette(CampaignDocument player, bool away, CampaignDocument fallback = null)
    {
        var result = new CampaignDocument("");
        foreach (string scheme in Schemes.Where(s => Enabled(k => player.Get(k), s, away)))
            for (int c = 0; c < 3; c++)
            {
                string key = AppearanceContract.ColorKey(scheme, c, away);
                string value = player.Get(key);
                if (value.Length == 0) value = fallback?.Get(key) ?? "";
                result.Set(key, value.Length > 0 ? value : AppearanceContract.DefaultColor(scheme, c));
            }
        return result;
    }
}
