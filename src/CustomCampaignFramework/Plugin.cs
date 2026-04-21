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

[BepInPlugin("com.mods.customcampaign", "Custom Campaign Framework", "2.1.3")]
public class Plugin : BasePlugin
{
    internal static new ManualLogSource Log;

    internal const int ScalingPerAct = 8;
    internal const int BossScalingPerAct = 10;
    internal static bool DebugSkipEnabled = false;

    internal static bool BossJustBeaten = false;
    // Set when CampaignState.LoadRunData fires (Continue Run path). Used to
    // suppress the PatchNewRunStart save wipe when the game's StartMenu.StartNewRun
    // coroutine fires during the Continue flow (not just New Run).
    internal static float LastLoadRunDataTime = -9999f;
    internal static int DebugRealAct = -1;
    internal static bool DebugActForced = false;
    internal static bool TeamsLogged = false;
    internal static bool ReposLogged = false;
    internal static bool SeparateFilesWritten = false;

    // ===== CONFIG =====
    // New layout (v2.1+):
    //   BepInEx/plugins/CustomCampaignFramework.dll   ← this DLL, sibling to the content folder
    //   BepInEx/plugins/Custom Campaigns Mod/         ← everything else (GUI + data)
    //       active.txt, defaults.txt, library/, campaigns/<CampaignName>/...
    //
    // Legacy layout (v1.x–v2.0) used BepInEx/plugins/Campaigns/<CampaignName>/ at root
    // with active.txt/defaults.txt at the same level. We fall back to that if the new
    // folder doesn't exist, so existing installs keep working until they migrate.
    private static string PickModContentRoot()
    {
        string pluginsDir = BepInEx.Paths.PluginPath;
        string newRoot = Path.Combine(pluginsDir, "Custom Campaigns Mod");
        if (Directory.Exists(newRoot)) return newRoot;
        string legacyRoot = Path.Combine(pluginsDir, "Campaigns");
        if (Directory.Exists(legacyRoot)) return legacyRoot;
        // Neither exists — default to new layout (will be created on first save)
        return newRoot;
    }
    internal static readonly string ModContentRoot = PickModContentRoot();
    // In the new layout, campaigns live in a 'campaigns/' subfolder. In the legacy
    // layout, campaign folders were at ModContentRoot directly — try the new
    // subfolder first, fall back to ModContentRoot.
    private static string PickCampaignsRoot()
    {
        string sub = Path.Combine(ModContentRoot, "campaigns");
        if (Directory.Exists(sub)) return sub;
        return ModContentRoot; // legacy: campaigns at root level
    }
    private static readonly string CampaignsRoot = PickCampaignsRoot();
    private static readonly string ActivePath = Path.Combine(ModContentRoot, "active.txt");
    private static readonly string DefaultsPath = Path.Combine(ModContentRoot, "defaults.txt");

    // Default fallback values (loaded from defaults.txt)
    internal static TeamConfig DefaultTeam = new TeamConfig();
    internal static PlayerConfig DefaultSkater = new PlayerConfig();
    internal static PlayerConfig DefaultGoalie = new PlayerConfig();

    private static string ModFolder;
    private static string ConfigPath;
    private static string SavePath;
    private static string PlayerTeamsPath;
    internal static string ActiveCampaign = "NHL Season";

    internal static bool IsDefaultMode = false; // true = no mod behavior, base game only

    // Player team editor — configs keyed by lowercase prefix ("basic", "defense", "speed", "trio")
    internal static Dictionary<string, TeamConfig> PlayerTeamConfigs = new Dictionary<string, TeamConfig>(StringComparer.OrdinalIgnoreCase);
    // Squad id ("Custom_<key>") -> (displayName, description) — consulted by the
    // get_LocalizedSquadName/Desc patches so custom squads show our strings
    // instead of "???" (localization falls back to key when no entry exists).
    internal static Dictionary<string, (string name, string desc)> CustomSquadText
        = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
    // Draft pool player configs keyed by lowercase full name ("stu stumpl", "freddy kovalski", etc.)
    internal static Dictionary<string, PlayerConfig> DraftPoolConfigs = new Dictionary<string, PlayerConfig>(StringComparer.OrdinalIgnoreCase);
    internal static bool DraftPoolApplied = false;
    // Free-agent node cap per run. Long campaigns otherwise accumulate more
    // free agents than the roster has slots for and crash the game on
    // 5th+ FA signing. When the cap is reached, further FanNumber1 nodes
    // get substituted with GeneralManager (team-upgrade) nodes.
    internal const int MaxFreeAgentNodes = 0;  // TEMP: 0 = replace ALL FA nodes with TeamTraining (testing)
    internal static int FreeAgentNodesPlaced = 0;
    // Track which ForwardData instances we've already applied draft-pool
    // config to. Using pointers so re-apply is skipped for the same instance
    // but NEWLY-loaded free agents get picked up on subsequent Team.Initialize
    // calls (they load lazily when the draft UI opens).
    internal static HashSet<IntPtr> AppliedDraftPtrs = new HashSet<IntPtr>();
    internal static bool UsePlayerTeams = false; // toggle from campaign settings
    internal static bool _pendingAutoDump = false;
    internal static List<string> ConfigTeamDirs = new List<string>(); // parallel to ConfigTeams — folder path per team

    // Runtime-grown set of face skin names registered as helmetless. Keeps
    // duplicate work out of the HeadsWithoutHelmets array replacement path.
    internal static HashSet<string> HelmetlessFaces = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// Extend the game's ForwardDataExtensions.HeadsWithoutHelmets array to
    /// include `faceSkin`, so the renderer skips the helmet for any forward
    /// that uses that face. Reflection-only (no Harmony patch) — safe against
    /// uninitialized IL2CPP owners, which crashed the previous approach.
    internal static void RegisterFaceAsHelmetless(string faceSkin)
    {
        if (string.IsNullOrEmpty(faceSkin)) return;
        if (HelmetlessFaces.Contains(faceSkin)) return;
        try
        {
            var t = typeof(Tape2Tape.Customization.UI.ForwardDataExtensions);
            var prop = t.GetProperty("HeadsWithoutHelmets", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (prop == null)
            {
                Log.LogWarning($"[NoHelmet] HeadsWithoutHelmets property not found — '{faceSkin}' not registered");
                return;
            }
            var current = prop.GetValue(null) as Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStringArray;
            int oldLen = current?.Length ?? 0;
            var replacement = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStringArray(oldLen + 1);
            for (int i = 0; i < oldLen; i++) replacement[i] = current[i];
            replacement[oldLen] = faceSkin;
            prop.SetValue(null, replacement);
            HelmetlessFaces.Add(faceSkin);
            Log.LogInfo($"[NoHelmet] Registered '{faceSkin}' as helmetless (HeadsWithoutHelmets: {oldLen} -> {oldLen + 1})");
        }
        catch (Exception ex) { Log.LogWarning($"[NoHelmet] RegisterFaceAsHelmetless('{faceSkin}'): {ex.Message}"); }
    }

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
        PlayerTeamsPath = Path.Combine(ModFolder, "player_teams.txt");

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
    internal static HashSet<int> ReplaceChallengesMaps = null; // null = use acts logic; set = 0-indexed map positions
    internal static bool DumpData = true; // generates reference dump files
    internal static bool ReplaceSoccerBall = true;
    internal static bool ReplaceGolfBall = true;

    // Reward-pool filters (populated from campaigns/<name>/reward_pools.txt).
    // IDs held here are filtered OUT of the game's random-reward picks at
    // runtime via Harmony postfixes on RelicRepository / TalentRepository.
    internal static HashSet<string> ExcludedRewardRelicIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    internal static HashSet<string> ExcludedRewardTalentIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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

            // Multi-folder format only. Campaign settings live in campaign.txt,
            // teams live in the teams/ subfolder.
            if (File.Exists(ConfigPath))
            {
                ParseKvFile(ConfigPath, (key, val) =>
                {
                    if (key == "act sequence")
                    {
                        var seq = new List<int>();
                        foreach (var p in val.Split(','))
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
                            ReplaceChallenges = true; ReplaceChallengesActs = null; ReplaceChallengesMaps = null;
                        }
                        else if (lv == "no" || lv == "false")
                        {
                            ReplaceChallenges = false; ReplaceChallengesActs = null; ReplaceChallengesMaps = null;
                        }
                        else if (lv.StartsWith("maps:"))
                        {
                            // Per-map mode: "maps:1,3,5" — 1-indexed map positions
                            ReplaceChallenges = true;
                            ReplaceChallengesActs = null;
                            ReplaceChallengesMaps = new HashSet<int>();
                            foreach (var p in lv.Substring(5).Split(','))
                                if (int.TryParse(p.Trim(), out int idx) && idx > 0)
                                    ReplaceChallengesMaps.Add(idx - 1); // store 0-indexed
                            Log.LogInfo($"[Config] Replace Challenges per-map: [{string.Join(", ", ReplaceChallengesMaps)}] (0-indexed)");
                        }
                        else
                        {
                            // Legacy per-act list: "1,2"
                            ReplaceChallenges = true;
                            ReplaceChallengesMaps = null;
                            ReplaceChallengesActs = new List<int>();
                            foreach (var p in val.Split(','))
                                if (int.TryParse(p.Trim(), out int a)) ReplaceChallengesActs.Add(a);
                        }
                    }
                    else if (key == "replace soccer ball") ReplaceSoccerBall = val.ToLower() == "yes" || val.ToLower() == "true";
                    else if (key == "replace golf ball") ReplaceGolfBall = val.ToLower() == "yes" || val.ToLower() == "true";
                    else if (key == "use player teams" || key == "custom player teams") UsePlayerTeams = val.ToLower() == "yes" || val.ToLower() == "true";
                    else if (key == "dump data") DumpData = val.ToLower() == "yes" || val.ToLower() == "true";
                });
            }
            else
            {
                Log.LogInfo("[Config] No campaign.txt found — using defaults");
            }

            // Reward-pool filters — campaigns/<name>/reward_pools.txt.
            // Format: "Excluded Relics = id1, id2, id3" (comma-separated ids,
            // matching the ids in _reward_relics.txt). Same for talents.
            ExcludedRewardRelicIds.Clear();
            ExcludedRewardTalentIds.Clear();
            try
            {
                string rewardPoolsPath = Path.Combine(ModFolder, "reward_pools.txt");
                if (File.Exists(rewardPoolsPath))
                {
                    ParseKvFile(rewardPoolsPath, (key, val) =>
                    {
                        if (key == "excluded relics" || key == "excluded relic")
                        {
                            foreach (var p in val.Split(','))
                            {
                                var id = p.Trim();
                                if (id.Length > 0) ExcludedRewardRelicIds.Add(id);
                            }
                        }
                        else if (key == "excluded talents" || key == "excluded talent")
                        {
                            foreach (var p in val.Split(','))
                            {
                                var id = p.Trim();
                                if (id.Length > 0) ExcludedRewardTalentIds.Add(id);
                            }
                        }
                    });
                    Log.LogInfo($"[Config] reward_pools.txt loaded — {ExcludedRewardRelicIds.Count} relics excluded, {ExcludedRewardTalentIds.Count} talents excluded");
                }
            }
            catch (Exception ex) { Log.LogWarning($"[Config] reward_pools.txt: {ex.Message}"); }

            LoadCampaignFolders();
        }
        catch (Exception ex)
        {
            Log.LogError($"[Config] Failed to load config: {ex.Message}\n{ex.StackTrace}");
        }
    }

    // Legacy single-file loader removed — only multi-folder format is supported now.
    // The methods below parse individual files in the folder structure.


    // ===== MULTI-FOLDER CAMPAIGN LOADER =====
    // Applies a single key=value pair to a TeamConfig. Used by both LoadConfig (inline)
    // and the multi-folder loader (per-file).
    internal static void ApplyTeamField(TeamConfig team, string key, string val)
    {
        if (team == null) return;
        if (key == "team name") team.Name = val;
        else if (key == "city") team.City = val;
        else if (key == "logo from") team.LogoFrom = val;
        else if (key == "import team") team.ImportTeam = val;
        else if (key == "abbreviation") team.Abbreviation = val;
        else if (key == "description") team.Description = val;
        else if (key == "squad head") team.SquadHead = val;
        else if (key == "stat scale") team.StatScale = ParseRandomFloat(val);
        // Home Colors
        else if (key == "jersey primary") team.JerseyPrimary = ParseRandomColor(val);
        else if (key == "jersey secondary") team.JerseySecondary = ParseRandomColor(val);
        else if (key == "jersey accent") team.JerseyAccent = ParseRandomColor(val);
        else if (key == "away primary") team.AwayPrimary = ParseRandomColor(val);
        else if (key == "away secondary") team.AwaySecondary = ParseRandomColor(val);
        else if (key == "away accent") team.AwayAccent = ParseRandomColor(val);
        else if (key == "number color home") team.NumberColorHome = ParseRandomColor(val);
        else if (key == "number color away") team.NumberColorAway = ParseRandomColor(val);
        else if (key == "transition primary") team.TransitionPrimary = ParseRandomColor(val);
        else if (key == "transition secondary") team.TransitionSecondary = ParseRandomColor(val);
        else if (key == "transition tertiary") team.TransitionTertiary = ParseRandomColor(val);
        // Uniform skins (may also take RGB)
        else if (key == "body") TryParseUniformRGB(val, "body", ref team.Uniform.Body, ref team.JerseyPrimary);
        else if (key == "body away") TryParseUniformRGB(val, "body", ref team.Uniform.BodyAway, ref team.AwayPrimary);
        else if (key == "bicep") TryParseUniformRGB(val, "bicep", ref team.Uniform.Bicep, ref team.TeamBicepColor);
        else if (key == "bicep away") team.Uniform.BicepAway = ResolveSkin(val, "bicep");
        else if (key == "gloves") TryParseUniformRGB(val, "gloves", ref team.Uniform.Gloves, ref team.TeamGlovesColor);
        else if (key == "gloves away") team.Uniform.GlovesAway = ResolveSkin(val, "gloves");
        else if (key == "pants") TryParseUniformRGB(val, "pants", ref team.Uniform.Pants, ref team.TeamPantsColor);
        else if (key == "pants away") team.Uniform.PantsAway = ResolveSkin(val, "pants");
        else if (key == "skates") TryParseUniformRGB(val, "skates", ref team.Uniform.Skates, ref team.TeamSkatesColor);
        else if (key == "skates away") team.Uniform.SkatesAway = ResolveSkin(val, "skates");
        else if (key == "helmet") TryParseUniformRGB(val, "helmet", ref team.Uniform.Helmet, ref team.TeamHelmetColor);
        else if (key == "helmet away") team.Uniform.HelmetAway = ResolveSkin(val, "helmet");
        else if (key == "stick") TryParseUniformRGB(val, "stick", ref team.Uniform.Stick, ref team.TeamStickColor);
        // Team equipment colors
        else if (key == "gloves color") team.TeamGlovesColor = ParseRandomColor(val);
        else if (key == "gloves secondary color" || key == "gloves color 2") team.TeamGlovesSecondary = ParseRandomColor(val);
        else if (key == "gloves tertiary color" || key == "gloves color 3") team.TeamGlovesTertiary = ParseRandomColor(val);
        else if (key == "helmet color") team.TeamHelmetColor = ParseRandomColor(val);
        else if (key == "helmet secondary color" || key == "helmet color 2") team.TeamHelmetSecondary = ParseRandomColor(val);
        else if (key == "helmet tertiary color" || key == "helmet color 3") team.TeamHelmetTertiary = ParseRandomColor(val);
        else if (key == "pants color") team.TeamPantsColor = ParseRandomColor(val);
        else if (key == "pants secondary color" || key == "pants color 2") team.TeamPantsSecondary = ParseRandomColor(val);
        else if (key == "pants tertiary color" || key == "pants color 3") team.TeamPantsTertiary = ParseRandomColor(val);
        else if (key == "skates color") team.TeamSkatesColor = ParseRandomColor(val);
        else if (key == "blade color") team.TeamBladeColor = ParseRandomColor(val);
        else if (key == "laces color") team.TeamLacesColor = ParseRandomColor(val);
        else if (key == "bicep color") team.TeamBicepColor = ParseRandomColor(val);
        else if (key == "socks color") team.TeamSocksColor = ParseRandomColor(val);
        else if (key == "socks secondary color" || key == "socks color 2") team.TeamSocksSecondary = ParseRandomColor(val);
        else if (key == "socks tertiary color" || key == "socks color 3") team.TeamSocksTertiary = ParseRandomColor(val);
        else if (key == "stick color") team.TeamStickColor = ParseRandomColor(val);
        else if (key == "number color") team.TeamNumberColor = ParseRandomColor(val);
        else if (key == "number secondary color" || key == "number color 2") team.TeamNumberSecondary = ParseRandomColor(val);
        // Gameplay
        else if (key == "bench size") team.BenchSize = ParseRandomInt(val);
        else if (key == "bench head") team.BenchHead = val;
        else if (key == "team relics" || key == "relics")
        {
            team.Relics = new List<string>();
            foreach (var r in val.Split(','))
            { string t = r.Trim(); if (t.Length > 0) team.Relics.Add(t); }
        }
        else if (key == "no bench bonus" || key == "disable bench bonus")
        {
            string lv = (val ?? "").Trim().ToLowerInvariant();
            team.NoBenchBonus = (lv == "yes" || lv == "true" || lv == "1");
        }
        else if (key == "team random talents") team.TeamRandomTalents = ParseRandomInt(val);
        else if (key == "team random pool")
        {
            string trp = val.Trim().ToLower();
            if (trp == "all" || trp == "whole pool" || trp == "full pool")
                team.TeamRandomPoolAll = true;
            else
            {
                team.TeamRandomPool = new List<string>();
                foreach (var t in val.Split(','))
                { string tr = t.Trim(); if (tr.Length > 0) team.TeamRandomPool.Add(tr); }
            }
        }
    }

    internal static void ApplyPlayerField(PlayerConfig p, string key, string val)
    {
        if (p == null) return;
        if (key == "name") p.Name = val;
        else if (key == "import player") p.ImportPlayer = val;
        else if (key == "number") p.Number = ParseRandomInt(val);
        else if (key == "face") p.Face = val;
        else if (key == "left handed")
        {
            string lh = val.ToLower().Trim();
            if (lh == "random") p.Lefty = ConfigRng.Next(2) == 1;
            else p.Lefty = lh == "yes" || lh == "true";
        }
        else if (key == "skin color")
        {
            string sc = val.ToLower().Trim();
            if (sc == "random") p.Black = ConfigRng.Next(2) == 1;
            else p.Black = sc == "dark";
        }
        else if (key == "size")
        {
            if (val.Trim().Equals("random", StringComparison.OrdinalIgnoreCase))
            {
                string[] sizes = { "ExtraSmall", "Small", "Medium", "Big", "ExtraBig" };
                p.Size = sizes[ConfigRng.Next(sizes.Length)];
            }
            else p.Size = val;
        }
        else if (key == "speed") p.Speed = ParseRandomInt(val);
        else if (key == "shot power") p.ShotPower = ParseRandomInt(val);
        else if (key == "accuracy") p.Accuracy = ParseRandomInt(val);
        else if (key == "checking") p.Checking = ParseRandomInt(val);
        else if (key == "ability") p.Ability = val;
        else if (key == "talents" || key == "goalie talents")
        {
            p.Talents = new List<string>();
            foreach (var t in val.Split(','))
            { string tr = t.Trim(); if (tr.Length > 0) p.Talents.Add(tr); }
        }
        else if (key == "random talents") p.RandomTalentCount = ParseRandomInt(val);
        else if (key == "random pool")
        {
            string rp = val.Trim().ToLower();
            if (rp == "all" || rp == "whole pool" || rp == "full pool") p.RandomTalentPoolAll = true;
            else
            {
                p.RandomTalentPool = new List<string>();
                foreach (var t in val.Split(','))
                { string tr = t.Trim(); if (tr.Length > 0) p.RandomTalentPool.Add(tr); }
            }
        }
        // Goalie stats
        else if (key == "skill") p.Skill = ParseRandomInt(val);
        else if (key == "catching") p.Catching = ParseRandomInt(val);
        else if (key == "glove") p.Glove = ParseRandomInt(val);
        else if (key == "blocker") p.Blocker = ParseRandomInt(val);
        else if (key == "five hole") p.FiveHole = ParseRandomInt(val);
        else if (key == "stand speed" || key == "standing speed") p.StandSpeed = ParseRandomInt(val);
        else if (key == "butterfly speed") p.ButterflySpeed = ParseRandomInt(val);
        else if (key == "control") p.Control = ParseRandomInt(val);
        else if (key == "recovery") p.Recovery = ParseRandomInt(val);
        else if (key == "pass power") p.PassPower = ParseRandomInt(val);
        else if (key == "pokecheck" || key == "poke check") p.Pokecheck = ParseRandomInt(val);
        else if (key == "depth") p.Depth = ParseRandomInt(val);
        else if (key == "pass read") p.PassRead = ParseRandomFloat(val);
        // Appearance
        else if (key == "size offset") p.SizeOffset = ParseRandomFloat(val);
        else if (key == "glasses") p.Glasses = val;
        // Per-player uniform overrides
        else if (key == "stick") p.StickOverride = ResolveSkin(val, "stick");
        else if (key == "helmet") p.HelmetOverride = ResolveSkin(val, "helmet");
        else if (key == "helmet away") p.HelmetAwayOverride = ResolveSkin(val, "helmet");
        else if (key == "body") p.BodyOverride = ResolveSkin(val, "body");
        else if (key == "body away") p.BodyAwayOverride = ResolveSkin(val, "body");
        else if (key == "bicep") p.BicepOverride = ResolveSkin(val, "bicep");
        else if (key == "bicep away") p.BicepAwayOverride = ResolveSkin(val, "bicep");
        else if (key == "gloves") p.GlovesOverride = ResolveSkin(val, "gloves");
        else if (key == "gloves away") p.GlovesAwayOverride = ResolveSkin(val, "gloves");
        else if (key == "pants") p.PantsOverride = ResolveSkin(val, "pants");
        else if (key == "pants away") p.PantsAwayOverride = ResolveSkin(val, "pants");
        else if (key == "skates") p.SkatesOverride = ResolveSkin(val, "skates");
        else if (key == "skates away") p.SkatesAwayOverride = ResolveSkin(val, "skates");
        // Per-player color overrides
        else if (key == "jersey color") p.JerseyColor = ParseRandomColor(val);
        else if (key == "jersey secondary color") p.JerseySecondaryColor = ParseRandomColor(val);
        else if (key == "jersey accent color") p.JerseyAccentColor = ParseRandomColor(val);
        else if (key == "gloves color") p.GlovesColor = ParseRandomColor(val);
        else if (key == "gloves secondary color" || key == "gloves color 2") p.GlovesSecondaryColor = ParseRandomColor(val);
        else if (key == "gloves tertiary color" || key == "gloves color 3") p.GlovesTertiaryColor = ParseRandomColor(val);
        else if (key == "helmet color") p.HelmetColor = ParseRandomColor(val);
        else if (key == "helmet secondary color" || key == "helmet color 2") p.HelmetSecondaryColor = ParseRandomColor(val);
        else if (key == "helmet tertiary color" || key == "helmet color 3") p.HelmetTertiaryColor = ParseRandomColor(val);
        else if (key == "pants color") p.PantsColor = ParseRandomColor(val);
        else if (key == "pants secondary color" || key == "pants color 2") p.PantsSecondaryColor = ParseRandomColor(val);
        else if (key == "pants tertiary color" || key == "pants color 3") p.PantsTertiaryColor = ParseRandomColor(val);
        else if (key == "skates color") p.SkatesColor = ParseRandomColor(val);
        else if (key == "blade color") p.BladeColor = ParseRandomColor(val);
        else if (key == "laces color") p.LacesColor = ParseRandomColor(val);
        else if (key == "bicep color") p.BicepColor = ParseRandomColor(val);
        else if (key == "number color") p.NumberColor = ParseRandomColor(val);
        else if (key == "number secondary color" || key == "number color 2") p.NumberSecondaryColor = ParseRandomColor(val);
        else if (key == "socks color") p.SocksColor = ParseRandomColor(val);
        else if (key == "socks secondary color" || key == "socks color 2") p.SocksSecondaryColor = ParseRandomColor(val);
        else if (key == "socks tertiary color" || key == "socks color 3") p.SocksTertiaryColor = ParseRandomColor(val);
        // Goalie-specific skins
        else if (key == "skin") p.GoalieSkin = val;
        else if (key == "skin away") p.GoalieSkinAway = val;
        else if (key == "glove skin") p.GoalieGloveSkin = val;
        else if (key == "glove away") p.GoalieGloveAway = val;
        else if (key == "blocker skin") p.GoalieBlockerSkin = val;
        else if (key == "blocker away") p.GoalieBlockerAway = val;
        else if (key == "pads skin") p.GoaliePadsSkin = val;
        else if (key == "pads away") p.GoaliePadsAway = val;
        else if (key == "stick skin") p.GoalieStickSkin = val;
        else if (key == "stick away") p.GoalieStickAway = val;
        else if (key == "helmet skin") p.GoalieHelmetSkin = val;
        else if (key == "logo skin") p.GoalieLogoSkin = val;
    }

    // Parses a file as key=value pairs, applying each pair via the provided callback
    private static void ParseKvFile(string path, Action<string, string> apply)
    {
        if (!File.Exists(path)) return;
        foreach (var raw in File.ReadAllLines(path))
        {
            string line = raw.Replace("\t", " ").Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith("#") || line.StartsWith("====")) continue;
            int eqIdx = line.IndexOf('=');
            if (eqIdx < 0) continue;
            string key = line.Substring(0, eqIdx).Trim().ToLower();
            string val = line.Substring(eqIdx + 1).Trim();
            if (string.IsNullOrEmpty(val)) continue;
            apply(key, val);
        }
    }

    private static void LoadTeamFile(string path, TeamConfig team)
    {
        ParseKvFile(path, (k, v) => ApplyTeamField(team, k, v));
    }

    private static void LoadPlayerFile(string path, PlayerConfig player)
    {
        ParseKvFile(path, (k, v) => ApplyPlayerField(player, k, v));
    }

    private static void LoadPlayersFolder(string playersDir, TeamConfig team)
    {
        if (!Directory.Exists(playersDir)) return;

        // Map a position name (case-insensitive) to the matching PlayerConfig slot
        PlayerConfig SlotFor(string pos)
        {
            if (string.IsNullOrEmpty(pos)) return null;
            string p = pos.Trim().ToLower();
            if (p == "goalie") return team.Goalie;
            if (p == "left wing") return team.LW;
            if (p == "right wing") return team.RW;
            if (p == "center") return team.C;
            if (p == "left defense") return team.LD;
            if (p == "right defense") return team.RD;
            if (p == "line 2 left wing") return team.L2_LW;
            if (p == "line 2 right wing") return team.L2_RW;
            if (p == "line 2 center") return team.L2_C;
            if (p == "line 2 left defense") return team.L2_LD;
            if (p == "line 2 right defense") return team.L2_RD;
            return null;
        }

        // New format: any "*.txt" — position is determined by:
        //   1) "Position - Name.txt" filename prefix (preferred)
        //   2) "Position = X" key=value field inside the file
        //   3) Plain "Position.txt" filename (legacy)
        foreach (var file in Directory.GetFiles(playersDir, "*.txt"))
        {
            string filename = Path.GetFileNameWithoutExtension(file);
            string position = null;
            // 1) Prefix split on " - "
            int dashIdx = filename.IndexOf(" - ");
            if (dashIdx > 0)
                position = filename.Substring(0, dashIdx).Trim();
            else
                position = filename.Trim();
            var slot = SlotFor(position);
            // 2) Fall back to inside-file Position field
            if (slot == null)
            {
                string posFromInside = null;
                ParseKvFile(file, (k, v) => { if (k == "position" && posFromInside == null) posFromInside = v; });
                if (posFromInside != null) slot = SlotFor(posFromInside);
            }
            if (slot != null)
            {
                LoadPlayerFile(file, slot);
                Log.LogDebug($"[Campaign] Loaded '{Path.GetFileName(file)}' -> position '{position}', slot Name='{slot.Name}'");
            }
            else
                Log.LogWarning($"[Campaign] Could not determine position for player file: {Path.GetFileName(file)}");
        }
    }

    internal static void LoadCampaignFolders()
    {
        string teamsDir = Path.Combine(ModFolder, "teams");
        if (!Directory.Exists(teamsDir))
        {
            Log.LogInfo("[Campaign] No teams/ folder — using single-file campaign.txt");
            return;
        }
        Log.LogInfo("[Campaign] Loading multi-folder format from teams/");

        // Sort team folders alphabetically (numeric prefix gives play order)
        var teamDirs = Directory.GetDirectories(teamsDir);
        Array.Sort(teamDirs, StringComparer.OrdinalIgnoreCase);

        ConfigTeams = new List<TeamConfig>();
        ConfigTeamDirs = new List<string>();
        foreach (var teamDir in teamDirs)
        {
            var tc = new TeamConfig();
            LoadTeamFile(Path.Combine(teamDir, "team.txt"), tc);
            LoadPlayersFolder(Path.Combine(teamDir, "players"), tc);
            ConfigTeams.Add(tc);
            ConfigTeamDirs.Add(teamDir);
            string name = !string.IsNullOrEmpty(tc.ImportTeam) ? $"IMPORT '{tc.ImportTeam}'" : $"'{tc.Name}'";
            Log.LogInfo($"  Loaded team: {Path.GetFileName(teamDir)} → {name}");
        }
        Log.LogInfo($"[Campaign] Multi-folder: {ConfigTeams.Count} teams loaded");
    }

    internal static void LoadPlayerTeamsFolders(bool loadTeams, bool loadDraft)
    {
        string rootDir = Path.Combine(ModFolder, "player_teams");
        if (!Directory.Exists(rootDir)) return;
        Log.LogInfo($"[PlayerTeam] Loading from player_teams/ (teams={loadTeams}, draft={loadDraft})");

        foreach (var subDir in Directory.GetDirectories(rootDir))
        {
            string folderName = Path.GetFileName(subDir);
            string lower = folderName.ToLower();

            if (lower == "draft_pool" || lower == "draft pool")
            {
                if (!loadDraft) continue;
                foreach (var file in Directory.GetFiles(subDir, "*.txt"))
                {
                    string playerName = Path.GetFileNameWithoutExtension(file);
                    var pc = new PlayerConfig();
                    LoadPlayerFile(file, pc);
                    DraftPoolConfigs[playerName.ToLower()] = pc;
                    Log.LogInfo($"  Draft pool: '{playerName}' loaded");
                }
                continue;
            }

            string teamKey = null;
            bool isPreset = false;
            if (lower.StartsWith("defense")) { teamKey = "defense"; isPreset = true; }
            else if (lower.StartsWith("speed")) { teamKey = "speed"; isPreset = true; }
            else if (lower.StartsWith("basic")) { teamKey = "basic"; isPreset = true; }
            else if (lower.StartsWith("trio")) { teamKey = "trio"; isPreset = true; }
            else teamKey = lower;

            // Preset squads (Basic/Defense/Speedy/Trios) only load when the
            // user opted in with Use Player Teams = yes — they OVERRIDE the
            // vanilla starting lineup. Custom squads (non-preset) always load:
            // they're ADDITIVE new entries in the squad-select menu that the
            // user picks at campaign start, so they never need the toggle.
            if (isPreset && !loadTeams) continue;

            var tc = new TeamConfig();
            LoadTeamFile(Path.Combine(subDir, "team.txt"), tc);
            LoadPlayersFolder(Path.Combine(subDir, "players"), tc);
            PlayerTeamConfigs[teamKey] = tc;
            Log.LogInfo($"  Player team: '{teamKey}' loaded from {folderName}{(isPreset ? "" : " (custom squad)")}");
        }
        Log.LogInfo($"[PlayerTeam] Loaded: {PlayerTeamConfigs.Count} teams, {DraftPoolConfigs.Count} draft pool players");
    }

    // ===== PLAYER TEAM EDITOR =====
    // Draft pool always loads when the folder exists — free agent edits are
    // additive and never replace the game's structure. Starting-team
    // replacement (Defense/Speedy/Basic/Trios) still requires opt-in via
    // `Use Player Teams = yes` because it overwrites the whole lineup.
    internal static void LoadPlayerTeams()
    {
        if (IsDefaultMode) return;
        if (!Directory.Exists(Path.Combine(ModFolder, "player_teams")))
        {
            Log.LogInfo("[PlayerTeam] No player_teams/ folder — nothing to load");
            return;
        }
        if (!UsePlayerTeams)
            Log.LogInfo("[PlayerTeam] Use Player Teams = no — loading draft pool + custom squads only");
        LoadPlayerTeamsFolders(loadTeams: UsePlayerTeams, loadDraft: true);
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

        // "none" = explicitly no skin / no helmet / no glasses.
        // For the helmet slot we return a sentinel so ApplyPlayerConfig can
        // detect the intent and swap the face to a helmetless variant —
        // the game's renderer decides helmet visibility from `headSkin`
        // (via ForwardDataExtensions.HeadsWithoutHelmets), so swapping the
        // face is the safe way to hide it. Other slots still return "".
        if (lower == "none")
        {
            string hint = (slotHint ?? "body").ToLower();
            if (hint == "helmet" || hint == "helmet away") return "__NO_HELMET__";
            return "";
        }

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

        // Bicep skins (fixed options with slot context)
        if (slotHint == "bicep" || slotHint == "bicep away")
        {
            if (lower == "crusaders") return "Body_Bicep/Crusaders";
            if (lower == "crusaders prince" || lower == "crusaders_prince") return "Body_Bicep/Crusaders_Prince";
            if (lower == "figure skaters" || lower == "figure_skaters") return "Body_Bicep/Figure_Skaters";
            if (lower == "golfers") return "Body_Bicep/Golfers";
            if (lower == "hockey fc" || lower == "hockey_fc") return "Body_Bicep/Hockey_FC";
            if (lower == "mountaineers black" || lower == "mountaineers_black") return "Body_Bicep/Mountaineers_Black";
            if (lower == "mountaineers white" || lower == "mountaineers_white") return "Body_Bicep/Mountaineers_White";
            if (lower == "princess") return "Body_Bicep/Princess";
            if (lower == "prisoners") return "Body_Bicep/Prisoners";
            if (lower == "referees" || lower == "referee") return "Body_Bicep/Referees";
            if (lower == "tycoons") return "Body_Bicep/Tycoons";
        }
        if (lower == "standard bicep") return "Body_Bicep/Customization/Customization_colors";

        // Glove skins
        if (lower == "standard gloves") return "Body_Gloves/Customization/Customization_colors";

        // Pants skins
        if (lower == "standard pants") return "Body_Pants/Customization/Customization_colors";

        // Skate skins
        if (lower == "standard skates" || lower == "colored skates")
            return "Body_Skates/Customization/Customization_colors";
        if (lower == "black skates" || lower == "black_skates")
            return "Body_Skates/Black_Skates";

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
            return "Body_Skates/Customization/Customization_colors";
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
    /// Resolve goalie-specific skin names (helmet/body/glove/blocker/pads/stick).
    /// Goalies have different skin paths than skaters.
    /// slot: "helmet" (the mask), "body", "glove", "blocker", "pads", "stick"
    /// </summary>
    internal static string ResolveGoalieSkin(string val, string slot)
    {
        if (string.IsNullOrEmpty(val)) return val;
        string lower = val.Trim().ToLower().Replace("_", " ");
        // Already a full path — return as-is
        if (val.Contains("/")) return val.Trim();

        // Standard / team colors
        if (lower == "standard" || lower == "team colors" || lower == "default" || lower == "colored")
        {
            // Goalie team-tinted mask: this path IS in the game's GOALIE
            // HELMET SKINS list (17 entries) and is what vanilla NPC goalies
            // like Bobby Butcher use. Old ALL_SKIN_OPTIONS.txt in repo root
            // was stale and missed the goalie-helmet section.
            if (slot == "helmet") return "Helmet/Helmet_Customization_colors";
            if (slot == "body") return "Body/Customization_colors";
            if (slot == "glove") return "Body_Glove/Customization/Customization_colors";
            if (slot == "blocker") return "Body_Blocker/Customization/Customization_colors";
            if (slot == "pads") return "Body_Pads/Customization/Customization_colors";
            if (slot == "stick") return "Body_Stick/Customization/Customization_colors";
        }

        if (slot == "helmet")
        {
            // 16 goalie masks
            if (lower == "canadians") return "Helmet/Helmet_Canadians";
            if (lower == "cheese") return "Helmet/Helmet_Cheese";
            if (lower == "cultists") return "Helmet/Helmet_Cultists";
            if (lower == "disco") return "Helmet/Helmet_Disco";
            if (lower == "figure skaters") return "Helmet/Helmet_Figure_Skaters";
            if (lower == "golfers") return "Helmet/Helmet_Golfers";
            if (lower == "hockey fc" || lower == "hockeyfc") return "Helmet/Helmet_HockeyFC";
            if (lower == "knights") return "Helmet/Helmet_Knights";
            if (lower == "meatballs") return "Helmet/Helmet_Meatballs";
            if (lower == "mountaineers") return "Helmet/Helmet_Mountaineers";
            if (lower == "princess") return "Helmet/Helmet_Princess";
            if (lower == "prisoners") return "Helmet/Helmet_Prisoners";
            if (lower == "referees" || lower == "referee") return "Helmet/Helmet_Referees";
            if (lower == "toronto") return "Helmet/Helmet_Toronto";
            if (lower == "tycoons") return "Helmet/Helmet_Tycoons";
        }
        if (slot == "body")
        {
            if (lower == "figure skaters") return "Body/Figure_Skaters";
            if (lower == "golfers") return "Body/Golfers";
            if (lower == "hockey fc" || lower == "hockeyfc") return "Body/HockeyFC";
            if (lower == "knights") return "Body/Knights";
            if (lower == "mountaineers") return "Body/Mountaineers";
            if (lower == "princess") return "Body/Princess";
            if (lower == "prisoners") return "Body/Prisoners";
            if (lower == "referees" || lower == "referee") return "Body/Referees";
            if (lower == "tycoons") return "Body/Tycoons";
        }
        if (slot == "glove")
        {
            if (lower == "brown") return "Body_Glove/Brown";
            if (lower == "figure skaters") return "Body_Glove/Figure_Skaters";
            if (lower == "golfers") return "Body_Glove/Golfers";
            if (lower == "hockey fc" || lower == "hockeyfc") return "Body_Glove/Hockey_FC";
            if (lower == "knights") return "Body_Glove/Knights";
            if (lower == "tycoons") return "Body_Glove/Tycoons";
        }
        if (slot == "blocker")
        {
            if (lower == "brown") return "Body_Blocker/Brown";
            if (lower == "figure skaters") return "Body_Blocker/Figure_Skaters";
            if (lower == "golfers") return "Body_Blocker/Golfers";
            if (lower == "knights") return "Body_Blocker/Knights";
            if (lower == "tycoons") return "Body_Blocker/Tycoons";
        }
        if (slot == "pads")
        {
            if (lower == "brown") return "Body_Pads/Brown";
            if (lower == "figure skaters") return "Body_Pads/Figure_Skaters";
            if (lower == "hockey fc" || lower == "hockeyfc") return "Body_Pads/Hockey_FC";
            if (lower == "tycoons") return "Body_Pads/Tycoons";
        }
        if (slot == "stick")
        {
            if (lower == "figure skaters") return "Body_Stick/Figure_Skaters";
            if (lower == "tycoons") return "Body_Stick/Tycoons";
        }

        // Already a name without a slash — return as-is, maybe it's a full name like "Helmet_Knights"
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

                // Auto-reset: if ANY campaign config file has been modified
                // since save.txt was last written, treat this as a "fresh run"
                // so the user's edits actually take effect. This is the only
                // reliable way to detect "user changed things — please re-apply"
                // without a dedicated UI button.
                if (GamesPlayed > 0 && ShouldAutoResetForEdits())
                {
                    Log.LogInfo($"[Campaign] Config files newer than save — resetting progress so edits apply");
                    ActsCompleted = 0;
                    GamesPlayed = 0;
                    DraftPoolApplied = false;
        AppliedDraftPtrs.Clear();
                    SaveProgress();
                }
            }
        }
        catch { }
    }

    /// <summary>Return true if any campaign config file (campaign.txt,
    /// team.txt's, player files, player_teams/**, defaults.txt) has a newer
    /// mtime than save.txt. Used to force a fresh-run reset when the user
    /// has edited config between sessions.</summary>
    private static bool ShouldAutoResetForEdits()
    {
        try
        {
            if (!File.Exists(SavePath)) return false;
            var saveTime = File.GetLastWriteTimeUtc(SavePath);
            var roots = new List<string> { ModFolder };
            if (!string.IsNullOrEmpty(DefaultsPath) && File.Exists(DefaultsPath)
                && File.GetLastWriteTimeUtc(DefaultsPath) > saveTime)
                return true;
            foreach (var root in roots)
            {
                if (!Directory.Exists(root)) continue;
                foreach (var f in Directory.EnumerateFiles(root, "*.txt", SearchOption.AllDirectories))
                {
                    // Skip save.txt itself
                    if (string.Equals(Path.GetFullPath(f), Path.GetFullPath(SavePath), StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (File.GetLastWriteTimeUtc(f) > saveTime) return true;
                }
            }
        }
        catch { }
        return false;
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
        LoadPlayerTeams();
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

        // Default mode = play vanilla base game. Skip ALL Harmony patches so
        // no mod behavior sneaks in — team remixes, challenge-node replacement,
        // save tracking, library dumping, etc. all stay off. The user can
        // re-enable the mod from active.txt when they want campaign behavior.
        if (IsDefaultMode)
        {
            Log.LogInfo("[Campaign] DEFAULT MODE active — skipping Harmony patches. Game runs 100% vanilla.");
            return;
        }

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

        // Reset our campaign save when player starts a NEW run from the menu.
        // PatchOnRunFinished handles run END; this covers the case where a user
        // abandons a run (without losing) and starts a new one — save.txt still
        // has leftover GamesPlayed/ActsCompleted that would block fresh-run edits.
        try
        {
            var titleNewRun = AccessTools.Method(typeof(Tape2Tape.Hockey.UI.TitleScreen), "NewRun");
            if (titleNewRun != null)
            {
                harmony.Patch(titleNewRun,
                    prefix: new HarmonyMethod(typeof(PatchNewRunStart), nameof(PatchNewRunStart.Prefix)));
                Log.LogInfo("Patched TitleScreen.NewRun — save resets on new-run start!");
            }
        }
        catch (Exception ex) { Log.LogError($"Failed TitleScreen.NewRun: {ex}"); }

        // NOTE: Previously we also patched UI.StartMenu.StartNewRun as a second
        // site to catch new-run starts. Removed because StartMenu.StartNewRun
        // ALSO fires on the Continue Run flow (post-squad-pick "begin playing"
        // coroutine), which was wiping save.txt every time the user clicked
        // Continue. TitleScreen.NewRun alone covers the fresh-run case because
        // the New Run button routes through it before squad selection.

        // Cap free-agent map nodes. After Plugin.MaxFreeAgentNodes, swap
        // further FanNumber1 lookups to GeneralManager so long campaigns
        // don't overflow the roster with uncollectable free agents.
        try
        {
            var getBlueprint = AccessTools.Method(typeof(STS.Map.MapObject), "GetBlueprint");
            if (getBlueprint != null)
            {
                harmony.Patch(getBlueprint,
                    prefix: new HarmonyMethod(typeof(PatchMapBlueprint), nameof(PatchMapBlueprint.Prefix)));
                Log.LogInfo($"Patched MapObject.GetBlueprint — FA nodes capped at {Plugin.MaxFreeAgentNodes}");
            }
            else
            {
                Log.LogWarning("Could not find MapObject.GetBlueprint");
            }
        }
        catch (Exception ex) { Log.LogError($"Failed MapObject.GetBlueprint: {ex}"); }

        // Apply draft-pool mods BEFORE the pick screen opens so players see
        // modded names/stats/skins on the free-agent selection cards.
        // CampaignState.PreGenerateFreeAgents builds the template list that
        // PreGeneratedFreeAgentData references by name — mutating the
        // ForwardData templates now propagates into the pick UI.
        try
        {
            var preGenFA = AccessTools.Method(typeof(State.CampaignState), "PreGenerateFreeAgents");
            if (preGenFA != null)
            {
                harmony.Patch(preGenFA,
                    postfix: new HarmonyMethod(typeof(PatchPreGenerateFreeAgents), nameof(PatchPreGenerateFreeAgents.Postfix)));
                Log.LogInfo("Patched CampaignState.PreGenerateFreeAgents — draft mods visible on pick screen!");
            }
        }
        catch (Exception ex) { Log.LogError($"Failed CampaignState.PreGenerateFreeAgents: {ex}"); }

        // Register custom squads BEFORE the game tries to resume a saved run.
        // Without this, RunDataV2.es3 references a squad id like
        // "customsquad_foo" that isn't in cs.squads yet → game can't resolve
        // the starting squad → only "New Run" is offered. Hooking
        // CampaignState.LoadRunData as a prefix injects our squads into the
        // list before save deserialization runs.
        try
        {
            var loadRunData = AccessTools.Method(typeof(State.CampaignState), "LoadRunData");
            if (loadRunData != null)
            {
                harmony.Patch(loadRunData,
                    prefix: new HarmonyMethod(typeof(PatchLoadRunData), nameof(PatchLoadRunData.Prefix)));
                Log.LogInfo("Patched CampaignState.LoadRunData — custom squads registered before save load!");
            }
            else
            {
                Log.LogWarning("Could not find CampaignState.LoadRunData");
            }
        }
        catch (Exception ex) { Log.LogError($"Failed CampaignState.LoadRunData: {ex}"); }

        // Patch TitleScreen.RefreshCampaignData so custom squads are
        // registered BEFORE the title screen's save-validity check runs.
        // Without this, the Continue Run button is suppressed because the
        // saved squad id can't be resolved at boot.
        try
        {
            var refreshCampaignData = AccessTools.Method(
                AccessTools.TypeByName("Tape2Tape.Hockey.UI.TitleScreen"), "RefreshCampaignData");
            if (refreshCampaignData != null)
            {
                harmony.Patch(refreshCampaignData,
                    prefix: new HarmonyMethod(typeof(PatchTitleScreenRefresh), nameof(PatchTitleScreenRefresh.Prefix)));
                Log.LogInfo("Patched TitleScreen.RefreshCampaignData — custom squads registered before save check!");
            }
            else
            {
                Log.LogWarning("Could not find TitleScreen.RefreshCampaignData");
            }
        }
        catch (Exception ex) { Log.LogError($"Failed TitleScreen.RefreshCampaignData: {ex}"); }

        // Reward-pool filters — filter RelicRepository and TalentRepository
        // so excluded ids (from reward_pools.txt) never show up as random
        // rewards. Postfixes on the random-selector methods mutate the
        // returned list before callers read it.
        try
        {
            var relicRepoType = typeof(RelicRepository);
            var miList = AccessTools.Method(relicRepoType, "GetRandomRelics");
            var miOne  = AccessTools.Method(relicRepoType, "GetRandomRelic");
            if (miList != null)
            {
                harmony.Patch(miList,
                    prefix:  new HarmonyMethod(typeof(PatchFilterRelicRewards), nameof(PatchFilterRelicRewards.Prefix)),
                    postfix: new HarmonyMethod(typeof(PatchFilterRelicRewards), nameof(PatchFilterRelicRewards.Postfix)));
                Log.LogInfo("Patched RelicRepository.GetRandomRelics — reward-pool pre+post filter active");
            }
            if (miOne != null)
            {
                harmony.Patch(miOne,
                    postfix: new HarmonyMethod(typeof(PatchFilterSingleRelicReward), nameof(PatchFilterSingleRelicReward.Postfix)));
            }

            var talentRepoType = AccessTools.TypeByName("TalentRepository");
            if (talentRepoType != null)
            {
                var mTList = AccessTools.Method(talentRepoType, "GetRandomTalents");
                var mTOne  = AccessTools.Method(talentRepoType, "GetRandomTalent");
                if (mTList != null)
                {
                    harmony.Patch(mTList,
                        prefix:  new HarmonyMethod(typeof(PatchFilterTalentRewards), nameof(PatchFilterTalentRewards.Prefix)),
                        postfix: new HarmonyMethod(typeof(PatchFilterTalentRewards), nameof(PatchFilterTalentRewards.Postfix)));
                    Log.LogInfo("Patched TalentRepository.GetRandomTalents — reward-pool pre+post filter active");
                }
                if (mTOne != null)
                {
                    harmony.Patch(mTOne,
                        postfix: new HarmonyMethod(typeof(PatchFilterSingleTalentReward), nameof(PatchFilterSingleTalentReward.Postfix)));
                }
            }
        }
        catch (Exception ex) { Log.LogError($"Failed reward-pool patches: {ex}"); }

        // Inject user-defined custom squads into the campaign squad-select
        // menu. Any `player_teams/<FolderName>/` whose folder isn't one of
        // the four presets (Basic/Defense/Speed/Trio) becomes a brand-new
        // squad option. PatchChooseMetaUI clones an existing squad SO +
        // its startingTeam, renames them to the config key, and appends to
        // CampaignState.squads before the menu builds its buttons.
        try
        {
            // The visible "Choose Your Squad" screen (shows Squad/Records/
            // Starting Relics panels, locked "???" tiles, etc.) is
            // Tape2Tape.Hockey.UI.ChooseMetaMenu — NOT Rogue.ChooseMetaUI
            // (that's a different, simpler controller that isn't triggered
            // by the main-menu flow). SetupMetas() is the method that
            // instantiates a MetaTeamItem per squad in CampaignState.squads.
            var setupMetas = AccessTools.Method(typeof(Tape2Tape.Hockey.UI.ChooseMetaMenu), "SetupMetas");
            if (setupMetas != null)
            {
                harmony.Patch(setupMetas,
                    prefix: new HarmonyMethod(typeof(PatchChooseMetaUI), nameof(PatchChooseMetaUI.PrefixMenu)));
                Log.LogInfo("Patched ChooseMetaMenu.SetupMetas — custom squads appear in menu!");
            }
            else
            {
                Log.LogWarning("Could not find ChooseMetaMenu.SetupMetas");
            }
            // Keep the legacy ChooseMetaUI patch too (no-op if not used).
            var setupButtons = AccessTools.Method(typeof(Rogue.ChooseMetaUI), "SetupMetaTeamButtons");
            if (setupButtons != null)
            {
                harmony.Patch(setupButtons,
                    prefix: new HarmonyMethod(typeof(PatchChooseMetaUI), nameof(PatchChooseMetaUI.Prefix)));
                Log.LogInfo("Patched ChooseMetaUI.SetupMetaTeamButtons (legacy path)");
            }

            // Redirect LocalizedSquadName/LocalizedSquadDesc for Custom_* ids
            // so the menu shows our team's Name/Description instead of "???"
            // from missing localization keys.
            var nameGetter = AccessTools.Method(typeof(RunSquadScriptableObject), "get_LocalizedSquadName");
            if (nameGetter != null)
                harmony.Patch(nameGetter,
                    postfix: new HarmonyMethod(typeof(PatchSquadLocalization), nameof(PatchSquadLocalization.NamePostfix)));
            var descGetter = AccessTools.Method(typeof(RunSquadScriptableObject), "get_LocalizedSquadDesc");
            if (descGetter != null)
                harmony.Patch(descGetter,
                    postfix: new HarmonyMethod(typeof(PatchSquadLocalization), nameof(PatchSquadLocalization.DescPostfix)));
            var unlockGetter = AccessTools.Method(typeof(RunSquadScriptableObject), "get_LocalizedUnlockCondition");
            if (unlockGetter != null)
                harmony.Patch(unlockGetter,
                    postfix: new HarmonyMethod(typeof(PatchSquadLocalization), nameof(PatchSquadLocalization.UnlockPostfix)));
        }
        catch (Exception ex) { Log.LogError($"Failed squad-menu patches: {ex}"); }

        // Show the opponent team name on world-map match nodes so the player
        // knows who they're about to fight before committing to the node.
        try
        {
            var setupTooltip = AccessTools.Method(typeof(STS.Map.MapNode), "SetupTooltip");
            if (setupTooltip != null)
            {
                harmony.Patch(setupTooltip,
                    postfix: new HarmonyMethod(typeof(PatchMapNodeTooltip), nameof(PatchMapNodeTooltip.Postfix)));
                Log.LogInfo("Patched MapNode.SetupTooltip — opponent name visible on world map!");
            }
        }
        catch (Exception ex) { Log.LogError($"Failed MapNode.SetupTooltip: {ex}"); }

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

        // Auto-dump: hook into SetCurrentAct (fires on main menu load) to run on main thread.
        _pendingAutoDump = true;
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
        if (!isWinning) return;

        // Log the actual runtime type so we can diagnose what Spartan/challenge/etc.
        // matches look like. IL2CPP-proxied objects report their wrapper type via
        // GetType() and their real Il2Cpp type via GetIl2CppType().
        string sysTypeName = "";
        string il2TypeName = "";
        try { sysTypeName = __instance?.GetType()?.FullName ?? ""; } catch {}
        try { il2TypeName = __instance?.GetIl2CppType()?.FullName ?? ""; } catch {}
        Plugin.Log.LogInfo($"[Campaign] MatchMapNode.OnGameEnd(win=true) on type sys='{sysTypeName}' il2='{il2TypeName}'");

        // Robust non-regular-match detection. In IL2CPP the `is` operator can be
        // unreliable across proxy boundaries — also check the Il2Cpp type name
        // and the CLR type name for Challenge/Spartan/Gauntlet keywords.
        bool isChallenge = false;
        try { if (__instance is ChallengeMapNode) isChallenge = true; } catch {}
        if (!isChallenge)
        {
            string combined = (sysTypeName + " " + il2TypeName).ToLowerInvariant();
            if (combined.Contains("challenge") || combined.Contains("spartan") || combined.Contains("gauntlet"))
                isChallenge = true;
        }
        if (!isChallenge && __instance != null)
        {
            // Also check the opponent team name — Spartans match opponent is named "Spartans"
            try
            {
                var opp = __instance.opponent;
                string oppName = opp?.teamName ?? "";
                if (oppName.IndexOf("Spartan", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    isChallenge = true;
                    Plugin.Log.LogInfo($"[Campaign] Detected Spartan match via opponent name '{oppName}'");
                }
            }
            catch {}
        }

        if (isChallenge)
        {
            Plugin.Log.LogInfo($"[Campaign] Challenge match won — evaluating whether to count (replace={Plugin.ReplaceChallenges}, map={Plugin.ActsCompleted})");
            if (!Plugin.ReplaceChallenges)
            {
                Plugin.Log.LogInfo("[Campaign] Challenge match won — not counting (replaceChallenges=false)");
                return;
            }
            if (Plugin.ReplaceChallengesMaps != null)
            {
                if (!Plugin.ReplaceChallengesMaps.Contains(Plugin.ActsCompleted))
                {
                    Plugin.Log.LogInfo($"[Campaign] Challenge match won — not counting (map {Plugin.ActsCompleted} not in per-map replace list)");
                    return;
                }
            }
            else if (Plugin.ReplaceChallengesActs != null)
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
// Inject user-defined custom squads into CampaignState.squads before the
// campaign squad-select menu builds its buttons. Each non-preset folder in
// player_teams/ becomes a new squad, cloned from the first existing squad
// so all refs/fields stay valid. The cloned squad's startingTeam is also
// cloned and renamed so PatchPlayerTeamInit's config-matching finds the
// right PlayerTeamConfig entry when the player selects it.
// ============================================================
public static class PatchChooseMetaUI
{
    // The actual in-game "Choose Your Squad" screen (screenshot with locked
    // "???" tiles). Injects custom squads by replacing locked slots in
    // CampaignState.squads BEFORE the menu instantiates its grid items.
    public static void PrefixMenu(Tape2Tape.Hockey.UI.ChooseMetaMenu __instance)
    {
        Plugin.Log.LogInfo($"[CustomSquad] PrefixMenu (ChooseMetaMenu.SetupMetas) called (configs={Plugin.PlayerTeamConfigs?.Count ?? -1})");
        try
        {
            if (Plugin.PlayerTeamConfigs == null || Plugin.PlayerTeamConfigs.Count == 0)
            {
                Plugin.Log.LogInfo("[CustomSquad] No PlayerTeamConfigs loaded — nothing to inject");
                return;
            }

            // Read the injected CampaignState via reflection — the IL2CPP
            // field is `m_CampaignState` here (not `_campaignState` like on
            // the legacy ChooseMetaUI).
            State.CampaignState cs = null;
            try
            {
                var t = __instance.GetIl2CppType();
                var f = t.GetField("m_CampaignState") ?? t.GetField("_campaignState") ?? t.GetField("campaignState");
                if (f != null)
                {
                    var v = f.GetValue(__instance);
                    if (v != null) cs = v.TryCast<State.CampaignState>();
                }
            }
            catch { }
            if (cs == null)
            {
                var all = UnityEngine.Resources.FindObjectsOfTypeAll<State.CampaignState>();
                if (all != null && all.Length > 0) cs = all[0];
            }
            if (cs == null) { Plugin.Log.LogWarning("[CustomSquad] CampaignState not found"); return; }

            InjectCustomSquads(cs);
        }
        catch (Exception ex) { Plugin.Log.LogError($"[CustomSquad] PrefixMenu: {ex}"); }
    }

    internal static void InjectCustomSquads(State.CampaignState cs)
    {
        var squads = cs.squads;
        if (squads == null || squads.Count == 0)
        {
            Plugin.Log.LogWarning("[CustomSquad] cs.squads is null/empty");
            return;
        }

        ProfileData profile = ProfileData.Instance;
        var unlocked = profile?.unlockedSquads;

        var customKeys = new List<string>();
        foreach (var kvp in Plugin.PlayerTeamConfigs)
        {
            if (PatchPlayerTeamInit.IsPresetKey(kvp.Key)) continue;
            customKeys.Add(kvp.Key);
        }
        if (customKeys.Count == 0) { Plugin.Log.LogInfo("[CustomSquad] No custom keys"); return; }

        Plugin.Log.LogInfo($"[CustomSquad] squads={squads.Count}, customKeys={customKeys.Count} — appending as NEW entries");

        // Prefer a template with a FULLY populated starting roster (all 5
        // forward positions non-null). Some squad SOs only fill a couple of
        // forwards — their nulls would prevent our per-slot apply from
        // writing the user's configured players.
        // Pick a 1-line template (exactly 5 forward slots, non-TwoLines).
        // The "Lines Meta"/TwoLines squad has 10 slots (Line 1 at 0-4 +
        // Line 2 at 5-9) and its layout collides with our single-line
        // model. Prefer by name first, then by slot count.
        RunSquadScriptableObject template = null;
        RunSquadScriptableObject fallback = null;
        string[] preferredNames = { "Basic Squad", "Basic", "Defense Squad", "Defense", "Speed", "Speedy" };

        // First pass: search for preferred squad names (case-insensitive),
        // requiring a 5-slot (1-line) layout.
        foreach (var name in preferredNames)
        {
            for (int i = 0; i < squads.Count; i++)
            {
                var sq = squads[i];
                if (sq == null || sq.startingTeam == null) continue;
                var tf = sq.startingTeam.forwards;
                int count = tf?.Count ?? -1;
                if (count != 5) continue;
                if (sq.squadName != null && sq.squadName.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    template = sq;
                    Plugin.Log.LogInfo($"[CustomSquad] Template matched preferred name '{name}' at index {i}");
                    break;
                }
            }
            if (template != null) break;
        }

        // Second pass: any 5-slot squad.
        if (template == null)
        {
            for (int i = 0; i < squads.Count; i++)
            {
                var sq = squads[i];
                if (sq == null || sq.startingTeam == null) continue;
                var tf = sq.startingTeam.forwards;
                int count = tf?.Count ?? -1;
                int nonNull = 0;
                if (tf != null)
                    for (int k = 0; k < tf.Count; k++) if (tf[k] != null) nonNull++;
                Plugin.Log.LogInfo($"[CustomSquad] Template candidate {i}: '{sq.squadName}' fwds={count} nonNull={nonNull}");
                if (count == 5) { template = sq; break; }
                if (fallback == null) fallback = sq;
            }
        }

        if (template == null) template = fallback;
        if (template == null) { Plugin.Log.LogWarning("[CustomSquad] no viable template"); return; }
        Plugin.Log.LogInfo($"[CustomSquad] Chose template '{template.squadName}'");

        for (int i = 0; i < customKeys.Count; i++)
        {
            string key = customKeys[i];
            var cfg = Plugin.PlayerTeamConfigs[key];
            string displayName = !string.IsNullOrEmpty(cfg?.Name)
                ? cfg.Name
                : char.ToUpper(key[0]) + key.Substring(1);
            string customId = "Custom_" + key;

            // Idempotent re-entry: if the clone already exists in the list
            // (repeat Prefix calls on the same menu open), just re-unlock it
            // and skip. This prevents duplicate tiles.
            bool alreadyPresent = false;
            for (int j = 0; j < squads.Count; j++)
            {
                var sq = squads[j];
                if (sq != null && sq.id == customId) { alreadyPresent = true; break; }
            }
            if (alreadyPresent)
            {
                if (unlocked != null && !unlocked.Contains(customId)) unlocked.Add(customId);
                continue;
            }

            try
            {
                var clone = UnityEngine.Object.Instantiate(template);
                clone.name = "CustomSquad_" + key;
                try { clone.squadName = displayName; } catch {}
                try { clone.id = customId; } catch {}

                var origTeam = template.startingTeam;
                if (origTeam != null)
                {
                    var teamClone = UnityEngine.Object.Instantiate(origTeam);
                    teamClone.teamName = key + " " + displayName;

                    Plugin.Log.LogInfo($"[CustomSquad] Cloned team '{origTeam.teamName}' -> '{teamClone.teamName}' fwds={teamClone.forwards?.Count ?? -1} goalie={(teamClone.goalie != null ? teamClone.goalie.firstName : "null")}");

                    // Deep-clone every ForwardData in the roster up front.
                    // Instantiate on TeamData only shallow-clones reference
                    // fields, so the cloned list still points at Basic's
                    // actual player SOs. ApplyPlayerTeamConfig would then
                    // mutate Basic directly (name/stats/talents on Basic's
                    // real LW, RW, etc.) — which corrupts the Basic tile and
                    // eventually wipes all squad rendering once the UI tries
                    // to reuse the stale SOs. Pre-cloning isolates our edits.
                    try { DeepCloneForwards(teamClone); }
                    catch (Exception cEx) { Plugin.Log.LogWarning($"[CustomSquad] DeepCloneForwards: {cEx.Message}"); }

                    // For any user-configured slot whose template forward is
                    // null (e.g. Basic Squad ships with LW/RW/C empty), fill
                    // the slot with a fresh clone of the first non-null
                    // forward in the list — gives ApplyPlayerTeamConfig a
                    // real ForwardData to overwrite with the user's player.
                    try { FillNullConfiguredSlots(teamClone, cfg); }
                    catch (Exception fEx) { Plugin.Log.LogWarning($"[CustomSquad] FillNullConfiguredSlots: {fEx.Message}"); }

                    // Apply the user's team config NOW so the menu preview
                    // shows the custom players (names, stats, skins) — not
                    // the Basic squad's roster we cloned from. firstApply
                    // wipes stale talents/relics from the cloned objects.
                    try { PatchPlayerTeamInit.ApplyPlayerTeamConfig(teamClone, cfg, null, firstApply: true); }
                    catch (Exception apEx) { Plugin.Log.LogWarning($"[CustomSquad] ApplyPlayerTeamConfig failed for '{key}': {apEx.Message}"); }

                    // Blank any slot the user DIDN'T define in their
                    // player_teams/<key>/players/ folder. Without this, the
                    // menu roster still shows Basic's default players in the
                    // unused positions — not what the user wants.
                    try { BlankUnconfiguredSlots(teamClone, cfg); }
                    catch (Exception bEx) { Plugin.Log.LogWarning($"[CustomSquad] Blank slots failed for '{key}': {bEx.Message}"); }

                    // Guarantee a Lineup at lines[0] before the sync — Basic's
                    // cloned TeamData comes back with lines=null sometimes.
                    try { EnsureLines(teamClone); }
                    catch (Exception eEx) { Plugin.Log.LogWarning($"[CustomSquad] EnsureLines failed for '{key}': {eEx.Message}"); }

                    // Sync the cloned team's lines[0] to the new roster.
                    // Lineup stores each position as a ForwardData.id; the
                    // pregame draft/UI reads from it to decide "who's
                    // at LW". If we leave it pointing at Basic's defunct
                    // ids, our configured players don't render and the
                    // free-agent picks get assigned straight into those
                    // line slots, masking the real roster.
                    try { SyncLinesToForwards(teamClone, cfg); }
                    catch (Exception lEx) { Plugin.Log.LogWarning($"[CustomSquad] SyncLines failed for '{key}': {lEx.Message}"); }

                    clone.startingTeam = teamClone;

                    // Point m_KeyPlayer at a position the user actually
                    // defined. The menu renders the squad-tile head icon by
                    // looking up that position on startingTeam — if it
                    // lands on an unconfigured (blanked) slot the icon
                    // resolves to an empty skater and the grid renders
                    // weirdly. Priority: Goalie > C > LW > RW > LD > RD.
                    try
                    {
                        // Priority: forwards first, goalie last. SquadHead
                        // face overrides write to startingTeam.KeyPlayer.
                        // headSkin; goalie head slots can't render forward
                        // face skins — applying one there caused the goalie
                        // to render headless in-game. Forwards handle the
                        // override cleanly, so pick a forward whenever any
                        // is configured.
                        Tape2Tape.Customization.UI.ESkaterPosition? picked = null;
                        if (SlotIsConfigured(cfg.C)) picked = Tape2Tape.Customization.UI.ESkaterPosition.C;
                        else if (SlotIsConfigured(cfg.LW)) picked = Tape2Tape.Customization.UI.ESkaterPosition.LW;
                        else if (SlotIsConfigured(cfg.RW)) picked = Tape2Tape.Customization.UI.ESkaterPosition.RW;
                        else if (SlotIsConfigured(cfg.LD)) picked = Tape2Tape.Customization.UI.ESkaterPosition.LD;
                        else if (SlotIsConfigured(cfg.RD)) picked = Tape2Tape.Customization.UI.ESkaterPosition.RD;
                        else if (SlotIsConfigured(cfg.Goalie)) picked = Tape2Tape.Customization.UI.ESkaterPosition.Goalie;
                        if (picked.HasValue)
                        {
                            clone.m_KeyPlayer = picked.Value;
                            Plugin.Log.LogInfo($"[CustomSquad] KeyPlayer for '{key}' set to {picked.Value}");
                        }

                        // Optional per-squad head override — user's "Squad Head"
                        // field overwrites the key player's face for the tile icon.
                        if (!string.IsNullOrEmpty(cfg?.SquadHead)
                            && !cfg.SquadHead.Equals("none", StringComparison.OrdinalIgnoreCase))
                        {
                            try
                            {
                                string faceSkin = Plugin.ResolveSkin(cfg.SquadHead, "face");
                                int headIdx = picked == Tape2Tape.Customization.UI.ESkaterPosition.LW ? 0
                                    : picked == Tape2Tape.Customization.UI.ESkaterPosition.RW ? 1
                                    : picked == Tape2Tape.Customization.UI.ESkaterPosition.C ? 2
                                    : picked == Tape2Tape.Customization.UI.ESkaterPosition.LD ? 3
                                    : picked == Tape2Tape.Customization.UI.ESkaterPosition.RD ? 4 : -1;
                                if (headIdx >= 0 && clone.startingTeam?.forwards != null
                                    && headIdx < clone.startingTeam.forwards.Count
                                    && clone.startingTeam.forwards[headIdx] != null)
                                {
                                    clone.startingTeam.forwards[headIdx].headSkin = faceSkin;
                                    Plugin.Log.LogInfo($"[CustomSquad] SquadHead '{cfg.SquadHead}' -> '{faceSkin}' applied to {picked} slot");
                                }
                                else if (picked == Tape2Tape.Customization.UI.ESkaterPosition.Goalie
                                         && clone.startingTeam?.goalie != null)
                                {
                                    // Forward face skins (Faces/Princess/Boni etc.) break
                                    // goalie rendering — the goalie skeleton has a different
                                    // slot layout and the head renders empty ("headless").
                                    // Skip the override for goalie-only squads; they'll
                                    // render with the standard bare-goalie face as vanilla
                                    // NPC goalies do.
                                    Plugin.Log.LogInfo($"[CustomSquad] SquadHead '{cfg.SquadHead}' skipped for goalie-only squad — forward faces render headless on goalies (vanilla goalies use empty headSkin)");
                                }
                            }
                            catch (Exception hEx) { Plugin.Log.LogWarning($"[CustomSquad] SquadHead apply: {hEx.Message}"); }
                        }
                    }
                    catch (Exception kpEx) { Plugin.Log.LogWarning($"[CustomSquad] KeyPlayer: {kpEx.Message}"); }
                }

                // Squad-level "Starting Relics" shown in the menu — driven by
                // RunSquadScriptableObject.m_RelicsData (a SquadRelicData[]).
                // Build a fresh array from cfg.Relics so the UI shows the
                // relics the user defined, all IsUnlockedByDefault=true so
                // they render as available not "???"-locked.
                //
                // Bench Bonus is the vanilla player-squad signature relic
                // (Basic Squad ships with it in its m_RelicsData). Our clone
                // overwrites the template's array with cfg.Relics, so the
                // user loses it unless they add it explicitly. Always
                // prepend Bench Bonus here unless the user already listed
                // it (or opted out via "No Bench Bonus = yes"). Keeps custom
                // squads consistent with how player-chosen squads feel.
                {
                    try
                    {
                        var relicsToApply = new List<string>();
                        bool optOut = cfg != null && cfg.NoBenchBonus;
                        bool hasBenchBonus = false;
                        if (cfg?.Relics != null)
                            for (int r = 0; r < cfg.Relics.Count; r++)
                            {
                                var rn = cfg.Relics[r] ?? "";
                                if (rn.Trim().ToLowerInvariant() == "bench bonus") hasBenchBonus = true;
                                relicsToApply.Add(rn);
                            }
                        if (!optOut && !hasBenchBonus) relicsToApply.Insert(0, "Bench Bonus");

                        if (relicsToApply.Count > 0)
                        {
                            var arr = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<State.SquadRelicData>(relicsToApply.Count);
                            int writeIdx = 0;
                            for (int r = 0; r < relicsToApply.Count; r++)
                            {
                                var relic = PatchBossLaunchMatch.FindRelic(relicsToApply[r]);
                                if (relic == null)
                                {
                                    Plugin.Log.LogWarning($"[CustomSquad] Relic '{relicsToApply[r]}' not found for '{key}'");
                                    continue;
                                }
                                var entry = new State.SquadRelicData();
                                entry.Relic = relic;
                                entry.IsUnlockedByDefault = true;
                                arr[writeIdx++] = entry;
                            }
                            if (writeIdx < relicsToApply.Count)
                            {
                                var trimmed = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<State.SquadRelicData>(writeIdx);
                                for (int k2 = 0; k2 < writeIdx; k2++) trimmed[k2] = arr[k2];
                                arr = trimmed;
                            }
                            clone.m_RelicsData = arr;
                            Plugin.Log.LogInfo($"[CustomSquad] Set {writeIdx} starting relics on '{key}'{(hasBenchBonus || optOut ? "" : " (auto-added Bench Bonus)")}");
                        }
                    }
                    catch (Exception rEx) { Plugin.Log.LogWarning($"[CustomSquad] Relic assign failed for '{key}': {rEx.Message}"); }
                }

                // Always append — user asked for NEW entries, not replacement
                // of existing locked tiles. The squad grid expands to fit.
                squads.Add(clone);

                // Register the display strings so the Localized name/desc
                // patches return our text instead of "???".
                Plugin.CustomSquadText[customId] = (displayName, cfg?.Description ?? "");

                Plugin.Log.LogInfo($"[CustomSquad] Appended '{key}' ('{displayName}') at index {squads.Count - 1}");

                // Mark unlocked so the tile is clickable, not greyed out.
                if (unlocked != null && !unlocked.Contains(customId))
                    unlocked.Add(customId);
            }
            catch (Exception ex) { Plugin.Log.LogError($"[CustomSquad] inject '{key}': {ex}"); }
        }
    }

    // Any forward slot (0=LW,1=RW,2=C,3=LD,4=RD) that the user's custom
    // squad DOESN'T define a player file for gets reset to an empty-ish
    // placeholder: no name, neutral stats, no talents/ability/skins, no
    // logo skin. The menu still needs the slot to exist so the lineup
    // preview isn't broken — we just make it visually empty.
    internal static bool SlotIsConfigured(PlayerConfig pc)
    {
        if (pc == null) return false;
        return !string.IsNullOrEmpty(pc.Name)
            || !string.IsNullOrEmpty(pc.ImportPlayer)
            || !string.IsNullOrEmpty(pc.Face)
            || !string.IsNullOrEmpty(pc.Ability)
            || (pc.Talents != null && pc.Talents.Count > 0)
            || pc.Speed != 50 || pc.ShotPower != 50
            || pc.Accuracy != 50 || pc.Checking != 50;
    }

    // Detach team.forwards from whatever it was sharing (Instantiate on
    // TeamData SHALLOW-copies list fields — the clone and the template point
    // at the same underlying List<ForwardData>), then Instantiate every
    // ForwardData / the goalie so edits can't leak back into the template
    // or any other squad that also references those SOs. Each cloned
    // forward also gets a FRESH id so lines[0] can point at each position
    // unambiguously — Instantiate would otherwise copy the donor's id onto
    // every clone, making id→ForwardData lookup collapse to the first match.
    internal static void DeepCloneForwards(TeamData team)
    {
        if (team == null) return;
        var origFwds = team.forwards;
        if (origFwds != null)
        {
            var newList = new Il2CppSystem.Collections.Generic.List<Data.ForwardData>();
            for (int i = 0; i < origFwds.Count; i++)
            {
                var f = origFwds[i];
                if (f == null) { newList.Add(null); continue; }
                try
                {
                    var c = UnityEngine.Object.Instantiate(f);
                    try { c.id = Guid.NewGuid().ToString(); } catch { }
                    newList.Add(c);
                }
                catch { newList.Add(f); }
            }
            try { team.forwards = newList; } catch { }
        }
        if (team.goalie != null)
        {
            try
            {
                var g = UnityEngine.Object.Instantiate(team.goalie);
                try { g.id = Guid.NewGuid().ToString(); } catch { }
                team.goalie = g;
            }
            catch { }
        }
    }

    // Rewrite team.lines[0] so configured positions point at our actual
    // ForwardData's id and unconfigured positions stay empty strings so
    // the pregame draft UI marks them as empty/draftable slots. If we
    // wrote the blank-placeholder's id into lines[0], the draft UI treats
    // the slot as filled-with-a-0-stats-player and won't let the user pick
    // an FA for it. The drafted FA reconcile in PatchPlayerTeamInit.Postfix
    // handles getting the FA into the right slot at match-init time.
    internal static void SyncLinesToForwards(TeamData team, TeamConfig cfg)
    {
        if (team == null) return;
        var fwds = team.forwards;
        if (fwds == null) return;
        var lines = team.lines;
        if (lines == null || lines.Count == 0) return;
        var line0 = lines[0];
        if (line0 == null) return;

        PlayerConfig[] slotCfgs = { cfg?.LW, cfg?.RW, cfg?.C, cfg?.LD, cfg?.RD };

        string IdForConfiguredSlot(int idx)
        {
            if (idx >= fwds.Count) return "";
            if (idx >= slotCfgs.Length) return "";
            if (!SlotIsConfigured(slotCfgs[idx])) return ""; // draftable
            var f = fwds[idx];
            if (f == null) return "";
            try { return f.id ?? ""; } catch { return ""; }
        }

        try { line0.leftWinger = IdForConfiguredSlot(0); } catch { }
        try { line0.rightWinger = IdForConfiguredSlot(1); } catch { }
        try { line0.center = IdForConfiguredSlot(2); } catch { }
        try { line0.leftDefensemen = IdForConfiguredSlot(3); } catch { }
        try { line0.rightDefensemen = IdForConfiguredSlot(4); } catch { }
        Plugin.Log.LogInfo($"[CustomSquad] Synced lines[0]: LW='{line0.leftWinger}' RW='{line0.rightWinger}' C='{line0.center}' LD='{line0.leftDefensemen}' RD='{line0.rightDefensemen}'");
    }

    // For each user-configured LW/RW/C/LD/RD slot whose template forward is
    // null, clone a non-null forward from somewhere else in the same list
    // into that slot. Each clone gets a FRESH id — Instantiate copies the
    // donor's id verbatim, so cloning one donor N times produces N forwards
    // that all share one id. That would collapse the lineup (every
    // lines[0] position resolves to the same forward) and the other
    // clones would vanish from the on-ice roster.
    internal static void FillNullConfiguredSlots(TeamData team, TeamConfig cfg)
    {
        if (team == null || cfg == null) return;
        var fwds = team.forwards;
        if (fwds == null) return;

        // Find a non-null forward to use as the source for blank slots.
        Data.ForwardData donor = null;
        for (int i = 0; i < fwds.Count; i++)
        {
            if (fwds[i] != null) { donor = fwds[i]; break; }
        }
        if (donor == null) return;

        PlayerConfig[] slotCfgs = { cfg.LW, cfg.RW, cfg.C, cfg.LD, cfg.RD };
        for (int i = 0; i < Math.Min(fwds.Count, slotCfgs.Length); i++)
        {
            if (fwds[i] != null) continue;
            if (!SlotIsConfigured(slotCfgs[i])) continue;
            try
            {
                var clone = UnityEngine.Object.Instantiate(donor);
                try { clone.id = Guid.NewGuid().ToString(); } catch { }
                fwds[i] = clone;
                Plugin.Log.LogInfo($"[CustomSquad] Filled null slot {i} with clone (new id={clone.id})");
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"[CustomSquad] FillNullConfiguredSlots {i}: {ex.Message}"); }
        }
    }

    // Guarantee team.lines has a Lineup at index 0. Basic's cloned TeamData
    // has come back with lines=null in testing — without a Lineup instance,
    // SyncLinesToForwards has nothing to write to and the game can't figure
    // out who starts at each position. We create one via reflection/new and
    // populate only lines[0]; the game only needs index 0 for the default
    // active line.
    internal static void EnsureLines(TeamData team)
    {
        if (team == null) return;
        try
        {
            var lines = team.lines;
            if (lines == null)
            {
                lines = new Il2CppSystem.Collections.Generic.List<Data.Lineup>();
                team.lines = lines;
            }
            if (lines.Count == 0)
            {
                var lu = new Data.Lineup(team);
                lines.Add(lu);
                Plugin.Log.LogInfo("[CustomSquad] Created lines[0] (team.lines was empty)");
            }
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[CustomSquad] EnsureLines: {ex.Message}"); }
    }

    // For each unconfigured LW/RW/C/LD/RD slot, replace whatever is in that
    // index with a BLANK PLACEHOLDER clone (unique id, empty identity,
    // zero stats). SyncLinesToForwards will leave lines[0].<position>
    // empty for these slots so the pregame draft UI still treats them as
    // draftable — but the game needs a non-null ForwardData object sitting
    // in the fwds slot so drafted free agents can overwrite it. Null slots
    // cause drafted FAs to silently drop (their id goes into lines[0] but
    // no ForwardData ever lands in forwards).
    internal static void BlankUnconfiguredSlots(TeamData team, TeamConfig cfg)
    {
        if (team == null || cfg == null) return;
        PlayerConfig[] slotCfgs = { cfg.LW, cfg.RW, cfg.C, cfg.LD, cfg.RD };
        string[] slotNames = { "LW", "RW", "C", "LD", "RD" };
        var fwds = team.forwards;
        if (fwds == null) return;

        Data.ForwardData donor = null;
        for (int i = 0; i < fwds.Count; i++)
            if (fwds[i] != null) { donor = fwds[i]; break; }
        if (donor == null) return;

        for (int i = 0; i < Math.Min(fwds.Count, slotCfgs.Length); i++)
        {
            bool configured = SlotIsConfigured(slotCfgs[i]);
            string pcName = slotCfgs[i]?.Name ?? "<null>";
            Plugin.Log.LogDebug($"[CustomSquad] Slot {slotNames[i]}: cfg.Name='{pcName}' configured={configured} fwd={(fwds[i] != null ? fwds[i].firstName : "null")}");
            if (configured) continue;
            try
            {
                var blank = UnityEngine.Object.Instantiate(donor);
                try { blank.id = Guid.NewGuid().ToString(); } catch { }
                try { blank.firstName = ""; } catch { }
                try { blank.lastName = ""; } catch { }
                try { blank.speed = 0; } catch { }
                try { blank.shotPower = 0; } catch { }
                try { blank.shotAccuracy = 0; } catch { }
                try { blank.checking = 0; } catch { }
                try { blank.ability = null; } catch { }
                try { blank.powerups = new Il2CppSystem.Collections.Generic.List<Rogue.Talent>(); } catch { }
                fwds[i] = blank;
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"[CustomSquad] Placeholder slot {i}: {ex.Message}"); }
        }
        // Goalie: blank identity rather than null. Previous tests showed that
        // assigning null to team.goalie while the squad was being rendered
        // broke the menu — some renderer does an unchecked deref even though
        // the forwards list tolerates nulls just fine.
        if (team.goalie != null && !SlotIsConfigured(cfg.Goalie))
        {
            try { team.goalie.firstName = ""; } catch { }
            try { team.goalie.lastName = ""; } catch { }
        }
    }

    public static void Prefix(Rogue.ChooseMetaUI __instance)
    {
        // Unconditional entry log so we can confirm the hook is firing.
        Plugin.Log.LogInfo($"[CustomSquad] Prefix called (configs={Plugin.PlayerTeamConfigs?.Count ?? -1})");
        try
        {
            if (Plugin.PlayerTeamConfigs == null || Plugin.PlayerTeamConfigs.Count == 0)
            {
                Plugin.Log.LogInfo("[CustomSquad] No PlayerTeamConfigs loaded — nothing to inject");
                return;
            }

            // Find CampaignState via the _campaignState field on ChooseMetaUI
            var t = __instance.GetIl2CppType();
            var csField = t.GetField("_campaignState") ?? t.GetField("m_CampaignState") ?? t.GetField("campaignState");
            State.CampaignState cs = null;
            if (csField != null)
            {
                var v = csField.GetValue(__instance);
                if (v != null) cs = v.TryCast<State.CampaignState>();
            }
            if (cs == null)
            {
                var all = UnityEngine.Resources.FindObjectsOfTypeAll<State.CampaignState>();
                if (all != null && all.Length > 0) cs = all[0];
            }
            if (cs == null)
            {
                Plugin.Log.LogWarning("[CustomSquad] CampaignState not found — cannot inject");
                return;
            }

            var squads = cs.squads;
            if (squads == null || squads.Count == 0)
            {
                Plugin.Log.LogWarning("[CustomSquad] cs.squads is null/empty — cannot inject");
                return;
            }

            // ProfileData.unlockedSquads is the source of truth for whether a
            // button is clickable or shows as "???". To make custom squads
            // selectable we must (1) put them in cs.squads and (2) add their
            // id to unlockedSquads.
            ProfileData profile = ProfileData.Instance;
            var unlocked = profile?.unlockedSquads;

            // Build ordered list of keys we want to inject. The squad grid in
            // the UI has a fixed slot count; adding past it means our squads
            // don't render. So we REPLACE the locked slots instead of adding.
            var customKeys = new List<string>();
            foreach (var kvp in Plugin.PlayerTeamConfigs)
            {
                if (PatchPlayerTeamInit.IsPresetKey(kvp.Key)) continue;
                customKeys.Add(kvp.Key);
            }
            if (customKeys.Count == 0)
            {
                Plugin.Log.LogInfo("[CustomSquad] No custom (non-preset) keys in config");
                return;
            }

            // Find locked slots = indices whose squad.id isn't in unlocked.
            var lockedIndices = new List<int>();
            for (int i = 0; i < squads.Count; i++)
            {
                var sq = squads[i];
                if (sq == null) continue;
                bool isUnlocked = unlocked != null
                    && !string.IsNullOrEmpty(sq.id)
                    && unlocked.Contains(sq.id);
                if (!isUnlocked) lockedIndices.Add(i);
            }
            Plugin.Log.LogInfo($"[CustomSquad] squads={squads.Count}, locked slots={lockedIndices.Count}, custom keys={customKeys.Count}");

            // Template to clone — first squad in the list; its internal refs
            // (relics, icons, etc.) are all valid so the cloned entry won't
            // crash when the UI/game reads them.
            var template = squads[0];
            if (template == null)
            {
                Plugin.Log.LogWarning("[CustomSquad] squads[0] is null — cannot clone");
                return;
            }

            int injected = 0;
            int lockedCursor = 0;
            for (int i = 0; i < customKeys.Count; i++)
            {
                string key = customKeys[i];
                var cfg = Plugin.PlayerTeamConfigs[key];
                string displayName = !string.IsNullOrEmpty(cfg?.Name)
                    ? cfg.Name
                    : char.ToUpper(key[0]) + key.Substring(1);
                string customId = "Custom_" + key;

                // Idempotent re-entry: if this custom squad is already sitting
                // in cs.squads from a previous Prefix call, skip it.
                bool alreadyPresent = false;
                for (int j = 0; j < squads.Count; j++)
                {
                    var sq = squads[j];
                    if (sq != null && sq.id == customId) { alreadyPresent = true; break; }
                }
                if (alreadyPresent)
                {
                    if (unlocked != null && !unlocked.Contains(customId))
                        unlocked.Add(customId);
                    continue;
                }

                try
                {
                    var clone = UnityEngine.Object.Instantiate(template);
                    clone.name = "CustomSquad_" + key;
                    try { clone.squadName = displayName; } catch {}
                    try { clone.id = customId; } catch {}

                    var origTeam = template.startingTeam;
                    if (origTeam != null)
                    {
                        var teamClone = UnityEngine.Object.Instantiate(origTeam);
                        // Team name must START WITH the config key so the
                        // PatchPlayerTeamInit prefix-match can find it.
                        teamClone.teamName = key + " " + displayName;
                        clone.startingTeam = teamClone;
                    }

                    // Slot in: REPLACE a locked slot if one is available, else
                    // append. Replacement keeps the UI grid bounded so custom
                    // squads always render.
                    if (lockedCursor < lockedIndices.Count)
                    {
                        int idx = lockedIndices[lockedCursor++];
                        squads[idx] = clone;
                        Plugin.Log.LogInfo($"[CustomSquad] Replaced locked slot {idx} with '{key}' ('{displayName}')");
                    }
                    else
                    {
                        squads.Add(clone);
                        Plugin.Log.LogInfo($"[CustomSquad] Appended '{key}' ('{displayName}') at {squads.Count - 1}");
                    }

                    // Mark it unlocked so the button is clickable.
                    if (unlocked != null && !unlocked.Contains(customId))
                        unlocked.Add(customId);

                    injected++;
                }
                catch (Exception ex) { Plugin.Log.LogError($"[CustomSquad] Failed to inject '{key}': {ex}"); }
            }
            Plugin.Log.LogInfo($"[CustomSquad] Done — injected {injected} custom squads");
        }
        catch (Exception ex) { Plugin.Log.LogError($"[CustomSquad] SetupMetaTeamButtons prefix: {ex}"); }
    }
}

// ============================================================
// Redirect RunSquadScriptableObject.LocalizedSquadName / Desc / UnlockCondition
// for Custom_<key> ids so the menu shows our display name + description
// instead of "???" (localization misses fall back to the key literal).
// ============================================================
public static class PatchSquadLocalization
{
    public static void NamePostfix(RunSquadScriptableObject __instance, ref string __result)
    {
        try
        {
            if (__instance == null) return;
            string id = __instance.id;
            if (string.IsNullOrEmpty(id)) return;
            if (!id.StartsWith("Custom_", StringComparison.Ordinal)) return;
            if (Plugin.CustomSquadText.TryGetValue(id, out var v) && !string.IsNullOrEmpty(v.name))
                __result = v.name;
            else if (!string.IsNullOrEmpty(__instance.squadName))
                __result = __instance.squadName;
        }
        catch { }
    }

    public static void DescPostfix(RunSquadScriptableObject __instance, ref string __result)
    {
        try
        {
            if (__instance == null) return;
            string id = __instance.id;
            if (string.IsNullOrEmpty(id)) return;
            if (!id.StartsWith("Custom_", StringComparison.Ordinal)) return;
            if (Plugin.CustomSquadText.TryGetValue(id, out var v))
                __result = string.IsNullOrEmpty(v.desc) ? "Custom squad." : v.desc;
            else
                __result = "Custom squad.";
        }
        catch { }
    }

    public static void UnlockPostfix(RunSquadScriptableObject __instance, ref string __result)
    {
        try
        {
            if (__instance == null) return;
            string id = __instance.id;
            if (string.IsNullOrEmpty(id)) return;
            // Custom squads are always unlocked — blank the unlock condition.
            if (id.StartsWith("Custom_", StringComparison.Ordinal))
                __result = "";
        }
        catch { }
    }
}

// ============================================================
// Reset campaign progress when player starts a NEW run from menu.
// Run-end reset (PatchOnRunFinished) only fires on loss/final victory;
// this catches abandonment cases where save.txt still has stale values.
// ============================================================
public static class PatchNewRunStart
{
    public static void Prefix()
    {
        try
        {
            // Guard: if a Continue Run just loaded (within 15s), this call is
            // part of the Continue flow, not a fresh NewRun — do NOT wipe save.
            float dt = UnityEngine.Time.realtimeSinceStartup - Plugin.LastLoadRunDataTime;
            if (Plugin.LastLoadRunDataTime > 0f && dt < 15f)
            {
                Plugin.Log.LogInfo($"[Campaign] NewRun prefix skipped — Continue Run active ({dt:F1}s since LoadRunData)");
                return;
            }
            Plugin.ActsCompleted = 0;
            Plugin.GamesPlayed = 0;
            Plugin.DraftPoolApplied = false;
            Plugin.AppliedDraftPtrs.Clear();
            Plugin.BossJustBeaten = false;
            Plugin.FreeAgentNodesPlaced = 0;
            Plugin.SaveProgress();
            Plugin.Log.LogInfo("[Campaign] New run starting — save reset (ActsCompleted=0, GamesPlayed=0)");
        }
        catch (Exception ex) { Plugin.Log.LogError($"[Campaign] NewRunStart reset: {ex.Message}"); }
    }
}

// ============================================================
// Register custom squads before the game loads saved run data. Without
// this, RunDataV2.es3 references a squad id like "customsquad_foo" that
// can't be resolved because our squads are only injected when the user
// opens the Choose Your Squad menu (i.e. on New Run flow). Running
// InjectCustomSquads here means "Continue Run" works and the saved run
// resumes with the right custom squad.
// ============================================================
public static class PatchLoadRunData
{
    private static bool injected = false;
    public static void Prefix(State.CampaignState __instance)
    {
        try
        {
            // Stamp Continue Run timing so PatchNewRunStart can avoid wiping
            // save.txt when StartMenu.StartNewRun (which fires on BOTH Continue
            // and New Run flows) triggers our reset code by mistake.
            Plugin.LastLoadRunDataTime = UnityEngine.Time.realtimeSinceStartup;
            if (injected) return; // only once per game launch
            if (__instance == null) return;
            if (Plugin.PlayerTeamConfigs == null || Plugin.PlayerTeamConfigs.Count == 0) return;
            PatchChooseMetaUI.InjectCustomSquads(__instance);
            injected = true;
            Plugin.Log.LogInfo("[Campaign] Custom squads registered via LoadRunData prefix — Continue Run should work");
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[Campaign] LoadRunData prefix: {ex.Message}"); }
    }

    // Reset the "already injected" latch so the next squads-reload cycle
    // (e.g. when the user re-opens the title screen without restarting)
    // can re-run.
    internal static void ResetLatch() { injected = false; }
}

// ============================================================
// Register custom squads BEFORE TitleScreen validates the save. The
// earlier PatchLoadRunData fires only when the user clicks "Continue
// Run" — but the Continue Run button is gated by RefreshCampaignData's
// save-validity check, which runs at title-screen load. If our custom
// squads aren't in cs.squads at that moment, the saved squad id can't
// be resolved and the Continue button never appears. Hooking
// TitleScreen.RefreshCampaignData as a prefix ensures squads are
// registered before the check runs.
// ============================================================
public static class PatchTitleScreenRefresh
{
    public static void Prefix()
    {
        try
        {
            if (Plugin.PlayerTeamConfigs == null || Plugin.PlayerTeamConfigs.Count == 0) return;
            State.CampaignState cs = null;
            var all = UnityEngine.Resources.FindObjectsOfTypeAll<State.CampaignState>();
            if (all != null && all.Length > 0) cs = all[0];
            if (cs == null) { Plugin.Log.LogInfo("[Campaign] TitleScreen.RefreshCampaignData prefix: CampaignState not found yet"); return; }
            if (cs.squads == null || cs.squads.Count == 0) { Plugin.Log.LogInfo("[Campaign] TitleScreen.RefreshCampaignData prefix: cs.squads not populated yet"); return; }
            PatchChooseMetaUI.InjectCustomSquads(cs);
            Plugin.Log.LogInfo("[Campaign] Custom squads registered via TitleScreen.RefreshCampaignData prefix");
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[Campaign] TitleScreen.RefreshCampaignData prefix: {ex.Message}"); }
    }
}

// ============================================================
// Free-agent (GM-choice) node cap. Long campaigns stack up GeneralManager
// nodes — each offers a choice of free agents to recruit — beyond the
// roster's capacity, which crashes the game when the 5th+ FA tries to
// sign. Patch MapObject.GetBlueprint to redirect GeneralManager lookups
// to TeamTraining (team upgrade) once Plugin.MaxFreeAgentNodes have been
// placed this run.
// ============================================================
public static class PatchMapBlueprint
{
    public static void Prefix(ref STS.Map.NodeType type)
    {
        try
        {
            if (type != STS.Map.NodeType.GeneralManager) return;
            if (Plugin.FreeAgentNodesPlaced >= Plugin.MaxFreeAgentNodes)
            {
                type = STS.Map.NodeType.TeamTraining;
                Plugin.Log.LogInfo($"[Campaign] FA node cap ({Plugin.MaxFreeAgentNodes}) reached — substituting TeamTraining");
            }
            else
            {
                Plugin.FreeAgentNodesPlaced++;
                Plugin.Log.LogInfo($"[Campaign] FA node placed #{Plugin.FreeAgentNodesPlaced}/{Plugin.MaxFreeAgentNodes}");
            }
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[Campaign] MapBlueprint prefix: {ex.Message}"); }
    }
}

// ============================================================
// Reward-pool filtering. Harmony postfixes on RelicRepository /
// TalentRepository strip excluded IDs (loaded from reward_pools.txt) out
// of the lists the game shows as random rewards. We mutate the returned
// list in-place so all callers downstream see the filtered pool without
// needing the shop / reward menu to know anything changed.
// ============================================================
public static class PatchFilterRelicRewards
{
    // PREFIX: add our excluded ids to the `excludedRelics` arg that the game
    // already passes in, so GetRandomRelics NEVER picks them in the first
    // place. Much safer than a postfix strip — the game would return empty
    // ("Rogue rewards have invalid data") if every picked relic got filtered.
    public static void Prefix(
        Il2CppSystem.Collections.Generic.List<Rogue.Relic> excludedRelics,
        RelicRepository __instance)
    {
        try
        {
            if (Plugin.ExcludedRewardRelicIds.Count == 0) return;
            if (__instance == null || excludedRelics == null) return;
            int added = 0;
            foreach (var id in Plugin.ExcludedRewardRelicIds)
            {
                if (string.IsNullOrEmpty(id)) continue;
                try
                {
                    var relic = __instance.GetRelic(id, false);
                    if (relic == null) continue;
                    bool present = false;
                    for (int j = 0; j < excludedRelics.Count; j++)
                        if (excludedRelics[j] != null && excludedRelics[j].id == id) { present = true; break; }
                    if (!present) { excludedRelics.Add(relic); added++; }
                }
                catch { }
            }
            if (added > 0) Plugin.Log.LogInfo($"[RewardPool] Relic prefilter: added {added} exclusions to GetRandomRelics call");
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[RewardPool] Relic prefilter: {ex.Message}"); }
    }

    // POSTFIX safety net: if the game somehow still returned an excluded relic
    // (e.g. it re-cloned internally and skipped our prefix), strip it — but
    // NEVER leave the list empty, since callers treat that as an error.
    public static void Postfix(Il2CppSystem.Collections.Generic.List<Rogue.Relic> __result)
    {
        try
        {
            if (__result == null || __result.Count == 0) return;
            if (Plugin.ExcludedRewardRelicIds.Count == 0) return;
            int before = __result.Count;
            for (int i = __result.Count - 1; i >= 0 && __result.Count > 1; i--)
            {
                var r = __result[i];
                if (r == null) continue;
                if (Plugin.ExcludedRewardRelicIds.Contains(r.id ?? ""))
                    __result.RemoveAt(i);
            }
            if (__result.Count != before)
                Plugin.Log.LogInfo($"[RewardPool] Relic postfix safety-net: {before} -> {__result.Count} (kept at least 1 to avoid empty rewards)");
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[RewardPool] Relic postfix: {ex.Message}"); }
    }
}

public static class PatchFilterSingleRelicReward
{
    public static void Postfix(ref Rogue.Relic __result)
    {
        try
        {
            if (__result == null) return;
            if (Plugin.ExcludedRewardRelicIds.Count == 0) return;
            if (Plugin.ExcludedRewardRelicIds.Contains(__result.id ?? ""))
            {
                // The chosen relic is excluded — null it out. The caller
                // may or may not handle null gracefully; this is a best-
                // effort fallback since most paths go through GetRandomRelics
                // (the list form) which we filter cleanly above.
                Plugin.Log.LogInfo($"[RewardPool] Excluded relic '{__result.id}' was picked — returning null");
                __result = null;
            }
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[RewardPool] Single-relic filter: {ex.Message}"); }
    }
}

public static class PatchFilterTalentRewards
{
    public static void Prefix(
        Il2CppSystem.Collections.Generic.List<Rogue.Talent> excludedTalents,
        TalentRepository __instance)
    {
        try
        {
            if (Plugin.ExcludedRewardTalentIds.Count == 0) return;
            if (__instance == null || excludedTalents == null) return;
            int added = 0;
            foreach (var id in Plugin.ExcludedRewardTalentIds)
            {
                if (string.IsNullOrEmpty(id)) continue;
                try
                {
                    var t = __instance.GetTalent(id, false);
                    if (t == null) continue;
                    bool present = false;
                    for (int j = 0; j < excludedTalents.Count; j++)
                        if (excludedTalents[j] != null && excludedTalents[j].name == id) { present = true; break; }
                    if (!present) { excludedTalents.Add(t); added++; }
                }
                catch { }
            }
            if (added > 0) Plugin.Log.LogInfo($"[RewardPool] Talent prefilter: added {added} exclusions to GetRandomTalents call");
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[RewardPool] Talent prefilter: {ex.Message}"); }
    }

    public static void Postfix(Il2CppSystem.Collections.Generic.List<Rogue.Talent> __result)
    {
        try
        {
            if (__result == null || __result.Count == 0) return;
            if (Plugin.ExcludedRewardTalentIds.Count == 0) return;
            int before = __result.Count;
            for (int i = __result.Count - 1; i >= 0 && __result.Count > 1; i--)
            {
                var t = __result[i];
                if (t == null) continue;
                if (Plugin.ExcludedRewardTalentIds.Contains(t.name ?? ""))
                    __result.RemoveAt(i);
            }
            if (__result.Count != before)
                Plugin.Log.LogInfo($"[RewardPool] Talent postfix safety-net: {before} -> {__result.Count} (kept at least 1)");
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[RewardPool] Talent postfix: {ex.Message}"); }
    }
}

public static class PatchFilterSingleTalentReward
{
    public static void Postfix(ref Rogue.Talent __result)
    {
        try
        {
            if (__result == null) return;
            if (Plugin.ExcludedRewardTalentIds.Count == 0) return;
            if (Plugin.ExcludedRewardTalentIds.Contains(__result.name ?? ""))
            {
                Plugin.Log.LogInfo($"[RewardPool] Excluded talent '{__result.name}' was picked — returning null");
                __result = null;
            }
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[RewardPool] Single-talent filter: {ex.Message}"); }
    }
}

// ============================================================
// Apply draft-pool mods to ForwardData templates BEFORE the free-agent
// pick UI reads them — without this, pick cards show vanilla names/looks
// until the player actually signs someone and the team re-initializes.
// ============================================================
public static class PatchPreGenerateFreeAgents
{
    // Cache the output list Harmony hands us so ApplyDraftPool can read it
    // without needing to locate the property/field on CampaignState.
    internal static Il2CppSystem.Collections.Generic.List<Rogue.FreeAgents.PreGeneratedFreeAgentData> LastOutput;

    public static void Postfix(
        Il2CppSystem.Collections.Generic.List<Data.ForwardData> __1,
        Il2CppSystem.Collections.Generic.List<Rogue.FreeAgents.PreGeneratedFreeAgentData> __2)
    {
        try
        {
            if (Plugin.DraftPoolConfigs == null || Plugin.DraftPoolConfigs.Count == 0) return;
            LastOutput = __2;
            int templatesCount = __1?.Count ?? -1;
            int outCount = __2?.Count ?? -1;
            Plugin.Log.LogInfo($"[Campaign] PreGenerateFreeAgents postfix — templates={templatesCount} output={outCount}");
            PatchPlayerTeamInit.ApplyDraftPool();
        }
        catch (Exception ex) { Plugin.Log.LogError($"[Campaign] PreGenerateFreeAgents: {ex.Message}"); }
    }
}

// ============================================================
// Append opponent team name to world-map node tooltip so the player
// can see which team they're about to fight before entering the node.
// ============================================================
public static class PatchMapNodeTooltip
{
    public static void Postfix(STS.Map.MapNode __instance)
    {
        try
        {
            if (__instance == null) return;
            // Only MatchMapNode (and subclasses: EliteMapNode, BossMapNode,
            // ChallengeMapNode) have an opponent team.
            var mm = __instance.TryCast<STS.Map.MatchMapNode>();
            if (mm == null) return;
            var opp = mm.opponent;
            if (opp == null) return;
            string teamName = opp.teamName;
            if (string.IsNullOrEmpty(teamName)) return;

            // Append "vs <Team>" to the tooltip description via reflection —
            // the tooltip text field name varies (m_TooltipDesc / tooltipDesc).
            var t = __instance.GetIl2CppType();
            var field = t.GetField("m_TooltipDesc") ?? t.GetField("tooltipDesc") ?? t.GetField("m_TooltipDescription");
            if (field == null) return;
            var tmp = field.GetValue(__instance);
            if (tmp == null) return;
            var tmpType = tmp.GetIl2CppType();
            var textProp = tmpType.GetProperty("text");
            if (textProp == null) return;
            var cur = textProp.GetValue(tmp)?.ToString() ?? "";
            string suffix = $"\nvs {teamName}";
            if (!cur.Contains(suffix.Trim()))
                textProp.SetValue(tmp, (Il2CppSystem.Object)(cur + suffix));
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[Campaign] MapNodeTooltip: {ex.Message}"); }
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
        // Run actually ended (loss or final victory) — reset progress so the
        // next fresh run gets mod edits applied again. The fresh-run guard on
        // Team.Initialize uses GamesPlayed == 0 to decide whether to apply
        // team/relic/uniform edits; leaving it non-zero after a lost run
        // permanently blocks edits on the new run's game 1.
        Plugin.ActsCompleted = 0;
        Plugin.GamesPlayed = 0;
        Plugin.DraftPoolApplied = false;
        Plugin.AppliedDraftPtrs.Clear();  // allow re-application to fresh forwards
        Plugin.SaveProgress();
        Plugin.Log.LogInfo("[Campaign] Run ended — ActsCompleted + GamesPlayed reset to 0 for next run");
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

        // Trigger auto-dump on first SetCurrentAct call (fires on main menu load)
        if (Plugin._pendingAutoDump)
        {
            Plugin._pendingAutoDump = false;
            try { LogRepositories.AutoDumpNameLists(); }
            catch (Exception ex) { Plugin.Log.LogWarning($"[Dump] Auto-dump failed: {ex.Message}"); }
        }
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
            // Per-map override (new format: maps:1,3,5)
            if (Plugin.ReplaceChallengesMaps != null)
            {
                if (!Plugin.ReplaceChallengesMaps.Contains(Plugin.ActsCompleted))
                {
                    Plugin.Log.LogInfo($"[MapGen] Challenge node kept (map {Plugin.ActsCompleted} not in per-map replace list)");
                    return;
                }
            }
            // Legacy per-act filtering
            else if (Plugin.ReplaceChallengesActs != null)
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
        // X-Ray Shot: the asset is "xRay" but users write "XRay Shot" / "X-Ray Shot"
        { "XRay Shot", "xRay" },
        { "X-Ray Shot", "xRay" },
        { "X Ray Shot", "xRay" },
        { "XRay", "xRay" },
        { "X-Ray", "xRay" },
    };

    internal static Rogue.Talent FindTalent(string name)
    {
        if (CachedTalentRepo?.talents == null) return null;
        // Check aliases first
        if (TalentAliases.TryGetValue(name, out string aliased))
            name = aliased;

        // 1) Strict match on internal asset name ("Always Catch Pucks")
        for (int i = 0; i < CachedTalentRepo.talents.Count; i++)
            if (CachedTalentRepo.talents[i]?.name == name)
                return CachedTalentRepo.talents[i];

        // 2) Case-insensitive match on internal name
        for (int i = 0; i < CachedTalentRepo.talents.Count; i++)
        {
            var t = CachedTalentRepo.talents[i];
            if (t != null && t.name != null &&
                t.name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return t;
        }

        // 3) Match on localized display name ("Infinimitt", "Coach's Favorite")
        for (int i = 0; i < CachedTalentRepo.talents.Count; i++)
        {
            var t = CachedTalentRepo.talents[i];
            if (t == null) continue;
            try
            {
                string key = t.powerupName;
                if (!string.IsNullOrEmpty(key))
                {
                    string display = LocalizationManager.GetTranslation(key, true, 0, true, false, null, null, true);
                    if (!string.IsNullOrEmpty(display) &&
                        display.Equals(name, StringComparison.OrdinalIgnoreCase))
                        return t;
                }
            }
            catch {}
        }

        return null;
    }

    internal static Rogue.Relic[] AllRelicCache;
    internal static Rogue.Relic FindRelic(string nameContains, int level = 1)
    {
        // Search ALL relics in memory, not just loaded repo lists
        if (AllRelicCache == null)
            AllRelicCache = UnityEngine.Resources.FindObjectsOfTypeAll<Rogue.Relic>();
        if (AllRelicCache == null) return null;

        // 1) Try localized display name ("Steel Mitts", "Bench Bonus")
        foreach (var r in AllRelicCache)
        {
            if (r == null || r.level != level) continue;
            try
            {
                string key = r.relicName;
                if (!string.IsNullOrEmpty(key))
                {
                    string display = LocalizationManager.GetTranslation(key, true, 0, true, false, null, null, true);
                    if (!string.IsNullOrEmpty(display) &&
                        display.Equals(nameContains, StringComparison.OrdinalIgnoreCase))
                        return r;
                }
            }
            catch {}
        }

        // 2) Try relicName (localization key) contains
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

    internal static string ColorToRGB(Color c)
    {
        return $"{(int)(c.r * 255)}, {(int)(c.g * 255)}, {(int)(c.b * 255)}";
    }

    internal static string ReverseSkinPath(string path, string slot)
    {
        if (string.IsNullOrEmpty(path)) return "";
        string lower = path.ToLower();
        if (lower.Contains("customization_colors") || lower.Contains("helmet_colors"))
            return slot == "helmet" ? "team colors" : "standard";
        if (lower.Contains("helmet_face")) return "cage";
        // Extract last segment as friendly name
        string last = path.Contains("/") ? path.Substring(path.LastIndexOf('/') + 1) : path;
        // Known sticks
        if (lower.StartsWith("sticks/")) return last.ToLower();
        // Known bodies/biceps/etc
        string[] knownNames = { "tycoons", "princess", "golfers", "prisoners", "mountaineers",
            "hockey_fc", "figure_skaters", "referee", "knights", "crusaders", "cultists" };
        foreach (var n in knownNames)
            if (lower.Contains(n)) return n;
        if (lower.Contains("black_skates")) return "black skates";
        return last;
    }

    private static void MaterializeTeamToDisk(string teamDir, TeamData team, string importName)
    {
        Plugin.Log.LogInfo($"[Materialize] Writing resolved team to {teamDir}");
        var sb = new System.Text.StringBuilder();

        // Write team.txt with resolved data (no Import Team line)
        sb.AppendLine($"# Materialized from Import Team = {importName}");
        sb.AppendLine($"Team Name               = {team.teamName}");
        if (!string.IsNullOrEmpty(team.city)) sb.AppendLine($"City                    = {team.city}");
        if (!string.IsNullOrEmpty(team.nickname)) sb.AppendLine($"Abbreviation            = {team.nickname}");
        sb.AppendLine($"Logo From               = {importName}");

        // Colors from homeColors
        try
        {
            var hc = team.homeColors;
            if (hc != null)
            {
                if (hc.jerseyScheme != null)
                {
                    sb.AppendLine($"Jersey Primary          = {PatchBossLaunchMatch.ColorToRGB(hc.jerseyScheme.primaryColor)}");
                    sb.AppendLine($"Jersey Secondary        = {PatchBossLaunchMatch.ColorToRGB(hc.jerseyScheme.secondaryColor)}");
                    sb.AppendLine($"Jersey Accent           = {PatchBossLaunchMatch.ColorToRGB(hc.jerseyScheme.tertiaryColor)}");
                }
                if (hc.helmetScheme != null)
                {
                    sb.AppendLine($"Helmet Color            = {PatchBossLaunchMatch.ColorToRGB(hc.helmetScheme.primaryColor)}");
                    sb.AppendLine($"Helmet Secondary Color  = {PatchBossLaunchMatch.ColorToRGB(hc.helmetScheme.secondaryColor)}");
                    sb.AppendLine($"Helmet Tertiary Color   = {PatchBossLaunchMatch.ColorToRGB(hc.helmetScheme.tertiaryColor)}");
                }
                if (hc.glovesScheme != null)
                {
                    sb.AppendLine($"Gloves Color            = {PatchBossLaunchMatch.ColorToRGB(hc.glovesScheme.primaryColor)}");
                    sb.AppendLine($"Gloves Secondary Color  = {PatchBossLaunchMatch.ColorToRGB(hc.glovesScheme.secondaryColor)}");
                    sb.AppendLine($"Gloves Tertiary Color   = {PatchBossLaunchMatch.ColorToRGB(hc.glovesScheme.tertiaryColor)}");
                }
                if (hc.pantsScheme != null)
                {
                    sb.AppendLine($"Pants Color             = {PatchBossLaunchMatch.ColorToRGB(hc.pantsScheme.primaryColor)}");
                    sb.AppendLine($"Pants Secondary Color   = {PatchBossLaunchMatch.ColorToRGB(hc.pantsScheme.secondaryColor)}");
                    sb.AppendLine($"Pants Tertiary Color    = {PatchBossLaunchMatch.ColorToRGB(hc.pantsScheme.tertiaryColor)}");
                }
                if (hc.skatesScheme != null)
                {
                    sb.AppendLine($"Skates Color            = {PatchBossLaunchMatch.ColorToRGB(hc.skatesScheme.primaryColor)}");
                    sb.AppendLine($"Blade Color             = {PatchBossLaunchMatch.ColorToRGB(hc.skatesScheme.secondaryColor)}");
                    sb.AppendLine($"Laces Color             = {PatchBossLaunchMatch.ColorToRGB(hc.skatesScheme.tertiaryColor)}");
                }
                if (hc.socksScheme != null)
                {
                    sb.AppendLine($"Socks Color             = {PatchBossLaunchMatch.ColorToRGB(hc.socksScheme.primaryColor)}");
                    sb.AppendLine($"Socks Secondary Color   = {PatchBossLaunchMatch.ColorToRGB(hc.socksScheme.secondaryColor)}");
                    sb.AppendLine($"Socks Tertiary Color    = {PatchBossLaunchMatch.ColorToRGB(hc.socksScheme.tertiaryColor)}");
                }
                if (hc.numberScheme != null)
                {
                    sb.AppendLine($"Number Color Home       = {PatchBossLaunchMatch.ColorToRGB(hc.numberScheme.primaryColor)}");
                    sb.AppendLine($"Number Color Away       = {PatchBossLaunchMatch.ColorToRGB(hc.numberScheme.secondaryColor)}");
                }
            }
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[Materialize] Color export error: {ex.Message}"); }

        // Uniform skins
        sb.AppendLine($"Body                    = standard");
        sb.AppendLine($"Bicep                   = standard");
        sb.AppendLine($"Gloves                  = standard");
        sb.AppendLine($"Pants                   = standard");
        sb.AppendLine($"Skates                  = standard");
        sb.AppendLine($"Helmet                  = team colors");
        sb.AppendLine($"Helmet Away             = team colors");
        sb.AppendLine($"Stick                   = black");

        File.WriteAllText(Path.Combine(teamDir, "team.txt"), sb.ToString());

        // Write player files
        string playersDir = Path.Combine(teamDir, "players");
        Directory.CreateDirectory(playersDir);

        var forwards = team.GetForwards();
        if (forwards != null)
        {
            string[] posNames = { "Left Wing", "Right Wing", "Center", "Left Defense", "Right Defense" };
            for (int i = 0; i < Math.Min(forwards.Count, posNames.Length); i++)
            {
                var f = forwards[i];
                if (f == null) continue;
                string pos = posNames[i];
                string pname = $"{f.firstName} {f.lastName}".Trim();
                if (string.IsNullOrEmpty(pname)) pname = pos;
                string safe = pname;
                foreach (char c in Path.GetInvalidFileNameChars()) safe = safe.Replace(c, '_');

                var psb = new System.Text.StringBuilder();
                psb.AppendLine($"Name                    = {pname}");
                psb.AppendLine($"Number                  = {f.number}");
                try { psb.AppendLine($"Face                    = {PatchBossLaunchMatch.ReverseSkinPath(f.headSkin, "face")}"); } catch {}
                psb.AppendLine($"Left Handed             = {(f.isLefty ? "yes" : "no")}");
                psb.AppendLine($"Skin Color              = {(f.isBlack ? "dark" : "light")}");
                psb.AppendLine($"Size                    = {f.skaterSize}");
                psb.AppendLine($"Speed                   = {f.speed}");
                psb.AppendLine($"Shot Power              = {f.shotPower}");
                psb.AppendLine($"Accuracy                = {f.shotAccuracy}");
                psb.AppendLine($"Checking                = {f.checking}");
                // Skins
                try { psb.AppendLine($"Stick                   = {PatchBossLaunchMatch.ReverseSkinPath(f.stickSkin, "stick")}"); } catch {}
                try { psb.AppendLine($"Helmet                  = {PatchBossLaunchMatch.ReverseSkinPath(f.helmetSkin, "helmet")}"); } catch {}
                try { psb.AppendLine($"Body                    = {PatchBossLaunchMatch.ReverseSkinPath(f.bodySkin, "body")}"); } catch {}
                try { psb.AppendLine($"Bicep                   = {PatchBossLaunchMatch.ReverseSkinPath(f.bicepSkin, "bicep")}"); } catch {}
                psb.AppendLine($"Gloves                  = standard");
                psb.AppendLine($"Pants                   = standard");
                try { psb.AppendLine($"Skates                  = {PatchBossLaunchMatch.ReverseSkinPath(f.skateSkin, "skates")}"); } catch {}
                // Talents
                try
                {
                    if (f.powerups != null && f.powerups.Count > 0)
                    {
                        var tnames = new List<string>();
                        for (int ti = 0; ti < f.powerups.Count; ti++)
                            if (f.powerups[ti] != null)
                                tnames.Add(f.powerups[ti].name);
                        if (tnames.Count > 0)
                            psb.AppendLine($"Talents                 = {string.Join(", ", tnames)}");
                    }
                } catch {}

                string fname = $"{pos} - {safe}.txt";
                File.WriteAllText(Path.Combine(playersDir, fname), psb.ToString());
            }
        }

        // Goalie
        try
        {
            var g = team.goalie;
            if (g != null)
            {
                string gname = $"{g.firstName} {g.lastName}".Trim();
                if (string.IsNullOrEmpty(gname)) gname = "Goalie";
                string gsafe = gname;
                foreach (char c in Path.GetInvalidFileNameChars()) gsafe = gsafe.Replace(c, '_');

                var gsb = new System.Text.StringBuilder();
                gsb.AppendLine($"Name                    = {gname}");
                gsb.AppendLine($"Face                    = Helmet_Face");
                gsb.AppendLine($"Skill                   = {g.catchingSkill}");
                gsb.AppendLine($"Catching                = {g.catchingSkill}");
                gsb.AppendLine($"Glove                   = {g.gloveSkill}");
                gsb.AppendLine($"Blocker                 = {g.blockerSkill}");
                gsb.AppendLine($"Five Hole               = {g.fiveHoleSkill}");
                gsb.AppendLine($"Standing Speed          = {g.standingSpeed}");
                gsb.AppendLine($"Butterfly Speed         = {g.butterflySpeed}");
                gsb.AppendLine($"Control                 = {g.controlSkill}");
                gsb.AppendLine($"Recovery                = {g.recoverySkill}");
                gsb.AppendLine($"Pass Power              = {g.passPower}");
                gsb.AppendLine($"Shot Power              = {g.shotPower}");
                gsb.AppendLine($"Poke Check              = {g.pokecheckSkill}");
                gsb.AppendLine($"Depth                   = {g.depth}");
                try { gsb.AppendLine($"Pass Read               = {g.passReadSkill}"); } catch {}
                // Goalie talents
                try
                {
                    if (g.powerups != null && g.powerups.Count > 0)
                    {
                        var tnames = new List<string>();
                        for (int ti = 0; ti < g.powerups.Count; ti++)
                            if (g.powerups[ti] != null)
                                tnames.Add(g.powerups[ti].name);
                        if (tnames.Count > 0)
                            gsb.AppendLine($"Goalie Talents          = {string.Join(", ", tnames)}");
                    }
                } catch {}

                File.WriteAllText(Path.Combine(playersDir, $"Goalie - {gsafe}.txt"), gsb.ToString());
            }
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[Materialize] Goalie write error: {ex.Message}"); }

        // Also copy to library
        try
        {
            string libDir = Path.Combine(Plugin.ModContentRoot, "library", "teams", team.teamName);
            if (!Directory.Exists(libDir))
            {
                CopyDirectory(teamDir, libDir);
                Plugin.Log.LogInfo($"[Materialize] Copied to library: {libDir}");
            }
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[Materialize] Library copy failed: {ex.Message}"); }

        Plugin.Log.LogInfo($"[Materialize] Team '{team.teamName}' written to disk ({teamDir})");
    }

    private static void CopyDirectory(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var file in Directory.GetFiles(src))
            File.Copy(file, Path.Combine(dst, Path.GetFileName(file)), true);
        foreach (var dir in Directory.GetDirectories(src))
            CopyDirectory(dir, Path.Combine(dst, Path.GetFileName(dir)));
    }

    internal static void ApplyTeamFromConfig(TeamData team, TeamConfig cfg)
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

            // Materialize: write resolved data to disk so users can edit it later.
            // Find the team's folder path and write team.txt + player files.
            int cfgIdx = Plugin.ConfigTeams.IndexOf(cfg);
            if (cfgIdx >= 0 && cfgIdx < Plugin.ConfigTeamDirs.Count)
            {
                try
                {
                    MaterializeTeamToDisk(Plugin.ConfigTeamDirs[cfgIdx], team, cfg.ImportTeam);
                }
                catch (Exception ex) { Plugin.Log.LogWarning($"[Materialize] Failed: {ex.Message}"); }
            }

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
            ApplyTeamEquipmentColorsToGoalie(team.goalie, team, cfg);
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
                    // NOTE: Previously this cloned f.colorSchemes to isolate per-player colors,
                    // but testing showed that broke application entirely — the game renderer
                    // didn't read the cloned instance, so overrides didn't reach the mesh.
                    // The shared-ref write IS what the game reads; bleed across the team was
                    // not observed in practice (likely because every player on the team shares
                    // team colors anyway, so "bleed" == "expected behavior" for unset fields).
                    SetSchemeColor(f.colorSchemes.jerseyScheme, pc.JerseyColor, pc.JerseySecondaryColor, pc.JerseyAccentColor);
                    SetSchemeColor(f.colorSchemes.glovesScheme, pc.GlovesColor, pc.GlovesSecondaryColor, pc.GlovesTertiaryColor);
                    SetSchemeColor(f.colorSchemes.helmetScheme, pc.HelmetColor, pc.HelmetSecondaryColor, pc.HelmetTertiaryColor);
                    SetSchemeColor(f.colorSchemes.pantsScheme, pc.PantsColor, pc.PantsSecondaryColor, pc.PantsTertiaryColor);
                    SetSchemeColor(f.colorSchemes.skatesScheme, pc.SkatesColor, pc.BladeColor, pc.LacesColor);
                    SetSchemeColor(f.colorSchemes.socksScheme, pc.SocksColor, pc.SocksSecondaryColor, pc.SocksTertiaryColor);
                    SetSchemeColor(f.colorSchemes.numberScheme, pc.NumberColor, pc.NumberSecondaryColor, null);
                    if (pc.BicepColor != null)
                        f.colorSchemes.jerseyScheme.secondaryColor = new Color(pc.BicepColor[0]/255f, pc.BicepColor[1]/255f, pc.BicepColor[2]/255f);
                    Plugin.Log.LogInfo($"[Color] Applied per-player overrides to {f.firstName} {f.lastName}");
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

    internal static void ApplyGoalieColorOverrides(GoaltenderData g, PlayerConfig pc)
    {
        if (g == null || pc == null) return;

        bool hasAny = pc.JerseyColor != null || pc.JerseySecondaryColor != null ||
            pc.JerseyAccentColor != null || pc.GlovesColor != null || pc.GlovesSecondaryColor != null ||
            pc.GlovesTertiaryColor != null || pc.HelmetColor != null || pc.HelmetSecondaryColor != null ||
            pc.HelmetTertiaryColor != null || pc.PantsColor != null || pc.PantsSecondaryColor != null ||
            pc.PantsTertiaryColor != null || pc.SkatesColor != null || pc.BladeColor != null ||
            pc.LacesColor != null || pc.BicepColor != null || pc.SocksColor != null ||
            pc.SocksSecondaryColor != null || pc.SocksTertiaryColor != null ||
            pc.NumberColor != null || pc.NumberSecondaryColor != null;
        if (!hasAny) return;

        try
        {
            // GoaltenderData has a colorSchemes field like ForwardData
            if (g.colorSchemes != null)
            {
                SetSchemeColor(g.colorSchemes.jerseyScheme, pc.JerseyColor, pc.JerseySecondaryColor, pc.JerseyAccentColor);
                SetSchemeColor(g.colorSchemes.glovesScheme, pc.GlovesColor, pc.GlovesSecondaryColor, pc.GlovesTertiaryColor);
                SetSchemeColor(g.colorSchemes.helmetScheme, pc.HelmetColor, pc.HelmetSecondaryColor, pc.HelmetTertiaryColor);
                SetSchemeColor(g.colorSchemes.pantsScheme, pc.PantsColor, pc.PantsSecondaryColor, pc.PantsTertiaryColor);
                SetSchemeColor(g.colorSchemes.skatesScheme, pc.SkatesColor, pc.BladeColor, pc.LacesColor);
                SetSchemeColor(g.colorSchemes.socksScheme, pc.SocksColor, pc.SocksSecondaryColor, pc.SocksTertiaryColor);
                SetSchemeColor(g.colorSchemes.numberScheme, pc.NumberColor, pc.NumberSecondaryColor, null);
                Plugin.Log.LogInfo($"[Color] Applied goalie color overrides to '{g.firstName} {g.lastName}'");
            }

        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[Color] Goalie color override error: {ex.Message}");
        }
    }

    internal static void ApplyTeamEquipmentColors(ForwardData f, TeamConfig cfg)
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

    /// <summary>
    /// Give the goalie the SAME color schemes as the team's forwards so the
    /// customizable mask (Helmet_Customization_colors) renders in team colors.
    /// Painting individual fields on g.colorSchemes.helmetScheme didn't stick —
    /// sharing the reference does.
    /// </summary>
    internal static void ApplyTeamEquipmentColorsToGoalie(GoaltenderData g, TeamData team, TeamConfig cfg)
    {
        if (g == null || team == null) return;
        try
        {
            if (team.homeColors != null)
            {
                g.colorSchemes = team.homeColors;
                Plugin.Log.LogInfo($"[Config] Goalie '{g.firstName} {g.lastName}' colorSchemes linked to team homeColors");
            }
            // Then still paint per-field cfg overrides on top (in case user
            // set specific Helmet Color values for this team).
            if (g.colorSchemes != null && cfg != null)
            {
                // Helmet color fallback chain: explicit TeamHelmet* →
                // jersey home colors → leave whatever the template had.
                // Ensures the team-tinted goalie mask has SOME sensible
                // color even when the user only set jersey colors.
                int[] helmetPrimary = cfg.TeamHelmetColor ?? cfg.JerseyPrimary;
                int[] helmetSecondary = cfg.TeamHelmetSecondary ?? cfg.JerseySecondary;
                int[] helmetTertiary = cfg.TeamHelmetTertiary ?? cfg.JerseyAccent;
                SetSchemeColor(g.colorSchemes.helmetScheme, helmetPrimary, helmetSecondary, helmetTertiary);
                SetSchemeColor(g.colorSchemes.glovesScheme, cfg.TeamGlovesColor, cfg.TeamGlovesSecondary, cfg.TeamGlovesTertiary);
                SetSchemeColor(g.colorSchemes.pantsScheme, cfg.TeamPantsColor, cfg.TeamPantsSecondary, cfg.TeamPantsTertiary);
                SetSchemeColor(g.colorSchemes.skatesScheme, cfg.TeamSkatesColor, cfg.TeamBladeColor, cfg.TeamLacesColor);
                SetSchemeColor(g.colorSchemes.socksScheme, cfg.TeamSocksColor, cfg.TeamSocksSecondary, cfg.TeamSocksTertiary);
                SetSchemeColor(g.colorSchemes.numberScheme, cfg.TeamNumberColor, cfg.TeamNumberSecondary, null);
            }
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[Config] Goalie team equipment color error: {ex.Message}"); }
    }

    internal static void ApplyPlayerConfig(ForwardData f, PlayerConfig pc, UniformConfig uniform)
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

            // Helmet=none handling via shared helper — see HandleNoHelmetSentinel
            // for details. Registers the face in HeadsWithoutHelmets so helmet
            // is skipped at render time, keeping the user's chosen face intact.
            PatchPlayerTeamInit.HandleNoHelmetSentinel(f);

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
            // Skip helmet default-fill for players explicitly flagged as
            // no-helmet (Helmet = none). Without this gate, the fallback
            // would re-stamp Helmet_Colors onto them and the helmet would
            // render as a grey team-colored shell on opponent teams.
            bool noHelmet = PatchPlayerTeamInit.NoHelmetForwards.Contains(f.Pointer);
            if (!noHelmet)
            {
                if (string.IsNullOrEmpty(f.helmetSkin))
                    f.helmetSkin = !string.IsNullOrEmpty(du.Helmet) ? du.Helmet : "Faces/Custom/Helmet_Colors";
                if (string.IsNullOrEmpty(f.helmetAwaySkin))
                    f.helmetAwaySkin = !string.IsNullOrEmpty(du.HelmetAway) ? du.HelmetAway : f.helmetSkin;
            }
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

    /// <summary>
    /// Add a skin path to the goalie's private per-field list (_skins,
    /// _helmetSkins, _gloveSkins, etc.). These lists are the pool the Spine
    /// skeleton actually loads at runtime — setting g.helmetSkin to a path
    /// NOT in _helmetSkins causes the slot to render empty (headless). We
    /// must add the path to the pool whenever we override a skin.
    /// </summary>
    internal static void EnsureGoalieSkinInPool(GoaltenderData g, string fieldName, string path)
    {
        if (g == null || string.IsNullOrEmpty(path)) return;
        try
        {
            var field = g.GetType().GetField(fieldName,
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field == null) return;
            var list = field.GetValue(g) as Il2CppSystem.Collections.Generic.List<string>;
            if (list == null)
            {
                list = new Il2CppSystem.Collections.Generic.List<string>();
                field.SetValue(g, list);
            }
            for (int i = 0; i < list.Count; i++)
                if (list[i] == path) return;
            list.Add(path);
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[Config] EnsureGoalieSkinInPool({fieldName}='{path}'): {ex.Message}"); }
    }

    /// <summary>
    /// Copy every private _*Skins pool from a donor NPC goalie into the
    /// target goalie. We populate the target's pool with the UNION of donor
    /// + target so no pre-loaded skins are lost. Use this when cloning a
    /// player-team template (Bobby Butcher) but the user wants a themed
    /// mask (Knights, Canadians, etc.) that's only pre-loaded on the
    /// corresponding NPC goalie (Sir Godfrey, etc.).
    /// </summary>
    internal static void CopyGoalieSkinPoolsFrom(GoaltenderData target, GoaltenderData donor)
    {
        if (target == null || donor == null) return;
        string[] pools = { "_skins", "_awaySkin", "_helmetSkins", "_headSkins",
                           "_logoSkins", "_blockerSkins", "_gloveSkins",
                           "_padSkins", "_stickSkins" };
        foreach (var p in pools)
        {
            try
            {
                var f = donor.GetType().GetField(p,
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (f == null) continue;
                var dList = f.GetValue(donor) as Il2CppSystem.Collections.Generic.List<string>;
                if (dList == null) continue;
                for (int i = 0; i < dList.Count; i++)
                    if (!string.IsNullOrEmpty(dList[i])) EnsureGoalieSkinInPool(target, p, dList[i]);
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"[Config] CopyGoalieSkinPoolsFrom({p}): {ex.Message}"); }
        }
    }

    /// <summary>
    /// Scan all GoaltenderData in the asset pool and return the first one
    /// whose helmetSkin matches the requested path. Used to find an NPC
    /// goalie whose Spine pool already contains the helmet so we can copy
    /// its skin pools onto our custom-squad clone.
    /// </summary>
    internal static GoaltenderData FindGoalieWithHelmet(string helmetPath)
    {
        if (string.IsNullOrEmpty(helmetPath)) return null;
        try
        {
            var all = UnityEngine.Resources.FindObjectsOfTypeAll<GoaltenderData>();
            if (all == null) return null;
            for (int i = 0; i < all.Length; i++)
            {
                var cand = all[i];
                if (cand == null) continue;
                if (cand.helmetSkin == helmetPath) return cand;
            }
        }
        catch { }
        return null;
    }

    internal static void ApplyGoalieConfig(GoaltenderData g, PlayerConfig pc)
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

            // Face — resolve through ResolveSkin like skaters do
            if (!string.IsNullOrEmpty(pc.Face))
                g.headSkin = Plugin.ResolveSkin(pc.Face);

            // Goalie-specific skins (all resolved through ResolveGoalieSkin for friendly names)
            // Each override also gets registered in the private pool (_skins,
            // _helmetSkins, etc.) — without that the Spine skeleton won't load
            // the asset and the slot renders empty.
            if (!string.IsNullOrEmpty(pc.GoalieSkin)) try { g.skin = Plugin.ResolveGoalieSkin(pc.GoalieSkin, "body"); EnsureGoalieSkinInPool(g, "_skins", g.skin); } catch {}
            if (!string.IsNullOrEmpty(pc.GoalieSkinAway)) try { g.awaySkin = Plugin.ResolveGoalieSkin(pc.GoalieSkinAway, "body"); EnsureGoalieSkinInPool(g, "_awaySkin", g.awaySkin); } catch {}
            if (!string.IsNullOrEmpty(pc.GoalieGloveSkin)) try { g.gloveSkin = Plugin.ResolveGoalieSkin(pc.GoalieGloveSkin, "glove"); EnsureGoalieSkinInPool(g, "_gloveSkins", g.gloveSkin); } catch {}
            if (!string.IsNullOrEmpty(pc.GoalieGloveAway)) try { g.awayGloveSkin = Plugin.ResolveGoalieSkin(pc.GoalieGloveAway, "glove"); EnsureGoalieSkinInPool(g, "_gloveSkins", g.awayGloveSkin); } catch {}
            if (!string.IsNullOrEmpty(pc.GoalieBlockerSkin)) try { g.blockerSkin = Plugin.ResolveGoalieSkin(pc.GoalieBlockerSkin, "blocker"); EnsureGoalieSkinInPool(g, "_blockerSkins", g.blockerSkin); } catch {}
            if (!string.IsNullOrEmpty(pc.GoalieBlockerAway)) try { g.awayBlockerSkin = Plugin.ResolveGoalieSkin(pc.GoalieBlockerAway, "blocker"); EnsureGoalieSkinInPool(g, "_blockerSkins", g.awayBlockerSkin); } catch {}
            if (!string.IsNullOrEmpty(pc.GoaliePadsSkin)) try { g.padsSkin = Plugin.ResolveGoalieSkin(pc.GoaliePadsSkin, "pads"); EnsureGoalieSkinInPool(g, "_padSkins", g.padsSkin); } catch {}
            if (!string.IsNullOrEmpty(pc.GoaliePadsAway)) try { g.awayPadsSkin = Plugin.ResolveGoalieSkin(pc.GoaliePadsAway, "pads"); EnsureGoalieSkinInPool(g, "_padSkins", g.awayPadsSkin); } catch {}
            if (!string.IsNullOrEmpty(pc.GoalieStickSkin)) try { g.stickSkin = Plugin.ResolveGoalieSkin(pc.GoalieStickSkin, "stick"); EnsureGoalieSkinInPool(g, "_stickSkins", g.stickSkin); } catch {}
            if (!string.IsNullOrEmpty(pc.GoalieStickAway)) try { g.awayStickSkin = Plugin.ResolveGoalieSkin(pc.GoalieStickAway, "stick"); EnsureGoalieSkinInPool(g, "_stickSkins", g.awayStickSkin); } catch {}
            if (!string.IsNullOrEmpty(pc.GoalieHelmetSkin))
                try
                {
                    var rh = Plugin.ResolveGoalieSkin(pc.GoalieHelmetSkin, "helmet");
                    if (!string.IsNullOrEmpty(rh))
                    {
                        g.helmetSkin = rh;
                        EnsureGoalieSkinInPool(g, "_helmetSkins", rh);
                        // If an NPC goalie already uses this helmet path, copy
                        // all their private skin pools onto ours. Their Spine
                        // skeleton knows how to load this helmet; ours inherits
                        // that knowledge. Critical for themed masks (Knights,
                        // Canadians, etc.) that aren't pre-loaded on the
                        // player-team template goalie we cloned from.
                        var donor = FindGoalieWithHelmet(rh);
                        if (donor != null && donor != g)
                        {
                            CopyGoalieSkinPoolsFrom(g, donor);
                            Plugin.Log.LogInfo($"[Config] Goalie helmet pool donor found for '{rh}': '{donor.firstName} {donor.lastName}' — skin pools copied");
                        }
                        else
                        {
                            Plugin.Log.LogInfo($"[Config] No NPC donor found for helmet '{rh}' (path may still load if already in pool)");
                        }
                    }
                }
                catch (Exception ex) { Plugin.Log.LogWarning($"[Config] Goalie helmet apply: {ex.Message}"); }
            if (!string.IsNullOrEmpty(pc.GoalieLogoSkin)) try { g.logoSkin = pc.GoalieLogoSkin; EnsureGoalieSkinInPool(g, "_logoSkins", pc.GoalieLogoSkin); } catch {}

            // Goalies render with a mask placed over the bare goalie face
            // (headSkin). If helmetSkin is empty we'd see the bare face with
            // no mask, so fill in the team-tinted default — the same path
            // vanilla NPC goalies (Bobby Butcher etc.) use.
            try
            {
                if (string.IsNullOrEmpty(g.helmetSkin))
                    g.helmetSkin = "Helmet/Helmet_Customization_colors";
            }
            catch { }

            // Diagnostic dump so we can see what actually got applied at
            // runtime (distinct from what config requested).
            try
            {
                Plugin.Log.LogInfo(
                    $"[GoalieDbg] '{g.firstName} {g.lastName}' " +
                    $"cfg.helmet='{pc.GoalieHelmetSkin}' -> g.helmetSkin='{g.helmetSkin}' " +
                    $"g.headSkin='{g.headSkin}' g.skin='{g.skin}' g.awaySkin='{g.awaySkin}' " +
                    $"colorsNull={(g.colorSchemes == null)} " +
                    $"helmetSchemePri={(g.colorSchemes?.helmetScheme == null ? "null" : g.colorSchemes.helmetScheme.primaryColor.ToString())}");
            }
            catch { }

            // Fallback defaults for goalie from defaults.txt
            var dg = Plugin.DefaultGoalie;
            if (string.IsNullOrEmpty(g.firstName) && string.IsNullOrEmpty(g.lastName))
            {
                string defName = !string.IsNullOrEmpty(dg.Name) ? dg.Name : "Goalie";
                var np = defName.Split(' ', 2);
                g.firstName = np[0];
                g.lastName = np.Length > 1 ? np[1] : "";
            }
            // Vanilla goalies ALL have empty headSkin (confirmed: 92/92 NPC
            // goalies in ALL_TEAMS_FULL have headSkin=""). The helmet mask
            // fully covers the head, so no face skin is needed — and forcing
            // one (e.g. Faces/Custom/Helmet_Face) on a goalie skeleton slot
            // caused rendering to break on some squads. Leave as-is.

            // Talents
            if (pc.Talents != null)
                foreach (var t in pc.Talents)
                    if (!string.IsNullOrEmpty(t)) GiveGoalieTalent(g, t);

            // Per-goalie color overrides (override team colors on specific equipment)
            ApplyGoalieColorOverrides(g, pc);
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

    internal static void CopyGoalieData(GoaltenderData src, GoaltenderData dst)
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

    internal static GoaltenderData FindGoalieByName(string name)
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

        // Always auto-dump team+player list for the GUI tool (not gated by DumpData).
        // Written to ModContentRoot so the GUI's "Import Game Team" picker can read it.
        try
        {
            AutoDumpNameLists();
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[Dump] Name list dump failed: {ex.Message}"); }

        if (!Plugin.DumpData) return;
        if (Plugin.SeparateFilesWritten) return; // only dump once
        Plugin.SeparateFilesWritten = true;
        Plugin.Log.LogInfo("[Dump] Generating data dumps...");

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

            // Now collect goalie-specific skins
            var goalieHeadSkins = new HashSet<string>();
            var goalieBodySkins = new HashSet<string>();
            var goalieHelmetSkins = new HashSet<string>();
            var goalieGloveSkins = new HashSet<string>();
            var goalieBlockerSkins = new HashSet<string>();
            var goaliePadsSkins = new HashSet<string>();
            var goalieStickSkins = new HashSet<string>();

            var allGoalies = UnityEngine.Resources.FindObjectsOfTypeAll<GoaltenderData>();
            if (allGoalies != null)
            {
                // Helper: pull a private list<string> via reflection
                void AddList(object obj, string fieldName, HashSet<string> target)
                {
                    try
                    {
                        var field = obj.GetType().GetField(fieldName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (field != null)
                        {
                            var list = field.GetValue(obj) as Il2CppSystem.Collections.Generic.List<string>;
                            if (list != null)
                                for (int i = 0; i < list.Count; i++)
                                    if (!string.IsNullOrEmpty(list[i])) target.Add(list[i]);
                        }
                    } catch {}
                }

                foreach (var g in allGoalies)
                {
                    if (g == null) continue;
                    // Public fields
                    if (!string.IsNullOrEmpty(g.headSkin)) goalieHeadSkins.Add(g.headSkin);
                    try { if (!string.IsNullOrEmpty(g.skin)) goalieBodySkins.Add(g.skin); } catch {}
                    try { if (!string.IsNullOrEmpty(g.awaySkin)) goalieBodySkins.Add(g.awaySkin); } catch {}
                    try { if (!string.IsNullOrEmpty(g.helmetSkin)) goalieHelmetSkins.Add(g.helmetSkin); } catch {}
                    try { if (!string.IsNullOrEmpty(g.gloveSkin)) goalieGloveSkins.Add(g.gloveSkin); } catch {}
                    try { if (!string.IsNullOrEmpty(g.awayGloveSkin)) goalieGloveSkins.Add(g.awayGloveSkin); } catch {}
                    try { if (!string.IsNullOrEmpty(g.blockerSkin)) goalieBlockerSkins.Add(g.blockerSkin); } catch {}
                    try { if (!string.IsNullOrEmpty(g.awayBlockerSkin)) goalieBlockerSkins.Add(g.awayBlockerSkin); } catch {}
                    try { if (!string.IsNullOrEmpty(g.padsSkin)) goaliePadsSkins.Add(g.padsSkin); } catch {}
                    try { if (!string.IsNullOrEmpty(g.awayPadsSkin)) goaliePadsSkins.Add(g.awayPadsSkin); } catch {}
                    try { if (!string.IsNullOrEmpty(g.stickSkin)) goalieStickSkins.Add(g.stickSkin); } catch {}
                    try { if (!string.IsNullOrEmpty(g.awayStickSkin)) goalieStickSkins.Add(g.awayStickSkin); } catch {}
                    // Private pools (all possible options for this goalie)
                    AddList(g, "_headSkins", goalieHeadSkins);
                    AddList(g, "_bodySkins", goalieBodySkins);
                    AddList(g, "_awayBodySkins", goalieBodySkins);
                    AddList(g, "_helmetSkins", goalieHelmetSkins);
                    AddList(g, "_gloveSkins", goalieGloveSkins);
                    AddList(g, "_awayGloveSkins", goalieGloveSkins);
                    AddList(g, "_blockerSkins", goalieBlockerSkins);
                    AddList(g, "_awayBlockerSkins", goalieBlockerSkins);
                    AddList(g, "_padsSkins", goaliePadsSkins);
                    AddList(g, "_awayPadsSkins", goaliePadsSkins);
                    AddList(g, "_stickSkins", goalieStickSkins);
                    AddList(g, "_awayStickSkins", goalieStickSkins);
                }
            }

            sbSkins.AppendLine("=== SKATER SKINS ===");
            sbSkins.AppendLine();
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

            sbSkins.AppendLine("=== GOALIE SKINS ===");
            sbSkins.AppendLine();
            sbSkins.AppendLine($"--- GOALIE HEAD/FACE SKINS ({goalieHeadSkins.Count}) ---");
            foreach (var s in goalieHeadSkins) sbSkins.AppendLine($"  {s}");
            sbSkins.AppendLine();
            sbSkins.AppendLine($"--- GOALIE BODY SKINS ({goalieBodySkins.Count}) ---");
            foreach (var s in goalieBodySkins) sbSkins.AppendLine($"  {s}");
            sbSkins.AppendLine();
            sbSkins.AppendLine($"--- GOALIE HELMET SKINS ({goalieHelmetSkins.Count}) ---");
            foreach (var s in goalieHelmetSkins) sbSkins.AppendLine($"  {s}");
            sbSkins.AppendLine();
            sbSkins.AppendLine($"--- GOALIE GLOVE SKINS ({goalieGloveSkins.Count}) ---");
            foreach (var s in goalieGloveSkins) sbSkins.AppendLine($"  {s}");
            sbSkins.AppendLine();
            sbSkins.AppendLine($"--- GOALIE BLOCKER SKINS ({goalieBlockerSkins.Count}) ---");
            foreach (var s in goalieBlockerSkins) sbSkins.AppendLine($"  {s}");
            sbSkins.AppendLine();
            sbSkins.AppendLine($"--- GOALIE PADS SKINS ({goaliePadsSkins.Count}) ---");
            foreach (var s in goaliePadsSkins) sbSkins.AppendLine($"  {s}");
            sbSkins.AppendLine();
            sbSkins.AppendLine($"--- GOALIE STICK SKINS ({goalieStickSkins.Count}) ---");
            foreach (var s in goalieStickSkins) sbSkins.AppendLine($"  {s}");
            sbSkins.AppendLine();

            File.WriteAllText(Path.Combine(basePath, "ALL_SKIN_OPTIONS.txt"), sbSkins.ToString());
            Plugin.Log.LogInfo($"[Dump] Skin options dumped to ALL_SKIN_OPTIONS.txt (goalie heads: {goalieHeadSkins.Count})");
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

        // === 4. DUMP ALL FORWARDS AND GOALIES IN MEMORY (includes draft pool) ===
        try
        {
            var sbPlayers = new StringBuilder();
            sbPlayers.AppendLine("=== ALL PLAYERS IN MEMORY ===");
            sbPlayers.AppendLine($"Generated: {DateTime.Now}");
            sbPlayers.AppendLine("Includes draft pool players, bench players, and all team rosters.");
            sbPlayers.AppendLine();

            var allFwds = UnityEngine.Resources.FindObjectsOfTypeAll<ForwardData>();
            if (allFwds != null)
            {
                sbPlayers.AppendLine($"========== ALL FORWARDS ({allFwds.Length}) ==========");
                foreach (var f in allFwds)
                {
                    if (f == null) continue;
                    sbPlayers.AppendLine($"--- {f.firstName} {f.lastName} ---");
                    sbPlayers.AppendLine($"  speed: {f.speed}, shotPower: {f.shotPower}, shotAccuracy: {f.shotAccuracy}, checking: {f.checking}");
                    sbPlayers.AppendLine($"  size: {f.skaterSize}, sizeOffset: {f.sizeOffsetPercentage}");
                    sbPlayers.AppendLine($"  isLefty: {f.isLefty}, isBlack: {f.isBlack}, number: {f.number}");
                    sbPlayers.AppendLine($"  headSkin: \"{f.headSkin ?? ""}\"");
                    sbPlayers.AppendLine($"  bodySkin: \"{f.bodySkin ?? ""}\"");
                    sbPlayers.AppendLine($"  bodyAwaySkin: \"{f.bodyAwaySkin ?? ""}\"");
                    sbPlayers.AppendLine($"  helmetSkin: \"{f.helmetSkin ?? ""}\"");
                    sbPlayers.AppendLine($"  helmetAwaySkin: \"{f.helmetAwaySkin ?? ""}\"");
                    sbPlayers.AppendLine($"  bicepSkin: \"{f.bicepSkin ?? ""}\"");
                    sbPlayers.AppendLine($"  gloveSkin: \"{f.gloveSkin ?? ""}\"");
                    sbPlayers.AppendLine($"  pantsSkin: \"{f.pantsSkin ?? ""}\"");
                    sbPlayers.AppendLine($"  skateSkin: \"{f.skateSkin ?? ""}\"");
                    sbPlayers.AppendLine($"  stickSkin: \"{f.stickSkin ?? ""}\"");
                    sbPlayers.AppendLine($"  numberSkin: \"{f.numberSkin ?? ""}\"");
                    sbPlayers.AppendLine($"  logoSkin: \"{f.logoSkin ?? ""}\"");
                    sbPlayers.AppendLine($"  glassesSkin: \"{f.glassesSkin ?? ""}\"");
                    if (f.ability != null)
                        sbPlayers.AppendLine($"  ability: \"{f.ability.name}\"");
                    if (f.powerups != null && f.powerups.Count > 0)
                    {
                        sbPlayers.Append("  talents: [");
                        for (int j = 0; j < f.powerups.Count; j++)
                        {
                            if (j > 0) sbPlayers.Append(", ");
                            sbPlayers.Append($"\"{f.powerups[j]?.name ?? "null"}\"");
                        }
                        sbPlayers.AppendLine("]");
                    }
                    sbPlayers.AppendLine();
                }
            }

            var allGoalies = UnityEngine.Resources.FindObjectsOfTypeAll<GoaltenderData>();
            if (allGoalies != null)
            {
                sbPlayers.AppendLine($"========== ALL GOALIES ({allGoalies.Length}) ==========");
                foreach (var g in allGoalies)
                {
                    if (g == null) continue;
                    sbPlayers.AppendLine($"--- {g.firstName} {g.lastName} ---");
                    sbPlayers.AppendLine($"  skill: {g.skill}, catching: {g.catchingSkill}, glove: {g.gloveSkill}, blocker: {g.blockerSkill}");
                    sbPlayers.AppendLine($"  fiveHole: {g.fiveHoleSkill}, standSpd: {g.standingSpeed}, buttSpd: {g.butterflySpeed}");
                    sbPlayers.AppendLine($"  control: {g.controlSkill}, recovery: {g.recoverySkill}, passPower: {g.passPower}");
                    sbPlayers.AppendLine($"  shotPower: {g.shotPower}, pokecheck: {g.pokecheckSkill}, depth: {g.depth}, passRead: {g.passReadSkill}");
                    sbPlayers.AppendLine($"  headSkin: \"{g.headSkin ?? ""}\"");
                    try { sbPlayers.AppendLine($"  skin: \"{g.skin ?? ""}\""); } catch {}
                    try { sbPlayers.AppendLine($"  awaySkin: \"{g.awaySkin ?? ""}\""); } catch {}
                    try { sbPlayers.AppendLine($"  gloveSkin: \"{g.gloveSkin ?? ""}\""); } catch {}
                    try { sbPlayers.AppendLine($"  blockerSkin: \"{g.blockerSkin ?? ""}\""); } catch {}
                    try { sbPlayers.AppendLine($"  padsSkin: \"{g.padsSkin ?? ""}\""); } catch {}
                    try { sbPlayers.AppendLine($"  stickSkin: \"{g.stickSkin ?? ""}\""); } catch {}
                    try { sbPlayers.AppendLine($"  helmetSkin: \"{g.helmetSkin ?? ""}\""); } catch {}
                    try { sbPlayers.AppendLine($"  logoSkin: \"{g.logoSkin ?? ""}\""); } catch {}
                    if (g.powerups != null && g.powerups.Count > 0)
                    {
                        sbPlayers.Append("  talents: [");
                        for (int j = 0; j < g.powerups.Count; j++)
                        {
                            if (j > 0) sbPlayers.Append(", ");
                            sbPlayers.Append($"\"{g.powerups[j]?.name ?? "null"}\"");
                        }
                        sbPlayers.AppendLine("]");
                    }
                    sbPlayers.AppendLine();
                }
            }

            File.WriteAllText(Path.Combine(basePath, "ALL_PLAYERS.txt"), sbPlayers.ToString());
            Plugin.Log.LogInfo($"[Dump] All players dumped to ALL_PLAYERS.txt ({allFwds?.Length ?? 0} forwards, {allGoalies?.Length ?? 0} goalies)");
        }
        catch (Exception ex) { Plugin.Log.LogError($"[Dump] All players dump failed: {ex}"); }
    }

    private static bool _guiListsDumped = false;
    public static void AutoDumpNameLists()
    {
        if (_guiListsDumped) return;
        _guiListsDumped = true;

        string root = Plugin.ModContentRoot;
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return;

        var allTeams = UnityEngine.Resources.FindObjectsOfTypeAll<TeamData>();
        if (allTeams == null || allTeams.Length == 0) return;

        // 1. Name lists (for GUI dropdowns)
        try
        {
            var teamNames = new List<string>();
            foreach (var t in allTeams)
                if (t != null && !string.IsNullOrEmpty(t.teamName))
                    teamNames.Add(t.teamName);
            teamNames.Sort();
            File.WriteAllLines(Path.Combine(root, "_game_team_names.txt"), teamNames.ToArray());

            var allPlayers = UnityEngine.Resources.FindObjectsOfTypeAll<ForwardData>();
            var allGoalies = UnityEngine.Resources.FindObjectsOfTypeAll<GoaltenderData>();
            var playerNames = new HashSet<string>();
            if (allPlayers != null)
                foreach (var p in allPlayers)
                    if (p != null && !string.IsNullOrEmpty(p.firstName))
                        playerNames.Add($"{p.firstName} {p.lastName}".Trim());
            if (allGoalies != null)
                foreach (var g in allGoalies)
                    if (g != null && !string.IsNullOrEmpty(g.firstName))
                        playerNames.Add($"{g.firstName} {g.lastName}".Trim());
            var sortedP = new List<string>(playerNames);
            sortedP.Sort();
            File.WriteAllLines(Path.Combine(root, "_game_player_names.txt"), sortedP.ToArray());
            Plugin.Log.LogInfo($"[Dump] Name lists: {teamNames.Count} teams, {sortedP.Count} players");
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[Dump] Name lists failed: {ex.Message}"); }

        // 1b. Dump every team logo as PNG into the game's CustomLogos folder.
        //     Makes the vanilla logos available to the in-game logo picker AND
        //     gives the GUI a list to populate the Logo From dropdown.
        DumpTeamLogosToCustomLogos(allTeams, root);

        // 2. Full library dump — write every game team as editable files
        //    into library/Base Game Teams/<TeamName>/team.txt + players/
        //    Runs EVERY launch to catch updates. Overwrites existing files.
        //    Users should COPY teams/players out if they want to customize.
        string baseGameDir = Path.Combine(root, "library", "Base Game Teams");
        string basePlayersDir = Path.Combine(root, "library", "Base Game Players");

        // Skip the 4 player-team presets (user edits those per-campaign)
        var excludeTeams = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Basic", "Basic Squad", "Defense", "Defense Squad",
              "Speedy", "Speed Squad", "Trios", "Trio Squad" };

        // The 32 base game (NHL parody) teams — anything NOT in this list
        // is a custom/in-game-editor team and goes to a separate folder.
        var baseGameTeamNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Anaheim", "Boston", "Buffalo", "Calgary", "Carolina", "Chicago",
            "Colorado", "Columbus", "Dallas", "Detroit", "Edmonton", "Florida",
            "Long Island", "Los Angeles", "Minnesota", "Montreal", "Nashville",
            "New Jersey", "New York", "Ottawa", "Philadelphia", "Pittsburgh",
            "San Jose", "Seattle", "St-Louis", "Tampa Bay", "Toronto", "Utah",
            "Vancouver", "Vegas", "Washington", "Winnipeg",
            "Calaveras", "Greasy Lettuce", "Top Cheese", "Meatballs",
            "The Officials", "Crusaders", "Princess", "Cup Cultists",
            "Mountaineers", "Disco", "Golfers", "Hockey FC",
            "Shooting Stars", "Team Canada", "Tycoons", "Prisoners",
            // Internal/special teams
            "16-Bit", "Spartans", "Gauntlet", "Solo", "Bum Squad",
            "Random", "Vanilla", "NoRelic", "Stats", "Trio", "TwoLines",
            "My Team"
        };

        string customTeamsDir = Path.Combine(root, "library", "Custom Teams (in-game editor)");
        string customPlayersDir = Path.Combine(root, "library", "Custom Players (in-game editor)");

        // Skip the full library dump if already done — writing ~85 teams each
        // launch is slow and spams logs. User can delete library/Base Game Teams/
        // to force a refresh, or set `dump data = yes` in active.txt to force.
        bool alreadyDumped = Directory.Exists(baseGameDir)
            && Directory.GetDirectories(baseGameDir).Length > 10;
        if (alreadyDumped && !Plugin.DumpData)
        {
            Plugin.Log.LogInfo("[Dump] Library already populated — skipping full team/player dump. Delete library/Base Game Teams/ to refresh.");
            return;
        }

        Plugin.Log.LogInfo($"[Dump] Dumping all game teams + players to library...");
        int teamCount = 0;
        foreach (var team in allTeams)
        {
            if (team == null || string.IsNullOrEmpty(team.teamName)) continue;
            if (excludeTeams.Contains(team.teamName.Trim())) continue;
            try
            {
                bool isBaseGame = baseGameTeamNames.Contains(team.teamName.Trim());
                string targetTeamDir = isBaseGame ? baseGameDir : customTeamsDir;
                string targetPlayerDir = isBaseGame ? basePlayersDir : customPlayersDir;
                DumpTeamToLibrary(targetTeamDir, team);
                DumpTeamPlayersFlat(targetPlayerDir, team);
                teamCount++;
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"[Dump] Team '{team.teamName}' failed: {ex.Message}"); }
        }

        // Write a README note in all auto-generated folders
        try
        {
            string note = "# AUTO-GENERATED — DO NOT EDIT IN PLACE\r\n"
                + "# These files are regenerated every time the game launches.\r\n"
                + "# To customize a team or player, COPY them to:\r\n"
                + "#   library/teams/<YourTeamName>/\r\n"
                + "#   library/players/<PlayerName>.txt\r\n"
                + "# Your copies will NOT be overwritten.\r\n";
            Directory.CreateDirectory(baseGameDir);
            Directory.CreateDirectory(basePlayersDir);
            Directory.CreateDirectory(customTeamsDir);
            Directory.CreateDirectory(customPlayersDir);
            File.WriteAllText(Path.Combine(baseGameDir, "_README.txt"), note);
            File.WriteAllText(Path.Combine(basePlayersDir, "_README.txt"), note);
            File.WriteAllText(Path.Combine(customTeamsDir, "_README.txt"), note);
            File.WriteAllText(Path.Combine(customPlayersDir, "_README.txt"), note);
        } catch {}

        Plugin.Log.LogInfo($"[Dump] Dumped {teamCount} teams to library");
    }

    private static bool _logosDumped = false;
    private static void DumpTeamLogosToCustomLogos(TeamData[] allTeams, string modRoot)
    {
        if (_logosDumped) return;
        _logosDumped = true;
        try
        {
            string logoDir = Path.Combine(UnityEngine.Application.persistentDataPath, "CustomLogos");
            Directory.CreateDirectory(logoDir);

            int wrote = 0, skipped = 0, failed = 0;
            var logoNames = new List<string>();
            foreach (var t in allTeams)
            {
                if (t == null || string.IsNullOrEmpty(t.teamName)) continue;
                if (t.logo == null) continue;

                string safe = t.teamName;
                foreach (char c in Path.GetInvalidFileNameChars()) safe = safe.Replace(c, '_');
                logoNames.Add(safe);

                string outPath = Path.Combine(logoDir, safe + ".png");
                if (File.Exists(outPath)) { skipped++; continue; }

                try
                {
                    byte[] bytes = SpriteToPng(t.logo);
                    if (bytes != null && bytes.Length > 0)
                    {
                        File.WriteAllBytes(outPath, bytes);
                        wrote++;
                    }
                    else failed++;
                }
                catch (Exception ex)
                {
                    failed++;
                    Plugin.Log.LogDebug($"[Dump] Logo '{t.teamName}' failed: {ex.Message}");
                }
            }

            // Write _game_team_logos.txt next to the team/player name lists so
            // the GUI can populate the Logo From dropdown without scanning the
            // persistent data folder.
            try
            {
                logoNames.Sort();
                File.WriteAllLines(Path.Combine(modRoot, "_game_team_logos.txt"), logoNames.ToArray());
            }
            catch { }

            Plugin.Log.LogInfo($"[Dump] CustomLogos: wrote {wrote}, skipped {skipped} existing, failed {failed} -> {logoDir}");
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[Dump] CustomLogos failed: {ex.Message}"); }
    }

    private static byte[] SpriteToPng(UnityEngine.Sprite sprite)
    {
        if (sprite == null || sprite.texture == null) return null;
        var src = sprite.texture;
        UnityEngine.RenderTexture rt = null;
        UnityEngine.Texture2D readable = null;
        var prevActive = UnityEngine.RenderTexture.active;
        try
        {
            // Blit the (possibly non-readable) atlas texture into a temp RT so
            // we can ReadPixels it back into a CPU-side Texture2D.
            rt = UnityEngine.RenderTexture.GetTemporary(
                src.width, src.height, 0,
                UnityEngine.RenderTextureFormat.ARGB32,
                UnityEngine.RenderTextureReadWrite.Linear);
            UnityEngine.Graphics.Blit(src, rt);
            UnityEngine.RenderTexture.active = rt;

            var r = sprite.textureRect;
            int x = Math.Max(0, (int)r.x);
            int y = Math.Max(0, (int)r.y);
            int w = Math.Max(1, (int)r.width);
            int h = Math.Max(1, (int)r.height);
            if (x + w > src.width) w = src.width - x;
            if (y + h > src.height) h = src.height - y;

            readable = new UnityEngine.Texture2D(w, h, UnityEngine.TextureFormat.ARGB32, false);
            readable.ReadPixels(new UnityEngine.Rect(x, y, w, h), 0, 0);
            readable.Apply();

            return UnityEngine.ImageConversion.EncodeToPNG(readable);
        }
        finally
        {
            UnityEngine.RenderTexture.active = prevActive;
            if (rt != null) UnityEngine.RenderTexture.ReleaseTemporary(rt);
            if (readable != null) UnityEngine.Object.Destroy(readable);
        }
    }

    private static void DumpTeamToLibrary(string baseDir, TeamData team)
    {
        string safe = team.teamName;
        foreach (char c in Path.GetInvalidFileNameChars()) safe = safe.Replace(c, '_');
        string teamDir = Path.Combine(baseDir, safe);
        string playersDir = Path.Combine(teamDir, "players");
        Directory.CreateDirectory(playersDir);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"# Base game team: {team.teamName}");
        sb.AppendLine($"Team Name               = {team.teamName}");
        if (!string.IsNullOrEmpty(team.city)) sb.AppendLine($"City                    = {team.city}");
        if (!string.IsNullOrEmpty(team.nickname)) sb.AppendLine($"Abbreviation            = {team.nickname}");
        sb.AppendLine($"Logo From               = {team.teamName}");

        // Colors
        try
        {
            var hc = team.homeColors;
            if (hc != null)
            {
                if (hc.jerseyScheme != null)
                {
                    sb.AppendLine($"Jersey Primary          = {PatchBossLaunchMatch.ColorToRGB(hc.jerseyScheme.primaryColor)}");
                    sb.AppendLine($"Jersey Secondary        = {PatchBossLaunchMatch.ColorToRGB(hc.jerseyScheme.secondaryColor)}");
                    sb.AppendLine($"Jersey Accent           = {PatchBossLaunchMatch.ColorToRGB(hc.jerseyScheme.tertiaryColor)}");
                }
                if (hc.helmetScheme != null)
                {
                    sb.AppendLine($"Helmet Color            = {PatchBossLaunchMatch.ColorToRGB(hc.helmetScheme.primaryColor)}");
                    sb.AppendLine($"Helmet Secondary Color  = {PatchBossLaunchMatch.ColorToRGB(hc.helmetScheme.secondaryColor)}");
                    sb.AppendLine($"Helmet Tertiary Color   = {PatchBossLaunchMatch.ColorToRGB(hc.helmetScheme.tertiaryColor)}");
                }
                if (hc.glovesScheme != null)
                {
                    sb.AppendLine($"Gloves Color            = {PatchBossLaunchMatch.ColorToRGB(hc.glovesScheme.primaryColor)}");
                    sb.AppendLine($"Gloves Secondary Color  = {PatchBossLaunchMatch.ColorToRGB(hc.glovesScheme.secondaryColor)}");
                    sb.AppendLine($"Gloves Tertiary Color   = {PatchBossLaunchMatch.ColorToRGB(hc.glovesScheme.tertiaryColor)}");
                }
                if (hc.pantsScheme != null)
                {
                    sb.AppendLine($"Pants Color             = {PatchBossLaunchMatch.ColorToRGB(hc.pantsScheme.primaryColor)}");
                    sb.AppendLine($"Pants Secondary Color   = {PatchBossLaunchMatch.ColorToRGB(hc.pantsScheme.secondaryColor)}");
                    sb.AppendLine($"Pants Tertiary Color    = {PatchBossLaunchMatch.ColorToRGB(hc.pantsScheme.tertiaryColor)}");
                }
                if (hc.skatesScheme != null)
                {
                    sb.AppendLine($"Skates Color            = {PatchBossLaunchMatch.ColorToRGB(hc.skatesScheme.primaryColor)}");
                    sb.AppendLine($"Blade Color             = {PatchBossLaunchMatch.ColorToRGB(hc.skatesScheme.secondaryColor)}");
                    sb.AppendLine($"Laces Color             = {PatchBossLaunchMatch.ColorToRGB(hc.skatesScheme.tertiaryColor)}");
                }
                if (hc.socksScheme != null)
                {
                    sb.AppendLine($"Socks Color             = {PatchBossLaunchMatch.ColorToRGB(hc.socksScheme.primaryColor)}");
                    sb.AppendLine($"Socks Secondary Color   = {PatchBossLaunchMatch.ColorToRGB(hc.socksScheme.secondaryColor)}");
                    sb.AppendLine($"Socks Tertiary Color    = {PatchBossLaunchMatch.ColorToRGB(hc.socksScheme.tertiaryColor)}");
                }
                if (hc.numberScheme != null)
                {
                    sb.AppendLine($"Number Color Home       = {PatchBossLaunchMatch.ColorToRGB(hc.numberScheme.primaryColor)}");
                    sb.AppendLine($"Number Color Away       = {PatchBossLaunchMatch.ColorToRGB(hc.numberScheme.secondaryColor)}");
                }
                if (hc.stickScheme != null)
                    sb.AppendLine($"Stick Color             = {PatchBossLaunchMatch.ColorToRGB(hc.stickScheme.primaryColor)}");
            }
            // Away colors
            var ac = team.awayColors;
            if (ac != null && ac.jerseyScheme != null)
            {
                sb.AppendLine($"Away Primary            = {PatchBossLaunchMatch.ColorToRGB(ac.jerseyScheme.primaryColor)}");
                sb.AppendLine($"Away Secondary          = {PatchBossLaunchMatch.ColorToRGB(ac.jerseyScheme.secondaryColor)}");
                sb.AppendLine($"Away Accent             = {PatchBossLaunchMatch.ColorToRGB(ac.jerseyScheme.tertiaryColor)}");
            }
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[Dump] Colors for '{team.teamName}': {ex.Message}"); }

        // Skins
        sb.AppendLine($"Body                    = standard");
        sb.AppendLine($"Bicep                   = standard");
        sb.AppendLine($"Gloves                  = standard");
        sb.AppendLine($"Pants                   = standard");
        sb.AppendLine($"Skates                  = standard");
        sb.AppendLine($"Helmet                  = team colors");
        sb.AppendLine($"Stick                   = black");

        // Relics
        try
        {
            if (team.relics != null && team.relics.Count > 0)
            {
                var rnames = new List<string>();
                for (int i = 0; i < team.relics.Count; i++)
                    if (team.relics[i] != null) rnames.Add(team.relics[i].name);
                if (rnames.Count > 0)
                    sb.AppendLine($"Team Relics             = {string.Join(", ", rnames)}");
            }
        } catch {}

        File.WriteAllText(Path.Combine(teamDir, "team.txt"), sb.ToString());

        // Forwards
        try
        {
            var forwards = team.GetForwards();
            string[] posNames = { "Left Wing", "Right Wing", "Center", "Left Defense", "Right Defense" };
            if (forwards != null)
            {
                for (int i = 0; i < Math.Min(forwards.Count, posNames.Length); i++)
                {
                    var f = forwards[i];
                    if (f == null) continue;
                    DumpSkaterToFile(playersDir, posNames[i], f);
                }
            }
        } catch (Exception ex) { Plugin.Log.LogWarning($"[Dump] Forwards for '{team.teamName}': {ex.Message}"); }

        // Goalie
        try
        {
            var g = team.goalie;
            if (g != null) DumpGoalieToFile(playersDir, g);
        } catch (Exception ex) { Plugin.Log.LogWarning($"[Dump] Goalie for '{team.teamName}': {ex.Message}"); }
    }

    private static void DumpTeamPlayersFlat(string basePlayersDir, TeamData team)
    {
        Directory.CreateDirectory(basePlayersDir);
        try
        {
            var forwards = team.GetForwards();
            if (forwards != null)
            {
                for (int i = 0; i < forwards.Count; i++)
                {
                    var f = forwards[i];
                    if (f == null) continue;
                    string pname = $"{f.firstName} {f.lastName}".Trim();
                    if (string.IsNullOrEmpty(pname)) continue;
                    string safe = pname;
                    foreach (char c in Path.GetInvalidFileNameChars()) safe = safe.Replace(c, '_');
                    // Write flat file (no position prefix — these are library players)
                    string path = Path.Combine(basePlayersDir, safe + ".txt");
                    // Reuse the skater dump logic but write to flat path
                    string[] posNames = { "Left Wing", "Right Wing", "Center", "Left Defense", "Right Defense" };
                    string pos = i < posNames.Length ? posNames[i] : "Center";
                    DumpSkaterToFile(basePlayersDir, pos, f, flat: true);
                }
            }
        } catch {}
        try
        {
            var g = team.goalie;
            if (g != null) DumpGoalieToFile(basePlayersDir, g, flat: true);
        } catch {}
    }

    private static void DumpSkaterToFile(string playersDir, string position, ForwardData f, bool flat = false)
    {
        string pname = $"{f.firstName} {f.lastName}".Trim();
        if (string.IsNullOrEmpty(pname)) pname = position;
        string safe = pname;
        foreach (char c in Path.GetInvalidFileNameChars()) safe = safe.Replace(c, '_');
        // flat = library player (just Name.txt); non-flat = team player (Position - Name.txt)
        string fname = flat ? $"{safe}.txt" : $"{position} - {safe}.txt";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Name                    = {pname}");
        sb.AppendLine($"Number                  = {f.number}");
        try { sb.AppendLine($"Face                    = {PatchBossLaunchMatch.ReverseSkinPath(f.headSkin, "face")}"); } catch {}
        sb.AppendLine($"Left Handed             = {(f.isLefty ? "yes" : "no")}");
        sb.AppendLine($"Skin Color              = {(f.isBlack ? "dark" : "light")}");
        sb.AppendLine($"Size                    = {f.skaterSize}");
        try
        {
            float so = f.sizeOffsetPercentage;
            if (Math.Abs(so - 1.0f) > 0.01f)
                sb.AppendLine($"Size Offset             = {so:F2}");
        } catch {}
        sb.AppendLine($"Speed                   = {f.speed}");
        sb.AppendLine($"Shot Power              = {f.shotPower}");
        sb.AppendLine($"Accuracy                = {f.shotAccuracy}");
        sb.AppendLine($"Checking                = {f.checking}");
        // Ability
        try
        {
            if (f.ability != null && !string.IsNullOrEmpty(f.ability.name))
                sb.AppendLine($"Ability                 = {f.ability.name}");
        } catch {}
        // Talents
        try
        {
            if (f.powerups != null && f.powerups.Count > 0)
            {
                var tnames = new List<string>();
                for (int ti = 0; ti < f.powerups.Count; ti++)
                    if (f.powerups[ti] != null) tnames.Add(f.powerups[ti].name);
                if (tnames.Count > 0)
                    sb.AppendLine($"Talents                 = {string.Join(", ", tnames)}");
            }
        } catch {}
        // Skins
        try { sb.AppendLine($"Stick                   = {PatchBossLaunchMatch.ReverseSkinPath(f.stickSkin, "stick")}"); } catch {}
        try { sb.AppendLine($"Helmet                  = {PatchBossLaunchMatch.ReverseSkinPath(f.helmetSkin, "helmet")}"); } catch {}
        try { sb.AppendLine($"Helmet Away             = {PatchBossLaunchMatch.ReverseSkinPath(f.helmetAwaySkin, "helmet")}"); } catch {}
        try { sb.AppendLine($"Body                    = {PatchBossLaunchMatch.ReverseSkinPath(f.bodySkin, "body")}"); } catch {}
        try { sb.AppendLine($"Body Away               = {PatchBossLaunchMatch.ReverseSkinPath(f.bodyAwaySkin, "body")}"); } catch {}
        try { sb.AppendLine($"Bicep                   = {PatchBossLaunchMatch.ReverseSkinPath(f.bicepSkin, "bicep")}"); } catch {}
        try { sb.AppendLine($"Gloves                  = {PatchBossLaunchMatch.ReverseSkinPath(f.gloveSkin, "gloves")}"); } catch {}
        try { sb.AppendLine($"Pants                   = {PatchBossLaunchMatch.ReverseSkinPath(f.pantsSkin, "pants")}"); } catch {}
        try { sb.AppendLine($"Skates                  = {PatchBossLaunchMatch.ReverseSkinPath(f.skateSkin, "skates")}"); } catch {}
        try
        {
            if (!string.IsNullOrEmpty(f.glassesSkin))
                sb.AppendLine($"Glasses                 = {f.glassesSkin}");
        } catch {}

        File.WriteAllText(Path.Combine(playersDir, fname), sb.ToString());
    }

    private static void DumpGoalieToFile(string playersDir, GoaltenderData g, bool flat = false)
    {
        string gname = $"{g.firstName} {g.lastName}".Trim();
        if (string.IsNullOrEmpty(gname)) gname = "Goalie";
        string safe = gname;
        foreach (char c in Path.GetInvalidFileNameChars()) safe = safe.Replace(c, '_');

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Name                    = {gname}");
        // Read the actual headSkin instead of hardcoding "Helmet_Face".
        // 92/92 vanilla goalies have empty headSkin — the mask covers the
        // head entirely. Only write the Face line when the goalie actually
        // has one, so round-tripping (load -> save) preserves accuracy.
        try
        {
            if (!string.IsNullOrEmpty(g.headSkin))
                sb.AppendLine($"Face                    = {PatchBossLaunchMatch.ReverseSkinPath(g.headSkin, "face")}");
        } catch {}
        try { sb.AppendLine($"Skill                   = {g.catchingSkill}"); } catch {}
        try { sb.AppendLine($"Catching                = {g.catchingSkill}"); } catch {}
        try { sb.AppendLine($"Glove                   = {g.gloveSkill}"); } catch {}
        try { sb.AppendLine($"Blocker                 = {g.blockerSkill}"); } catch {}
        try { sb.AppendLine($"Five Hole               = {g.fiveHoleSkill}"); } catch {}
        try { sb.AppendLine($"Standing Speed          = {g.standingSpeed}"); } catch {}
        try { sb.AppendLine($"Butterfly Speed         = {g.butterflySpeed}"); } catch {}
        try { sb.AppendLine($"Control                 = {g.controlSkill}"); } catch {}
        try { sb.AppendLine($"Recovery                = {g.recoverySkill}"); } catch {}
        try { sb.AppendLine($"Pass Power              = {g.passPower}"); } catch {}
        try { sb.AppendLine($"Shot Power              = {g.shotPower}"); } catch {}
        try { sb.AppendLine($"Poke Check              = {g.pokecheckSkill}"); } catch {}
        try { sb.AppendLine($"Depth                   = {g.depth}"); } catch {}
        try { sb.AppendLine($"Pass Read               = {g.passReadSkill}"); } catch {}
        // Goalie talents
        try
        {
            if (g.powerups != null && g.powerups.Count > 0)
            {
                var tnames = new List<string>();
                for (int ti = 0; ti < g.powerups.Count; ti++)
                    if (g.powerups[ti] != null) tnames.Add(g.powerups[ti].name);
                if (tnames.Count > 0)
                    sb.AppendLine($"Goalie Talents          = {string.Join(", ", tnames)}");
            }
        } catch {}
        // Goalie skins
        try { if (!string.IsNullOrEmpty(g.helmetSkin)) sb.AppendLine($"Helmet Skin             = {PatchBossLaunchMatch.ReverseSkinPath(g.helmetSkin, "helmet")}"); } catch {}
        try { if (!string.IsNullOrEmpty(g.skin)) sb.AppendLine($"Skin                    = {PatchBossLaunchMatch.ReverseSkinPath(g.skin, "body")}"); } catch {}
        try { if (!string.IsNullOrEmpty(g.awaySkin)) sb.AppendLine($"Skin Away               = {PatchBossLaunchMatch.ReverseSkinPath(g.awaySkin, "body")}"); } catch {}
        try { if (!string.IsNullOrEmpty(g.gloveSkin)) sb.AppendLine($"Glove Skin              = {PatchBossLaunchMatch.ReverseSkinPath(g.gloveSkin, "glove")}"); } catch {}
        try { if (!string.IsNullOrEmpty(g.blockerSkin)) sb.AppendLine($"Blocker Skin            = {PatchBossLaunchMatch.ReverseSkinPath(g.blockerSkin, "blocker")}"); } catch {}
        try { if (!string.IsNullOrEmpty(g.padsSkin)) sb.AppendLine($"Pads Skin               = {PatchBossLaunchMatch.ReverseSkinPath(g.padsSkin, "pads")}"); } catch {}
        try { if (!string.IsNullOrEmpty(g.stickSkin)) sb.AppendLine($"Stick Skin              = {PatchBossLaunchMatch.ReverseSkinPath(g.stickSkin, "stick")}"); } catch {}

        string gfname = flat ? $"{safe}.txt" : $"Goalie - {safe}.txt";
        File.WriteAllText(Path.Combine(playersDir, gfname), sb.ToString());
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
            try { sb.AppendLine($"    skin: \"{g.skin ?? ""}\""); } catch {}
            try { sb.AppendLine($"    awaySkin: \"{g.awaySkin ?? ""}\""); } catch {}
            try { sb.AppendLine($"    gloveSkin: \"{g.gloveSkin ?? ""}\""); } catch {}
            try { sb.AppendLine($"    blockerSkin: \"{g.blockerSkin ?? ""}\""); } catch {}
            try { sb.AppendLine($"    padsSkin: \"{g.padsSkin ?? ""}\""); } catch {}
            try { sb.AppendLine($"    stickSkin: \"{g.stickSkin ?? ""}\""); } catch {}
            try { sb.AppendLine($"    helmetSkin: \"{g.helmetSkin ?? ""}\""); } catch {}
            try { sb.AppendLine($"    logoSkin: \"{g.logoSkin ?? ""}\""); } catch {}
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

        // SIMPLE REWARD-POOL LISTS: id|display_name|category — consumed by
        // the Campaign Creator GUI to build the per-relic / per-talent
        // checkbox lists under Reward Pools.
        try
        {
            var relicRepos2 = UnityEngine.Resources.FindObjectsOfTypeAll<RelicRepository>();
            var relicRepo2 = relicRepos2 != null && relicRepos2.Length > 0 ? relicRepos2[0] : null;
            if (relicRepo2 != null)
            {
                var sb = new StringBuilder();
                sb.AppendLine("# id|display_name|category_hint|in_default_pool  — used by Campaign Creator Reward Pools tab");
                sb.AppendLine("# in_default_pool: 1 = shows up in random rewards by default, 0 = not in default pool (boss/customization/hidden)");
                var seen = new HashSet<string>();
                // Primary "category hint" for a given relic id — populated first from
                // the named category lists, so e.g. a relic in offensiveRelics stays
                // labelled Offensive even if it's also in maxGoalRelics etc.
                var catHint = new Dictionary<string, string>();
                // Set of relic ids that are in the default random-reward pool
                // (usedInCampaignPoolRelics). Drives the default checkbox state in the GUI.
                var poolIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (relicRepo2.usedInCampaignPoolRelics != null)
                {
                    for (int i = 0; i < relicRepo2.usedInCampaignPoolRelics.Count; i++)
                    {
                        var r = relicRepo2.usedInCampaignPoolRelics[i];
                        if (r != null && !string.IsNullOrEmpty(r.id)) poolIds.Add(r.id);
                    }
                }
                // Walk every categorised list we know about. Order matters: first
                // non-empty hint wins (Offensive beats Utility etc.).
                var catLists = new (Il2CppSystem.Collections.Generic.List<Rogue.Relic> list, string cat)[]
                {
                    (relicRepo2.offensiveRelics, "Offensive"),
                    (relicRepo2.defensiveRelics, "Defensive"),
                    (relicRepo2.utilityRelics,   "Utility"),
                    (relicRepo2.speedRelics,     "Speed"),
                    (relicRepo2.checkingRelics,  "Checking"),
                    (relicRepo2.powerRelics,     "Power"),
                    (relicRepo2.accuracyRelics,  "Accuracy"),
                    (relicRepo2.chaosRelics,     "Chaos"),
                    (relicRepo2.bossRelics,      "Boss"),
                    (relicRepo2.goalieRelics,    "Goalie"),
                    (relicRepo2.coachRelics,     "Coach"),
                    (relicRepo2.injuryRelics,    "Injury"),
                    (relicRepo2.timerRelics,     "Timer"),
                    (relicRepo2.maxGoalRelics,   "MaxGoal"),
                    (relicRepo2.customizationRelics, "Customization"),
                };
                foreach (var (list, cat) in catLists)
                {
                    if (list == null) continue;
                    for (int i = 0; i < list.Count; i++)
                    {
                        var r = list[i];
                        if (r == null || string.IsNullOrEmpty(r.id)) continue;
                        if (!catHint.ContainsKey(r.id)) catHint[r.id] = cat;
                    }
                }
                // Now walk every Relic SO in memory (catches ones not in any
                // categorised list, e.g. challenge relics or legacy assets).
                var all = UnityEngine.Resources.FindObjectsOfTypeAll<Rogue.Relic>();
                if (all != null)
                {
                    for (int i = 0; i < all.Length; i++)
                    {
                        var r = all[i];
                        if (r == null || string.IsNullOrEmpty(r.id)) continue;
                        if (!seen.Add(r.id)) continue;
                        string display = "";
                        try { display = r.localizedRelicName ?? ""; } catch { }
                        if (string.IsNullOrEmpty(display)) display = r.relicName ?? r.id;
                        string cat = catHint.TryGetValue(r.id, out var c) ? c : "Uncategorised";
                        int inPool = poolIds.Contains(r.id) ? 1 : 0;
                        sb.AppendLine($"{r.id}|{display}|{cat}|{inPool}");
                    }
                }
                File.WriteAllText(Path.Combine(basePath, "_reward_relics.txt"), sb.ToString());
                Plugin.Log.LogInfo($"[Dump] Wrote _reward_relics.txt ({seen.Count} relics, {poolIds.Count} in default pool)");
            }

            var talentRepos2 = UnityEngine.Resources.FindObjectsOfTypeAll<TalentRepository>();
            var talentRepo2 = talentRepos2 != null && talentRepos2.Length > 0 ? talentRepos2[0] : null;
            if (talentRepo2 != null && talentRepo2.talents != null)
            {
                var sb = new StringBuilder();
                sb.AppendLine("# id|display_name|in_default_pool  — used by Campaign Creator Reward Pools tab");
                sb.AppendLine("# in_default_pool: 1 = shows up in random talent rewards by default, 0 = not in default pool");
                var seen = new HashSet<string>();
                // Pull the "used in campaign pool" list via reflection (private field).
                var poolNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    var fld = talentRepo2.GetIl2CppType().GetField("usedInCampaignPoolTalents",
                        Il2CppSystem.Reflection.BindingFlags.NonPublic | Il2CppSystem.Reflection.BindingFlags.Instance);
                    if (fld != null)
                    {
                        var v = fld.GetValue(talentRepo2);
                        if (v != null)
                        {
                            var poolList = v.Cast<Il2CppSystem.Collections.Generic.List<Rogue.Talent>>();
                            if (poolList != null)
                            {
                                for (int i = 0; i < poolList.Count; i++)
                                {
                                    var pt = poolList[i];
                                    if (pt != null && !string.IsNullOrEmpty(pt.name)) poolNames.Add(pt.name);
                                }
                            }
                        }
                    }
                }
                catch { }
                var talents = talentRepo2.talents;
                for (int i = 0; i < talents.Count; i++)
                {
                    var t = talents[i];
                    if (t == null || string.IsNullOrEmpty(t.name)) continue;
                    if (!seen.Add(t.name)) continue;
                    string descKey = ""; try { descKey = t.description ?? ""; } catch { }
                    string nameKey = "";
                    if (!string.IsNullOrEmpty(descKey) && descKey.EndsWith("/description"))
                        nameKey = descKey.Replace("/description", "/name");
                    string display = ResolveI2(nameKey);
                    if (string.IsNullOrEmpty(display)) display = t.name;
                    int inPool = poolNames.Contains(t.name) ? 1 : 0;
                    sb.AppendLine($"{t.name}|{display}|{inPool}");
                }
                File.WriteAllText(Path.Combine(basePath, "_reward_talents.txt"), sb.ToString());
                Plugin.Log.LogInfo($"[Dump] Wrote _reward_talents.txt ({seen.Count} talents, {poolNames.Count} in default pool)");
            }
        }
        catch (Exception ex) { Plugin.Log.LogError($"[Dump] reward-pool list dump: {ex.Message}"); }
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
// Reconcile drafted FAs right before match launch. The game's match
// controller calls InitializeForwardsForTeam to build the on-ice roster
// — if our drafted FAs aren't in teamData.forwards at that moment, they
// never appear on the ice. Running reconcile here (in addition to the
// Team.Initialize postfix) makes sure the TeamData the match sees has
// all the drafted players injected.
// ============================================================
[HarmonyPatch]
public static class PatchMatchInitForwards
{
    static System.Reflection.MethodBase TargetMethod()
    {
        try
        {
            var t = AccessTools.TypeByName("NormalMatchGameModeController");
            if (t == null) return null;
            return AccessTools.Method(t, "InitializeForwardsForTeam");
        }
        catch { return null; }
    }

    [HarmonyPrefix]
    public static void Prefix(TeamData team)
    {
        if (Plugin.IsDefaultMode) return;
        if (team == null) return;
        if (Plugin.PlayerTeamConfigs == null || Plugin.PlayerTeamConfigs.Count == 0) return;
        string name = "";
        try { name = team.teamName?.Trim() ?? ""; } catch { return; }
        bool isCustom = false;
        foreach (var key in Plugin.PlayerTeamConfigs.Keys)
        {
            if (PatchPlayerTeamInit.IsPresetKey(key)) continue;
            if (name.StartsWith(key, StringComparison.OrdinalIgnoreCase)) { isCustom = true; break; }
        }
        if (!isCustom) return;
        PatchPlayerTeamInit.ReconcileDraftedFAs(team, "MatchInit");
    }
}

// ============================================================
// Intercept TeamData.AddForward* — when the game drafts an FA onto a
// custom squad, the native AddForwardToActiveLine can't find a null
// slot in fwds[0..4] (our blanks are non-null), so it appends to
// fwds[5+]. We move it into the first unconfigured slot immediately
// and write the FA's id into lines[0] so InitializeLine resolves the
// id at match-init. Patches all three variants because the draft can
// go through any of them depending on the event path.
// ============================================================
internal static class DraftAddForwardHelper
{
    public static void MoveIntoBlank(TeamData team, ForwardData forward, string source)
    {
        try
        {
            if (team == null || forward == null) return;
            if (Plugin.PlayerTeamConfigs == null || Plugin.PlayerTeamConfigs.Count == 0) return;

            string name = "";
            try { name = team.teamName?.Trim() ?? ""; } catch { return; }
            if (string.IsNullOrEmpty(name)) return;
            TeamConfig cfg = null;
            foreach (var kvp in Plugin.PlayerTeamConfigs)
            {
                if (PatchPlayerTeamInit.IsPresetKey(kvp.Key)) continue;
                if (name.StartsWith(kvp.Key, StringComparison.OrdinalIgnoreCase))
                {
                    cfg = kvp.Value;
                    break;
                }
            }
            Plugin.Log.LogInfo($"[PatchAddForward/{source}] fired: team='{name}' cfgMatched={(cfg != null)} fwd='{forward.firstName} {forward.lastName}' id='{forward.id}'");
            if (cfg == null) return;

            var fwds = team.forwards;
            if (fwds == null) return;
            Plugin.Log.LogInfo($"[PatchAddForward/{source}] fwds.Count={fwds.Count}");

            PlayerConfig[] slotCfgs = { cfg.LW, cfg.RW, cfg.C, cfg.LD, cfg.RD };
            int blankIdx = -1;
            for (int i = 0; i < 5 && i < fwds.Count; i++)
            {
                if (!PatchChooseMetaUI.SlotIsConfigured(slotCfgs[i])) { blankIdx = i; break; }
            }
            if (blankIdx < 0)
            {
                Plugin.Log.LogInfo($"[PatchAddForward/{source}] no blank slot available (all 5 configured)");
                return;
            }

            for (int j = fwds.Count - 1; j >= 5; j--)
            {
                var f = fwds[j];
                if (f != null && f.Pointer == forward.Pointer)
                {
                    try { fwds.RemoveAt(j); } catch { }
                    break;
                }
            }

            fwds[blankIdx] = forward;
            string faId = "";
            try { faId = forward.id ?? ""; } catch { }
            try
            {
                var lns = team.lines;
                if (lns != null && lns.Count > 0 && lns[0] != null)
                {
                    switch (blankIdx)
                    {
                        case 0: lns[0].leftWinger = faId; break;
                        case 1: lns[0].rightWinger = faId; break;
                        case 2: lns[0].center = faId; break;
                        case 3: lns[0].leftDefensemen = faId; break;
                        case 4: lns[0].rightDefensemen = faId; break;
                    }
                }
            }
            catch { }
            Plugin.Log.LogInfo($"[PatchAddForward/{source}] Moved '{forward.firstName} {forward.lastName}' into slot {blankIdx}");
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[PatchAddForward/{source}] {ex.Message}"); }
    }
}

[HarmonyPatch(typeof(TeamData), nameof(TeamData.AddForward))]
public static class PatchTeamDataAddForward
{
    [HarmonyPostfix]
    public static void Postfix(TeamData __instance, ForwardData forward)
        => DraftAddForwardHelper.MoveIntoBlank(__instance, forward, "AddForward");
}

[HarmonyPatch(typeof(TeamData), nameof(TeamData.AddForwardToActiveLine))]
public static class PatchTeamDataAddForwardToActiveLine
{
    [HarmonyPostfix]
    public static void Postfix(TeamData __instance, ForwardData forward)
        => DraftAddForwardHelper.MoveIntoBlank(__instance, forward, "AddForwardToActiveLine");
}

[HarmonyPatch(typeof(TeamData), nameof(TeamData.AddForwardToBench))]
public static class PatchTeamDataAddForwardToBench
{
    [HarmonyPostfix]
    public static void Postfix(TeamData __instance, ForwardData forward)
        => DraftAddForwardHelper.MoveIntoBlank(__instance, forward, "AddForwardToBench");
}

// ============================================================
// Player Team Editor — apply player_teams.txt to player teams
// ============================================================
[HarmonyPatch(typeof(Team), nameof(Team.Initialize))]
public static class PatchPlayerTeamInit
{
    private static readonly string[] PlayerPrefixes = { "Basic", "Defense", "Speed", "Trio" };
    private static readonly HashSet<IntPtr> AppliedTeamPtrs = new();

    internal static bool IsPresetKey(string key)
    {
        if (string.IsNullOrEmpty(key)) return false;
        key = key.ToLowerInvariant();
        return key == "basic" || key == "defense" || key == "speed" || key == "trio";
    }

    [HarmonyPostfix]
    public static void Postfix(Team __instance, TeamData teamData)
    {
        if (Plugin.IsDefaultMode) return;

        // Draft pool runs on EVERY Team.Initialize (including mid-run) so that
        // free agents which load lazily still get their names/stats/skins
        // applied. Per-forward HashSet prevents re-applying to the same one —
        // so earned progress during a run is preserved.
        if (Plugin.DraftPoolConfigs.Count > 0)
            ApplyDraftPool();

        // GUARD for starting-team replacement: only on FRESH runs (GamesPlayed == 0).
        // On continue, player has earned stats/relics that we must not reset.
        if (Plugin.GamesPlayed > 0) return;

        // Check if this is a player team
        if (teamData == null) return;
        if (Plugin.PlayerTeamConfigs.Count == 0) return;
        string name = teamData.teamName?.Trim() ?? "";
        // Match first against hardcoded presets, then fall back to any custom
        // squad key the user added via player_teams/<FolderName>/. The custom
        // squad injection (PatchChooseMetaUI) renames each cloned team to the
        // config key, so name.StartsWith finds them here.
        string matchedKey = null;
        foreach (var p in PlayerPrefixes)
        {
            if (name.StartsWith(p, StringComparison.OrdinalIgnoreCase))
            {
                matchedKey = p.ToLower();
                break;
            }
        }
        if (matchedKey == null)
        {
            foreach (var key in Plugin.PlayerTeamConfigs.Keys)
            {
                // Skip presets (already handled above via PlayerPrefixes)
                if (IsPresetKey(key)) continue;
                if (name.StartsWith(key, StringComparison.OrdinalIgnoreCase))
                {
                    matchedKey = key;
                    break;
                }
            }
        }
        if (matchedKey == null) return;

        if (!Plugin.PlayerTeamConfigs.ContainsKey(matchedKey)) return;

        // Re-apply on every Team.Initialize during the fresh-run window
        // (GamesPlayed == 0) — the game re-initializes the Team between map
        // generation and match launch (including for Spartan/Challenge), which
        // resets stats/skins/colors back to defaults. Only the first call for
        // each TeamData does the destructive talent/relic wipe; subsequent
        // calls refresh the non-destructive fields so the player still sees
        // their modded team during the Spartan match.
        bool firstApply = !AppliedTeamPtrs.Contains(teamData.Pointer);
        if (firstApply) AppliedTeamPtrs.Add(teamData.Pointer);

        var cfg = Plugin.PlayerTeamConfigs[matchedKey];
        Plugin.Log.LogInfo($"[PlayerTeam] Applying config for '{matchedKey}' to team '{name}' (firstApply={firstApply})");

        // For custom squads: reconcile drafted FAs into the starting-five
        // slots. The problem: our blank placeholder slots at fwds[0..2] (for
        // unconfigured LW/RW/C) are non-null, so the game's AddForwardToActiveLine
        // doesn't recognize them as "empty" — it appends the drafted FA to
        // fwds[5+] instead of overwriting the blank. Result: lines[0] still
        // references blank.id, the drafted FA is stuck on the bench, and
        // nothing renders on the ice at that position.
        //
        // Fix: scan fwds[0..4] for blank placeholders (empty firstName AND
        // lastName — the marker we set in BlankUnconfiguredSlots), scan
        // fwds[5+] for real drafted forwards, and swap them in. Also update
        // lines[0] to reference the drafted FA's id at each moved slot.
        if (!IsPresetKey(matchedKey))
            ReconcileDraftedFAs(teamData, "Team.Init");


        // Re-sync lines[0] for CONFIGURED positions only. The game
        // regenerates forward ids between menu-inject and match-init (the
        // log showed lines[0].LW pointing at a defunct id 7aa98674 while
        // fwds[0] had the new BigBOy id 5beebbe6), so Big BOy never
        // resolved at render time → not on the ice. We only rewrite the
        // slots WE configured; free-agent picks in other slots stay put.
        if (!IsPresetKey(matchedKey))
        {
            try
            {
                var lns = teamData.lines;
                if (lns != null && lns.Count > 0 && lns[0] != null)
                {
                    var l0 = lns[0];
                    var fwds = teamData.forwards;
                    void SyncSlot(int idx, PlayerConfig pc, Action<string> setter)
                    {
                        if (pc == null) return;
                        bool has = !string.IsNullOrEmpty(pc.Name)
                                    || !string.IsNullOrEmpty(pc.ImportPlayer)
                                    || !string.IsNullOrEmpty(pc.Face)
                                    || !string.IsNullOrEmpty(pc.Ability)
                                    || (pc.Talents != null && pc.Talents.Count > 0)
                                    || pc.Speed != 50 || pc.ShotPower != 50
                                    || pc.Accuracy != 50 || pc.Checking != 50;
                        if (!has) return;
                        if (fwds == null || idx >= fwds.Count) return;
                        var f = fwds[idx];
                        if (f == null) return;
                        try { setter(f.id ?? ""); } catch { }
                    }
                    SyncSlot(0, cfg.LW, id => l0.leftWinger = id);
                    SyncSlot(1, cfg.RW, id => l0.rightWinger = id);
                    SyncSlot(2, cfg.C,  id => l0.center = id);
                    SyncSlot(3, cfg.LD, id => l0.leftDefensemen = id);
                    SyncSlot(4, cfg.RD, id => l0.rightDefensemen = id);
                    Plugin.Log.LogInfo($"[PlayerTeam] Re-synced lines[0]: LW='{l0.leftWinger}' RW='{l0.rightWinger}' C='{l0.center}' LD='{l0.leftDefensemen}' RD='{l0.rightDefensemen}'");
                }
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"[PlayerTeam] Re-sync lines failed: {ex.Message}"); }
        }

        // Roster snapshot pre-apply so we can diagnose "game one has no
        // players except goalie" situations.
        try
        {
            var rf = teamData.forwards;
            if (rf != null)
            {
                for (int i = 0; i < rf.Count; i++)
                {
                    var f = rf[i];
                    Plugin.Log.LogInfo($"[PlayerTeam] pre-apply fwds[{i}] = {(f == null ? "null" : (f.firstName + " " + f.lastName + " id=" + (f.id ?? "")))}");
                }
            }
            Plugin.Log.LogInfo($"[PlayerTeam] pre-apply goalie = {(teamData.goalie == null ? "null" : (teamData.goalie.firstName + " " + teamData.goalie.lastName))}");
            var lns = teamData.lines;
            if (lns != null && lns.Count > 0 && lns[0] != null)
            {
                var l0 = lns[0];
                Plugin.Log.LogInfo($"[PlayerTeam] pre-apply lines[0]: LW='{l0.leftWinger}' RW='{l0.rightWinger}' C='{l0.center}' LD='{l0.leftDefensemen}' RD='{l0.rightDefensemen}'");
            }
            else Plugin.Log.LogInfo($"[PlayerTeam] pre-apply lines is null/empty");
        }
        catch { }
        ApplyPlayerTeamConfig(teamData, cfg, __instance, firstApply);
        // Snapshot after apply too, so we can see whether the game or our
        // code mutated the roster between menu-inject and match-init.
        try
        {
            var rf = teamData.forwards;
            if (rf != null)
            {
                for (int i = 0; i < rf.Count; i++)
                {
                    var f = rf[i];
                    Plugin.Log.LogInfo($"[PlayerTeam] post-apply fwds[{i}] = {(f == null ? "null" : (f.firstName + " " + f.lastName))}");
                }
            }
        }
        catch { }
    }

    // Finds ForwardData whose id appears in lines[0] but is missing from
    // teamData.forwards, and injects a clone into the matching slot. The
    // game's FA draft for custom squads writes the id to lines[0] but
    // never adds the SO to fwds (because our non-null blank placeholders
    // block AddForwardToActiveLine's null-slot search). Instantiate the
    // ForwardData (not share the template) so lifetime is owned by us.
    internal static void ReconcileDraftedFAs(TeamData teamData, string source)
    {
        if (teamData == null) return;
        try
        {
            var lns = teamData.lines;
            var fwds = teamData.forwards;
            if (lns == null || lns.Count == 0 || lns[0] == null) return;
            if (fwds == null || fwds.Count < 5) return;

            string[] lineIds = {
                lns[0].leftWinger ?? "",
                lns[0].rightWinger ?? "",
                lns[0].center ?? "",
                lns[0].leftDefensemen ?? "",
                lns[0].rightDefensemen ?? ""
            };
            Plugin.Log.LogInfo($"[Reconcile/{source}] team='{teamData.teamName}' lineIds: LW='{lineIds[0]}' RW='{lineIds[1]}' C='{lineIds[2]}' LD='{lineIds[3]}' RD='{lineIds[4]}'");

            bool IsInFwds(string id)
            {
                if (string.IsNullOrEmpty(id)) return false;
                for (int i = 0; i < fwds.Count; i++)
                {
                    var f = fwds[i];
                    if (f != null && f.id == id) return true;
                }
                return false;
            }

            var missingByPos = new System.Collections.Generic.Dictionary<int, string>();
            for (int pos = 0; pos < 5; pos++)
            {
                if (!string.IsNullOrEmpty(lineIds[pos]) && !IsInFwds(lineIds[pos]))
                    missingByPos[pos] = lineIds[pos];
            }
            if (missingByPos.Count == 0) return;
            Plugin.Log.LogInfo($"[Reconcile/{source}] missing FA ids: {missingByPos.Count}");

            var allCS = UnityEngine.Resources.FindObjectsOfTypeAll<State.CampaignState>();
            Il2CppSystem.Collections.Generic.List<Data.ForwardData> freeAgents = null;
            if (allCS != null && allCS.Length > 0) freeAgents = allCS[0].freeAgents;

            Data.ForwardData[] allFwdsScene = null;
            try { allFwdsScene = UnityEngine.Resources.FindObjectsOfTypeAll<Data.ForwardData>(); }
            catch { }

            Data.ForwardData LookupById(string id)
            {
                if (string.IsNullOrEmpty(id)) return null;
                if (freeAgents != null)
                {
                    for (int i = 0; i < freeAgents.Count; i++)
                    {
                        var f = freeAgents[i];
                        if (f != null && f.id == id) return f;
                    }
                }
                if (allFwdsScene != null)
                {
                    for (int i = 0; i < allFwdsScene.Length; i++)
                    {
                        var f = allFwdsScene[i];
                        if (f != null && f.id == id) return f;
                    }
                }
                return null;
            }

            int moved = 0;
            foreach (var kvp in missingByPos)
            {
                int pos = kvp.Key;
                string id = kvp.Value;
                var fa = LookupById(id);
                if (fa == null)
                {
                    Plugin.Log.LogWarning($"[Reconcile/{source}] FA id '{id}' for slot {pos} NOT FOUND");
                    continue;
                }
                if (pos >= fwds.Count) continue;
                // Instantiate so we own the SO (no unloading surprises) and
                // preserve the id so lines[0] lookups still resolve.
                Data.ForwardData injected = fa;
                try
                {
                    var clone = UnityEngine.Object.Instantiate(fa);
                    try { clone.id = fa.id; } catch { }
                    injected = clone;
                }
                catch { }
                fwds[pos] = injected;
                moved++;
                Plugin.Log.LogInfo($"[Reconcile/{source}] Injected FA '{injected.firstName} {injected.lastName}' into slot {pos}");
            }
            Plugin.Log.LogInfo($"[Reconcile/{source}] done moved={moved}");
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[Reconcile/{source}] {ex.Message}"); }
    }

    internal static void ApplyPlayerTeamConfig(TeamData team, TeamConfig cfg, Team instance, bool firstApply)
    {
        // Diagnostic: print which slot configs the loader populated so we can
        // tell "loaded but skipped" from "never loaded at all".
        try
        {
            Plugin.Log.LogDebug(
                $"[PlayerTeam] Slot cfg for '{team?.teamName}': " +
                $"LW='{cfg?.LW?.Name}' RW='{cfg?.RW?.Name}' C='{cfg?.C?.Name}' " +
                $"LD='{cfg?.LD?.Name}' RD='{cfg?.RD?.Name}' G='{cfg?.Goalie?.Name}'");
        }
        catch { }
        // On firstApply only: wipe earned talents/relics from previous runs so
        // the config's Talents + Relics are the only ones present. Re-entries
        // during the same run (e.g. Spartan match re-init) MUST NOT wipe —
        // that would destroy in-run earned progress.
        if (firstApply)
        {
            try
            {
                if (team.forwards != null)
                {
                    for (int i = 0; i < team.forwards.Count; i++)
                    {
                        var fwd = team.forwards[i];
                        if (fwd == null) continue;
                        try { fwd.powerups = new Il2CppSystem.Collections.Generic.List<Rogue.Talent>(); } catch {}
                    }
                }
                if (team.goalie != null)
                    try { team.goalie.powerups = new Il2CppSystem.Collections.Generic.List<Rogue.Talent>(); } catch {}
                try { team.relics = new Il2CppSystem.Collections.Generic.List<Rogue.Relic>(); } catch {}
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"[PlayerTeam] New-run wipe: {ex.Message}"); }
        }

        // Logo from another team
        if (!string.IsNullOrEmpty(cfg.LogoFrom))
        {
            var logoTeam = PatchBossLaunchMatch.FindTeamByName(cfg.LogoFrom);
            if (logoTeam != null && logoTeam != team)
            {
                team.logo = logoTeam.logo;
                team.alternateBigLogo = logoTeam.alternateBigLogo;
                if (logoTeam.nickname != null) team.nickname = logoTeam.nickname;
                Plugin.Log.LogInfo($"[PlayerTeam] Applied logo from '{cfg.LogoFrom}'");
            }
        }

        // Team name, city, abbreviation
        if (!string.IsNullOrEmpty(cfg.Name)) team.teamName = cfg.Name;
        if (!string.IsNullOrEmpty(cfg.City)) team.city = cfg.City;
        if (!string.IsNullOrEmpty(cfg.Abbreviation)) team.nickname = cfg.Abbreviation;

        // Home jersey colors
        if (cfg.JerseyPrimary != null || cfg.JerseySecondary != null || cfg.JerseyAccent != null)
        {
            var p = cfg.JerseyPrimary != null ? new Color(cfg.JerseyPrimary[0]/255f, cfg.JerseyPrimary[1]/255f, cfg.JerseyPrimary[2]/255f) : team.primaryColorPlayer;
            var s = cfg.JerseySecondary != null ? new Color(cfg.JerseySecondary[0]/255f, cfg.JerseySecondary[1]/255f, cfg.JerseySecondary[2]/255f) : team.secondaryColorPlayer;
            var a = cfg.JerseyAccent != null ? new Color(cfg.JerseyAccent[0]/255f, cfg.JerseyAccent[1]/255f, cfg.JerseyAccent[2]/255f) : Color.white;
            PatchBossLaunchMatch.SetColors(team.homeColors, p, s, a);
            team.primaryColorPlayer = p;
            team.secondaryColorPlayer = s;
        }

        // Away colors
        if (cfg.AwayPrimary != null || cfg.AwaySecondary != null || cfg.AwayAccent != null)
        {
            var ap = cfg.AwayPrimary != null ? new Color(cfg.AwayPrimary[0]/255f, cfg.AwayPrimary[1]/255f, cfg.AwayPrimary[2]/255f) : Color.white;
            var as2 = cfg.AwaySecondary != null ? new Color(cfg.AwaySecondary[0]/255f, cfg.AwaySecondary[1]/255f, cfg.AwaySecondary[2]/255f) : team.primaryColorPlayer;
            var aa = cfg.AwayAccent != null ? new Color(cfg.AwayAccent[0]/255f, cfg.AwayAccent[1]/255f, cfg.AwayAccent[2]/255f) : Color.white;
            PatchBossLaunchMatch.SetColors(team.awayColors, ap, as2, aa);
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

        // Apply uniform skins and equipment colors to existing forwards
        var fwds = team.forwards;
        if (fwds != null)
        {
            for (int i = 0; i < fwds.Count; i++)
            {
                if (fwds[i] == null) continue;
                ApplyUniformToForward(fwds[i], cfg.Uniform);
                PatchBossLaunchMatch.ApplyTeamEquipmentColors(fwds[i], cfg);
            }

            // Per-slot player overrides — for each starting lineup slot that
            // has a file on disk, apply that player's name/stats/face/colors
            // to the matching forward. Positions in cfg are set by
            // LoadPlayersFolder (matches the "Left Defense.txt" etc. filenames).
            // Forward array order: 0=LW, 1=RW, 2=C, 3=LD, 4=RD.
            PlayerConfig[] line1 = { cfg.LW, cfg.RW, cfg.C, cfg.LD, cfg.RD };
            for (int i = 0; i < Math.Min(fwds.Count, line1.Length); i++)
            {
                var pc = line1[i];
                if (pc == null || fwds[i] == null) continue;
                // Skip slots with no real data — LoadPlayersFolder leaves
                // untouched slots at their default empty PlayerConfig.
                bool hasAny = !string.IsNullOrEmpty(pc.Name) || !string.IsNullOrEmpty(pc.ImportPlayer)
                               || !string.IsNullOrEmpty(pc.Face)
                               || pc.Speed != 50 || pc.ShotPower != 50
                               || pc.Accuracy != 50 || pc.Checking != 50
                               || (pc.Talents != null && pc.Talents.Count > 0)
                               || !string.IsNullOrEmpty(pc.Ability);
                if (!hasAny) continue;
                try { PatchBossLaunchMatch.ApplyPlayerConfig(fwds[i], pc, cfg.Uniform); }
                catch (Exception ex) { Plugin.Log.LogWarning($"[PlayerTeam] slot {i} apply error: {ex.Message}"); }
                Plugin.Log.LogInfo($"[PlayerTeam] Applied slot override: {pc.Name ?? pc.ImportPlayer}");
            }
        }

        // Apply goalie config
        if (team.goalie != null && cfg.Goalie != null && !string.IsNullOrEmpty(cfg.Goalie.Name))
        {
            if (!string.IsNullOrEmpty(cfg.Goalie.ImportPlayer))
            {
                var srcGoalie = PatchBossLaunchMatch.FindGoalieByName(cfg.Goalie.ImportPlayer);
                if (srcGoalie != null)
                {
                    PatchBossLaunchMatch.CopyGoalieData(srcGoalie, team.goalie);
                    Plugin.Log.LogInfo($"[PlayerTeam] Imported goalie '{srcGoalie.firstName} {srcGoalie.lastName}'");
                }
            }
            PatchBossLaunchMatch.ApplyGoalieConfig(team.goalie, cfg.Goalie);
            PatchBossLaunchMatch.ApplyTeamEquipmentColorsToGoalie(team.goalie, team, cfg);
        }

        // Apply relics (additive — adds starting relics). Only on firstApply:
        // subsequent re-applies during the same run (e.g. Spartan match init)
        // must not add duplicate copies of every starting relic.
        if (firstApply && cfg.Relics.Count > 0)
        {
            foreach (var r in cfg.Relics)
                PatchBossLaunchMatch.GiveRelic(team, r);
            Plugin.Log.LogInfo($"[PlayerTeam] Added {cfg.Relics.Count} starting relics");
        }

        Plugin.Log.LogInfo($"[PlayerTeam] Applied team config for '{team.teamName}'");
    }

    private static void ApplyUniformToForward(ForwardData f, UniformConfig uniform)
    {
        try
        {
            if (!string.IsNullOrEmpty(uniform.Body)) f.bodySkin = uniform.Body;
            if (!string.IsNullOrEmpty(uniform.BodyAway)) f.bodyAwaySkin = uniform.BodyAway;
            if (!string.IsNullOrEmpty(uniform.Helmet)) f.helmetSkin = uniform.Helmet;
            if (!string.IsNullOrEmpty(uniform.HelmetAway)) f.helmetAwaySkin = uniform.HelmetAway;
            if (!string.IsNullOrEmpty(uniform.Gloves)) f.gloveSkin = uniform.Gloves;
            if (!string.IsNullOrEmpty(uniform.GlovesAway)) f.gloveAwaySkin = uniform.GlovesAway;
            if (!string.IsNullOrEmpty(uniform.Pants)) f.pantsSkin = uniform.Pants;
            if (!string.IsNullOrEmpty(uniform.PantsAway)) f.pantsAwaySkin = uniform.PantsAway;
            if (!string.IsNullOrEmpty(uniform.Skates)) f.skateSkin = uniform.Skates;
            if (!string.IsNullOrEmpty(uniform.SkatesAway)) f.skateAwaySkin = uniform.SkatesAway;
            if (!string.IsNullOrEmpty(uniform.Bicep)) f.bicepSkin = uniform.Bicep;
            if (!string.IsNullOrEmpty(uniform.BicepAway)) f.bicepAwaySkin = uniform.BicepAway;
            if (!string.IsNullOrEmpty(uniform.Stick)) f.stickSkin = uniform.Stick;
            HandleNoHelmetSentinel(f);
        }
        catch (Exception ex) { Plugin.Log.LogError($"[PlayerTeam] Uniform apply error: {ex.Message}"); }
    }

    // Forwards explicitly flagged as no-helmet via `Helmet = none` in config.
    // Checked by the fallback default-fill code so the default helmet skin
    // doesn't sneak back in for these players.
    internal static readonly HashSet<IntPtr> NoHelmetForwards = new HashSet<IntPtr>();

    /// Shared no-helmet sentinel handler. If helmetSkin was stamped with the
    /// `__NO_HELMET__` marker by ResolveSkin (Helmet = none in config), this
    /// registers the forward's face in ForwardDataExtensions.HeadsWithoutHelmets
    /// and flags the forward in NoHelmetForwards so the default-fill code
    /// won't re-stamp a helmet skin onto it. MUST be called from every code
    /// path that assigns helmetSkin.
    internal static void HandleNoHelmetSentinel(ForwardData f)
    {
        if (f == null) return;
        if (f.helmetSkin == "__NO_HELMET__" || f.helmetAwaySkin == "__NO_HELMET__")
        {
            Plugin.RegisterFaceAsHelmetless(f.headSkin);
            if (f.helmetSkin == "__NO_HELMET__") f.helmetSkin = "";
            if (f.helmetAwaySkin == "__NO_HELMET__") f.helmetAwaySkin = "";
            NoHelmetForwards.Add(f.Pointer);
            Plugin.Log.LogInfo($"[NoHelmet] '{f.firstName} {f.lastName}' face '{f.headSkin}' flagged as helmetless");
        }
    }

    // Names we've already announced in the log this session. ApplyDraftPool
    // runs on every Team.Initialize (many times per scene); this keeps the
    // log to one line per unique player rather than one per instance.
    private static readonly HashSet<string> _loggedDraftNames = new(StringComparer.OrdinalIgnoreCase);

    internal static void ApplyDraftPool()
    {
        var allFwds = UnityEngine.Resources.FindObjectsOfTypeAll<ForwardData>();
        if (allFwds == null || allFwds.Length == 0) return;

        // Track which configs got applied via name-match so the index-based
        // fallback only uses the leftovers.
        var appliedConfigKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        int applied = 0;
        foreach (var f in allFwds)
        {
            if (f == null) continue;
            if (Plugin.AppliedDraftPtrs.Contains(f.Pointer)) continue;  // already done

            string fullName = $"{f.firstName} {f.lastName}".Trim().ToLower();
            string lastName = (f.lastName ?? "").Trim().ToLower();

            PlayerConfig pc = null;
            string matchedKey = null;
            foreach (var kvp in Plugin.DraftPoolConfigs)
            {
                if (kvp.Key == fullName || kvp.Key == lastName)
                {
                    pc = kvp.Value;
                    matchedKey = kvp.Key;
                    break;
                }
            }
            if (pc == null) continue;

            appliedConfigKeys.Add(matchedKey);
            Plugin.AppliedDraftPtrs.Add(f.Pointer);
            // Only log once per unique name — the game keeps multiple
            // ForwardData instances per named player (league rosters, bench,
            // pick-screen templates) and we mod every copy, so suppress
            // duplicate log lines to keep the log readable.
            string nameKey = $"{f.firstName} {f.lastName}".Trim();
            if (_loggedDraftNames.Add(nameKey))
                Plugin.Log.LogInfo($"[PlayerTeam] Modifying draft player '{nameKey}' (name match)");
            ApplyConfigToForward(f, pc);
            applied++;
        }

        // INDEX-BASED FALLBACK: any configs whose filename didn't match a
        // vanilla free-agent name get applied to the actual generated free
        // agents (CampaignState.preGeneratedFreeAgents), in order. This is
        // the common case when the user names their draft files with
        // original labels rather than matching vanilla names.
        var leftovers = new List<PlayerConfig>();
        foreach (var kvp in Plugin.DraftPoolConfigs)
            if (!appliedConfigKeys.Contains(kvp.Key))
                leftovers.Add(kvp.Value);

        if (leftovers.Count > 0)
        {
            var targets = GetGeneratedFreeAgentForwards(allFwds);
            int n = Math.Min(leftovers.Count, targets.Count);
            for (int i = 0; i < n; i++)
            {
                var f = targets[i];
                if (f == null) continue;
                if (Plugin.AppliedDraftPtrs.Contains(f.Pointer)) continue;
                Plugin.AppliedDraftPtrs.Add(f.Pointer);
                Plugin.Log.LogInfo($"[PlayerTeam] Modifying generated free agent #{i} '{f.firstName} {f.lastName}' -> '{leftovers[i].Name}' (index fallback)");
                ApplyConfigToForward(f, leftovers[i]);
                applied++;
            }
        }

        if (applied > 0)
            Plugin.Log.LogInfo($"[PlayerTeam] Draft pool: {applied} player instance(s) modified ({_loggedDraftNames.Count}/{Plugin.DraftPoolConfigs.Count} unique names)");
    }

    // Look up the ForwardData instances that correspond to currently
    // pre-generated free agents. Reads CampaignState.preGeneratedFreeAgents
    // (List<PreGeneratedFreeAgentData>) and matches each entry's
    // templateFullName to a loaded ForwardData.
    private static List<ForwardData> GetGeneratedFreeAgentForwards(ForwardData[] allFwds)
    {
        var result = new List<ForwardData>();
        try
        {
            // Try BOTH sources and take whichever has items — PreGenerateFreeAgents
            // may overwrite CampaignState.preGeneratedFreeAgents with a new list
            // reference, leaving the `output` arg we cached pointing at an old
            // empty list. Looking up the current property value covers that case.
            var cached = PatchPreGenerateFreeAgents.LastOutput;
            Il2CppSystem.Collections.Generic.List<Rogue.FreeAgents.PreGeneratedFreeAgentData> fromState = null;

            var allCS = UnityEngine.Resources.FindObjectsOfTypeAll<State.CampaignState>();
            if (allCS != null && allCS.Length > 0)
            {
                var cs = allCS[0];
                var csType = cs.GetIl2CppType();
                var prop = csType.GetProperty("preGeneratedFreeAgents");
                if (prop != null)
                {
                    var val = prop.GetValue(cs);
                    if (val != null)
                        fromState = val.TryCast<Il2CppSystem.Collections.Generic.List<Rogue.FreeAgents.PreGeneratedFreeAgentData>>();
                }
                if (fromState == null)
                {
                    var field = csType.GetField("preGeneratedFreeAgents")
                             ?? csType.GetField("_preGeneratedFreeAgents")
                             ?? csType.GetField("m_PreGeneratedFreeAgents");
                    if (field != null)
                    {
                        var val = field.GetValue(cs);
                        if (val != null)
                            fromState = val.TryCast<Il2CppSystem.Collections.Generic.List<Rogue.FreeAgents.PreGeneratedFreeAgentData>>();
                    }
                }
            }

            int cachedCount = cached?.Count ?? -1;
            int stateCount = fromState?.Count ?? -1;
            Plugin.Log.LogInfo($"[PlayerTeam] PreGenerated list counts: cached_arg={cachedCount}, campaign_state={stateCount}");

            // Pick the richer source
            var list = (stateCount > cachedCount) ? fromState : cached;
            if (list == null || list.Count == 0)
            {
                Plugin.Log.LogInfo("[PlayerTeam] preGeneratedFreeAgents is empty — pick screen not yet populated");
                return result;
            }

            for (int i = 0; i < list.Count; i++)
            {
                var pg = list[i];
                if (pg == null) continue;
                string templ = pg.templateFullName;
                if (string.IsNullOrEmpty(templ)) continue;

                // Match against ForwardData — try UnityEngine.Object name first,
                // then firstName+lastName composite.
                ForwardData hit = null;
                foreach (var f in allFwds)
                {
                    if (f == null) continue;
                    string assetName = f.name ?? "";
                    string composed = $"{f.firstName} {f.lastName}".Trim();
                    if (assetName.Equals(templ, StringComparison.OrdinalIgnoreCase)
                        || composed.Equals(templ, StringComparison.OrdinalIgnoreCase))
                    {
                        hit = f;
                        break;
                    }
                }
                if (hit != null) result.Add(hit);
            }
            Plugin.Log.LogInfo($"[PlayerTeam] GetGeneratedFreeAgentForwards: resolved {result.Count}/{list.Count} templates to ForwardData");
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[PlayerTeam] GetGeneratedFreeAgentForwards: {ex.Message}"); }
        return result;
    }

    private static void ApplyConfigToForward(ForwardData f, PlayerConfig pc)
    {
        // NAME is skipped intentionally for draft-pool free agents. Renaming
        // them breaks the rest of the customization (looks, stats, abilities
        // stop applying) because the lookup pipeline keys by the vanilla
        // firstName/lastName in several places. Look/ability/stat mods all
        // still apply — only the display name stays at the game's default.
        // Revisit once the name-keyed lookups are refactored to use pointer
        // identity instead.

            // Apply stats (only if non-default, i.e. not 50)
            if (pc.Speed != 50) f.speed = pc.Speed;
            if (pc.ShotPower != 50) f.shotPower = pc.ShotPower;
            if (pc.Accuracy != 50) f.shotAccuracy = pc.Accuracy;
            if (pc.Checking != 50) f.checking = pc.Checking;

            // Apply size
            if (!string.IsNullOrEmpty(pc.Size) && pc.Size != "Medium")
            {
                try
                {
                    if (pc.Size.Equals("ExtraSmall", StringComparison.OrdinalIgnoreCase)) f.skaterSize = Data.SkaterSize.ExtraSmall;
                    else if (pc.Size.Equals("Small", StringComparison.OrdinalIgnoreCase)) f.skaterSize = Data.SkaterSize.Small;
                    else if (pc.Size.Equals("Medium", StringComparison.OrdinalIgnoreCase)) f.skaterSize = Data.SkaterSize.Medium;
                    else if (pc.Size.Equals("Big", StringComparison.OrdinalIgnoreCase)) f.skaterSize = Data.SkaterSize.Big;
                    else if (pc.Size.Equals("ExtraBig", StringComparison.OrdinalIgnoreCase)) f.skaterSize = Data.SkaterSize.ExtraBig;
                    else if (pc.Size.Equals("ExtraExtraBig", StringComparison.OrdinalIgnoreCase)) f.skaterSize = Data.SkaterSize.ExtraExtraBig;
                }
                catch {}
            }

            // Apply face
            if (!string.IsNullOrEmpty(pc.Face))
            {
                string resolved = Plugin.ResolveSkin(pc.Face, "face");
                if (resolved != "RANDOM_FACE")
                    f.headSkin = resolved;
            }

            // Apply handedness and skin color
            if (pc.Lefty) f.isLefty = true;
            if (pc.Black) f.isBlack = true;

            // Apply number
            if (pc.Number != 88) f.number = pc.Number;

            // Apply ability
            if (!string.IsNullOrEmpty(pc.Ability))
            {
                try
                {
                    PatchBossLaunchMatch.EnsureRepos();
                    var abilityRepos = UnityEngine.Resources.FindObjectsOfTypeAll<Rogue.Powerups.Repository.AbilityRepository>();
                    if (abilityRepos != null && abilityRepos.Length > 0)
                    {
                        var repo = abilityRepos[0];
                        if (repo.abilities != null)
                        {
                            for (int ai = 0; ai < repo.abilities.Count; ai++)
                            {
                                var ab = repo.abilities[ai];
                                if (ab != null && ab.name.Equals(pc.Ability, StringComparison.OrdinalIgnoreCase))
                                {
                                    f.ability = ab;
                                    Plugin.Log.LogInfo($"[PlayerTeam]   Set ability: {ab.name}");
                                    break;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex) { Plugin.Log.LogError($"[PlayerTeam] Ability error: {ex.Message}"); }
            }

            // Apply talents
            if (pc.Talents.Count > 0)
            {
                f.powerups = new Il2CppSystem.Collections.Generic.List<Rogue.Talent>();
                foreach (var talentName in pc.Talents)
                    PatchBossLaunchMatch.GiveTalentToPlayer(f, talentName);
            }

            // Apply per-player uniform overrides
            if (pc.StickOverride != null) f.stickSkin = pc.StickOverride;
            if (pc.HelmetOverride != null) f.helmetSkin = pc.HelmetOverride;
            if (pc.BodyOverride != null) f.bodySkin = pc.BodyOverride;
            if (pc.BodyAwayOverride != null) f.bodyAwaySkin = pc.BodyAwayOverride;
            if (pc.GlovesOverride != null) f.gloveSkin = pc.GlovesOverride;
            if (pc.PantsOverride != null) f.pantsSkin = pc.PantsOverride;
            if (pc.SkatesOverride != null) f.skateSkin = pc.SkatesOverride;
            if (pc.BicepOverride != null) f.bicepSkin = pc.BicepOverride;

            // Apply per-player color overrides
            PatchBossLaunchMatch.ApplyPlayerColorOverrides(f, pc);

            // Apply glasses
            if (!string.IsNullOrEmpty(pc.Glasses))
                try { f.glassesSkin = pc.Glasses; } catch {}

            // Helmet=none: catch the sentinel from team uniform OR per-player
            // override paths that assigned it above — final check so all
            // helmet-set flows normalize through the same logic.
            HandleNoHelmetSentinel(f);
            // Tail stats-log removed — the "Modifying" line in ApplyDraftPool
            // already announces each unique player once per session. Leaving
            // it in here caused N-copy-per-name spam since the game keeps
            // multiple ForwardData instances per player.
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
    public string Description = "";
    public string SquadHead = "";     // face name for the custom-squad tile icon
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
    // Opt-out for the auto-added Bench Bonus starting relic. Custom
    // player-team squads auto-prepend "Bench Bonus" to m_RelicsData (the
    // relic that buffs bench players by their slot position — the signature
    // mechanic of player-chosen squads). Set "No Bench Bonus = yes" in
    // team.txt to disable.
    public bool NoBenchBonus = false;
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
