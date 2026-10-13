using System;
using System.Collections.Generic;
using System.Linq;
using Rogue.Powerups.Repository;
using Rogue.Relics.Repository;
using UnityEngine;

namespace EndlessMode;

internal static class NativeCustomizationOptions
{
    private static readonly Dictionary<string, HashSet<string>> Options = new();
    private static string Key(string kind, bool goalie) => kind == "relic" ? kind : kind + (goalie ? "-goalie" : "-skater");
    private static HashSet<string> Ids<T>(Il2CppSystem.Collections.Generic.IEnumerable<T> source, Func<T, string> id) where T : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        var enumerator = source.GetEnumerator();
        var cursor = enumerator.Cast<Il2CppSystem.Collections.IEnumerator>();
        while (cursor.MoveNext()) { var item = enumerator.Current; if (item != null) result.Add(id(item)); }
        return result;
    }
    private static HashSet<string> Load(string kind, bool goalie)
    {
        string key = Key(kind, goalie);
        if (Options.TryGetValue(key, out var cached)) return cached;
        HashSet<string> choices;
        if (kind == "talent")
        {
            var repo = Resources.FindObjectsOfTypeAll<TalentRepository>().FirstOrDefault(r => r != null);
            if (repo == null) return null;
            choices = Ids(goalie ? repo.GetCustomizationGoalieTalents() : repo.GetCustomizationSkaterTalents(), t => t.id);
        }
        else if (kind == "ability")
        {
            var repo = Resources.FindObjectsOfTypeAll<AbilityRepository>().FirstOrDefault(r => r != null);
            if (repo == null) return null;
            choices = Ids(goalie ? repo.GetCustomizationGoalieAbilities() : repo.GetCustomizationSkaterAbilities(), a => a.id);
        }
        else
        {
            var repo = Resources.FindObjectsOfTypeAll<RelicRepository>().FirstOrDefault(r => r != null);
            if (repo == null) return null;
            choices = Ids(repo.GetCustomizationRelics(), r => r.id);
        }
        Options[key] = choices;
        return choices;
    }
    internal static bool Missing(string kind, bool goalie, string id)
    {
        try { var options = Load(kind, goalie); return options != null && !string.IsNullOrEmpty(id) && !options.Contains(id); }
        catch (Exception ex) { Plugin.Log.LogWarning("[CustomOptions] " + ex.Message); return false; }
    }
}
