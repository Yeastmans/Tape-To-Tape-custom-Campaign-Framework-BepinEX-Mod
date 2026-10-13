using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using State;
using UnityEngine;

namespace EndlessMode;

internal static class GameCatalogBridge
{
    private static string fingerprint;
    internal static string Fingerprint
    {
        get
        {
            if (fingerprint != null) return fingerprint;
            using var input = File.OpenRead(Path.Combine(BepInEx.Paths.GameRootPath, "GameAssembly.dll"));
            using var sha = SHA256.Create();
            fingerprint = Convert.ToHexString(sha.ComputeHash(input)).ToLowerInvariant();
            return fingerprint;
        }
    }
    internal static void WriteSquads(string root)
    {
        var options = new StringBuilder("# Runtime editor options\nversion\t1\ngame_sha256\t" + Fingerprint + "\n");
        foreach (string size in Enum.GetNames(typeof(Data.SkaterSize))) options.AppendLine("skater\tSize\t" + size);
        foreach (string type in Enum.GetNames(typeof(DAGG.Core.NodeType))) options.AppendLine("map\tNode Type\t" + type);
        File.WriteAllText(Path.Combine(root, "_editor_options.tsv"), options.ToString());
        var sb = new StringBuilder("# Game squad catalog\nversion\t1\n");
        sb.AppendLine("game_sha256\t" + Fingerprint);
        sb.AppendLine("id\tname\tforward_slots\tmap_count");
        var seen = new System.Collections.Generic.HashSet<string>();
        foreach (var squad in Resources.FindObjectsOfTypeAll<RunSquadScriptableObject>())
        {
            if (!LogRepositories.IsShipped(squad) || string.IsNullOrEmpty(squad.id) || squad.id.StartsWith("Custom_") || !seen.Add(squad.id)) continue;
            sb.Append(squad.id.Replace('\t', ' ')).Append('\t')
                .Append((squad.squadName ?? squad.id).Replace('\t', ' ').Replace('\n', ' ')).Append('\t')
                .Append(squad.startingTeam?.forwards?.Count ?? 0).Append('\t').Append(squad.maps?.Count ?? 0).AppendLine();
        }
        File.WriteAllText(Path.Combine(root, "_squad_templates.tsv"), sb.ToString());
        Plugin.Log.LogInfo($"[GameCatalog] Exported {seen.Count} base squad templates for game {Fingerprint.Substring(0, 12)}");
    }
}
