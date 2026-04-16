using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Data;
using HarmonyLib;
using STS.Map;
using Tape2Tape;
using Tape2Tape.Hockey.UI;
using Rogue.Relics.Repository;
using Rogue.Powerups.Repository;
using UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using I2.Loc;
using ScriptableStates;
using State;
using Rogue.BenchSnapshots;

namespace EndlessMode;

[BepInPlugin("com.mods.customcampaign", "Custom Campaign Framework", "2.0.0")]
public class Plugin : BasePlugin
{
    internal static new ManualLogSource Log;

    internal const int ScalingPerAct = 8;
    internal const int BossScalingPerAct = 10;
    internal static bool DebugSkipEnabled = false;

    internal static bool BossJustBeaten = false;
    internal static int DebugRealAct = -1;
    internal static bool DebugActForced = false;
    internal static bool TeamsLogged = false;
    internal static bool ReposLogged = false;
    internal static bool SeparateFilesWritten = false;

    // ===== CONFIG =====
    private static readonly string CampaignsRoot = Path.Combine(BepInEx.Paths.PluginPath, "Campaigns");
    private static readonly string ActivePath = Path.Combine(BepInEx.Paths.PluginPath, "Campaigns", "active.txt");
    private static readonly string DefaultsPath = Path.Combine(BepInEx.Paths.PluginPath, "Campaigns", "defaults.txt");

    // Default fallback values (loaded from defaults.txt)
    internal static TeamConfig DefaultTeam = new TeamConfig();
    internal static PlayerConfig DefaultSkater = new PlayerConfig();
    internal static PlayerConfig DefaultGoalie = new PlayerConfig();

    private static string ModFolder;
    private static string ConfigPath;
    private static string SavePath;
    internal static string ActiveCampaign = "NHL Season";

    internal static bool IsDefaultMode = false; // true = no mod behavior, base game only

    internal static void ResolveCampaignPaths()
    {
        // Read active campaign name
        if (File.Exists(ActivePath))
        {
            foreach (var line in File.ReadAllLines(ActivePath))
            {
                string trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#")) continue;
                int eq = trimmed.IndexOf('=');
                if (eq >= 0)
                    ActiveCampaign = trimmed.Substring(eq + 1).Trim();
                else
                    ActiveCampaign = trimmed;
                break;
            }
        }

        // "default" or "none" = disable all mod behavior, play base game
        string lower = ActiveCampaign.Trim().ToLower();
        if (lower == "default" || lower == "none" || lower == "off" || lower == "base game")
        {
            IsDefaultMode = true;
            Log.LogInfo($"[Campaign] DEFAULT MODE — all mod behavior disabled, playing base game");
            ModFolder = Path.Combine(CampaignsRoot, "default");
            ConfigPath = "";
            SavePath = "";
            return;
        }

        ModFolder = Path.Combine(CampaignsRoot, ActiveCampaign);
        ConfigPath = Path.Combine(ModFolder, "campaign.txt");
        SavePath = Path.Combine(ModFolder, "save.txt");

        if (!Directory.Exists(ModFolder))
            Directory.CreateDirectory(ModFolder);

        Log.LogInfo($"[Campaign] Active campaign: '{ActiveCampaign}' ({ModFolder})");
    }

    internal static void LoadDefaults()
    {
        if (!File.Exists(DefaultsPath)) return;
        try
        {
            var lines = File.ReadAllLines(DefaultsPath);
            string section = "";
            foreach (var raw in lines)
            {
                string line = raw.Replace("\t", " ").Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("====") || line.StartsWith("####") || line.StartsWith("#")) continue;
                if (line.StartsWith("---") && line.EndsWith("---"))
                { section = line.Trim('-', ' ').Trim().ToLower(); continue; }
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                string key = line.Substring(0, eq).Trim().ToLower();
                string val = line.Substring(eq + 1).Trim();
                if (string.IsNullOrEmpty(val)) continue;

                if (section == "team defaults" || section == "team colors" || section == "team uniform")
                {
                    if (key == "team name") Plugin.DefaultTeam.Name = val;
                    else if (key == "city") Plugin.DefaultTeam.City = val;
                    else if (key == "abbreviation") Plugin.DefaultTeam.Abbreviation = val;
                    else if (key == "logo from") Plugin.DefaultTeam.LogoFrom = val;
                    else if (key == "jersey primary") Plugin.DefaultTeam.JerseyPrimary = ParseColor(val);
                    else if (key == "jersey secondary") Plugin.DefaultTeam.JerseySecondary = ParseColor(val);
                    else if (key == "jersey accent") Plugin.DefaultTeam.JerseyAccent = ParseColor(val);
                    else if (key == "away primary") Plugin.DefaultTeam.AwayPrimary = ParseColor(val);
                    else if (key == "away secondary") Plugin.DefaultTeam.AwaySecondary = ParseColor(val);
                    else if (key == "away accent") Plugin.DefaultTeam.AwayAccent = ParseColor(val);
                    else if (key == "number color home") Plugin.DefaultTeam.NumberColorHome = ParseColor(val);
                    else if (key == "number color away") Plugin.DefaultTeam.NumberColorAway = ParseColor(val);
                    else if (key == "helmet color") Plugin.DefaultTeam.TeamHelmetColor = ParseColor(val);
                    else if (key == "helmet secondary color") Plugin.DefaultTeam.TeamHelmetSecondary = ParseColor(val);
                    else if (key == "helmet tertiary color") Plugin.DefaultTeam.TeamHelmetTertiary = ParseColor(val);
                    else if (key == "gloves color") Plugin.DefaultTeam.TeamGlovesColor = ParseColor(val);
                    else if (key == "gloves secondary color") Plugin.DefaultTeam.TeamGlovesSecondary = ParseColor(val);
                    else if (key == "gloves tertiary color") Plugin.DefaultTeam.TeamGlovesTertiary = ParseColor(val);
                    else if (key == "pants color") Plugin.DefaultTeam.TeamPantsColor = ParseColor(val);
                    else if (key == "pants secondary color") Plugin.DefaultTeam.TeamPantsSecondary = ParseColor(val);
                    else if (key == "pants tertiary color") Plugin.DefaultTeam.TeamPantsTertiary = ParseColor(val);
                    else if (key == "skates color") Plugin.DefaultTeam.TeamSkatesColor = ParseColor(val);
                    else if (key == "blade color") Plugin.DefaultTeam.TeamBladeColor = ParseColor(val);
                    else if (key == "laces color") Plugin.DefaultTeam.TeamLacesColor = ParseColor(val);
                    else if (key == "bicep color") Plugin.DefaultTeam.TeamBicepColor = ParseColor(val);
                    else if (key == "socks color") Plugin.DefaultTeam.TeamSocksColor = ParseColor(val);
                    else if (key == "socks secondary color") Plugin.DefaultTeam.TeamSocksSecondary = ParseColor(val);
                    else if (key == "socks tertiary color") Plugin.DefaultTeam.TeamSocksTertiary = ParseColor(val);
                    else if (key == "number color") Plugin.DefaultTeam.TeamNumberColor = ParseColor(val);
                    else if (key == "number secondary color") Plugin.DefaultTeam.TeamNumberSecondary = ParseColor(val);
                    else if (key == "body") Plugin.DefaultTeam.Uniform.Body = ResolveSkin(val, "body");
                    else if (key == "body away") Plugin.DefaultTeam.Uniform.BodyAway = ResolveSkin(val, "body");
                    else if (key == "bicep") Plugin.DefaultTeam.Uniform.Bicep = ResolveSkin(val, "bicep");
                    else if (key == "gloves") Plugin.DefaultTeam.Uniform.Gloves = ResolveSkin(val, "gloves");
                    else if (key == "pants") Plugin.DefaultTeam.Uniform.Pants = ResolveSkin(val, "pants");
                    else if (key == "skates") Plugin.DefaultTeam.Uniform.Skates = ResolveSkin(val, "skates");
                    else if (key == "helmet") Plugin.DefaultTeam.Uniform.Helmet = ResolveSkin(val, "helmet");
                    else if (key == "helmet away") Plugin.DefaultTeam.Uniform.HelmetAway = ResolveSkin(val, "helmet");
                    else if (key == "stick") Plugin.DefaultTeam.Uniform.Stick = ResolveSkin(val, "stick");
                }
                else if (section == "default skater")
                {
                    if (key == "name") Plugin.DefaultSkater.Name = val;
                    else if (key == "number") int.TryParse(val, out Plugin.DefaultSkater.Number);
                    else if (key == "face") Plugin.DefaultSkater.Face = val;
                    else if (key == "left handed") Plugin.DefaultSkater.Lefty = val.ToLower() == "yes";
                    else if (key == "skin color") Plugin.DefaultSkater.Black = val.ToLower() == "dark";
                    else if (key == "size") Plugin.DefaultSkater.Size = val;
                    else if (key == "speed") int.TryParse(val, out Plugin.DefaultSkater.Speed);
                    else if (key == "shot power") int.TryParse(val, out Plugin.DefaultSkater.ShotPower);
                    else if (key == "accuracy") int.TryParse(val, out Plugin.DefaultSkater.Accuracy);
                    else if (key == "checking") int.TryParse(val, out Plugin.DefaultSkater.Checking);
                }
                else if (section == "default goalie")
                {
                    if (key == "name") Plugin.DefaultGoalie.Name = val;
                    else if (key == "face") Plugin.DefaultGoalie.Face = val;
                    else if (key == "skill") int.TryParse(val, out Plugin.DefaultGoalie.Skill);
                    else if (key == "catching") int.TryParse(val, out Plugin.DefaultGoalie.Catching);
                    else if (key == "glove") int.TryParse(val, out Plugin.DefaultGoalie.Glove);
                    else if (key == "blocker") int.TryParse(val, out Plugin.DefaultGoalie.Blocker);
                    else if (key == "five hole") int.TryParse(val, out Plugin.DefaultGoalie.FiveHole);
                    else if (key == "standing speed") int.TryParse(val, out Plugin.DefaultGoalie.StandSpeed);
                    else if (key == "butterfly speed") int.TryParse(val, out Plugin.DefaultGoalie.ButterflySpeed);
                    else if (key == "control") int.TryParse(val, out Plugin.DefaultGoalie.Control);
                    else if (key == "recovery") int.TryParse(val, out Plugin.DefaultGoalie.Recovery);
                    else if (key == "pass power") int.TryParse(val, out Plugin.DefaultGoalie.PassPower);
                    else if (key == "shot power") int.TryParse(val, out Plugin.DefaultGoalie.ShotPower);
                    else if (key == "poke check") int.TryParse(val, out Plugin.DefaultGoalie.Pokecheck);
                    else if (key == "depth") int.TryParse(val, out Plugin.DefaultGoalie.Depth);
                    else if (key == "pass read") { val = val.Replace("f",""); float.TryParse(val, out Plugin.DefaultGoalie.PassRead); }
                }
            }
            Log.LogInfo($"[Config] Loaded defaults.txt (team='{Plugin.DefaultTeam.Name}', logo='{Plugin.DefaultTeam.LogoFrom}')");
        }
        catch (Exception ex) { Log.LogError($"[Config] Failed to load defaults.txt: {ex.Message}"); }
    }

    // Campaign settings
    internal static int[] ActSequence = new[] { 1, 1, 2, 2, 1, 2, 2, 2, 3 };
    internal static int TotalMaps => ActSequence.Length;
    internal static bool ReplaceChallenges = true;
    internal static List<int> ReplaceChallengesActs = null; // null = all acts, list = only these acts
    internal static bool DumpData = false; // Debug only — generates reference dump files
    internal static bool ReplaceSoccerBall = true;
    internal static bool ReplaceGolfBall = true;

    // Shared RNG for random() values — seeded once per config load
    internal static System.Random ConfigRng = new System.Random();

    /// <summary>
    /// Parse a value that may contain random(min,max). Returns resolved int.
    /// Supports: "50", "random(30,70)", "random(30, 70)"
    /// </summary>
    internal static int ParseRandomInt(string val)
    {
        if (string.IsNullOrEmpty(val)) return 0;
        val = val.Trim();
        if (val.StartsWith("random(", StringComparison.OrdinalIgnoreCase) && val.EndsWith(")"))
        {
            string inner = val.Substring(7, val.Length - 8);
            var parts = inner.Split(',');
            if (parts.Length == 2 &&
                int.TryParse(parts[0].Trim(), out int min) &&
                int.TryParse(parts[1].Trim(), out int max))
            {
                return ConfigRng.Next(min, max + 1); // inclusive max
            }
        }
        if (int.TryParse(val, out int result)) return result;
        return 0;
    }

    /// <summary>
    /// Parse a value that may contain random(min,max) for float. Returns resolved float.
    /// </summary>
    internal static float ParseRandomFloat(string val)
    {
        if (string.IsNullOrEmpty(val)) return 0f;
        val = val.Trim().Replace("f", "");
        if (val.StartsWith("random(", StringComparison.OrdinalIgnoreCase) && val.EndsWith(")"))
        {
            string inner = val.Substring(7, val.Length - 8);
            var parts = inner.Split(',');
            if (parts.Length == 2 &&
                float.TryParse(parts[0].Trim(), out float min) &&
                float.TryParse(parts[1].Trim(), out float max))
            {
                return min + (float)(ConfigRng.NextDouble() * (max - min));
            }
        }
        if (float.TryParse(val, out float result)) return result;
        return 0f;
    }

    /// <summary>
    /// Parse color that may contain random(min,max) per channel.
    /// Supports: "255, 0, 128", "random(0,255), random(0,255), random(0,255)", "random"
    /// </summary>
    internal static int[] ParseRandomColor(string val)
    {
        if (string.IsNullOrEmpty(val)) return null;
        val = val.Trim();
        if (val.Equals("random", StringComparison.OrdinalIgnoreCase))
            return new[] { ConfigRng.Next(0, 256), ConfigRng.Next(0, 256), ConfigRng.Next(0, 256) };
        // Split carefully — respect parentheses in random()
        var channels = SplitColorChannels(val);
        if (channels.Length >= 3)
        {
            return new[] { ParseRandomInt(channels[0]), ParseRandomInt(channels[1]), ParseRandomInt(channels[2]) };
        }
        return ParseColor(val);
    }

    /// <summary>
    /// Split color string into channels, respecting random() parentheses.
    /// "random(0,255), random(0,128), 50" -> ["random(0,255)", "random(0,128)", "50"]
    /// </summary>
    internal static string[] SplitColorChannels(string val)
    {
        var result = new List<string>();
        int depth = 0;
        int start = 0;
        for (int i = 0; i < val.Length; i++)
        {
            if (val[i] == '(') depth++;
            else if (val[i] == ')') depth--;
            else if (val[i] == ',' && depth == 0)
            {
                result.Add(val.Substring(start, i - start).Trim());
                start = i + 1;
            }
        }
        result.Add(val.Substring(start).Trim());
        return result.ToArray();
    }

    // Parsed team configs
    internal static List<TeamConfig> ConfigTeams = new List<TeamConfig>();

    internal static void LoadConfig()
    {
        if (IsDefaultMode) return;
        try
        {
            if (!Directory.Exists(ModFolder))
                Directory.CreateDirectory(ModFolder);

            if (!File.Exists(ConfigPath))
            {
                Log.LogInfo("[Config] No config.txt found, using defaults");
                return;
            }

            var lines = File.ReadAllLines(ConfigPath);
            string currentSection = "";
            TeamConfig currentTeam = null;
            PlayerConfig currentPlayer = null;
            bool inRelics = false;

            for (int i = 0; i < lines.Length; i++)
            {
                string raw = lines[i];
                string line = raw.Replace("\t", " ").Trim();

                // Skip empty, whitespace-only, and decoration
                if (string.IsNullOrEmpty(line) || string.IsNullOrWhiteSpace(line)
                    || line.StartsWith("====") || line.StartsWith("####"))
                    continue;

                // New team block: ## TEAM N — ... ##
                if (line.StartsWith("##") && line.Contains("TEAM "))
                {
                    currentTeam = new TeamConfig();
                    ConfigTeams.Add(currentTeam);
                    currentPlayer = null;
                    currentSection = "";
                    inRelics = false;
                    continue;
                }

                // Skip comments (after team detection)
                if (line.StartsWith("#"))
                    continue;

                // Section header: --- Something ---
                if (line.StartsWith("---") && line.EndsWith("---"))
                {
                    currentSection = line.Trim('-', ' ').Trim().ToLower();
                    currentPlayer = null;
                    inRelics = false;

                    if (currentSection == "campaign settings") continue;
                    if (currentSection == "team relics") { inRelics = true; continue; }
                    if (currentSection == "team colors" || currentSection == "team uniform") continue;

                    // Player sections
                    if (currentTeam != null)
                    {
                        if (currentSection == "goalie") { currentPlayer = currentTeam.Goalie; continue; }
                        if (currentSection == "left wing") { currentPlayer = currentTeam.LW; continue; }
                        if (currentSection == "right wing") { currentPlayer = currentTeam.RW; continue; }
                        if (currentSection == "center") { currentPlayer = currentTeam.C; continue; }
                        if (currentSection == "left defense") { currentPlayer = currentTeam.LD; continue; }
                        if (currentSection == "right defense") { currentPlayer = currentTeam.RD; continue; }
                        if (currentSection == "line 2 left wing") { currentPlayer = currentTeam.L2_LW; continue; }
                        if (currentSection == "line 2 right wing") { currentPlayer = currentTeam.L2_RW; continue; }
                        if (currentSection == "line 2 center") { currentPlayer = currentTeam.L2_C; continue; }
                        if (currentSection == "line 2 left defense") { currentPlayer = currentTeam.L2_LD; continue; }
                        if (currentSection == "line 2 right defense") { currentPlayer = currentTeam.L2_RD; continue; }
                    }
                    continue;
                }

                // Skip — team detection is handled above via ## TEAM markers

                // Relic lines (no = sign, just the name)
                if (inRelics && !line.Contains("="))
                {
                    currentTeam?.Relics.Add(line);
                    continue;
                }

                // Key = Value parsing
                int eqIdx = line.IndexOf('=');
                if (eqIdx < 0) continue;
                string key = line.Substring(0, eqIdx).Trim().ToLower();
                string val = line.Substring(eqIdx + 1).Trim();

                // Campaign settings
                if (currentTeam == null)
                {
                    if (key == "act sequence")
                    {
                        var parts = val.Split(',');
                        var seq = new List<int>();
                        foreach (var p in parts)
                            if (int.TryParse(p.Trim(), out int v)) seq.Add(v);
                        if (seq.Count > 0 && seq[seq.Count - 1] == 3)
                        {
                            ActSequence = seq.ToArray();
                            Log.LogInfo($"[Config] Act Sequence: [{string.Join(", ", ActSequence)}] ({TotalMaps} maps)");
                        }
                    }
                    else if (key == "replace challenges")
                    {
                        string lv = val.ToLower().Trim();
                        if (lv == "yes" || lv == "true")
                        {
                            ReplaceChallenges = true;
                            ReplaceChallengesActs = null; // all acts
                        }
                        else if (lv == "no" || lv == "false")
                        {
                            ReplaceChallenges = false;
                            ReplaceChallengesActs = null;
                        }
                        else
                        {
                            // Per-act list: "1, 2" means only replace in acts 1 and 2
                            ReplaceChallenges = true;
                            ReplaceChallengesActs = new List<int>();
                            foreach (var p in val.Split(','))
                                if (int.TryParse(p.Trim(), out int a)) ReplaceChallengesActs.Add(a);
                            Log.LogInfo($"[Config] Replace Challenges in acts: [{string.Join(", ", ReplaceChallengesActs)}]");
                        }
                        Log.LogInfo($"[Config] Replace Challenges: {ReplaceChallenges}" + (ReplaceChallengesActs != null ? $" (acts: {string.Join(",", ReplaceChallengesActs)})" : ""));
                    }
                    else if (key == "replace soccer ball")
                    {
                        ReplaceSoccerBall = val.ToLower() == "yes" || val.ToLower() == "true";
                        Log.LogInfo($"[Config] Replace Soccer Ball: {ReplaceSoccerBall}");
                    }
                    else if (key == "replace golf ball")
                    {
                        ReplaceGolfBall = val.ToLower() == "yes" || val.ToLower() == "true";
                        Log.LogInfo($"[Config] Replace Golf Ball: {ReplaceGolfBall}");
                    }
                    continue;
                }

                // Team-level fields
                if (currentPlayer == null && !inRelics)
                {
                    if (key == "team name") currentTeam.Name = val;
                    else if (key == "city") currentTeam.City = val;
                    else if (key == "logo from") currentTeam.LogoFrom = val;
                    else if (key == "import team") currentTeam.ImportTeam = val;
                    else if (key == "abbreviation") currentTeam.Abbreviation = val;
                    else if (key == "stat scale") currentTeam.StatScale = ParseRandomFloat(val);
                    // Home Colors
                    else if (key == "jersey primary") currentTeam.JerseyPrimary = ParseRandomColor(val);
                    else if (key == "jersey secondary") currentTeam.JerseySecondary = ParseRandomColor(val);
                    else if (key == "jersey accent") currentTeam.JerseyAccent = ParseRandomColor(val);
                    // Away Colors
                    else if (key == "away primary") currentTeam.AwayPrimary = ParseRandomColor(val);
                    else if (key == "away secondary") currentTeam.AwaySecondary = ParseRandomColor(val);
                    else if (key == "away accent") currentTeam.AwayAccent = ParseRandomColor(val);
                    // Number Colors
                    else if (key == "number color home") currentTeam.NumberColorHome = ParseRandomColor(val);
                    else if (key == "number color away") currentTeam.NumberColorAway = ParseRandomColor(val);
                    // Transition Colors
                    else if (key == "transition primary") currentTeam.TransitionPrimary = ParseRandomColor(val);
                    else if (key == "transition secondary") currentTeam.TransitionSecondary = ParseRandomColor(val);
                    else if (key == "transition tertiary") currentTeam.TransitionTertiary = ParseRandomColor(val);
                    // Uniform — accepts skin names OR RGB values
                    // If RGB is given, auto-sets skin to colorable and stores the color
                    else if (key == "body") { if (TryParseUniformRGB(val, "body", ref currentTeam.Uniform.Body, ref currentTeam.JerseyPrimary)) {} }
                    else if (key == "body away") { if (TryParseUniformRGB(val, "body", ref currentTeam.Uniform.BodyAway, ref currentTeam.AwayPrimary)) {} }
                    else if (key == "bicep") { if (TryParseUniformRGB(val, "bicep", ref currentTeam.Uniform.Bicep, ref currentTeam.TeamBicepColor)) {} }
                    else if (key == "bicep away") currentTeam.Uniform.BicepAway = Plugin.ResolveSkin(val, "bicep");
                    else if (key == "gloves") { if (TryParseUniformRGB(val, "gloves", ref currentTeam.Uniform.Gloves, ref currentTeam.TeamGlovesColor)) {} }
                    else if (key == "gloves away") currentTeam.Uniform.GlovesAway = Plugin.ResolveSkin(val, "gloves");
                    else if (key == "pants") { if (TryParseUniformRGB(val, "pants", ref currentTeam.Uniform.Pants, ref currentTeam.TeamPantsColor)) {} }
                    else if (key == "pants away") currentTeam.Uniform.PantsAway = Plugin.ResolveSkin(val, "pants");
                    else if (key == "skates") { if (TryParseUniformRGB(val, "skates", ref currentTeam.Uniform.Skates, ref currentTeam.TeamSkatesColor)) {} }
                    else if (key == "skates away") currentTeam.Uniform.SkatesAway = Plugin.ResolveSkin(val, "skates");
                    else if (key == "helmet") { if (TryParseUniformRGB(val, "helmet", ref currentTeam.Uniform.Helmet, ref currentTeam.TeamHelmetColor)) {} }
                    else if (key == "helmet away") currentTeam.Uniform.HelmetAway = Plugin.ResolveSkin(val, "helmet");
                    else if (key == "stick") { if (TryParseUniformRGB(val, "stick", ref currentTeam.Uniform.Stick, ref currentTeam.TeamStickColor)) {} }
                    // Team-level equipment colors (defaults for all players)
                    else if (key == "gloves color") currentTeam.TeamGlovesColor = ParseRandomColor(val);
                    else if (key == "gloves secondary color" || key == "gloves color 2") currentTeam.TeamGlovesSecondary = ParseRandomColor(val);
                    else if (key == "gloves tertiary color" || key == "gloves color 3") currentTeam.TeamGlovesTertiary = ParseRandomColor(val);
                    else if (key == "helmet color") currentTeam.TeamHelmetColor = ParseRandomColor(val);
                    else if (key == "helmet secondary color" || key == "helmet color 2") currentTeam.TeamHelmetSecondary = ParseRandomColor(val);
                    else if (key == "helmet tertiary color" || key == "helmet color 3") currentTeam.TeamHelmetTertiary = ParseRandomColor(val);
                    else if (key == "pants color") currentTeam.TeamPantsColor = ParseRandomColor(val);
                    else if (key == "pants secondary color" || key == "pants color 2") currentTeam.TeamPantsSecondary = ParseRandomColor(val);
                    else if (key == "pants tertiary color" || key == "pants color 3") currentTeam.TeamPantsTertiary = ParseRandomColor(val);
                    else if (key == "skates color") currentTeam.TeamSkatesColor = ParseRandomColor(val);
                    else if (key == "blade color") currentTeam.TeamBladeColor = ParseRandomColor(val);
                    else if (key == "laces color") currentTeam.TeamLacesColor = ParseRandomColor(val);
                    else if (key == "bicep color") currentTeam.TeamBicepColor = ParseRandomColor(val);
                    else if (key == "socks color") currentTeam.TeamSocksColor = ParseRandomColor(val);
                    else if (key == "socks secondary color" || key == "socks color 2") currentTeam.TeamSocksSecondary = ParseRandomColor(val);
                    else if (key == "socks tertiary color" || key == "socks color 3") currentTeam.TeamSocksTertiary = ParseRandomColor(val);
                    else if (key == "stick color") currentTeam.TeamStickColor = ParseRandomColor(val);
                    else if (key == "number color") currentTeam.TeamNumberColor = ParseRandomColor(val);
                    else if (key == "number secondary color" || key == "number color 2") currentTeam.TeamNumberSecondary = ParseRandomColor(val);
                    // Gameplay
                    else if (key == "bench size") currentTeam.BenchSize = ParseRandomInt(val);
                    else if (key == "bench head") currentTeam.BenchHead = val;
                    else if (key == "team random talents") currentTeam.TeamRandomTalents = ParseRandomInt(val);
                    else if (key == "team random pool")
                    {
                        string trpLower = val.Trim().ToLower();
                        if (trpLower == "all" || trpLower == "whole pool" || trpLower == "full pool")
                        {
                            currentTeam.TeamRandomPoolAll = true;
                        }
                        else
                        {
                            currentTeam.TeamRandomPool = new List<string>();
                            foreach (var t in val.Split(','))
                            { string trimmed = t.Trim(); if (trimmed.Length > 0) currentTeam.TeamRandomPool.Add(trimmed); }
                        }
                    }
                    continue;
                }

                // Player fields (works for skaters and goalie)
                if (currentPlayer != null)
                {
                    if (key == "name") currentPlayer.Name = val;
                    else if (key == "import player") currentPlayer.ImportPlayer = val;
                    else if (key == "number") currentPlayer.Number = ParseRandomInt(val);
                    else if (key == "face") currentPlayer.Face = val;
                    else if (key == "left handed")
                    {
                        string lh = val.ToLower().Trim();
                        if (lh == "random") currentPlayer.Lefty = ConfigRng.Next(2) == 1;
                        else currentPlayer.Lefty = lh == "yes" || lh == "true";
                    }
                    else if (key == "skin color")
                    {
                        string sc = val.ToLower().Trim();
                        if (sc == "random") currentPlayer.Black = ConfigRng.Next(2) == 1;
                        else currentPlayer.Black = sc == "dark";
                    }
                    else if (key == "size")
                    {
                        if (val.Trim().Equals("random", StringComparison.OrdinalIgnoreCase))
                        {
                            string[] sizes = { "ExtraSmall", "Small", "Medium", "Big", "ExtraBig" };
                            currentPlayer.Size = sizes[ConfigRng.Next(sizes.Length)];
                        }
                        else currentPlayer.Size = val;
                    }
                    else if (key == "speed") currentPlayer.Speed = ParseRandomInt(val);
                    else if (key == "shot power") currentPlayer.ShotPower = ParseRandomInt(val);
                    else if (key == "accuracy") currentPlayer.Accuracy = ParseRandomInt(val);
                    else if (key == "checking") currentPlayer.Checking = ParseRandomInt(val);
                    else if (key == "ability") currentPlayer.Ability = val;
                    else if (key == "talents" || key == "goalie talents")
                    {
                        currentPlayer.Talents = new List<string>();
                        foreach (var t in val.Split(','))
                        {
                            string trimmed = t.Trim();
                            if (trimmed.Length > 0) currentPlayer.Talents.Add(trimmed);
                        }
                    }
                    else if (key == "random talents") currentPlayer.RandomTalentCount = ParseRandomInt(val);
                    else if (key == "random pool")
                    {
                        string rpLower = val.Trim().ToLower();
                        if (rpLower == "all" || rpLower == "whole pool" || rpLower == "full pool")
                        {
                            currentPlayer.RandomTalentPoolAll = true;
                        }
                        else
                        {
                            currentPlayer.RandomTalentPool = new List<string>();
                            foreach (var t in val.Split(','))
                            { string trimmed = t.Trim(); if (trimmed.Length > 0) currentPlayer.RandomTalentPool.Add(trimmed); }
                        }
                    }
                    // Goalie stats
                    else if (key == "skill") currentPlayer.Skill = ParseRandomInt(val);
                    else if (key == "catching") currentPlayer.Catching = ParseRandomInt(val);
                    else if (key == "glove") currentPlayer.Glove = ParseRandomInt(val);
                    else if (key == "blocker") currentPlayer.Blocker = ParseRandomInt(val);
                    else if (key == "five hole") currentPlayer.FiveHole = ParseRandomInt(val);
                    else if (key == "stand speed" || key == "standing speed") currentPlayer.StandSpeed = ParseRandomInt(val);
                    else if (key == "butterfly speed") currentPlayer.ButterflySpeed = ParseRandomInt(val);
                    else if (key == "control") currentPlayer.Control = ParseRandomInt(val);
                    else if (key == "recovery") currentPlayer.Recovery = ParseRandomInt(val);
                    else if (key == "pass power") currentPlayer.PassPower = ParseRandomInt(val);
                    else if (key == "pokecheck" || key == "poke check") currentPlayer.Pokecheck = ParseRandomInt(val);
                    else if (key == "depth") currentPlayer.Depth = ParseRandomInt(val);
                    else if (key == "pass read") currentPlayer.PassRead = ParseRandomFloat(val);
                    // Player appearance extras
                    else if (key == "size offset") currentPlayer.SizeOffset = ParseRandomFloat(val);
                    else if (key == "glasses") currentPlayer.Glasses = val;
                    // Per-player uniform overrides
                    else if (key == "stick") currentPlayer.StickOverride = Plugin.ResolveSkin(val, "stick");
                    else if (key == "helmet") currentPlayer.HelmetOverride = Plugin.ResolveSkin(val, "helmet");
                    else if (key == "helmet away") currentPlayer.HelmetAwayOverride = Plugin.ResolveSkin(val, "helmet");
                    else if (key == "body") currentPlayer.BodyOverride = Plugin.ResolveSkin(val, "body");
                    else if (key == "body away") currentPlayer.BodyAwayOverride = Plugin.ResolveSkin(val, "body");
                    else if (key == "bicep") currentPlayer.BicepOverride = Plugin.ResolveSkin(val, "bicep");
                    else if (key == "bicep away") currentPlayer.BicepAwayOverride = Plugin.ResolveSkin(val, "bicep");
                    else if (key == "gloves" && currentPlayer != null) currentPlayer.GlovesOverride = Plugin.ResolveSkin(val, "gloves");
                    else if (key == "gloves away") currentPlayer.GlovesAwayOverride = Plugin.ResolveSkin(val, "gloves");
                    else if (key == "pants" && currentPlayer != null) currentPlayer.PantsOverride = Plugin.ResolveSkin(val, "pants");
                    else if (key == "pants away") currentPlayer.PantsAwayOverride = Plugin.ResolveSkin(val, "pants");
                    else if (key == "skates" && currentPlayer != null) currentPlayer.SkatesOverride = Plugin.ResolveSkin(val, "skates");
                    else if (key == "skates away") currentPlayer.SkatesAwayOverride = Plugin.ResolveSkin(val, "skates");
                    // Per-player color overrides
                    else if (key == "jersey color") currentPlayer.JerseyColor = ParseRandomColor(val);
                    else if (key == "jersey secondary color") currentPlayer.JerseySecondaryColor = ParseRandomColor(val);
                    else if (key == "jersey accent color") currentPlayer.JerseyAccentColor = ParseRandomColor(val);
                    else if (key == "gloves color") currentPlayer.GlovesColor = ParseRandomColor(val);
                    else if (key == "gloves secondary color" || key == "gloves color 2") currentPlayer.GlovesSecondaryColor = ParseRandomColor(val);
                    else if (key == "gloves tertiary color" || key == "gloves color 3") currentPlayer.GlovesTertiaryColor = ParseRandomColor(val);
                    else if (key == "helmet color") currentPlayer.HelmetColor = ParseRandomColor(val);
                    else if (key == "helmet secondary color" || key == "helmet color 2") currentPlayer.HelmetSecondaryColor = ParseRandomColor(val);
                    else if (key == "helmet tertiary color" || key == "helmet color 3") currentPlayer.HelmetTertiaryColor = ParseRandomColor(val);
                    else if (key == "pants color") currentPlayer.PantsColor = ParseRandomColor(val);
                    else if (key == "pants secondary color" || key == "pants color 2") currentPlayer.PantsSecondaryColor = ParseRandomColor(val);
                    else if (key == "pants tertiary color" || key == "pants color 3") currentPlayer.PantsTertiaryColor = ParseRandomColor(val);
                    else if (key == "skates color") currentPlayer.SkatesColor = ParseRandomColor(val);
                    else if (key == "blade color") currentPlayer.BladeColor = ParseRandomColor(val);
                    else if (key == "laces color") currentPlayer.LacesColor = ParseRandomColor(val);
                    else if (key == "bicep color") currentPlayer.BicepColor = ParseRandomColor(val);
                    else if (key == "number color") currentPlayer.NumberColor = ParseRandomColor(val);
                    else if (key == "number secondary color" || key == "number color 2") currentPlayer.NumberSecondaryColor = ParseRandomColor(val);
                    else if (key == "socks color") currentPlayer.SocksColor = ParseRandomColor(val);
                    else if (key == "socks secondary color" || key == "socks color 2") currentPlayer.SocksSecondaryColor = ParseRandomColor(val);
                    else if (key == "socks tertiary color" || key == "socks color 3") currentPlayer.SocksTertiaryColor = ParseRandomColor(val);
                    // Goalie-specific skins
                    else if (key == "skin") currentPlayer.GoalieSkin = val;
                    else if (key == "skin away") currentPlayer.GoalieSkinAway = val;
                    else if (key == "glove skin") currentPlayer.GoalieGloveSkin = val;
                    else if (key == "glove away") currentPlayer.GoalieGloveAway = val;
                    else if (key == "blocker skin") currentPlayer.GoalieBlockerSkin = val;
                    else if (key == "blocker away") currentPlayer.GoalieBlockerAway = val;
                    else if (key == "pads skin") currentPlayer.GoaliePadsSkin = val;
                    else if (key == "pads away") currentPlayer.GoaliePadsAway = val;
                    else if (key == "stick skin") currentPlayer.GoalieStickSkin = val;
                    else if (key == "stick away") currentPlayer.GoalieStickAway = val;
                    else if (key == "helmet skin") currentPlayer.GoalieHelmetSkin = val;
                    else if (key == "logo skin") currentPlayer.GoalieLogoSkin = val;
                }
            }

            Log.LogInfo($"[Config] Loaded {ConfigTeams.Count} teams from config.txt");
            int totalGames = 0;
            foreach (int a in ActSequence)
                totalGames += a == 1 ? (ReplaceChallenges ? 5 : 4) : 3;
            Log.LogInfo($"[Config] Campaign: {TotalMaps} maps, ~{totalGames} games, {ConfigTeams.Count} teams configured");

            for (int i = 0; i < ConfigTeams.Count; i++)
            {
                var t = ConfigTeams[i];
                string boss = "";
                // Check if this game is a boss
                int gameCount = 0;
                for (int m = 0; m < ActSequence.Length; m++)
                {
                    int gamesInMap = ActSequence[m] == 1 ? (ReplaceChallenges ? 5 : 4) : 3;
                    gameCount += gamesInMap;
                    if (i == gameCount - 1) { boss = " [BOSS]"; break; }
                    if (i < gameCount) break;
                }

                // Calculate average OVR for manual teams
                string ovr = "";
                if (!t.IsImport)
                {
                    var players = new[] { t.LW, t.RW, t.C, t.LD, t.RD };
                    int totalStats = 0; int count = 0;
                    foreach (var p in players)
                    {
                        if (p != null && !string.IsNullOrEmpty(p.Name))
                        {
                            totalStats += (p.Speed + p.ShotPower + p.Accuracy + p.Checking) / 4;
                            count++;
                        }
                    }
                    if (count > 0) ovr = $" ~{totalStats / count} OVR";
                }

                if (!string.IsNullOrEmpty(t.ImportTeam))
                    Log.LogInfo($"  Game {i + 1}: IMPORT '{t.ImportTeam}'{boss}");
                else
                    Log.LogInfo($"  Game {i + 1}: '{t.Name}' ({t.City}){ovr}{boss}");

                // Validate team
                if (!t.IsImport && string.IsNullOrEmpty(t.Name))
                    Log.LogWarning($"  [WARN] Game {i + 1}: Missing Team Name!");
                if (!t.IsImport)
                {
                    ValidatePlayerConfig(i + 1, "LW", t.LW);
                    ValidatePlayerConfig(i + 1, "RW", t.RW);
                    ValidatePlayerConfig(i + 1, "C", t.C);
                    ValidatePlayerConfig(i + 1, "LD", t.LD);
                    ValidatePlayerConfig(i + 1, "RD", t.RD);
                    ValidatePlayerConfig(i + 1, "G", t.Goalie);
                }
            }

            if (ConfigTeams.Count < totalGames)
                Log.LogWarning($"[Config] Only {ConfigTeams.Count} teams but ~{totalGames} games — remaining will use hardcoded fallback");
        }
        catch (Exception ex)
        {
            Log.LogError($"[Config] Failed to load config: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private static void ValidatePlayerConfig(int gameNum, string pos, PlayerConfig pc)
    {
        if (pc == null) return;
        if (string.IsNullOrEmpty(pc.Name) && string.IsNullOrEmpty(pc.ImportPlayer))
            Log.LogWarning($"  [WARN] Game {gameNum} {pos}: No name set");
        if (pc.Speed == 0 && pc.ShotPower == 0 && pc.Accuracy == 0 && pc.Checking == 0
            && string.IsNullOrEmpty(pc.ImportPlayer) && pos != "G")
            Log.LogWarning($"  [WARN] Game {gameNum} {pos}: All stats are 0 — intentional?");
    }

    internal static void LogNextGame()
    {
        int next = GamesPlayed;
        int total = ConfigTeams.Count > 0 ? ConfigTeams.Count : 33;
        float pct = total > 0 ? (next * 100f / total) : 0;

        Log.LogInfo($"[Season] === Progress: {next}/{total} games ({pct:F0}%) | Map {ActsCompleted + 1}/{TotalMaps} ===");

        if (next < ConfigTeams.Count && ConfigTeams[next] != null)
        {
            var t = ConfigTeams[next];
            string name = t.IsImport ? $"IMPORT '{t.ImportTeam}'" : $"'{t.Name}'";
            Log.LogInfo($"[Season] Next up: Game {next + 1} — {name}");
        }
        else if (next < 33)
        {
            Log.LogInfo($"[Season] Next up: Game {next + 1} (hardcoded fallback)");
        }
        else
        {
            Log.LogInfo($"[Season] All games complete!");
        }
    }

    // Friendly name → internal path mapping for skins
    // slotHint: "body", "bicep", "gloves", "pants", "skates", "helmet", "stick" — used for "standard"
    internal static string ResolveSkin(string val, string slotHint = "body")
    {
        if (string.IsNullOrEmpty(val)) return val;
        string lower = val.Trim().ToLower();

        // Context-aware "standard" / "team colors" / "default"
        if (lower == "standard" || lower == "team colors" || lower == "default")
        {
            string hint = (slotHint ?? "body").ToLower();
            if (hint == "bicep" || hint == "bicep away") return "Body_Bicep/Customization/Customization_colors";
            if (hint == "gloves" || hint == "gloves away") return "Body_Gloves/Customization/Customization_colors";
            if (hint == "pants" || hint == "pants away") return "Body_Pants/Customization/Customization_colors";
            if (hint == "skates" || hint == "skates away") return "Body_Skates/Customization/Customization_colors";
            if (hint == "helmet" || hint == "helmet away") return "Faces/Custom/Helmet_Colors";
            // default = body
            return "Body/Customization/Customization_colors";
        }

        // Body skins
        if (lower == "tycoons") return "Body/Tycoons/Tycoons";
        if (lower == "princess") return "Body/Princess/Princess";
        if (lower == "golfers") return "Body/Golfers/Golfers";
        if (lower == "prisoners") return "Body/Prisoners/Prisoners";
        if (lower == "mountaineers") return "Body/Mountaineers/Mountaineers";
        if (lower == "mountaineers beer") return "Body/Mountaineers/Mountaineers_Beer";
        if (lower == "hockey fc") return "Body/HockeyFC/HockeyFC";
        if (lower == "figure skaters") return "Body/Figure_Skaters/Figure_Skaters";
        if (lower == "referee") return "Body/Alumni/Ref_Alumni";

        // Bicep skins (legacy explicit names still work)
        if (lower == "standard bicep") return "Body_Bicep/Customization/Customization_colors";

        // Glove skins
        if (lower == "standard gloves") return "Body_Gloves/Customization/Customization_colors";

        // Pants skins
        if (lower == "standard pants") return "Body_Pants/Customization/Customization_colors";

        // Skate skins
        if (lower == "black skates") return "Body_Skates/Black_Skates";
        if (lower == "standard skates" || lower == "colored skates")
            return "Body_Skates/Customization/Customization_colors";

        // Helmet skins
        if (lower == "team colors" || lower == "colored" || lower == "standard helmet")
            return "Faces/Custom/Helmet_Colors";
        if (lower == "cage" || lower == "face cage") return "Faces/Custom/Helmet_Face";

        // Random options
        if (lower == "random stick")
        {
            string[] sticks = { "Sticks/Black", "Sticks/Gold", "Sticks/Red", "Sticks/Purple", "Sticks/Bluegreen", "Sticks/Redgold" };
            return sticks[new System.Random().Next(sticks.Length)];
        }
        if (lower == "random body")
        {
            string[] bodies = { "Body/Customization/Customization_colors", "Body/Tycoons/Tycoons", "Body/Princess/Princess", "Body/Golfers/Golfers", "Body/Prisoners/Prisoners", "Body/Mountaineers/Mountaineers" };
            return bodies[new System.Random().Next(bodies.Length)];
        }
        if (lower == "random helmet")
        {
            string[] helmets = { "Faces/Custom/Helmet_Colors", "Faces/Custom/Helmet_Face" };
            return helmets[new System.Random().Next(helmets.Length)];
        }
        if (lower == "random skates")
        {
            string[] skates = { "Body_Skates/Black_Skates", "Body_Skates/Customization/Customization_colors" };
            return skates[new System.Random().Next(skates.Length)];
        }

        // Stick skins
        if (lower == "black") return "Sticks/Black";
        if (lower == "gold") return "Sticks/Gold";
        if (lower == "red") return "Sticks/Red";
        if (lower == "purple") return "Sticks/Purple";
        if (lower == "blue green" || lower == "bluegreen" || lower == "teal") return "Sticks/Bluegreen";
        if (lower == "red gold" || lower == "redgold") return "Sticks/Redgold";
        if (lower == "sword") return "Sticks/Sword";
        if (lower == "golf" || lower == "golf iron") return "Sticks/Golf_Iron";
        if (lower == "colored stick" || lower == "team stick")
            return "Sticks/Customization/Customization_colors";

        // Face shortcuts — just use the face name without the full path
        if (!lower.Contains("/"))
        {
            // Try matching as a face name
            string[] facePaths = {
                "Faces/Twinfalls/Wiener", "Faces/Twinfalls/Haggis", "Faces/Twinfalls/Jerky", "Faces/Twinfalls/Crockett",
                "Faces/Toronto/Mathieu", "Faces/Toronto/Jelly", "Faces/Toronto/Kilmore", "Faces/Toronto/Spark",
                "Faces/Toronto/Dord", "Faces/Toronto/Popping",
                "Faces/Chicago/Angus", "Faces/Chicago/Rory", "Faces/Chicago/Grohl", "Faces/Chicago/Chicos",
                "Faces/Chicago/Chapstick", "Faces/Chicago/Louder", "Faces/Chicago/Angus_Pixel",
                "Faces/Canadians/Captain", "Faces/Canadians/Poule", "Faces/Canadians/Gratz",
                "Faces/Midwest/Brie", "Faces/Midwest/Amber", "Faces/Midwest/Mental", "Faces/Midwest/Rochefort",
                "Faces/Moutaineers/Krupp", "Faces/Moutaineers/Wurst", "Faces/Moutaineers/Torte",
                "Faces/Moutaineers/Furter", "Faces/Moutaineers/Pianist",
                "Faces/Princess/Joan", "Faces/Princess/Clementine", "Faces/Princess/Boni",
                "Faces/Tycoons/Tycoons_Large", "Faces/Tycoons/Tycoons_Small",
                "Faces/Tycoons/Tycoons_Elite", "Faces/Tycoons/Tycoons_Lady",
                "Faces/Prisoners/Dalton", "Faces/Prisoners/Ma", "Faces/Prisoners/Averell", "Faces/Prisoners/Joe",
                "Faces/Knights/Prince", "Faces/Knights/Lancelov",
                "Faces/Cultists/Cultist", "Faces/Cultists/Jelly_Evil", "Faces/Cultists/Rory_Evil", "Faces/Cultists/Dord_Evil",
                "Faces/HockeyFC/Knudribble", "Faces/HockeyFC/Zidanejad", "Faces/HockeyFC/OHenry",
                "Faces/HockeyFC/Icekicks", "Faces/HockeyFC/Ronaldo", "Faces/HockeyFC/Messier",
                "Faces/HockeyFC/Maroondona", "Faces/HockeyFC/Backham", "Faces/HockeyFC/Ehrhoffaldo",
                "Faces/Golfers/Golfer_Lady", "Faces/Golfers/Golfer_Ramirez",
                "Faces/Golfers/Golfer_Elite", "Faces/Golfers/Golfer_Whacker", "Faces/Golfers/Golfer_Gillman",
                "Faces/Disco/Oioioi", "Faces/Referees/Gedeon", "Faces/Custom/Helmet_Face",
                "Faces/Anyteam/Nasher", "Faces/Anyteam/Chickensneeze", "Faces/Anyteam/Onepunch",
                "Faces/Anyteam/Bench_Kovalski", "Faces/Anyteam/Bench_Bench",
                "Faces/Anyteam/Bench_Brewster", "Faces/Anyteam/Bench_Kirby",
                "Faces/Anyteam/Bench_Buttface", "Faces/Anyteam/Bench_Stumple"
            };

            // "random" = special flag handled at apply time
            if (lower == "random") return "RANDOM_FACE";

            foreach (var fp in facePaths)
            {
                string faceName = fp.Substring(fp.LastIndexOf('/') + 1).ToLower();
                if (lower == faceName || lower.Replace(" ", "_") == faceName || lower.Replace(" ", "") == faceName.Replace("_", ""))
                    return fp;
            }
        }

        // Already a path — return as-is
        return val.Trim();
    }

    /// <summary>
    /// Try to parse a uniform field value as RGB. If it looks like RGB (has commas + numbers),
    /// sets the skin to colorable and stores the color. Returns true if handled as RGB.
    /// If it's a skin name, resolves normally. Either way, the field gets set.
    /// </summary>
    internal static bool TryParseUniformRGB(string val, string slot, ref string skinField, ref int[] colorField)
    {
        if (string.IsNullOrEmpty(val) || string.IsNullOrWhiteSpace(val))
        {
            skinField = "";
            return true;
        }
        string trimmed = val.Trim();
        // Check if it looks like RGB: contains comma and first part is a number or "random"
        bool looksLikeRGB = false;
        if (trimmed.Contains(","))
        {
            var firstPart = trimmed.Split(',')[0].Trim();
            if (int.TryParse(firstPart, out _) || firstPart.StartsWith("random", StringComparison.OrdinalIgnoreCase))
                looksLikeRGB = true;
        }
        if (trimmed.Equals("random", StringComparison.OrdinalIgnoreCase))
            looksLikeRGB = true;

        if (looksLikeRGB)
        {
            // Auto-set skin to colorable version
            skinField = ResolveSkin("standard", slot);
            colorField = ParseRandomColor(trimmed);
            return true;
        }
        else
        {
            // It's a skin name
            skinField = ResolveSkin(val, slot);
            return true;
        }
    }

    private static int[] ParseColor(string val)
    {
        var parts = val.Split(',');
        if (parts.Length >= 3 &&
            int.TryParse(parts[0].Trim(), out int r) &&
            int.TryParse(parts[1].Trim(), out int g) &&
            int.TryParse(parts[2].Trim(), out int b))
            return new[] { r, g, b };
        return null;
    }

    // ===== PROGRESS =====
    internal static int ActsCompleted = 0;
    internal static int GamesPlayed = 0;

    internal static void SaveProgress()
    {
        try
        {
            if (!Directory.Exists(ModFolder))
                Directory.CreateDirectory(ModFolder);
            File.WriteAllText(SavePath, $"{ActsCompleted},{GamesPlayed}");
        }
        catch { }
    }

    internal static void LoadProgress()
    {
        try
        {
            if (File.Exists(SavePath))
            {
                string text = File.ReadAllText(SavePath).Trim();
                var parts = text.Split(',');
                if (parts.Length >= 1 && int.TryParse(parts[0], out int ac))
                    ActsCompleted = ac;
                if (parts.Length >= 2 && int.TryParse(parts[1], out int gp))
                    GamesPlayed = gp;
                Log.LogInfo($"[Campaign] Loaded progress: ActsCompleted={ActsCompleted}, GamesPlayed={GamesPlayed}");
            }
        }
        catch { }
    }

    // ===== MAP PROPERTIES (config-driven) =====
    internal static bool IsRemixed => true;

    // Which game act for the current map (reads from actSequence)
    internal static int ActForMap
    {
        get
        {
            if (ActsCompleted < ActSequence.Length)
                return ActSequence[ActsCompleted];
            return 3; // fallback to act 3
        }
    }

    // Which remix round = how many times this act has appeared before this point
    internal static int RemixRound
    {
        get
        {
            int currentAct = ActForMap;
            int round = 0;
            for (int i = 0; i < ActsCompleted && i < ActSequence.Length; i++)
            {
                if (ActSequence[i] == currentAct)
                    round++;
            }
            return round;
        }
    }

    // Is this the final map?
    internal static bool IsFinalMap => ActsCompleted >= ActSequence.Length - 1;

    // Stat boost per map — disabled, we manually scale team stats now
    internal static int CurrentRemixBoost
    {
        get { return 0; }
    }

    public override void Load()
    {
        Log = base.Log;
        Log.LogInfo("=== Custom Campaign Framework v2.0 ===");
        ResolveCampaignPaths();
        LoadDefaults();
        LoadConfig();
        LoadProgress();
        if (GamesPlayed > 0)
            LogNextGame();

        // Check if PLAYER mirror match is possible
        bool hasPlayerImport = false;
        foreach (var t in ConfigTeams)
            if (t != null && t.ImportTeam != null && t.ImportTeam.Trim().Equals("PLAYER", StringComparison.OrdinalIgnoreCase))
            { hasPlayerImport = true; break; }
        if (hasPlayerImport)
            Log.LogInfo("[Config] PLAYER mirror match configured — will clone player team at runtime");

        var harmony = new Harmony("com.mods.customcampaign");
        harmony.PatchAll();

        // Manual patches for UI classes
        try
        {
            var toggleMethod = AccessTools.Method(typeof(EndRunStats), "Toggle");
            if (toggleMethod != null)
            {
                harmony.Patch(toggleMethod,
                    prefix: new HarmonyMethod(typeof(PatchEndRunStatsToggle), nameof(PatchEndRunStatsToggle.Prefix)));
                Log.LogInfo($"Patched EndRunStats.Toggle!");
            }
        }
        catch (Exception ex) { Log.LogError($"Failed EndRunStats.Toggle: {ex}"); }

        try
        {
            var onRunFinished = AccessTools.Method(typeof(RunEndHandler), "OnRunFinished");
            if (onRunFinished != null)
            {
                harmony.Patch(onRunFinished,
                    prefix: new HarmonyMethod(typeof(PatchOnRunFinished), nameof(PatchOnRunFinished.Prefix)));
                Log.LogInfo($"Patched RunEndHandler.OnRunFinished!");
            }
        }
        catch (Exception ex) { Log.LogError($"Failed RunEndHandler.OnRunFinished: {ex}"); }

        try
        {
            var showEndRun = AccessTools.Method(typeof(RunEndHandler), "ShowEndRunSequence");
            if (showEndRun != null)
            {
                harmony.Patch(showEndRun,
                    prefix: new HarmonyMethod(typeof(PatchShowEndRunSequence), nameof(PatchShowEndRunSequence.Prefix)));
                Log.LogInfo($"Patched RunEndHandler.ShowEndRunSequence!");
            }
        }
        catch (Exception ex) { Log.LogError($"Failed RunEndHandler.ShowEndRunSequence: {ex}"); }

        // Suppress GoalDenier (Bad Luck) completely
        try
        {
            var goalDenierAdd = AccessTools.Method(typeof(Rogue.Relics.Controllers.GoalDenierController), "AddEffect");
            if (goalDenierAdd != null)
            {
                harmony.Patch(goalDenierAdd,
                    prefix: new HarmonyMethod(typeof(PatchGoalDenier), nameof(PatchGoalDenier.Skip)));
                Log.LogInfo("Patched GoalDenierController.AddEffect — suppressed!");
            }
        }
        catch (Exception ex) { Log.LogError($"Failed GoalDenier patch: {ex}"); }

        try
        {
            var goalDenierInit = AccessTools.Method(typeof(Rogue.Relics.Components.GoalDenierComponent), "Initialize");
            if (goalDenierInit != null)
            {
                harmony.Patch(goalDenierInit,
                    prefix: new HarmonyMethod(typeof(PatchGoalDenier), nameof(PatchGoalDenier.Skip)));
                Log.LogInfo("Patched GoalDenierComponent.Initialize — suppressed!");
            }
        }
        catch (Exception ex) { Log.LogError($"Failed GoalDenierComponent patch: {ex}"); }

        // Replace golf ball and soccer ball with regular puck
        try
        {
            var initPuck = AccessTools.Method(typeof(PuckManager), "InitializePuck");
            if (initPuck != null)
            {
                harmony.Patch(initPuck,
                    prefix: new HarmonyMethod(typeof(PatchPuckManager), nameof(PatchPuckManager.Prefix)));
                Log.LogInfo("Patched PuckManager.InitializePuck — golf/soccer balls replaced with puck!");
            }
        }
        catch (Exception ex) { Log.LogError($"Failed PuckManager patch: {ex}"); }

        // Replace challenge nodes with elite nodes
        try
        {
            var createMapNode = AccessTools.Method(typeof(MapObject), "CreateMapNode");
            if (createMapNode != null)
            {
                harmony.Patch(createMapNode,
                    prefix: new HarmonyMethod(typeof(PatchCreateMapNode), nameof(PatchCreateMapNode.Prefix)));
                Log.LogInfo("Patched MapObject.CreateMapNode — challenge nodes become elite nodes!");
            }
        }
        catch (Exception ex) { Log.LogError($"Failed CreateMapNode patch: {ex}"); }

        Log.LogInfo("All patches applied.");
    }

    public static int WrapAct(int act)
    {
        // Use ActForMap to determine the correct game act based on progression
        return ActForMap;
    }
}

// ============================================================
// Suppress GoalDenier (Bad Luck) — skip AddEffect and Initialize
// ============================================================
public static class PatchGoalDenier
{
    public static bool Skip()
    {
        Plugin.Log.LogInfo("[GoalDenier] BLOCKED — Bad Luck suppressed");
        return false; // skip original method
    }
}

// ============================================================
// Increment GamesPlayed on match win (any match type)
// ============================================================
[HarmonyPatch(typeof(MatchMapNode), nameof(MatchMapNode.OnGameEnd))]
public static class PatchMatchGameEnd
{
    [HarmonyPrefix]
    public static void Prefix(MatchMapNode __instance, bool isWinning)
    {
        if (isWinning)
        {
            // Don't count Spartan challenge wins when challenges aren't replaced
            if (__instance is ChallengeMapNode)
            {
                if (!Plugin.ReplaceChallenges)
                {
                    Plugin.Log.LogInfo("[Campaign] Challenge match won — not counting (replaceChallenges=false)");
                    return;
                }
                // Per-act: if current act not in the replace list, don't count
                if (Plugin.ReplaceChallengesActs != null)
                {
                    int currentAct = Plugin.ActForMap;
                    bool actMatch = false;
                    foreach (int a in Plugin.ReplaceChallengesActs)
                        if (a == currentAct) { actMatch = true; break; }
                    if (!actMatch)
                    {
                        Plugin.Log.LogInfo($"[Campaign] Challenge match won — not counting (act {currentAct} not in replace list)");
                        return;
                    }
                }
            }
            Plugin.GamesPlayed++;
            Plugin.SaveProgress();
            Plugin.Log.LogInfo($"[Campaign] Match won! GamesPlayed={Plugin.GamesPlayed}");
        }
    }
}

// ============================================================
// Detect boss win — set flag so we know to redirect
// ============================================================
[HarmonyPatch(typeof(BossMapNode), nameof(BossMapNode.OnGameEnd))]
public static class PatchBossOnGameEnd
{
    [HarmonyPrefix]
    public static void Prefix(bool isWinning)
    {
        if (isWinning)
        {
            Plugin.ActsCompleted++;
            // Final boss — let the real victory screen show, don't redirect
            if (Plugin.ActsCompleted >= Plugin.TotalMaps)
            {
                Plugin.BossJustBeaten = false;
                Plugin.GamesPlayed = 0;
                Plugin.ActsCompleted = 0;
                Plugin.SaveProgress();
                Plugin.Log.LogInfo("[Campaign] FINAL BOSS WON! Victory screen, progress reset.");
            }
            else
            {
                Plugin.BossJustBeaten = true;
                Plugin.SaveProgress();
                Plugin.Log.LogInfo($"[Campaign] Boss won! ActsCompleted={Plugin.ActsCompleted}, BossJustBeaten=true");
            }
        }
    }
}

// ============================================================
// Intercept ShowEndRunSequence — if boss was beaten, change
// isVictory to false so it doesn't show the victory panel
// ============================================================
public static class PatchShowEndRunSequence
{
    public static void Prefix(RunEndHandler __instance, ref bool isVictory)
    {
        if (isVictory && Plugin.BossJustBeaten)
        {
            Plugin.Log.LogInfo("[Campaign] ShowEndRunSequence: changing isVictory to FALSE");
            isVictory = false;

            // Increment act and save
            var cs = __instance.m_CampaignState;
            if (cs?.runData != null)
            {
                int currentAct = cs.runData.CurrentAct;
                int newAct = currentAct + 1;
                cs.runData._currentAct = newAct;
                cs.runData.isRunFailed = false;
                Plugin.Log.LogInfo($"[Campaign] Act {currentAct} -> {newAct}");

                try { cs.SaveRunData(); }
                catch (Exception ex) { Plugin.Log.LogWarning($"SaveRunData: {ex.Message}"); }
            }

            __instance.m_IsVictory = false;
        }
    }
}

// ============================================================
// Replace golf ball and soccer ball with regular puck
// ============================================================
public static class PatchPuckManager
{
    public static void Prefix(PuckManager __instance)
    {
        try
        {
            if (__instance.regularPuck != null)
            {
                if (Plugin.ReplaceSoccerBall)
                    __instance.soccerBall = __instance.regularPuck;
                if (Plugin.ReplaceGolfBall)
                    __instance.golfBall = __instance.regularPuck;
            }
        }
        catch {}
    }
}

// ============================================================
// Intercept OnRunFinished — block it entirely, let the game
// show the stats screen (RunFailed path) and handle continue
// ============================================================
public static class PatchOnRunFinished
{
    public static bool Prefix()
    {
        if (Plugin.BossJustBeaten)
        {
            Plugin.Log.LogInfo("[Campaign] OnRunFinished BLOCKED (boss redirect active)");
            return false;
        }
        // Run actually ended (loss or final victory) — reset ActsCompleted only
        Plugin.ActsCompleted = 0;
        Plugin.SaveProgress();
        Plugin.Log.LogInfo("[Campaign] Run ended, ActsCompleted reset to 0 (GamesPlayed={Plugin.GamesPlayed} preserved)");
        return true;
    }
}

// ============================================================
// Intercept EndRunStats.Toggle — the "press button" on stats screen.
// If boss was beaten, go to world map instead of victory/title screen.
// ============================================================
public static class PatchEndRunStatsToggle
{
    public static bool Prefix(EndRunStats __instance)
    {
        if (!Plugin.BossJustBeaten) return true;

        Plugin.Log.LogInfo("[Campaign] EndRunStats.Toggle intercepted — going to world map!");
        Plugin.BossJustBeaten = false;

        try
        {
            // Log all scenes for debugging
            int sceneCount = SceneManager.sceneCountInBuildSettings;
            Plugin.Log.LogInfo($"[Campaign] Total scenes in build: {sceneCount}");
            for (int i = 0; i < sceneCount; i++)
            {
                string path = SceneUtility.GetScenePathByBuildIndex(i);
                Plugin.Log.LogInfo($"  Scene [{i}]: {path}");
            }

            // Try loading each scene name that might be the world map
            var lsm = __instance._endRunPanel?._loadingScreenManager;
            if (lsm != null)
            {
                // Try common scene names
                string[] candidates = { "run", "Run", "world_map", "WorldMap", "campaign", "Campaign", "roguelike", "Roguelike" };
                foreach (var name in candidates)
                {
                    try
                    {
                        Plugin.Log.LogInfo($"[Campaign] Trying LoadScene('{name}')...");
                        lsm.LoadScene(name);
                        Plugin.Log.LogInfo($"[Campaign] LoadScene('{name}') succeeded!");
                        return false;
                    }
                    catch
                    {
                        Plugin.Log.LogInfo($"[Campaign] '{name}' failed, trying next...");
                    }
                }

                // Try by build index — title is probably 0, world map might be 1 or 2
                for (int i = 0; i < sceneCount && i < 6; i++)
                {
                    try
                    {
                        string path = SceneUtility.GetScenePathByBuildIndex(i);
                        string sceneName = System.IO.Path.GetFileNameWithoutExtension(path);
                        Plugin.Log.LogInfo($"[Campaign] Trying LoadScene('{sceneName}') from index {i}...");
                        lsm.LoadScene(sceneName);
                        Plugin.Log.LogInfo($"[Campaign] LoadScene('{sceneName}') succeeded!");
                        return false;
                    }
                    catch
                    {
                        continue;
                    }
                }
            }

            Plugin.Log.LogError("[Campaign] Could not find world map scene!");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[Campaign] Toggle error: {ex}");
        }

        return true; // fallback to normal
    }
}

// ============================================================
// Map generation
// ============================================================
[HarmonyPatch(typeof(MapManager), nameof(MapManager.GenerateNewMap))]
public static class PatchGenerateNewMap
{
    [HarmonyPostfix]
    public static void Postfix(MapManager __instance)
    {
        // After map is generated, if we forced a higher act, update CurrentAct
        if (Plugin.DebugRealAct > 0 && Plugin.DebugSkipEnabled)
        {
            // Find CampaignState via the map's act or RunManager
            try
            {
                var runManager = UnityEngine.Object.FindObjectOfType<RunManager>();
                if (runManager?.CampaignState?.runData != null)
                {
                    runManager.CampaignState.runData._currentAct = Plugin.DebugRealAct;
                    Plugin.Log.LogInfo($"[DEBUG] Set _currentAct to {Plugin.DebugRealAct} after map gen");
                }
            }
            catch (Exception ex) { Plugin.Log.LogError($"[DEBUG] postfix error: {ex}"); }
        }
    }

    [HarmonyPrefix]
    public static void Prefix(ref int act)
    {
        // Debug: start on act 2 map so we only need to beat one boss to test loop
        if (Plugin.DebugSkipEnabled && act < 2)
        {
            Plugin.Log.LogInfo($"[DEBUG] GenerateNewMap: skipping act {act} -> 2");
            act = 2;
        }

        int wrapped = Plugin.WrapAct(act);
        if (act != wrapped)
        {
            Plugin.Log.LogInfo($"[Campaign] GenerateNewMap: wrapped act {act} -> {wrapped}");
            act = wrapped;
        }
        else
            Plugin.Log.LogInfo($"[Campaign] GenerateNewMap: act={act}");
            Plugin.LogNextGame();
    }
}

// ============================================================
// Act tracking — never let act reach 2 (act 3, the final act)
// When game tries to set act to 2+, wrap it back to 0
// This makes the game loop acts 1 and 2 forever
// ============================================================
[HarmonyPatch(typeof(RunData), nameof(RunData.CurrentAct), MethodType.Setter)]
public static class PatchSetCurrentAct
{
    [HarmonyPostfix]
    public static void Postfix(RunData __instance)
    {
        int act = __instance._currentAct;
        // Config-driven act sequence
        if (Plugin.BossJustBeaten)
        {
            Plugin.BossJustBeaten = false;

            // Look up the next act from the config sequence
            int targetAct;
            if (Plugin.ActsCompleted < Plugin.ActSequence.Length)
                targetAct = Plugin.ActSequence[Plugin.ActsCompleted];
            else
                targetAct = act; // past the end = let game handle (act 3 final)

            if (targetAct != act)
            {
                __instance._currentAct = targetAct;
                Plugin.Log.LogInfo($"[Campaign] Boss #{Plugin.ActsCompleted}: overriding act {act} -> {targetAct} (remixed={Plugin.IsRemixed})");
                return;
            }
            Plugin.Log.LogInfo($"[Campaign] Boss #{Plugin.ActsCompleted}: act stays {act} (remixed={Plugin.IsRemixed})");
        }

        Plugin.Log.LogInfo($"[Campaign] RunData.CurrentAct is now {act}");
    }
}

// ============================================================
// Boss/elite selection
// ============================================================
[HarmonyPatch(typeof(CampaignOpponentsConfig), nameof(CampaignOpponentsConfig.GetActBosses))]
public static class PatchGetActBosses
{
    [HarmonyPrefix]
    public static void Prefix(ref int act, CampaignOpponentsConfig __instance)
    {
        // Debug team logging removed for release

        int original = act;
        act = Plugin.WrapAct(act);
        if (original != act)
            Plugin.Log.LogInfo($"[Campaign] GetActBosses: wrapped {original} -> {act}");
    }

    private static void LogTeamRoster(TeamData team, string type)
    {
        if (team == null) return;
        var forwards = team.forwards;
        if (forwards != null)
            for (int i = 0; i < forwards.Count; i++)
            {
                var f = forwards[i];
                if (f == null) continue;
                Plugin.Log.LogInfo($"  [{type}] Forward: '{f.firstName} {f.lastName}' SP={f.shotPower} SPD={f.speed} CHK={f.checking} ACC={f.shotAccuracy} Ability={f.ability?.name}");
            }
        var g = team.goalie;
        if (g != null)
            Plugin.Log.LogInfo($"  [{type}] Goalie: '{g.firstName} {g.lastName}' skill={g.skill} catch={g.catchingSkill} glove={g.gloveSkill} blocker={g.blockerSkill} fiveHole={g.fiveHoleSkill} standSpd={g.standingSpeed} buttSpd={g.butterflySpeed} ctrl={g.controlSkill} recov={g.recoverySkill} pass={g.passPower} shot={g.shotPower} poke={g.pokecheckSkill} depth={g.depth} passRead={g.passReadSkill}");
    }
}

[HarmonyPatch(typeof(CampaignOpponentsConfig), nameof(CampaignOpponentsConfig.GetActElites))]
public static class PatchGetActElites
{
    [HarmonyPrefix]
    public static void Prefix(ref int act)
    {
        int original = act;
        act = Plugin.WrapAct(act);
        if (original != act)
            Plugin.Log.LogInfo($"[Campaign] GetActElites: wrapped {original} -> {act}");
    }
}

// ============================================================
// Replace challenge nodes with elite nodes during map creation
// (manually patched in Plugin.Load because CreateMapNode is private)
// ============================================================
public static class PatchCreateMapNode
{
    public static void Prefix(STS.Map.Node node)
    {
        if (!Plugin.ReplaceChallenges) return;
        if (node?.layerNodeType == null) return;
        if (node.layerNodeType.nodeType == NodeType.Challenge)
        {
            // Per-act filtering: if ReplaceChallengesActs is set, only replace in those acts
            if (Plugin.ReplaceChallengesActs != null)
            {
                int currentAct = Plugin.ActForMap;
                bool actMatch = false;
                foreach (int a in Plugin.ReplaceChallengesActs)
                    if (a == currentAct) { actMatch = true; break; }
                if (!actMatch)
                {
                    Plugin.Log.LogInfo($"[MapGen] Challenge node at layer {node.LayerIndex} kept (act {currentAct} not in replace list)");
                    return;
                }
            }
            Plugin.Log.LogInfo($"[MapGen] Converting Challenge node at layer {node.LayerIndex} to Elite node");
            node.layerNodeType.nodeType = NodeType.Elite;
        }
    }
}

// ============================================================
// Scale opponents
// ============================================================
[HarmonyPatch(typeof(ChallengeMapNode), nameof(ChallengeMapNode.GetOpponentStrength))]
public static class PatchGetOpponentStrength
{
    [HarmonyPostfix]
    public static void Postfix(ref int __result, int act, int floor)
    {
        if (act > 3)
            __result += (act - 3) * Plugin.ScalingPerAct;
    }
}

[HarmonyPatch(typeof(ChallengeMapNode), nameof(ChallengeMapNode.InitializeOpponentStats))]
public static class PatchInitializeOpponentStats
{
    [HarmonyPrefix]
    public static void Prefix(ref int act, int floor, ForwardData forwardData)
    {
        if (act > 3)
            Plugin.Log.LogInfo($"[Campaign] InitializeOpponentStats: act={act}, floor={floor}");
    }
}

// ============================================================
// Remix and scale boss/elite teams
// ============================================================
[HarmonyPatch(typeof(BossMapNode), nameof(BossMapNode.LaunchMatch))]
public static class PatchBossLaunchMatch
{
    [HarmonyPrefix]
    public static void Prefix(BossMapNode __instance)
    {
        try
        {
            var opponent = __instance.opponent;
            if (opponent == null) return;

            if (Plugin.IsRemixed)
            {
                RemixTeam(opponent, Plugin.CurrentRemixBoost);
                Plugin.Log.LogInfo($"[Remix] Boss '{opponent.teamName}' remixed with +{Plugin.CurrentRemixBoost} stats");
            }
        }
        catch (Exception ex) { Plugin.Log.LogError($"[Remix] BossLaunch: {ex}"); }
    }

    internal static void BoostTeam(TeamData team, int boost)
    {
        var forwards = team.forwards;
        if (forwards != null)
            for (int i = 0; i < forwards.Count; i++)
            {
                var f = forwards[i];
                if (f == null) continue;
                f.shotPower += boost; f.speed += boost;
                f.checking += boost; f.shotAccuracy += boost;
            }
        var g = team.goalie;
        if (g != null)
        {
            g.skill += boost; g.catchingSkill += boost; g.gloveSkill += boost;
            g.blockerSkill += boost; g.fiveHoleSkill += boost; g.standingSpeed += boost;
            g.butterflySpeed += boost; g.controlSkill += boost; g.recoverySkill += boost;
            g.pokecheckSkill += boost; g.depth += boost; g.passPower += boost;
            g.shotPower += boost;
        }
    }

    // Cache repositories
    internal static TalentRepository CachedTalentRepo;
    internal static RelicRepository CachedRelicRepo;

    internal static void EnsureRepos()
    {
        if (CachedTalentRepo == null)
        {
            var t = UnityEngine.Resources.FindObjectsOfTypeAll<TalentRepository>();
            CachedTalentRepo = t != null && t.Length > 0 ? t[0] : null;
        }
        if (CachedRelicRepo == null)
        {
            var r = UnityEngine.Resources.FindObjectsOfTypeAll<RelicRepository>();
            CachedRelicRepo = r != null && r.Length > 0 ? r[0] : null;
        }
    }

    // Map correctly-spelled config names to misspelled in-game names
    private static readonly Dictionary<string, string> TalentAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        { "Goalie Pass Propel", "Goalie Pass Proepl" },
        { "Goalie Pass Propell", "Goalie Pass Proepl" },
    };

    internal static Rogue.Talent FindTalent(string name)
    {
        if (CachedTalentRepo?.talents == null) return null;
        // Check aliases first
        if (TalentAliases.TryGetValue(name, out string aliased))
            name = aliased;
        for (int i = 0; i < CachedTalentRepo.talents.Count; i++)
            if (CachedTalentRepo.talents[i]?.name == name)
                return CachedTalentRepo.talents[i];
        return null;
    }

    internal static Rogue.Relic[] AllRelicCache;
    internal static Rogue.Relic FindRelic(string nameContains, int level = 1)
    {
        // Search ALL relics in memory, not just loaded repo lists
        if (AllRelicCache == null)
            AllRelicCache = UnityEngine.Resources.FindObjectsOfTypeAll<Rogue.Relic>();
        if (AllRelicCache == null) return null;

        // Try relicName (localization key) contains
        foreach (var r in AllRelicCache)
        {
            if (r == null) continue;
            string rn = r.relicName ?? "";
            if (rn.Contains(nameContains) && r.level == level)
                return r;
        }
        // Try Unity asset name (r.name) exact or contains
        foreach (var r in AllRelicCache)
        {
            if (r == null) continue;
            string assetName = r.name ?? "";
            if ((assetName.Equals(nameContains, StringComparison.OrdinalIgnoreCase) ||
                 assetName.Contains(nameContains)) && r.level == level)
                return r;
        }
        // Try class type name
        foreach (var r in AllRelicCache)
        {
            if (r == null) continue;
            string typeName = r.GetType().Name ?? "";
            if (typeName.Equals(nameContains, StringComparison.OrdinalIgnoreCase) && r.level == level)
                return r;
        }
        // Try case-insensitive contains on all name fields
        foreach (var r in AllRelicCache)
        {
            if (r == null) continue;
            string rn = (r.relicName ?? "").ToLower();
            string an = (r.name ?? "").ToLower();
            string tn = (r.GetType().Name ?? "").ToLower();
            string search = nameContains.ToLower();
            if ((rn.Contains(search) || an.Contains(search) || tn.Contains(search)) && r.level == level)
                return r;
        }
        Plugin.Log.LogWarning($"[Remix] Relic '{nameContains}' level={level} not found in {AllRelicCache.Length} relics");
        return null;
    }

    internal static void GiveTalentToAll(TeamData team, string talentName)
    {
        var talent = FindTalent(talentName);
        if (talent == null) { Plugin.Log.LogWarning($"[Remix] Talent '{talentName}' not found"); return; }
        var forwards = team.forwards;
        if (forwards == null) return;
        for (int i = 0; i < forwards.Count; i++)
        {
            var f = forwards[i];
            if (f == null) continue;
            if (f.powerups == null)
                f.powerups = new Il2CppSystem.Collections.Generic.List<Rogue.Talent>();
            f.powerups.Add(talent);
        }
        Plugin.Log.LogInfo($"[Remix] Gave all forwards '{talentName}'");
    }

    internal static void GiveRelic(TeamData team, string relicName)
    {
        int level = 1;
        if (relicName.Contains(":"))
        {
            var parts = relicName.Split(':');
            relicName = parts[0];
            int.TryParse(parts[1], out level);
        }
        var relic = FindRelic(relicName, level);
        if (relic == null) { Plugin.Log.LogWarning($"[Remix] Relic '{relicName}' level={level} not found"); return; }
        if (team.relics == null)
            team.relics = new Il2CppSystem.Collections.Generic.List<Rogue.Relic>();
        team.relics.Add(relic);
        Plugin.Log.LogInfo($"[Remix] Gave relic '{relicName}'");
    }

    internal static void SetColors(TeamColorsData colors, Color primary, Color secondary, Color tertiary)
    {
        if (colors == null) return;
        colors.jerseyScheme.primaryColor = primary;
        colors.jerseyScheme.secondaryColor = secondary;
        colors.jerseyScheme.tertiaryColor = tertiary;
        colors.pantsScheme.primaryColor = secondary;
        colors.pantsScheme.secondaryColor = primary;
        colors.helmetScheme.primaryColor = primary;
        colors.helmetScheme.secondaryColor = secondary;
        colors.glovesScheme.primaryColor = secondary;
        colors.glovesScheme.secondaryColor = primary;
        colors.socksScheme.primaryColor = primary;
        colors.skatesScheme.primaryColor = secondary;
    }

    internal static void RemixTeam(TeamData team, int boost)
    {
        if (Plugin.IsDefaultMode) return; // Default mode = no team modifications
        EnsureRepos();
        ResetClearedPlayers();
        if (boost > 0) BoostTeam(team, boost);

        string origName = team.teamName ?? "";

        // Dispatch by game number — config teams first, then hardcoded fallback
        int gameNum = Plugin.GamesPlayed;
        Plugin.Log.LogInfo($"[Remix] Game #{gameNum + 1} (base team '{origName}')");

        bool configApplied = false;
        if (gameNum < Plugin.ConfigTeams.Count && Plugin.ConfigTeams[gameNum] != null)
        {
            try
            {
                ApplyTeamFromConfig(team, Plugin.ConfigTeams[gameNum]);
                configApplied = true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[Config] Failed to apply team {gameNum + 1}: {ex.Message}");
                Plugin.Log.LogWarning("[Config] Falling back to hardcoded team");
            }
        }
        if (!configApplied)
        {
            // Hardcoded fallback (original 32 NHL teams)
            switch (gameNum)
            {
                case 0:  RemixGreasyLettuce(team, 0); break;
                case 1:  RemixTeamCanada(team, 0); break;
                case 2:  RemixCrusaders(team, 0); break;
                case 3:  RemixPrisoners(team, 1); break;
                case 4:  RemixShootingStars(team, 0); break;
                case 5:  RemixGreasyLettuce(team, 2); break;
                case 6:  RemixTopCheese(team, 0); break;
                case 7:  RemixCalaveras(team, 2); break;
                case 8:  RemixMeatballs(team, 0); break;
                case 9:  RemixTopCheese(team, 1); break;
                case 10: RemixMountaineers(team, 1); break;
                case 11: RemixPrisoners(team, 2); break;
                case 12: RemixPrincess(team, 0); break;
                case 13: RemixShootingStars(team, 1); break;
                case 14: RemixHockeyFC(team, 1); break;
                case 15: RemixGreasyLettuce(team, 1); break;
                case 16: RemixOfficials(team, 0); break;
                case 17: RemixTopCheese(team, 2); break;
                case 18: RemixMeatballs(team, 2); break;
                case 19: RemixCalaveras(team, 0); break;
                case 20: RemixTycoons(team, 0); break;
                case 21: RemixHockeyFC(team, 0); break;
                case 22: RemixCalaveras(team, 1); break;
                case 23: RemixCrusaders(team, 1); break;
                case 24: RemixMeatballs(team, 1); break;
                case 25: RemixMountaineers(team, 0); break;
                case 26: RemixOfficials(team, 1); break;
                case 27: RemixOfficials(team, 2); break;
                case 28: RemixPrisoners(team, 0); break;
                case 29: RemixCupCultists(team, 0); break;
                case 30: RemixCupCultists(team, 1); break;
                case 31: RemixGolfers(team, 0); break;
                case 32: RemixGolfers(team, 0); break;
                default: RemixGeneric(team); break;
            }
        }

        // If this team landed on the Tycoons slot (10 forwards, 2 lines), set up line 2
        if (origName.Contains("Tycoon") && team.forwards != null && team.forwards.Count >= 10)
        {
            Plugin.Log.LogInfo($"[Remix] Tycoons slot detected — setting up line 2 for game #{gameNum + 1}");
            var fl = team.forwards;

            // Copy jerseys/skins from line 1 to line 2
            for (int li2 = 5; li2 < fl.Count && li2 <= 9; li2++)
            {
                if (fl[li2] == null || fl[li2 - 5] == null) continue;
                var src = fl[li2 - 5];
                fl[li2].headSkin = src.headSkin;
                fl[li2].numberSkin = src.numberSkin;
                fl[li2].logoSkin = src.logoSkin;
                fl[li2].helmetSkin = src.helmetSkin;
                fl[li2].helmetAwaySkin = src.helmetAwaySkin;
                fl[li2].stickSkin = src.stickSkin;
                fl[li2].bodySkin = src.bodySkin;
                fl[li2].bicepSkin = src.bicepSkin;
                fl[li2].gloveSkin = src.gloveSkin;
                fl[li2].skateSkin = src.skateSkin;
                fl[li2].pantsSkin = src.pantsSkin;
                fl[li2].bodyAwaySkin = src.bodyAwaySkin;
                fl[li2].colorSchemes = src.colorSchemes;
            }

            // Line 2 rosters for Act 3 teams
            if (gameNum == 30) // Carolina Hurricanes
            {
                if (fl.Count > 5 && fl[5] != null) { fl[5].firstName = "Jack"; fl[5].lastName = "Roslovic"; fl[5].speed = 130; fl[5].shotPower = 126; fl[5].shotAccuracy = 128; fl[5].checking = 110; fl[5].skaterSize = Data.SkaterSize.Medium; GiveTalentToPlayer(fl[5], "Cherry Picker"); GiveTalentToPlayer(fl[5], "Speed Transfer"); }
                if (fl.Count > 6 && fl[6] != null) { fl[6].firstName = "Jordan"; fl[6].lastName = "Martinook"; fl[6].speed = 126; fl[6].shotPower = 118; fl[6].shotAccuracy = 116; fl[6].checking = 124; fl[6].skaterSize = Data.SkaterSize.Big; GiveTalentToPlayer(fl[6], "Enraged"); GiveTalentToPlayer(fl[6], "Porcelain Hammer"); }
                if (fl.Count > 7 && fl[7] != null) { fl[7].firstName = "Jesperi"; fl[7].lastName = "Kotkaniemi"; fl[7].speed = 124; fl[7].shotPower = 122; fl[7].shotAccuracy = 120; fl[7].checking = 116; fl[7].skaterSize = Data.SkaterSize.Big; GiveTalentToPlayer(fl[7], "Flawless Feeder"); GiveTalentToPlayer(fl[7], "Power Transfer"); }
                if (fl.Count > 8 && fl[8] != null) { fl[8].firstName = "Sean"; fl[8].lastName = "Walker"; fl[8].speed = 128; fl[8].shotPower = 118; fl[8].shotAccuracy = 120; fl[8].checking = 118; fl[8].skaterSize = Data.SkaterSize.Medium; GiveTalentToPlayer(fl[8], "Blue Line Boost"); GiveTalentToPlayer(fl[8], "Board Bumper"); }
                if (fl.Count > 9 && fl[9] != null) { fl[9].firstName = "Shayne"; fl[9].lastName = "Gostisbehere"; fl[9].speed = 122; fl[9].shotPower = 126; fl[9].shotAccuracy = 124; fl[9].checking = 108; fl[9].skaterSize = Data.SkaterSize.Medium; GiveTalentToPlayer(fl[9], "Point Sniper"); GiveTalentToPlayer(fl[9], "Slapshot Slowmo"); }
                Plugin.Log.LogInfo("[Remix] Carolina Hurricanes line 2 applied");
            }
            else if (gameNum == 31) // Colorado Avalanche
            {
                if (fl.Count > 5 && fl[5] != null) { fl[5].firstName = "Artturi"; fl[5].lastName = "Lehkonen"; fl[5].speed = 200; fl[5].shotPower = 196; fl[5].shotAccuracy = 198; fl[5].checking = 190; fl[5].skaterSize = Data.SkaterSize.Medium; GiveTalentToPlayer(fl[5], "Cherry Picker"); GiveTalentToPlayer(fl[5], "Puck Rocket"); }
                if (fl.Count > 6 && fl[6] != null) { fl[6].firstName = "Valeri"; fl[6].lastName = "Nichushkin"; fl[6].speed = 196; fl[6].shotPower = 206; fl[6].shotAccuracy = 200; fl[6].checking = 210; fl[6].skaterSize = Data.SkaterSize.ExtraBig; GiveTalentToPlayer(fl[6], "Enraged"); GiveTalentToPlayer(fl[6], "Onepunch"); }
                if (fl.Count > 7 && fl[7] != null) { fl[7].firstName = "Casey"; fl[7].lastName = "Mittelstadt"; fl[7].speed = 198; fl[7].shotPower = 200; fl[7].shotAccuracy = 202; fl[7].checking = 194; fl[7].skaterSize = Data.SkaterSize.Medium; GiveTalentToPlayer(fl[7], "Flawless Feeder (Level 2)"); GiveTalentToPlayer(fl[7], "Power Transfer"); }
                if (fl.Count > 8 && fl[8] != null) { fl[8].firstName = "Samuel"; fl[8].lastName = "Girard"; fl[8].speed = 206; fl[8].shotPower = 192; fl[8].shotAccuracy = 196; fl[8].checking = 186; fl[8].skaterSize = Data.SkaterSize.Small; GiveTalentToPlayer(fl[8], "Blue Line Boost"); GiveTalentToPlayer(fl[8], "Sonic Interception"); }
                if (fl.Count > 9 && fl[9] != null) { fl[9].firstName = "Josh"; fl[9].lastName = "Manson"; fl[9].speed = 192; fl[9].shotPower = 198; fl[9].shotAccuracy = 194; fl[9].checking = 208; fl[9].skaterSize = Data.SkaterSize.ExtraBig; GiveTalentToPlayer(fl[9], "Point Sniper"); GiveTalentToPlayer(fl[9], "Spiked Armor"); }
                Plugin.Log.LogInfo("[Remix] Colorado Avalanche line 2 applied");
            }
            else
            {
                // Fallback: generic line 2 — copy line 1 stats at 90%
                for (int li2 = 5; li2 < fl.Count && li2 <= 9; li2++)
                {
                    if (fl[li2] == null || fl[li2 - 5] == null) continue;
                    var src2 = fl[li2 - 5];
                    fl[li2].firstName = src2.firstName; fl[li2].lastName = src2.lastName + " Jr";
                    fl[li2].speed = (int)(src2.speed * 0.9f);
                    fl[li2].shotPower = (int)(src2.shotPower * 0.9f);
                    fl[li2].shotAccuracy = (int)(src2.shotAccuracy * 0.9f);
                    fl[li2].checking = (int)(src2.checking * 0.9f);
                    fl[li2].skaterSize = src2.skaterSize;
                }
                Plugin.Log.LogInfo("[Remix] Generic line 2 applied (90% of line 1)");
            }

            // Update Lineup entries
            if (team.lines != null)
            {
                for (int li = 0; li < team.lines.Count; li++)
                {
                    var lineup = team.lines[li];
                    if (lineup == null) continue;
                    int offset = li * 5;
                    if (fl.Count > offset + 0 && fl[offset + 0] != null) lineup.leftWinger = fl[offset + 0].id;
                    if (fl.Count > offset + 1 && fl[offset + 1] != null) lineup.rightWinger = fl[offset + 1].id;
                    if (fl.Count > offset + 2 && fl[offset + 2] != null) lineup.center = fl[offset + 2].id;
                    if (fl.Count > offset + 3 && fl[offset + 3] != null) lineup.leftDefensemen = fl[offset + 3].id;
                    if (fl.Count > offset + 4 && fl[offset + 4] != null) lineup.rightDefensemen = fl[offset + 4].id;
                    Plugin.Log.LogInfo($"  [Lineup] Line {li+1}: LW={lineup.leftWinger} RW={lineup.rightWinger} C={lineup.center} LD={lineup.leftDefensemen} RD={lineup.rightDefensemen}");
                }
            }
        }

        // Log the remixed roster
        var forwards = team.forwards;
        if (forwards != null)
            for (int i = 0; i < forwards.Count; i++)
            {
                var f = forwards[i];
                if (f == null) continue;
                Plugin.Log.LogInfo($"  [Remix] '{f.firstName} {f.lastName}' SP={f.shotPower} SPD={f.speed} CHK={f.checking} ACC={f.shotAccuracy}");
            }
    }

    // Cache all teams for logo swapping
    internal static TeamData[] AllTeamCache;
    internal static TeamData FindTeamByName(string name)
    {
        if (AllTeamCache == null)
            AllTeamCache = UnityEngine.Resources.FindObjectsOfTypeAll<TeamData>();
        if (string.IsNullOrEmpty(name)) return null;
        string search = name.Trim();
        // Exact match first
        foreach (var t in AllTeamCache)
            if (t != null && t.teamName != null && t.teamName.Trim() == search) return t;
        // Case-insensitive match
        foreach (var t in AllTeamCache)
            if (t != null && t.teamName != null && t.teamName.Trim().Equals(search, StringComparison.OrdinalIgnoreCase)) return t;
        return null;
    }

    // Helper: Full NHL team swap — logo, colors, players from in-game team, then rename
    // Public wrapper for access from other patch classes
    internal static void SwapToNHLTeamPublic(TeamData team, string nhlTeamName, string displayName, string city,
        string[] playerNames, string goalieName, string[] talents, string[] relics)
    {
        SwapToNHLTeam(team, nhlTeamName, displayName, city, playerNames, goalieName, talents, relics);
    }

    private static void SwapToNHLTeam(TeamData team, string nhlTeamName, string displayName, string city,
        string[] playerNames, string goalieName, string[] talents, string[] relics)
    {
        team.teamName = displayName;
        team.city = city;
        // Disable soccer mode (Hockey FC slot)
        try
        {
            var prop = team.GetType().GetProperty("IsSoccerTeam");
            if (prop != null) prop.SetValue(team, false);
            var field = team.GetType().GetField("isSoccer");
            if (field != null) field.SetValue(team, false);
        }
        catch {}
        Plugin.Log.LogInfo($"[Remix] === {displayName} ===");

        var nhlTeam = FindTeamByName(nhlTeamName);
        if (nhlTeam != null)
        {
            team.logo = nhlTeam.logo;
            team.alternateBigLogo = nhlTeam.alternateBigLogo;
            if (nhlTeam.homeColors != null) team.homeColors.CopyInPlace(nhlTeam.homeColors);
            if (nhlTeam.awayColors != null) team.awayColors.CopyInPlace(nhlTeam.awayColors);
            team.primaryColorPlayer = nhlTeam.primaryColorPlayer;
            team.secondaryColorPlayer = nhlTeam.secondaryColorPlayer;
            // Copy nickname (abbreviation shown in-game, e.g. "VAN", "TOR")
            if (nhlTeam.nickname != null) team.nickname = nhlTeam.nickname;

            // Copy players
            var srcFwd = nhlTeam.forwards;
            var dstFwd = team.forwards;
            if (srcFwd != null && dstFwd != null)
            {
                int count = Math.Min(srcFwd.Count, dstFwd.Count);
                for (int i = 0; i < count; i++)
                    if (srcFwd[i] != null) dstFwd[i] = srcFwd[i];
            }
            if (nhlTeam.goalie != null && team.goalie != null)
                CopyGoalieData(nhlTeam.goalie, team.goalie);
            // Clear soccer player flag on all forwards
            if (team.forwards != null)
                for (int i = 0; i < team.forwards.Count; i++)
                {
                    try
                    {
                        var fwd = team.forwards[i];
                        if (fwd == null) continue;
                        var fp = fwd.GetType().GetField("isSoccerPlayer");
                        if (fp != null) fp.SetValue(fwd, false);
                    }
                    catch {}
                }
            Plugin.Log.LogInfo($"[Remix] Copied {nhlTeamName} players/logo/colors (+ bench)");
        }
        else
            Plugin.Log.LogWarning($"[Remix] {nhlTeamName} not found!");

        // NUKE all existing talents, abilities, and relics on the team
        var fwdsClear = team.forwards;
        if (fwdsClear != null)
        {
            for (int i = 0; i < fwdsClear.Count; i++)
            {
                var p = fwdsClear[i];
                if (p == null) continue;
                // Try every possible way to clear
                try { if (p.powerups != null) { for (int j = p.powerups.Count - 1; j >= 0; j--) p.powerups.RemoveAt(j); } } catch {}
                try { p.powerups = new Il2CppSystem.Collections.Generic.List<Rogue.Talent>(); } catch {}
                try { p.ability = null; } catch {}
                Plugin.Log.LogInfo($"  [NUKE] {p.firstName} {p.lastName}: talents={p.powerups?.Count ?? -1} ability={p.ability?.name ?? "null"}");
            }
        }
        if (team.goalie != null)
        {
            var gl = team.goalie;
            try { if (gl.powerups != null) { for (int j = gl.powerups.Count - 1; j >= 0; j--) gl.powerups.RemoveAt(j); } } catch {}
            try { gl.powerups = new Il2CppSystem.Collections.Generic.List<Rogue.Talent>(); } catch {}
            Plugin.Log.LogInfo($"  [NUKE] Goalie {gl.firstName} {gl.lastName}: talents={gl.powerups?.Count ?? -1}");
        }

        // Rename players: "First Last" format, split on space
        var forwards = team.forwards;
        if (forwards != null && playerNames != null)
        {
            for (int i = 0; i < Math.Min(forwards.Count, playerNames.Length); i++)
            {
                if (forwards[i] == null || playerNames[i] == null) continue;
                var parts = playerNames[i].Split(' ', 2);
                forwards[i].firstName = parts[0];
                forwards[i].lastName = parts.Length > 1 ? parts[1] : "";
            }
        }
        if (team.goalie != null && goalieName != null)
        {
            var parts = goalieName.Split(' ', 2);
            team.goalie.firstName = parts[0];
            team.goalie.lastName = parts.Length > 1 ? parts[1] : "";
        }

        // Per-player talents
        if (forwards != null && talents != null)
        {
            for (int i = 0; i < Math.Min(forwards.Count, talents.Length); i++)
                if (talents[i] != null) GiveTalentToPlayer(forwards[i], talents[i]);
        }

        // Wipe existing relics item by item then add ours
        if (team.relics != null)
        {
            int relicsBefore = team.relics.Count;
            for (int i = team.relics.Count - 1; i >= 0; i--)
            {
                try { team.relics.RemoveAt(i); } catch {}
            }
            try { team.relics.Clear(); } catch {}
            Plugin.Log.LogInfo($"  [Clear] Relics wiped ({relicsBefore} -> {team.relics.Count})");
        }
        else
        {
            team.relics = new Il2CppSystem.Collections.Generic.List<Rogue.Relic>();
        }
        if (relics != null)
            foreach (var r in relics) GiveRelic(team, r);
    }

    // ===== ABILITY HELPERS =====

    internal static Rogue.Ability FindAbility(string name)
    {
        var repos = UnityEngine.Resources.FindObjectsOfTypeAll<AbilityRepository>();
        var repo = repos != null && repos.Length > 0 ? repos[0] : null;
        if (repo?.abilities == null) return null;
        for (int i = 0; i < repo.abilities.Count; i++)
        {
            var a = repo.abilities[i];
            if (a != null && (a.name == name || a.id == name))
                return a;
        }
        for (int i = 0; i < repo.abilities.Count; i++)
        {
            var a = repo.abilities[i];
            if (a != null && a.name != null && a.name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return a;
        }
        return null;
    }

    private static void SetPlayerAbility(ForwardData f, string abilityName)
    {
        if (f == null) return;
        var ability = FindAbility(abilityName);
        if (ability == null) { Plugin.Log.LogWarning($"[Remix] Ability '{abilityName}' not found for {f.firstName}"); return; }
        f.ability = ability;
        Plugin.Log.LogInfo($"  [Remix] {f.firstName} {f.lastName} ability = '{abilityName}'");
    }

    // ===== GREASY LETTUCE REMIX =====

    private static HashSet<string> _clearedPlayers = new HashSet<string>();

    internal static void ResetClearedPlayers() { _clearedPlayers.Clear(); }

    private static void WipeList(Il2CppSystem.Collections.Generic.List<Rogue.Talent> list)
    {
        if (list == null) return;
        // Remove every item individually from the existing native list
        for (int i = list.Count - 1; i >= 0; i--)
        {
            try { list.RemoveAt(i); } catch {}
        }
        // Also try Clear as backup
        try { list.Clear(); } catch {}
    }

    internal static void GiveGoalieTalent(GoaltenderData g, string talentName)
    {
        if (g == null) return;
        string key = "G_" + (g.firstName ?? "") + (g.lastName ?? "");
        if (!_clearedPlayers.Contains(key))
        {
            if (g.powerups != null)
            {
                int before = g.powerups.Count;
                WipeList(g.powerups);
                Plugin.Log.LogInfo($"  [Clear] Goalie {g.firstName} {g.lastName} talents wiped ({before} -> {g.powerups.Count})");
            }
            else
            {
                g.powerups = new Il2CppSystem.Collections.Generic.List<Rogue.Talent>();
                Plugin.Log.LogInfo($"  [Clear] Goalie {g.firstName} {g.lastName} talents list created");
            }
            try { g.ResetTalentCache(); } catch {}
            _clearedPlayers.Add(key);
        }
        var talent = FindTalent(talentName);
        if (talent == null) { Plugin.Log.LogWarning($"[Remix] Goalie talent '{talentName}' not found"); return; }
        g.powerups.Add(talent);
        try { g.ResetTalentCache(); } catch {}
        Plugin.Log.LogInfo($"  [Remix] Goalie {g.firstName} {g.lastName} got '{talentName}'");
    }

    internal static void GiveTalentToPlayer(ForwardData f, string talentName)
    {
        if (f == null) return;
        string key = "F_" + (f.firstName ?? "") + (f.lastName ?? "");
        if (!_clearedPlayers.Contains(key))
        {
            if (f.powerups != null)
            {
                int before = f.powerups.Count;
                WipeList(f.powerups);
                Plugin.Log.LogInfo($"  [Clear] {f.firstName} {f.lastName} talents wiped ({before} -> {f.powerups.Count})");
            }
            else
            {
                f.powerups = new Il2CppSystem.Collections.Generic.List<Rogue.Talent>();
                Plugin.Log.LogInfo($"  [Clear] {f.firstName} {f.lastName} talents list created");
            }
            f.ability = null;
            // Reset the talent cache so the game re-reads from powerups list
            try { f.ResetTalentCache(); Plugin.Log.LogInfo($"  [Clear] {f.firstName} {f.lastName} talent cache reset"); } catch {}
            _clearedPlayers.Add(key);
        }
        var talent = FindTalent(talentName);
        if (talent == null) { Plugin.Log.LogWarning($"[Remix] Talent '{talentName}' not found for {f.firstName}"); return; }
        f.powerups.Add(talent);
        // Reset cache again after adding so the game picks up the new talent
        try { f.ResetTalentCache(); } catch {}
        Plugin.Log.LogInfo($"  [Remix] {f.firstName} {f.lastName} got '{talentName}'");
    }

    // ========== CONFIG-DRIVEN TEAM APPLICATION ==========

    private static void ApplyTeamFromConfig(TeamData team, TeamConfig cfg)
    {
        Plugin.Log.LogInfo($"[Config] Applying team: '{cfg.Name}' (import={cfg.IsImport})");

        if (cfg.IsImport)
        {
            // Special: "RANDOM" picks a random in-game team
            if (cfg.ImportTeam.Trim().Equals("RANDOM", StringComparison.OrdinalIgnoreCase))
            {
                if (AllTeamCache == null)
                    AllTeamCache = UnityEngine.Resources.FindObjectsOfTypeAll<TeamData>();
                if (AllTeamCache != null && AllTeamCache.Length > 0)
                {
                    // Filter to real teams (has a name and forwards)
                    var validTeams = new List<TeamData>();
                    foreach (var t in AllTeamCache)
                        if (t != null && !string.IsNullOrEmpty(t.teamName) && t.forwards != null && t.forwards.Count >= 5)
                            validTeams.Add(t);
                    if (validTeams.Count > 0)
                    {
                        var picked = validTeams[Plugin.ConfigRng.Next(validTeams.Count)];
                        cfg.ImportTeam = picked.teamName;
                        Plugin.Log.LogInfo($"[Config] RANDOM team import picked: '{picked.teamName}'");
                    }
                }
            }

            // Special: "PLAYER" imports the player's current team (mirror match with away colors)
            if (cfg.ImportTeam.Trim().Equals("PLAYER", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    // Find the player's team via TeamSelection
                    var teamSels = UnityEngine.Resources.FindObjectsOfTypeAll<TeamSelection>();
                    Plugin.Log.LogInfo($"[Config] PLAYER: Found {teamSels?.Length ?? 0} TeamSelection objects");
                    TeamData playerTeam = null;
                    if (teamSels != null)
                    {
                        for (int tsi = 0; tsi < teamSels.Length; tsi++)
                        {
                            var ts = teamSels[tsi];
                            if (ts == null) continue;
                            Plugin.Log.LogInfo($"[Config] PLAYER: TeamSelection[{tsi}] home='{ts.homeTeam?.teamName ?? "null"}' visitor='{ts.visitorTeam?.teamName ?? "null"}'");
                            if (ts.homeTeam != null && playerTeam == null)
                                playerTeam = ts.homeTeam;
                        }
                    }
                    if (playerTeam != null)
                    {
                        Plugin.Log.LogInfo($"[Config] PLAYER MIRROR: Cloning player team '{playerTeam.teamName}'");
                        // Copy logo
                        team.logo = playerTeam.logo;
                        team.alternateBigLogo = playerTeam.alternateBigLogo;
                        team.nickname = playerTeam.nickname;
                        // Use away colors as home (so they wear away jerseys)
                        if (playerTeam.awayColors != null) team.homeColors.CopyInPlace(playerTeam.awayColors);
                        if (playerTeam.homeColors != null) team.awayColors.CopyInPlace(playerTeam.homeColors);
                        team.primaryColorPlayer = playerTeam.secondaryColorPlayer;
                        team.secondaryColorPlayer = playerTeam.primaryColorPlayer;
                        // Copy players field by field
                        var pSrcFwd = playerTeam.forwards;
                        var pDstFwd = team.forwards;
                        if (pSrcFwd != null && pDstFwd != null)
                        {
                            int cnt = Math.Min(pSrcFwd.Count, pDstFwd.Count);
                            for (int pi = 0; pi < cnt; pi++)
                                if (pSrcFwd[pi] != null && pDstFwd[pi] != null)
                                    CopyPlayerData(pSrcFwd[pi], pDstFwd[pi]);
                        }
                        if (playerTeam.goalie != null && team.goalie != null)
                            CopyGoalieData(playerTeam.goalie, team.goalie);
                        // Copy relics
                        NukeRelics(team);
                        if (playerTeam.relics != null)
                            for (int ri = 0; ri < playerTeam.relics.Count; ri++)
                                if (playerTeam.relics[ri] != null)
                                    team.relics.Add(playerTeam.relics[ri]);
                        // Set display name
                        team.teamName = !string.IsNullOrEmpty(cfg.Name) ? cfg.Name : playerTeam.teamName + " (Mirror)";
                        team.city = !string.IsNullOrEmpty(cfg.City) ? cfg.City : playerTeam.city;
                        Plugin.Log.LogInfo($"[Config] Mirror match: '{team.teamName}' with away colors");
                        return;
                    }
                    Plugin.Log.LogWarning("[Config] PLAYER team not found — falling back");
                }
                catch (Exception ex) { Plugin.Log.LogError($"[Config] PLAYER mirror error: {ex.Message}"); }
            }

            // Import mode — find team by name and copy everything
            var srcTeam = FindTeamByName(cfg.ImportTeam);
            if (srcTeam == null)
            {
                Plugin.Log.LogWarning($"[Config] Import team '{cfg.ImportTeam}' not found! Skipping.");
                return;
            }
            // Copy logo, colors, nickname
            team.logo = srcTeam.logo;
            team.alternateBigLogo = srcTeam.alternateBigLogo;
            if (srcTeam.homeColors != null) team.homeColors.CopyInPlace(srcTeam.homeColors);
            if (srcTeam.awayColors != null) team.awayColors.CopyInPlace(srcTeam.awayColors);
            team.primaryColorPlayer = srcTeam.primaryColorPlayer;
            team.secondaryColorPlayer = srcTeam.secondaryColorPlayer;
            if (srcTeam.nickname != null) team.nickname = srcTeam.nickname;

            // Set name
            team.teamName = !string.IsNullOrEmpty(cfg.Name) ? cfg.Name : srcTeam.teamName;
            team.city = !string.IsNullOrEmpty(cfg.City) ? cfg.City : srcTeam.city;

            // Copy player look + stats from source (NOT references — copy fields)
            var srcFwd = srcTeam.forwards;
            var dstFwd = team.forwards;
            if (srcFwd != null && dstFwd != null)
            {
                int count = Math.Min(srcFwd.Count, dstFwd.Count);
                for (int i = 0; i < count; i++)
                {
                    if (srcFwd[i] == null || dstFwd[i] == null) continue;
                    CopyPlayerData(srcFwd[i], dstFwd[i]);
                }
            }
            // Copy goalie
            if (srcTeam.goalie != null && team.goalie != null)
                CopyGoalieData(srcTeam.goalie, team.goalie);

            // Copy relics
            NukeRelics(team);
            if (srcTeam.relics != null)
                for (int i = 0; i < srcTeam.relics.Count; i++)
                    if (srcTeam.relics[i] != null)
                        GiveRelic(team, srcTeam.relics[i].name);

            // Apply stat scale
            if (cfg.StatScale != 1.0f)
            {
                var sf = team.forwards;
                if (sf != null)
                    for (int i = 0; i < sf.Count; i++)
                    {
                        if (sf[i] == null) continue;
                        sf[i].speed = (int)(sf[i].speed * cfg.StatScale);
                        sf[i].shotPower = (int)(sf[i].shotPower * cfg.StatScale);
                        sf[i].shotAccuracy = (int)(sf[i].shotAccuracy * cfg.StatScale);
                        sf[i].checking = (int)(sf[i].checking * cfg.StatScale);
                    }
                if (team.goalie != null)
                {
                    var gg = team.goalie;
                    gg.skill = (int)(gg.skill * cfg.StatScale);
                    gg.catchingSkill = (int)(gg.catchingSkill * cfg.StatScale);
                    gg.gloveSkill = (int)(gg.gloveSkill * cfg.StatScale);
                    gg.blockerSkill = (int)(gg.blockerSkill * cfg.StatScale);
                    gg.fiveHoleSkill = (int)(gg.fiveHoleSkill * cfg.StatScale);
                    gg.standingSpeed = (int)(gg.standingSpeed * cfg.StatScale);
                    gg.butterflySpeed = (int)(gg.butterflySpeed * cfg.StatScale);
                    gg.controlSkill = (int)(gg.controlSkill * cfg.StatScale);
                    gg.recoverySkill = (int)(gg.recoverySkill * cfg.StatScale);
                    gg.pokecheckSkill = (int)(gg.pokecheckSkill * cfg.StatScale);
                    gg.depth = (int)(gg.depth * cfg.StatScale);
                }
                Plugin.Log.LogInfo($"[Config] Stats scaled by {cfg.StatScale}x");
            }

            Plugin.Log.LogInfo($"[Config] Imported '{cfg.ImportTeam}' as '{team.teamName}'");
            return;
        }

        // Manual mode — apply from config fields
        // Clear soccer flags
        try
        {
            var prop = team.GetType().GetProperty("IsSoccerTeam");
            if (prop != null) prop.SetValue(team, false);
            var field = team.GetType().GetField("isSoccer");
            if (field != null) field.SetValue(team, false);
        }
        catch {}

        // Logo from another team (BEFORE renaming — so we don't find ourselves)
        if (!string.IsNullOrEmpty(cfg.LogoFrom))
        {
            // Random logo: pick a random team's logo
            if (cfg.LogoFrom.Trim().Equals("RANDOM", StringComparison.OrdinalIgnoreCase))
            {
                if (AllTeamCache == null)
                    AllTeamCache = UnityEngine.Resources.FindObjectsOfTypeAll<TeamData>();
                if (AllTeamCache != null && AllTeamCache.Length > 0)
                {
                    var validLogos = new List<TeamData>();
                    foreach (var tl in AllTeamCache)
                        if (tl != null && tl != team && tl.logo != null)
                            validLogos.Add(tl);
                    if (validLogos.Count > 0)
                    {
                        var picked = validLogos[Plugin.ConfigRng.Next(validLogos.Count)];
                        cfg.LogoFrom = picked.teamName;
                        Plugin.Log.LogInfo($"[Config] RANDOM logo picked: '{picked.teamName}'");
                    }
                }
            }
            var logoTeam = FindTeamByName(cfg.LogoFrom);
            Plugin.Log.LogInfo($"[Config] Logo lookup '{cfg.LogoFrom}': {(logoTeam != null ? "FOUND '" + logoTeam.teamName + "'" : "NOT FOUND")}");
            // Make sure we didn't find ourselves (the team we're currently modifying)
            if (logoTeam == team)
            {
                Plugin.Log.LogWarning($"[Config] Logo lookup returned the SAME team we're modifying! Searching for another match...");
                logoTeam = null;
                if (AllTeamCache != null)
                {
                    string search = cfg.LogoFrom.Trim();
                    foreach (var t in AllTeamCache)
                    {
                        if (t != null && t != team && t.teamName != null && t.teamName.Trim().Equals(search, StringComparison.OrdinalIgnoreCase))
                        { logoTeam = t; break; }
                    }
                }
                Plugin.Log.LogInfo($"[Config] Second lookup: {(logoTeam != null ? "FOUND '" + logoTeam.teamName + "'" : "NOT FOUND")}");
            }
            if (logoTeam != null)
            {
                Plugin.Log.LogInfo($"[Config] Logo sprite: {(logoTeam.logo != null ? "YES" : "NULL")}, BigLogo: {(logoTeam.alternateBigLogo != null ? "YES" : "NULL")}, Nick: '{logoTeam.nickname}'");
                team.logo = logoTeam.logo;
                team.alternateBigLogo = logoTeam.alternateBigLogo;
                if (logoTeam.homeColors != null) team.homeColors.CopyInPlace(logoTeam.homeColors);
                if (logoTeam.awayColors != null) team.awayColors.CopyInPlace(logoTeam.awayColors);
                team.primaryColorPlayer = logoTeam.primaryColorPlayer;
                team.secondaryColorPlayer = logoTeam.secondaryColorPlayer;
                if (logoTeam.nickname != null) team.nickname = logoTeam.nickname;
            }
        }

        // Team name, city, abbreviation (AFTER logo so they override logo team values)
        if (!string.IsNullOrEmpty(cfg.Name)) team.teamName = cfg.Name;
        else if (string.IsNullOrEmpty(team.teamName)) team.teamName = "Custom Team";
        if (!string.IsNullOrEmpty(cfg.City)) team.city = cfg.City;
        else if (string.IsNullOrEmpty(team.city))
            team.city = !string.IsNullOrEmpty(Plugin.DefaultTeam.City) ? Plugin.DefaultTeam.City : "Custom City";
        if (!string.IsNullOrEmpty(cfg.Abbreviation)) team.nickname = cfg.Abbreviation;
        else if (string.IsNullOrEmpty(team.nickname))
            team.nickname = !string.IsNullOrEmpty(Plugin.DefaultTeam.Abbreviation) ? Plugin.DefaultTeam.Abbreviation : "CUS";

        // If no colors and no logo source, try defaults.txt then hardcoded
        if (cfg.JerseyPrimary == null && cfg.JerseySecondary == null && cfg.JerseyAccent == null
            && string.IsNullOrEmpty(cfg.LogoFrom) && string.IsNullOrEmpty(cfg.ImportTeam))
        {
            // Try logo from defaults.txt
            if (!string.IsNullOrEmpty(Plugin.DefaultTeam.LogoFrom))
            {
                var defLogo = FindTeamByName(Plugin.DefaultTeam.LogoFrom);
                if (defLogo != null && defLogo != team)
                {
                    team.logo = defLogo.logo;
                    team.alternateBigLogo = defLogo.alternateBigLogo;
                    if (defLogo.homeColors != null) team.homeColors.CopyInPlace(defLogo.homeColors);
                    if (defLogo.awayColors != null) team.awayColors.CopyInPlace(defLogo.awayColors);
                    team.primaryColorPlayer = defLogo.primaryColorPlayer;
                    team.secondaryColorPlayer = defLogo.secondaryColorPlayer;
                    if (defLogo.nickname != null) team.nickname = defLogo.nickname;
                    Plugin.Log.LogInfo($"[Config] Applied default logo from '{Plugin.DefaultTeam.LogoFrom}'");
                }
            }
            // Apply default colors from defaults.txt if set
            var dt = Plugin.DefaultTeam;
            if (dt.JerseyPrimary != null)
            {
                var defP = new Color(dt.JerseyPrimary[0]/255f, dt.JerseyPrimary[1]/255f, dt.JerseyPrimary[2]/255f);
                var defS = dt.JerseySecondary != null ? new Color(dt.JerseySecondary[0]/255f, dt.JerseySecondary[1]/255f, dt.JerseySecondary[2]/255f) : new Color(0.2f, 0.2f, 0.2f);
                var defA = dt.JerseyAccent != null ? new Color(dt.JerseyAccent[0]/255f, dt.JerseyAccent[1]/255f, dt.JerseyAccent[2]/255f) : Color.white;
                SetColors(team.homeColors, defP, defS, defA);
                team.primaryColorPlayer = defP;
                team.secondaryColorPlayer = defS;
                if (dt.AwayPrimary != null)
                {
                    var ap = new Color(dt.AwayPrimary[0]/255f, dt.AwayPrimary[1]/255f, dt.AwayPrimary[2]/255f);
                    var as2 = dt.AwaySecondary != null ? new Color(dt.AwaySecondary[0]/255f, dt.AwaySecondary[1]/255f, dt.AwaySecondary[2]/255f) : defP;
                    var aa = dt.AwayAccent != null ? new Color(dt.AwayAccent[0]/255f, dt.AwayAccent[1]/255f, dt.AwayAccent[2]/255f) : defA;
                    SetColors(team.awayColors, ap, as2, aa);
                }
                else
                    SetColors(team.awayColors, Color.white, defP, defS);
            }
            else
            {
                // Hardcoded fallback
                var defP = new Color(0.3f, 0.3f, 0.6f);
                var defS = new Color(0.2f, 0.2f, 0.2f);
                SetColors(team.homeColors, defP, defS, Color.white);
                SetColors(team.awayColors, Color.white, defP, defS);
                team.primaryColorPlayer = defP;
                team.secondaryColorPlayer = defS;
            }
        }
        if (cfg.JerseyPrimary != null || cfg.JerseySecondary != null || cfg.JerseyAccent != null)
        {
            var p = cfg.JerseyPrimary != null ? new Color(cfg.JerseyPrimary[0]/255f, cfg.JerseyPrimary[1]/255f, cfg.JerseyPrimary[2]/255f) : team.primaryColorPlayer;
            var s = cfg.JerseySecondary != null ? new Color(cfg.JerseySecondary[0]/255f, cfg.JerseySecondary[1]/255f, cfg.JerseySecondary[2]/255f) : team.secondaryColorPlayer;
            var a = cfg.JerseyAccent != null ? new Color(cfg.JerseyAccent[0]/255f, cfg.JerseyAccent[1]/255f, cfg.JerseyAccent[2]/255f) : Color.white;
            SetColors(team.homeColors, p, s, a);
            team.primaryColorPlayer = p;
            team.secondaryColorPlayer = s;
        }

        // Away colors override
        if (cfg.AwayPrimary != null || cfg.AwaySecondary != null || cfg.AwayAccent != null)
        {
            var ap = cfg.AwayPrimary != null ? new Color(cfg.AwayPrimary[0]/255f, cfg.AwayPrimary[1]/255f, cfg.AwayPrimary[2]/255f) : Color.white;
            var as2 = cfg.AwaySecondary != null ? new Color(cfg.AwaySecondary[0]/255f, cfg.AwaySecondary[1]/255f, cfg.AwaySecondary[2]/255f) : team.primaryColorPlayer;
            var aa = cfg.AwayAccent != null ? new Color(cfg.AwayAccent[0]/255f, cfg.AwayAccent[1]/255f, cfg.AwayAccent[2]/255f) : Color.white;
            SetColors(team.awayColors, ap, as2, aa);
        }

        // Number colors
        if (cfg.NumberColorHome != null)
            try { team.jerseyHomeNumberColor = new Color(cfg.NumberColorHome[0]/255f, cfg.NumberColorHome[1]/255f, cfg.NumberColorHome[2]/255f); } catch {}
        if (cfg.NumberColorAway != null)
            try { team.jerseyAwayNumberColor = new Color(cfg.NumberColorAway[0]/255f, cfg.NumberColorAway[1]/255f, cfg.NumberColorAway[2]/255f); } catch {}

        // Transition colors
        if (cfg.TransitionPrimary != null)
            try { team.primaryColorTransition = new Color(cfg.TransitionPrimary[0]/255f, cfg.TransitionPrimary[1]/255f, cfg.TransitionPrimary[2]/255f); } catch {}
        if (cfg.TransitionSecondary != null)
            try { team.secondaryColorTransition = new Color(cfg.TransitionSecondary[0]/255f, cfg.TransitionSecondary[1]/255f, cfg.TransitionSecondary[2]/255f); } catch {}
        if (cfg.TransitionTertiary != null)
            try { team.tertiaryColorTransition = new Color(cfg.TransitionTertiary[0]/255f, cfg.TransitionTertiary[1]/255f, cfg.TransitionTertiary[2]/255f); } catch {}

        // Bench
        if (cfg.BenchSize >= 0) team.benchSize = cfg.BenchSize;
        if (!string.IsNullOrEmpty(cfg.BenchHead)) try { team.vanillaBenchPlayerHead = cfg.BenchHead; } catch {}

        // Nuke existing talents/abilities/relics
        var fwds = team.forwards;
        if (fwds != null)
        {
            for (int i = 0; i < fwds.Count; i++)
            {
                var f = fwds[i];
                if (f == null) continue;
                try { f.powerups = new Il2CppSystem.Collections.Generic.List<Rogue.Talent>(); } catch {}
                try { f.ability = null; } catch {}
            }
        }
        if (team.goalie != null)
        {
            try { team.goalie.powerups = new Il2CppSystem.Collections.Generic.List<Rogue.Talent>(); } catch {}
        }
        NukeRelics(team);

        // Clear soccer flags on forwards
        if (fwds != null)
            for (int i = 0; i < fwds.Count; i++)
            {
                try { var fp = fwds[i]?.GetType().GetField("isSoccerPlayer"); if (fp != null) fp.SetValue(fwds[i], false); } catch {}
            }

        // Apply players
        PlayerConfig[] line1 = { cfg.LW, cfg.RW, cfg.C, cfg.LD, cfg.RD };
        if (fwds != null)
        {
            for (int i = 0; i < Math.Min(fwds.Count, line1.Length); i++)
            {
                if (fwds[i] == null) continue;
                var pc = line1[i];
                if (!string.IsNullOrEmpty(pc.ImportPlayer))
                {
                    if (pc.ImportPlayer.Trim().Equals("RANDOM", StringComparison.OrdinalIgnoreCase))
                    {
                        // Pick a random player from all available forwards
                        var allFwds = UnityEngine.Resources.FindObjectsOfTypeAll<ForwardData>();
                        if (allFwds != null && allFwds.Length > 0)
                        {
                            var validFwds = new List<ForwardData>();
                            foreach (var af in allFwds)
                                if (af != null && !string.IsNullOrEmpty(af.firstName))
                                    validFwds.Add(af);
                            if (validFwds.Count > 0)
                            {
                                var picked = validFwds[Plugin.ConfigRng.Next(validFwds.Count)];
                                CopyPlayerData(picked, fwds[i]);
                                Plugin.Log.LogInfo($"[Config] RANDOM player import picked: '{picked.firstName} {picked.lastName}'");
                            }
                        }
                    }
                    else
                    {
                        // Import player by name
                        var srcPlayer = FindPlayerByName(pc.ImportPlayer);
                        if (srcPlayer != null) CopyPlayerData(srcPlayer, fwds[i]);
                    }
                }
                // Apply player config (skins, stats, talents — but NOT per-player colors yet)
                ApplyPlayerConfig(fwds[i], pc, cfg.Uniform);
                // Apply team-level equipment color defaults
                ApplyTeamEquipmentColors(fwds[i], cfg);
                // Apply per-player color overrides (highest priority, overrides team defaults)
                PatchBossLaunchMatch.ApplyPlayerColorOverrides(fwds[i], pc);
            }
        }

        // Team-level random talents (applied to all forwards)
        if (cfg.TeamRandomTalents > 0 && fwds != null)
        {
            List<string> teamPool = null;
            if (cfg.TeamRandomPoolAll)
            {
                EnsureRepos();
                if (CachedTalentRepo?.talents != null)
                {
                    teamPool = new List<string>();
                    for (int ti = 0; ti < CachedTalentRepo.talents.Count; ti++)
                        if (CachedTalentRepo.talents[ti] != null)
                            teamPool.Add(CachedTalentRepo.talents[ti].name);
                }
            }
            else if (cfg.TeamRandomPool != null && cfg.TeamRandomPool.Count > 0)
            {
                teamPool = cfg.TeamRandomPool;
            }
            if (teamPool != null && teamPool.Count > 0)
            {
                for (int i = 0; i < Math.Min(fwds.Count, 5); i++)
                {
                    if (fwds[i] == null) continue;
                    var used = new HashSet<int>();
                    int toGive = Math.Min(cfg.TeamRandomTalents, teamPool.Count);
                    for (int rt = 0; rt < toGive; rt++)
                    {
                        int idx;
                        do { idx = Plugin.ConfigRng.Next(teamPool.Count); } while (used.Contains(idx));
                        used.Add(idx);
                        GiveTalentToPlayer(fwds[i], teamPool[idx]);
                        Plugin.Log.LogInfo($"  [TeamRandom] {fwds[i].firstName} got team random talent: {teamPool[idx]}");
                    }
                }
            }
        }

        // Apply goalie — support Import Player for goalies too
        if (team.goalie != null && cfg.Goalie != null)
        {
            if (!string.IsNullOrEmpty(cfg.Goalie.ImportPlayer))
            {
                var srcGoalie = FindGoalieByName(cfg.Goalie.ImportPlayer);
                if (srcGoalie != null)
                {
                    CopyGoalieData(srcGoalie, team.goalie);
                    Plugin.Log.LogInfo($"[Config] Imported goalie '{srcGoalie.firstName} {srcGoalie.lastName}'");
                }
                else
                    Plugin.Log.LogWarning($"[Config] Goalie '{cfg.Goalie.ImportPlayer}' not found for import");
            }
            ApplyGoalieConfig(team.goalie, cfg.Goalie);
        }

        // Apply relics
        foreach (var r in cfg.Relics)
            GiveRelic(team, r);

        Plugin.Log.LogInfo($"[Config] Applied manual team '{team.teamName}'");
    }

    internal static void SetSchemeColor(ColorScheme scheme, int[] primary, int[] secondary, int[] tertiary)
    {
        if (scheme == null) return;
        if (primary != null) scheme.primaryColor = new Color(primary[0]/255f, primary[1]/255f, primary[2]/255f);
        if (secondary != null) scheme.secondaryColor = new Color(secondary[0]/255f, secondary[1]/255f, secondary[2]/255f);
        if (tertiary != null) scheme.tertiaryColor = new Color(tertiary[0]/255f, tertiary[1]/255f, tertiary[2]/255f);
    }

    internal static void ApplyPlayerColorOverrides(ForwardData f, PlayerConfig pc)
    {
        if (f == null || pc == null) return;

        bool hasAnyColor = pc.JerseyColor != null || pc.JerseySecondaryColor != null ||
            pc.JerseyAccentColor != null || pc.GlovesColor != null || pc.GlovesSecondaryColor != null ||
            pc.GlovesTertiaryColor != null || pc.HelmetColor != null || pc.HelmetSecondaryColor != null ||
            pc.HelmetTertiaryColor != null || pc.PantsColor != null || pc.PantsSecondaryColor != null ||
            pc.PantsTertiaryColor != null || pc.SkatesColor != null || pc.BladeColor != null ||
            pc.LacesColor != null || pc.BicepColor != null || pc.SocksColor != null ||
            pc.SocksSecondaryColor != null || pc.SocksTertiaryColor != null;
        if (!hasAnyColor && pc.NumberColor == null && pc.NumberSecondaryColor == null) return;

        try
        {
            if (hasAnyColor)
            {
                if (f.colorSchemes == null)
                {
                    var teamSels = UnityEngine.Resources.FindObjectsOfTypeAll<TeamSelection>();
                    if (teamSels != null && teamSels.Length > 0 && teamSels[0].visitorTeam?.homeColors != null)
                        f.colorSchemes = teamSels[0].visitorTeam.homeColors;
                }

                if (f.colorSchemes != null)
                {
                    SetSchemeColor(f.colorSchemes.jerseyScheme, pc.JerseyColor, pc.JerseySecondaryColor, pc.JerseyAccentColor);
                    SetSchemeColor(f.colorSchemes.glovesScheme, pc.GlovesColor, pc.GlovesSecondaryColor, pc.GlovesTertiaryColor);
                    SetSchemeColor(f.colorSchemes.helmetScheme, pc.HelmetColor, pc.HelmetSecondaryColor, pc.HelmetTertiaryColor);
                    SetSchemeColor(f.colorSchemes.pantsScheme, pc.PantsColor, pc.PantsSecondaryColor, pc.PantsTertiaryColor);
                    SetSchemeColor(f.colorSchemes.skatesScheme, pc.SkatesColor, pc.BladeColor, pc.LacesColor);
                    SetSchemeColor(f.colorSchemes.socksScheme, pc.SocksColor, pc.SocksSecondaryColor, pc.SocksTertiaryColor);
                    SetSchemeColor(f.colorSchemes.numberScheme, pc.NumberColor, pc.NumberSecondaryColor, null);
                    if (pc.BicepColor != null)
                        f.colorSchemes.jerseyScheme.secondaryColor = new Color(pc.BicepColor[0]/255f, pc.BicepColor[1]/255f, pc.BicepColor[2]/255f);
                }
            }

            if (pc.NumberColor != null)
                f.numberColorOverride = new Color(pc.NumberColor[0]/255f, pc.NumberColor[1]/255f, pc.NumberColor[2]/255f);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[Color] Player color override error: {ex.Message}");
        }
    }

    private static void ApplyTeamEquipmentColors(ForwardData f, TeamConfig cfg)
    {
        if (f == null || cfg == null) return;
        try
        {
            if (f.colorSchemes == null) return;

            SetSchemeColor(f.colorSchemes.glovesScheme, cfg.TeamGlovesColor, cfg.TeamGlovesSecondary, cfg.TeamGlovesTertiary);
            SetSchemeColor(f.colorSchemes.helmetScheme, cfg.TeamHelmetColor, cfg.TeamHelmetSecondary, cfg.TeamHelmetTertiary);
            SetSchemeColor(f.colorSchemes.pantsScheme, cfg.TeamPantsColor, cfg.TeamPantsSecondary, cfg.TeamPantsTertiary);
            SetSchemeColor(f.colorSchemes.skatesScheme, cfg.TeamSkatesColor, cfg.TeamBladeColor, cfg.TeamLacesColor);
            SetSchemeColor(f.colorSchemes.socksScheme, cfg.TeamSocksColor, cfg.TeamSocksSecondary, cfg.TeamSocksTertiary);
            SetSchemeColor(f.colorSchemes.numberScheme, cfg.TeamNumberColor, cfg.TeamNumberSecondary, null);

            if (cfg.TeamBicepColor != null)
                f.colorSchemes.jerseyScheme.secondaryColor = new Color(cfg.TeamBicepColor[0]/255f, cfg.TeamBicepColor[1]/255f, cfg.TeamBicepColor[2]/255f);
            if (cfg.TeamStickColor != null)
                f.colorSchemes.stickScheme.primaryColor = new Color(cfg.TeamStickColor[0]/255f, cfg.TeamStickColor[1]/255f, cfg.TeamStickColor[2]/255f);
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[Config] Team equipment color error: {ex.Message}"); }
    }

    private static void ApplyPlayerConfig(ForwardData f, PlayerConfig pc, UniformConfig uniform)
    {
        if (f == null || pc == null) return;
        if (uniform == null) uniform = new UniformConfig();

        try
        {
            // Name
            if (!string.IsNullOrEmpty(pc.Name))
            {
                var parts = pc.Name.Split(' ', 2);
                f.firstName = parts[0];
                f.lastName = parts.Length > 1 ? parts[1] : "";
            }

            // Stats (defaults to 50 if not set)
            f.speed = pc.Speed;
            f.shotPower = pc.ShotPower;
            f.shotAccuracy = pc.Accuracy;
            f.checking = pc.Checking;

            // Size
            string sizeStr = (pc.Size ?? "Medium").Trim().ToLower();
            f.skaterSize = sizeStr switch
            {
                "extrasmall" => Data.SkaterSize.ExtraSmall,
                "small" => Data.SkaterSize.Small,
                "big" => Data.SkaterSize.Big,
                "extrabig" => Data.SkaterSize.ExtraBig,
                "extraextrabig" => Data.SkaterSize.ExtraExtraBig,
                _ => Data.SkaterSize.Medium
            };

            // Size offset
            if (pc.SizeOffset > 0) f.sizeOffsetPercentage = pc.SizeOffset;

            // Look — face (supports "random")
            if (!string.IsNullOrEmpty(pc.Face))
            {
                string resolvedFace = Plugin.ResolveSkin(pc.Face);
                if (resolvedFace == "RANDOM_FACE")
                {
                    string[] allFaces = {
                        "Faces/Twinfalls/Wiener", "Faces/Twinfalls/Haggis", "Faces/Twinfalls/Jerky", "Faces/Twinfalls/Crockett",
                        "Faces/Toronto/Mathieu", "Faces/Toronto/Jelly", "Faces/Toronto/Kilmore", "Faces/Toronto/Spark",
                        "Faces/Toronto/Dord", "Faces/Toronto/Popping",
                        "Faces/Chicago/Angus", "Faces/Chicago/Rory", "Faces/Chicago/Grohl", "Faces/Chicago/Chicos",
                        "Faces/Chicago/Chapstick", "Faces/Chicago/Louder",
                        "Faces/Canadians/Captain", "Faces/Canadians/Poule", "Faces/Canadians/Gratz",
                        "Faces/Midwest/Brie", "Faces/Midwest/Amber", "Faces/Midwest/Mental", "Faces/Midwest/Rochefort",
                        "Faces/Moutaineers/Krupp", "Faces/Moutaineers/Wurst", "Faces/Moutaineers/Torte",
                        "Faces/Moutaineers/Furter", "Faces/Moutaineers/Pianist",
                        "Faces/Princess/Joan", "Faces/Princess/Clementine", "Faces/Princess/Boni",
                        "Faces/Tycoons/Tycoons_Large", "Faces/Tycoons/Tycoons_Small", "Faces/Tycoons/Tycoons_Elite",
                        "Faces/Prisoners/Dalton", "Faces/Prisoners/Ma", "Faces/Prisoners/Averell", "Faces/Prisoners/Joe",
                        "Faces/Knights/Prince", "Faces/Knights/Lancelov",
                        "Faces/Disco/Oioioi", "Faces/Custom/Helmet_Face",
                        "Faces/Anyteam/Nasher", "Faces/Anyteam/Onepunch",
                        "Faces/Golfers/Golfer_Lady", "Faces/Golfers/Golfer_Ramirez", "Faces/Golfers/Golfer_Elite"
                    };
                    var rng = new System.Random();
                    resolvedFace = allFaces[rng.Next(allFaces.Length)];
                    Plugin.Log.LogInfo($"  [Random] {f.firstName} got random face: {resolvedFace}");
                }
                f.headSkin = resolvedFace;
            }
            f.isLefty = pc.Lefty;
            f.isBlack = pc.Black;

            // Glasses
            if (!string.IsNullOrEmpty(pc.Glasses)) f.glassesSkin = pc.Glasses;

            // Look — uniform (team defaults first)
            if (!string.IsNullOrEmpty(uniform.Body)) f.bodySkin = uniform.Body;
            if (!string.IsNullOrEmpty(uniform.BodyAway)) f.bodyAwaySkin = uniform.BodyAway;
            if (!string.IsNullOrEmpty(uniform.Bicep)) f.bicepSkin = uniform.Bicep;
            if (!string.IsNullOrEmpty(uniform.BicepAway)) f.bicepAwaySkin = uniform.BicepAway;
            if (!string.IsNullOrEmpty(uniform.Gloves)) f.gloveSkin = uniform.Gloves;
            if (!string.IsNullOrEmpty(uniform.GlovesAway)) f.gloveAwaySkin = uniform.GlovesAway;
            if (!string.IsNullOrEmpty(uniform.Pants)) f.pantsSkin = uniform.Pants;
            if (!string.IsNullOrEmpty(uniform.PantsAway)) f.pantsAwaySkin = uniform.PantsAway;
            if (!string.IsNullOrEmpty(uniform.Skates)) f.skateSkin = uniform.Skates;
            if (!string.IsNullOrEmpty(uniform.SkatesAway)) f.skateAwaySkin = uniform.SkatesAway;
            if (!string.IsNullOrEmpty(uniform.Helmet)) f.helmetSkin = uniform.Helmet;
            if (!string.IsNullOrEmpty(uniform.HelmetAway)) f.helmetAwaySkin = uniform.HelmetAway;
            if (!string.IsNullOrEmpty(uniform.Stick)) f.stickSkin = uniform.Stick;

            // Per-player uniform overrides (only apply if explicitly set)
            if (pc.StickOverride != null) f.stickSkin = pc.StickOverride;
            if (pc.HelmetOverride != null) f.helmetSkin = pc.HelmetOverride;
            if (pc.HelmetAwayOverride != null) f.helmetAwaySkin = pc.HelmetAwayOverride;
            if (pc.BodyOverride != null) f.bodySkin = pc.BodyOverride;
            if (pc.BodyAwayOverride != null) f.bodyAwaySkin = pc.BodyAwayOverride;
            if (pc.BicepOverride != null) f.bicepSkin = pc.BicepOverride;
            if (pc.BicepAwayOverride != null) f.bicepAwaySkin = pc.BicepAwayOverride;
            if (pc.GlovesOverride != null) f.gloveSkin = pc.GlovesOverride;
            if (pc.GlovesAwayOverride != null) f.gloveAwaySkin = pc.GlovesAwayOverride;
            if (pc.PantsOverride != null) f.pantsSkin = pc.PantsOverride;
            if (pc.PantsAwayOverride != null) f.pantsAwaySkin = pc.PantsAwayOverride;
            if (pc.SkatesOverride != null) f.skateSkin = pc.SkatesOverride;
            if (pc.SkatesAwayOverride != null) f.skateAwaySkin = pc.SkatesAwayOverride;

            // Logo skin based on handedness
            f.logoSkin = pc.Lefty ? "Team_Logo/Custom_L" : "Team_Logo/Custom_R";

            // Number skin (default to 88 if invalid)
            int num = pc.Number > 0 && pc.Number < 100 ? pc.Number : 88;
            f.numberSkin = $"Numbers/Number_{num}{(pc.Lefty ? "LH" : "")}";

            // Fallback defaults from defaults.txt — ensure no invisible/broken assets
            var ds = Plugin.DefaultSkater;
            var du = Plugin.DefaultTeam.Uniform;
            if (string.IsNullOrEmpty(f.headSkin))
                f.headSkin = !string.IsNullOrEmpty(ds.Face) ? Plugin.ResolveSkin(ds.Face) : "Faces/Custom/Helmet_Face";
            if (string.IsNullOrEmpty(f.bodySkin))
                f.bodySkin = !string.IsNullOrEmpty(du.Body) ? du.Body : "Body/Customization/Customization_colors";
            if (string.IsNullOrEmpty(f.bodyAwaySkin))
                f.bodyAwaySkin = !string.IsNullOrEmpty(du.BodyAway) ? du.BodyAway : f.bodySkin;
            if (string.IsNullOrEmpty(f.bicepSkin))
                f.bicepSkin = !string.IsNullOrEmpty(du.Bicep) ? du.Bicep : "Body_Bicep/Customization/Customization_colors";
            if (string.IsNullOrEmpty(f.gloveSkin))
                f.gloveSkin = !string.IsNullOrEmpty(du.Gloves) ? du.Gloves : "Body_Gloves/Customization/Customization_colors";
            if (string.IsNullOrEmpty(f.pantsSkin))
                f.pantsSkin = !string.IsNullOrEmpty(du.Pants) ? du.Pants : "Body_Pants/Customization/Customization_colors";
            if (string.IsNullOrEmpty(f.skateSkin))
                f.skateSkin = !string.IsNullOrEmpty(du.Skates) ? du.Skates : "Body_Skates/Customization/Customization_colors";
            if (string.IsNullOrEmpty(f.helmetSkin))
                f.helmetSkin = !string.IsNullOrEmpty(du.Helmet) ? du.Helmet : "Faces/Custom/Helmet_Colors";
            if (string.IsNullOrEmpty(f.helmetAwaySkin))
                f.helmetAwaySkin = !string.IsNullOrEmpty(du.HelmetAway) ? du.HelmetAway : f.helmetSkin;
            if (string.IsNullOrEmpty(f.stickSkin))
                f.stickSkin = !string.IsNullOrEmpty(du.Stick) ? du.Stick : "Sticks/Black";
            if (string.IsNullOrEmpty(f.numberSkin))
            {
                int defNum = ds.Number > 0 && ds.Number < 100 ? ds.Number : 88;
                f.numberSkin = $"Numbers/Number_{defNum}";
            }
            if (string.IsNullOrEmpty(f.logoSkin)) f.logoSkin = "Team_Logo/Custom_R";
            // Set default name if empty
            if (string.IsNullOrEmpty(f.firstName) && string.IsNullOrEmpty(f.lastName))
            {
                string defName = !string.IsNullOrEmpty(ds.Name) ? ds.Name : "Player";
                var np = defName.Split(' ', 2);
                f.firstName = np[0];
                f.lastName = np.Length > 1 ? np[1] : "";
            }

            // Per-player color overrides handled by caller (after team-level defaults)

            // Fixed talents (GiveTalentToPlayer clears ability on first call)
            if (pc.Talents != null)
                foreach (var t in pc.Talents)
                    if (!string.IsNullOrEmpty(t)) GiveTalentToPlayer(f, t);

            // Random talents per player
            if (pc.RandomTalentCount > 0)
            {
                List<string> pool = null;
                if (pc.RandomTalentPoolAll)
                {
                    // Use entire talent repository
                    EnsureRepos();
                    if (CachedTalentRepo?.talents != null)
                    {
                        pool = new List<string>();
                        for (int ti = 0; ti < CachedTalentRepo.talents.Count; ti++)
                            if (CachedTalentRepo.talents[ti] != null)
                                pool.Add(CachedTalentRepo.talents[ti].name);
                    }
                }
                else if (pc.RandomTalentPool != null && pc.RandomTalentPool.Count > 0)
                {
                    pool = pc.RandomTalentPool;
                }
                if (pool != null && pool.Count > 0)
                {
                    var used = new HashSet<int>();
                    int toGive = Math.Min(pc.RandomTalentCount, pool.Count);
                    for (int rt = 0; rt < toGive; rt++)
                    {
                        int idx;
                        do { idx = Plugin.ConfigRng.Next(pool.Count); } while (used.Contains(idx));
                        used.Add(idx);
                        GiveTalentToPlayer(f, pool[idx]);
                        Plugin.Log.LogInfo($"  [Random] {f.firstName} got random talent: {pool[idx]}");
                    }
                }
            }

            // Ability AFTER talents (so it doesn't get wiped)
            if (!string.IsNullOrEmpty(pc.Ability))
                SetPlayerAbility(f, pc.Ability);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[Config] Error applying player '{pc.Name}': {ex.Message}");
        }
    }

    private static void ApplyGoalieConfig(GoaltenderData g, PlayerConfig pc)
    {
        if (g == null || pc == null) return;

        try
        {
            // Name
            if (!string.IsNullOrEmpty(pc.Name))
            {
                var parts = pc.Name.Split(' ', 2);
                g.firstName = parts[0];
                g.lastName = parts.Length > 1 ? parts[1] : "";
            }

            // Stats
            g.skill = pc.Skill;
            g.catchingSkill = pc.Catching;
            g.gloveSkill = pc.Glove;
            g.blockerSkill = pc.Blocker;
            g.fiveHoleSkill = pc.FiveHole;
            g.standingSpeed = pc.StandSpeed;
            g.butterflySpeed = pc.ButterflySpeed;
            g.controlSkill = pc.Control;
            g.recoverySkill = pc.Recovery;
            g.passPower = pc.PassPower;
            g.shotPower = pc.ShotPower;
            g.pokecheckSkill = pc.Pokecheck;
            g.depth = pc.Depth;
            g.passReadSkill = pc.PassRead;

            // Face
            if (!string.IsNullOrEmpty(pc.Face)) g.headSkin = pc.Face;

            // Goalie-specific skins
            if (!string.IsNullOrEmpty(pc.GoalieSkin)) try { g.skin = pc.GoalieSkin; } catch {}
            if (!string.IsNullOrEmpty(pc.GoalieSkinAway)) try { g.awaySkin = pc.GoalieSkinAway; } catch {}
            if (!string.IsNullOrEmpty(pc.GoalieGloveSkin)) try { g.gloveSkin = pc.GoalieGloveSkin; } catch {}
            if (!string.IsNullOrEmpty(pc.GoalieGloveAway)) try { g.awayGloveSkin = pc.GoalieGloveAway; } catch {}
            if (!string.IsNullOrEmpty(pc.GoalieBlockerSkin)) try { g.blockerSkin = pc.GoalieBlockerSkin; } catch {}
            if (!string.IsNullOrEmpty(pc.GoalieBlockerAway)) try { g.awayBlockerSkin = pc.GoalieBlockerAway; } catch {}
            if (!string.IsNullOrEmpty(pc.GoaliePadsSkin)) try { g.padsSkin = pc.GoaliePadsSkin; } catch {}
            if (!string.IsNullOrEmpty(pc.GoaliePadsAway)) try { g.awayPadsSkin = pc.GoaliePadsAway; } catch {}
            if (!string.IsNullOrEmpty(pc.GoalieStickSkin)) try { g.stickSkin = pc.GoalieStickSkin; } catch {}
            if (!string.IsNullOrEmpty(pc.GoalieStickAway)) try { g.awayStickSkin = pc.GoalieStickAway; } catch {}
            if (!string.IsNullOrEmpty(pc.GoalieHelmetSkin)) try { g.helmetSkin = pc.GoalieHelmetSkin; } catch {}
            if (!string.IsNullOrEmpty(pc.GoalieLogoSkin)) try { g.logoSkin = pc.GoalieLogoSkin; } catch {}

            // Fallback defaults for goalie from defaults.txt
            var dg = Plugin.DefaultGoalie;
            if (string.IsNullOrEmpty(g.firstName) && string.IsNullOrEmpty(g.lastName))
            {
                string defName = !string.IsNullOrEmpty(dg.Name) ? dg.Name : "Goalie";
                var np = defName.Split(' ', 2);
                g.firstName = np[0];
                g.lastName = np.Length > 1 ? np[1] : "";
            }
            if (string.IsNullOrEmpty(g.headSkin) && !string.IsNullOrEmpty(dg.Face))
                g.headSkin = Plugin.ResolveSkin(dg.Face);

            // Talents
            if (pc.Talents != null)
                foreach (var t in pc.Talents)
                    if (!string.IsNullOrEmpty(t)) GiveGoalieTalent(g, t);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[Config] Error applying goalie '{pc.Name}': {ex.Message}");
        }
    }

    private static void CopyPlayerData(ForwardData src, ForwardData dst)
    {
        // Copy stats
        dst.speed = src.speed; dst.shotPower = src.shotPower;
        dst.shotAccuracy = src.shotAccuracy; dst.checking = src.checking;
        dst.skaterSize = src.skaterSize; dst.isLefty = src.isLefty; dst.isBlack = src.isBlack;
        dst.defaultSkaterType = src.defaultSkaterType;
        dst.sizeOffsetPercentage = src.sizeOffsetPercentage;
        // Copy name
        dst.firstName = src.firstName; dst.lastName = src.lastName;
        // Copy look — all skins including away variants
        dst.headSkin = src.headSkin;
        dst.bodySkin = src.bodySkin; dst.bodyAwaySkin = src.bodyAwaySkin;
        dst.bicepSkin = src.bicepSkin; dst.bicepAwaySkin = src.bicepAwaySkin;
        dst.gloveSkin = src.gloveSkin; dst.gloveAwaySkin = src.gloveAwaySkin;
        dst.pantsSkin = src.pantsSkin; dst.pantsAwaySkin = src.pantsAwaySkin;
        dst.skateSkin = src.skateSkin; dst.skateAwaySkin = src.skateAwaySkin;
        dst.helmetSkin = src.helmetSkin; dst.helmetAwaySkin = src.helmetAwaySkin;
        dst.stickSkin = src.stickSkin;
        dst.numberSkin = src.numberSkin; dst.logoSkin = src.logoSkin;
        dst.glassesSkin = src.glassesSkin;
        // Copy color schemes (all equipment colors)
        dst.colorSchemes = src.colorSchemes;
        dst.numberColorOverride = src.numberColorOverride;
        // Copy ability
        dst.ability = src.ability;
        // Copy talents
        try { dst.powerups = new Il2CppSystem.Collections.Generic.List<Rogue.Talent>(); } catch {}
        if (src.powerups != null)
            for (int i = 0; i < src.powerups.Count; i++)
                if (src.powerups[i] != null) dst.powerups.Add(src.powerups[i]);
    }

    private static void CopyGoalieData(GoaltenderData src, GoaltenderData dst)
    {
        // Name and stats
        dst.firstName = src.firstName; dst.lastName = src.lastName;
        dst.skill = src.skill; dst.catchingSkill = src.catchingSkill;
        dst.gloveSkill = src.gloveSkill; dst.blockerSkill = src.blockerSkill;
        dst.fiveHoleSkill = src.fiveHoleSkill; dst.standingSpeed = src.standingSpeed;
        dst.butterflySpeed = src.butterflySpeed; dst.controlSkill = src.controlSkill;
        dst.recoverySkill = src.recoverySkill; dst.passPower = src.passPower;
        dst.shotPower = src.shotPower; dst.pokecheckSkill = src.pokecheckSkill;
        dst.depth = src.depth; dst.passReadSkill = src.passReadSkill;
        // Face
        dst.headSkin = src.headSkin;
        // All goalie skins
        try { dst.skin = src.skin; } catch {}
        try { dst.awaySkin = src.awaySkin; } catch {}
        try { dst.gloveSkin = src.gloveSkin; } catch {}
        try { dst.awayGloveSkin = src.awayGloveSkin; } catch {}
        try { dst.blockerSkin = src.blockerSkin; } catch {}
        try { dst.awayBlockerSkin = src.awayBlockerSkin; } catch {}
        try { dst.padsSkin = src.padsSkin; } catch {}
        try { dst.awayPadsSkin = src.awayPadsSkin; } catch {}
        try { dst.stickSkin = src.stickSkin; } catch {}
        try { dst.awayStickSkin = src.awayStickSkin; } catch {}
        try { dst.helmetSkin = src.helmetSkin; } catch {}
        try { dst.logoSkin = src.logoSkin; } catch {}
        // Talents
        try { dst.powerups = new Il2CppSystem.Collections.Generic.List<Rogue.Talent>(); } catch {}
        if (src.powerups != null)
            for (int i = 0; i < src.powerups.Count; i++)
                if (src.powerups[i] != null) dst.powerups.Add(src.powerups[i]);
    }

    private static ForwardData FindPlayerByName(string name)
    {
        var allForwards = UnityEngine.Resources.FindObjectsOfTypeAll<ForwardData>();
        if (allForwards == null) return null;
        foreach (var f in allForwards)
        {
            if (f == null) continue;
            string fullName = $"{f.firstName} {f.lastName}".Trim();
            if (fullName.Equals(name, StringComparison.OrdinalIgnoreCase))
                return f;
        }
        return null;
    }

    private static GoaltenderData FindGoalieByName(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        // Random goalie
        if (name.Trim().Equals("RANDOM", StringComparison.OrdinalIgnoreCase))
        {
            var allGoalies = UnityEngine.Resources.FindObjectsOfTypeAll<GoaltenderData>();
            if (allGoalies != null && allGoalies.Length > 0)
            {
                var valid = new List<GoaltenderData>();
                foreach (var g in allGoalies)
                    if (g != null && !string.IsNullOrEmpty(g.firstName))
                        valid.Add(g);
                if (valid.Count > 0)
                {
                    var picked = valid[Plugin.ConfigRng.Next(valid.Count)];
                    Plugin.Log.LogInfo($"[Config] RANDOM goalie picked: '{picked.firstName} {picked.lastName}'");
                    return picked;
                }
            }
            return null;
        }
        // Find by name
        var goalies = UnityEngine.Resources.FindObjectsOfTypeAll<GoaltenderData>();
        if (goalies == null) return null;
        foreach (var g in goalies)
        {
            if (g == null) continue;
            string fullName = $"{g.firstName} {g.lastName}".Trim();
            if (fullName.Equals(name, StringComparison.OrdinalIgnoreCase))
                return g;
        }
        return null;
    }

    private static void NukeRelics(TeamData team)
    {
        if (team.relics != null)
        {
            for (int i = team.relics.Count - 1; i >= 0; i--)
                try { team.relics.RemoveAt(i); } catch {}
            try { team.relics.Clear(); } catch {}
        }
        else
        {
            team.relics = new Il2CppSystem.Collections.Generic.List<Rogue.Relic>();
        }
    }

    // ========== ACT 1 ELITES ==========

    private static void RemixGreasyLettuce(TeamData team, int round)
    {
        if (round == 0)
        {
            SwapToNHLTeam(team, "Vancouver", "Vancouver Canucks", "Vancouver",
                new[] { "Elias Pettersson", "Brock Boeser", "Marco Rossi", "Zeev Buium", "Filip Hronek" },
                "Kevin Lankinen", null,
                new[] { "sorest_loser", "weak_shots" });

            // --- Per-player stats, size, talents, abilities ---
            var f = team.forwards;
            if (f != null)
            {
                // LW: Elias Pettersson
                if (f.Count > 0 && f[0] != null)
                {
                    f[0].speed = 64; f[0].shotPower = 65; f[0].shotAccuracy = 71; f[0].checking = 33;
                    f[0].skaterSize = Data.SkaterSize.Medium;
                    GiveTalentToPlayer(f[0], "Human Shield");
                    GiveTalentToPlayer(f[0], "Quick Draw");
                    GiveTalentToPlayer(f[0], "Flawless Feeder");
                }
                // RW: Brock Boeser
                if (f.Count > 1 && f[1] != null)
                {
                    f[1].speed = 52; f[1].shotPower = 63; f[1].shotAccuracy = 65; f[1].checking = 27;
                    f[1].skaterSize = Data.SkaterSize.Medium;
                    GiveTalentToPlayer(f[1], "Point Sniper");
                }
                // C: Marco Rossi
                if (f.Count > 2 && f[2] != null)
                {
                    f[2].speed = 69; f[2].shotPower = 59; f[2].shotAccuracy = 63; f[2].checking = 37;
                    f[2].skaterSize = Data.SkaterSize.Small;
                    GiveTalentToPlayer(f[2], "Speed Transfer");
                }
                // LD: Zeev Buium
                if (f.Count > 3 && f[3] != null)
                {
                    f[3].speed = 59; f[3].shotPower = 47; f[3].shotAccuracy = 51; f[3].checking = 37;
                    f[3].skaterSize = Data.SkaterSize.Medium;
                    GiveTalentToPlayer(f[3], "Blue Line Boost");
                }
                // RD: Filip Hronek
                if (f.Count > 4 && f[4] != null)
                {
                    f[4].speed = 53; f[4].shotPower = 49; f[4].shotAccuracy = 54; f[4].checking = 43;
                    f[4].skaterSize = Data.SkaterSize.Medium;
                    GiveTalentToPlayer(f[4], "Point Sniper");
                }
            }
            // G: Kevin Lankinen
            if (team.goalie != null)
            {
                var g = team.goalie;
                g.skill = 31; g.catchingSkill = 35; g.gloveSkill = 33; g.blockerSkill = 31;
                g.fiveHoleSkill = 29; g.standingSpeed = 48; g.butterflySpeed = 44;
                g.controlSkill = 31; g.recoverySkill = 33; g.passPower = 38;
                g.shotPower = 33; g.pokecheckSkill = 28; g.depth = 41; g.passReadSkill = 0.40f;
                GiveGoalieTalent(g, "Goalie Enraged On Goal");
            }
        }
        else if (round == 1)
        {
            SwapToNHLTeam(team, "Long Island", "New York Islanders", "New York",
                new[] { "Anders Lee", "Bo Horvat", "Mathew Barzal", "Adam Pelech", "Matthew Schaefer" },
                "Ilya Sorokin", null,
                new[] { "self_defense" });

            var f2 = team.forwards;
            if (f2 != null)
            {
                // LW: Anders Lee
                if (f2.Count > 0 && f2[0] != null)
                {
                    f2[0].speed = 57; f2[0].shotPower = 78; f2[0].shotAccuracy = 74; f2[0].checking = 81;
                    f2[0].skaterSize = Data.SkaterSize.ExtraBig;
                }
                // RW: Bo Horvat
                if (f2.Count > 1 && f2[1] != null)
                {
                    f2[1].speed = 76; f2[1].shotPower = 83; f2[1].shotAccuracy = 78; f2[1].checking = 68;
                    f2[1].skaterSize = Data.SkaterSize.Medium;
                }
                // C: Mathew Barzal
                if (f2.Count > 2 && f2[2] != null)
                {
                    f2[2].speed = 92; f2[2].shotPower = 74; f2[2].shotAccuracy = 83; f2[2].checking = 48;
                    f2[2].skaterSize = Data.SkaterSize.Medium;
                    GiveTalentToPlayer(f2[2], "Velcro");
                    GiveTalentToPlayer(f2[2], "En Garde!");
                    GiveTalentToPlayer(f2[2], "Crit Boost");
                }
                // LD: Adam Pelech (STAR)
                if (f2.Count > 3 && f2[3] != null)
                {
                    f2[3].speed = 68; f2[3].shotPower = 63; f2[3].shotAccuracy = 63; f2[3].checking = 74;
                    f2[3].skaterSize = Data.SkaterSize.Medium;
                    GiveTalentToPlayer(f2[3], "Sonic Interception");
                    GiveTalentToPlayer(f2[3], "Spiked Armor");
                    SetPlayerAbility(f2[3], "Headshot Redirect");
                }
                // RD: Matthew Schaefer
                if (f2.Count > 4 && f2[4] != null)
                {
                    f2[4].speed = 85; f2[4].shotPower = 74; f2[4].shotAccuracy = 74; f2[4].checking = 64;
                    f2[4].skaterSize = Data.SkaterSize.Medium;
                    GiveTalentToPlayer(f2[4], "Flawless Feeder");
                }
            }
            // G: Ilya Sorokin
            if (team.goalie != null)
            {
                var g2 = team.goalie;
                g2.skill = 58; g2.catchingSkill = 62; g2.gloveSkill = 64; g2.blockerSkill = 56;
                g2.fiveHoleSkill = 54; g2.standingSpeed = 56; g2.butterflySpeed = 54;
                g2.controlSkill = 60; g2.recoverySkill = 62; g2.passPower = 48;
                g2.shotPower = 44; g2.pokecheckSkill = 52; g2.depth = 58; g2.passReadSkill = 0.60f;
                GiveGoalieTalent(g2, "Crease Clearer");
            }
        }
        else
        {
            SwapToNHLTeam(team, "Seattle", "Seattle Kraken", "Seattle",
                new[] { "Jordan Eberle", "Matty Beniers", "Jared McCann", "Vince Dunn", "Brandon Montour" },
                "Joey Daccord", null,
                new[] { "briefcase", "rigged_faceoff" });

            var f3 = team.forwards;
            if (f3 != null)
            {
                // LW: Jordan Eberle
                if (f3.Count > 0 && f3[0] != null)
                {
                    f3[0].speed = 92; f3[0].shotPower = 92; f3[0].shotAccuracy = 98; f3[0].checking = 53;
                    f3[0].skaterSize = Data.SkaterSize.Small;
                }
                // RW: Matty Beniers
                if (f3.Count > 1 && f3[1] != null)
                {
                    f3[1].speed = 98; f3[1].shotPower = 88; f3[1].shotAccuracy = 92; f3[1].checking = 66;
                    f3[1].skaterSize = Data.SkaterSize.Medium;
                }
                // C: Jared McCann
                if (f3.Count > 2 && f3[2] != null)
                {
                    f3[2].speed = 95; f3[2].shotPower = 98; f3[2].shotAccuracy = 95; f3[2].checking = 66;
                    f3[2].skaterSize = Data.SkaterSize.Medium;
                }
                // LD: Vince Dunn
                if (f3.Count > 3 && f3[3] != null)
                {
                    f3[3].speed = 95; f3[3].shotPower = 88; f3[3].shotAccuracy = 88; f3[3].checking = 75;
                    f3[3].skaterSize = Data.SkaterSize.Medium;
                }
                // RD: Brandon Montour
                if (f3.Count > 4 && f3[4] != null)
                {
                    f3[4].speed = 98; f3[4].shotPower = 92; f3[4].shotAccuracy = 92; f3[4].checking = 72;
                    f3[4].skaterSize = Data.SkaterSize.Medium;
                }

                // Random talents per position
                var rng = new System.Random();
                var shootingPool = new[] { "Charge Shot", "Iron Helmet", "Mega Deflect", "Point Sniper", "Sword", "Wild Shot", "X-Ray Shot" };
                var speedPool = new[] { "Backward Turbo", "Blue Line Boost", "Board Bumper", "Propeller Helmet", "Puck Hunter", "Puck Rocket", "Puckless Rocket", "Supernova Talent" };
                var passingPool = new[] { "Express Delivery", "Flawless Feeder", "Pass Puncher", "Power Transfer", "Sonic Pass" };
                var defensivePool = new[] { "Defensive Deflect", "En Garde!", "Poke Rage", "Bonecrusher", "Onepunch", "Enraged" };

                // LW Eberle: 1 shooting + 1 speed
                if (f3.Count > 0 && f3[0] != null)
                {
                    var pick1 = shootingPool[rng.Next(shootingPool.Length)];
                    var pick2 = speedPool[rng.Next(speedPool.Length)];
                    GiveTalentToPlayer(f3[0], pick1);
                    GiveTalentToPlayer(f3[0], pick2);
                    Plugin.Log.LogInfo($"  [Kraken] {f3[0].firstName} {f3[0].lastName} got {pick1} + {pick2}");
                }
                // RW Beniers: 1 passing
                if (f3.Count > 1 && f3[1] != null)
                {
                    var pick = passingPool[rng.Next(passingPool.Length)];
                    GiveTalentToPlayer(f3[1], pick);
                    Plugin.Log.LogInfo($"  [Kraken] {f3[1].firstName} {f3[1].lastName} got {pick}");
                }
                // C McCann: 1 shooting
                if (f3.Count > 2 && f3[2] != null)
                {
                    var pick = shootingPool[rng.Next(shootingPool.Length)];
                    GiveTalentToPlayer(f3[2], pick);
                    Plugin.Log.LogInfo($"  [Kraken] {f3[2].firstName} {f3[2].lastName} got {pick}");
                }
                // LD Dunn: 1 defensive
                if (f3.Count > 3 && f3[3] != null)
                {
                    var pick = defensivePool[rng.Next(defensivePool.Length)];
                    GiveTalentToPlayer(f3[3], pick);
                    Plugin.Log.LogInfo($"  [Kraken] {f3[3].firstName} {f3[3].lastName} got {pick}");
                }
                // RD Montour: 1 defensive
                if (f3.Count > 4 && f3[4] != null)
                {
                    var pick = defensivePool[rng.Next(defensivePool.Length)];
                    GiveTalentToPlayer(f3[4], pick);
                    Plugin.Log.LogInfo($"  [Kraken] {f3[4].firstName} {f3[4].lastName} got {pick}");
                }
            }
            if (team.goalie != null)
            {
                var g3 = team.goalie;
                g3.skill = 58; g3.catchingSkill = 62; g3.gloveSkill = 64; g3.blockerSkill = 58;
                g3.fiveHoleSkill = 56; g3.standingSpeed = 58; g3.butterflySpeed = 56;
                g3.controlSkill = 60; g3.recoverySkill = 64; g3.passPower = 54;
                g3.shotPower = 50; g3.pokecheckSkill = 50; g3.depth = 60; g3.passReadSkill = 0.60f;
                var goaliePool = new[] { "Goalie Assist", "Goalie Dance", "Goalie Enrage First30Sec", "Goalie Enrage Last30Sec", "Goalie Enraged On Breakaway", "Goalie Enraged On Goal", "Goalie Enraged On Shot", "Goalie Fart", "Goalie Headshot", "Goalie Pass Proepl", "Goalie Pass Rebound", "Goalie Speed Talent", "Goalie Throw Stick", "Crease Clearer", "Always Catch Pucks" };
                var grng = new System.Random();
                var gpick = goaliePool[grng.Next(goaliePool.Length)];
                GiveGoalieTalent(g3, gpick);
                Plugin.Log.LogInfo($"  [Kraken] Goalie got random talent: {gpick}");
            }
        }
    }

    private static void RemixMeatballs(TeamData team, int round)
    {
        if (round == 0)
        {
            SwapToNHLTeam(team, "St-Louis", "St. Louis Blues", "St. Louis",
                new[] { "Jordan Kyrou", "Pavel Buchnevich", "Robert Thomas", "Philip Broberg", "Colton Parayko" },
                "Jordan Binnington", null,
                new[] { "buzzer_beater" });

            var f = team.forwards;
            if (f != null)
            {
                // LW: Jordan Kyrou (STAR)
                if (f.Count > 0 && f[0] != null)
                {
                    f[0].speed = 73; f[0].shotPower = 67; f[0].shotAccuracy = 65; f[0].checking = 39;
                    f[0].skaterSize = Data.SkaterSize.Medium;
                    GiveTalentToPlayer(f[0], "princeAI");
                    GiveTalentToPlayer(f[0], "Charge Shot (Level 2)");
                    GiveTalentToPlayer(f[0], "XRay Shot (Level 2)");
                    GiveTalentToPlayer(f[0], "Sword (Level 2)");
                    GiveTalentToPlayer(f[0], "Deadzone");
                }
                // RW: Pavel Buchnevich
                if (f.Count > 1 && f[1] != null)
                {
                    f[1].speed = 49; f[1].shotPower = 55; f[1].shotAccuracy = 59; f[1].checking = 37;
                    f[1].skaterSize = Data.SkaterSize.Big;
                }
                // C: Robert Thomas
                if (f.Count > 2 && f[2] != null)
                {
                    f[2].speed = 52; f[2].shotPower = 49; f[2].shotAccuracy = 61; f[2].checking = 27;
                    f[2].skaterSize = Data.SkaterSize.Medium;
                    GiveTalentToPlayer(f[2], "slugger");
                    GiveTalentToPlayer(f[2], "Tong");
                }
                // LD: Philip Broberg
                if (f.Count > 3 && f[3] != null)
                {
                    f[3].speed = 55; f[3].shotPower = 51; f[3].shotAccuracy = 52; f[3].checking = 51;
                    f[3].skaterSize = Data.SkaterSize.ExtraBig;
                }
                // RD: Colton Parayko (STAR)
                if (f.Count > 4 && f[4] != null)
                {
                    f[4].speed = 29; f[4].shotPower = 68; f[4].shotAccuracy = 59; f[4].checking = 74;
                    f[4].skaterSize = Data.SkaterSize.ExtraExtraBig;
                    GiveTalentToPlayer(f[4], "BoneCrusher (Level 2)");
                    GiveTalentToPlayer(f[4], "Defensive Deflect (Level 2)");
                    SetPlayerAbility(f[4], "wet_towel");
                }
            }
            // G: Jordan Binnington
            if (team.goalie != null)
            {
                var g = team.goalie;
                g.skill = 38; g.catchingSkill = 42; g.gloveSkill = 40; g.blockerSkill = 38;
                g.fiveHoleSkill = 36; g.standingSpeed = 52; g.butterflySpeed = 48;
                g.controlSkill = 40; g.recoverySkill = 41; g.passPower = 44;
                g.shotPower = 41; g.pokecheckSkill = 36; g.depth = 48; g.passReadSkill = 0.45f;
                GiveGoalieTalent(g, "Goalie Pass Proepl");
            }
        }
        else if (round == 1)
        {
            SwapToNHLTeam(team, "Boston", "Boston Bruins", "Boston",
                new[] { "Morgan Geekie", "David Pastrnak", "Elias Lindholm", "Hampus Lindholm", "Charlie McAvoy" },
                "Jeremy Swayman", null,
                new[] { "dry_noodle", "slap_stick_relic" });

            var f2 = team.forwards;
            if (f2 != null)
            {
                // LW: Morgan Geekie
                if (f2.Count > 0 && f2[0] != null)
                {
                    f2[0].speed = 70; f2[0].shotPower = 80; f2[0].shotAccuracy = 78; f2[0].checking = 72;
                    f2[0].skaterSize = Data.SkaterSize.ExtraBig;
                }
                // RW: David Pastrnak
                if (f2.Count > 1 && f2[1] != null)
                {
                    f2[1].speed = 84; f2[1].shotPower = 93; f2[1].shotAccuracy = 91; f2[1].checking = 50;
                    f2[1].skaterSize = Data.SkaterSize.Medium;
                    GiveTalentToPlayer(f2[1], "Point Sniper (Level 2)");
                    GiveTalentToPlayer(f2[1], "Sonic Slap");
                    GiveTalentToPlayer(f2[1], "X-Ray Shot");
                    GiveTalentToPlayer(f2[1], "Sword");
                }
                // C: Elias Lindholm
                if (f2.Count > 2 && f2[2] != null)
                {
                    f2[2].speed = 73; f2[2].shotPower = 71; f2[2].shotAccuracy = 76; f2[2].checking = 65;
                    f2[2].skaterSize = Data.SkaterSize.Medium;
                    GiveTalentToPlayer(f2[2], "Blue Line Boost (Level 2)");
                }
                // LD: Hampus Lindholm
                if (f2.Count > 3 && f2[3] != null)
                {
                    f2[3].speed = 74; f2[3].shotPower = 73; f2[3].shotAccuracy = 71; f2[3].checking = 71;
                    f2[3].skaterSize = Data.SkaterSize.ExtraBig;
                }
                // RD: Charlie McAvoy
                if (f2.Count > 4 && f2[4] != null)
                {
                    f2[4].speed = 76; f2[4].shotPower = 78; f2[4].shotAccuracy = 76; f2[4].checking = 80;
                    f2[4].skaterSize = Data.SkaterSize.Big;
                    GiveTalentToPlayer(f2[4], "Enraged");
                }
            }
            // G: Jeremy Swayman
            if (team.goalie != null)
            {
                var g2 = team.goalie;
                g2.skill = 46; g2.catchingSkill = 50; g2.gloveSkill = 52; g2.blockerSkill = 44;
                g2.fiveHoleSkill = 42; g2.standingSpeed = 52; g2.butterflySpeed = 50;
                g2.controlSkill = 48; g2.recoverySkill = 50; g2.passPower = 42;
                g2.shotPower = 38; g2.pokecheckSkill = 40; g2.depth = 46; g2.passReadSkill = 0.54f;
                GiveGoalieTalent(g2, "Goalie Enrage First30Sec");
            }
        }
        else
        {
            SwapToNHLTeam(team, "Detroit", "Detroit Red Wings", "Detroit",
                new[] { "Alex DeBrincat", "Dylan Larkin", "Lucas Raymond", "Moritz Seider", "Simon Edvinsson" },
                "Cam Talbot", null,
                new[] { "gurney" });

            var f3 = team.forwards;
            if (f3 != null)
            {
                // LW: Alex DeBrincat (STAR)
                if (f3.Count > 0 && f3[0] != null)
                {
                    f3[0].speed = 100; f3[0].shotPower = 95; f3[0].shotAccuracy = 102; f3[0].checking = 36;
                    f3[0].skaterSize = Data.SkaterSize.ExtraSmall;
                    GiveTalentToPlayer(f3[0], "Crit Boost");
                    GiveTalentToPlayer(f3[0], "Deadzone");
                }
                // RW: Dylan Larkin
                if (f3.Count > 1 && f3[1] != null)
                {
                    f3[1].speed = 97; f3[1].shotPower = 92; f3[1].shotAccuracy = 92; f3[1].checking = 70;
                    f3[1].skaterSize = Data.SkaterSize.Medium;
                    GiveTalentToPlayer(f3[1], "Speed Transfer");
                }
                // C: Lucas Raymond
                if (f3.Count > 2 && f3[2] != null)
                {
                    f3[2].speed = 100; f3[2].shotPower = 87; f3[2].shotAccuracy = 95; f3[2].checking = 52;
                    f3[2].skaterSize = Data.SkaterSize.Small;
                }
                // LD: Moritz Seider (STAR)
                if (f3.Count > 3 && f3[3] != null)
                {
                    f3[3].speed = 81; f3[3].shotPower = 87; f3[3].shotAccuracy = 85; f3[3].checking = 87;
                    f3[3].skaterSize = Data.SkaterSize.ExtraBig;
                    GiveTalentToPlayer(f3[3], "Onepunch");
                    GiveTalentToPlayer(f3[3], "Spiked Armor");
                }
                // RD: Simon Edvinsson
                if (f3.Count > 4 && f3[4] != null)
                {
                    f3[4].speed = 84; f3[4].shotPower = 85; f3[4].shotAccuracy = 82; f3[4].checking = 82;
                    f3[4].skaterSize = Data.SkaterSize.ExtraBig;
                    GiveTalentToPlayer(f3[4], "Supernova Talent");
                }
            }
            if (team.goalie != null)
            {
                var g3 = team.goalie;
                g3.skill = 56; g3.catchingSkill = 60; g3.gloveSkill = 62; g3.blockerSkill = 56;
                g3.fiveHoleSkill = 54; g3.standingSpeed = 58; g3.butterflySpeed = 56;
                g3.controlSkill = 58; g3.recoverySkill = 62; g3.passPower = 52;
                g3.shotPower = 48; g3.pokecheckSkill = 48; g3.depth = 58; g3.passReadSkill = 0.58f;
                GiveGoalieTalent(g3, "Goalie Pass Proepl");
            }
        }
    }

    private static void RemixCalaveras(TeamData team, int round)
    {
        if (round == 0)
        {
            SwapToNHLTeam(team, "Vegas", "Vegas Golden Knights", "Las Vegas",
                new[] { "Mark Stone", "Mitch Marner", "Jack Eichel", "Shea Theodore", "Noah Hanifin" },
                "Carter Hart", null,
                new[] { "blue_banana" });

            var f = team.forwards;
            if (f != null)
            {
                // LW: Mark Stone
                if (f.Count > 0 && f[0] != null)
                {
                    f[0].speed = 41; f[0].shotPower = 49; f[0].shotAccuracy = 53; f[0].checking = 65;
                    f[0].skaterSize = Data.SkaterSize.ExtraBig;
                    GiveTalentToPlayer(f[0], "BoneCrusher (Level 2)");
                    GiveTalentToPlayer(f[0], "Built Different");
                }
                // RW: Mitch Marner
                if (f.Count > 1 && f[1] != null)
                {
                    f[1].speed = 77; f[1].shotPower = 59; f[1].shotAccuracy = 81; f[1].checking = 17;
                    f[1].skaterSize = Data.SkaterSize.Small;
                    GiveTalentToPlayer(f[1], "Flawless Feeder");
                    GiveTalentToPlayer(f[1], "Blue Line Boost");
                }
                // C: Jack Eichel
                if (f.Count > 2 && f[2] != null)
                {
                    f[2].speed = 73; f[2].shotPower = 73; f[2].shotAccuracy = 75; f[2].checking = 44;
                    f[2].skaterSize = Data.SkaterSize.Big;
                    GiveTalentToPlayer(f[2], "Trick Shot");
                    GiveTalentToPlayer(f[2], "Rebound Magnet");
                }
                // LD: Shea Theodore
                if (f.Count > 3 && f[3] != null)
                {
                    f[3].speed = 59; f[3].shotPower = 61; f[3].shotAccuracy = 63; f[3].checking = 47;
                    f[3].skaterSize = Data.SkaterSize.Big;
                }
                // RD: Noah Hanifin
                if (f.Count > 4 && f[4] != null)
                {
                    f[4].speed = 48; f[4].shotPower = 53; f[4].shotAccuracy = 51; f[4].checking = 49;
                    f[4].skaterSize = Data.SkaterSize.ExtraBig;
                }
            }
            // G: Carter Hart
            if (team.goalie != null)
            {
                var g = team.goalie;
                g.skill = 34; g.catchingSkill = 36; g.gloveSkill = 33; g.blockerSkill = 35;
                g.fiveHoleSkill = 32; g.standingSpeed = 50; g.butterflySpeed = 46;
                g.controlSkill = 34; g.recoverySkill = 37; g.passPower = 32;
                g.shotPower = 30; g.pokecheckSkill = 26; g.depth = 37; g.passReadSkill = 0.44f;
                GiveGoalieTalent(g, "Goalie Pass Rebound");
            }
        }
        else if (round == 1)
        {
            SwapToNHLTeam(team, "Philadelphia", "Philadelphia Flyers", "Philadelphia",
                new[] { "Travis Konecny", "Matvei Michkov", "Sean Couturier", "Travis Sanheim", "Cam York" },
                "Samuel Ersson", null,
                new[] { "enrage_bonus" });

            var f2 = team.forwards;
            if (f2 != null)
            {
                // LW: Travis Konecny
                if (f2.Count > 0 && f2[0] != null)
                {
                    f2[0].speed = 82; f2[0].shotPower = 77; f2[0].shotAccuracy = 79; f2[0].checking = 48;
                    f2[0].skaterSize = Data.SkaterSize.Small;
                }
                // RW: Matvei Michkov
                if (f2.Count > 1 && f2[1] != null)
                {
                    f2[1].speed = 86; f2[1].shotPower = 81; f2[1].shotAccuracy = 86; f2[1].checking = 27;
                    f2[1].skaterSize = Data.SkaterSize.ExtraSmall;
                }
                // C: Sean Couturier
                if (f2.Count > 2 && f2[2] != null)
                {
                    f2[2].speed = 64; f2[2].shotPower = 73; f2[2].shotAccuracy = 73; f2[2].checking = 73;
                    f2[2].skaterSize = Data.SkaterSize.Big;
                    GiveTalentToPlayer(f2[2], "Quick Draw");
                }
                // LD: Travis Sanheim
                if (f2.Count > 3 && f2[3] != null)
                {
                    f2[3].speed = 71; f2[3].shotPower = 71; f2[3].shotAccuracy = 68; f2[3].checking = 68;
                    f2[3].skaterSize = Data.SkaterSize.Big;
                }
                // RD: Cam York
                if (f2.Count > 4 && f2[4] != null)
                {
                    f2[4].speed = 75; f2[4].shotPower = 68; f2[4].shotAccuracy = 68; f2[4].checking = 58;
                    f2[4].skaterSize = Data.SkaterSize.Medium;
                }
            }
            if (team.goalie != null)
            {
                var g2 = team.goalie;
                g2.skill = 44; g2.catchingSkill = 48; g2.gloveSkill = 48; g2.blockerSkill = 42;
                g2.fiveHoleSkill = 40; g2.standingSpeed = 50; g2.butterflySpeed = 48;
                g2.controlSkill = 46; g2.recoverySkill = 46; g2.passPower = 40;
                g2.shotPower = 36; g2.pokecheckSkill = 38; g2.depth = 44; g2.passReadSkill = 0.50f;
                GiveGoalieTalent(g2, "Goalie Enraged On Shot");
            }
        }
        else
        {
            SwapToNHLTeam(team, "Winnipeg", "Winnipeg Jets", "Winnipeg",
                new[] { "Kyle Connor", "Mark Scheifele", "Nikolaj Ehlers", "Josh Morrissey", "Neal Pionk" },
                "Connor Hellebuyck", null,
                new[] { "stopwatch", "rare_berry" });

            var f3 = team.forwards;
            if (f3 != null)
            {
                // LW: Kyle Connor
                if (f3.Count > 0 && f3[0] != null)
                {
                    f3[0].speed = 104; f3[0].shotPower = 98; f3[0].shotAccuracy = 96; f3[0].checking = 59;
                    f3[0].skaterSize = Data.SkaterSize.Medium;
                    GiveTalentToPlayer(f3[0], "tornadoAI");
                }
                // RW: Mark Scheifele
                if (f3.Count > 1 && f3[1] != null)
                {
                    f3[1].speed = 85; f3[1].shotPower = 98; f3[1].shotAccuracy = 93; f3[1].checking = 74;
                    f3[1].skaterSize = Data.SkaterSize.Big;
                    GiveTalentToPlayer(f3[1], "tornadoAI");
                }
                // C: Nikolaj Ehlers
                if (f3.Count > 2 && f3[2] != null)
                {
                    f3[2].speed = 109; f3[2].shotPower = 90; f3[2].shotAccuracy = 93; f3[2].checking = 48;
                    f3[2].skaterSize = Data.SkaterSize.Small;
                    GiveTalentToPlayer(f3[2], "Built Different");
                    GiveTalentToPlayer(f3[2], "Trick Shot (Level 2)");
                }
                // LD: Josh Morrissey
                if (f3.Count > 3 && f3[3] != null)
                {
                    f3[3].speed = 90; f3[3].shotPower = 87; f3[3].shotAccuracy = 85; f3[3].checking = 75;
                    f3[3].skaterSize = Data.SkaterSize.Medium;
                    GiveTalentToPlayer(f3[3], "Heavy Helmet");
                    GiveTalentToPlayer(f3[3], "Armor");
                    GiveTalentToPlayer(f3[3], "knightAI");
                }
                // RD: Neal Pionk
                if (f3.Count > 4 && f3[4] != null)
                {
                    f3[4].speed = 87; f3[4].shotPower = 82; f3[4].shotAccuracy = 82; f3[4].checking = 75;
                    f3[4].skaterSize = Data.SkaterSize.Medium;
                    GiveTalentToPlayer(f3[4], "Built Different");
                    GiveTalentToPlayer(f3[4], "Defensive Deflect (Level 2)");
                }
            }
            if (team.goalie != null)
            {
                var g3 = team.goalie;
                g3.skill = 68; g3.catchingSkill = 70; g3.gloveSkill = 72; g3.blockerSkill = 66;
                g3.fiveHoleSkill = 64; g3.standingSpeed = 62; g3.butterflySpeed = 60;
                g3.controlSkill = 68; g3.recoverySkill = 70; g3.passPower = 58;
                g3.shotPower = 54; g3.pokecheckSkill = 56; g3.depth = 62; g3.passReadSkill = 0.68f;
                GiveGoalieTalent(g3, "Always Catch Pucks");
                GiveGoalieTalent(g3, "Goalie Headshot");
                GiveGoalieTalent(g3, "Goalie Throw Stick");
                GiveGoalieTalent(g3, "Goalie Enraged On Shot");
            }
        }
    }

    private static void RemixTopCheese(TeamData team, int round)
    {
        if (round == 0)
        {
            SwapToNHLTeam(team, "Florida", "Florida Panthers", "Florida",
                new[] { "Sam Bennett", "Sam Reinhart", "Aleksander Barkov", "Gustav Forsling", "Aaron Ekblad" },
                "Sergei Bobrovsky", null,
                new[] { "sorest_loser", "revenge" });

            var f = team.forwards;
            if (f != null)
            {
                // LW: Sam Bennett (STAR)
                if (f.Count > 0 && f[0] != null)
                {
                    f[0].speed = 58; f[0].shotPower = 66; f[0].shotAccuracy = 60; f[0].checking = 64;
                    f[0].skaterSize = Data.SkaterSize.Big;
                    GiveTalentToPlayer(f[0], "Pressure Cooker");
                }
                // RW: Sam Reinhart
                if (f.Count > 1 && f[1] != null)
                {
                    f[1].speed = 57; f[1].shotPower = 63; f[1].shotAccuracy = 67; f[1].checking = 31;
                    f[1].skaterSize = Data.SkaterSize.Medium;
                }
                // C: Aleksander Barkov (STAR)
                if (f.Count > 2 && f[2] != null)
                {
                    f[2].speed = 60; f[2].shotPower = 62; f[2].shotAccuracy = 64; f[2].checking = 66;
                    f[2].skaterSize = Data.SkaterSize.Big;
                }
                // LD: Gustav Forsling
                if (f.Count > 3 && f[3] != null)
                {
                    f[3].speed = 57; f[3].shotPower = 47; f[3].shotAccuracy = 51; f[3].checking = 43;
                    f[3].skaterSize = Data.SkaterSize.Medium;
                    SetPlayerAbility(f[3], "throwingStick");
                }
                // RD: Aaron Ekblad
                if (f.Count > 4 && f[4] != null)
                {
                    f[4].speed = 45; f[4].shotPower = 57; f[4].shotAccuracy = 51; f[4].checking = 59;
                    f[4].skaterSize = Data.SkaterSize.Big;
                }
            }
            // G: Sergei Bobrovsky
            if (team.goalie != null)
            {
                var g = team.goalie;
                g.skill = 39; g.catchingSkill = 41; g.gloveSkill = 43; g.blockerSkill = 39;
                g.fiveHoleSkill = 37; g.standingSpeed = 54; g.butterflySpeed = 50;
                g.controlSkill = 41; g.recoverySkill = 41; g.passPower = 33;
                g.shotPower = 31; g.pokecheckSkill = 29; g.depth = 39; g.passReadSkill = 0.47f;
                GiveGoalieTalent(g, "Always Catch Pucks");
            }
        }
        else if (round == 1)
        {
            SwapToNHLTeam(team, "San Jose", "San Jose Sharks", "San Jose",
                new[] { "William Eklund", "Tyler Toffoli", "Macklin Celebrini", "Dmitry Orlov", "Mario Ferraro" },
                "Yaroslav Askarov", null,
                new[] { "invisible_thread", "whistle_of_time" });

            var f2 = team.forwards;
            if (f2 != null)
            {
                // LW: William Eklund
                if (f2.Count > 0 && f2[0] != null)
                {
                    f2[0].speed = 80; f2[0].shotPower = 68; f2[0].shotAccuracy = 74; f2[0].checking = 40;
                    f2[0].skaterSize = Data.SkaterSize.Small;
                }
                // RW: Tyler Toffoli
                if (f2.Count > 1 && f2[1] != null)
                {
                    f2[1].speed = 70; f2[1].shotPower = 76; f2[1].shotAccuracy = 78; f2[1].checking = 52;
                    f2[1].skaterSize = Data.SkaterSize.Medium;
                }
                // C: Macklin Celebrini (STAR)
                if (f2.Count > 2 && f2[2] != null)
                {
                    f2[2].speed = 90; f2[2].shotPower = 86; f2[2].shotAccuracy = 88; f2[2].checking = 52;
                    f2[2].skaterSize = Data.SkaterSize.Small;
                    GiveTalentToPlayer(f2[2], "Blue Line Boost");
                    GiveTalentToPlayer(f2[2], "Curve Ball");
                    GiveTalentToPlayer(f2[2], "Puck Rocket");
                    GiveTalentToPlayer(f2[2], "tornadoAI");
                    GiveTalentToPlayer(f2[2], "slugger");
                }
                // LD: Dmitry Orlov
                if (f2.Count > 3 && f2[3] != null)
                {
                    f2[3].speed = 64; f2[3].shotPower = 64; f2[3].shotAccuracy = 62; f2[3].checking = 76;
                    f2[3].skaterSize = Data.SkaterSize.Big;
                }
                // RD: Mario Ferraro
                if (f2.Count > 4 && f2[4] != null)
                {
                    f2[4].speed = 70; f2[4].shotPower = 62; f2[4].shotAccuracy = 60; f2[4].checking = 80;
                    f2[4].skaterSize = Data.SkaterSize.Big;
                    GiveTalentToPlayer(f2[4], "Tong (Level 2)");
                    GiveTalentToPlayer(f2[4], "magnetInterception");
                    GiveTalentToPlayer(f2[4], "Velcro");
                    GiveTalentToPlayer(f2[4], "knightAI");
                }
            }
            // G: Yaroslav Askarov
            if (team.goalie != null)
            {
                var g2 = team.goalie;
                g2.skill = 42; g2.catchingSkill = 46; g2.gloveSkill = 44; g2.blockerSkill = 40;
                g2.fiveHoleSkill = 38; g2.standingSpeed = 52; g2.butterflySpeed = 50;
                g2.controlSkill = 44; g2.recoverySkill = 46; g2.passPower = 38;
                g2.shotPower = 34; g2.pokecheckSkill = 36; g2.depth = 42; g2.passReadSkill = 0.48f;
                GiveGoalieTalent(g2, "Goalie Speed Talent");
            }
        }
        else
        {
            SwapToNHLTeam(team, "Columbus", "Columbus Blue Jackets", "Columbus",
                new[] { "Kirill Marchenko", "Patrik Laine", "Adam Fantilli", "Zach Werenski", "Jake Christiansen" },
                "Elvis Merzlikins", null,
                new[] { "bolt" });

            var f3 = team.forwards;
            if (f3 != null)
            {
                // LW: Kirill Marchenko
                if (f3.Count > 0 && f3[0] != null)
                {
                    f3[0].speed = 87; f3[0].shotPower = 91; f3[0].shotAccuracy = 89; f3[0].checking = 68;
                    f3[0].skaterSize = Data.SkaterSize.Big;
                }
                // RW: Patrik Laine (STAR)
                if (f3.Count > 1 && f3[1] != null)
                {
                    f3[1].speed = 67; f3[1].shotPower = 115; f3[1].shotAccuracy = 104; f3[1].checking = 55;
                    f3[1].skaterSize = Data.SkaterSize.ExtraBig;
                    GiveTalentToPlayer(f3[1], "Crit Boost");
                    GiveTalentToPlayer(f3[1], "Escalating Crit");
                    GiveTalentToPlayer(f3[1], "Point Sniper (Level 2)");
                    GiveTalentToPlayer(f3[1], "Glass Cannon");
                    GiveTalentToPlayer(f3[1], "Charge Shot (Level 2)");
                }
                // C: Adam Fantilli
                if (f3.Count > 2 && f3[2] != null)
                {
                    f3[2].speed = 95; f3[2].shotPower = 86; f3[2].shotAccuracy = 89; f3[2].checking = 71;
                    f3[2].skaterSize = Data.SkaterSize.Big;
                }
                // LD: Zach Werenski
                if (f3.Count > 3 && f3[3] != null)
                {
                    f3[3].speed = 89; f3[3].shotPower = 89; f3[3].shotAccuracy = 86; f3[3].checking = 62;
                    f3[3].skaterSize = Data.SkaterSize.Medium;
                }
                // RD: Jake Christiansen
                if (f3.Count > 4 && f3[4] != null)
                {
                    f3[4].speed = 81; f3[4].shotPower = 73; f3[4].shotAccuracy = 76; f3[4].checking = 72;
                    f3[4].skaterSize = Data.SkaterSize.Medium;
                }

                // Random speed talent for everyone except Laine (idx 1) and Werenski (idx 3)
                var speedPool = new[] { "Backward Turbo", "Blue Line Boost", "Board Bumper", "Propeller Helmet", "Puck Hunter", "Puck Rocket", "Puckless Rocket", "Supernova Talent" };
                var rng = new System.Random();
                for (int i = 0; i < f3.Count; i++)
                {
                    if (f3[i] == null || i == 1 || i == 3) continue;
                    var pick = speedPool[rng.Next(speedPool.Length)];
                    GiveTalentToPlayer(f3[i], pick);
                    Plugin.Log.LogInfo($"  [CBJ] {f3[i].firstName} {f3[i].lastName} got random speed talent: {pick}");
                }
            }
            if (team.goalie != null)
            {
                var g3 = team.goalie;
                g3.skill = 60; g3.catchingSkill = 62; g3.gloveSkill = 64; g3.blockerSkill = 58;
                g3.fiveHoleSkill = 56; g3.standingSpeed = 60; g3.butterflySpeed = 58;
                g3.controlSkill = 60; g3.recoverySkill = 62; g3.passPower = 52;
                g3.shotPower = 48; g3.pokecheckSkill = 48; g3.depth = 56; g3.passReadSkill = 0.58f;
                GiveGoalieTalent(g3, "Goalie Speed Talent");
            }
        }
    }

    // ========== ACT 1 BOSSES ==========

    private static void RemixPrisoners(TeamData team, int round)
    {
        if (round == 0)
        {
            SwapToNHLTeam(team, "Buffalo", "Buffalo Sabres", "Buffalo",
                new[] { "Peyton Krebs", "Alex Tuch", "Tage Thompson", "Owen Power", "Rasmus Dahlin" },
                "Ukko-Pekka Luukkonen", null,
                new[] { "stick_breaking_knockout_relic", "crash_test_dummy" });

            var f = team.forwards;
            if (f != null)
            {
                // LW: Peyton Krebs
                if (f.Count > 0 && f[0] != null)
                {
                    f[0].speed = 71; f[0].shotPower = 61; f[0].shotAccuracy = 65; f[0].checking = 41;
                    f[0].skaterSize = Data.SkaterSize.Small;
                }
                // RW: Alex Tuch
                if (f.Count > 1 && f[1] != null)
                {
                    f[1].speed = 67; f[1].shotPower = 71; f[1].shotAccuracy = 69; f[1].checking = 61;
                    f[1].skaterSize = Data.SkaterSize.Big;
                }
                // C: Tage Thompson (STAR)
                if (f.Count > 2 && f[2] != null)
                {
                    f[2].speed = 61; f[2].shotPower = 79; f[2].shotAccuracy = 77; f[2].checking = 57;
                    f[2].skaterSize = Data.SkaterSize.ExtraBig;
                    GiveTalentToPlayer(f[2], "Bouncy McBounce");
                    GiveTalentToPlayer(f[2], "Fast Rebound");
                }
                // LD: Owen Power
                if (f.Count > 3 && f[3] != null)
                {
                    f[3].speed = 63; f[3].shotPower = 67; f[3].shotAccuracy = 65; f[3].checking = 59;
                    f[3].skaterSize = Data.SkaterSize.Big;
                }
                // RD: Rasmus Dahlin (STAR)
                if (f.Count > 4 && f[4] != null)
                {
                    f[4].speed = 75; f[4].shotPower = 71; f[4].shotAccuracy = 73; f[4].checking = 55;
                    f[4].skaterSize = Data.SkaterSize.Big;
                    GiveTalentToPlayer(f[4], "Board Bumper");
                    SetPlayerAbility(f[4], "Grappling Hook Mountaineer");
                }
                // Random checking talents — 2 for Dahlin (idx 4), 1 for everyone else, no dupes per player
                var checkingTalents = new[] { "Armor", "Bonecrusher", "Chexplosion", "Enraged", "Feed", "Heavy Helmet", "Onepunch" };
                var rng = new System.Random();
                for (int i = 0; i < f.Count; i++)
                {
                    if (f[i] == null) continue;
                    int count = (i == 4) ? 2 : 1; // Dahlin gets 2
                    var used = new HashSet<int>();
                    for (int j = 0; j < count; j++)
                    {
                        int idx;
                        do { idx = rng.Next(checkingTalents.Length); } while (used.Contains(idx));
                        used.Add(idx);
                        GiveTalentToPlayer(f[i], checkingTalents[idx]);
                        Plugin.Log.LogInfo($"  [Sabres] {f[i].firstName} {f[i].lastName} got random checking talent: {checkingTalents[idx]}");
                    }
                }
            }
            // G: Ukko-Pekka Luukkonen
            if (team.goalie != null)
            {
                var g = team.goalie;
                g.skill = 33; g.catchingSkill = 35; g.gloveSkill = 37; g.blockerSkill = 33;
                g.fiveHoleSkill = 31; g.standingSpeed = 48; g.butterflySpeed = 44;
                g.controlSkill = 32; g.recoverySkill = 33; g.passPower = 27;
                g.shotPower = 23; g.pokecheckSkill = 19; g.depth = 31; g.passReadSkill = 0.38f;
            }
        }
        else if (round == 1)
        {
            SwapToNHLTeam(team, "Calgary", "Calgary Flames", "Calgary",
                new[] { "Jonathan Huberdeau", "Martin Pospisil", "Nazem Kadri", "Rasmus Andersson", "MacKenzie Weegar" },
                "Dustin Wolf", null,
                new[] { "bolt", "hot_cold" });

            var f2 = team.forwards;
            if (f2 != null)
            {
                // LW: Jonathan Huberdeau
                if (f2.Count > 0 && f2[0] != null)
                {
                    f2[0].speed = 79; f2[0].shotPower = 74; f2[0].shotAccuracy = 83; f2[0].checking = 49;
                    f2[0].skaterSize = Data.SkaterSize.Medium;
                    GiveTalentToPlayer(f2[0], "Cherry Picker");
                    GiveTalentToPlayer(f2[0], "Mega Deflect (Level 2)");
                    GiveTalentToPlayer(f2[0], "slugger");
                    GiveTalentToPlayer(f2[0], "Redirector");
                }
                // RW: Martin Pospisil
                if (f2.Count > 1 && f2[1] != null)
                {
                    f2[1].speed = 83; f2[1].shotPower = 72; f2[1].shotAccuracy = 70; f2[1].checking = 70;
                    f2[1].skaterSize = Data.SkaterSize.Medium;
                    GiveTalentToPlayer(f2[1], "Cherry Picker");
                    GiveTalentToPlayer(f2[1], "Mega Deflect (Level 2)");
                    GiveTalentToPlayer(f2[1], "slugger");
                    GiveTalentToPlayer(f2[1], "Redirector");
                }
                // C: Nazem Kadri
                if (f2.Count > 2 && f2[2] != null)
                {
                    f2[2].speed = 77; f2[2].shotPower = 79; f2[2].shotAccuracy = 77; f2[2].checking = 66;
                    f2[2].skaterSize = Data.SkaterSize.Medium;
                    GiveTalentToPlayer(f2[2], "Flawless Feeder (Level 2)");
                    GiveTalentToPlayer(f2[2], "Supernova Talent");
                }
                // LD: Rasmus Andersson
                if (f2.Count > 3 && f2[3] != null)
                {
                    f2[3].speed = 72; f2[3].shotPower = 72; f2[3].shotAccuracy = 70; f2[3].checking = 68;
                    f2[3].skaterSize = Data.SkaterSize.Medium;
                    GiveTalentToPlayer(f2[3], "Point Sniper");
                }
                // RD: MacKenzie Weegar
                if (f2.Count > 4 && f2[4] != null)
                {
                    f2[4].speed = 72; f2[4].shotPower = 70; f2[4].shotAccuracy = 70; f2[4].checking = 66;
                    f2[4].skaterSize = Data.SkaterSize.Medium;
                    GiveTalentToPlayer(f2[4], "Point Sniper");
                }

                // Manual Hot & Cold: +10 OVR to one random player, -10 to another
                var hcRng = new System.Random();
                int boost = hcRng.Next(f2.Count);
                int nerf;
                do { nerf = hcRng.Next(f2.Count); } while (nerf == boost);
                if (f2[boost] != null)
                {
                    f2[boost].speed += 10; f2[boost].shotPower += 10; f2[boost].shotAccuracy += 10; f2[boost].checking += 10;
                    Plugin.Log.LogInfo($"  [Flames] Hot & Cold: {f2[boost].firstName} {f2[boost].lastName} BOOSTED +10");
                }
                if (f2[nerf] != null)
                {
                    f2[nerf].speed -= 10; f2[nerf].shotPower -= 10; f2[nerf].shotAccuracy -= 10; f2[nerf].checking -= 10;
                    Plugin.Log.LogInfo($"  [Flames] Hot & Cold: {f2[nerf].firstName} {f2[nerf].lastName} NERFED -10");
                }
            }
            if (team.goalie != null)
            {
                var g2 = team.goalie;
                g2.skill = 54; g2.catchingSkill = 58; g2.gloveSkill = 60; g2.blockerSkill = 52;
                g2.fiveHoleSkill = 50; g2.standingSpeed = 58; g2.butterflySpeed = 56;
                g2.controlSkill = 56; g2.recoverySkill = 58; g2.passPower = 48;
                g2.shotPower = 44; g2.pokecheckSkill = 46; g2.depth = 54; g2.passReadSkill = 0.60f;
                GiveGoalieTalent(g2, "Goalie Enrage Last30Sec");
            }
        }
        else
        {
            SwapToNHLTeam(team, "New Jersey", "New Jersey Devils", "New Jersey",
                new[] { "Jack Hughes", "Nico Hischier", "Jesper Bratt", "Dougie Hamilton", "Luke Hughes" },
                "Jacob Markstrom", null,
                new[] { "odd_fungus", "hot_cake" });

            var f3 = team.forwards;
            if (f3 != null)
            {
                // LW: Jack Hughes (STAR)
                if (f3.Count > 0 && f3[0] != null)
                {
                    f3[0].speed = 109; f3[0].shotPower = 93; f3[0].shotAccuracy = 104; f3[0].checking = 54;
                    f3[0].skaterSize = Data.SkaterSize.Small;
                    GiveTalentToPlayer(f3[0], "Puck Rocket");
                    GiveTalentToPlayer(f3[0], "tornadoAI");
                    GiveTalentToPlayer(f3[0], "Hidden Ace");
                }
                // RW: Nico Hischier
                if (f3.Count > 1 && f3[1] != null)
                {
                    f3[1].speed = 96; f3[1].shotPower = 93; f3[1].shotAccuracy = 96; f3[1].checking = 76;
                    f3[1].skaterSize = Data.SkaterSize.Medium;
                    GiveTalentToPlayer(f3[1], "Built Different");
                    GiveTalentToPlayer(f3[1], "En Garde!");
                    GiveTalentToPlayer(f3[1], "Crit Boost");
                }
                // C: Jesper Bratt
                if (f3.Count > 2 && f3[2] != null)
                {
                    f3[2].speed = 104; f3[2].shotPower = 88; f3[2].shotAccuracy = 96; f3[2].checking = 52;
                    f3[2].skaterSize = Data.SkaterSize.Small;
                    GiveTalentToPlayer(f3[2], "Express Delivery");
                    GiveTalentToPlayer(f3[2], "Trick Shot");
                }
                // LD: Dougie Hamilton (STAR)
                if (f3.Count > 3 && f3[3] != null)
                {
                    f3[3].speed = 83; f3[3].shotPower = 101; f3[3].shotAccuracy = 96; f3[3].checking = 82;
                    f3[3].skaterSize = Data.SkaterSize.ExtraExtraBig;
                    GiveTalentToPlayer(f3[3], "The Howitzer");
                    GiveTalentToPlayer(f3[3], "Sword");
                }
                // RD: Luke Hughes
                if (f3.Count > 4 && f3[4] != null)
                {
                    f3[4].speed = 99; f3[4].shotPower = 91; f3[4].shotAccuracy = 91; f3[4].checking = 77;
                    f3[4].skaterSize = Data.SkaterSize.Big;
                    GiveTalentToPlayer(f3[4], "Board Bumper");
                    GiveTalentToPlayer(f3[4], "Blue Line Boost");
                    GiveTalentToPlayer(f3[4], "tornadoAI");
                    GiveTalentToPlayer(f3[4], "Charge Shot");
                }
            }
            if (team.goalie != null)
            {
                var g3 = team.goalie;
                g3.skill = 61; g3.catchingSkill = 63; g3.gloveSkill = 65; g3.blockerSkill = 59;
                g3.fiveHoleSkill = 57; g3.standingSpeed = 57; g3.butterflySpeed = 55;
                g3.controlSkill = 61; g3.recoverySkill = 63; g3.passPower = 51;
                g3.shotPower = 47; g3.pokecheckSkill = 47; g3.depth = 55; g3.passReadSkill = 0.59f;
            }
        }
    }

    private static void RemixOfficials(TeamData team, int round)
    {
        if (round == 0)
        {
            SwapToNHLTeam(team, "Edmonton", "Edmonton Oilers", "Edmonton",
                new[] { "Leon Draisaitl", "Zach Hyman", "Connor McDavid", "Mattias Ekholm", "Evan Bouchard" },
                "Connor Ingram", null,
                new[] { "tied_homie" });

            var f = team.forwards;
            if (f != null)
            {
                // LW: Leon Draisaitl
                if (f.Count > 0 && f[0] != null)
                {
                    f[0].speed = 66; f[0].shotPower = 74; f[0].shotAccuracy = 72; f[0].checking = 54;
                    f[0].skaterSize = Data.SkaterSize.Big;
                    GiveTalentToPlayer(f[0], "Quick Draw");
                }
                // RW: Zach Hyman
                if (f.Count > 1 && f[1] != null)
                {
                    f[1].speed = 69; f[1].shotPower = 69; f[1].shotAccuracy = 67; f[1].checking = 57;
                    f[1].skaterSize = Data.SkaterSize.Big;
                    GiveTalentToPlayer(f[1], "Cherry Picker");
                }
                // C: Connor McDavid
                if (f.Count > 2 && f[2] != null)
                {
                    f[2].speed = 90; f[2].shotPower = 80; f[2].shotAccuracy = 86; f[2].checking = 52;
                    f[2].skaterSize = Data.SkaterSize.Medium;
                    GiveTalentToPlayer(f[2], "Flawless Feeder");
                    GiveTalentToPlayer(f[2], "Quick Draw");
                    GiveTalentToPlayer(f[2], "Blue Line Boost (Level 2)");
                    GiveTalentToPlayer(f[2], "XRay Shot (Level 2)");
                }
                // LD: Mattias Ekholm
                if (f.Count > 3 && f[3] != null)
                {
                    f[3].speed = 58; f[3].shotPower = 66; f[3].shotAccuracy = 64; f[3].checking = 74;
                    f[3].skaterSize = Data.SkaterSize.ExtraBig;
                }
                // RD: Evan Bouchard
                if (f.Count > 4 && f[4] != null)
                {
                    f[4].speed = 64; f[4].shotPower = 68; f[4].shotAccuracy = 68; f[4].checking = 46;
                    f[4].skaterSize = Data.SkaterSize.Big;
                    GiveTalentToPlayer(f[4], "Point Sniper (Level 2)");
                    GiveTalentToPlayer(f[4], "The Howitzer");
                }
            }
            // G: Connor Ingram
            if (team.goalie != null)
            {
                var g = team.goalie;
                g.skill = 36; g.catchingSkill = 38; g.gloveSkill = 36; g.blockerSkill = 34;
                g.fiveHoleSkill = 32; g.standingSpeed = 50; g.butterflySpeed = 46;
                g.controlSkill = 35; g.recoverySkill = 40; g.passPower = 27;
                g.shotPower = 25; g.pokecheckSkill = 23; g.depth = 33; g.passReadSkill = 0.40f;
            }
        }
        else if (round == 1)
        {
            SwapToNHLTeam(team, "Montreal", "Montreal Canadiens", "Montreal",
                new[] { "Juraj Slafkovsky", "Cole Caufield", "Nick Suzuki", "Lane Hutson", "Noah Dobson" },
                "Jakub Dobes", null,
                new[] { "critical_one_timer_relic:2", "bolt:2" });

            var f2 = team.forwards;
            if (f2 != null)
            {
                // LW: Juraj Slafkovsky
                if (f2.Count > 0 && f2[0] != null)
                {
                    f2[0].speed = 79; f2[0].shotPower = 84; f2[0].shotAccuracy = 80; f2[0].checking = 73;
                    f2[0].skaterSize = Data.SkaterSize.ExtraBig;
                }
                // RW: Cole Caufield
                if (f2.Count > 1 && f2[1] != null)
                {
                    f2[1].speed = 96; f2[1].shotPower = 86; f2[1].shotAccuracy = 91; f2[1].checking = 26;
                    f2[1].skaterSize = Data.SkaterSize.ExtraSmall;
                    GiveTalentToPlayer(f2[1], "Sonic Slap (Level 2)");
                    GiveTalentToPlayer(f2[1], "Crit Boost");
                    GiveTalentToPlayer(f2[1], "Point Sniper (Level 2)");
                    GiveTalentToPlayer(f2[1], "Pressure Coooker (Level 2)");
                    GiveTalentToPlayer(f2[1], "Sword (Level 2)");
                }
                // C: Nick Suzuki
                if (f2.Count > 2 && f2[2] != null)
                {
                    f2[2].speed = 90; f2[2].shotPower = 75; f2[2].shotAccuracy = 82; f2[2].checking = 54;
                    f2[2].skaterSize = Data.SkaterSize.Medium;
                    GiveTalentToPlayer(f2[2], "Puckless Rocket (Level 2)");
                    SetPlayerAbility(f2[2], "Grappling Hook Mountaineer");
                }
                // LD: Lane Hutson
                if (f2.Count > 3 && f2[3] != null)
                {
                    f2[3].speed = 99; f2[3].shotPower = 73; f2[3].shotAccuracy = 78; f2[3].checking = 43;
                    f2[3].skaterSize = Data.SkaterSize.Small;
                    GiveTalentToPlayer(f2[3], "Blue Line Boost (Level 2)");
                }
                // RD: Noah Dobson
                if (f2.Count > 4 && f2[4] != null)
                {
                    f2[4].speed = 82; f2[4].shotPower = 80; f2[4].shotAccuracy = 80; f2[4].checking = 69;
                    f2[4].skaterSize = Data.SkaterSize.ExtraBig;
                    GiveTalentToPlayer(f2[4], "Onepunch");
                    GiveTalentToPlayer(f2[4], "Human Shield");
                }
            }
            if (team.goalie != null)
            {
                var g2 = team.goalie;
                g2.skill = 46; g2.catchingSkill = 50; g2.gloveSkill = 52; g2.blockerSkill = 44;
                g2.fiveHoleSkill = 42; g2.standingSpeed = 52; g2.butterflySpeed = 50;
                g2.controlSkill = 48; g2.recoverySkill = 50; g2.passPower = 42;
                g2.shotPower = 38; g2.pokecheckSkill = 40; g2.depth = 46; g2.passReadSkill = 0.52f;
                GiveGoalieTalent(g2, "Goalie Speed Talent");
            }
        }
        else
        {
            SwapToNHLTeam(team, "Tampa Bay", "Tampa Bay Lightning", "Tampa Bay",
                new[] { "Nikita Kucherov", "Brandon Hagel", "Brayden Point", "Victor Hedman", "Erik Cernak" },
                "Andrei Vasilevskiy", null,
                new[] { "last_minute", "breakaway_boost" });

            var f3 = team.forwards;
            if (f3 != null)
            {
                // LW: Nikita Kucherov (STAR)
                if (f3.Count > 0 && f3[0] != null)
                {
                    f3[0].speed = 99; f3[0].shotPower = 102; f3[0].shotAccuracy = 110; f3[0].checking = 50;
                    f3[0].skaterSize = Data.SkaterSize.Small;
                    GiveTalentToPlayer(f3[0], "Trick Shot (Level 2)");
                    GiveTalentToPlayer(f3[0], "XRay Shot (Level 2)");
                    GiveTalentToPlayer(f3[0], "Flawless Feeder");
                }
                // RW: Brandon Hagel
                if (f3.Count > 1 && f3[1] != null)
                {
                    f3[1].speed = 105; f3[1].shotPower = 86; f3[1].shotAccuracy = 86; f3[1].checking = 69;
                    f3[1].skaterSize = Data.SkaterSize.Medium;
                    GiveTalentToPlayer(f3[1], "Speed Transfer");
                }
                // C: Brayden Point
                if (f3.Count > 2 && f3[2] != null)
                {
                    f3[2].speed = 102; f3[2].shotPower = 97; f3[2].shotAccuracy = 99; f3[2].checking = 55;
                    f3[2].skaterSize = Data.SkaterSize.Small;
                    GiveTalentToPlayer(f3[2], "Crit Boost");
                }
                // LD: Victor Hedman
                if (f3.Count > 3 && f3[3] != null)
                {
                    f3[3].speed = 73; f3[3].shotPower = 97; f3[3].shotAccuracy = 94; f3[3].checking = 89;
                    f3[3].skaterSize = Data.SkaterSize.ExtraExtraBig;
                    GiveTalentToPlayer(f3[3], "Armor");
                    GiveTalentToPlayer(f3[3], "knightAI");
                }
                // RD: Erik Cernak
                if (f3.Count > 4 && f3[4] != null)
                {
                    f3[4].speed = 72; f3[4].shotPower = 81; f3[4].shotAccuracy = 78; f3[4].checking = 94;
                    f3[4].skaterSize = Data.SkaterSize.ExtraBig;
                    GiveTalentToPlayer(f3[4], "Spiked Armor");
                    GiveTalentToPlayer(f3[4], "cultistPowerRD");
                }
            }
            if (team.goalie != null)
            {
                var g3 = team.goalie;
                g3.skill = 72; g3.catchingSkill = 74; g3.gloveSkill = 76; g3.blockerSkill = 70;
                g3.fiveHoleSkill = 68; g3.standingSpeed = 65; g3.butterflySpeed = 63;
                g3.controlSkill = 72; g3.recoverySkill = 74; g3.passPower = 62;
                g3.shotPower = 56; g3.pokecheckSkill = 58; g3.depth = 66; g3.passReadSkill = 0.72f;
                GiveGoalieTalent(g3, "Always Catch Pucks");
                GiveGoalieTalent(g3, "Goalie Pass Proepl");
            }
        }
    }

    // ========== ACT 2 ELITES ==========

    private static void RemixCrusaders(TeamData team, int round)
    {
        if (round == 0)
        {
            SwapToNHLTeam(team, "New York", "New York Rangers", "New York",
                new[] { "Alexis Lafreniere", "Mika Zibanejad", "J.T. Miller", "Adam Fox", "Ryan Lindgren" },
                "Igor Shesterkin", null,
                new[] { "hot_cake", "false_goal_on_post" });

            var f = team.forwards;
            if (f != null)
            {
                // LW: Alexis Lafreniere
                if (f.Count > 0 && f[0] != null)
                {
                    f[0].speed = 97; f[0].shotPower = 91; f[0].shotAccuracy = 93; f[0].checking = 73;
                    f[0].skaterSize = Data.SkaterSize.Medium;
                }
                // RW: Mika Zibanejad
                if (f.Count > 1 && f[1] != null)
                {
                    f[1].speed = 83; f[1].shotPower = 97; f[1].shotAccuracy = 95; f[1].checking = 79;
                    f[1].skaterSize = Data.SkaterSize.Big;
                    GiveTalentToPlayer(f[1], "Charge Shot (Level 2)");
                }
                // C: J.T. Miller
                if (f.Count > 2 && f[2] != null)
                {
                    f[2].speed = 85; f[2].shotPower = 93; f[2].shotAccuracy = 91; f[2].checking = 83;
                    f[2].skaterSize = Data.SkaterSize.Big;
                    GiveTalentToPlayer(f[2], "Scrambled");
                }
                // LD: Adam Fox (STAR)
                if (f.Count > 3 && f[3] != null)
                {
                    f[3].speed = 95; f[3].shotPower = 89; f[3].shotAccuracy = 95; f[3].checking = 61;
                    f[3].skaterSize = Data.SkaterSize.Small;
                    GiveTalentToPlayer(f[3], "Power Transfer");
                    GiveTalentToPlayer(f[3], "ImperviousOnPass");
                }
                // RD: Ryan Lindgren
                if (f.Count > 4 && f[4] != null)
                {
                    f[4].speed = 85; f[4].shotPower = 81; f[4].shotAccuracy = 81; f[4].checking = 87;
                    f[4].skaterSize = Data.SkaterSize.Medium;
                    GiveTalentToPlayer(f[4], "Spring Board");
                }
            }
            if (team.goalie != null)
            {
                var g = team.goalie;
                g.skill = 77; g.catchingSkill = 79; g.gloveSkill = 81; g.blockerSkill = 75;
                g.fiveHoleSkill = 75; g.standingSpeed = 60; g.butterflySpeed = 58;
                g.controlSkill = 77; g.recoverySkill = 79; g.passPower = 63;
                g.shotPower = 57; g.pokecheckSkill = 61; g.depth = 67; g.passReadSkill = 0.75f;
                GiveGoalieTalent(g, "Goalie Dance");
            }
        }
        else
        {
            SwapToNHLTeam(team, "Pittsburgh", "Pittsburgh Penguins", "Pittsburgh",
                new[] { "Evgeni Malkin", "Rickard Rakell", "Sidney Crosby", "Erik Karlsson", "Kris Letang" },
                "Stuart Skinner", null,
                new[] { "pitiless", "oven_mitts", "silky_mitts", "reverse_odd_fungus" });

            var f2 = team.forwards;
            if (f2 != null)
            {
                // LW: Evgeni Malkin
                if (f2.Count > 0 && f2[0] != null)
                {
                    f2[0].speed = 95; f2[0].shotPower = 126; f2[0].shotAccuracy = 124; f2[0].checking = 104;
                    f2[0].skaterSize = Data.SkaterSize.ExtraBig;
                    GiveTalentToPlayer(f2[0], "Enraged");
                }
                // RW: Rickard Rakell
                if (f2.Count > 1 && f2[1] != null)
                {
                    f2[1].speed = 110; f2[1].shotPower = 114; f2[1].shotAccuracy = 114; f2[1].checking = 102;
                    f2[1].skaterSize = Data.SkaterSize.Big;
                    GiveTalentToPlayer(f2[1], "Speed Transfer");
                }
                // C: Sidney Crosby (STAR)
                if (f2.Count > 2 && f2[2] != null)
                {
                    f2[2].speed = 115; f2[2].shotPower = 128; f2[2].shotAccuracy = 130; f2[2].checking = 102;
                    f2[2].skaterSize = Data.SkaterSize.Medium;
                    GiveTalentToPlayer(f2[2], "Flawless Feeder (Level 2)");
                    GiveTalentToPlayer(f2[2], "Power Transfer");
                    GiveTalentToPlayer(f2[2], "Hidden Ace");
                }
                // LD: Erik Karlsson
                if (f2.Count > 3 && f2[3] != null)
                {
                    f2[3].speed = 116; f2[3].shotPower = 114; f2[3].shotAccuracy = 118; f2[3].checking = 92;
                    f2[3].skaterSize = Data.SkaterSize.Medium;
                    GiveTalentToPlayer(f2[3], "Blue Line Boost (Level 2)");
                    GiveTalentToPlayer(f2[3], "Slapshot Slowmo");
                    GiveTalentToPlayer(f2[3], "Charge Shot");
                    GiveTalentToPlayer(f2[3], "cultistPowerLD");
                }
                // RD: Kris Letang
                if (f2.Count > 4 && f2[4] != null)
                {
                    f2[4].speed = 108; f2[4].shotPower = 110; f2[4].shotAccuracy = 110; f2[4].checking = 102;
                    f2[4].skaterSize = Data.SkaterSize.Medium;
                    GiveTalentToPlayer(f2[4], "Spring Board");
                }
            }
            if (team.goalie != null)
            {
                var g2 = team.goalie;
                g2.skill = 78; g2.catchingSkill = 80; g2.gloveSkill = 82; g2.blockerSkill = 76;
                g2.fiveHoleSkill = 74; g2.standingSpeed = 62; g2.butterflySpeed = 60;
                g2.controlSkill = 78; g2.recoverySkill = 80; g2.passPower = 64;
                g2.shotPower = 58; g2.pokecheckSkill = 60; g2.depth = 68; g2.passReadSkill = 0.72f;
                GiveGoalieTalent(g2, "Always Catch Pucks");
                GiveGoalieTalent(g2, "Goalie Headshot");
            }
        }
    }

    private static void RemixMountaineers(TeamData team, int round)
    {
        if (round == 0)
        {
            SwapToNHLTeam(team, "Minnesota", "Minnesota Wild", "Minnesota",
                new[] { "Kirill Kaprizov", "Mats Zuccarello", "Joel Eriksson Ek", "Quinn Hughes", "Brock Faber" },
                "Filip Gustavsson", null,
                new[] { "corn_dog", "frog_on_ability" });

            var f = team.forwards;
            if (f != null)
            {
                // LW: Kirill Kaprizov (STAR)
                if (f.Count > 0 && f[0] != null)
                {
                    f[0].speed = 102; f[0].shotPower = 98; f[0].shotAccuracy = 100; f[0].checking = 54;
                    f[0].skaterSize = Data.SkaterSize.Small;
                    GiveTalentToPlayer(f[0], "Trick Shot (Level 2)");
                    GiveTalentToPlayer(f[0], "Puck Rocket");
                    GiveTalentToPlayer(f[0], "tornadoAI");
                }
                // RW: Mats Zuccarello
                if (f.Count > 1 && f[1] != null)
                {
                    f[1].speed = 90; f[1].shotPower = 84; f[1].shotAccuracy = 94; f[1].checking = 36;
                    f[1].skaterSize = Data.SkaterSize.ExtraSmall;
                    GiveTalentToPlayer(f[1], "Express Delivery");
                    GiveTalentToPlayer(f[1], "Sonic Pass");
                }
                // C: Joel Eriksson Ek
                if (f.Count > 2 && f[2] != null)
                {
                    f[2].speed = 84; f[2].shotPower = 88; f[2].shotAccuracy = 86; f[2].checking = 82;
                    f[2].skaterSize = Data.SkaterSize.Big;
                    GiveTalentToPlayer(f[2], "Enraged");
                }
                // LD: Quinn Hughes (STAR)
                if (f.Count > 3 && f[3] != null)
                {
                    f[3].speed = 104; f[3].shotPower = 86; f[3].shotAccuracy = 92; f[3].checking = 50;
                    f[3].skaterSize = Data.SkaterSize.Small;
                    GiveTalentToPlayer(f[3], "Board Bumper");
                    GiveTalentToPlayer(f[3], "Blue Line Boost (Level 2)");
                }
                // RD: Brock Faber
                if (f.Count > 4 && f[4] != null)
                {
                    f[4].speed = 90; f[4].shotPower = 86; f[4].shotAccuracy = 86; f[4].checking = 78;
                    f[4].skaterSize = Data.SkaterSize.Medium;
                    GiveTalentToPlayer(f[4], "Human Shield");
                    GiveTalentToPlayer(f[4], "Defensive Deflect");
                }
            }
            if (team.goalie != null)
            {
                var g = team.goalie;
                g.skill = 70; g.catchingSkill = 72; g.gloveSkill = 74; g.blockerSkill = 68;
                g.fiveHoleSkill = 68; g.standingSpeed = 60; g.butterflySpeed = 58;
                g.controlSkill = 70; g.recoverySkill = 70; g.passPower = 54;
                g.shotPower = 48; g.pokecheckSkill = 50; g.depth = 56; g.passReadSkill = 0.66f;
                GiveGoalieTalent(g, "Goalie Enraged On Breakaway");
            }
        }
        else
        {
            SwapToNHLTeam(team, "Nashville", "Nashville Predators", "Nashville",
                new[] { "Jonathan Marchessault", "Steven Stamkos", "Ryan O'Reilly", "Roman Josi", "Brady Skjei" },
                "Juuse Saros", null,
                new[] { "wasabi_paste", "dentist_drill", "revenge" });

            var f2 = team.forwards;
            if (f2 != null)
            {
                // LW: Jonathan Marchessault
                if (f2.Count > 0 && f2[0] != null)
                {
                    f2[0].speed = 113; f2[0].shotPower = 115; f2[0].shotAccuracy = 119; f2[0].checking = 85;
                    f2[0].skaterSize = Data.SkaterSize.Small;
                    GiveTalentToPlayer(f2[0], "Puck Rocket");
                    GiveTalentToPlayer(f2[0], "tornadoAI");
                    GiveTalentToPlayer(f2[0], "Fragile Talent");
                    GiveTalentToPlayer(f2[0], "Sado Maso");
                }
                // RW: Steven Stamkos (STAR)
                if (f2.Count > 1 && f2[1] != null)
                {
                    f2[1].speed = 109; f2[1].shotPower = 127; f2[1].shotAccuracy = 127; f2[1].checking = 93;
                    f2[1].skaterSize = Data.SkaterSize.Medium;
                    GiveTalentToPlayer(f2[1], "Fury Stick");
                    GiveTalentToPlayer(f2[1], "Pass Back");
                    GiveTalentToPlayer(f2[1], "karlsonBehavior");
                }
                // C: Ryan O'Reilly
                if (f2.Count > 2 && f2[2] != null)
                {
                    f2[2].speed = 101; f2[2].shotPower = 113; f2[2].shotAccuracy = 113; f2[2].checking = 110;
                    f2[2].skaterSize = Data.SkaterSize.Big;
                    GiveTalentToPlayer(f2[2], "Tong (Level 2)");
                    GiveTalentToPlayer(f2[2], "En Garde!");
                }
                // LD: Roman Josi (STAR)
                if (f2.Count > 3 && f2[3] != null)
                {
                    f2[3].speed = 117; f2[3].shotPower = 115; f2[3].shotAccuracy = 117; f2[3].checking = 98;
                    f2[3].skaterSize = Data.SkaterSize.Medium;
                    GiveTalentToPlayer(f2[3], "Homewrecker");
                    GiveTalentToPlayer(f2[3], "Ball Chaser");
                }
                // RD: Brady Skjei
                if (f2.Count > 4 && f2[4] != null)
                {
                    f2[4].speed = 98; f2[4].shotPower = 112; f2[4].shotAccuracy = 110; f2[4].checking = 113;
                    f2[4].skaterSize = Data.SkaterSize.ExtraBig;
                    GiveTalentToPlayer(f2[4], "Onepunch");
                    GiveTalentToPlayer(f2[4], "knightAI");
                }
            }
            if (team.goalie != null)
            {
                var g2 = team.goalie;
                g2.skill = 80; g2.catchingSkill = 82; g2.gloveSkill = 84; g2.blockerSkill = 78;
                g2.fiveHoleSkill = 78; g2.standingSpeed = 62; g2.butterflySpeed = 60;
                g2.controlSkill = 80; g2.recoverySkill = 82; g2.passPower = 64;
                g2.shotPower = 58; g2.pokecheckSkill = 62; g2.depth = 68; g2.passReadSkill = 0.76f;
                GiveGoalieTalent(g2, "Always Catch Pucks");
                GiveGoalieTalent(g2, "Goalie Throw Stick");
            }
        }
    }

    private static void RemixShootingStars(TeamData team, int round)
    {
        if (round == 0)
        {
            SwapToNHLTeam(team, "Toronto", "Toronto Maple Leafs", "Toronto",
                new[] { "Matthew Knies", "William Nylander", "Auston Matthews", "Morgan Rielly", "Jake McCabe" },
                "Joseph Woll", null,
                new[] { "both_team_crit", "laser_pointer" });

            var f = team.forwards;
            if (f != null)
            {
                // LW: Matthew Knies
                if (f.Count > 0 && f[0] != null)
                {
                    f[0].speed = 80; f[0].shotPower = 90; f[0].shotAccuracy = 90; f[0].checking = 84;
                    f[0].skaterSize = Data.SkaterSize.ExtraBig;
                    GiveTalentToPlayer(f[0], "Enraged");
                }
                // RW: William Nylander
                if (f.Count > 1 && f[1] != null)
                {
                    f[1].speed = 96; f[1].shotPower = 92; f[1].shotAccuracy = 94; f[1].checking = 62;
                    f[1].skaterSize = Data.SkaterSize.Medium;
                    GiveTalentToPlayer(f[1], "Speed Transfer");
                    GiveTalentToPlayer(f[1], "Curve Ball");
                }
                // C: Auston Matthews (STAR)
                if (f.Count > 2 && f[2] != null)
                {
                    f[2].speed = 82; f[2].shotPower = 108; f[2].shotAccuracy = 106; f[2].checking = 80;
                    f[2].skaterSize = Data.SkaterSize.ExtraBig;
                    GiveTalentToPlayer(f[2], "Crit Boost");
                    GiveTalentToPlayer(f[2], "Charge Shot (Level 2)");
                    GiveTalentToPlayer(f[2], "X-Ray Shot");
                }
                // LD: Morgan Rielly
                if (f.Count > 3 && f[3] != null)
                {
                    f[3].speed = 90; f[3].shotPower = 86; f[3].shotAccuracy = 86; f[3].checking = 72;
                    f[3].skaterSize = Data.SkaterSize.Medium;
                    GiveTalentToPlayer(f[3], "Blue Line Boost");
                }
                // RD: Jake McCabe
                if (f.Count > 4 && f[4] != null)
                {
                    f[4].speed = 82; f[4].shotPower = 80; f[4].shotAccuracy = 80; f[4].checking = 82;
                    f[4].skaterSize = Data.SkaterSize.Medium;
                    GiveTalentToPlayer(f[4], "Spiked Armor");
                }
            }
            if (team.goalie != null)
            {
                var g = team.goalie;
                g.skill = 65; g.catchingSkill = 67; g.gloveSkill = 69; g.blockerSkill = 63;
                g.fiveHoleSkill = 63; g.standingSpeed = 58; g.butterflySpeed = 56;
                g.controlSkill = 65; g.recoverySkill = 67; g.passPower = 55;
                g.shotPower = 51; g.pokecheckSkill = 51; g.depth = 60; g.passReadSkill = 0.62f;
                GiveGoalieTalent(g, "Goalie Assist");
            }
        }
        else
        {
            SwapToNHLTeam(team, "Anaheim", "Anaheim Ducks", "Anaheim",
                new[] { "Cutter Gauthier", "Beckett Sennecke", "Leo Carlsson", "Radko Gudas", "Olen Zellweger" },
                "Lukas Dostal", null,
                new[] { "egg_timer", "fragile" });

            var f2 = team.forwards;
            if (f2 != null)
            {
                // LW: Cutter Gauthier
                if (f2.Count > 0 && f2[0] != null)
                {
                    f2[0].speed = 110; f2[0].shotPower = 118; f2[0].shotAccuracy = 116; f2[0].checking = 102;
                    f2[0].skaterSize = Data.SkaterSize.Big;
                    GiveTalentToPlayer(f2[0], "Feed");
                    GiveTalentToPlayer(f2[0], "Avenge Me! (Level 2)");
                    GiveTalentToPlayer(f2[0], "Porcelain Hammer");
                    GiveTalentToPlayer(f2[0], "Scrambled");
                    GiveTalentToPlayer(f2[0], "Poke Rage");
                }
                // RW: Beckett Sennecke
                if (f2.Count > 1 && f2[1] != null)
                {
                    f2[1].speed = 114; f2[1].shotPower = 114; f2[1].shotAccuracy = 116; f2[1].checking = 92;
                    f2[1].skaterSize = Data.SkaterSize.Medium;
                    GiveTalentToPlayer(f2[1], "Mega Deflect (Level 2)");
                    GiveTalentToPlayer(f2[1], "Redirector");
                }
                // C: Leo Carlsson (STAR)
                if (f2.Count > 2 && f2[2] != null)
                {
                    f2[2].speed = 118; f2[2].shotPower = 122; f2[2].shotAccuracy = 124; f2[2].checking = 96;
                    f2[2].skaterSize = Data.SkaterSize.Medium;
                    GiveTalentToPlayer(f2[2], "Fury Stick");
                    GiveTalentToPlayer(f2[2], "Anchor");
                    GiveTalentToPlayer(f2[2], "Musical Nets");
                    GiveTalentToPlayer(f2[2], "ImperviousOnPass");
                    GiveTalentToPlayer(f2[2], "Ping King");
                }
                // LD: Radko Gudas
                if (f2.Count > 3 && f2[3] != null)
                {
                    f2[3].speed = 96; f2[3].shotPower = 104; f2[3].shotAccuracy = 100; f2[3].checking = 124;
                    f2[3].skaterSize = Data.SkaterSize.ExtraBig;
                    GiveTalentToPlayer(f2[3], "Onepunch");
                    GiveTalentToPlayer(f2[3], "Chexplosion");
                    GiveTalentToPlayer(f2[3], "marauder");
                    GiveTalentToPlayer(f2[3], "Homewrecker");
                    GiveTalentToPlayer(f2[3], "Heavy Helmet");
                }
                // RD: Olen Zellweger
                if (f2.Count > 4 && f2[4] != null)
                {
                    f2[4].speed = 122; f2[4].shotPower = 108; f2[4].shotAccuracy = 112; f2[4].checking = 80;
                    f2[4].skaterSize = Data.SkaterSize.Small;
                    GiveTalentToPlayer(f2[4], "Board Bumper");
                    GiveTalentToPlayer(f2[4], "Puckless Rocket");
                }
            }
            if (team.goalie != null)
            {
                var g2 = team.goalie;
                g2.skill = 82; g2.catchingSkill = 84; g2.gloveSkill = 86; g2.blockerSkill = 80;
                g2.fiveHoleSkill = 78; g2.standingSpeed = 64; g2.butterflySpeed = 62;
                g2.controlSkill = 82; g2.recoverySkill = 84; g2.passPower = 66;
                g2.shotPower = 60; g2.pokecheckSkill = 62; g2.depth = 70; g2.passReadSkill = 0.78f;
                GiveGoalieTalent(g2, "Goalie Fart");
            }
        }
    }

    private static void RemixHockeyFC(TeamData team, int round)
    {
        if (round == 0)
        {
            SwapToNHLTeam(team, "Ottawa", "Ottawa Senators", "Ottawa",
                new[] { "Brady Tkachuk", "Drake Batherson", "Tim Stutzle", "Thomas Chabot", "Jake Sanderson" },
                "Anton Forsberg", null,
                new[] { "macrocephalia", "sorest_loser" });

            var f = team.forwards;
            if (f != null)
            {
                // LW: Brady Tkachuk (STAR)
                if (f.Count > 0 && f[0] != null)
                {
                    f[0].speed = 76; f[0].shotPower = 94; f[0].shotAccuracy = 90; f[0].checking = 96;
                    f[0].skaterSize = Data.SkaterSize.ExtraBig;
                    GiveTalentToPlayer(f[0], "Enraged");
                    GiveTalentToPlayer(f[0], "Feed");
                    GiveTalentToPlayer(f[0], "marauder");
                    GiveTalentToPlayer(f[0], "Porcelain Hammer");
                    GiveTalentToPlayer(f[0], "Explosion On Landing");
                }
                // RW: Drake Batherson
                if (f.Count > 1 && f[1] != null)
                {
                    f[1].speed = 90; f[1].shotPower = 90; f[1].shotAccuracy = 92; f[1].checking = 66;
                    f[1].skaterSize = Data.SkaterSize.Medium;
                    GiveTalentToPlayer(f[1], "Escalating Crit");
                    GiveTalentToPlayer(f[1], "Glass Cannon");
                    GiveTalentToPlayer(f[1], "Twig Tax");
                }
                // C: Tim Stutzle
                if (f.Count > 2 && f[2] != null)
                {
                    f[2].speed = 98; f[2].shotPower = 92; f[2].shotAccuracy = 94; f[2].checking = 66;
                    f[2].skaterSize = Data.SkaterSize.Medium;
                    GiveTalentToPlayer(f[2], "Backward Turbo");
                    GiveTalentToPlayer(f[2], "Propeller Helmet");
                }
                // LD: Thomas Chabot
                if (f.Count > 3 && f[3] != null)
                {
                    f[3].speed = 86; f[3].shotPower = 88; f[3].shotAccuracy = 88; f[3].checking = 80;
                    f[3].skaterSize = Data.SkaterSize.Big;
                    GiveTalentToPlayer(f[3], "Point Sniper");
                }
                // RD: Jake Sanderson
                if (f.Count > 4 && f[4] != null)
                {
                    f[4].speed = 86; f[4].shotPower = 86; f[4].shotAccuracy = 86; f[4].checking = 82;
                    f[4].skaterSize = Data.SkaterSize.Big;
                    GiveTalentToPlayer(f[4], "Tong");
                }
            }
            if (team.goalie != null)
            {
                var g = team.goalie;
                g.skill = 68; g.catchingSkill = 70; g.gloveSkill = 72; g.blockerSkill = 66;
                g.fiveHoleSkill = 66; g.standingSpeed = 58; g.butterflySpeed = 56;
                g.controlSkill = 68; g.recoverySkill = 70; g.passPower = 58;
                g.shotPower = 54; g.pokecheckSkill = 54; g.depth = 62; g.passReadSkill = 0.62f;
                GiveGoalieTalent(g, "Goalie Fart");
            }
        }
        else
        {
            SwapToNHLTeam(team, "Utah", "Utah Mammoth", "Utah",
                new[] { "Michael Carcone", "Dylan Guenther", "Logan Cooley", "Ian Cole", "Nick DeSimone" },
                "Vitek Vanecek", null,
                new[] { "false_goal_on_post", "nutjob" });

            var f2 = team.forwards;
            if (f2 != null)
            {
                // LW: Michael Carcone
                if (f2.Count > 0 && f2[0] != null)
                {
                    f2[0].speed = 108; f2[0].shotPower = 106; f2[0].shotAccuracy = 104; f2[0].checking = 100;
                    f2[0].skaterSize = Data.SkaterSize.Medium;
                    GiveTalentToPlayer(f2[0], "Scrambled");
                }
                // RW: Dylan Guenther
                if (f2.Count > 1 && f2[1] != null)
                {
                    f2[1].speed = 114; f2[1].shotPower = 122; f2[1].shotAccuracy = 124; f2[1].checking = 84;
                    f2[1].skaterSize = Data.SkaterSize.Medium;
                    GiveTalentToPlayer(f2[1], "Ping King");
                }
                // C: Logan Cooley (STAR)
                if (f2.Count > 2 && f2[2] != null)
                {
                    f2[2].speed = 120; f2[2].shotPower = 118; f2[2].shotAccuracy = 120; f2[2].checking = 88;
                    f2[2].skaterSize = Data.SkaterSize.Small;
                    GiveTalentToPlayer(f2[2], "Random Skill (Level 2)");
                    GiveTalentToPlayer(f2[2], "Changeup");
                }
                // LD: Ian Cole
                if (f2.Count > 3 && f2[3] != null)
                {
                    f2[3].speed = 96; f2[3].shotPower = 100; f2[3].shotAccuracy = 98; f2[3].checking = 118;
                    f2[3].skaterSize = Data.SkaterSize.ExtraBig;
                    GiveTalentToPlayer(f2[3], "Safety First");
                    GiveTalentToPlayer(f2[3], "Anchor");
                }
                // RD: Nick DeSimone
                if (f2.Count > 4 && f2[4] != null)
                {
                    f2[4].speed = 102; f2[4].shotPower = 98; f2[4].shotAccuracy = 98; f2[4].checking = 106;
                    f2[4].skaterSize = Data.SkaterSize.Big;
                    GiveTalentToPlayer(f2[4], "Pass Puncher");
                }

                // 2 random cultist teleport talents assigned to matching positions
                var cultistByPos = new[] { "cultistPowerLW", "cultistPowerRW", "cultistPowerC", "cultistPowerLD", "cultistPowerRD" };
                var cultRng = new System.Random();
                var cultPicked = new HashSet<int>();
                for (int c = 0; c < 2; c++)
                {
                    int idx;
                    do { idx = cultRng.Next(5); } while (cultPicked.Contains(idx));
                    cultPicked.Add(idx);
                    if (f2[idx] != null)
                    {
                        GiveTalentToPlayer(f2[idx], cultistByPos[idx]);
                        Plugin.Log.LogInfo($"  [Utah] {f2[idx].firstName} {f2[idx].lastName} got cultist: {cultistByPos[idx]}");
                    }
                }

                // Random speed talent for everyone + random shooting for forwards (idx 0,1,2)
                // 50/50 chance of Level 2 for talents that have it
                var speedBase = new[] { "Backward Turbo", "Blue Line Boost", "Board Bumper", "Propeller Helmet", "Puck Hunter", "Puck Rocket", "Puckless Rocket", "Supernova Talent" };
                var speedHasLv2 = new HashSet<string> { "Backward Turbo", "Blue Line Boost", "Propeller Helmet" };
                var shootingBase = new[] { "Charge Shot", "Iron Helmet", "Mega Deflect", "Sword", "Wild Shot", "X-Ray Shot", "Trick Shot", "Changeup" };
                var shootingHasLv2 = new HashSet<string> { "Charge Shot", "Iron Helmet", "Mega Deflect", "Sword", "Wild Shot", "Trick Shot" };
                // X-Ray Shot Lv2 uses different naming: "XRay Shot (Level 2)"
                var rng = new System.Random();
                for (int i = 0; i < f2.Count; i++)
                {
                    if (f2[i] == null) continue;
                    var sBase = speedBase[rng.Next(speedBase.Length)];
                    var sPick = (speedHasLv2.Contains(sBase) && rng.Next(2) == 1) ? sBase + " (Level 2)" : sBase;
                    GiveTalentToPlayer(f2[i], sPick);
                    Plugin.Log.LogInfo($"  [Utah] {f2[i].firstName} {f2[i].lastName} got random speed: {sPick}");
                    if (i <= 2) // forwards only
                    {
                        // Cooley (idx 2) already has Changeup, exclude it from his pool
                        string[] pool = (i == 2) ?
                            new[] { "Charge Shot", "Iron Helmet", "Mega Deflect", "Sword", "Wild Shot", "X-Ray Shot", "Trick Shot" } :
                            shootingBase;
                        var shBase = pool[rng.Next(pool.Length)];
                        string shPick;
                        if (shBase == "X-Ray Shot" && rng.Next(2) == 1)
                            shPick = "XRay Shot (Level 2)";
                        else if (shootingHasLv2.Contains(shBase) && rng.Next(2) == 1)
                            shPick = shBase + " (Level 2)";
                        else
                            shPick = shBase;
                        GiveTalentToPlayer(f2[i], shPick);
                        Plugin.Log.LogInfo($"  [Utah] {f2[i].firstName} {f2[i].lastName} got random shooting: {shPick}");
                    }
                }
            }
            if (team.goalie != null)
            {
                var g2 = team.goalie;
                g2.skill = 76; g2.catchingSkill = 78; g2.gloveSkill = 80; g2.blockerSkill = 74;
                g2.fiveHoleSkill = 72; g2.standingSpeed = 62; g2.butterflySpeed = 60;
                g2.controlSkill = 76; g2.recoverySkill = 78; g2.passPower = 60;
                g2.shotPower = 54; g2.pokecheckSkill = 56; g2.depth = 64; g2.passReadSkill = 0.70f;
                GiveGoalieTalent(g2, "Goalie Enraged On Goal");
            }
        }
    }

    // ========== ACT 2 BOSS ==========

    private static void RemixCupCultists(TeamData team, int round)
    {
        if (round == 0)
        {
            SwapToNHLTeam(team, "Dallas", "Dallas Stars", "Dallas",
                new[] { "Jason Robertson", "Tyler Seguin", "Mikko Rantanen", "Miro Heiskanen", "Thomas Harley" },
                "Jake Oettinger", null,
                new[] { "voodoodoll:2", "lucky_molar", "tribal_mask_v2", "slapchot4000" });

            var f = team.forwards;
            if (f != null)
            {
                // LW: Jason Robertson (STAR)
                if (f.Count > 0 && f[0] != null)
                {
                    f[0].speed = 92; f[0].shotPower = 104; f[0].shotAccuracy = 102; f[0].checking = 86;
                    f[0].skaterSize = Data.SkaterSize.Big;
                    GiveTalentToPlayer(f[0], "Wild Shot");
                    GiveTalentToPlayer(f[0], "Bouncy McBounce");
                }
                // RW: Tyler Seguin
                if (f.Count > 1 && f[1] != null)
                {
                    f[1].speed = 97; f[1].shotPower = 97; f[1].shotAccuracy = 99; f[1].checking = 77;
                    f[1].skaterSize = Data.SkaterSize.Medium;
                }
                // C: Mikko Rantanen (STAR)
                if (f.Count > 2 && f[2] != null)
                {
                    f[2].speed = 91; f[2].shotPower = 107; f[2].shotAccuracy = 105; f[2].checking = 86;
                    f[2].skaterSize = Data.SkaterSize.Big;
                    GiveTalentToPlayer(f[2], "Sword (Level 2)");
                    GiveTalentToPlayer(f[2], "Sonic Slap");
                }
                // LD: Miro Heiskanen
                if (f.Count > 3 && f[3] != null)
                {
                    f[3].speed = 103; f[3].shotPower = 93; f[3].shotAccuracy = 97; f[3].checking = 79;
                    f[3].skaterSize = Data.SkaterSize.Medium;
                    GiveTalentToPlayer(f[3], "Puckless Rocket");
                }
                // RD: Thomas Harley
                if (f.Count > 4 && f[4] != null)
                {
                    f[4].speed = 89; f[4].shotPower = 91; f[4].shotAccuracy = 91; f[4].checking = 89;
                    f[4].skaterSize = Data.SkaterSize.Big;
                    GiveTalentToPlayer(f[4], "Defensive Deflect");
                }
            }
            if (team.goalie != null)
            {
                var g = team.goalie;
                g.skill = 72; g.catchingSkill = 74; g.gloveSkill = 76; g.blockerSkill = 70;
                g.fiveHoleSkill = 70; g.standingSpeed = 60; g.butterflySpeed = 58;
                g.controlSkill = 72; g.recoverySkill = 74; g.passPower = 58;
                g.shotPower = 52; g.pokecheckSkill = 56; g.depth = 64; g.passReadSkill = 0.68f;
                GiveGoalieTalent(g, "Goalie Enrage Last30Sec");
                GiveGoalieTalent(g, "Always Catch Pucks");
                GiveGoalieTalent(g, "Goalie Speed Talent");
            }
        }
        else
        {
            SwapToNHLTeam(team, "Carolina", "Carolina Hurricanes", "Carolina",
                new[] { "Nikolaj Ehlers", "Seth Jarvis", "Sebastian Aho", "K'Andre Miller", "Jaccob Slavin" },
                "Brandon Bussi", null,
                new[] { "shrink_serum_opponent", "express_delivery", "relic_freezer", "toothpick:2" });

            var f2 = team.forwards;
            if (f2 != null)
            {
                // Pools for random talents (used by Aho, Jarvis, and all-player speed)
                var speedBase = new[] { "Backward Turbo", "Blue Line Boost", "Board Bumper", "Propeller Helmet", "Puck Hunter", "Puck Rocket", "Puckless Rocket", "Supernova Talent" };
                var speedHasLv2 = new HashSet<string> { "Backward Turbo", "Blue Line Boost", "Propeller Helmet" };
                var shootingBase = new[] { "Charge Shot", "Iron Helmet", "Mega Deflect", "Sword", "Wild Shot", "X-Ray Shot", "Trick Shot", "Changeup" };
                var shootingHasLv2 = new HashSet<string> { "Charge Shot", "Iron Helmet", "Mega Deflect", "Sword", "Wild Shot", "Trick Shot" };
                var rng = new System.Random();

                // LW: Nikolaj Ehlers
                if (f2.Count > 0 && f2[0] != null)
                {
                    f2[0].speed = 136; f2[0].shotPower = 126; f2[0].shotAccuracy = 130; f2[0].checking = 92;
                    f2[0].skaterSize = Data.SkaterSize.Small;
                    GiveTalentToPlayer(f2[0], "Mega Deflect (Level 2)");
                    GiveTalentToPlayer(f2[0], "Redirector");
                    GiveTalentToPlayer(f2[0], "Blue Line Boost (Level 2)");
                }
                // RW: Seth Jarvis
                if (f2.Count > 1 && f2[1] != null)
                {
                    f2[1].speed = 130; f2[1].shotPower = 128; f2[1].shotAccuracy = 132; f2[1].checking = 96;
                    f2[1].skaterSize = Data.SkaterSize.Medium;
                    // 2 random shooting + 1 random speed (50/50 Lv2)
                    var jUsed = new HashSet<int>();
                    for (int j = 0; j < 2; j++)
                    {
                        int idx;
                        do { idx = rng.Next(shootingBase.Length); } while (jUsed.Contains(idx));
                        jUsed.Add(idx);
                        var shBase = shootingBase[idx];
                        string shPick;
                        if (shBase == "X-Ray Shot" && rng.Next(2) == 1) shPick = "XRay Shot (Level 2)";
                        else if (shootingHasLv2.Contains(shBase) && rng.Next(2) == 1) shPick = shBase + " (Level 2)";
                        else shPick = shBase;
                        GiveTalentToPlayer(f2[1], shPick);
                        Plugin.Log.LogInfo($"  [Canes] Jarvis got random shooting: {shPick}");
                    }
                    var jSpd = speedBase[rng.Next(speedBase.Length)];
                    var jSpdPick = (speedHasLv2.Contains(jSpd) && rng.Next(2) == 1) ? jSpd + " (Level 2)" : jSpd;
                    GiveTalentToPlayer(f2[1], jSpdPick);
                    Plugin.Log.LogInfo($"  [Canes] Jarvis got random speed: {jSpdPick}");
                }
                // C: Sebastian Aho (STAR)
                if (f2.Count > 2 && f2[2] != null)
                {
                    f2[2].speed = 132; f2[2].shotPower = 136; f2[2].shotAccuracy = 138; f2[2].checking = 106;
                    f2[2].skaterSize = Data.SkaterSize.Small;
                    // 1 random shooting + 2 random speed (50/50 Lv2)
                    var aShBase = shootingBase[rng.Next(shootingBase.Length)];
                    string aShPick;
                    if (aShBase == "X-Ray Shot" && rng.Next(2) == 1) aShPick = "XRay Shot (Level 2)";
                    else if (shootingHasLv2.Contains(aShBase) && rng.Next(2) == 1) aShPick = aShBase + " (Level 2)";
                    else aShPick = aShBase;
                    GiveTalentToPlayer(f2[2], aShPick);
                    Plugin.Log.LogInfo($"  [Canes] Aho got random shooting: {aShPick}");
                    var aUsed = new HashSet<int>();
                    for (int j = 0; j < 2; j++)
                    {
                        int idx;
                        do { idx = rng.Next(speedBase.Length); } while (aUsed.Contains(idx));
                        aUsed.Add(idx);
                        var sBase = speedBase[idx];
                        var sPick = (speedHasLv2.Contains(sBase) && rng.Next(2) == 1) ? sBase + " (Level 2)" : sBase;
                        GiveTalentToPlayer(f2[2], sPick);
                        Plugin.Log.LogInfo($"  [Canes] Aho got random speed: {sPick}");
                    }
                }
                // LD: K'Andre Miller
                if (f2.Count > 3 && f2[3] != null)
                {
                    f2[3].speed = 110; f2[3].shotPower = 122; f2[3].shotAccuracy = 120; f2[3].checking = 130;
                    f2[3].skaterSize = Data.SkaterSize.ExtraBig;
                    GiveTalentToPlayer(f2[3], "Onepunch");
                    GiveTalentToPlayer(f2[3], "knightAI");
                    GiveTalentToPlayer(f2[3], "Heavy Helmet");
                    GiveTalentToPlayer(f2[3], "Porcelain Hammer");
                    GiveTalentToPlayer(f2[3], "Built Different");
                    GiveTalentToPlayer(f2[3], "Point Sniper (Level 2)");
                }
                // RD: Jaccob Slavin
                if (f2.Count > 4 && f2[4] != null)
                {
                    f2[4].speed = 120; f2[4].shotPower = 118; f2[4].shotAccuracy = 116; f2[4].checking = 124;
                    f2[4].skaterSize = Data.SkaterSize.Big;
                    GiveTalentToPlayer(f2[4], "Sonic Pass");
                }

                // Random speed talent for each player (50/50 Lv2)
                for (int i = 0; i < f2.Count; i++)
                {
                    if (f2[i] == null) continue;
                    var sBase = speedBase[rng.Next(speedBase.Length)];
                    var sPick = (speedHasLv2.Contains(sBase) && rng.Next(2) == 1) ? sBase + " (Level 2)" : sBase;
                    GiveTalentToPlayer(f2[i], sPick);
                    Plugin.Log.LogInfo($"  [Canes] {f2[i].firstName} {f2[i].lastName} got random speed: {sPick}");
                }
            }
            if (team.goalie != null)
            {
                var g2 = team.goalie;
                g2.skill = 84; g2.catchingSkill = 86; g2.gloveSkill = 88; g2.blockerSkill = 82;
                g2.fiveHoleSkill = 80; g2.standingSpeed = 64; g2.butterflySpeed = 62;
                g2.controlSkill = 84; g2.recoverySkill = 86; g2.passPower = 68;
                g2.shotPower = 62; g2.pokecheckSkill = 64; g2.depth = 72; g2.passReadSkill = 0.80f;
                var goaliePool = new[] { "Goalie Assist", "Goalie Dance", "Goalie Enrage First30Sec", "Goalie Enrage Last30Sec", "Goalie Enraged On Breakaway", "Goalie Enraged On Goal", "Goalie Enraged On Shot", "Goalie Fart", "Goalie Headshot", "Goalie Pass Proepl", "Goalie Pass Rebound", "Goalie Speed Talent", "Goalie Throw Stick", "Crease Clearer", "Always Catch Pucks" };
                var grng = new System.Random();
                var gUsed = new HashSet<int>();
                for (int g = 0; g < 2; g++)
                {
                    int idx;
                    do { idx = grng.Next(goaliePool.Length); } while (gUsed.Contains(idx));
                    gUsed.Add(idx);
                    GiveGoalieTalent(g2, goaliePool[idx]);
                    Plugin.Log.LogInfo($"  [Canes] Goalie got random talent: {goaliePool[idx]}");
                }
            }
        }
    }

    // ========== ACT 3 ELITES ==========

    private static void RemixPrincess(TeamData team, int round)
    {
        SwapToNHLTeam(team, "Los Angeles", "Los Angeles Kings", "Los Angeles",
            new[] { "Artemi Panarin", "Quinton Byfield", "Anze Kopitar", "Drew Doughty", "Brandt Clarke" },
            "Darcy Kuemper", null,
            new[] { "briefcase:2", "stopwatch", "ice_slapshot", "relic_freezer", "oaky_timer" });

        var f = team.forwards;
        if (f != null)
        {
            // LW: Artemi Panarin
            if (f.Count > 0 && f[0] != null)
            {
                f[0].speed = 132; f[0].shotPower = 122; f[0].shotAccuracy = 130; f[0].checking = 82;
                f[0].skaterSize = Data.SkaterSize.Small;
                GiveTalentToPlayer(f[0], "Flawless Feeder (Level 2)");
                GiveTalentToPlayer(f[0], "Power Transfer");
                GiveTalentToPlayer(f[0], "Puck Rocket");
                SetPlayerAbility(f[0], "jump");
            }
            // RW: Quinton Byfield
            if (f.Count > 1 && f[1] != null)
            {
                f[1].speed = 120; f[1].shotPower = 124; f[1].shotAccuracy = 122; f[1].checking = 116;
                f[1].skaterSize = Data.SkaterSize.Big;
                GiveTalentToPlayer(f[1], "Onepunch");
                GiveTalentToPlayer(f[1], "Scrambled");
                GiveTalentToPlayer(f[1], "Charge Shot (Level 2)");
                GiveTalentToPlayer(f[1], "marauder");
                GiveTalentToPlayer(f[1], "Ball Chaser");
            }
            // C: Anze Kopitar
            if (f.Count > 2 && f[2] != null)
            {
                f[2].speed = 106; f[2].shotPower = 120; f[2].shotAccuracy = 120; f[2].checking = 114;
                f[2].skaterSize = Data.SkaterSize.Big;
                GiveTalentToPlayer(f[2], "Tong (Level 2)");
                GiveTalentToPlayer(f[2], "En Garde!");
                GiveTalentToPlayer(f[2], "ImperviousOnPass");
                GiveTalentToPlayer(f[2], "Hidden Ace");
            }
            // LD: Drew Doughty
            if (f.Count > 3 && f[3] != null)
            {
                f[3].speed = 112; f[3].shotPower = 118; f[3].shotAccuracy = 118; f[3].checking = 118;
                f[3].skaterSize = Data.SkaterSize.ExtraBig;
                GiveTalentToPlayer(f[3], "Homewrecker");
                GiveTalentToPlayer(f[3], "Human Shield");
                GiveTalentToPlayer(f[3], "Velcro");
                GiveTalentToPlayer(f[3], "Enraged");
                GiveTalentToPlayer(f[3], "Feed");
                GiveTalentToPlayer(f[3], "Enrage On Shot");
                GiveTalentToPlayer(f[3], "Poke Rage");
            }
            // RD: Brandt Clarke
            if (f.Count > 4 && f[4] != null)
            {
                f[4].speed = 120; f[4].shotPower = 118; f[4].shotAccuracy = 118; f[4].checking = 108;
                f[4].skaterSize = Data.SkaterSize.ExtraBig;
                GiveTalentToPlayer(f[4], "Homewrecker");
                GiveTalentToPlayer(f[4], "Human Shield");
                GiveTalentToPlayer(f[4], "Velcro");
                GiveTalentToPlayer(f[4], "Triple Twig");
                SetPlayerAbility(f[4], "throwingStick");
            }

            // 2 random players get immune to injury
            var immRng = new System.Random();
            var immPicked = new HashSet<int>();
            for (int im = 0; im < 2; im++)
            {
                int idx;
                do { idx = immRng.Next(f.Count); } while (immPicked.Contains(idx));
                immPicked.Add(idx);
                if (f[idx] != null)
                {
                    GiveTalentToPlayer(f[idx], "Built Different");
                    Plugin.Log.LogInfo($"  [Kings] {f[idx].firstName} {f[idx].lastName} got immune to injury");
                }
            }
        }
        if (team.goalie != null)
        {
            var g = team.goalie;
            g.skill = 94; g.catchingSkill = 96; g.gloveSkill = 98; g.blockerSkill = 92;
            g.fiveHoleSkill = 92; g.standingSpeed = 68; g.butterflySpeed = 66;
            g.controlSkill = 64; g.recoverySkill = 66; g.passPower = 56;
            g.shotPower = 52; g.pokecheckSkill = 52; g.depth = 60; g.passReadSkill = 0.62f;
            GiveGoalieTalent(g, "Always Catch Pucks");
            GiveGoalieTalent(g, "Goalie Speed Talent");
            GiveGoalieTalent(g, "Goalie Headshot");
            GiveGoalieTalent(g, "Goalie Pass Rebound");
            GiveGoalieTalent(g, "Goalie Pass Proepl");
            GiveGoalieTalent(g, "Goalie Dance");
        }
    }

    private static void RemixTeamCanada(TeamData team, int round)
    {
        SwapToNHLTeam(team, "Chicago", "Chicago Blackhawks", "Chicago",
            new[] { "Tyler Bertuzzi", "Teuvo Teravainen", "Connor Bedard", "Artyom Levshunov", "Alex Vlasic" },
            "Spencer Knight", null,
            new[] { "sorest_loser", "fossilized_star", "nutjob", "double_incision", "blood_bonus" });

        var f = team.forwards;
        if (f != null)
        {
            // LW: Tyler Bertuzzi
            if (f.Count > 0 && f[0] != null)
            {
                f[0].speed = 116; f[0].shotPower = 120; f[0].shotAccuracy = 118; f[0].checking = 114;
                f[0].skaterSize = Data.SkaterSize.Big;
                GiveTalentToPlayer(f[0], "Sado Maso");
                GiveTalentToPlayer(f[0], "Enraged");
                GiveTalentToPlayer(f[0], "Scrambled");
                GiveTalentToPlayer(f[0], "Quick Draw (Level 2)");
                GiveTalentToPlayer(f[0], "Curve Ball (Level 2)");
            }
            // RW: Teuvo Teravainen
            if (f.Count > 1 && f[1] != null)
            {
                f[1].speed = 118; f[1].shotPower = 115; f[1].shotAccuracy = 120; f[1].checking = 92;
                f[1].skaterSize = Data.SkaterSize.Medium;
                GiveTalentToPlayer(f[1], "Sado Maso");
                GiveTalentToPlayer(f[1], "Express Delivery (Level 2)");
                GiveTalentToPlayer(f[1], "Flawless Feeder (Level 2)");
            }
            // C: Connor Bedard (STAR)
            if (f.Count > 2 && f[2] != null)
            {
                f[2].speed = 122; f[2].shotPower = 122; f[2].shotAccuracy = 128; f[2].checking = 84;
                f[2].skaterSize = Data.SkaterSize.Small;
                GiveTalentToPlayer(f[2], "Sado Maso");
                GiveTalentToPlayer(f[2], "Trick Shot (Level 2)");
                GiveTalentToPlayer(f[2], "Crit Boost");
                GiveTalentToPlayer(f[2], "Curve Ball (Level 2)");
                GiveTalentToPlayer(f[2], "Puck Rocket");
                GiveTalentToPlayer(f[2], "Blue Line Boost (Level 2)");
                GiveTalentToPlayer(f[2], "Cherry Picker");
                GiveTalentToPlayer(f[2], "Anchor");
                GiveTalentToPlayer(f[2], "XRay Shot (Level 2)");
            }
            // LD: Artyom Levshunov
            if (f.Count > 3 && f[3] != null)
            {
                f[3].speed = 120; f[3].shotPower = 116; f[3].shotAccuracy = 115; f[3].checking = 110;
                f[3].skaterSize = Data.SkaterSize.Big;
                GiveTalentToPlayer(f[3], "Sado Maso");
                GiveTalentToPlayer(f[3], "Blue Line Boost (Level 2)");
                GiveTalentToPlayer(f[3], "Charge Shot");
                GiveTalentToPlayer(f[3], "knightAI");
                GiveTalentToPlayer(f[3], "magnetInterception");
            }
            // RD: Alex Vlasic (BERZERKER)
            if (f.Count > 4 && f[4] != null)
            {
                f[4].speed = 120; f[4].shotPower = 110; f[4].shotAccuracy = 106; f[4].checking = 120;
                f[4].skaterSize = Data.SkaterSize.ExtraExtraBig;
                GiveTalentToPlayer(f[4], "Sado Maso");
                GiveTalentToPlayer(f[4], "berserk");
                GiveTalentToPlayer(f[4], "Spiked Armor");
                GiveTalentToPlayer(f[4], "Chexplosion");
                GiveTalentToPlayer(f[4], "Built Different");
            }
        }
        if (team.goalie != null)
        {
            var g = team.goalie;
            g.skill = 82; g.catchingSkill = 85; g.gloveSkill = 88; g.blockerSkill = 82;
            g.fiveHoleSkill = 80; g.standingSpeed = 65; g.butterflySpeed = 65;
            g.controlSkill = 55; g.recoverySkill = 58; g.passPower = 52;
            g.shotPower = 48; g.pokecheckSkill = 46; g.depth = 56; g.passReadSkill = 0.55f;
            GiveGoalieTalent(g, "Goalie Pass Rebound");
            GiveGoalieTalent(g, "Goalie Pass Proepl");
            // 3 random goalie talents (no dupes, excluding the 2 already given)
            var goaliePool = new[] { "Goalie Assist", "Goalie Dance", "Goalie Enrage First30Sec", "Goalie Enrage Last30Sec", "Goalie Enraged On Breakaway", "Goalie Enraged On Goal", "Goalie Enraged On Shot", "Goalie Fart", "Goalie Headshot", "Goalie Speed Talent", "Goalie Throw Stick", "Crease Clearer", "Always Catch Pucks" };
            var grng = new System.Random();
            var gUsed = new HashSet<int>();
            for (int gt = 0; gt < 3; gt++)
            {
                int idx;
                do { idx = grng.Next(goaliePool.Length); } while (gUsed.Contains(idx));
                gUsed.Add(idx);
                GiveGoalieTalent(g, goaliePool[idx]);
                Plugin.Log.LogInfo($"  [Hawks] Goalie got random talent: {goaliePool[idx]}");
            }
        }
    }

    private static void RemixTycoons(TeamData team, int round)
    {
        SwapToNHLTeam(team, "Washington", "Washington Capitals", "Washington",
            new[] { "Alex Ovechkin", "Tom Wilson", "Dylan Strome", "Jakob Chychrun", "Matt Roy" },
            "Logan Thompson", null,
            new[] { "cushioned:2", "bubble_wrap", "frog_on_faceoff" });

        var f = team.forwards;
        if (f != null)
        {
            // LW: Alex Ovechkin
            if (f.Count > 0 && f[0] != null)
            {
                f[0].speed = 110; f[0].shotPower = 146; f[0].shotAccuracy = 142; f[0].checking = 118;
                f[0].skaterSize = Data.SkaterSize.ExtraBig;
                GiveTalentToPlayer(f[0], "The Howitzer");
                GiveTalentToPlayer(f[0], "Charge Shot (Level 2)");
                GiveTalentToPlayer(f[0], "Sonic Slap (Level 2)");
                GiveTalentToPlayer(f[0], "Deadzone");
                GiveTalentToPlayer(f[0], "Quick Draw (Level 2)");
                GiveTalentToPlayer(f[0], "Hidden Ace");
                GiveTalentToPlayer(f[0], "Avenge Me! (Level 2)");
                GiveTalentToPlayer(f[0], "Bouncy McBounce");
                GiveTalentToPlayer(f[0], "Rebound Magnet (Level 2)");
                GiveTalentToPlayer(f[0], "Fast Rebound");
            }
            // RW: Tom Wilson
            if (f.Count > 1 && f[1] != null)
            {
                f[1].speed = 112; f[1].shotPower = 122; f[1].shotAccuracy = 116; f[1].checking = 128;
                f[1].skaterSize = Data.SkaterSize.ExtraBig;
                GiveTalentToPlayer(f[1], "Onepunch");
                GiveTalentToPlayer(f[1], "marauder");
                GiveTalentToPlayer(f[1], "Enraged");
                GiveTalentToPlayer(f[1], "Porcelain Hammer");
                GiveTalentToPlayer(f[1], "Explosion On Landing");
            }
            // C: Dylan Strome
            if (f.Count > 2 && f[2] != null)
            {
                f[2].speed = 110; f[2].shotPower = 120; f[2].shotAccuracy = 118; f[2].checking = 106;
                f[2].skaterSize = Data.SkaterSize.ExtraBig;
                GiveTalentToPlayer(f[2], "Flawless Feeder (Level 2)");
                GiveTalentToPlayer(f[2], "Power Transfer");
            }
            // LD: Jakob Chychrun
            if (f.Count > 3 && f[3] != null)
            {
                f[3].speed = 112; f[3].shotPower = 122; f[3].shotAccuracy = 118; f[3].checking = 114;
                f[3].skaterSize = Data.SkaterSize.ExtraBig;
                GiveTalentToPlayer(f[3], "Point Sniper (Level 2)");
                GiveTalentToPlayer(f[3], "Slapshot Slowmo");
            }
            // RD: Matt Roy
            if (f.Count > 4 && f[4] != null)
            {
                f[4].speed = 112; f[4].shotPower = 110; f[4].shotAccuracy = 110; f[4].checking = 112;
                f[4].skaterSize = Data.SkaterSize.Medium;
                GiveTalentToPlayer(f[4], "Human Shield");
                GiveTalentToPlayer(f[4], "Defensive Deflect (Level 2)");
            }

            // Copy Washington jerseys/skins onto line 2 players
            for (int li2 = 5; li2 < f.Count && li2 <= 9; li2++)
            {
                if (f[li2] == null || f[0] == null) continue;
                var src = f[li2 - 5]; // copy visuals from matching line 1 player
                f[li2].headSkin = src.headSkin;
                f[li2].numberSkin = src.numberSkin;
                f[li2].logoSkin = src.logoSkin;
                f[li2].helmetSkin = src.helmetSkin;
                f[li2].helmetAwaySkin = src.helmetAwaySkin;
                f[li2].stickSkin = src.stickSkin;
                f[li2].bodySkin = src.bodySkin;
                f[li2].bicepSkin = src.bicepSkin;
                f[li2].gloveSkin = src.gloveSkin;
                f[li2].skateSkin = src.skateSkin;
                f[li2].pantsSkin = src.pantsSkin;
                f[li2].bodyAwaySkin = src.bodyAwaySkin;
                f[li2].colorSchemes = src.colorSchemes;
            }

            // === LINE 2 (indices 5-9) ===
            // LW: Connor McMichael
            if (f.Count > 5 && f[5] != null)
            {
                f[5].firstName = "Connor"; f[5].lastName = "McMichael";
                f[5].speed = 100; f[5].shotPower = 110; f[5].shotAccuracy = 108; f[5].checking = 96;
                f[5].skaterSize = Data.SkaterSize.Medium;
                GiveTalentToPlayer(f[5], "Cherry Picker");
                GiveTalentToPlayer(f[5], "Puck Rocket");
            }
            // RW: Andrew Mangiapane
            if (f.Count > 6 && f[6] != null)
            {
                f[6].firstName = "Andrew"; f[6].lastName = "Mangiapane";
                f[6].speed = 104; f[6].shotPower = 106; f[6].shotAccuracy = 104; f[6].checking = 100;
                f[6].skaterSize = Data.SkaterSize.Medium;
                GiveTalentToPlayer(f[6], "Speed Transfer");
                GiveTalentToPlayer(f[6], "Crit Boost");
            }
            // C: Pierre-Luc Dubois
            if (f.Count > 7 && f[7] != null)
            {
                f[7].firstName = "Pierre-Luc"; f[7].lastName = "Dubois";
                f[7].speed = 98; f[7].shotPower = 112; f[7].shotAccuracy = 108; f[7].checking = 108;
                f[7].skaterSize = Data.SkaterSize.ExtraBig;
                GiveTalentToPlayer(f[7], "Flawless Feeder");
                GiveTalentToPlayer(f[7], "Enraged");
            }
            // LD: Rasmus Sandin
            if (f.Count > 8 && f[8] != null)
            {
                f[8].firstName = "Rasmus"; f[8].lastName = "Sandin";
                f[8].speed = 106; f[8].shotPower = 104; f[8].shotAccuracy = 106; f[8].checking = 96;
                f[8].skaterSize = Data.SkaterSize.Medium;
                GiveTalentToPlayer(f[8], "Blue Line Boost");
                GiveTalentToPlayer(f[8], "Board Bumper");
            }
            // RD: John Carlson
            if (f.Count > 9 && f[9] != null)
            {
                f[9].firstName = "John"; f[9].lastName = "Carlson";
                f[9].speed = 96; f[9].shotPower = 114; f[9].shotAccuracy = 112; f[9].checking = 102;
                f[9].skaterSize = Data.SkaterSize.Big;
                GiveTalentToPlayer(f[9], "Point Sniper");
                GiveTalentToPlayer(f[9], "Slapshot Slowmo");
            }

            // Update Lineup entries so the game knows which players are on each line
            if (team.lines != null)
            {
                for (int li = 0; li < team.lines.Count; li++)
                {
                    var lineup = team.lines[li];
                    if (lineup == null) continue;
                    int offset = li * 5; // line 0 = forwards 0-4, line 1 = forwards 5-9
                    if (f.Count > offset + 0 && f[offset + 0] != null) lineup.leftWinger = f[offset + 0].id;
                    if (f.Count > offset + 1 && f[offset + 1] != null) lineup.rightWinger = f[offset + 1].id;
                    if (f.Count > offset + 2 && f[offset + 2] != null) lineup.center = f[offset + 2].id;
                    if (f.Count > offset + 3 && f[offset + 3] != null) lineup.leftDefensemen = f[offset + 3].id;
                    if (f.Count > offset + 4 && f[offset + 4] != null) lineup.rightDefensemen = f[offset + 4].id;
                    Plugin.Log.LogInfo($"  [Lineup] Line {li+1}: LW={lineup.leftWinger} RW={lineup.rightWinger} C={lineup.center} LD={lineup.leftDefensemen} RD={lineup.rightDefensemen}");
                }
            }
        }
        if (team.goalie != null)
        {
            var g = team.goalie;
            g.skill = 96; g.catchingSkill = 98; g.gloveSkill = 98; g.blockerSkill = 94;
            g.fiveHoleSkill = 94; g.standingSpeed = 68; g.butterflySpeed = 66;
            g.controlSkill = 66; g.recoverySkill = 68; g.passPower = 56;
            g.shotPower = 50; g.pokecheckSkill = 52; g.depth = 60; g.passReadSkill = 0.62f;
            GiveGoalieTalent(g, "Always Catch Pucks");
            GiveGoalieTalent(g, "Goalie Enraged On Shot");
            GiveGoalieTalent(g, "Goalie Headshot");
            GiveGoalieTalent(g, "Goalie Throw Stick");
        }
    }

    // ========== ACT 3 BOSS ==========

    private static void RemixGolfers(TeamData team, int round)
    {
        SwapToNHLTeam(team, "Colorado", "Colorado Avalanche", "Colorado",
            new[] { "Gabriel Landeskog", "Martin Necas", "Nathan MacKinnon", "Devon Toews", "Cale Makar" },
            "Mackenzie Blackwood", null,
            new[] { "crit_shot_knockout", "critical_mass", "voodoodoll:2", "briefcase:2", "shrink_serum_opponent", "crit_shot_frog", "double_incision:2", "cushioned:2", "bubble_wrap:2", "greater_cooldown" });

        var f = team.forwards;
        if (f != null)
        {
            // LW: Gabriel Landeskog (IMMUNE)
            if (f.Count > 0 && f[0] != null)
            {
                f[0].speed = 210; f[0].shotPower = 220; f[0].shotAccuracy = 216; f[0].checking = 220;
                f[0].skaterSize = Data.SkaterSize.ExtraBig;
                GiveTalentToPlayer(f[0], "Built Different");
                GiveTalentToPlayer(f[0], "Onepunch");
                GiveTalentToPlayer(f[0], "Chexplosion");
                GiveTalentToPlayer(f[0], "marauder");
                GiveTalentToPlayer(f[0], "Homewrecker");
                GiveTalentToPlayer(f[0], "Enraged");
                GiveTalentToPlayer(f[0], "Avenge Me! (Level 2)");
                GiveTalentToPlayer(f[0], "Spiked Armor");
            }
            // RW: Martin Necas
            if (f.Count > 1 && f[1] != null)
            {
                f[1].speed = 220; f[1].shotPower = 218; f[1].shotAccuracy = 222; f[1].checking = 196;
                f[1].skaterSize = Data.SkaterSize.Medium;
                GiveTalentToPlayer(f[1], "Trick Shot (Level 2)");
                GiveTalentToPlayer(f[1], "Curve Ball (Level 2)");
                GiveTalentToPlayer(f[1], "Crit Boost");
                GiveTalentToPlayer(f[1], "Escalating Crit");
                GiveTalentToPlayer(f[1], "Puck Rocket");
                GiveTalentToPlayer(f[1], "tornadoAI");
                GiveTalentToPlayer(f[1], "Cherry Picker");
            }
            // C: Nathan MacKinnon (GOD MODE - IMMUNE)
            if (f.Count > 2 && f[2] != null)
            {
                f[2].speed = 260; f[2].shotPower = 250; f[2].shotAccuracy = 255; f[2].checking = 235;
                f[2].skaterSize = Data.SkaterSize.Medium;
                GiveTalentToPlayer(f[2], "Built Different");
                GiveTalentToPlayer(f[2], "Hidden Ace");
                GiveTalentToPlayer(f[2], "Flawless Feeder (Level 2)");
                GiveTalentToPlayer(f[2], "Power Transfer");
                GiveTalentToPlayer(f[2], "XRay Shot (Level 2)");
                GiveTalentToPlayer(f[2], "Charge Shot (Level 2)");
                GiveTalentToPlayer(f[2], "Bouncy McBounce");
                GiveTalentToPlayer(f[2], "Rebound Magnet (Level 2)");
                GiveTalentToPlayer(f[2], "Fast Rebound");
                GiveTalentToPlayer(f[2], "Anchor");
            }
            // LD: Devon Toews (IMMUNE)
            if (f.Count > 3 && f[3] != null)
            {
                f[3].speed = 214; f[3].shotPower = 212; f[3].shotAccuracy = 212; f[3].checking = 210;
                f[3].skaterSize = Data.SkaterSize.ExtraBig;
                GiveTalentToPlayer(f[3], "Built Different");
                GiveTalentToPlayer(f[3], "Human Shield");
                GiveTalentToPlayer(f[3], "Velcro");
                GiveTalentToPlayer(f[3], "Defensive Deflect (Level 2)");
                GiveTalentToPlayer(f[3], "knightAI");
                GiveTalentToPlayer(f[3], "Sonic Interception");
                GiveTalentToPlayer(f[3], "En Garde!");
                GiveTalentToPlayer(f[3], "Triple Twig");
                GiveTalentToPlayer(f[3], "Fury Stick (Level 2)");
                GiveTalentToPlayer(f[3], "Porcelain Hammer");
                GiveTalentToPlayer(f[3], "Onepunch");
                SetPlayerAbility(f[3], "throwingStick");
            }
            // RD: Cale Makar
            if (f.Count > 4 && f[4] != null)
            {
                f[4].speed = 240; f[4].shotPower = 236; f[4].shotAccuracy = 238; f[4].checking = 218;
                f[4].skaterSize = Data.SkaterSize.Medium;
                GiveTalentToPlayer(f[4], "Blue Line Boost (Level 2)");
                GiveTalentToPlayer(f[4], "Point Sniper (Level 2)");
                GiveTalentToPlayer(f[4], "Slapshot Slowmo");
                GiveTalentToPlayer(f[4], "Sonic Slap (Level 2)");
                GiveTalentToPlayer(f[4], "Board Bumper");
                GiveTalentToPlayer(f[4], "Puckless Rocket");
                GiveTalentToPlayer(f[4], "princeAI");
                GiveTalentToPlayer(f[4], "Sword (Level 2)");
                GiveTalentToPlayer(f[4], "Triple Twig");
                GiveTalentToPlayer(f[4], "Fury Stick (Level 2)");
                GiveTalentToPlayer(f[4], "Porcelain Hammer");
                GiveTalentToPlayer(f[4], "Explosion On Landing");
                SetPlayerAbility(f[4], "throwingStick");
            }
        }
        if (team.goalie != null)
        {
            var g = team.goalie;
            g.skill = 120; g.catchingSkill = 122; g.gloveSkill = 124; g.blockerSkill = 118;
            g.fiveHoleSkill = 116; g.standingSpeed = 75; g.butterflySpeed = 73;
            g.controlSkill = 120; g.recoverySkill = 122; g.passPower = 80;
            g.shotPower = 70; g.pokecheckSkill = 72; g.depth = 80; g.passReadSkill = 0.90f;
            GiveGoalieTalent(g, "Always Catch Pucks");
            GiveGoalieTalent(g, "Crease Clearer");
            GiveGoalieTalent(g, "Goalie Headshot");
            GiveGoalieTalent(g, "Goalie Throw Stick");
            GiveGoalieTalent(g, "Goalie Enraged On Shot");
            GiveGoalieTalent(g, "Goalie Pass Proepl");
            GiveGoalieTalent(g, "Goalie Speed Talent");
            GiveGoalieTalent(g, "Goalie Dance");
        }
    }

    // ========== GENERIC FALLBACK ==========

    private static void RemixGeneric(TeamData team)
    {
        team.teamName = "Remix " + team.teamName;
        Plugin.Log.LogInfo($"[Remix] === GENERIC: {team.teamName} ===");
        SetColors(team.homeColors, new Color(0f, 1f, 1f), new Color(1f, 0f, 1f), new Color(1f, 1f, 1f));
        SetColors(team.awayColors, new Color(1f, 0f, 1f), new Color(0f, 1f, 1f), new Color(1f, 1f, 1f));
    }
}

[HarmonyPatch(typeof(EliteMapNode), nameof(EliteMapNode.LaunchMatch))]
public static class PatchEliteLaunchMatch
{
    [HarmonyPrefix]
    public static void Prefix(EliteMapNode __instance)
    {
        try
        {
            var opponent = __instance.opponent;
            if (opponent == null) return;

            if (Plugin.IsRemixed)
            {
                PatchBossLaunchMatch.RemixTeam(opponent, Plugin.CurrentRemixBoost);
                Plugin.Log.LogInfo($"[Remix] Elite '{opponent.teamName}' remixed with +{Plugin.CurrentRemixBoost} stats");
            }
        }
        catch (Exception ex) { Plugin.Log.LogError($"[Remix] EliteLaunch: {ex}"); }
    }

}

// ============================================================
// Log all relics, abilities, talents from repositories
// ============================================================
[HarmonyPatch(typeof(Team), nameof(Team.Initialize))]
public static class LogRepositories
{
    [HarmonyPostfix]
    public static void Postfix(Team __instance, TeamData teamData)
    {
        if (!Plugin.ReposLogged)
        {
            Plugin.ReposLogged = true;
        }

        if (!Plugin.DumpData) return;
        Plugin.Log.LogInfo("[Dump] Generating data dumps (set 'Dump Data = no' in config to skip)...");

        var sb = new StringBuilder();
        sb.AppendLine("=== TAPE TO TAPE - GAME DATA DUMP ===");
        sb.AppendLine($"Generated: {DateTime.Now}");
        sb.AppendLine();

        // Dump all league teams (NHL parody teams not in campaign)
        try
        {
            var ptAll = UnityEngine.Resources.FindObjectsOfTypeAll<PlayableTeams>();
            var playableTeams = ptAll != null && ptAll.Length > 0 ? ptAll[0] : null;
            if (playableTeams != null)
            {
                sb.AppendLine("========== LEAGUE TEAMS (NHL parody teams) ==========");
                var league = playableTeams.leagueTeams;
                if (league != null)
                {
                    for (int i = 0; i < league.Count; i++)
                    {
                        var t = league[i];
                        if (t == null) continue;
                        sb.AppendLine($"  Team: '{t.teamName}' (id={t.id})");
                        var fwds = t.forwards;
                        if (fwds != null)
                            for (int j = 0; j < fwds.Count; j++)
                            {
                                var f = fwds[j];
                                if (f == null) continue;
                                sb.AppendLine($"    Forward: '{f.firstName} {f.lastName}' SP={f.shotPower} SPD={f.speed} CHK={f.checking} ACC={f.shotAccuracy}");
                            }
                        if (t.goalie != null)
                            sb.AppendLine($"    Goalie: '{t.goalie.firstName} {t.goalie.lastName}' skill={t.goalie.skill}");
                    }
                }

                sb.AppendLine();
                sb.AppendLine("========== CAMPAIGN EXCLUSIVE TEAMS ==========");
                var campExcl = playableTeams.campaignExclusiveTeams;
                if (campExcl != null)
                {
                    for (int i = 0; i < campExcl.Count; i++)
                    {
                        var t = campExcl[i];
                        if (t == null) continue;
                        sb.AppendLine($"  Team: '{t.teamName}' (id={t.id})");
                        var fwds = t.forwards;
                        if (fwds != null)
                            for (int j = 0; j < fwds.Count; j++)
                            {
                                var f = fwds[j];
                                if (f == null) continue;
                                sb.AppendLine($"    Forward: '{f.firstName} {f.lastName}' SP={f.shotPower} SPD={f.speed} CHK={f.checking} ACC={f.shotAccuracy}");
                            }
                        if (t.goalie != null)
                            sb.AppendLine($"    Goalie: '{t.goalie.firstName} {t.goalie.lastName}' skill={t.goalie.skill}");
                    }
                }

                sb.AppendLine();
                sb.AppendLine("========== ALL CAMPAIGN TEAMS ==========");
                var campTeams = playableTeams.campaignTeams;
                if (campTeams != null)
                {
                    for (int i = 0; i < campTeams.Count; i++)
                    {
                        var t = campTeams[i];
                        if (t == null) continue;
                        sb.AppendLine($"  Team: '{t.teamName}' (id={t.id})");
                    }
                }
                sb.AppendLine();
            }
            else sb.AppendLine("PlayableTeams NOT FOUND");
        }
        catch (Exception ex) { sb.AppendLine($"PlayableTeams error: {ex.Message}"); }

        // Dump ALL TeamData objects in memory (campaign + league + everything)
        try
        {
            var allTeams = UnityEngine.Resources.FindObjectsOfTypeAll<TeamData>();
            sb.AppendLine($"========== ALL TEAM DATA IN MEMORY ({allTeams.Length}) ==========");
            for (int i = 0; i < allTeams.Length; i++)
            {
                var t = allTeams[i];
                if (t == null) continue;
                bool hasLogo = t.logo != null;
                bool hasBigLogo = t.alternateBigLogo != null;
                sb.AppendLine($"  '{t.teamName}' id={t.id} city={t.city} hasLogo={hasLogo} hasBigLogo={hasBigLogo}");
            }
            sb.AppendLine();
        }
        catch (Exception ex) { sb.AppendLine($"TeamData dump error: {ex.Message}"); }

        // Try to get repositories via Resources.FindObjectsOfTypeAll
        try
        {
            var relicRepos = UnityEngine.Resources.FindObjectsOfTypeAll<RelicRepository>();
            var relicRepo = relicRepos != null && relicRepos.Length > 0 ? relicRepos[0] : null;
            if (relicRepo != null)
            {
                sb.AppendLine("========== RELICS ==========");
                DumpRelicList(sb, relicRepo.offensiveRelics, "Offensive");
                DumpRelicList(sb, relicRepo.defensiveRelics, "Defensive");
                DumpRelicList(sb, relicRepo.utilityRelics, "Utility");
                DumpRelicList(sb, relicRepo.speedRelics, "Speed");
                DumpRelicList(sb, relicRepo.checkingRelics, "Checking");
                DumpRelicList(sb, relicRepo.powerRelics, "Power");
                DumpRelicList(sb, relicRepo.accuracyRelics, "Accuracy");
                DumpRelicList(sb, relicRepo.chaosRelics, "Chaos");
                DumpRelicList(sb, relicRepo.bossRelics, "Boss");
                DumpRelicList(sb, relicRepo.goalieRelics, "Goalie");
                DumpRelicList(sb, relicRepo.coachRelics, "Coach");
                sb.AppendLine();
            }
            else sb.AppendLine("RelicRepository NOT FOUND");
        }
        catch (Exception ex) { sb.AppendLine($"Relic error: {ex.Message}"); }

        try
        {
            var abilityRepos = UnityEngine.Resources.FindObjectsOfTypeAll<AbilityRepository>();
            var abilityRepo = abilityRepos != null && abilityRepos.Length > 0 ? abilityRepos[0] : null;
            if (abilityRepo != null)
            {
                sb.AppendLine("========== ABILITIES ==========");
                DumpAbilityList(sb, abilityRepo.abilities, "All");
                sb.AppendLine();
            }
            else sb.AppendLine("AbilityRepository NOT FOUND");
        }
        catch (Exception ex) { sb.AppendLine($"Ability error: {ex.Message}"); }

        try
        {
            var talentRepos = UnityEngine.Resources.FindObjectsOfTypeAll<TalentRepository>();
            var talentRepo = talentRepos != null && talentRepos.Length > 0 ? talentRepos[0] : null;
            if (talentRepo != null)
            {
                sb.AppendLine("========== TALENTS ==========");
                DumpTalentList(sb, talentRepo.talents, "All");
                sb.AppendLine();
            }
            else sb.AppendLine("TalentRepository NOT FOUND");
        }
        catch (Exception ex) { sb.AppendLine($"Talent error: {ex.Message}"); }

        // Write to file
        string path = Path.Combine(BepInEx.Paths.PluginPath, "game_data_dump.txt");
        try
        {
            File.WriteAllText(path, sb.ToString());
            Plugin.Log.LogInfo($"[Repo] Game data dumped to {path}");
        }
        catch (Exception ex) { Plugin.Log.LogError($"[Repo] File write error: {ex}"); }

        // Write 3 separate files
        try
        {
            WriteSeparateFiles();
        }
        catch (Exception ex) { Plugin.Log.LogError($"[Dump] WriteSeparateFiles failed: {ex}"); }

        // Dump all player look data from parody teams
        try
        {
            DumpPlayerLooks();
        }
        catch (Exception ex) { Plugin.Log.LogError($"[Dump] DumpPlayerLooks failed: {ex}"); }

    }

    private static void DumpPlayerLooks()
    {
        string basePath = BepInEx.Paths.PluginPath;

        // === 1. DUMP ALL AVAILABLE SKIN OPTIONS ===
        try
        {
            var sbSkins = new StringBuilder();
            sbSkins.AppendLine("=== AVAILABLE SKIN OPTIONS ===");
            sbSkins.AppendLine($"Generated: {DateTime.Now}");
            sbSkins.AppendLine("Use these values in config.json look sections.");
            sbSkins.AppendLine();

            var allForwards = UnityEngine.Resources.FindObjectsOfTypeAll<ForwardData>();
            var headSkins = new HashSet<string>();
            var bodySkins = new HashSet<string>();
            var helmetSkins = new HashSet<string>();
            var stickSkins = new HashSet<string>();
            var glassesSkins = new HashSet<string>();

            if (allForwards != null)
            {
                foreach (var f in allForwards)
                {
                    if (f == null) continue;
                    if (!string.IsNullOrEmpty(f.headSkin)) headSkins.Add(f.headSkin);
                    if (!string.IsNullOrEmpty(f.bodySkin)) bodySkins.Add(f.bodySkin);
                    if (!string.IsNullOrEmpty(f.helmetSkin)) helmetSkins.Add(f.helmetSkin);
                    if (!string.IsNullOrEmpty(f.stickSkin)) stickSkins.Add(f.stickSkin);
                    if (!string.IsNullOrEmpty(f.glassesSkin)) glassesSkins.Add(f.glassesSkin);
                    // Try to read the private _headSkins list via reflection
                    try
                    {
                        var field = f.GetType().GetField("_headSkins", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (field != null)
                        {
                            var list = field.GetValue(f) as Il2CppSystem.Collections.Generic.List<string>;
                            if (list != null)
                                for (int i = 0; i < list.Count; i++)
                                    if (!string.IsNullOrEmpty(list[i])) headSkins.Add(list[i]);
                        }
                    } catch {}
                    try
                    {
                        var field = f.GetType().GetField("_bodySkins", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (field != null)
                        {
                            var list = field.GetValue(f) as Il2CppSystem.Collections.Generic.List<string>;
                            if (list != null)
                                for (int i = 0; i < list.Count; i++)
                                    if (!string.IsNullOrEmpty(list[i])) bodySkins.Add(list[i]);
                        }
                    } catch {}
                    try
                    {
                        var field = f.GetType().GetField("_helmetSkins", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (field != null)
                        {
                            var list = field.GetValue(f) as Il2CppSystem.Collections.Generic.List<string>;
                            if (list != null)
                                for (int i = 0; i < list.Count; i++)
                                    if (!string.IsNullOrEmpty(list[i])) helmetSkins.Add(list[i]);
                        }
                    } catch {}
                    try
                    {
                        var field = f.GetType().GetField("_stickSkins", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (field != null)
                        {
                            var list = field.GetValue(f) as Il2CppSystem.Collections.Generic.List<string>;
                            if (list != null)
                                for (int i = 0; i < list.Count; i++)
                                    if (!string.IsNullOrEmpty(list[i])) stickSkins.Add(list[i]);
                        }
                    } catch {}
                }
            }

            sbSkins.AppendLine($"--- HEAD SKINS ({headSkins.Count}) ---");
            foreach (var s in headSkins) sbSkins.AppendLine($"  {s}");
            sbSkins.AppendLine();
            sbSkins.AppendLine($"--- BODY SKINS ({bodySkins.Count}) ---");
            foreach (var s in bodySkins) sbSkins.AppendLine($"  {s}");
            sbSkins.AppendLine();
            sbSkins.AppendLine($"--- HELMET SKINS ({helmetSkins.Count}) ---");
            foreach (var s in helmetSkins) sbSkins.AppendLine($"  {s}");
            sbSkins.AppendLine();
            sbSkins.AppendLine($"--- STICK SKINS ({stickSkins.Count}) ---");
            foreach (var s in stickSkins) sbSkins.AppendLine($"  {s}");
            sbSkins.AppendLine();
            if (glassesSkins.Count > 0)
            {
                sbSkins.AppendLine($"--- GLASSES SKINS ({glassesSkins.Count}) ---");
                foreach (var s in glassesSkins) sbSkins.AppendLine($"  {s}");
                sbSkins.AppendLine();
            }

            File.WriteAllText(Path.Combine(basePath, "ALL_SKIN_OPTIONS.txt"), sbSkins.ToString());
            Plugin.Log.LogInfo("[Dump] Skin options dumped to ALL_SKIN_OPTIONS.txt");
        }
        catch (Exception ex) { Plugin.Log.LogError($"[Dump] Skin options dump failed: {ex}"); }

        // === 2. DUMP ALL TEAMS WITH FULL DATA (looks, stats, everything) ===
        try
        {
            var sbTeams = new StringBuilder();
            sbTeams.AppendLine("=== ALL TEAMS — FULL DATA ===");
            sbTeams.AppendLine($"Generated: {DateTime.Now}");
            sbTeams.AppendLine("Use team names with \"importTeam\" in config.json to import everything.");
            sbTeams.AppendLine();

            var allTeams = UnityEngine.Resources.FindObjectsOfTypeAll<TeamData>();
            if (allTeams != null)
            {
                foreach (var t in allTeams)
                {
                    if (t == null || string.IsNullOrEmpty(t.teamName)) continue;
                    DumpTeamFull(sbTeams, t);
                }
            }

            File.WriteAllText(Path.Combine(basePath, "ALL_TEAMS_FULL.txt"), sbTeams.ToString());
            Plugin.Log.LogInfo("[Dump] Full team data dumped to ALL_TEAMS_FULL.txt");
        }
        catch (Exception ex) { Plugin.Log.LogError($"[Dump] Full team dump failed: {ex}"); }

        // === 3. DUMP CUSTOM TEAMS ===
        try
        {
            var sbCustom = new StringBuilder();
            sbCustom.AppendLine("=== CUSTOM TEAMS ===");
            sbCustom.AppendLine($"Generated: {DateTime.Now}");
            sbCustom.AppendLine("Teams created in the player creator.");
            sbCustom.AppendLine("Use these names with \"importTeam\" in config.json.");
            sbCustom.AppendLine();

            var playableTeams = UnityEngine.Resources.FindObjectsOfTypeAll<PlayableTeams>();
            if (playableTeams != null && playableTeams.Length > 0)
            {
                var pt = playableTeams[0];

                sbCustom.AppendLine("--- LEAGUE TEAMS (parody NHL) ---");
                if (pt.leagueTeams != null)
                    for (int i = 0; i < pt.leagueTeams.Count; i++)
                        if (pt.leagueTeams[i] != null)
                            sbCustom.AppendLine($"  \"{pt.leagueTeams[i].teamName}\"");
                sbCustom.AppendLine();

                sbCustom.AppendLine("--- CAMPAIGN TEAMS ---");
                if (pt.campaignTeams != null)
                    for (int i = 0; i < pt.campaignTeams.Count; i++)
                        if (pt.campaignTeams[i] != null)
                            sbCustom.AppendLine($"  \"{pt.campaignTeams[i].teamName}\"");
                sbCustom.AppendLine();

                sbCustom.AppendLine("--- CAMPAIGN EXCLUSIVE TEAMS ---");
                if (pt.campaignExclusiveTeams != null)
                    for (int i = 0; i < pt.campaignExclusiveTeams.Count; i++)
                        if (pt.campaignExclusiveTeams[i] != null)
                            sbCustom.AppendLine($"  \"{pt.campaignExclusiveTeams[i].teamName}\"");
                sbCustom.AppendLine();
            }

            // Find custom teams via ITeamRepository
            var teamRepos = UnityEngine.Resources.FindObjectsOfTypeAll<PlayableTeams>();
            // Also search for any TeamData that might be custom
            var allTeams = UnityEngine.Resources.FindObjectsOfTypeAll<TeamData>();
            if (allTeams != null)
            {
                sbCustom.AppendLine("--- ALL TEAM DATA IN MEMORY ---");
                sbCustom.AppendLine("(includes custom-created teams)");
                foreach (var t in allTeams)
                {
                    if (t == null || string.IsNullOrEmpty(t.teamName)) continue;
                    bool hasLogo = t.logo != null;
                    int fwdCount = t.forwards?.Count ?? 0;
                    sbCustom.AppendLine($"  \"{t.teamName}\" — {fwdCount} forwards, hasLogo={hasLogo}");
                }
            }

            File.WriteAllText(Path.Combine(basePath, "ALL_AVAILABLE_TEAMS.txt"), sbCustom.ToString());
            Plugin.Log.LogInfo("[Dump] Available teams dumped to ALL_AVAILABLE_TEAMS.txt");
        }
        catch (Exception ex) { Plugin.Log.LogError($"[Dump] Custom teams dump failed: {ex}"); }
    }

    private static void DumpTeamFull(StringBuilder sb, TeamData t)
    {
        sb.AppendLine($"========== {t.teamName} ==========");
        sb.AppendLine($"  city: \"{t.city ?? ""}\"");
        sb.AppendLine($"  nickname: \"{t.nickname ?? ""}\"");
        sb.AppendLine($"  id: \"{t.id ?? ""}\"");
        sb.AppendLine($"  hasLogo: {t.logo != null}");
        sb.AppendLine($"  hasBigLogo: {t.alternateBigLogo != null}");

        // Relics
        if (t.relics != null && t.relics.Count > 0)
        {
            sb.Append("  relics: [");
            for (int i = 0; i < t.relics.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append($"\"{t.relics[i]?.name ?? "null"}\"");
            }
            sb.AppendLine("]");
        }

        // Forwards
        var fwds = t.forwards;
        if (fwds != null)
        {
            for (int i = 0; i < fwds.Count; i++)
            {
                var f = fwds[i];
                if (f == null) continue;
                string pos = i == 0 ? "LW" : i == 1 ? "RW" : i == 2 ? "C" : i == 3 ? "LD" : i == 4 ? "RD" : $"Line2[{i-5}]";
                sb.AppendLine($"  {pos}: {f.firstName} {f.lastName}");
                sb.AppendLine($"    speed: {f.speed}, shotPower: {f.shotPower}, shotAccuracy: {f.shotAccuracy}, checking: {f.checking}");
                sb.AppendLine($"    size: {f.skaterSize}, type: {f.defaultSkaterType}");
                sb.AppendLine($"    isLefty: {f.isLefty}, isBlack: {f.isBlack}");
                sb.AppendLine($"    headSkin: \"{f.headSkin ?? ""}\"");
                sb.AppendLine($"    bodySkin: \"{f.bodySkin ?? ""}\"");
                sb.AppendLine($"    helmetSkin: \"{f.helmetSkin ?? ""}\"");
                sb.AppendLine($"    stickSkin: \"{f.stickSkin ?? ""}\"");
                sb.AppendLine($"    bicepSkin: \"{f.bicepSkin ?? ""}\"");
                sb.AppendLine($"    gloveSkin: \"{f.gloveSkin ?? ""}\"");
                sb.AppendLine($"    pantsSkin: \"{f.pantsSkin ?? ""}\"");
                sb.AppendLine($"    skateSkin: \"{f.skateSkin ?? ""}\"");
                sb.AppendLine($"    bodyAwaySkin: \"{f.bodyAwaySkin ?? ""}\"");
                sb.AppendLine($"    helmetAwaySkin: \"{f.helmetAwaySkin ?? ""}\"");
                sb.AppendLine($"    numberSkin: \"{f.numberSkin ?? ""}\"");
                sb.AppendLine($"    logoSkin: \"{f.logoSkin ?? ""}\"");
                sb.AppendLine($"    glassesSkin: \"{f.glassesSkin ?? ""}\"");
                sb.AppendLine($"    sizeOffsetPercentage: {f.sizeOffsetPercentage}");
                if (f.ability != null)
                    sb.AppendLine($"    ability: \"{f.ability.name}\"");
                if (f.powerups != null && f.powerups.Count > 0)
                {
                    sb.Append("    talents: [");
                    for (int j = 0; j < f.powerups.Count; j++)
                    {
                        if (j > 0) sb.Append(", ");
                        sb.Append($"\"{f.powerups[j]?.name ?? "null"}\"");
                    }
                    sb.AppendLine("]");
                }
                sb.AppendLine();
            }
        }

        // Goalie
        var g = t.goalie;
        if (g != null)
        {
            sb.AppendLine($"  Goalie: {g.firstName} {g.lastName}");
            sb.AppendLine($"    skill: {g.skill}, catching: {g.catchingSkill}, glove: {g.gloveSkill}, blocker: {g.blockerSkill}");
            sb.AppendLine($"    fiveHole: {g.fiveHoleSkill}, standSpd: {g.standingSpeed}, buttSpd: {g.butterflySpeed}");
            sb.AppendLine($"    control: {g.controlSkill}, recovery: {g.recoverySkill}, passPower: {g.passPower}");
            sb.AppendLine($"    shotPower: {g.shotPower}, pokecheck: {g.pokecheckSkill}, depth: {g.depth}, passRead: {g.passReadSkill}");
            sb.AppendLine($"    headSkin: \"{g.headSkin ?? ""}\"");
            if (g.powerups != null && g.powerups.Count > 0)
            {
                sb.Append("    talents: [");
                for (int j = 0; j < g.powerups.Count; j++)
                {
                    if (j > 0) sb.Append(", ");
                    sb.Append($"\"{g.powerups[j]?.name ?? "null"}\"");
                }
                sb.AppendLine("]");
            }
        }
        sb.AppendLine();
    }

    private static string ResolveI2(string key)
    {
        if (string.IsNullOrEmpty(key)) return "";
        try
        {
            string result = LocalizationManager.GetTranslation(key, true, 0, true, false, null, null, true);
            if (!string.IsNullOrEmpty(result))
                return StripKeywordTags(result);
        }
        catch { }
        return "";
    }

    private static string StripKeywordTags(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        // Replace <keyword="...">Text</keyword> with just Text
        text = System.Text.RegularExpressions.Regex.Replace(text, @"<keyword=""[^""]*"">", "");
        text = text.Replace("</keyword>", "");
        // Strip any other XML-like tags
        text = System.Text.RegularExpressions.Regex.Replace(text, @"<[^>]+>", "");
        return text.Trim();
    }

    private static void DumpSingleRelic(StringBuilder sb, Rogue.Relic r)
    {
        string desc = "";
        try { desc = r.description ?? ""; } catch { }
        string statDesc = "";
        try { statDesc = r.statBonusDescription ?? ""; } catch { }
        string descText = ResolveI2(desc);
        string statText = ResolveI2(statDesc);
        string relicNameKey = "";
        if (!string.IsNullOrEmpty(desc) && desc.EndsWith("/description"))
            relicNameKey = desc.Replace("/description", "/name");
        string relicDisplayName = ResolveI2(relicNameKey);
        sb.AppendLine($"  [{r.relicName}] \"{(relicDisplayName != "" ? relicDisplayName : r.relicName)}\" (Lv{r.level}) Asset={r.name}");
        if (descText != "") sb.AppendLine($"    {descText}");
        if (statText != "") sb.AppendLine($"    Stats: {statText}");
        // Dump all public fields to find actual values
        try
        {
            var fields = r.GetIl2CppType().GetFields(Il2CppSystem.Reflection.BindingFlags.Public | Il2CppSystem.Reflection.BindingFlags.Instance);
            foreach (var field in fields)
            {
                string fname = field.Name;
                if (fname == "relicName" || fname == "description" || fname == "statBonusDescription" || fname == "id" || fname == "level" || fname == "name") continue;
                try
                {
                    var val = field.GetValue(r);
                    string vs = val?.ToString() ?? "null";
                    if (vs != "0" && vs != "" && vs != "null" && vs != "False" && vs != "0.0")
                        sb.AppendLine($"    {fname}={vs}");
                }
                catch { }
            }
        }
        catch { }
        sb.AppendLine();
    }

    private static void DumpRelicList(StringBuilder sb, Il2CppSystem.Collections.Generic.List<Rogue.Relic> list, string category)
    {
        if (list == null) return;
        sb.AppendLine($"  --- {category} ({list.Count}) ---");
        for (int i = 0; i < list.Count; i++)
        {
            var r = list[i];
            if (r == null) continue;
            sb.AppendLine($"    {r.relicName} | id={r.id} | level={r.level} | type={r.GetType().Name}");
        }
    }

    private static void DumpAbilityList(StringBuilder sb, Il2CppSystem.Collections.Generic.List<Rogue.Ability> list, string category)
    {
        if (list == null) return;
        sb.AppendLine($"  --- {category} ({list.Count}) ---");
        for (int i = 0; i < list.Count; i++)
        {
            var a = list[i];
            if (a == null) continue;
            sb.AppendLine($"    {a.name} | id={a.id} | type={a.GetType().Name}");
        }
    }

    private static void DumpTalentList(StringBuilder sb, Il2CppSystem.Collections.Generic.List<Rogue.Talent> list, string category)
    {
        if (list == null) return;
        sb.AppendLine($"  --- {category} ({list.Count}) ---");
        for (int i = 0; i < list.Count; i++)
        {
            var t = list[i];
            if (t == null) continue;
            sb.AppendLine($"    {t.name} | id={t.id} | type={t.GetType().Name}");
        }
    }

    private static void WriteSeparateFiles()
    {
        Plugin.Log.LogInfo("[Dump] WriteSeparateFiles starting...");
        string basePath = BepInEx.Paths.PluginPath;

        // RELICS FILE
        try
        {
            var relicRepos = UnityEngine.Resources.FindObjectsOfTypeAll<RelicRepository>();
            var relicRepo = relicRepos != null && relicRepos.Length > 0 ? relicRepos[0] : null;
            if (relicRepo != null)
            {
                var sb = new StringBuilder();
                sb.AppendLine("=== ALL RELICS ===");
                sb.AppendLine();
                var allLists = new (Il2CppSystem.Collections.Generic.List<Rogue.Relic> list, string cat)[]
                {
                    (relicRepo.offensiveRelics, "Offensive"), (relicRepo.defensiveRelics, "Defensive"),
                    (relicRepo.utilityRelics, "Utility"), (relicRepo.speedRelics, "Speed"),
                    (relicRepo.checkingRelics, "Checking"), (relicRepo.powerRelics, "Power"),
                    (relicRepo.accuracyRelics, "Accuracy"), (relicRepo.chaosRelics, "Chaos"),
                    (relicRepo.bossRelics, "Boss"), (relicRepo.goalieRelics, "Goalie"),
                    (relicRepo.coachRelics, "Coach")
                };
                foreach (var (list, cat) in allLists)
                {
                    if (list == null || list.Count == 0) continue;
                    sb.AppendLine($"--- {cat} ---");
                    for (int i = 0; i < list.Count; i++)
                    {
                        var r = list[i];
                        if (r == null) continue;
                        DumpSingleRelic(sb, r);
                    }
                }

                // Also dump ALL relics found in memory (catches ones not in repo lists)
                sb.AppendLine();
                sb.AppendLine("--- ALL IN MEMORY (includes non-repo relics) ---");
                var allRelics = UnityEngine.Resources.FindObjectsOfTypeAll<Rogue.Relic>();
                if (allRelics != null)
                {
                    var seen = new HashSet<string>();
                    foreach (var r in allRelics)
                    {
                        if (r == null) continue;
                        string key = $"{r.relicName}_{r.level}";
                        if (seen.Contains(key)) continue;
                        seen.Add(key);
                        DumpSingleRelic(sb, r);
                    }
                }

                File.WriteAllText(Path.Combine(basePath, "ALL_RELICS.txt"), sb.ToString());
                Plugin.Log.LogInfo("[Dump] Wrote ALL_RELICS.txt");
            }
        }
        catch (Exception ex) { Plugin.Log.LogError($"[Dump] Relics file error: {ex.Message}"); }

        // ABILITIES FILE
        try
        {
            var abilityRepos = UnityEngine.Resources.FindObjectsOfTypeAll<AbilityRepository>();
            var abilityRepo = abilityRepos != null && abilityRepos.Length > 0 ? abilityRepos[0] : null;
            if (abilityRepo != null)
            {
                var sb = new StringBuilder();
                sb.AppendLine("=== ALL ABILITIES ===");
                sb.AppendLine();
                var list = abilityRepo.abilities;
                if (list != null)
                {
                    for (int i = 0; i < list.Count; i++)
                    {
                        var a = list[i];
                        if (a == null) continue;
                        string descKey = "";
                        try { descKey = a.description ?? ""; } catch { }
                        string descText = ResolveI2(descKey);
                        string abilNameKey = "";
                        if (!string.IsNullOrEmpty(descKey) && descKey.EndsWith("/description"))
                            abilNameKey = descKey.Replace("/description", "/name");
                        string abilDisplayName = ResolveI2(abilNameKey);
                        sb.AppendLine($"  [{a.name}] \"{(abilDisplayName != "" ? abilDisplayName : a.name)}\" (Lv{a.level}) CD={a.baseCooldown} Charges={a.maxCharges}");
                        if (descText != "") sb.AppendLine($"    {descText}");
                        sb.AppendLine();
                    }
                }
                File.WriteAllText(Path.Combine(basePath, "ALL_ABILITIES.txt"), sb.ToString());
                Plugin.Log.LogInfo("[Dump] Wrote ALL_ABILITIES.txt");
            }
        }
        catch (Exception ex) { Plugin.Log.LogError($"[Dump] Abilities file error: {ex.Message}"); }

        // TALENTS FILE
        try
        {
            var talentRepos = UnityEngine.Resources.FindObjectsOfTypeAll<TalentRepository>();
            var talentRepo = talentRepos != null && talentRepos.Length > 0 ? talentRepos[0] : null;
            if (talentRepo != null)
            {
                var sb = new StringBuilder();
                sb.AppendLine("=== ALL TALENTS ===");
                sb.AppendLine();
                var list = talentRepo.talents;
                if (list != null)
                {
                    for (int i = 0; i < list.Count; i++)
                    {
                        var t = list[i];
                        if (t == null) continue;
                        string descKey = "";
                        try { descKey = t.description ?? ""; } catch { }
                        string statKey = "";
                        try { statKey = t.statBonusDescription ?? ""; } catch { }
                        string descText = ResolveI2(descKey);
                        string statText = ResolveI2(statKey);
                        // Derive UI display name from description key: powerups/X/description -> powerups/X/name
                        string nameKey = "";
                        if (!string.IsNullOrEmpty(descKey) && descKey.EndsWith("/description"))
                            nameKey = descKey.Replace("/description", "/name");
                        string displayName = ResolveI2(nameKey);
                        sb.AppendLine($"  [{t.name}] \"{(displayName != "" ? displayName : t.name)}\" (Lv{t.level})");
                        if (descText != "") sb.AppendLine($"    {descText}");
                        if (statText != "") sb.AppendLine($"    Stats: {statText}");
                        // Dump non-zero fields to find actual values
                        try
                        {
                            var fields = t.GetIl2CppType().GetFields(Il2CppSystem.Reflection.BindingFlags.Public | Il2CppSystem.Reflection.BindingFlags.Instance);
                            foreach (var field in fields)
                            {
                                string fname = field.Name;
                                if (fname == "name" || fname == "description" || fname == "statBonusDescription" || fname == "id" || fname == "level") continue;
                                try
                                {
                                    var val = field.GetValue(t);
                                    string vs = val?.ToString() ?? "null";
                                    if (vs != "0" && vs != "" && vs != "null" && vs != "False" && vs != "0.0")
                                        sb.AppendLine($"    {fname}={vs}");
                                }
                                catch { }
                            }
                        }
                        catch { }
                        sb.AppendLine();
                    }
                }
                File.WriteAllText(Path.Combine(basePath, "ALL_TALENTS.txt"), sb.ToString());
                Plugin.Log.LogInfo("[Dump] Wrote ALL_TALENTS.txt");
            }
        }
        catch (Exception ex) { Plugin.Log.LogError($"[Dump] Talents file error: {ex.Message}"); }
    }
}

// ============================================================
// DEBUG: Boost player team
// ============================================================
[HarmonyPatch(typeof(Team), nameof(Team.Initialize))]
public static class DebugTeamBoost
{
    private static readonly HashSet<IntPtr> BoostedPtrs = new();
    private static readonly string[] PlayerPrefixes = { "Basic", "Defense", "Speed", "Trio" };

    [HarmonyPostfix]
    public static void Postfix(Team __instance, TeamData teamData)
    {
        if (!Plugin.DebugSkipEnabled || teamData == null) return;
        string name = teamData.teamName?.Trim() ?? "";
        bool isPlayer = false;
        foreach (var p in PlayerPrefixes)
            if (name.StartsWith(p, StringComparison.OrdinalIgnoreCase)) { isPlayer = true; break; }
        if (!isPlayer) return;

        var forwards = teamData.forwards;
        if (forwards != null)
            for (int i = 0; i < forwards.Count; i++)
            {
                var f = forwards[i];
                if (f == null || BoostedPtrs.Contains(f.Pointer)) continue;
                BoostedPtrs.Add(f.Pointer);
                f.shotPower += 200; f.speed += 200; f.checking += 200; f.shotAccuracy += 200;
            }
        var allFwd = __instance.AllForwards;
        if (allFwd != null)
            for (int i = 0; i < allFwd.Count; i++)
            {
                var fwd = allFwd[i];
                if (fwd?._forwardData == null || BoostedPtrs.Contains(fwd._forwardData.Pointer)) continue;
                BoostedPtrs.Add(fwd._forwardData.Pointer);
                fwd._forwardData.shotPower += 200; fwd._forwardData.speed += 200;
                fwd._forwardData.checking += 200; fwd._forwardData.shotAccuracy += 200;
            }
        var g = teamData.goalie;
        if (g != null && !BoostedPtrs.Contains(g.Pointer))
        {
            BoostedPtrs.Add(g.Pointer);
            g.skill += 200; g.catchingSkill += 200; g.gloveSkill += 200;
            g.blockerSkill += 200; g.fiveHoleSkill += 200; g.standingSpeed += 200;
            g.butterflySpeed += 200; g.controlSkill += 200; g.recoverySkill += 200;
            g.pokecheckSkill += 200; g.depth += 200; g.passPower += 200; g.shotPower += 200;
        }
        Plugin.Log.LogInfo($"[DEBUG] Boosted '{name}' +200");
    }
}

// ============================================================
// Config data classes
// ============================================================
public class UniformConfig
{
    public string Body = "";
    public string BodyAway = "";
    public string Bicep = "";
    public string BicepAway = "";
    public string Gloves = "";
    public string GlovesAway = "";
    public string Pants = "";
    public string PantsAway = "";
    public string Skates = "";
    public string SkatesAway = "";
    public string Helmet = "";
    public string HelmetAway = "";
    public string Stick = "";
}

public class PlayerConfig
{
    public string Name = "";
    public string ImportPlayer = "";
    public int Number = 88;
    public string Face = "";
    public bool Lefty = false;
    public bool Black = false;
    public string Size = "Medium";
    public int Speed = 50;
    public int ShotPower = 50;
    public int Accuracy = 50;
    public int Checking = 50;
    public string Ability = "";
    public List<string> Talents = new List<string>();
    public int RandomTalentCount = 0;
    public List<string> RandomTalentPool = new List<string>();
    public bool RandomTalentPoolAll = false; // true = pick from entire talent pool
    // Goalie stats
    public int Skill = 50;
    public int Catching = 50;
    public int Glove = 50;
    public int Blocker = 50;
    public int FiveHole = 50;
    public int StandSpeed = 50;
    public int ButterflySpeed = 50;
    public int Control = 50;
    public int Recovery = 50;
    public int PassPower = 50;
    public int Pokecheck = 50;
    public int Depth = 50;
    public float PassRead = 0f;
    // Player appearance extras
    public float SizeOffset = 1.0f;
    public string Glasses = "";
    // Per-player uniform overrides (null = use team uniform)
    public string StickOverride = null;
    public string HelmetOverride = null;
    public string HelmetAwayOverride = null;
    public string BodyOverride = null;
    public string BodyAwayOverride = null;
    public string BicepOverride = null;
    public string BicepAwayOverride = null;
    public string GlovesOverride = null;
    public string GlovesAwayOverride = null;
    public string PantsOverride = null;
    public string PantsAwayOverride = null;
    public string SkatesOverride = null;
    public string SkatesAwayOverride = null;
    // Per-player color overrides (null = use team colors)
    public int[] JerseyColor = null;
    public int[] JerseySecondaryColor = null;
    public int[] JerseyAccentColor = null;
    public int[] GlovesColor = null;
    public int[] GlovesSecondaryColor = null;
    public int[] GlovesTertiaryColor = null;
    public int[] HelmetColor = null;
    public int[] HelmetSecondaryColor = null;
    public int[] HelmetTertiaryColor = null;
    public int[] PantsColor = null;
    public int[] PantsSecondaryColor = null;
    public int[] PantsTertiaryColor = null;
    public int[] SkatesColor = null;        // skate body color
    public int[] BladeColor = null;          // blade color
    public int[] LacesColor = null;          // laces color
    public int[] BicepColor = null;
    public int[] NumberColor = null;
    public int[] NumberSecondaryColor = null;
    public int[] SocksColor = null;
    public int[] SocksSecondaryColor = null;
    public int[] SocksTertiaryColor = null;
    // Goalie-specific skins
    public string GoalieSkin = null;
    public string GoalieSkinAway = null;
    public string GoalieGloveSkin = null;
    public string GoalieGloveAway = null;
    public string GoalieBlockerSkin = null;
    public string GoalieBlockerAway = null;
    public string GoaliePadsSkin = null;
    public string GoaliePadsAway = null;
    public string GoalieStickSkin = null;
    public string GoalieStickAway = null;
    public string GoalieHelmetSkin = null;
    public string GoalieLogoSkin = null;
}

public class TeamConfig
{
    public string Name = "";
    public string City = "";
    public string Abbreviation = "";
    public string LogoFrom = "";
    public string ImportTeam = "";
    public float StatScale = 1.0f;
    public int[] JerseyPrimary = null;
    public int[] JerseySecondary = null;
    public int[] JerseyAccent = null;
    public int[] AwayPrimary = null;
    public int[] AwaySecondary = null;
    public int[] AwayAccent = null;
    public int[] NumberColorHome = null;
    public int[] NumberColorAway = null;
    public int[] TransitionPrimary = null;
    public int[] TransitionSecondary = null;
    public int[] TransitionTertiary = null;
    // Team-level equipment colors (applied to all players as defaults)
    // Each piece has primary/secondary/tertiary via ColorScheme
    public int[] TeamGlovesColor = null;
    public int[] TeamGlovesSecondary = null;
    public int[] TeamGlovesTertiary = null;
    public int[] TeamHelmetColor = null;
    public int[] TeamHelmetSecondary = null;
    public int[] TeamHelmetTertiary = null;
    public int[] TeamPantsColor = null;
    public int[] TeamPantsSecondary = null;
    public int[] TeamPantsTertiary = null;
    public int[] TeamSkatesColor = null;
    public int[] TeamBladeColor = null;
    public int[] TeamLacesColor = null;
    public int[] TeamBicepColor = null;
    public int[] TeamSocksColor = null;
    public int[] TeamSocksSecondary = null;
    public int[] TeamSocksTertiary = null;
    public int[] TeamStickColor = null;
    public int[] TeamNumberColor = null;
    public int[] TeamNumberSecondary = null;
    public int BenchSize = -1;
    public string BenchHead = "";
    public UniformConfig Uniform = new UniformConfig();
    public List<string> Relics = new List<string>();
    public int TeamRandomTalents = 0;
    public List<string> TeamRandomPool = new List<string>();
    public bool TeamRandomPoolAll = false;
    public PlayerConfig Goalie = new PlayerConfig();
    public PlayerConfig LW = new PlayerConfig();
    public PlayerConfig RW = new PlayerConfig();
    public PlayerConfig C = new PlayerConfig();
    public PlayerConfig LD = new PlayerConfig();
    public PlayerConfig RD = new PlayerConfig();
    public PlayerConfig L2_LW = new PlayerConfig();
    public PlayerConfig L2_RW = new PlayerConfig();
    public PlayerConfig L2_C = new PlayerConfig();
    public PlayerConfig L2_LD = new PlayerConfig();
    public PlayerConfig L2_RD = new PlayerConfig();
    public bool IsImport => !string.IsNullOrEmpty(ImportTeam);
}
