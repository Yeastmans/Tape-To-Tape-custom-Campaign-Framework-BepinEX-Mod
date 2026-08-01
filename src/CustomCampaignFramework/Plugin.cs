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

[BepInPlugin("com.mods.customcampaign", "Custom Campaign Framework", "2.1.34")]
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
    internal static List<PlayerConfig> FreeAgentPoolList = new List<PlayerConfig>();
    // Superstar pool configs (player_teams/superstars/), ordered by filename.
    // Replace the superstars offered on the OldSquadMenu "pick a superstar" screen.
    internal static List<PlayerConfig> SuperstarPoolList = new List<PlayerConfig>();
    // templateFullName (lowercase) → PlayerConfig for FA slots — populated by
    // ApplyFreeAgentPool at pool generation time, consumed at sign time.
    // Kept separate from DraftPoolConfigs so bench players are never touched.
    internal static Dictionary<string, PlayerConfig> FreeAgentSignedConfigs = new Dictionary<string, PlayerConfig>(StringComparer.OrdinalIgnoreCase);
    internal static HashSet<IntPtr> AppliedFreeAgentPtrs = new HashSet<IntPtr>();
    internal static bool DraftPoolApplied = false;
    // Free-agent node cap per run. Long campaigns otherwise accumulate more
    // free agents than the roster has slots for and crash the game on
    // 5th+ FA signing. When the cap is reached, further FanNumber1 nodes
    // get substituted with GeneralManager (team-upgrade) nodes.
    internal const int MaxFreeAgentNodes = 4;  // First 4 FA nodes kept, rest substituted with TeamTraining
    internal static int FreeAgentNodesPlaced = 0;

    // ===== GM SQUAD =====
    // The General Manager squad starts with an unfilled roster and depends on GM
    // (free-agent) nodes to sign players. Its own RunSquadScriptableObject.maps put
    // those nodes where they need to be — including one right at the start of map 1
    // so the team can be filled before the first game.
    //
    // Two things break that in a custom campaign: the campaign can start on any act
    // (so map 1's layout isn't the GM squad's map 1), and "Maps = X" overwrites
    // EVERY squad's .maps with the chosen source squad's, wiping the GM layout
    // outright. So capture the GM squad's original map list before that override
    // runs, and re-impose its GM node layers on whatever map is actually generated.
    //
    // Only active when the player actually picked the GM squad.
    internal static Il2CppSystem.Collections.Generic.List<MapConfig> GmSquadMaps = null;   // captured BEFORE the Maps override
    internal static string GmSquadId = null;
    internal static bool GmSquadActive = false;
    // Layer index -> how many players that GM node lets you sign
    // (MapLayerNodeType.gmSelectionCount). Converting a node's TYPE alone isn't
    // enough: the selection count comes along with the node it replaced, so the
    // node offered the wrong number of players. Both have to be set.
    internal static readonly Dictionary<int, int> GmForcedLayers = new Dictionary<int, int>();
    internal static readonly HashSet<int> GmLayersDone = new HashSet<int>();

    // Fallback only, for when the GM squad's own layout can't be read — the real
    // count from its MapConfig is always preferred.
    internal const int GmDefaultSelectionCount = 5;

    // The opening node of map 1 is overridden OUTRIGHT: type and selection count
    // are both imposed even when the map already put a GM node there, because that
    // existing node carries its own (smaller) count and the run needs enough picks
    // to fill the roster before game 1. Every other forced layer keeps an existing
    // GM node exactly as the map intended. -1 = no override this map.
    internal static int GmOverrideLayer = -1;
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
    internal static bool UseGauntletMap = false;
    // "Maps = <squad>" in campaign.txt — play every run on this base squad's
    // map layout (Basic, Defence, Speedy, Gauntlet, Solo, ...). Empty = each
    // squad keeps its own maps. "Gauntlet Map = yes" is the legacy special
    // case and behaves like Maps = Gauntlet (plus injecting all squads).
    internal static string MapSourceSquad = "";

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
            UseGauntletMap = false; // reset before parsing — absence of key means off
            MapSourceSquad = "";    // reset — absence of key means each squad keeps its own maps
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
                    else if (key == "gauntlet map") UseGauntletMap = val.ToLower() == "yes" || val.ToLower() == "true";
                    // Map selector: play every run on the map layout of a chosen
                    // base squad (Basic / Defence / Speedy / Gauntlet / ...).
                    // Generalizes "Gauntlet Map = yes" to any squad's maps.
                    else if (key == "maps" || key == "map source" || key == "squad maps")
                    {
                        string mv = val.Trim();
                        string mvl = mv.ToLowerInvariant();
                        if (mvl.Length == 0 || mvl == "default" || mvl == "(default)" || mvl == "no" || mvl == "none"
                            || mvl.StartsWith("(each squad")) MapSourceSquad = "";
                        else MapSourceSquad = mv;
                    }
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
        // Team equipment colors — AWAY variants (worn on the away jersey)
        else if (key == "gloves away color" || key == "gloves color away") team.TeamGlovesColorAway = ParseRandomColor(val);
        else if (key == "gloves away secondary color") team.TeamGlovesSecondaryAway = ParseRandomColor(val);
        else if (key == "gloves away tertiary color") team.TeamGlovesTertiaryAway = ParseRandomColor(val);
        else if (key == "helmet away color" || key == "helmet color away") team.TeamHelmetColorAway = ParseRandomColor(val);
        else if (key == "helmet away secondary color") team.TeamHelmetSecondaryAway = ParseRandomColor(val);
        else if (key == "helmet away tertiary color") team.TeamHelmetTertiaryAway = ParseRandomColor(val);
        else if (key == "pants away color" || key == "pants color away") team.TeamPantsColorAway = ParseRandomColor(val);
        else if (key == "pants away secondary color") team.TeamPantsSecondaryAway = ParseRandomColor(val);
        else if (key == "pants away tertiary color") team.TeamPantsTertiaryAway = ParseRandomColor(val);
        else if (key == "skates away color" || key == "skates color away") team.TeamSkatesColorAway = ParseRandomColor(val);
        else if (key == "blade away color") team.TeamBladeColorAway = ParseRandomColor(val);
        else if (key == "laces away color") team.TeamLacesColorAway = ParseRandomColor(val);
        else if (key == "bicep away color" || key == "bicep color away") team.TeamBicepColorAway = ParseRandomColor(val);
        else if (key == "socks away color" || key == "socks color away") team.TeamSocksColorAway = ParseRandomColor(val);
        else if (key == "socks away secondary color") team.TeamSocksSecondaryAway = ParseRandomColor(val);
        else if (key == "socks away tertiary color") team.TeamSocksTertiaryAway = ParseRandomColor(val);
        else if (key == "stick away color") team.TeamStickColorAway = ParseRandomColor(val);
        else if (key == "number away color") team.TeamNumberColorAway = ParseRandomColor(val);
        else if (key == "number away secondary color") team.TeamNumberSecondaryAway = ParseRandomColor(val);
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
        // Per-player AWAY color overrides (same naming convention as team-level)
        else if (key == "jersey away color") p.JerseyColorAway = ParseRandomColor(val);
        else if (key == "jersey away secondary color") p.JerseySecondaryColorAway = ParseRandomColor(val);
        else if (key == "jersey away accent color") p.JerseyAccentColorAway = ParseRandomColor(val);
        else if (key == "gloves away color") p.GlovesColorAway = ParseRandomColor(val);
        else if (key == "gloves away secondary color") p.GlovesSecondaryColorAway = ParseRandomColor(val);
        else if (key == "gloves away tertiary color") p.GlovesTertiaryColorAway = ParseRandomColor(val);
        else if (key == "helmet away color") p.HelmetColorAway = ParseRandomColor(val);
        else if (key == "helmet away secondary color") p.HelmetSecondaryColorAway = ParseRandomColor(val);
        else if (key == "helmet away tertiary color") p.HelmetTertiaryColorAway = ParseRandomColor(val);
        else if (key == "pants away color") p.PantsColorAway = ParseRandomColor(val);
        else if (key == "pants away secondary color") p.PantsSecondaryColorAway = ParseRandomColor(val);
        else if (key == "pants away tertiary color") p.PantsTertiaryColorAway = ParseRandomColor(val);
        else if (key == "skates away color") p.SkatesColorAway = ParseRandomColor(val);
        else if (key == "blade away color") p.BladeColorAway = ParseRandomColor(val);
        else if (key == "laces away color") p.LacesColorAway = ParseRandomColor(val);
        else if (key == "bicep away color") p.BicepColorAway = ParseRandomColor(val);
        else if (key == "number away color") p.NumberColorAway = ParseRandomColor(val);
        else if (key == "number away secondary color") p.NumberSecondaryColorAway = ParseRandomColor(val);
        else if (key == "socks away color") p.SocksColorAway = ParseRandomColor(val);
        else if (key == "socks away secondary color") p.SocksSecondaryColorAway = ParseRandomColor(val);
        else if (key == "socks away tertiary color") p.SocksTertiaryColorAway = ParseRandomColor(val);
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

        LoadNodeAssignments();
    }

    // ===== PER-NODE TEAM ASSIGNMENTS =====
    //
    // The original model keys teams to a linear game number: teams/ sorted by
    // filename, ConfigTeams[n] plays game n. That cannot express two different
    // opponents on one branch layer, and it cannot reuse a team on several nodes
    // ("every elite is the soccer players"), because a folder IS its position.
    //
    // assignments.txt keys a team to a NODE instead:
    //     Map 1 / Layer 2 / Node 0 = Meatballs
    // where Map is 1-based over the campaign's Act Sequence and Layer/Node are the
    // template's own layerIndex/nodeIndex, which _game_maps.txt lists. Every map
    // ships exactly ONE template (verified: 51 maps, 51 templates), so those
    // coordinates are stable across runs and a node is a real thing to point at.
    //
    // The value is a team KEY: a teams/ folder name, or a team's Name, or its
    // Import Team. Several nodes may name the same team.
    //
    // Absent file = empty dictionary = every code path below falls through to the
    // existing sequential behaviour, so campaigns that predate this stay identical.
    internal static Dictionary<string, string> NodeAssignments =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    internal static string NodeKey(int mapPos, int layer, int node) => $"{mapPos}|{layer}|{node}";

    internal static void LoadNodeAssignments()
    {
        NodeAssignments.Clear();
        try
        {
            string path = Path.Combine(ModFolder, "assignments.txt");
            if (!File.Exists(path)) return;

            int parsed = 0, bad = 0;
            foreach (var raw in File.ReadAllLines(path))
            {
                string line = (raw ?? "").Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;

                int eq = line.IndexOf('=');
                if (eq <= 0) { bad++; continue; }
                string lhs = line.Substring(0, eq).Trim();
                string team = line.Substring(eq + 1).Trim();
                if (team.Length == 0) continue;   // blank value = "leave this node alone"

                // "Map 1 / Layer 2 / Node 0" — tolerate any separator run and the
                // words being cased however the GUI happens to write them.
                int mapPos = -1, layer = -1, node = -1;
                foreach (var partRaw in lhs.Split('/'))
                {
                    var part = partRaw.Trim();
                    if (part.Length == 0) continue;
                    var bits = part.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (bits.Length < 2) continue;
                    if (!int.TryParse(bits[bits.Length - 1], out int num)) continue;
                    string word = bits[0].ToLowerInvariant();
                    if (word.StartsWith("map")) mapPos = num;
                    else if (word.StartsWith("layer") || word == "l") layer = num;
                    else if (word.StartsWith("node") || word == "n") node = num;
                }

                if (mapPos < 1 || layer < 0 || node < 0)
                {
                    bad++;
                    Log.LogWarning($"[Assign] Could not read node coordinates from '{lhs}' — expected 'Map 1 / Layer 2 / Node 0'");
                    continue;
                }

                NodeAssignments[NodeKey(mapPos, layer, node)] = team;
                parsed++;
            }

            Log.LogInfo($"[Assign] assignments.txt: {parsed} node assignment(s) loaded"
                + (bad > 0 ? $", {bad} unreadable line(s) ignored" : ""));
        }
        catch (Exception ex) { Log.LogWarning($"[Assign] assignments.txt: {ex.Message}"); }
    }

    /// <summary>Resolve an assignment's team key to a loaded TeamConfig. Matches a
    /// teams/ folder name first (that is what the Creator writes and it is unique),
    /// then a team's Name, then its Import Team. Returns null and warns if nothing
    /// matches, so a typo leaves the node vanilla instead of silently playing the
    /// wrong team.</summary>
    internal static TeamConfig FindConfigTeamByKey(string key)
    {
        if (string.IsNullOrEmpty(key) || ConfigTeams == null) return null;
        string want = key.Trim();

        if (ConfigTeamDirs != null)
            for (int i = 0; i < ConfigTeamDirs.Count && i < ConfigTeams.Count; i++)
                if (string.Equals(Path.GetFileName(ConfigTeamDirs[i]), want, StringComparison.OrdinalIgnoreCase))
                    return ConfigTeams[i];

        for (int i = 0; i < ConfigTeams.Count; i++)
            if (ConfigTeams[i] != null && !string.IsNullOrEmpty(ConfigTeams[i].Name)
                && string.Equals(ConfigTeams[i].Name.Trim(), want, StringComparison.OrdinalIgnoreCase))
                return ConfigTeams[i];

        for (int i = 0; i < ConfigTeams.Count; i++)
            if (ConfigTeams[i] != null && !string.IsNullOrEmpty(ConfigTeams[i].ImportTeam)
                && string.Equals(ConfigTeams[i].ImportTeam.Trim(), want, StringComparison.OrdinalIgnoreCase))
                return ConfigTeams[i];

        return null;
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

            if (lower == "free_agents" || lower == "free agents")
            {
                if (!loadDraft) continue;
                FreeAgentPoolList.Clear();
                var faFiles = Directory.GetFiles(subDir, "*.txt");
                Array.Sort(faFiles);
                foreach (var file in faFiles)
                {
                    string playerName = Path.GetFileNameWithoutExtension(file);
                    var pc = new PlayerConfig();
                    LoadPlayerFile(file, pc);
                    FreeAgentPoolList.Add(pc);
                    Log.LogInfo($"  Free agent pool: '{playerName}' loaded");
                }
                continue;
            }

            if (lower == "superstars" || lower == "superstar")
            {
                if (!loadDraft) continue;
                SuperstarPoolList.Clear();
                var ssFiles = Directory.GetFiles(subDir, "*.txt");
                Array.Sort(ssFiles);
                foreach (var file in ssFiles)
                {
                    string playerName = Path.GetFileNameWithoutExtension(file);
                    var pc = new PlayerConfig();
                    LoadPlayerFile(file, pc);
                    SuperstarPoolList.Add(pc);
                    Log.LogInfo($"  Superstar pool: '{playerName}' loaded");
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
        Log.LogInfo($"[PlayerTeam] Loaded: {PlayerTeamConfigs.Count} teams, {DraftPoolConfigs.Count} bench players, {FreeAgentPoolList.Count} free agents, {SuperstarPoolList.Count} superstars");
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

    /// <summary>Is the run the player is on using the General Manager squad?
    /// Read from RunData.squadId, which is the squad actually picked.</summary>
    internal static void RefreshGmSquadActive()
    {
        bool was = GmSquadActive;
        GmSquadActive = false;
        try
        {
            if (string.IsNullOrEmpty(GmSquadId)) return;
            var runManager = UnityEngine.Object.FindObjectOfType<RunManager>();
            string squadId = runManager?.CampaignState?.runData?.squadId;
            if (string.IsNullOrEmpty(squadId)) return;
            GmSquadActive = string.Equals(squadId, GmSquadId, StringComparison.OrdinalIgnoreCase);
            if (GmSquadActive != was)
                Log.LogInfo($"[GmSquad] Run squad is '{squadId}' — GM node placement {(GmSquadActive ? "ON" : "off")}");
        }
        catch (Exception ex) { Log.LogWarning($"[GmSquad] RefreshGmSquadActive: {ex.Message}"); }
    }

    /// <summary>Work out which layers of the map about to be generated must carry a
    /// GM node, by reading the GM squad's own layout for this map position.</summary>
    internal static void ComputeGmForcedLayers()
    {
        GmForcedLayers.Clear();
        GmLayersDone.Clear();
        GmOverrideLayer = -1;
        if (!GmSquadActive) return;

        try
        {
            // Map 1 must open with a GM node so the roster can be filled before the
            // first game — that's the whole point of the squad, and it has to hold
            // whichever act the campaign actually starts on.
            bool firstMap = ActsCompleted <= 0;
            if (firstMap) GmOverrideLayer = 0;

            if (GmSquadMaps == null || GmSquadMaps.Count == 0)
            {
                if (firstMap)
                {
                    GmForcedLayers[0] = GmDefaultSelectionCount;
                    Log.LogWarning($"[GmSquad] No captured GM maps — forcing a GM node on layer 0 of map 1"
                        + $" with the fallback selection count ({GmDefaultSelectionCount}).");
                }
                return;
            }

            int idx = ActsCompleted;
            if (idx < 0) idx = 0;
            if (idx >= GmSquadMaps.Count) idx = GmSquadMaps.Count - 1;
            var cfg = GmSquadMaps[idx];
            var templates = cfg != null ? cfg.mapTemplates : null;
            if (templates == null || templates.Count == 0) return;

            // Several templates per map are variants of the same layout; take the
            // first so the result is deterministic rather than a union of all.
            var tmpl = templates[0];
            var layers = tmpl != null ? tmpl.layers : null;
            if (layers == null) return;

            for (int li = 0; li < layers.Count; li++)
            {
                var layer = layers[li];
                var nodes = layer != null ? layer.nodes : null;
                if (nodes == null) continue;
                for (int ni = 0; ni < nodes.Count; ni++)
                {
                    var nd = nodes[ni];
                    if (nd == null) continue;
                    // NodeData.type is DAGG.Core.NodeType, a DIFFERENT enum from
                    // STS.Map.NodeType — and they are not interchangeable:
                    // GeneralManager is 8 in STS.Map but 9 in DAGG.Core, with Coach
                    // holding the other value. Casting between them by int would
                    // quietly turn Coach nodes into GM nodes. Compare in the enum
                    // the field actually belongs to.
                    if (nd.type == DAGG.Core.NodeType.GeneralManager)
                    {
                        // Carry the squad's own selection count across, not just the
                        // node type — that count is what decides how many players
                        // the node lets you sign.
                        GmForcedLayers[layer.layerIndex] = nd.gmSelectionCount;
                        break;
                    }
                }
            }

            // Guarantee the opening node on the first map, using the count from the
            // GM squad's own first GM node rather than a made-up number.
            if (firstMap && !GmForcedLayers.ContainsKey(0))
            {
                int count = GmDefaultSelectionCount;
                foreach (var kv in GmForcedLayers) { count = kv.Value; break; }
                if (count <= 0) count = GmDefaultSelectionCount;
                GmForcedLayers[0] = count;
                Log.LogInfo($"[GmSquad] Map 1 has no GM node on layer 0 in the squad's own layout — adding one (selection count {count}).");
            }

            var sb = new StringBuilder();
            foreach (var kv in GmForcedLayers)
                sb.Append(sb.Length == 0 ? "" : ", ").Append($"L{kv.Key}(pick {kv.Value})");
            Log.LogInfo($"[GmSquad] Map {ActsCompleted + 1}: GM nodes required on [{sb}]"
                + $" (from GM squad map {idx + 1} of {GmSquadMaps.Count}, {templates.Count} template(s))");
        }
        catch (Exception ex) { Log.LogWarning($"[GmSquad] ComputeGmForcedLayers: {ex.Message}"); }
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
        if (lower == "crusaders lancelov" || lower == "crusaders_lancelov") return "Body/Crusaders/Lancelov";
        if (lower == "crusaders prince" || lower == "crusaders_prince") return "Body/Crusaders/Prince";
        if (lower == "crusaders guretski" || lower == "crusaders_guretski") return "Body/Crusaders/Guretski";
        if (lower == "crusaders galahad" || lower == "crusaders_galahad") return "Body/Crusaders/Galahad";

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
        if (lower == "curve") return "Sticks/Curve";
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
                "Faces/Anyteam/Bench_Buttface", "Faces/Anyteam/Bench_Stumple",
                "Faces/Anyteam/Bench_Stumple_Helmet", "Faces/Anyteam/Bench_Buttface_Angus",
                "Faces/Anyteam/Bench_Buttface_Rambo", "Faces/Anyteam/Referee_Old",
                "Faces/Figure_Skaters/Figure_Skater_Vanilla", "Faces/Figure_Skaters/FigureSkaterbig",
                "Faces/Figure_Skaters/FigureSkatersmall",
                "Faces/Angus_Events/Angus_Chad", "Faces/Angus_Events/Angus_Speed",
                "Faces/Angus_Events/Angus_Trio", "Faces/Angus_Events/Angus_Bald",
                "Faces/Knights/Lancelov_Helmless", "Faces/Knights/Red_Knight_Helmetless",
                "Faces/Spark"
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
            if (lower == "mid cheese" || lower == "mid_cheese" || lower == "cheese") return "Body/Mid_Cheese";
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

    // GamesPlayed at the moment the CURRENT map was generated. Config teams are
    // assigned to map nodes by match depth (see CampaignOpponents), and that
    // assignment has to stay stable while the player walks the map — so it is
    // anchored here rather than to the live GamesPlayed, which moves under it.
    // Persisted so a quit/reload mid-map (LoadMap, not GenerateNewMap) doesn't
    // silently re-anchor every node to 0.
    internal static int MapStartGamesPlayed = 0;

    // Diagnostic only: which kind of match node the player last interacted with,
    // so the [RewardPool] lines can be read per node type. The user believes the
    // reward pool differs by node, and GetRandomRelics IS category-scoped, so the
    // "61 relics" figure recorded in SESSION_HANDOFF.md is probably per-call.
    // Nothing branches on this — it is a log label.
    internal static string LastMatchNodeKind = "?";

    internal static void SaveProgress()
    {
        try
        {
            if (!Directory.Exists(ModFolder))
                Directory.CreateDirectory(ModFolder);
            File.WriteAllText(SavePath, $"{ActsCompleted},{GamesPlayed},{MapStartGamesPlayed}");
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
                // 3rd field is newer than the 2.1.31 release. Saves written by
                // older builds have only two — fall back to GamesPlayed, which is
                // right for the common "quit on the map right after a match" case.
                if (parts.Length >= 3 && int.TryParse(parts[2], out int ms))
                    MapStartGamesPlayed = ms;
                else
                    MapStartGamesPlayed = GamesPlayed;
                if (MapStartGamesPlayed > GamesPlayed || MapStartGamesPlayed < 0)
                    MapStartGamesPlayed = GamesPlayed;
                Log.LogInfo($"[Campaign] Loaded progress: ActsCompleted={ActsCompleted}, GamesPlayed={GamesPlayed}, MapStartGamesPlayed={MapStartGamesPlayed}");

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
                    MapStartGamesPlayed = 0;
                    DraftPoolApplied = false;
        AppliedDraftPtrs.Clear();
        AppliedFreeAgentPtrs.Clear();
        FreeAgentSignedConfigs.Clear();
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

        // Default mode = play vanilla base game. Skip every Harmony patch that
        // CHANGES anything — team remixes, challenge-node replacement, save
        // tracking and so on all stay off — with one exception: the read-only data
        // dumps still run.
        //
        // Those dumps (team/player/logo/skin lists and the team library) describe
        // the GAME, not the campaign. The Creator needs them to populate its
        // dropdowns, and refusing to write them while default is selected meant a
        // user with no active campaign could never generate the lists the Creator
        // needs to build one — a chicken-and-egg the mod put in its own way.
        // Dumping mutates no game state, so vanilla play is still vanilla.
        if (IsDefaultMode)
        {
            Log.LogInfo("[Campaign] DEFAULT MODE active — skipping gameplay patches. Game runs 100% vanilla.");
            var dumpOnly = new Harmony("com.mods.customcampaign.dumponly");
            try
            {
                var refreshCampaign = AccessTools.Method(typeof(TitleScreen), "RefreshCampaignData");
                if (refreshCampaign != null)
                {
                    dumpOnly.Patch(refreshCampaign,
                        postfix: new HarmonyMethod(typeof(PatchDefaultModeDump), nameof(PatchDefaultModeDump.Postfix)));
                    Log.LogInfo("[Campaign] DEFAULT MODE: data dumps still active (TitleScreen.RefreshCampaignData)");
                }
                else Log.LogWarning("[Campaign] DEFAULT MODE: TitleScreen.RefreshCampaignData not found — no dumps this run");
            }
            catch (Exception ex) { Log.LogError($"[Campaign] DEFAULT MODE dump hook: {ex}"); }
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

            // Backup: patch CreateMapNode too since IL2CPP sometimes inlines
            // short methods and the GetBlueprint hook silently no-ops.
            var createMapNode = AccessTools.Method(typeof(STS.Map.MapObject), "CreateMapNode");
            if (createMapNode != null)
            {
                harmony.Patch(createMapNode,
                    prefix: new HarmonyMethod(typeof(PatchCreateMapNodeFACap), nameof(PatchCreateMapNodeFACap.Prefix)));
                Log.LogInfo("Patched MapObject.CreateMapNode — FA node cap (backup path)");
            }
            else
            {
                Log.LogWarning("Could not find MapObject.CreateMapNode");
            }

            // Third line of defense: post-InitializeMap sweep.
            var initMap = AccessTools.Method(typeof(STS.Map.MapObject), "InitializeMap");
            if (initMap != null)
            {
                harmony.Patch(initMap,
                    postfix: new HarmonyMethod(typeof(PatchInitializeMapPost), nameof(PatchInitializeMapPost.Postfix)));
                Log.LogInfo("Patched MapObject.InitializeMap — FA node post-sweep");

                // Backup hook for the opponent pass below, in case SetOpponents is
                // inlined. Registered after the FA sweep so challenge->elite and
                // FA->training rewrites have settled before we classify nodes.
                harmony.Patch(initMap,
                    postfix: new HarmonyMethod(typeof(PatchMapOpponents), nameof(PatchMapOpponents.Postfix)));
                Log.LogInfo("Patched MapObject.InitializeMap — campaign opponents (backup hook)");
            }
            else
            {
                Log.LogWarning("Could not find MapObject.InitializeMap");
            }

            // Put the mascot skeleton back for the post-match explosion, which is
            // drawn by the very skeleton we hide to get rid of the mascot.
            try
            {
                var playExplosion = AccessTools.Method(typeof(STS.Map.MatchMapNode), "PlayExplosionAnim");
                if (playExplosion != null)
                {
                    harmony.Patch(playExplosion,
                        prefix: new HarmonyMethod(typeof(PatchPlayExplosionAnim), nameof(PatchPlayExplosionAnim.Prefix)));
                    Log.LogInfo("Patched MatchMapNode.PlayExplosionAnim — explosion still plays with the mascot hidden");
                }
                else Log.LogWarning("Could not find MatchMapNode.PlayExplosionAnim — post-match explosion may not render");
            }
            catch (Exception ex) { Log.LogError($"Failed PlayExplosionAnim patch: {ex}"); }

            // Late node probe — runs once the map is live, unlike the map-generation
            // pass which sees a half-built node.
            try
            {
                var refreshStates = AccessTools.Method(typeof(STS.Map.MapObject), "RefreshNodeStates");
                if (refreshStates != null)
                {
                    harmony.Patch(refreshStates,
                        postfix: new HarmonyMethod(typeof(PatchLateNodeProbe), nameof(PatchLateNodeProbe.Postfix)));
                    Log.LogInfo("Patched MapObject.RefreshNodeStates — late node-art probe");
                }
                else Log.LogWarning("Could not find MapObject.RefreshNodeStates");
            }
            catch (Exception ex) { Log.LogError($"Failed late node probe patch: {ex}"); }

            // Swap the per-team stadium animation for a neutral one, so the vanilla
            // mascot never appears next to the custom team's logo. Must be a prefix
            // on SetElite/SetBoss — the animation name is an argument to them.
            try
            {
                var setElite = AccessTools.Method(typeof(EliteMapNode), nameof(EliteMapNode.SetElite));
                if (setElite != null)
                {
                    harmony.Patch(setElite,
                        prefix: new HarmonyMethod(typeof(PatchNodeStadiumAnimation), nameof(PatchNodeStadiumAnimation.ElitePrefix)));
                    Log.LogInfo("Patched EliteMapNode.SetElite — neutral node stadium (no vanilla mascot)");
                }
                else Log.LogWarning("Could not find EliteMapNode.SetElite — elite nodes will keep the vanilla mascot");

                var setBoss = AccessTools.Method(typeof(BossMapNode), nameof(BossMapNode.SetBoss));
                if (setBoss != null)
                {
                    harmony.Patch(setBoss,
                        prefix: new HarmonyMethod(typeof(PatchNodeStadiumAnimation), nameof(PatchNodeStadiumAnimation.BossPrefix)));
                    Log.LogInfo("Patched BossMapNode.SetBoss — neutral node stadium (no vanilla mascot)");
                }
                else Log.LogWarning("Could not find BossMapNode.SetBoss — boss nodes will keep the vanilla mascot");
            }
            catch (Exception ex) { Log.LogError($"Failed node stadium animation patch: {ex}"); }

            // Primary hook for assigning campaign teams to map nodes. SetOpponents
            // is where the game fills MatchMapNode.opponent for every elite and
            // boss node, so a postfix here is guaranteed to see a fully populated
            // map — unlike InitializeMap, whose internals we can't verify from the
            // signatures-only dump.
            var setOpponents = AccessTools.Method(typeof(STS.Map.MapObject), "SetOpponents");
            if (setOpponents != null)
            {
                harmony.Patch(setOpponents,
                    postfix: new HarmonyMethod(typeof(PatchMapOpponents), nameof(PatchMapOpponents.AfterSetOpponents)));
                Log.LogInfo("Patched MapObject.SetOpponents — campaign opponents assigned at map generation");
            }
            else
            {
                Log.LogWarning("Could not find MapObject.SetOpponents — falling back to the InitializeMap hook for map teams");
            }
        }
        catch (Exception ex) { Log.LogError($"Failed MapObject FA patches: {ex}"); }

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
                    postfix:   new HarmonyMethod(typeof(PatchPreGenerateFreeAgents), nameof(PatchPreGenerateFreeAgents.Postfix)),
                    finalizer: new HarmonyMethod(typeof(PatchPreGenerateFreeAgents), nameof(PatchPreGenerateFreeAgents.Finalizer)));
                Log.LogInfo("Patched CampaignState.PreGenerateFreeAgents — draft mods visible on pick screen!");
            }
        }
        catch (Exception ex) { Log.LogError($"Failed CampaignState.PreGenerateFreeAgents: {ex}"); }

        // Replace the superstars offered on the "pick a superstar" screen
        // (OldSquadMenu) with the user's player_teams/superstars/ pool.
        try
        {
            var genSuper = AccessTools.Method(typeof(Tape2Tape.Hockey.UI.OldSquadMenu), "GenerateSuperStarSkaters");
            if (genSuper != null)
            {
                harmony.Patch(genSuper,
                    postfix: new HarmonyMethod(typeof(PatchSuperstarPool), nameof(PatchSuperstarPool.Postfix)));
                Log.LogInfo("Patched OldSquadMenu.GenerateSuperStarSkaters — custom superstar pool");
            }
            else
                Log.LogWarning("[Superstar] OldSquadMenu.GenerateSuperStarSkaters not found");
        }
        catch (Exception ex) { Log.LogError($"Failed OldSquadMenu.GenerateSuperStarSkaters patch: {ex}"); }

        // MINIMUM hooks to make picks land on the custom squad's run team.
        // Without these, the game's own flow drops drafted line picks AND the
        // chosen superstar for Custom_* squads (the run-start path is gated on
        // vanilla SquadIds and doesn't merge OldSquadMenu picks into the
        // freshly-instantiated run team). NOTHING is moved/reshuffled — drafts
        // land in the line slot the player picked them for; superstar appends
        // to the bench (end of fwds).
        try
        {
            var clickSuper = AccessTools.Method(typeof(Tape2Tape.Hockey.UI.OldSquadMenu), "OnClickSuperStar");
            if (clickSuper != null)
            {
                harmony.Patch(clickSuper,
                    postfix: new HarmonyMethod(typeof(PatchOldSquadMenuSuperstar), nameof(PatchOldSquadMenuSuperstar.OnClickSuperStarPostfix)));
                Log.LogInfo("Patched OldSquadMenu.OnClickSuperStar — capture picked superstar");
            }
            else
                Log.LogWarning("[Superstar] OldSquadMenu.OnClickSuperStar NOT FOUND — picked superstar will NOT be captured");

            var instStartTeam = AccessTools.Method(typeof(State.CampaignState), "InstantiateStartingTeam");
            if (instStartTeam != null)
            {
                harmony.Patch(instStartTeam,
                    postfix: new HarmonyMethod(typeof(PatchInstantiateStartingTeam), nameof(PatchInstantiateStartingTeam.Postfix)));
                Log.LogInfo("Patched CampaignState.InstantiateStartingTeam — reconcile drafts + append superstar at run start");
            }
            else
                Log.LogWarning("[InstStartTeam] CampaignState.InstantiateStartingTeam NOT FOUND — drafts/superstar will NOT be reconciled at run start");

            // NOTE: We do NOT patch TeamData.IsCreationLineUpPositionLocked.
            // That accessor is on an extremely hot native path (called for every
            // UI refresh / skater card); reading __instance.teamName inside the
            // postfix caused a fatal AccessViolationException on superstar pick.
            // The line-position locks are instead cleared by writing the private
            // bool fields directly on the run TeamData (UnlockAllLinePositions),
            // which doesn't touch the accessor at runtime.
        }
        catch (Exception ex) { Log.LogError($"Failed minimum-hook patches: {ex}"); }

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
                        prefix:    new HarmonyMethod(typeof(PatchFilterTalentRewards), nameof(PatchFilterTalentRewards.Prefix)),
                        postfix:   new HarmonyMethod(typeof(PatchFilterTalentRewards), nameof(PatchFilterTalentRewards.Postfix)),
                        finalizer: new HarmonyMethod(typeof(PatchFilterTalentRewards), nameof(PatchFilterTalentRewards.Finalizer)));
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
            // GetSquadTeamData is called by SetupMetas for every squad to determine
            // unlock state. Patch it first so SetupMetas sees Custom_* as unlocked.
            var getSquadTeamData = AccessTools.Method(typeof(ProfileData), "GetSquadTeamData", new Type[] { typeof(string) });
            if (getSquadTeamData != null)
            {
                harmony.Patch(getSquadTeamData,
                    postfix: new HarmonyMethod(typeof(PatchGetSquadTeamData), nameof(PatchGetSquadTeamData.Postfix)));
                Log.LogInfo("Patched ProfileData.GetSquadTeamData — Custom_* always Unlocked=true");
            }
            else
                Log.LogWarning("[CustomSquad] ProfileData.GetSquadTeamData not found");

            var setupMetas = AccessTools.Method(typeof(Tape2Tape.Hockey.UI.ChooseMetaMenu), "SetupMetas");
            if (setupMetas != null)
            {
                harmony.Patch(setupMetas,
                    prefix: new HarmonyMethod(typeof(PatchChooseMetaUI), nameof(PatchChooseMetaUI.PrefixMenu)),
                    postfix: new HarmonyMethod(typeof(PatchChooseMetaUI), nameof(PatchChooseMetaUI.PostfixMenu)));
                Log.LogInfo("Patched ChooseMetaMenu.SetupMetas — custom squads appear in menu!");
            }
            else
            {
                Log.LogWarning("Could not find ChooseMetaMenu.SetupMetas");
            }

            // Jul-2026 update: SetupMetas only builds a tile when
            // SquadIds.IsAvailableSquad(id) passes (hardcoded whitelist).
            // Whitelist Custom_* ids or appended squads never render.
            var isAvail = AccessTools.Method(typeof(State.SquadIds), "IsAvailableSquad", new Type[] { typeof(string) });
            if (isAvail != null)
            {
                harmony.Patch(isAvail,
                    postfix: new HarmonyMethod(typeof(PatchSquadIds), nameof(PatchSquadIds.Postfix)));
                Log.LogInfo("Patched SquadIds.IsAvailableSquad — Custom_* squads pass the availability whitelist");
            }
            else
                Log.LogWarning("[CustomSquad] SquadIds.IsAvailableSquad not found — custom squad tiles will not appear");

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
            // Patch get_IsUnlocked so Custom_* squads always read as unlocked
            // regardless of ProfileData.unlockedSquads state at patch time.
            var isUnlockedGetter = AccessTools.Method(typeof(RunSquadScriptableObject), "get_IsUnlocked");
            if (isUnlockedGetter != null)
                harmony.Patch(isUnlockedGetter,
                    postfix: new HarmonyMethod(typeof(PatchSquadLocalization), nameof(PatchSquadLocalization.IsUnlockedPostfix)));
            else
                Log.LogWarning("[CustomSquad] get_IsUnlocked not found — patching MetaTeamItem.Refresh instead");

            // MetaTeamItem.Refresh(RunSquadScriptableObject squad, SquadTeamData savedData)
            // After Refresh runs it sets MetaTeamItem.IsUnlocked based on unlockCondition.IsMet().
            // Our cloned squads have no valid unlock condition so IsUnlocked ends up false → greyed tile.
            // Postfix: force IsUnlocked=true for Custom_* squads after Refresh sets it.
            var metaRefresh = AccessTools.Method(typeof(Tape2Tape.Hockey.UI.MetaTeamItem), "Refresh");
            if (metaRefresh != null)
            {
                harmony.Patch(metaRefresh,
                    postfix: new HarmonyMethod(typeof(PatchMetaTeamItemRefresh), nameof(PatchMetaTeamItemRefresh.Postfix)));
                Log.LogInfo("Patched MetaTeamItem.Refresh — custom squads will be selectable");
            }
            else
                Log.LogWarning("[CustomSquad] MetaTeamItem.Refresh not found");

            // Start() fires after Refresh in Unity lifecycle and may reset IsUnlocked.
            var metaStart = AccessTools.Method(typeof(Tape2Tape.Hockey.UI.MetaTeamItem), "Start");
            if (metaStart != null)
            {
                harmony.Patch(metaStart,
                    postfix: new HarmonyMethod(typeof(PatchMetaTeamItemRefresh), nameof(PatchMetaTeamItemRefresh.StartPostfix)));
                Log.LogInfo("Patched MetaTeamItem.Start — forces unlock state after Unity lifecycle");
            }
            else
                Log.LogWarning("[CustomSquad] MetaTeamItem.Start not found");

            // Patch UnlockSystem.IsUnlocked so Custom_* ids always pass the
            // selectability gate in ChooseMetaMenu.IsValidMetaSelection.
            try
            {
                var unlockSysType = AccessTools.TypeByName("Unlocks.UnlockSystem");
                if (unlockSysType != null)
                {
                    var isUnlockedMethod = AccessTools.Method(unlockSysType, "IsUnlocked", new Type[] { typeof(string) });
                    if (isUnlockedMethod != null)
                    {
                        harmony.Patch(isUnlockedMethod,
                            postfix: new HarmonyMethod(typeof(PatchUnlockSystem), nameof(PatchUnlockSystem.Postfix)));
                        Log.LogInfo("Patched UnlockSystem.IsUnlocked — Custom_* squads always unlock-gated true");
                    }
                    else
                        Log.LogWarning("[CustomSquad] UnlockSystem.IsUnlocked(string) not found");
                }
                else
                    Log.LogWarning("[CustomSquad] Unlocks.UnlockSystem type not found");
            }
            catch (Exception uex) { Log.LogError($"Failed UnlockSystem.IsUnlocked patch: {uex}"); }

            // Patch IsValidMetaSelection directly — whatever check it does
            // (profile data, unlock condition, etc.) force true for Custom_*.
            try
            {
                var isValidMeta = AccessTools.Method(typeof(Tape2Tape.Hockey.UI.ChooseMetaMenu), "IsValidMetaSelection");
                if (isValidMeta != null)
                {
                    harmony.Patch(isValidMeta,
                        postfix: new HarmonyMethod(typeof(PatchIsValidMetaSelection), nameof(PatchIsValidMetaSelection.Postfix)));
                    Log.LogInfo("Patched ChooseMetaMenu.IsValidMetaSelection — Custom_* squads always valid");
                }
                else
                    Log.LogWarning("[CustomSquad] ChooseMetaMenu.IsValidMetaSelection not found");
            }
            catch (Exception ivex) { Log.LogError($"Failed IsValidMetaSelection patch: {ivex}"); }

            // Diagnostic: trace OnMetaClicked and ConfirmSelections to see what fires on click.
            try
            {
                var onMetaClicked = AccessTools.Method(typeof(Tape2Tape.Hockey.UI.ChooseMetaMenu), "OnMetaClicked");
                if (onMetaClicked != null)
                {
                    harmony.Patch(onMetaClicked,
                        prefix: new HarmonyMethod(typeof(PatchChooseMetaDiag), nameof(PatchChooseMetaDiag.OnMetaClickedPrefix)));
                    Log.LogInfo("Patched ChooseMetaMenu.OnMetaClicked (diagnostic)");
                }
                var confirmSel = AccessTools.Method(typeof(Tape2Tape.Hockey.UI.ChooseMetaMenu), "ConfirmSelections");
                if (confirmSel != null)
                {
                    harmony.Patch(confirmSel,
                        prefix: new HarmonyMethod(typeof(PatchChooseMetaDiag), nameof(PatchChooseMetaDiag.ConfirmPrefix)));
                    Log.LogInfo("Patched ChooseMetaMenu.ConfirmSelections (diagnostic)");
                }
                var canGoNext = AccessTools.Method(typeof(Tape2Tape.Hockey.UI.ChooseMetaMenu), "CanGoToNextState");
                if (canGoNext != null)
                {
                    harmony.Patch(canGoNext,
                        postfix: new HarmonyMethod(typeof(PatchChooseMetaDiag), nameof(PatchChooseMetaDiag.CanGoNextPostfix)));
                    Log.LogInfo("Patched ChooseMetaMenu.CanGoToNextState (diagnostic)");
                }
            }
            catch (Exception diagex) { Log.LogError($"Failed diagnostic patches: {diagex}"); }

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

        // Pre-match preview screen shows the vanilla opponent (the campaign team
        // is only applied at LaunchMatch), so correct the name + logo there.
        try
        {
            var showMenu = AccessTools.Method(typeof(MatchPreviewMenu), "ShowMenu");
            if (showMenu != null)
            {
                harmony.Patch(showMenu,
                    postfix: new HarmonyMethod(typeof(PatchMatchPreviewMenu), nameof(PatchMatchPreviewMenu.Postfix)));
                Log.LogInfo("Patched MatchPreviewMenu.ShowMenu — preview shows the custom opponent");
            }
            else Log.LogWarning("MatchPreviewMenu.ShowMenu not found — preview will show the vanilla opponent");
        }
        catch (Exception ex) { Log.LogError($"Failed MatchPreviewMenu.ShowMenu: {ex}"); }

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
        Plugin.LastMatchNodeKind = !string.IsNullOrEmpty(il2TypeName) ? il2TypeName : sysTypeName;

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
                Plugin.MapStartGamesPlayed = 0;
                CampaignOpponents.ForgetAll("final boss beaten");
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
// Jul-2026 update (found by disassembling SetupMetas): every squad tile is
// now gated behind SquadIds.IsAvailableSquad(id) — a hardcoded whitelist
// (ONLY_ALLOW_AVAILABLE_SQUADS = true game-side). Custom_* ids can never be
// on it, so appended squads silently stopped getting tiles (the game also
// hides its own not-yet-released 'allangus' squad this way). Flip the check
// for our ids only.
public static class PatchSquadIds
{
    public static void Postfix(string id, ref bool __result)
    {
        if (!__result && id != null && id.StartsWith("Custom_"))
            __result = true;
    }
}

public static class PatchChooseMetaUI
{
    // The Choose Your Squad tile head is RunSquadScriptableObject.m_SmallTeamIcon
    // — a plain UI Sprite drawn by MetaTeamItem.m_KeyPlayerHead, NOT a Spine skin.
    // That means a runtime-created Sprite renders fine here (unlike jersey logos),
    // so a PNG is a valid source. Accepts either the name of a sprite the game
    // already has loaded, or any PNG in CustomLogos/. Returns null when neither
    // matches, so the caller can keep the template's icon.
    internal static UnityEngine.Sprite ResolveTileIcon(string value, string squadKey)
    {
        string leaf = (value ?? "").Trim();
        int slash = leaf.LastIndexOf('/');
        if (slash >= 0) leaf = leaf.Substring(slash + 1);
        if (leaf.Length == 0) return null;

        // 1) A face — the normal case. Faces live in the Spine atlas, so this
        //    has to go through FaceToSprite; they are NOT Unity Sprites.
        var face = FaceToSprite(leaf);
        if (face != null)
        {
            Plugin.Log.LogInfo($"[CustomSquad] Tile icon for '{squadKey}': face '{leaf}'");
            return face;
        }

        // 2) A sprite the game already has loaded (vanilla tile icons).
        var all = UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.Sprite>();
        if (all != null)
            foreach (var sp in all)
                if (sp != null && sp.name.Equals(leaf, StringComparison.OrdinalIgnoreCase))
                {
                    Plugin.Log.LogInfo($"[CustomSquad] Tile icon for '{squadKey}': game sprite '{sp.name}'");
                    return sp;
                }

        // 3) Last resort: a PNG in CustomLogos/, so custom artwork is possible.
        var png = PatchBossLaunchMatch.LoadCustomLogoSprite(leaf);
        if (png != null)
        {
            Plugin.Log.LogInfo($"[CustomSquad] Tile icon for '{squadKey}': CustomLogos PNG '{leaf}'");
            return png;
        }

        Plugin.Log.LogWarning($"[CustomSquad] Tile icon for '{squadKey}': '{value}' matched no atlas face,"
            + " no loaded sprite and no PNG in CustomLogos/ — tile keeps the template icon.");
        return null;
    }

    // A face is a Spine ATLAS REGION, not a Unity Sprite. That is why the 2.1.31
    // heuristic — scanning Resources for a Sprite named after the face — could
    // never match, no matter which face was chosen. Cut the region out of its
    // atlas page texture into a real Sprite the tile's UI Image can draw.
    private static readonly Dictionary<string, UnityEngine.Sprite> _faceIconCache =
        new Dictionary<string, UnityEngine.Sprite>(StringComparer.OrdinalIgnoreCase);

    // One-shot: what the loaded atlases actually contain. FindRegion() is an
    // exact-name match, so a miss can mean either "wrong name form" or "the
    // atlas holding faces isn't loaded on this screen" — these two lists tell
    // them apart instead of guessing again.
    private static bool _atlasDumped;
    private static void DumpAtlasRegions(
        Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppArrayBase<Spine.Unity.AtlasAssetBase> assets,
        string wanted)
    {
        if (_atlasDumped || assets == null) return;
        _atlasDumped = true;
        try
        {
            foreach (var a in assets)
            {
                if (a == null) continue;
                Spine.Atlas atlas = null;
                try { atlas = a.GetAtlas(false); } catch { }
                if (atlas == null) { Plugin.Log.LogInfo($"[AtlasDump] '{a.name}': no atlas"); continue; }

                var regions = atlas.Regions;
                int n = regions != null ? regions.Count : 0;
                var sample = new System.Text.StringBuilder();
                var hits = new System.Text.StringBuilder();
                for (int i = 0; i < n; i++)
                {
                    string rn = regions[i]?.name ?? "";
                    if (i < 10) sample.Append('\'').Append(rn).Append("' ");
                    if (rn.IndexOf(wanted, StringComparison.OrdinalIgnoreCase) >= 0)
                        hits.Append('\'').Append(rn).Append("' ");
                }
                Plugin.Log.LogInfo($"[AtlasDump] '{a.name}': {n} region(s) | first: {sample}"
                    + (hits.Length > 0 ? $"| MATCHING '{wanted}': {hits}" : $"| no region contains '{wanted}'"));
            }
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[AtlasDump] {ex.Message}"); }
    }

    // Cut a named ATLAS REGION out of its page texture into a Sprite.
    private static UnityEngine.Sprite RegionToSprite(string regionName, out string where)
    {
        where = "";
        if (string.IsNullOrEmpty(regionName)) return null;
        var assets = UnityEngine.Resources.FindObjectsOfTypeAll<Spine.Unity.AtlasAssetBase>();
        if (assets == null) return null;
        foreach (var a in assets)
        {
            if (a == null) continue;
            Spine.Atlas atlas = null;
            try { atlas = a.GetAtlas(false); } catch { continue; }
            if (atlas == null) continue;

            Spine.AtlasRegion region = null;
            try { region = atlas.FindRegion(regionName); } catch { }
            if (region == null || region.page == null) continue;

            UnityEngine.Texture2D tex = null;
            try
            {
                var mat = region.page.rendererObject?.TryCast<UnityEngine.Material>();
                if (mat != null && mat.mainTexture != null)
                    tex = mat.mainTexture.TryCast<UnityEngine.Texture2D>();
            }
            catch { }
            if (tex == null) continue;

            int w = region.packedWidth, h = region.packedHeight;
            // Spine packs from the top-left; Unity's Rect origin is bottom-left.
            float y = region.page.height - region.y - h;
            var sp = UnityEngine.Sprite.Create(tex,
                new UnityEngine.Rect(region.x, y, w, h),
                new UnityEngine.Vector2(0.5f, 0.5f), 100f);
            sp.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(sp);
            where = $"atlas '{a.name}' region '{regionName}' ({w}x{h})"
                  + (region.rotate ? " — ROTATED in atlas, icon may appear turned" : "");
            return sp;
        }
        return null;
    }

    internal static UnityEngine.Sprite FaceToSprite(string faceLeaf)
    {
        if (string.IsNullOrEmpty(faceLeaf)) return null;
        if (_faceIconCache.TryGetValue(faceLeaf, out var cached)) return cached;

        UnityEngine.Sprite made = null;
        try
        {
            // A face in the GUI ('Mathieu') is a Spine SKIN name; the atlas holds
            // the raw art under other names. Walking skin -> attachment to learn
            // the real region name is the obvious next step, and it is
            // DELIBERATELY NOT DONE HERE: Skin.Attachments is an Il2Cpp
            // ICollection of non-blittable SkinEntry structs, and reading the
            // boxed entries by IL2CPP reflection crashed the game outright —
            // 0xC0000005 access violation at squad-clone time, killing the whole
            // process. A direct region lookup is safe (it simply misses), so that
            // is all we do. Anything revisiting this must find a route that never
            // dereferences boxed Spine structs from managed code.
            string where;
            made = RegionToSprite(faceLeaf, out where);
            if (made != null)
                Plugin.Log.LogInfo($"[CustomSquad] Face '{faceLeaf}' resolved from {where}");
            else
                Plugin.Log.LogInfo($"[CustomSquad] Face '{faceLeaf}' has no atlas region of that name — tile keeps its icon");
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[CustomSquad] FaceToSprite '{faceLeaf}': {ex.Message}"); }

        _faceIconCache[faceLeaf] = made;   // cache misses too
        return made;
    }

    // The actual in-game "Choose Your Squad" screen (screenshot with locked
    // "???" tiles). Injects custom squads by replacing locked slots in
    // CampaignState.squads BEFORE the menu instantiates its grid items.
    public static void PrefixMenu(Tape2Tape.Hockey.UI.ChooseMetaMenu __instance)
    {
        // Always reload player_teams/ from disk so squads created after game
        // launch (via the creator GUI) are picked up without a restart.
        try
        {
            Plugin.PlayerTeamConfigs.Clear();
            Plugin.LoadPlayerTeamsFolders(loadTeams: Plugin.UsePlayerTeams, loadDraft: false);
        }
        catch (Exception reloadEx) { Plugin.Log.LogWarning($"[CustomSquad] reload: {reloadEx.Message}"); }

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

            // Gauntlet map: inject ALL game squads so the player isn't locked to
            // only the Gauntlet squad when starting a gauntlet campaign.
            if (Plugin.UseGauntletMap)
            {
                try
                {
                    var allSO = UnityEngine.Resources.FindObjectsOfTypeAll<RunSquadScriptableObject>();
                    if (allSO != null && allSO.Length > 0)
                    {
                        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        for (int i = 0; i < cs.squads.Count; i++)
                            if (cs.squads[i]?.squadName != null)
                                existing.Add(cs.squads[i].squadName);
                        int added = 0;
                        foreach (var sq in allSO)
                        {
                            if (sq == null || sq.squadName == null) continue;
                            if (!existing.Contains(sq.squadName))
                            {
                                cs.squads.Add(sq);
                                existing.Add(sq.squadName);
                                added++;
                            }
                        }
                        Plugin.Log.LogInfo($"[GauntletMap] Injected {added} additional squad(s) into gauntlet picker");
                    }
                }
                catch (Exception gex) { Plugin.Log.LogWarning($"[GauntletMap] squad inject: {gex.Message}"); }
            }

            // Map selector: force every squad to play on the chosen base squad's
            // map layout ("Maps = Speedy" etc. in campaign.txt). The game reads
            // each squad's own .maps list to pick the level, so we overwrite all
            // of them with the source squad's. "Gauntlet Map = yes" is the
            // legacy spelling of Maps = Gauntlet.
            // Capture the GM squad's OWN map layouts before the override below
            // replaces them. This is the only record of where its free-agent nodes
            // belong; once "Maps = X" has run, every squad points at the source
            // squad's list and the GM layout is gone. We hold the original List
            // reference — the override reassigns each squad's field rather than
            // mutating the list, so ours stays intact.
            try
            {
                var gmSquad = FindSquadByName(cs, "General Manager");
                if (gmSquad != null)
                {
                    Plugin.GmSquadMaps = gmSquad.maps;
                    Plugin.GmSquadId = gmSquad.id;
                    Plugin.Log.LogInfo($"[GmSquad] Captured '{gmSquad.squadName}' (id='{gmSquad.id}') map layouts:"
                        + $" {(gmSquad.maps != null ? gmSquad.maps.Count : 0)} map(s)");
                }
                else Plugin.Log.LogWarning("[GmSquad] General Manager squad not found — its free-agent nodes can't be restored");
            }
            catch (Exception gex) { Plugin.Log.LogWarning($"[GmSquad] capture: {gex.Message}"); }

            string mapSource = EffectiveMapSource();
            if (!string.IsNullOrEmpty(mapSource))
            {
                try
                {
                    var srcSquad = FindSquadByName(cs, mapSource);
                    if (srcSquad != null && srcSquad.maps != null)
                    {
                        int overrode = 0;
                        for (int gi = 0; gi < cs.squads.Count; gi++)
                        {
                            var sq = cs.squads[gi];
                            if (sq == null || sq == srcSquad) continue;
                            try { sq.maps = srcSquad.maps; overrode++; } catch { }
                        }
                        Plugin.Log.LogInfo($"[MapSource] Overrode .maps on {overrode} squad(s) → all picks use '{srcSquad.squadName}' maps ({srcSquad.maps.Count} map(s))");
                    }
                    else
                        Plugin.Log.LogWarning($"[MapSource] Squad '{mapSource}' not found — couldn't override squad maps");
                }
                catch (Exception gex) { Plugin.Log.LogWarning($"[MapSource] override: {gex.Message}"); }
            }
        }
        catch (Exception ex) { Plugin.Log.LogError($"[CustomSquad] PrefixMenu: {ex}"); }
    }

    // The squad whose maps every run should use: explicit "Maps = X" wins,
    // legacy "Gauntlet Map = yes" means gauntlet, empty = no override.
    internal static string EffectiveMapSource()
    {
        if (!string.IsNullOrEmpty(Plugin.MapSourceSquad)) return Plugin.MapSourceSquad;
        if (Plugin.UseGauntletMap) return "gauntlet";
        return "";
    }

    // Find a base squad by user-typed name. Tolerant: exact id → exact
    // squadName → space-stripped equality → contains, case-insensitive, with
    // common spelling aliases (Defence/Defense, General Manger typo). Searches
    // cs.squads first, then all loaded squad assets.
    internal static RunSquadScriptableObject FindSquadByName(State.CampaignState cs, string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        string Norm(string s) => (s ?? "").Replace(" ", "").Replace("_", "").ToLowerInvariant();
        string want = Norm(name);
        if (want == "defence") want = "defense";
        if (want == "generalmanger") want = "generalmanager";

        var pools = new List<RunSquadScriptableObject>();
        try { if (cs?.squads != null) for (int i = 0; i < cs.squads.Count; i++) if (cs.squads[i] != null) pools.Add(cs.squads[i]); } catch { }
        try
        {
            var allSO = UnityEngine.Resources.FindObjectsOfTypeAll<RunSquadScriptableObject>();
            if (allSO != null) foreach (var sq in allSO) if (sq != null) pools.Add(sq);
        }
        catch { }

        // Pass 1: exact (normalized) id or squadName match.
        foreach (var sq in pools)
        {
            string id = Norm(sq.id), nm = Norm(sq.squadName);
            if (nm == "defence") nm = "defense";
            if (id == want || nm == want) return sq;
        }
        // Pass 2: contains either way ("speedy" matches "Speedy Squad").
        foreach (var sq in pools)
        {
            string id = Norm(sq.id), nm = Norm(sq.squadName);
            if ((nm.Length > 0 && (nm.Contains(want) || want.Contains(nm)))
             || (id.Length > 0 && (id.Contains(want) || want.Contains(id)))) return sq;
        }
        return null;
    }

    // TEMP DIAGNOSTIC (remove once append is confirmed working). Runs after
    // SetupMetas has built the tile grid. The dump gives signatures only, not
    // SetupMetas's body — this is how we settle whether plain append produces a
    // fully-wired tile for our Custom_* squad: per tile we log its squad id,
    // unlock state, and navigation reachability (a tile with no neighbors / not
    // navigable can't be reached by input → its OnClick never fires).
    public static void PostfixMenu(Tape2Tape.Hockey.UI.ChooseMetaMenu __instance)
    {
        try
        {
            var t = __instance.GetIl2CppType();
            var bf = Il2CppSystem.Reflection.BindingFlags.NonPublic | Il2CppSystem.Reflection.BindingFlags.Instance | Il2CppSystem.Reflection.BindingFlags.Public;
            var miField = t.GetField("m_MetaItems", bf);
            if (miField == null) { Plugin.Log.LogWarning("[SquadDiag] m_MetaItems field not found"); return; }
            var miVal = miField.GetValue(__instance);
            var list = miVal?.TryCast<Il2CppSystem.Collections.Generic.List<Tape2Tape.Hockey.UI.MetaTeamItem>>();
            if (list == null) { Plugin.Log.LogWarning("[SquadDiag] m_MetaItems is null"); return; }
            Plugin.Log.LogInfo($"[SquadDiag] SetupMetas built {list.Count} tile(s):");
            for (int i = 0; i < list.Count; i++)
            {
                var mti = list[i];
                if (mti == null) { Plugin.Log.LogInfo($"[SquadDiag]   [{i}] <null MetaTeamItem>"); continue; }
                var squad = mti.CurrentSquad;
                string id = squad?.id ?? "null";
                var nav = mti.Navigable;
                bool navNull = nav == null;
                bool isNav = !navNull && nav.IsNavigable;
                bool canClick = !navNull && nav.CanBeClicked;
                int neigh = -1;
                try { if (!navNull) { var nbrs = nav.Neighbors; if (nbrs != null) neigh = nbrs.Length; } } catch { }
                Plugin.Log.LogInfo($"[SquadDiag]   [{i}] id='{id}' unlocked={mti.IsUnlocked} nav={(navNull ? "NULL" : "ok")} isNavigable={isNav} canBeClicked={canClick} neighbors={neigh}");
            }

            // Jul-2026 update: tiles no longer 1:1 with cs.squads. Dump both
            // candidate sources so we can diff which list SetupMetas filters
            // by (compare against the tile ids above).
            try
            {
                var csField = t.GetField("m_CampaignState", bf);
                var cs = csField?.GetValue(__instance)?.TryCast<State.CampaignState>();
                if (cs?.squads != null)
                {
                    var sb = new System.Text.StringBuilder($"[SquadDiag] cs.squads ({cs.squads.Count}): ");
                    for (int i = 0; i < cs.squads.Count; i++)
                        sb.Append('\'').Append(cs.squads[i]?.id ?? "null").Append("' ");
                    Plugin.Log.LogInfo(sb.ToString());
                }
                else Plugin.Log.LogInfo("[SquadDiag] m_CampaignState/.squads not readable via reflection");

                var prof = ProfileData.Instance;
                var ul = prof?.unlockedSquads;
                if (ul != null)
                {
                    var sb2 = new System.Text.StringBuilder($"[SquadDiag] unlockedSquads ({ul.Count}): ");
                    for (int i = 0; i < ul.Count; i++)
                        sb2.Append('\'').Append(ul[i]).Append("' ");
                    Plugin.Log.LogInfo(sb2.ToString());
                }
            }
            catch (Exception dx) { Plugin.Log.LogWarning($"[SquadDiag] source diff: {dx.Message}"); }
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[SquadDiag] PostfixMenu: {ex.Message}"); }
    }

    internal static void InjectCustomSquads(State.CampaignState cs)
    {
        var squads = cs.squads;
        // If cs.squads is empty (e.g. fresh custom campaign), bootstrap from all
        // RunSquadScriptableObjects in game assets so we have a template to clone.
        if (squads == null || squads.Count == 0)
        {
            Plugin.Log.LogInfo("[CustomSquad] cs.squads is null/empty — bootstrapping from game assets");
            try
            {
                var allSO = UnityEngine.Resources.FindObjectsOfTypeAll<RunSquadScriptableObject>();
                if (allSO != null && allSO.Length > 0)
                {
                    if (squads == null)
                    {
                        // Create a new list and assign it if possible
                        squads = new Il2CppSystem.Collections.Generic.List<RunSquadScriptableObject>();
                        try { cs.squads = squads; } catch { }
                    }
                    foreach (var sq in allSO)
                        if (sq != null) squads.Add(sq);
                    Plugin.Log.LogInfo($"[CustomSquad] Bootstrapped {squads.Count} squad(s) from assets");
                }
            }
            catch (Exception bex) { Plugin.Log.LogWarning($"[CustomSquad] bootstrap: {bex.Message}"); }
            if (squads == null || squads.Count == 0)
            {
                Plugin.Log.LogWarning("[CustomSquad] cs.squads still empty after bootstrap — cannot inject");
                return;
            }
        }

        // Add our custom ids to ProfileData.unlockedSquads — the game checks
        // this list (not a field on the SO) to decide if a tile is clickable.
        ProfileData profile = ProfileData.Instance;
        var unlocked = profile?.unlockedSquads;
        Plugin.Log.LogInfo($"[CustomSquad] ProfileData.Instance={profile != null}, unlockedSquads count={unlocked?.Count ?? -1}");

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
        int templateIndex = -1;

        for (int i = 0; i < squads.Count; i++)
        {
            var sq = squads[i];
            if (sq == null) continue;
            if (sq.squadName != null && sq.squadName.Equals("Basic Squad", StringComparison.OrdinalIgnoreCase))
            {
                template = sq;
                templateIndex = i;
                Plugin.Log.LogInfo($"[CustomSquad] Found Basic Squad at index {i}");
                break;
            }
        }

        if (template == null) { Plugin.Log.LogWarning("[CustomSquad] Basic Squad not found"); return; }
        Plugin.Log.LogInfo($"[CustomSquad] Chose template '{template.squadName}' at index {templateIndex}");

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
            string customUnityName = "CustomSquad_" + key;
            bool alreadyPresent = false;
            for (int j = 0; j < squads.Count; j++)
            {
                var sq = squads[j];
                if (sq != null && (sq.id == customId || sq.name == customUnityName)) { alreadyPresent = true; break; }
            }
            if (alreadyPresent)
            {
                EnsureSquadUnlocked(profile, customId);
                continue;
            }

            try
            {
                var clone = UnityEngine.Object.Instantiate(template);
                clone.name = customUnityName;
                try { clone.squadName = displayName; } catch {}
                try { clone.id = customId; } catch {}
                Plugin.Log.LogInfo($"[CustomSquad] clone.id after set = '{clone.id}' (wanted '{customId}'), clone.name='{clone.name}', template.id='{template.id}'");
                // Log template unlock fields (first squad only) so we know what makes Basic selectable
                if (i == 0)
                {
                    try
                    {
                        var soType = template.GetIl2CppType();
                        var allFields = soType.GetFields();
                        var sb2 = new System.Text.StringBuilder("[CustomSquad] Template fields: ");
                        foreach (var tf2 in allFields)
                            sb2.Append(tf2.Name).Append('=').Append(tf2.GetValue(template)).Append(", ");
                        Plugin.Log.LogInfo(sb2.ToString());
                    }
                    catch (Exception dumpEx) { Plugin.Log.LogWarning($"[CustomSquad] field dump: {dumpEx.Message}"); }
                }
                // If the IL2CPP id setter failed, try reflection
                if (clone.id != customId)
                {
                    try
                    {
                        var fId = AccessTools.Field(typeof(RunSquadScriptableObject), "id")
                               ?? AccessTools.Field(typeof(RunSquadScriptableObject), "_id")
                               ?? AccessTools.Field(typeof(RunSquadScriptableObject), "m_Id");
                        fId?.SetValue(clone, customId);
                        Plugin.Log.LogInfo($"[CustomSquad] id set via reflection: '{clone.id}'");
                    }
                    catch (Exception ridEx) { Plugin.Log.LogWarning($"[CustomSquad] reflection id set: {ridEx.Message}"); }
                }

                var origTeam = template.startingTeam;
                if (origTeam != null)
                {
                    DumpTeamStructure(origTeam, "BASIC-RAW");
                    var teamClone = UnityEngine.Object.Instantiate(origTeam);
                    teamClone.teamName = key + " " + displayName;

                    Plugin.Log.LogInfo($"[CustomSquad] Cloned team '{origTeam.teamName}' -> '{teamClone.teamName}' fwds={teamClone.forwards?.Count ?? -1} goalie={(teamClone.goalie != null ? teamClone.goalie.firstName : "null")}");

                    // Basic Squad ships with LW/RW/RD line positions
                    // "creation-locked" (the vanilla draft-your-team-over-the-run
                    // gimmick). Our clone inherits those locks, so on the Edit
                    // Lineup screen those slots show "LOCKED" and the player's
                    // drafted skaters have nowhere to go → they get dropped and
                    // only the unlocked slot + goalie survive. Unlock all five
                    // positions so the full custom roster can be placed. NOT a
                    // reshuffle: this only removes artificial slot locks.
                    try { UnlockAllLinePositions(teamClone); }
                    catch (Exception ulEx) { Plugin.Log.LogWarning($"[CustomSquad] UnlockAllLinePositions: {ulEx.Message}"); }

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

                    // Bug fix: NEVER let Basic's Angus McShaggy survive into a
                    // custom squad. Basic's startingTeam keeps exactly ONE forward
                    // (cat=Angus @ slot 3) that the draft flow seats as your
                    // starter. If the user configured the LD slot (Angus's slot)
                    // ApplyPlayerTeamConfig already overwrote him; if they
                    // configured a DIFFERENT position their player landed in
                    // another slot and McShaggy stayed at slot 3 → "McShaggy
                    // auto-added". Seat the user's primary forward at the Angus
                    // keep-slot, open its old slot (Basic's 1-forward shape), and
                    // repoint the squad's angusPlayer so no angus-based mechanic
                    // (Mark Bench's quest) re-introduces McShaggy.
                    bool starterAtAngus = false;
                    try { starterAtAngus = SeatUserStarterAtAngusSlot(teamClone, clone, cfg); }
                    catch (Exception anEx) { Plugin.Log.LogWarning($"[CustomSquad] Angus keep-slot: {anEx.Message}"); }

                    // Don't blank unconfigured slots — let the template's nulls
                    // (or game-supplied benchwarmers) stand so the run-start
                    // draft/superstar flow lands picks naturally without any
                    // reshuffling. User explicitly asked for no interference.
                    // (BlankUnconfiguredSlots intentionally NOT called.)

                    // ── DO NOT inject lines[0] — MIRROR Basic Squad exactly. ──
                    // PROVEN by the v2.1.26 diagnostic: Basic Squad's startingTeam
                    // ships with lines.Count == 0 (NO line array) and all locks
                    // false; the game builds the active line itself from the
                    // forwards + the superstar (AddForwardToActiveLine) + drafted
                    // skaters (AddForward) at run time. Our old EnsureLines +
                    // SyncLinesToForwards CREATED a lines[0] Basic never has; the
                    // native draft then desynced from it (lines[0] ended up
                    // pointing at drafted-skater ids whose ForwardData weren't in
                    // team.forwards, while the picked superstar sat in forwards
                    // but was absent from lines[0]) → "superstar not on team /
                    // only certain positions remain". A vanilla Basic run with a
                    // CUSTOM-style roster works fine BECAUSE it has no injected
                    // lines[0]. So we leave teamClone.lines exactly as cloned from
                    // Basic (empty) and let the native flow own the lineup.
                    // (EnsureLines + SyncLinesToForwards intentionally NOT called.)

                    DumpTeamStructure(teamClone, "CLONE-FINAL");

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
                        // After SeatUserStarterAtAngusSlot the user's starter
                        // always sits in the Angus keep-slot (LD); point the tile
                        // head there so it never resolves to a now-empty donor slot.
                        if (starterAtAngus) picked = Tape2Tape.Customization.UI.ESkaterPosition.LD;
                        else if (SlotIsConfigured(cfg.C)) picked = Tape2Tape.Customization.UI.ESkaterPosition.C;
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

                        // "Squad Head" is TILE-ONLY: it sets the head icon on the
                        // Choose Your Squad tile and must not touch any player.
                        // (Until 2.1.31 it also wrote the key player's Spine
                        // headSkin — a leftover from when the tile was rendered
                        // from that skin. The Jul-2026 menu reads
                        // squad.m_SmallTeamIcon instead, so all that did was
                        // silently change a skater's face. Removed.)
                        if (!string.IsNullOrEmpty(cfg?.SquadHead)
                            && !cfg.SquadHead.Equals("none", StringComparison.OrdinalIgnoreCase))
                        {
                            try
                            {
                                // Never assign null — that would wipe the template's
                                // icon and leave the tile blank.
                                var icon = ResolveTileIcon(cfg.SquadHead, key);
                                if (icon != null)
                                {
                                    clone.m_SmallTeamIcon = icon;
                                    clone.m_ZamboniSkaterHead = icon;
                                }
                            }
                            catch (Exception spEx) { Plugin.Log.LogWarning($"[CustomSquad] Tile icon: {spEx.Message}"); }
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

                // Gauntlet map: if "Gauntlet Map = yes" is set on the campaign,
                // copy the Gauntlet squad's maps list onto this custom clone so
                // picking the custom squad actually loads the gauntlet map. The
                // game keys map selection off each squad's own .maps list — our
                // clone of Basic otherwise inherits Basic's standard maps.
                string cloneMapSource = EffectiveMapSource();
                if (!string.IsNullOrEmpty(cloneMapSource))
                {
                    try
                    {
                        var srcSquad = FindSquadByName(cs, cloneMapSource);
                        if (srcSquad != null && srcSquad.maps != null)
                        {
                            clone.maps = srcSquad.maps;
                            Plugin.Log.LogInfo($"[MapSource] Copied {srcSquad.maps.Count} map(s) from '{srcSquad.squadName}' to '{key}'");
                        }
                        else
                            Plugin.Log.LogWarning($"[MapSource] Squad '{cloneMapSource}' not found — couldn't copy maps; custom squad will use Basic's maps");
                    }
                    catch (Exception gex) { Plugin.Log.LogWarning($"[MapSource] map copy for '{key}': {gex.Message}"); }
                }

                // Append as a NEW entry so SetupMetas builds an additional
                // tile for it — Basic Squad and all other base squads stay in
                // the list. SetupMetas wires every tile it creates (instantiate
                // prefab → Refresh → register navigable). With the unlock
                // postfixes forcing Custom_* Unlocked=true during SetupMetas,
                // the appended tile is built unlocked & selectable, not "???".
                squads.Add(clone);

                // Register the display strings so the Localized name/desc
                // patches return our text instead of "???".
                Plugin.CustomSquadText[customId] = (displayName, cfg?.Description ?? "");

                Plugin.Log.LogInfo($"[CustomSquad] Appended new squad '{key}' ('{displayName}') — squads now {squads.Count}");

                // UnlockSquad writes to ProfileData.m_SquadTeamsData which is
                // what IsValidMetaSelection reads via GetSquadTeamData(id).
                EnsureSquadUnlocked(profile, customId);
            }
            catch (Exception ex) { Plugin.Log.LogError($"[CustomSquad] inject '{key}': {ex}"); }
        }
    }

    // Jul-2026 update: SetupMetas no longer builds a tile for every entry in
    // cs.squads (appending alone stopped producing a tile — 16 base squads,
    // 15 tiles, our appended clone ignored). The menu filters by id against a
    // profile string list, so make sure the custom id is REALLY in
    // profile.unlockedSquads: call the game's UnlockSquad, then verify and
    // append the raw list entry ourselves if it didn't land.
    internal static void EnsureSquadUnlocked(ProfileData profile, string customId)
    {
        if (profile == null) return;
        try { profile.UnlockSquad(customId); }
        catch (Exception uex) { Plugin.Log.LogWarning($"[CustomSquad] UnlockSquad('{customId}'): {uex.Message}"); }
        try
        {
            var ul = profile.unlockedSquads;
            if (ul == null) { Plugin.Log.LogWarning("[CustomSquad] profile.unlockedSquads is null"); return; }
            bool present = false;
            for (int i = 0; i < ul.Count; i++)
                if (ul[i] == customId) { present = true; break; }
            if (!present)
            {
                ul.Add(customId);
                Plugin.Log.LogInfo($"[CustomSquad] '{customId}' was NOT in unlockedSquads — added directly (count now {ul.Count})");
            }
            else
                Plugin.Log.LogInfo($"[CustomSquad] '{customId}' already in unlockedSquads");
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[CustomSquad] EnsureSquadUnlocked: {ex.Message}"); }

        // Jul-2026 menu builds tiles only for squads with a saved
        // m_SquadTeamsData entry (the new locked All Angus squad has none and
        // is hidden the same way ours is). UnlockSquad early-outs when the id
        // is already in unlockedSquads — persisted from pre-update sessions —
        // so it never creates the entry for our custom ids. Create it directly.
        try
        {
            var pt = profile.GetIl2CppType();
            var pbf = Il2CppSystem.Reflection.BindingFlags.NonPublic | Il2CppSystem.Reflection.BindingFlags.Instance | Il2CppSystem.Reflection.BindingFlags.Public;
            var stf = pt.GetField("m_SquadTeamsData", pbf);
            var stList = stf?.GetValue(profile)?.TryCast<Il2CppSystem.Collections.Generic.List<SquadTeamData>>();
            if (stList == null) { Plugin.Log.LogWarning("[CustomSquad] m_SquadTeamsData not readable"); return; }
            var sb = new System.Text.StringBuilder($"[CustomSquad] SquadTeamsData ({stList.Count}): ");
            bool has = false;
            for (int i = 0; i < stList.Count; i++)
            {
                var e = stList[i];
                sb.Append('\'').Append(e.Id ?? "null").Append(e.Unlocked ? "'+ " : "'- ");
                if (e.Id == customId)
                {
                    has = true;
                    if (!e.Unlocked) { e.Unlocked = true; stList[i] = e; Plugin.Log.LogInfo($"[CustomSquad] SquadTeamData('{customId}') existed but locked — set Unlocked=true"); }
                }
            }
            Plugin.Log.LogInfo(sb.ToString());
            if (!has)
            {
                stList.Add(new SquadTeamData(customId, true));
                Plugin.Log.LogInfo($"[CustomSquad] Added SquadTeamData('{customId}', unlocked) — list now {stList.Count}");
            }
        }
        catch (Exception sx) { Plugin.Log.LogWarning($"[CustomSquad] SquadTeamData ensure: {sx.Message}"); }
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

    // Ensure a custom squad never starts with Basic's Angus McShaggy. Basic's
    // startingTeam keeps ONE forward in the cat=Angus keep-slot (forward index 3
    // / LD position) which the run's draft flow seats as the starter. Our clone
    // inherits McShaggy there. If the user configured the LD slot, the per-slot
    // apply already overwrote him. Otherwise their configured forward landed in a
    // different slot and McShaggy survived at slot 3 → he shows up uninvited.
    //
    // Move the user's PRIMARY configured forward into the Angus keep-slot (so the
    // kept starter is theirs), null the donor slot, and repoint the squad's
    // angusPlayer field away from McShaggy so the angus mechanic (Mark Bench's
    // quest) can't re-add him either.
    // Returns true if a user forward now occupies the keep-slot.
    //
    // ── WHAT THIS DOES *NOT* DO (corrected 2026-07-31) ──────────────────────
    // An earlier version of this comment claimed the null-out leaves the squad
    // "with exactly one forward (Basic's proven shape)". It does not. The only
    // forward this function ever removes is McShaggy:
    //   * LD configured    → early branch, nothing is nulled (the per-slot apply
    //                        already overwrote him in the keep-slot).
    //   * LD unconfigured  → the primary is copied INTO the keep-slot and its
    //                        donor slot is nulled, so the count drops by one —
    //                        and that one is McShaggy, not a user player.
    // Either way the clone ends up holding exactly as many forwards as the user
    // configured. "Exactly one forward" is only true for a 1-forward config.
    //
    // Consequence, reported and ACCEPTED by the user (2026-07-31): a squad that
    // configures all five forward slots starts with a full lineup, leaving the
    // run-start superstar pick nowhere to sit, and the superstar screen does not
    // appear. Configure four or fewer forwards and it comes back. The user chose
    // to keep this behaviour rather than have the mod drop one of their
    // configured players to make room — that is exactly the reshuffling they
    // have rejected before. DO NOT "fix" this by nulling a configured slot.
    // (The game-side gate itself was never proven — dump.cs is signatures-only
    // here — so if you ever need certainty, probe the run-start flow at runtime.)
    internal static bool SeatUserStarterAtAngusSlot(TeamData team, RunSquadScriptableObject clone, TeamConfig cfg)
    {
        var fwds = team?.forwards;
        if (fwds == null || fwds.Count <= 3) return false;
        const int angusIdx = 3; // Basic's cat=Angus keep-slot (LD position)

        bool seated = false;
        if (SlotIsConfigured(cfg?.LD) && fwds[angusIdx] != null)
        {
            // LD configured — ApplyPlayerTeamConfig already replaced McShaggy.
            seated = true;
        }
        else
        {
            // Find the user's primary configured forward (priority C, LW, RW, RD).
            var slots = new[] { cfg?.C, cfg?.LW, cfg?.RW, cfg?.RD };
            var idxs  = new[] { 2, 0, 1, 4 };
            int primaryIdx = -1;
            for (int k = 0; k < slots.Length; k++)
                if (SlotIsConfigured(slots[k]) && idxs[k] < fwds.Count && fwds[idxs[k]] != null)
                { primaryIdx = idxs[k]; break; }

            if (primaryIdx >= 0)
            {
                if (fwds[angusIdx] == null)
                    fwds[angusIdx] = fwds[primaryIdx];
                else
                    PatchBossLaunchMatch.CopyPlayerData(fwds[primaryIdx], fwds[angusIdx]);
                fwds[primaryIdx] = null; // open the donor slot for the draft
                seated = true;
                Plugin.Log.LogInfo($"[CustomSquad] Seated '{fwds[angusIdx].firstName} {fwds[angusIdx].lastName}' at Angus keep-slot (from slot {primaryIdx}); McShaggy removed");
            }
            else if (fwds[angusIdx] != null)
            {
                // Goalie-only squad: strip McShaggy's identity so he doesn't show.
                var a = fwds[angusIdx];
                try { a.firstName = "Draft"; a.lastName = "Pick"; } catch {}
                try { a.powerups = new Il2CppSystem.Collections.Generic.List<Rogue.Talent>(); } catch {}
                try { a.ability = null; } catch {}
                Plugin.Log.LogInfo("[CustomSquad] No configured forward — neutralized McShaggy keep-slot to a generic draft pick");
            }
        }

        try { if (fwds[angusIdx] != null) clone.angusPlayer = fwds[angusIdx]; }
        catch (Exception ex) { Plugin.Log.LogWarning($"[CustomSquad] angusPlayer repoint: {ex.Message}"); }

        return seated;
    }

    // Clear the five per-position "line-up creation locked" flags on a TeamData.
    // Basic Squad sets some of these true so the player has to draft into the
    // locked slots during the run; our clone inherits them, so the Edit Lineup
    // screen shows LW/RW/RD as "LOCKED" and drafted skaters can't be placed.
    //
    // Il2CppInterop exposes these as PROPERTIES on the TeamData wrapper (not
    // fields — confirmed by inspecting MainScriptsAssembly.dll: GetField found
    // nothing, which is why earlier attempts logged 0/5). Assign them directly.
    internal static void UnlockAllLinePositions(TeamData team)
    {
        if (team == null) return;
        int cleared = 0;
        try { team.leftWingerLineUpCreationLocked = false; cleared++; } catch (Exception ex) { Plugin.Log.LogWarning($"[CustomSquad] LW lock: {ex.Message}"); }
        try { team.rightWingerLineUpCreationLocked = false; cleared++; } catch (Exception ex) { Plugin.Log.LogWarning($"[CustomSquad] RW lock: {ex.Message}"); }
        try { team.centerLineUpCreationLocked = false; cleared++; } catch (Exception ex) { Plugin.Log.LogWarning($"[CustomSquad] C lock: {ex.Message}"); }
        try { team.leftDefensemenLineUpCreationLocked = false; cleared++; } catch (Exception ex) { Plugin.Log.LogWarning($"[CustomSquad] LD lock: {ex.Message}"); }
        try { team.rightDefensemenLineUpCreationLocked = false; cleared++; } catch (Exception ex) { Plugin.Log.LogWarning($"[CustomSquad] RD lock: {ex.Message}"); }
        Plugin.Log.LogInfo($"[CustomSquad] Unlocked {cleared}/5 line positions on '{team.teamName}'");
    }

    // ── DIAGNOSTIC ──────────────────────────────────────────────────────────
    // Dump the full shape of a TeamData (forwards array, lines[0] ids resolved
    // to names, the five lock flags, goalie) under a label. Read-only. Used to
    // capture the EXACT structure of vanilla Basic Squad vs our custom clone vs
    // the run team at each stage, so we can mirror base-game behavior precisely
    // instead of guessing. Strip these calls once the flow is confirmed.
    internal static void DumpTeamStructure(TeamData team, string label)
    {
        try
        {
            if (team == null) { Plugin.Log.LogInfo($"[DUMP/{label}] team=null"); return; }
            string Short(string id) => string.IsNullOrEmpty(id) ? "''" : (id.Length > 8 ? id.Substring(0, 8) : id);

            var fwds = team.forwards;
            int fc = -1; try { fc = fwds?.Count ?? -1; } catch { }
            Plugin.Log.LogInfo($"[DUMP/{label}] team='{team.teamName}' forwards.Count={fc}");

            // Build id→name map so line ids are readable.
            var idToName = new System.Collections.Generic.Dictionary<string, string>();
            if (fwds != null)
            {
                for (int i = 0; i < fc; i++)
                {
                    Data.ForwardData f = null; try { f = fwds[i]; } catch { }
                    if (f == null) { Plugin.Log.LogInfo($"[DUMP/{label}]   fwd[{i}] = null"); continue; }
                    string fn = "", ln = "", id = "", cat = "";
                    try { fn = f.firstName ?? ""; } catch { }
                    try { ln = f.lastName ?? ""; } catch { }
                    try { id = f.id ?? ""; } catch { }
                    try { cat = f.skaterCategory.ToString(); } catch { }
                    int tal = -1; try { tal = f.powerups != null ? f.powerups.Count : 0; } catch { }
                    string ab = ""; try { ab = f.ability != null ? (f.ability.name ?? "") : "—"; } catch { }
                    if (!string.IsNullOrEmpty(id) && !idToName.ContainsKey(id)) idToName[id] = (fn + " " + ln).Trim();
                    Plugin.Log.LogInfo($"[DUMP/{label}]   fwd[{i}] = '{fn} {ln}' id={Short(id)} cat={cat} talents={tal} ability={ab}");
                }
            }

            var lns = team.lines;
            int lc = -1; try { lc = lns?.Count ?? -1; } catch { }
            if (lns != null && lc > 0 && lns[0] != null)
            {
                var l0 = lns[0];
                string lw = "", rw = "", c = "", ld = "", rd = "";
                try { lw = l0.leftWinger ?? ""; } catch { }
                try { rw = l0.rightWinger ?? ""; } catch { }
                try { c = l0.center ?? ""; } catch { }
                try { ld = l0.leftDefensemen ?? ""; } catch { }
                try { rd = l0.rightDefensemen ?? ""; } catch { }
                string Nm(string id) => string.IsNullOrEmpty(id) ? "—" : (idToName.TryGetValue(id, out var n) ? n : "?" + Short(id));
                Plugin.Log.LogInfo($"[DUMP/{label}]   lines={lc} lines[0]: LW={Nm(lw)} RW={Nm(rw)} C={Nm(c)} LD={Nm(ld)} RD={Nm(rd)}");
            }
            else Plugin.Log.LogInfo($"[DUMP/{label}]   lines={lc} (no lines[0])");

            bool lkLW = false, lkRW = false, lkC = false, lkLD = false, lkRD = false;
            try { lkLW = team.leftWingerLineUpCreationLocked; } catch { }
            try { lkRW = team.rightWingerLineUpCreationLocked; } catch { }
            try { lkC = team.centerLineUpCreationLocked; } catch { }
            try { lkLD = team.leftDefensemenLineUpCreationLocked; } catch { }
            try { lkRD = team.rightDefensemenLineUpCreationLocked; } catch { }
            Plugin.Log.LogInfo($"[DUMP/{label}]   LOCKS: LW={lkLW} RW={lkRW} C={lkC} LD={lkLD} RD={lkRD}");

            string gn = "null"; try { gn = team.goalie != null ? (team.goalie.firstName + " " + team.goalie.lastName) : "null"; } catch { }
            Plugin.Log.LogInfo($"[DUMP/{label}]   goalie={gn}");
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[DUMP/{label}] {ex.Message}"); }
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
                string customUnityName = "CustomSquad_" + key;
                bool alreadyPresent = false;
                for (int j = 0; j < squads.Count; j++)
                {
                    var sq = squads[j];
                    if (sq != null && (sq.id == customId || sq.name == customUnityName)) { alreadyPresent = true; break; }
                }
                if (alreadyPresent)
                {
                    try { profile?.UnlockSquad(customId); } catch { }
                    continue;
                }

                try
                {
                    var clone = UnityEngine.Object.Instantiate(template);
                    clone.name = customUnityName;
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

                    try { profile?.UnlockSquad(customId); } catch { }

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
            {
                Plugin.Log.LogInfo($"[CustomSquad] UnlockPostfix: blanking unlock condition for '{id}' (was '{__result}')");
                __result = "";
            }
            else if (id.StartsWith("Basic", StringComparison.OrdinalIgnoreCase) && __result != "")
            {
                // Diagnostic: log what Basic squad returns so we know what our clone inherits
                Plugin.Log.LogInfo($"[CustomSquad] UnlockPostfix: id='{id}' result='{__result}'");
            }
        }
        catch { }
    }

    // Force IsUnlocked = true for any squad we injected so the tile is
    // clickable even if ProfileData.unlockedSquads wasn't populated yet.
    public static void IsUnlockedPostfix(RunSquadScriptableObject __instance, ref bool __result)
    {
        try
        {
            if (__instance == null || __result) return;
            string id = __instance.id;
            if (!string.IsNullOrEmpty(id) && id.StartsWith("Custom_", StringComparison.Ordinal))
                __result = true;
        }
        catch { }
    }
}

// ============================================================
// After MetaTeamItem.Refresh(squad, savedData) sets IsUnlocked from
// unlockCondition.IsMet(), force it true for Custom_* squads.
// Toggle may still be null here (SetupMetas wires it after Refresh),
// so we also patch Start() which fires once Toggle is ready.
// ============================================================
public static class PatchMetaTeamItemRefresh
{
    private static void ForceUnlock(Tape2Tape.Hockey.UI.MetaTeamItem item, string tag)
    {
        try
        {
            var nav = item.Navigable;
            var tog = item.Toggle;
            var tnav = tog != null ? tog.Navigable : null;

            // BEFORE state — reveals the REAL click gate. The locked-sound /
            // blacked-out tile means the click is rejected before OnMetaClicked.
            // SetCanToggle only sets the Toggle's CanToggle backing field; the
            // actual click gate is AbstractNavigable.CanBeClicked, which nothing
            // we did previously touched.
            Plugin.Log.LogInfo($"[CustomSquad] {tag} BEFORE: IsUnlocked={item.IsUnlocked}"
                + $" | nav={(nav == null ? "NULL" : $"isNav={nav.IsNavigable},canClick={nav.CanBeClicked}")}"
                + $" | tog={(tog == null ? "NULL" : $"canToggle={tog.CanToggle}")}"
                + $" | togNav={(tnav == null ? "NULL" : $"isNav={tnav.IsNavigable},canClick={tnav.CanBeClicked}")}");

            item.IsUnlocked = true;
            if (nav != null) { try { nav.IsNavigable = true; nav.CanBeClicked = true; } catch { } }
            if (tog != null) { try { tog.SetCanToggle(true); } catch { } }
            if (tnav != null) { try { tnav.IsNavigable = true; tnav.CanBeClicked = true; } catch { } }

            // Visual: flip the animator IS_UNLOCKED bool so the tile isn't
            // blacked-out/locked. IS_UNLOCKED is a private static Animator-hash
            // int on MetaTeamItem; read it + m_Anim via IL2CPP reflection.
            try
            {
                var it = item.GetIl2CppType();
                var bf = Il2CppSystem.Reflection.BindingFlags.NonPublic | Il2CppSystem.Reflection.BindingFlags.Instance;
                var sf = Il2CppSystem.Reflection.BindingFlags.NonPublic | Il2CppSystem.Reflection.BindingFlags.Static;
                var animObj = it.GetField("m_Anim", bf)?.GetValue(item);
                var anim = animObj?.TryCast<UnityEngine.Animator>();
                if (anim != null)
                {
                    int hUnlocked = ReadStaticHash(it, "IS_UNLOCKED", sf);
                    int hNew = ReadStaticHash(it, "IS_NEW_UNLOCK", sf);
                    if (hUnlocked != int.MinValue) anim.SetBool(hUnlocked, true);
                    if (hNew != int.MinValue) anim.SetBool(hNew, false);
                    Plugin.Log.LogInfo($"[CustomSquad] {tag}: animator IS_UNLOCKED(hash={hUnlocked})=true");
                }
                else Plugin.Log.LogInfo($"[CustomSquad] {tag}: m_Anim null — skipped visual unlock");
            }
            catch (Exception aex) { Plugin.Log.LogWarning($"[CustomSquad] {tag} animator: {aex.Message}"); }

            Plugin.Log.LogInfo($"[CustomSquad] {tag} AFTER: nav.canClick={(nav == null ? "-" : nav.CanBeClicked.ToString())}"
                + $" tog.canToggle={(tog == null ? "-" : tog.CanToggle.ToString())}"
                + $" togNav.canClick={(tnav == null ? "-" : tnav.CanBeClicked.ToString())}");
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[CustomSquad] {tag} ForceUnlock: {ex.Message}"); }
    }

    private static int ReadStaticHash(Il2CppSystem.Type t, string name, Il2CppSystem.Reflection.BindingFlags sf)
    {
        try
        {
            var v = t.GetField(name, sf)?.GetValue(null);
            if (v != null && int.TryParse(v.ToString(), out int h)) return h;
        }
        catch { }
        return int.MinValue;
    }

    // One-shot per squad. The tile's head icon is MetaTeamItem.m_KeyPlayerHead
    // (a UI Image); the 2.1.31 "Squad Head" heuristic searched every loaded
    // Sprite for the configured FACE name and never matched, because face skins
    // and these UI head sprites are different naming spaces. Log what vanilla
    // squads actually carry so the real source field — and the real sprite
    // names a Squad Head value could legally take — stop being guesswork.
    private static readonly HashSet<string> _probedTiles = new HashSet<string>(StringComparer.Ordinal);
    private static void ProbeTileIcon(Tape2Tape.Hockey.UI.MetaTeamItem item, RunSquadScriptableObject squad, string id)
    {
        try
        {
            string key = string.IsNullOrEmpty(id) ? (squad.name ?? "?") : id;
            if (!_probedTiles.Add(key)) return;

            string head = "null";
            try
            {
                var it = item.GetIl2CppType();
                var bf = Il2CppSystem.Reflection.BindingFlags.NonPublic | Il2CppSystem.Reflection.BindingFlags.Instance;
                var imgObj = it.GetField("m_KeyPlayerHead", bf)?.GetValue(item);
                var img = imgObj?.TryCast<UnityEngine.UI.Image>();
                if (img == null) head = "(field missing)";
                else if (img.sprite == null) head = "(image, no sprite)";
                else head = img.sprite.name;
            }
            catch (Exception ex) { head = "err:" + ex.Message; }

            string small = "null", zam = "null";
            try { if (squad.SmallTeamIcon != null) small = squad.SmallTeamIcon.name; } catch { }
            try { if (squad.ZamboniSkaterHead != null) zam = squad.ZamboniSkaterHead.name; } catch { }
            Plugin.Log.LogInfo($"[TileIcon] '{key}' m_KeyPlayerHead='{head}' SmallTeamIcon='{small}' ZamboniSkaterHead='{zam}'");
        }
        catch { }
    }

    // Fires immediately after Refresh — Toggle may not be wired yet.
    public static void Postfix(Tape2Tape.Hockey.UI.MetaTeamItem __instance, RunSquadScriptableObject __0)
    {
        try
        {
            if (__0 == null || __instance == null) return;
            string id = __0.id;
            string uname = __0.name;
            ProbeTileIcon(__instance, __0, id);   // vanilla squads too — that's the reference data
            bool isCustom = (!string.IsNullOrEmpty(id) && id.StartsWith("Custom_", StringComparison.Ordinal))
                         || (!string.IsNullOrEmpty(uname) && uname.StartsWith("CustomSquad_", StringComparison.Ordinal));
            if (!isCustom) return;
            ForceUnlock(__instance, $"Refresh id='{id}'");
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[CustomSquad] Refresh postfix: {ex.Message}"); }
    }

    // Start() fires after Refresh in Unity lifecycle — if it resets IsUnlocked
    // based on the cloned squad's original unlock state, force it back here.
    public static void StartPostfix(Tape2Tape.Hockey.UI.MetaTeamItem __instance)
    {
        try
        {
            if (__instance == null) return;
            var squad = __instance.CurrentSquad;
            if (squad == null) return;
            string id = squad.id;
            string uname = squad.name;
            bool isCustom = (!string.IsNullOrEmpty(id) && id.StartsWith("Custom_", StringComparison.Ordinal))
                         || (!string.IsNullOrEmpty(uname) && uname.StartsWith("CustomSquad_", StringComparison.Ordinal));
            if (!isCustom) return;
            ForceUnlock(__instance, $"Start id='{id}'");
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[CustomSquad] Start postfix: {ex.Message}"); }
    }
}

// ============================================================
// UnlockSystem.IsUnlocked gates whether a squad is actually selectable
// on the ChooseMetaMenu. Custom squads have no entry in _unlockData so
// the call returns false and clicking the tile plays a locked sound.
// Force-return true for any id that starts with "Custom_".
// ============================================================
public static class PatchUnlockSystem
{
    public static void Postfix(string id, ref bool __result)
    {
        try
        {
            if (!__result && !string.IsNullOrEmpty(id) && id.StartsWith("Custom_", StringComparison.Ordinal))
            {
                Plugin.Log.LogInfo($"[CustomSquad] UnlockSystem.IsUnlocked: forcing true for '{id}'");
                __result = true;
            }
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[CustomSquad] UnlockSystem.IsUnlocked postfix: {ex.Message}"); }
    }
}

// ============================================================
// IsValidMetaSelection is the final gate in ChooseMetaMenu before
// a squad click proceeds to the difficulty screen. Whatever internal
// check it does (unlock condition, profile data, etc.), force true
// for Custom_* squads so they are always selectable.
// ============================================================
public static class PatchIsValidMetaSelection
{
    // Use __0 for IL2CPP — parameter names are not preserved in AOT compilation.
    public static void Postfix(Tape2Tape.Hockey.UI.MetaTeamItem __0, ref bool __result)
    {
        try
        {
            var squad = __0?.CurrentSquad;
            if (squad == null) return;
            string id = squad.id;
            string uname = squad.name;
            bool isCustom = (!string.IsNullOrEmpty(id) && id.StartsWith("Custom_", StringComparison.Ordinal))
                         || (!string.IsNullOrEmpty(uname) && uname.StartsWith("CustomSquad_", StringComparison.Ordinal));
            if (!isCustom) return;
            Plugin.Log.LogInfo($"[CustomSquad] IsValidMetaSelection: id='{id}' originalResult={__result} -> forcing true");
            __result = true;
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[CustomSquad] IsValidMetaSelection postfix: {ex.Message}"); }
    }
}

// ============================================================
// ProfileData.GetSquadTeamData returns the unlock/stats record for a
// squad. SetupMetas calls this for every squad and creates a locked
// (non-interactive) tile when SquadTeamData.Unlocked == false.
// Custom squads have no entry in m_SquadTeamsData so the default struct
// is returned with Unlocked=false — fix by forcing it true here.
// ============================================================
public static class PatchGetSquadTeamData
{
    public static void Postfix(string squadId, ref SquadTeamData __result)
    {
        try
        {
            if (!string.IsNullOrEmpty(squadId) && squadId.StartsWith("Custom_", StringComparison.Ordinal))
            {
                __result.Unlocked = true;
                Plugin.Log.LogInfo($"[CustomSquad] GetSquadTeamData: forced Unlocked=true for '{squadId}'");
            }
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[CustomSquad] GetSquadTeamData postfix: {ex.Message}"); }
    }
}

// ============================================================
// Diagnostic: trace the click flow in ChooseMetaMenu to find where
// custom squads are getting blocked.
// ============================================================
public static class PatchChooseMetaDiag
{
    public static void OnMetaClickedPrefix(Tape2Tape.Hockey.UI.ChooseMetaMenu __instance)
    {
        try
        {
            var cur = __instance?.CurrentMeta;
            var squad = cur?.CurrentSquad;
            Plugin.Log.LogInfo($"[CustomSquad] OnMetaClicked: squad='{squad?.id ?? "null"}' IsUnlocked={cur?.IsUnlocked}");
        }
        catch { }
    }

    public static void ConfirmPrefix(Tape2Tape.Hockey.UI.ChooseMetaMenu __instance)
    {
        try
        {
            var squad = __instance?.CurrentRunSquad;
            Plugin.Log.LogInfo($"[CustomSquad] ConfirmSelections: squad='{squad?.id ?? "null"}'");
        }
        catch { }
    }

    public static void CanGoNextPostfix(Tape2Tape.Hockey.UI.ChooseMetaMenu __instance, ref bool __result)
    {
        try
        {
            var squad = __instance?.CurrentRunSquad;
            Plugin.Log.LogInfo($"[CustomSquad] CanGoToNextState: squad='{squad?.id ?? "null"}' result={__result}");
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
            Plugin.AppliedFreeAgentPtrs.Clear();
            Plugin.FreeAgentSignedConfigs.Clear();
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
            // Reliable backup trigger for the library dump. PatchSetCurrentAct can fire
            // before TeamData is loaded; TitleScreen.RefreshCampaignData fires later
            // when the main menu is fully up. _guiListsDumped prevents double-running.
            try { LogRepositories.AutoDumpNameLists(); }
            catch (Exception dumpEx) { Plugin.Log.LogWarning($"[Dump] TitleScreen dump: {dumpEx.Message}"); }

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
    // First line of defense: mutate the NodeType on the way INTO GetBlueprint.
    // IL2CPP's Harmony hook on this method is flaky (method is short enough to
    // be inlined in some builds), so we also patch CreateMapNode below as
    // a belt-and-suspenders — the one that fires first handles the swap.
    public static void Prefix(ref STS.Map.NodeType type)
    {
        try
        {
            if (type != STS.Map.NodeType.GeneralManager) return;
            // The GM squad lives on these nodes — never cap them away.
            if (Plugin.GmSquadActive) return;
            if (Plugin.FreeAgentNodesPlaced >= Plugin.MaxFreeAgentNodes)
            {
                type = STS.Map.NodeType.TeamTraining;
                Plugin.Log.LogInfo($"[Campaign] GetBlueprint: FA node cap ({Plugin.MaxFreeAgentNodes}) reached — substituting TeamTraining");
            }
            else
            {
                Plugin.FreeAgentNodesPlaced++;
                Plugin.Log.LogInfo($"[Campaign] GetBlueprint: FA node placed #{Plugin.FreeAgentNodesPlaced}/{Plugin.MaxFreeAgentNodes}");
            }
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[Campaign] MapBlueprint prefix: {ex.Message}"); }
    }
}

// ============================================================
// Second line of defense for FA node cap. Separate class so it doesn't
// collide with PatchCreateMapNode (which handles Challenge→Elite swaps).
// Both prefixes run on CreateMapNode; order is not important since they
// operate on different nodeType values.
// ============================================================
public static class PatchCreateMapNodeFACap
{
    public static void Prefix(STS.Map.Node node)
    {
        try
        {
            if (node?.layerNodeType == null) return;
            var lnt = node.layerNodeType;

            // GM squad: its free-agent nodes are the squad's whole mechanic, so they
            // are placed where its own layout wants them and are NEVER capped. Done
            // here rather than in a separate patch so there's no ordering race
            // between forcing a node to GeneralManager and the cap swapping it away.
            if (Plugin.GmSquadActive)
            {
                int layer = node.LayerIndex;
                if (lnt.nodeType == STS.Map.NodeType.GeneralManager)
                {
                    // The opening node of map 1 is overridden even though it is
                    // already a GM node: the one the map supplies carries its own
                    // smaller selection count, and this is the node that has to fill
                    // the roster before the first game.
                    if (layer == Plugin.GmOverrideLayer && !Plugin.GmLayersDone.Contains(layer)
                        && Plugin.GmForcedLayers.TryGetValue(layer, out int want) && want > 0)
                    {
                        int had = lnt.gmSelectionCount;
                        lnt.gmSelectionCount = want;
                        Plugin.GmLayersDone.Add(layer);
                        Plugin.Log.LogInfo($"[GmSquad] Layer {layer} opening node: selection count {had} -> {want} (cap does not apply)");
                        return;
                    }
                    // Any other layer keeps the GM node exactly as the map intended.
                    Plugin.GmLayersDone.Add(layer);
                    Plugin.Log.LogInfo($"[GmSquad] Layer {layer} already has a GM node offering"
                        + $" {lnt.gmSelectionCount} player(s) — kept as-is (cap does not apply)");
                    return;
                }
                if (Plugin.GmForcedLayers.TryGetValue(layer, out int picks) && !Plugin.GmLayersDone.Contains(layer))
                {
                    lnt.nodeType = STS.Map.NodeType.GeneralManager;
                    // Set the selection count too. Without this the node keeps the
                    // count belonging to whatever it replaced, and offers the wrong
                    // number of players to sign.
                    if (picks > 0) lnt.gmSelectionCount = picks;
                    Plugin.GmLayersDone.Add(layer);
                    Plugin.Log.LogInfo($"[GmSquad] Layer {layer}: node converted to a GM node offering {lnt.gmSelectionCount} player(s)");
                }
                return;
            }

            if (lnt.nodeType != STS.Map.NodeType.GeneralManager) return;
            if (Plugin.FreeAgentNodesPlaced >= Plugin.MaxFreeAgentNodes)
            {
                lnt.nodeType = STS.Map.NodeType.TeamTraining;
                Plugin.Log.LogInfo($"[Campaign] CreateMapNode: FA node cap ({Plugin.MaxFreeAgentNodes}) reached — swapping layer to TeamTraining");
            }
            else
            {
                Plugin.FreeAgentNodesPlaced++;
                Plugin.Log.LogInfo($"[Campaign] CreateMapNode: FA node placed #{Plugin.FreeAgentNodesPlaced}/{Plugin.MaxFreeAgentNodes}");
            }
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[Campaign] CreateMapNode FA cap prefix: {ex.Message}"); }
    }
}

// ============================================================
// Third line of defense: after MapObject.InitializeMap runs, walk every
// MapNode and, if it's a GeneralManagerMapNode, replace its layerNodeType.
// This catches any FA node that slipped past the earlier patches (e.g.
// regenerated by a chaos effect or loaded from a saved map).
// ============================================================
public static class PatchInitializeMapPost
{
    public static void Postfix(STS.Map.MapObject __instance)
    {
        try
        {
            if (__instance == null) return;
            // GM squad runs keep every GM node — see Plugin.GmSquadActive.
            if (Plugin.GmSquadActive) return;
            var nodes = __instance.MapNodes;
            if (nodes == null) return;
            int swapped = 0;
            for (int i = 0; i < nodes.Count; i++)
            {
                var mn = nodes[i];
                if (mn == null) continue;
                var lnt = mn?.Node?.layerNodeType;
                if (lnt == null) continue;
                if (lnt.nodeType != STS.Map.NodeType.GeneralManager) continue;
                if (Plugin.FreeAgentNodesPlaced >= Plugin.MaxFreeAgentNodes)
                {
                    lnt.nodeType = STS.Map.NodeType.TeamTraining;
                    swapped++;
                }
                else
                {
                    Plugin.FreeAgentNodesPlaced++;
                }
            }
            if (swapped > 0)
                Plugin.Log.LogInfo($"[Campaign] InitializeMap post: swapped {swapped} FA node(s) to TeamTraining");
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[Campaign] InitializeMap postfix: {ex.Message}"); }
    }
}

// ============================================================
// Campaign opponents: which config team each match node stands for.
//
// Historically the config was applied to MatchMapNode.opponent inside
// Boss/EliteMapNode.LaunchMatch — i.e. after the player had already seen the
// map node, its tooltip and the pre-match preview, all of which read `opponent`
// and therefore showed the VANILLA team. Now the whole map is configured up
// front (PatchMapOpponents below) and the launch path only fills in what map
// generation didn't manage to do.
//
// The apply itself is idempotent for the manual-config path — ApplyTeamFromConfig
// wipes powerups / ability / relics before writing — and CurrentRemixBoost is 0,
// so the one additive step (BoostTeam) is a no-op today. The bookkeeping below is
// still worth having: it keeps the log honest about who configured what, and it
// stops a re-entered map from redoing every node on every InitializeMap.
// ============================================================
public static class CampaignOpponents
{
    // TeamData instance id -> config game index most recently applied to it.
    // Instance ids are the only identity we can read cheaply and safely here;
    // deliberately NO reflection into the boxed struct fields of these objects
    // (see the 0xC0000005 warning at PatchChooseMetaUI.FaceToSprite).
    private static readonly Dictionary<int, int> _configured = new Dictionary<int, int>();

    // TeamData instance id -> the node key that claimed it (assignments.txt).
    // Kept separate from _configured rather than folding both into one map: the
    // sequential path is confirmed working and its four call sites stay untouched.
    //
    // This also has to OUTRANK the launch-time fallbacks. BossMapNode/EliteMapNode
    // .LaunchMatch both call Ensure(opponent, GamesPlayed, ...) as a correcting
    // fallback; without this, an assigned node would be configured correctly at map
    // generation and then overwritten at launch by whatever team the linear game
    // counter happened to point at.
    private static readonly Dictionary<int, string> _assigned = new Dictionary<int, string>();

    internal static void ForgetAll(string why)
    {
        if (_configured.Count > 0 || _assigned.Count > 0)
            Plugin.Log.LogInfo($"[MapTeams] Forgetting {_configured.Count} configured + {_assigned.Count} assigned opponent(s) — {why}");
        _configured.Clear();
        _assigned.Clear();
    }

    /// <summary>Node key that claimed this TeamData via assignments.txt, or null.</summary>
    internal static string AssignedKey(TeamData team)
    {
        if (!TryGetId(team, out int id)) return null;
        return _assigned.TryGetValue(id, out string k) ? k : null;
    }

    /// <summary>Apply a specific TeamConfig to this team on behalf of a node
    /// assignment. Unlike <see cref="Ensure"/> the team is chosen by node, not by
    /// game number, so nothing here reads Plugin.GamesPlayed.</summary>
    internal static bool EnsureAssigned(TeamData team, TeamConfig cfg, string nodeKey, string why)
    {
        try
        {
            if (team == null || cfg == null || Plugin.IsDefaultMode) return false;
            if (!TryGetId(team, out int id)) return false;

            if (_assigned.TryGetValue(id, out string already) && already == nodeKey)
                return false;   // same node, same team — silent, this is the second hook

            // Same preamble RemixTeamForGame runs before ApplyTeamFromConfig; the
            // apply depends on the repositories being resolved and on the
            // cleared-player set being reset per team.
            PatchBossLaunchMatch.EnsureRepos();
            PatchBossLaunchMatch.ResetClearedPlayers();

            // Deliberately NO BoostTeam here. It is additive, and a node assignment
            // can legitimately be re-applied (two map hooks, plus the preview pass),
            // which would compound the boost. CurrentRemixBoost is 0 today so the
            // sequential path gets away with it; this path shouldn't rely on that.
            PatchBossLaunchMatch.ApplyTeamFromConfig(team, cfg);
            _assigned[id] = nodeKey;
            _configured.Remove(id);   // a node assignment supersedes any game-index claim
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[Assign] {why}: {ex}");
            return false;
        }
    }

    private static bool TryGetId(TeamData team, out int id)
    {
        id = 0;
        if (team == null) return false;
        try { id = team.GetInstanceID(); return true; }
        catch { return false; }
    }

    /// <summary>Game index this TeamData was last configured for, or -1.</summary>
    internal static int AppliedGameIndex(TeamData team)
    {
        if (!TryGetId(team, out int id)) return -1;
        return _configured.TryGetValue(id, out int g) ? g : -1;
    }

    /// <summary>Apply the campaign config for <paramref name="gameIndex"/> to this
    /// team unless that exact config is already on it. Returns true if it applied.</summary>
    internal static bool Ensure(TeamData team, int gameIndex, string why)
    {
        try
        {
            if (team == null || Plugin.IsDefaultMode) return false;
            if (gameIndex < 0) return false;
            if (!TryGetId(team, out int id)) return false;

            // A node assignment wins over the linear game counter. Without this the
            // LaunchMatch fallbacks would undo it moments before the match starts.
            if (_assigned.TryGetValue(id, out string pinnedBy))
            {
                Plugin.Log.LogInfo($"[Assign] {why}: '{team.teamName}' is pinned by assignments.txt"
                    + $" (node {pinnedBy}) — leaving it as assigned");
                return false;
            }

            if (_configured.TryGetValue(id, out int applied) && applied == gameIndex)
            {
                Plugin.Log.LogInfo($"[MapTeams] {why}: '{team.teamName}' already configured for game {gameIndex + 1} — skipping re-apply");
                return false;
            }

            PatchBossLaunchMatch.RemixTeamForGame(team, Plugin.CurrentRemixBoost, gameIndex);
            _configured[id] = gameIndex;
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[MapTeams] {why}: {ex}");
            return false;
        }
    }
}

// ============================================================
// Assign a config team to every campaign match node as soon as the map exists,
// so the node icon, the tooltip, the pre-match preview (skaters and colours
// included) and the match itself all read the same TeamData.
//
// Ordering is "sequential by match depth", per the user's decision: match nodes
// are grouped by their layer, the layers holding match nodes are ranked in
// ascending order, and the k-th such layer gets ConfigTeams[MapStartGamesPlayed + k].
// Two match nodes on the same layer are both "the next game" on a branching map,
// so they deliberately get the SAME team — picking a path never changes who you
// play, exactly as before this change.
//
// Only Elite and Boss nodes take part. A Challenge node that the campaign is NOT
// replacing doesn't consume a config team (PatchMatchGameEnd won't count its win),
// so giving it one would desynchronise the whole sequence. Challenges the campaign
// DOES replace have already been rewritten to Elite by PatchCreateMapNode, and so
// arrive here as real EliteMapNodes.
// ============================================================
public static class PatchMapOpponents
{
    private static bool IsChallengeNode(STS.Map.MatchMapNode node)
    {
        // IL2CPP `is` is unreliable across proxy boundaries, so mirror the
        // belt-and-braces detection PatchMatchGameEnd already uses.
        try { if (node.TryCast<ChallengeMapNode>() != null) return true; } catch { }
        string names = "";
        try { names += node.GetType()?.FullName ?? ""; } catch { }
        try { names += " " + (node.GetIl2CppType()?.FullName ?? ""); } catch { }
        names = names.ToLowerInvariant();
        return names.Contains("challenge") || names.Contains("spartan") || names.Contains("gauntlet");
    }

    // ---- node-visual probe -------------------------------------------------
    // The map node still shows the VANILLA team ("Tycoons", "Team Canada") even
    // though opponent is now our config team: EliteMapNode.SetElite(TeamData,
    // string eliteSkin) takes its art from ActElite.stadiumAnimation, a Spine
    // animation baked per vanilla elite, which has nothing to do with TeamData.
    // A Spine skin cannot be built from a PNG (session 12) so the fix has to be a
    // sprite renderer — this dumps the node hierarchy once per session so we can
    // see which renderer to target instead of guessing. Read-only: names and
    // sprite names, no Spine internals (walking Skin.Attachments hard-crashed the
    // game — see PatchChooseMetaUI.FaceToSprite).
    private static bool _probedNodeVisuals;

    private static string PathOf(Transform t, Transform root)
    {
        var parts = new List<string>();
        int guard = 0;
        while (t != null && t != root && guard++ < 12)
        {
            parts.Insert(0, t.name);
            t = t.parent;
        }
        return parts.Count > 0 ? string.Join("/", parts.ToArray()) : ".";
    }

    private static void ProbeNodeVisuals(STS.Map.MatchMapNode node, string vanillaName)
    {
        try
        {
            var go = node.gameObject;
            if (go == null) return;
            var root = go.transform;
            Plugin.Log.LogInfo($"[NodeArt] --- node '{go.name}' (vanilla team '{vanillaName}') ---");

            try
            {
                var srs = go.GetComponentsInChildren<SpriteRenderer>(true);
                if (srs != null)
                    foreach (var sr in srs)
                    {
                        if (sr == null) continue;
                        string sprite = "null";
                        try { sprite = sr.sprite != null ? sr.sprite.name : "null"; } catch { }
                        Plugin.Log.LogInfo($"[NodeArt]   SpriteRenderer '{PathOf(sr.transform, root)}'"
                            + $" sprite='{sprite}' enabled={sr.enabled} order={sr.sortingOrder}");
                    }
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"[NodeArt]   SpriteRenderer sweep: {ex.Message}"); }

            try
            {
                var skel = node.skeletonSprite;
                if (skel != null)
                    Plugin.Log.LogInfo($"[NodeArt]   SkeletonAnimation on '{PathOf(skel.transform, root)}' name='{skel.name}'");
                else
                    Plugin.Log.LogInfo("[NodeArt]   SkeletonAnimation: none");
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"[NodeArt]   skeletonSprite: {ex.Message}"); }

            // m_Icon is a private SerializeField, but it's a reference type — the
            // same kind of read PatchMapNodeTooltip already does for m_TooltipDesc.
            try
            {
                var f = node.GetIl2CppType().GetField("m_Icon");
                var icon = f != null ? f.GetValue(node) : null;
                if (icon != null)
                {
                    var sr = icon.TryCast<SpriteRenderer>();
                    string sprite = "unreadable";
                    try { sprite = sr != null && sr.sprite != null ? sr.sprite.name : "null"; } catch { }
                    Plugin.Log.LogInfo($"[NodeArt]   m_Icon sprite='{sprite}'");
                }
                else Plugin.Log.LogInfo("[NodeArt]   m_Icon: null");
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"[NodeArt]   m_Icon: {ex.Message}"); }
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[NodeArt] probe: {ex.Message}"); }
    }

    // ---- node logo ---------------------------------------------------------
    // Swap the node's vanilla team mascot (the maple leaf on a pole for Team
    // Canada, etc.) for the campaign team's logo.
    //
    // The mascot is MapNode.skeletonSprite, a Spine SkeletonAnimation whose skin
    // comes from EliteMapNode.SetElite(TeamData, string eliteSkin) — and eliteSkin
    // is ActElite.stadiumAnimation, baked per vanilla elite. It has NOTHING to do
    // with the TeamData we rewrite, which is why the node kept saying "Tycoons"
    // after the pre-match preview started reading correctly.
    //
    // A Spine skin cannot be built from a PNG (session 12: no atlas region exists
    // for one, and walking Skin.Attachments to fake it hard-crashed the game), so
    // the logo goes on a plain SpriteRenderer parented to the mascot, auto-fitted
    // to the mascot's rendered bounds, and the mascot's own renderer is switched
    // off. Everything here is ordinary Unity API on reference types — no reflection
    // into Spine internals.
    private const string LogoChildName = "__CampaignNodeLogo";

    // Placement, as a fraction of the node graphic's rendered bounds. The mascot
    // sits on top of a pole above the stadium — measured off a screenshot, its
    // centre is ~0.33 of the bounds height above the centre and it stands ~0.35 of
    // the bounds height tall.
    //
    // The Flag anchor was tried and is WRONG: flag_base is planted on the dome, not
    // at the top of the pole, so it put the logo across the stadium instead of
    // where the mascot is. Its numbers are still logged for reference, but it is no
    // longer used to position anything.
    private const float MascotHeightFraction = 0.35f;
    private const float MascotCentreOffset = 0.33f;   // upward, from bounds centre


    // THE icon above the node is 'rewardIcon_image' — a uGUI Image, which is why
    // every sweep so far missed it: Image is a Graphic on a CanvasRenderer, NOT a
    // Renderer, so GetComponentsInChildren<Renderer> walks straight past it. That
    // also explains the dead ends: it was never the NodeGraphic skeleton, never the
    // explosionSkeleton, and never a Spine skin or animation.
    //
    // So don't hide anything and don't overlay anything — just point that Image at
    // the campaign team's logo. One sprite assignment, the game's own object, in the
    // game's own position and size.
    //
    // Set overrideSprite as well as sprite: the squad tile in session 12 needed
    // exactly that, its `.sprite` staying at a placeholder while overrideSprite is
    // what actually draws.
    private const string RewardIconName = "rewardicon";
    private static bool _loggedNodeImages;

    internal static bool ReplaceRewardIcon(STS.Map.MatchMapNode node, TeamData team)
    {
        try
        {
            if (node == null || team == null) return false;
            UnityEngine.Sprite sprite = null;
            try { sprite = team.logo; } catch { }
            if (sprite == null) return false;

            var go = node.gameObject;
            if (go == null) return false;

            var images = go.GetComponentsInChildren<UnityEngine.UI.Image>(true);
            if (images == null) return false;

            // One-off inventory, so a rename or a second candidate is obvious from
            // the log rather than needing another probe build.
            if (!_loggedNodeImages)
            {
                _loggedNodeImages = true;
                var sb = new StringBuilder();
                foreach (var im in images)
                {
                    if (im == null) continue;
                    string sp = "null";
                    try { sp = im.sprite != null ? im.sprite.name : "null"; } catch { }
                    sb.Append($"\n[NodeArt]   Image '{im.name}' sprite='{sp}' enabled={im.enabled}");
                }
                Plugin.Log.LogInfo($"[NodeArt] uGUI Images on '{go.name}':{sb}");
            }

            bool replaced = false;
            foreach (var im in images)
            {
                if (im == null) continue;
                string n = im.name ?? "";
                if (n.IndexOf(RewardIconName, StringComparison.OrdinalIgnoreCase) < 0) continue;
                try
                {
                    im.sprite = sprite;
                    im.overrideSprite = sprite;
                    im.enabled = true;
                    replaced = true;
                    Plugin.Log.LogInfo($"[NodeArt] '{team.teamName}': set Image '{n}' to logo '{sprite.name}'.");
                }
                catch (Exception ex) { Plugin.Log.LogWarning($"[NodeArt] setting '{n}': {ex.Message}"); }
            }
            return replaced;
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[NodeArt] ReplaceRewardIcon: {ex.Message}"); return false; }
    }

    // Late pass: the node is fully built by now, so this is where the swap is most
    // likely to stick. Also retires our own overlay sprite once the real icon is
    // carrying the logo, so the two can't both show.
    internal static void EnsureNodeArt(STS.Map.MatchMapNode node)
    {
        try
        {
            if (node == null) return;
            TeamData opp = null;
            try { opp = node.opponent; } catch { }
            if (opp == null) return;
            if (!ReplaceRewardIcon(node, opp)) return;
            RetireOverlay(node);
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[NodeArt] EnsureNodeArt: {ex.Message}"); }
    }

    private static void RetireOverlay(STS.Map.MatchMapNode node)
    {
        try
        {
            var skel = node.skeletonSprite;
            var t = skel != null ? skel.transform : null;
            var existing = t != null ? t.Find(LogoChildName) : null;
            if (existing == null) return;
            var sr = existing.gameObject.GetComponent<SpriteRenderer>();
            if (sr == null || !sr.enabled) return;
            sr.enabled = false;
            Plugin.Log.LogInfo("[NodeArt] retired the overlay sprite — the node's own icon now carries the logo.");
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[NodeArt] RetireOverlay: {ex.Message}"); }
    }

    private static void ApplyNodeLogo(STS.Map.MatchMapNode node, TeamData team)
    {
        try
        {
            if (node == null || team == null) return;

            // Preferred route: the node's own 'rewardIcon_image'. If it's already
            // present at map-generation time we're done — no overlay needed at all.
            // If it isn't yet, the late pass (EnsureNodeArt) catches it, and the
            // overlay below covers the gap meanwhile.
            if (ReplaceRewardIcon(node, team)) { RetireOverlay(node); return; }

            UnityEngine.Sprite sprite = null;
            try { sprite = team.logo; } catch { }
            if (sprite == null)
            {
                Plugin.Log.LogInfo($"[NodeArt] '{team.teamName}' has no logo sprite — leaving the vanilla mascot alone.");
                return;
            }

            var skel = node.skeletonSprite;
            if (skel == null) { Plugin.Log.LogInfo("[NodeArt] node has no skeletonSprite — nothing to replace."); return; }
            var graphic = skel.gameObject;
            var rend = graphic.GetComponent<Renderer>();
            if (rend == null) { Plugin.Log.LogInfo("[NodeArt] node graphic has no Renderer — skipping."); return; }

            // 'NodeGraphic' is ONE Spine skeleton drawing the whole node — stadium
            // AND team mascot together. Disabling it (which an earlier build did)
            // takes the stadium with it. The mascot cannot be separated out: that
            // would mean Spine skin surgery, which is exactly what crashed the game
            // in session 12. So the skeleton stays visible and the logo is placed
            // over the mascot's patch of it. Self-heal anything the earlier build
            // switched off.
            if (!rend.enabled)
            {
                rend.enabled = true;
                Plugin.Log.LogInfo($"[NodeArt] re-enabled node graphic '{graphic.name}' (an earlier build hid it).");
            }

            var b = rend.bounds;
            if (b.size.y <= 0.0001f)
            {
                Plugin.Log.LogWarning($"[NodeArt] node graphic '{graphic.name}' has degenerate bounds — skipping.");
                return;
            }

            // Where on the node does the team marker sit? The Flag object is the
            // post-match banner and is planted at the same spot as the mascot, so
            // its bounds are a far better anchor than anything we could estimate.
            // Fall back to the top-centre of the skeleton, where the mascot is
            // drawn, when the flag isn't measurable (it's hidden until the match
            // is over, so this fallback is the common path).
            Vector3 center;
            float targetHeight;
            string anchor;
            Bounds flagBounds = default;
            bool haveFlag = false;
            try
            {
                var flag = node.transform.Find("Flag/Container/flag_base");
                var fr = flag != null ? flag.GetComponent<Renderer>() : null;
                if (fr != null)
                {
                    flagBounds = fr.bounds;
                    haveFlag = flagBounds.size.y > 0.0001f;
                }
            }
            catch { }

            targetHeight = b.size.y * MascotHeightFraction;
            center = new Vector3(b.center.x, b.center.y + b.size.y * MascotCentreOffset, b.center.z);
            anchor = "mascot-estimate";

            // Logged every time: if the logo lands in the wrong place these are the
            // numbers to correct the two constants with, without another probe run.
            Plugin.Log.LogInfo($"[NodeArt] anchor={anchor} graphic(centre={b.center} size={b.size})"
                + $" flag(valid={haveFlag} centre={flagBounds.center} size={flagBounds.size})"
                + $" -> logo centre={center} height={targetHeight:F2}");

            var t = graphic.transform;
            var existing = t.Find(LogoChildName);
            GameObject go;
            SpriteRenderer sr;
            bool isNew = existing == null;
            if (!isNew)
            {
                go = existing.gameObject;
                sr = go.GetComponent<SpriteRenderer>();
                if (sr == null) sr = go.AddComponent<SpriteRenderer>();
            }
            else
            {
                go = new GameObject(LogoChildName);
                go.transform.SetParent(t, false);
                sr = go.AddComponent<SpriteRenderer>();
            }

            sr.sprite = sprite;
            try
            {
                sr.sortingLayerID = rend.sortingLayerID;
                sr.sortingOrder = rend.sortingOrder + 5;   // in front of the mascot
            }
            catch { }

            // Position and size ONCE, on the pass that creates the overlay. Hiding
            // the mascot below shrinks the skeleton's bounds to just the stadium,
            // so re-measuring on a later pass would walk the logo down the pole.
            if (isNew)
            {
                // Match the mascot's on-screen height. Sprite PPU varies (custom
                // PNGs are created at 100, repository logos are whatever the game
                // shipped), so measure rather than assume.
                float spriteHeight = 0f;
                try { spriteHeight = sprite.bounds.size.y; } catch { }
                float parentScale = 1f;
                try { parentScale = t.lossyScale.y; } catch { }
                if (spriteHeight > 0.0001f && parentScale > 0.0001f)
                {
                    float s = targetHeight / (spriteHeight * parentScale);
                    go.transform.localScale = new Vector3(s, s, 1f);
                }
                go.transform.rotation = Quaternion.identity;
                go.transform.position = center;
            }


            Plugin.Log.LogInfo($"[NodeArt] '{team.teamName}': logo '{sprite.name}' placed on"
                + $" '{graphic.name}' via {anchor} anchor (height {targetHeight:F2}, order {sr.sortingOrder}).");
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[NodeArt] ApplyNodeLogo: {ex.Message}"); }
    }

    // Some configs cannot be resolved this early and must wait for the launch path.
    // `Import Team = PLAYER` (mirror match) copies the player's team out of
    // TeamSelection, which isn't populated while the map is being built — applying
    // it here would find nothing, log "Import team 'PLAYER' not found", and then be
    // skipped at launch because the node looked configured. Leave it vanilla on the
    // map and let PatchBoss/EliteLaunchMatch do it, as before this change.
    internal static bool MustWaitForLaunch(int gameIndex, out string reason)
    {
        reason = null;
        if (gameIndex < 0 || gameIndex >= Plugin.ConfigTeams.Count) return false;
        return MustWaitForLaunchConfig(Plugin.ConfigTeams[gameIndex], out reason);
    }

    /// <summary>The same test against a config chosen by node assignment, which has
    /// no game index to look up. Node-assigned mirror matches have to defer for
    /// exactly the same reason.</summary>
    internal static bool MustWaitForLaunchConfig(TeamConfig cfg, out string reason)
    {
        reason = null;
        if (cfg == null || !cfg.IsImport || string.IsNullOrEmpty(cfg.ImportTeam)) return false;
        if (cfg.ImportTeam.Trim().Equals("PLAYER", StringComparison.OrdinalIgnoreCase))
        {
            reason = "mirror match (Import Team = PLAYER); the player's team isn't readable during map generation";
            return true;
        }
        return false;
    }

    // Primary hook: MapObject.SetOpponents(int act) is by definition the point at
    // which every elite and boss node has been given its TeamData, whoever called
    // it. Preferred over InitializeMap, which we cannot prove from the dump does
    // the assignment itself (its calls are indirect, so disassembly names nothing).
    public static void AfterSetOpponents(STS.Map.MapObject __instance)
    {
        Run(__instance, "SetOpponents");
    }

    // Backup hook, in case SetOpponents got inlined and the patch above no-ops.
    // Costs nothing when the primary already ran — every node reports as current.
    public static void Postfix(STS.Map.MapObject __instance)
    {
        Run(__instance, "InitializeMap");
    }

    private static void Run(STS.Map.MapObject __instance, string why)
    {
        try
        {
            if (__instance == null || Plugin.IsDefaultMode) return;
            var nodes = __instance.MapNodes;
            if (nodes == null) return;

            // Collect campaign match nodes with their layer.
            var layers = new List<int>();
            var byLayer = new Dictionary<int, List<STS.Map.MatchMapNode>>();
            int challengesSkipped = 0;
            for (int i = 0; i < nodes.Count; i++)
            {
                var mn = nodes[i];
                if (mn == null) continue;
                STS.Map.MatchMapNode match = null;
                try { match = mn.TryCast<STS.Map.MatchMapNode>(); } catch { }
                if (match == null) continue;
                if (IsChallengeNode(match)) { challengesSkipped++; continue; }

                int layer;
                try { layer = mn.Node != null ? mn.Node.LayerIndex : -1; } catch { layer = -1; }
                if (layer < 0) continue;

                if (!byLayer.TryGetValue(layer, out var list))
                {
                    list = new List<STS.Map.MatchMapNode>();
                    byLayer[layer] = list;
                    layers.Add(layer);
                }
                list.Add(match);
            }

            if (layers.Count == 0)
            {
                Plugin.Log.LogInfo($"[MapTeams] {why}: no campaign match nodes on this map (challenge nodes skipped: {challengesSkipped})");
                return;
            }
            layers.Sort();

            // Anchor to where the campaign stood when this map was generated, not
            // to the live GamesPlayed — otherwise every return to the map (each
            // InitializeMap) would shift every node forward by the match just won.
            int baseGames = Plugin.MapStartGamesPlayed;
            if (baseGames < 0 || baseGames > Plugin.GamesPlayed) baseGames = Plugin.GamesPlayed;

            // Two nodes on DIFFERENT layers can only hold different teams if they
            // hold different TeamData objects. ActElite.team / ActBoss.team are
            // direct ScriptableObject references, so a repeat is possible; writing
            // both would make the earlier node display the later node's team.
            // First layer wins, and we say so loudly — the launch path still
            // corrects the match itself when the player gets there.
            var claimedBy = new Dictionary<int, int>();
            int applied = 0, shared = 0, alreadyDone = 0, deferred = 0;
            var summary = new StringBuilder();

            for (int k = 0; k < layers.Count; k++)
            {
                int layer = layers[k];
                int gameIndex = baseGames + k;
                foreach (var node in byLayer[layer])
                {
                    TeamData opp = null;
                    try { opp = node.opponent; } catch { }
                    if (opp == null) continue;

                    // ── Per-node assignment (assignments.txt) ──────────────────
                    // Checked BEFORE the sequential path, and independently per
                    // node, so two branch siblings on one layer can hold different
                    // teams — the thing the game-numbered model could never express.
                    //
                    // Node coordinates come from the runtime node itself
                    // (STS.Map.Node exposes LayerIndex and NodeIndex), which is what
                    // _game_maps.txt lists, so the Creator and the game agree on
                    // what "Layer 2 / Node 1" means without any position matching.
                    if (Plugin.NodeAssignments.Count > 0)
                    {
                        int nodeIdx = -1;
                        try { nodeIdx = node.Node != null ? node.Node.NodeIndex : -1; } catch { }
                        if (nodeIdx >= 0)
                        {
                            string nkey = Plugin.NodeKey(Plugin.ActsCompleted + 1, layer, nodeIdx);
                            if (Plugin.NodeAssignments.TryGetValue(nkey, out string teamKey))
                            {
                                var acfg = Plugin.FindConfigTeamByKey(teamKey);
                                if (acfg == null)
                                {
                                    Plugin.Log.LogWarning($"[Assign] {nkey}: no team named '{teamKey}' in teams/"
                                        + " — leaving this node vanilla rather than guessing.");
                                }
                                else if (MustWaitForLaunchConfig(acfg, out string awaitWhy))
                                {
                                    // Same PLAYER-import deferral the sequential path
                                    // needs: TeamSelection isn't populated during map
                                    // generation, so a mirror match must wait.
                                    deferred++;
                                    Plugin.Log.LogInfo($"[Assign] {nkey} left vanilla on the map — {awaitWhy}.");
                                    continue;
                                }
                                else
                                {
                                    if (CampaignOpponents.EnsureAssigned(opp, acfg, nkey, $"map gen {nkey}"))
                                    {
                                        applied++;
                                        summary.Append($" L{layer}.N{nodeIdx}=>'{opp.teamName}'");
                                        ApplyNodeLogo(node, opp);
                                    }
                                    else alreadyDone++;
                                    continue;   // assigned nodes never fall through
                                }
                            }
                        }
                    }

                    if (MustWaitForLaunch(gameIndex, out string wait))
                    {
                        deferred++;
                        Plugin.Log.LogInfo($"[MapTeams] Layer {layer} (game {gameIndex + 1}) left vanilla on the map — {wait}.");
                        continue;
                    }

                    int id;
                    try { id = opp.GetInstanceID(); } catch { continue; }
                    if (claimedBy.TryGetValue(id, out int ownerLayer) && ownerLayer != layer)
                    {
                        shared++;
                        Plugin.Log.LogWarning($"[MapTeams] Layer {layer} shares its TeamData with layer {ownerLayer}"
                            + $" — leaving it as game {baseGames + layers.IndexOf(ownerLayer) + 1}'s team."
                            + " The match itself will still be corrected at launch.");
                        continue;
                    }
                    claimedBy[id] = layer;

                    // Capture the vanilla name BEFORE the config overwrites it —
                    // it's how the node-art probe recognises team-specific art.
                    string vanillaName = null;
                    try { vanillaName = opp.teamName; } catch { }

                    // Check before calling so a second pass over the same map (we
                    // hook both SetOpponents and InitializeMap) stays quiet.
                    if (CampaignOpponents.AppliedGameIndex(opp) == gameIndex) { alreadyDone++; continue; }

                    if (!_probedNodeVisuals) ProbeNodeVisuals(node, vanillaName);

                    if (CampaignOpponents.Ensure(opp, gameIndex, $"map gen (layer {layer})")) applied++;
                    summary.Append($" L{layer}=>G{gameIndex + 1}('{opp.teamName}')");

                    // opp.logo is populated by the apply above, so this covers both
                    // custom PNGs and logos borrowed from another team.
                    ApplyNodeLogo(node, opp);
                }
            }

            // One map's worth of node-art detail is enough to work from; don't
            // spam it on every map for the rest of the session.
            if (applied > 0) _probedNodeVisuals = true;

            // A pass that changed nothing is the normal second hook firing; only
            // the first pass over a map should report applications.
            if (applied > 0 || shared > 0)
            {
                Plugin.Log.LogInfo($"[MapTeams] Map configured from game {baseGames + 1} ({why}): {layers.Count} match layer(s),"
                    + $" {applied} team(s) applied, {alreadyDone} already current, {shared} shared-TeamData conflict(s),"
                    + $" {deferred} deferred to launch, {challengesSkipped} challenge node(s) left vanilla.");
                if (summary.Length > 0)
                    Plugin.Log.LogInfo($"[MapTeams]{summary}");
            }
            else
            {
                Plugin.Log.LogInfo($"[MapTeams] {why}: nothing to do — {alreadyDone} node(s) already hold their config team.");
            }
        }
        catch (Exception ex) { Plugin.Log.LogError($"[MapTeams] {why} opponent pass: {ex}"); }
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
    private static int CountRelics(Il2CppSystem.Collections.Generic.List<Rogue.Relic> list)
    {
        try { return list != null ? list.Count : 0; } catch { return 0; }
    }

    // Per-category sizes, so a [RewardPool] line shows how big the pool for the
    // category being asked for actually is (vs the ~325 entries the editor lists).
    private static string DescribeCategories(RelicRepository repo)
    {
        try
        {
            return $"off={CountRelics(repo.offensiveRelics)} def={CountRelics(repo.defensiveRelics)}"
                 + $" util={CountRelics(repo.utilityRelics)} spd={CountRelics(repo.speedRelics)}"
                 + $" chk={CountRelics(repo.checkingRelics)} pwr={CountRelics(repo.powerRelics)}"
                 + $" acc={CountRelics(repo.accuracyRelics)} chaos={CountRelics(repo.chaosRelics)}"
                 + $" boss={CountRelics(repo.bossRelics)} goalie={CountRelics(repo.goalieRelics)}"
                 + $" coach={CountRelics(repo.coachRelics)}";
        }
        catch { return "unreadable"; }
    }

    // Ids the prefilter actually managed to exclude on the most recent call.
    // The postfix must strip ONLY these — using the full exclusion list there
    // re-applies the ones the budget deliberately skipped and guts the result
    // (seen as "3 -> 1"), which defeats the whole point of the budget.
    internal static readonly HashSet<string> AppliedExclusions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    // PREFIX: add our excluded ids to the `excludedRelics` arg that the game
    // already passes in, so GetRandomRelics NEVER picks them in the first
    // place. Much safer than a postfix strip — the game would return empty
    // ("Rogue rewards have invalid data") if every picked relic got filtered.
    public static void Prefix(
        Il2CppSystem.Collections.Generic.List<Rogue.Relic> excludedRelics,
        RelicRepository __instance,
        int numberOfRelics,
        RelicCategory relicCategory,
        bool mustBeInPool,
        bool matchAnyCategory)
    {
        try
        {
            // Diagnostic, runs even with no exclusions configured: GetRandomRelics
            // is category-scoped, so the pool the game actually draws from is not
            // the flat catalogue the reward-pool editor lists. Log what each call
            // asks for, per node type, before deciding anything about that editor.
            if (__instance != null)
                Plugin.Log.LogInfo($"[RewardPool] GetRandomRelics: category={relicCategory}"
                    + $" want={numberOfRelics} matchAny={matchAnyCategory} mustBeInPool={mustBeInPool}"
                    + $" alreadyExcluded={(excludedRelics != null ? excludedRelics.Count : -1)}"
                    + $" node='{Plugin.LastMatchNodeKind}' | repo totals: {DescribeCategories(__instance)}");

            if (Plugin.ExcludedRewardRelicIds.Count == 0) return;
            if (__instance == null || excludedRelics == null) return;

            // Never exclude the pool down to less than the game asked for. The
            // game NREs on an empty/short result — a campaign that excludes 283
            // of 304 relics froze the run at game end, because its own category
            // and already-owned filters run on top of ours and emptied it. Honour
            // as many exclusions as fit and drop the rest with a loud warning:
            // a slightly wider reward pool beats a run that can't continue.
            // GetAllRelics() returns an interop IReadOnlyList that exposes no
            // Count and does not cast to IEnumerable — it counted 0, which made
            // the budget below infinite and silently disabled this whole guard.
            // The category lists are plain Il2Cpp Lists with a working Count,
            // and are what the reward dump already walks.
            int total = CountRelics(__instance.offensiveRelics)
                      + CountRelics(__instance.defensiveRelics)
                      + CountRelics(__instance.utilityRelics)
                      + CountRelics(__instance.speedRelics)
                      + CountRelics(__instance.checkingRelics)
                      + CountRelics(__instance.powerRelics)
                      + CountRelics(__instance.accuracyRelics)
                      + CountRelics(__instance.chaosRelics)
                      + CountRelics(__instance.bossRelics)
                      + CountRelics(__instance.goalieRelics)
                      + CountRelics(__instance.coachRelics);
            // Keep a working margin above the request for the game's own filters.
            int want = numberOfRelics > 0 ? numberOfRelics : 3;
            int mustKeep = want + 6;
            if (total <= 0)
            {
                // Can't size the pool — applying exclusions blind is what froze
                // runs at game end, so skip filtering rather than risk it.
                Plugin.Log.LogWarning("[RewardPool] Relic pool size unknown — skipping exclusions this call"
                    + " so rewards can still be generated.");
                return;
            }
            int budget = Math.Max(0, total - excludedRelics.Count - mustKeep);

            AppliedExclusions.Clear();
            int added = 0, skipped = 0;
            foreach (var id in Plugin.ExcludedRewardRelicIds)
            {
                if (string.IsNullOrEmpty(id)) continue;
                if (added >= budget) { skipped++; continue; }
                try
                {
                    var relic = __instance.GetRelic(id, false);
                    if (relic == null) continue;
                    bool present = false;
                    for (int j = 0; j < excludedRelics.Count; j++)
                        if (excludedRelics[j] != null && excludedRelics[j].id == id) { present = true; break; }
                    if (!present) { excludedRelics.Add(relic); added++; }
                    AppliedExclusions.Add(id);
                }
                catch { }
            }
            if (added > 0) Plugin.Log.LogInfo($"[RewardPool] Relic prefilter: added {added} exclusions to GetRandomRelics call (pool {total}, asked for {want})");
            if (skipped > 0)
                Plugin.Log.LogWarning($"[RewardPool] {skipped} relic exclusion(s) IGNORED — the campaign excludes too many"
                    + $" ({Plugin.ExcludedRewardRelicIds.Count} of {total}) to leave {want} pickable rewards."
                    + " Re-enable some relics in the campaign's reward pool editor.");
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
            // Diagnostic companion to the prefix line — what the category-scoped
            // call actually yielded.
            try
            {
                var names = new StringBuilder();
                if (__result != null)
                    for (int i = 0; i < __result.Count && i < 12; i++)
                        names.Append(i == 0 ? "" : ", ").Append(__result[i]?.id ?? "null");
                Plugin.Log.LogInfo($"[RewardPool] GetRandomRelics returned {(__result != null ? __result.Count : -1)}: {names}");
            }
            catch { }

            if (__result == null || __result.Count == 0) return;
            if (AppliedExclusions.Count == 0) return;
            int before = __result.Count;
            for (int i = __result.Count - 1; i >= 0 && __result.Count > 1; i--)
            {
                var r = __result[i];
                if (r == null) continue;
                // Only ids the prefilter actually honoured — see AppliedExclusions.
                if (AppliedExclusions.Contains(r.id ?? ""))
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
        TalentRepository __instance,
        int numberOfTalents)
    {
        try
        {
            if (Plugin.ExcludedRewardTalentIds.Count == 0) return;
            if (__instance == null || excludedTalents == null) return;

            // Same failure mode as relics: exclude the pool down past what the
            // game asked for and it NREs on the empty result, freezing the run.
            int total = 0;
            try { total = __instance.talents != null ? __instance.talents.Count : 0; } catch { }
            int want = numberOfTalents > 0 ? numberOfTalents : 3;
            int mustKeep = want + 6;
            if (total <= 0)
            {
                Plugin.Log.LogWarning("[RewardPool] Talent pool size unknown — skipping exclusions this call"
                    + " so rewards can still be generated.");
                return;
            }
            int budget = Math.Max(0, total - excludedTalents.Count - mustKeep);

            int added = 0, skipped = 0;
            foreach (var id in Plugin.ExcludedRewardTalentIds)
            {
                if (string.IsNullOrEmpty(id)) continue;
                if (added >= budget) { skipped++; continue; }
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
            if (added > 0) Plugin.Log.LogInfo($"[RewardPool] Talent prefilter: added {added} exclusions to GetRandomTalents call (pool {total}, asked for {want})");
            if (skipped > 0)
                Plugin.Log.LogWarning($"[RewardPool] {skipped} talent exclusion(s) IGNORED — the campaign excludes too many"
                    + $" ({Plugin.ExcludedRewardTalentIds.Count} of {total}) to leave {want} pickable rewards."
                    + " Re-enable some talents in the campaign's reward pool editor.");
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[RewardPool] Talent prefilter: {ex.Message}"); }
    }

    // GetRandomTalents crashes with NullReferenceException when forwardData is
    // null (game bug during PreGenerateFreeAgents on title screen). Swallow it.
    public static Exception Finalizer(Exception __exception)
    {
        if (__exception is NullReferenceException)
            return null; // suppress — return empty list naturally
        return __exception;
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
            LastOutput = __2;
            int templatesCount = __1?.Count ?? -1;
            int outCount = __2?.Count ?? -1;
            Plugin.Log.LogInfo($"[Campaign] PreGenerateFreeAgents postfix — templates={templatesCount} output={outCount}");
            if (Plugin.DraftPoolConfigs != null && Plugin.DraftPoolConfigs.Count > 0)
                PatchPlayerTeamInit.ApplyDraftPool();
            PatchPlayerTeamInit.ApplyFreeAgentPool();
        }
        catch (Exception ex) { Plugin.Log.LogError($"[Campaign] PreGenerateFreeAgents: {ex.Message}"); }
    }

    // PreGenerateFreeAgents crashes with NullReferenceException when it calls
    // GetRandomTalents with a null ForwardData (game bug on title screen load).
    // The postfix never runs in that case, so we suppress here instead.
    public static Exception Finalizer(Exception __exception)
    {
        if (__exception is NullReferenceException)
        {
            Plugin.Log.LogInfo("[Campaign] PreGenerateFreeAgents: suppressed NullReferenceException (null ForwardData on initial load)");
            return null;
        }
        return __exception;
    }
}

// ============================================================
// Custom superstar pool. OldSquadMenu.GenerateSuperStarSkaters() returns
// the List<ForwardData> shown on the "pick a superstar out of 3" screen
// (after squad select, before the bench step). We overwrite each shown
// slot with a clone of a real superstar customized from the user's
// player_teams/superstars/ pool (cycled to fill all shown slots), so the
// options become the editor-defined superstars. Empty pool = vanilla.
// ============================================================
public static class PatchSuperstarPool
{
    public static void Postfix(Il2CppSystem.Collections.Generic.List<Data.ForwardData> __result)
    {
        try
        {
            if (Plugin.SuperstarPoolList == null || Plugin.SuperstarPoolList.Count == 0) return;
            if (__result == null || __result.Count == 0)
            {
                Plugin.Log.LogInfo("[Superstar] GenerateSuperStarSkaters returned no templates — nothing to replace");
                return;
            }
            int N = Plugin.SuperstarPoolList.Count;
            for (int i = 0; i < __result.Count; i++)
            {
                var template = __result[i];
                if (template == null) continue;
                var cfg = Plugin.SuperstarPoolList[i % N];
                var clone = UnityEngine.Object.Instantiate(template);
                PatchPlayerTeamInit.ApplyConfigToForward(clone, cfg, applyName: true);
                __result[i] = clone;
                Plugin.Log.LogInfo($"[Superstar] Slot {i} → '{cfg.Name}' (cycled {i % N + 1}/{N})");
            }
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[Superstar] postfix: {ex.Message}"); }
    }
}

// ============================================================
// Capture which superstar the player clicked. Stored so the run-start
// hook can append it to the team's fwds (the game otherwise drops the
// pick for Custom_* squads).
// ============================================================
public static class PatchOldSquadMenuSuperstar
{
    internal static Data.ForwardData PickedSuperstar;

    public static void OnClickSuperStarPostfix(Tape2Tape.Hockey.UI.OldSquadMenu __instance, BishopGames.UI.AbstractNavigable __0)
    {
        try
        {
            if (__instance == null || __0 == null) return;
            var t = __instance.GetIl2CppType();
            var bf = Il2CppSystem.Reflection.BindingFlags.NonPublic
                   | Il2CppSystem.Reflection.BindingFlags.Instance
                   | Il2CppSystem.Reflection.BindingFlags.Public;

            // Match nav pointer against m_SuperStarParent's child navigables
            // (read m_Navigables List directly — the IReadOnlyList interop
            // wrapper lacks .Count). Then read the parallel m_SuperStarSkaters[idx].
            var parentField = t.GetField("m_SuperStarParent", bf);
            var parent = parentField?.GetValue(__instance)?.TryCast<BishopGames.UI.NavigableParent>();
            if (parent == null) return;
            var navsField = parent.GetIl2CppType().GetField("m_Navigables", bf);
            var navsList = navsField?.GetValue(parent)?.TryCast<Il2CppSystem.Collections.Generic.List<BishopGames.UI.AbstractNavigable>>();
            if (navsList == null) return;
            int idx = -1;
            for (int i = 0; i < navsList.Count; i++)
            {
                var n = navsList[i];
                if (n != null && n.Pointer == __0.Pointer) { idx = i; break; }
            }
            if (idx < 0) return;

            var ssList = t.GetField("m_SuperStarSkaters", bf)?.GetValue(__instance)
                ?.TryCast<Il2CppSystem.Collections.Generic.List<Data.ForwardData>>();
            if (ssList == null || idx >= ssList.Count) return;

            PickedSuperstar = ssList[idx];
            Plugin.Log.LogInfo($"[Superstar] Captured pick idx={idx} → '{PickedSuperstar?.firstName} {PickedSuperstar?.lastName}'");
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[Superstar] OnClickSuperStarPostfix: {ex.Message}"); }
    }
}

// ============================================================
// Run-start hook for custom squads: reconcile drafted picks (their IDs are in
// lines[0] but ForwardData isn't in fwds) and append the captured superstar.
// No bump/move/reshuffle — reconcile fills the line slot; superstar appends to
// the end (bench). The base game's flow + these two helpers = picks visible
// on the run team. For vanilla squads this is a no-op (early return on id).
// ============================================================
public static class PatchInstantiateStartingTeam
{
    public static void Postfix(Data.TeamData __result, RunSquadScriptableObject __0)
    {
        try
        {
            if (__result == null) return;
            string squadId = "";
            try { squadId = __0?.id ?? ""; } catch { }
            bool isCustom = squadId.StartsWith("Custom_", StringComparison.Ordinal);

            // DIAGNOSTIC: capture vanilla squads too (especially Basic Squad) so
            // we have a base-game baseline for how InstantiateStartingTeam shapes
            // the run team before the player picks a superstar / drafts.
            if (!isCustom)
            {
                string vnm = ""; try { vnm = __result.teamName ?? ""; } catch { }
                string vsq = ""; try { vsq = __0?.squadName ?? ""; } catch { }
                if (vsq.IndexOf("Basic", StringComparison.OrdinalIgnoreCase) >= 0
                    || vnm.IndexOf("Basic", StringComparison.OrdinalIgnoreCase) >= 0)
                    PatchChooseMetaUI.DumpTeamStructure(__result, "VANILLA-Basic@InstStart");
                return;
            }

            Plugin.Log.LogInfo($"[InstStartTeam] postfix: team='{__result.teamName}' squadId='{squadId}' fwds.Count={__result.forwards?.Count ?? -1}");

            // ── RUNTIME INTERFERENCE DISABLED (diagnostic pass) ──────────────
            // We confirmed the game's NATIVE flow already adds the picked
            // superstar to the run team on its own (it placed the superstar at
            // forwards[0] last run with none of our hooks doing it). Our old
            // steps here — unlock all positions, reconcile drafted FAs, force the
            // superstar to center, and rewrite lines[0] positionally — fought
            // that native flow and produced a garbage line. Disable them all so
            // this run shows what the game does unaided; the DUMP lines below
            // capture the result. (Old code retained under `if (false)` so the
            // exact previous behavior is one edit away if the dumps say we need
            // some of it back.)
            PatchChooseMetaUI.DumpTeamStructure(__result, "InstStart");

            // 2) Place the picked superstar at CENTER (slot 2) — the same way
            //    goalie is set to the goalie slot and the configured D is set
            //    to LD. If something else is already at C (a drafted pick),
            //    bump it to the bench so the superstar gets the C spot. Also
            //    update lines[0].center so the line renders the superstar at C.
            var ss = PatchOldSquadMenuSuperstar.PickedSuperstar;
            if (false && ss != null && __result.forwards != null)   // DISABLED — native flow handles superstar
            {
                const int CENTER = 2;
                try
                {
                    // Ensure slot 2 exists.
                    while (__result.forwards.Count <= CENTER) __result.forwards.Add(null);

                    // Already at C? Skip.
                    var atC = __result.forwards[CENTER];
                    bool alreadyAtC = atC != null
                        && (atC.Pointer == ss.Pointer
                            || (!string.IsNullOrEmpty(atC.id) && !string.IsNullOrEmpty(ss.id) && atC.id == ss.id));
                    if (alreadyAtC)
                    {
                        Plugin.Log.LogInfo($"[InstStartTeam] Superstar '{ss.firstName} {ss.lastName}' already at Center — no change");
                    }
                    else
                    {
                        if (atC != null)
                        {
                            try { __result.forwards.Add(atC); } catch { }
                            Plugin.Log.LogInfo($"[InstStartTeam] Bumped '{atC.firstName} {atC.lastName}' from Center → fwds[{__result.forwards.Count - 1}] (bench) to make room for superstar");
                        }
                        __result.forwards[CENTER] = ss;

                        // Update lines[0].center so the line slot resolves to the
                        // superstar's id instead of whatever draft pick was there.
                        try
                        {
                            var lns = __result.lines;
                            if (lns != null && lns.Count > 0 && lns[0] != null)
                                lns[0].center = ss.id ?? "";
                        }
                        catch { }

                        Plugin.Log.LogInfo($"[InstStartTeam] Placed superstar '{ss.firstName} {ss.lastName}' at Center (slot {CENTER})");
                    }
                }
                catch (Exception ex) { Plugin.Log.LogWarning($"[InstStartTeam] superstar place: {ex.Message}"); }
            }

            // 3) Force lines[0] to reference the forwards actually sitting in
            //    the starting-five slots (fwds[0..4]). The cloned Basic template
            //    leaves lines[0] pointing at Basic's defunct forward ids — those
            //    ghost ids don't resolve to any ForwardData on this team, so the
            //    game's on-ice line builder finds nothing and the picked
            //    superstar + drafted/configured players never take the ice. This
            //    is NOT a reshuffle: each forward stays in the exact slot it's
            //    already in; we only rewrite the line's id pointers to match.
            try
            {
                var fwds = __result.forwards;
                var lns = __result.lines;
                if (false && fwds != null && lns != null && lns.Count > 0 && lns[0] != null)   // DISABLED — positional line sync clobbered native draft
                {
                    var l0 = lns[0];
                    string IdAt(int i) => (i < fwds.Count && fwds[i] != null) ? (fwds[i].id ?? "") : null;
                    string lw = IdAt(0), rw = IdAt(1), c = IdAt(2), ld = IdAt(3), rd = IdAt(4);
                    if (lw != null) l0.leftWinger = lw;
                    if (rw != null) l0.rightWinger = rw;
                    if (c  != null) l0.center = c;
                    if (ld != null) l0.leftDefensemen = ld;
                    if (rd != null) l0.rightDefensemen = rd;
                    Plugin.Log.LogInfo($"[InstStartTeam] Synced lines[0] to roster: LW='{l0.leftWinger}' RW='{l0.rightWinger}' C='{l0.center}' LD='{l0.leftDefensemen}' RD='{l0.rightDefensemen}'");
                }
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"[InstStartTeam] line sync: {ex.Message}"); }
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[InstStartTeam] {ex.Message}"); }
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
// The pre-match preview ("ELITE GAME vs MEATBALLS") is built from
// MatchMapNode.opponent. Since PatchMapOpponents configures that TeamData at map
// generation, the screen normally draws the right team on its own — name, logo,
// skater models and colour swatches included, without this patch touching any of
// them.
//
// Two jobs remain here:
//  1. Last-chance correction. The previewed node is the match that is about to
//     start, so its opponent must stand for ConfigTeams[GamesPlayed]. If the map
//     pass mispredicted (a mixed layer where the player took a rest node instead
//     of a match, so fewer games were played than match layers passed) this puts
//     it right BEFORE the player commits, not at launch.
//  2. Repaint name + logo. ShowMenu has already run by the time we get here, so a
//     correction in (1) would otherwise not reach the widgets it already filled.
//
// Public fields only — no reflection into private members, no boxed structs
// (see the 0xC0000005 warning at PatchChooseMetaUI.FaceToSprite).
// ============================================================
public static class PatchMatchPreviewMenu
{
    public static void Postfix(MatchPreviewMenu __instance)
    {
        try
        {
            if (__instance == null || Plugin.IsDefaultMode) return;
            int next = Plugin.GamesPlayed;
            if (next < 0) return;

            // 1. Make sure the node we're previewing holds this game's team.
            TeamData opp = null;
            try
            {
                var node = __instance.currentMapNode;
                var match = node != null ? node.TryCast<STS.Map.MatchMapNode>() : null;
                if (match != null) opp = match.opponent;
            }
            catch { }
            // A mirror match still has to wait for LaunchMatch — TeamSelection may
            // not be readable yet, and a failed attempt here would mark the node
            // configured and stop the launch path from doing it properly.
            bool waitForLaunch = PatchMapOpponents.MustWaitForLaunch(next, out _);
            if (opp != null && !waitForLaunch
                && CampaignOpponents.Ensure(opp, next, "MatchPreviewMenu.ShowMenu"))
                Plugin.Log.LogInfo($"[Preview] Corrected previewed opponent to game {next + 1} — map generation had it wrong");

            // 2. Repaint the widgets ShowMenu already filled. Prefer the node's
            //    own team so the text can never disagree with what will be played;
            //    fall back to the config when the node isn't readable.
            string shownName = waitForLaunch ? null : opp?.teamName;
            string logoFrom = null;
            if (next < Plugin.ConfigTeams.Count)
            {
                var cfg = Plugin.ConfigTeams[next];
                if (cfg != null)
                {
                    if (string.IsNullOrEmpty(shownName))
                        shownName = !string.IsNullOrEmpty(cfg.Name) ? cfg.Name : cfg.ImportTeam;
                    logoFrom = cfg.LogoFrom;
                }
            }
            if (string.IsNullOrEmpty(shownName)) return;
            // "PLAYER"/"RANDOM" are directives, not team names — never show them.
            if (shownName.Trim().Equals("PLAYER", StringComparison.OrdinalIgnoreCase)
                || shownName.Trim().Equals("RANDOM", StringComparison.OrdinalIgnoreCase)) return;

            if (__instance.teamName != null)
                __instance.teamName.text = shownName;

            if (!string.IsNullOrEmpty(logoFrom))
            {
                var sprite = PatchBossLaunchMatch.LoadCustomLogoSprite(logoFrom);
                if (sprite != null)
                {
                    if (__instance.logo != null) __instance.logo.sprite = sprite;
                    if (__instance.logoDropShadow != null) __instance.logoDropShadow.sprite = sprite;
                }
            }
            Plugin.Log.LogInfo($"[Preview] Match preview -> '{shownName}' (game {next + 1}, logo '{logoFrom}')");
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[Preview] MatchPreviewMenu: {ex.Message}"); }
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
        Plugin.MapStartGamesPlayed = 0;
        Plugin.DraftPoolApplied = false;
        Plugin.AppliedDraftPtrs.Clear();
        Plugin.AppliedFreeAgentPtrs.Clear();
        Plugin.FreeAgentSignedConfigs.Clear();
        CampaignOpponents.ForgetAll("run ended");
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

        // Decide the GM-node layout for the map about to be built. Must happen
        // before CreateMapNode starts, since node TYPE selects the prefab.
        Plugin.RefreshGmSquadActive();
        Plugin.ComputeGmForcedLayers();

        // A brand-new map: anchor node->team assignment to where the campaign
        // stands now, and drop what we knew about the old map's opponents (the
        // same TeamData assets get reused, for different games this time).
        Plugin.MapStartGamesPlayed = Plugin.GamesPlayed;
        Plugin.SaveProgress();
        CampaignOpponents.ForgetAll($"new map generated at game {Plugin.GamesPlayed + 1}");
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
        // Never wrap away from act 3 — boss games always need the game's own
        // act-3 boss list so SetBoss gets a non-null list. Our ApplyManualTeam
        // overwrites the opponent team afterwards. Wrapping 3→1 returns a null
        // or empty list that crashes SetBoss in multi-cycle campaigns where
        // ActsCompleted has looped back to 0.
        if (act == 3) return;

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
                // Normally a no-op: PatchMapOpponents already configured this node
                // at map generation. It still matters when the map-gen pass
                // couldn't run (loaded map, node regenerated by a chaos effect) or
                // when the node's predicted game index drifted from the real one —
                // GamesPlayed is the authority for the match that is about to start.
                if (CampaignOpponents.Ensure(opponent, Plugin.GamesPlayed, "BossMapNode.LaunchMatch"))
                    Plugin.Log.LogInfo($"[Remix] Boss '{opponent.teamName}' configured at launch (game {Plugin.GamesPlayed + 1})");
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
            // Pick the MOST POPULATED instance, not [0]: early in a session
            // (or after a scene unload) FindObjectsOfTypeAll can return a
            // stale/empty repo first, and interop null-checks don't see
            // Unity's fake-null — caching that one made every talent lookup
            // fail forever ("Talent 'Status Extender' not found").
            var t = UnityEngine.Resources.FindObjectsOfTypeAll<TalentRepository>();
            TalentRepository best = null; int bestCount = -1;
            if (t != null)
                foreach (var repo in t)
                {
                    int c = -1;
                    try { c = repo?.talents?.Count ?? -1; } catch { }
                    if (c > bestCount) { best = repo; bestCount = c; }
                }
            CachedTalentRepo = best;
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
        // X-Ray Shot: the Level-1 "xRay" asset was REMOVED in the Jul-2026 game
        // update — only "XRay Shot (Level 2)" remains in the skater talent repo
        // (confirmed via T2T_Dumps/rewards/TALENTS_SKATER.txt). Map every user
        // spelling (and the old asset name) to the surviving Level-2 asset so
        // existing campaign configs keep working.
        { "XRay Shot", "XRay Shot (Level 2)" },
        { "X-Ray Shot", "XRay Shot (Level 2)" },
        { "X Ray Shot", "XRay Shot (Level 2)" },
        // NOTE: the comparer is OrdinalIgnoreCase — one entry covers every
        // casing ("xRay", "XRAY", …); a second casing is a DUPLICATE KEY and
        // crashes the type initializer, killing this whole class at runtime.
        { "XRay", "XRay Shot (Level 2)" },
        { "X-Ray", "XRay Shot (Level 2)" },
    };

    internal static Rogue.Talent FindTalent(string name)
    {
        var found = FindTalentOnce(name);
        if (found != null) return found;
        // Miss: the cached repo may have been grabbed before the game filled
        // it (or belong to an unloaded scene). Re-resolve and retry once.
        CachedTalentRepo = null;
        EnsureRepos();
        return FindTalentOnce(name);
    }

    private static Rogue.Talent FindTalentOnce(string name)
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
        var found = FindRelicOnce(nameContains, level);
        if (found != null) return found;
        // Miss: the cache may predate the game loading all relic assets
        // (same early-lookup race as FindTalent). Rebuild and retry once.
        AllRelicCache = null;
        found = FindRelicOnce(nameContains, level);
        if (found == null)
            Plugin.Log.LogWarning($"[Remix] Relic '{nameContains}' level={level} not found in {AllRelicCache?.Length ?? 0} relics");
        return found;
    }

    private static Rogue.Relic FindRelicOnce(string nameContains, int level = 1)
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

    // Apply the campaign's config for game <paramref name="gameNum"/> to this team.
    // Callers go through CampaignOpponents.Ensure rather than here directly, so the
    // work already done at map generation isn't repeated at launch. Map generation
    // configures nodes ahead of the player, which is why the game index is a
    // parameter instead of being read from the live Plugin.GamesPlayed.
    internal static void RemixTeamForGame(TeamData team, int boost, int gameNum)
    {
        if (Plugin.IsDefaultMode) return; // Default mode = no team modifications
        EnsureRepos();
        ResetClearedPlayers();
        if (boost > 0) BoostTeam(team, boost);

        string origName = team.teamName ?? "";

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

    // Custom logo PNGs the user drops into <persistentDataPath>/CustomLogos/.
    // The GUI's "Logo From" dropdown merges base-game team names with these PNG
    // names; when the chosen value is NOT a base-game team, FindTeamByName fails
    // and we load the PNG here into a Sprite so the team actually shows it
    // in-game. Cached (including misses) so we don't re-read/re-create per frame.
    private static readonly Dictionary<string, UnityEngine.Sprite> _customLogoCache =
        new Dictionary<string, UnityEngine.Sprite>(StringComparer.OrdinalIgnoreCase);

    // The game's own logo repository (built-in logoSprites + custom logos loaded
    // from CustomLogos/). Sprites it returns are RECOGNIZED by the engine — the
    // Spine jersey pipeline only maps logos it recognizes, which is why a
    // hand-made sprite shows in UI but never on jerseys. This is the asset the
    // in-game editor's custom teams use.
    private static UnityEngine.Object _logoRepo;
    private static bool _logoRepoSearched;

    private static string NormLogo(string s) =>
        (s ?? "").Replace(" ", "").Replace("_", "").Replace(".png", "").ToLowerInvariant();

    // One-time dump of what the game's logo repository actually holds.
    // GetLogo() silently falls back to its defaultLogo for ids it doesn't know
    // (that's the 'Teams_TapetoTape' we kept seeing), so a miss on its own says
    // nothing about WHY. This lists every sprite in _allLogos — baked
    // logoSprites plus the custom PNGs the game loads from CustomLogos/ — with
    // the exact name form, which is the key GetLogo matches on.
    private static bool _logoRepoDumped;
    internal static void DumpLogoRepository(Tape2Tape.Customization.TeamAssetsRepositoryScriptableObject repo)
    {
        if (repo == null || _logoRepoDumped) return;
        _logoRepoDumped = true;
        try
        {
            var all = repo.GetAllLogos();
            Plugin.Log.LogInfo($"[LogoDump] GetAllLogos() -> {(all != null ? all.Length : -1)} sprite(s)");
            if (all != null)
                for (int i = 0; i < all.Length; i++)
                {
                    string nm = "null"; int w = 0, h = 0;
                    try
                    {
                        var sp = all[i];
                        if (sp != null)
                        {
                            nm = sp.name;
                            var t = sp.texture;
                            if (t != null) { w = t.width; h = t.height; }
                        }
                    }
                    catch { }
                    Plugin.Log.LogInfo($"[LogoDump]   [{i}] '{nm}' {w}x{h}");
                }
            // What the game SHOULD have picked up, for comparison against the above.
            try
            {
                string dir = Path.Combine(UnityEngine.Application.persistentDataPath, "CustomLogos");
                if (Directory.Exists(dir))
                    Plugin.Log.LogInfo($"[LogoDump] CustomLogos/ on disk: {Directory.GetFiles(dir, "*.png").Length} png");
            }
            catch { }
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[LogoDump] failed: {ex.Message}"); }
    }

    // The game's own loader registers everything in CustomLogos/ under
    // "custom_<file name>.png" — 'custom_Canucks (25-26).png', not
    // 'Canucks (25-26)'. Asking by the bare pack/team name always missed and got
    // handed defaultLogo ('Teams_TapetoTape'), which is why custom logos never
    // rendered on any surface. Try the bare id first (that's the form baked team
    // logos like 'Vancouver' use), then the custom form.
    private static List<string> LogoIdCandidates(string logoId)
    {
        string bare = logoId.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            ? logoId.Substring(0, logoId.Length - 4)
            : logoId;
        return new List<string> { logoId, "custom_" + bare + ".png" };
    }

    internal static UnityEngine.Sprite GetNativeLogo(string logoId)
    {
        try
        {
            if (_logoRepo == null && !_logoRepoSearched)
            {
                _logoRepoSearched = true;
                var repos = UnityEngine.Resources.FindObjectsOfTypeAll<Tape2Tape.Customization.TeamAssetsRepositoryScriptableObject>();
                if (repos != null && repos.Length > 0) _logoRepo = repos[0];
                Plugin.Log.LogInfo($"[CustomLogo] TeamAssetsRepository: {(_logoRepo != null ? "found" : "NOT found")}");
            }
            var repo = _logoRepo as Tape2Tape.Customization.TeamAssetsRepositoryScriptableObject;
            if (repo != null)
            {
                foreach (var candidate in LogoIdCandidates(logoId))
                {
                    var s = repo.GetLogo(candidate);
                    string sn = "null"; try { sn = s != null ? s.name : "null"; } catch { }
                    // Only trust it if the returned sprite actually matches what we
                    // asked for — GetLogo falls back to defaultLogo for unknown ids,
                    // which we must NOT mistake for the requested logo.
                    if (s != null && NormLogo(s.name) == NormLogo(candidate))
                    {
                        Plugin.Log.LogInfo($"[CustomLogo] Native GetLogo('{candidate}') -> '{sn}' (hit)");
                        return s;
                    }
                }
                // Nothing matched under any spelling — dump what the repository
                // actually holds so the mismatch is diagnosable from the log.
                Plugin.Log.LogWarning($"[CustomLogo] No repository logo for '{logoId}' (tried bare + custom_ form)");
                DumpLogoRepository(repo);
            }
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[CustomLogo] GetNativeLogo '{logoId}': {ex.Message}"); }
        return null;
    }

    internal static UnityEngine.Sprite LoadCustomLogoSprite(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        string key = name.Trim();
        if (_customLogoCache.TryGetValue(key, out var cached)) return cached;

        // 1) Prefer the game's OWN recognized logo (so jerseys map it too).
        var native = GetNativeLogo(key);
        if (native != null) { _customLogoCache[key] = native; return native; }

        UnityEngine.Sprite sprite = null;
        try
        {
            string dir = Path.Combine(UnityEngine.Application.persistentDataPath, "CustomLogos");
            string fileName = key.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? key : key + ".png";
            string path = Path.Combine(dir, fileName);
            if (!File.Exists(path))
            {
                // Try the sanitized form the dumper writes (invalid chars -> '_').
                string safe = key;
                foreach (char c in Path.GetInvalidFileNameChars()) safe = safe.Replace(c, '_');
                string altPath = Path.Combine(dir, safe + ".png");
                if (File.Exists(altPath)) path = altPath;
            }
            if (File.Exists(path))
            {
                byte[] data = File.ReadAllBytes(path);
                var tex = new UnityEngine.Texture2D(2, 2, UnityEngine.TextureFormat.RGBA32, false);
                tex.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
                bool ok = UnityEngine.ImageConversion.LoadImage(tex, data);
                if (ok)
                {
                    UnityEngine.Object.DontDestroyOnLoad(tex);
                    sprite = UnityEngine.Sprite.Create(
                        tex,
                        new UnityEngine.Rect(0, 0, tex.width, tex.height),
                        new UnityEngine.Vector2(0.5f, 0.5f),
                        100f);
                    sprite.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
                    UnityEngine.Object.DontDestroyOnLoad(sprite);
                    Plugin.Log.LogInfo($"[CustomLogo] Loaded '{key}' ({tex.width}x{tex.height}) from {path}");
                }
                else Plugin.Log.LogWarning($"[CustomLogo] LoadImage failed for '{path}'");
            }
            else Plugin.Log.LogWarning($"[CustomLogo] No PNG for '{key}' in {dir}");
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[CustomLogo] '{name}': {ex.Message}"); }

        _customLogoCache[key] = sprite;   // cache misses too (null) to avoid re-tries
        return sprite;
    }

    // Make a team's jerseys show a CUSTOM logo the way the in-game team editor
    // does: point every skater's logoSkin at the game's custom-logo Spine slot
    // (Team_Logo/Custom_R for righties, _L for lefties). The game's skin/Spine
    // pipeline (ApplySkin/SetLogo + SetLogoSkinUVsForCustomLogo) then UV-maps
    // the team's custom logo texture (team.alternateBigLogo) onto that slot.
    // Without flipping logoSkin to the custom slot the crest keeps its default
    // (team-specific) slot and ignores the custom texture — which is why setting
    // team.logo/alternateBigLogo alone left jerseys unchanged.
    internal static void ApplyCustomLogoSkinToSkaters(TeamData team)
    {
        if (team == null) return;
        try
        {
            int n = 0;
            var fwds = team.forwards;
            if (fwds != null)
                for (int i = 0; i < fwds.Count; i++)
                {
                    var f = fwds[i];
                    if (f == null) continue;
                    bool lefty = false; try { lefty = f.isLefty; } catch { }
                    try { f.logoSkin = lefty ? "Team_Logo/Custom_L" : "Team_Logo/Custom_R"; n++; } catch { }
                }
            if (team.goalie != null)
                try { team.goalie.logoSkin = "Team_Logo/Custom_R"; n++; } catch { }
            Plugin.Log.LogInfo($"[CustomLogo] Set custom logoSkin on {n} skater(s) of '{team.teamName}'");
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[CustomLogo] logoSkin apply: {ex.Message}"); }
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

    /// <summary>Path -> friendly name for materialised team/player files, with a
    /// guaranteed round trip: a friendly name is only used when it resolves back to
    /// the exact path it came from. Otherwise the raw path is written.
    ///
    /// Without that check the mapping is LOSSY and silently changes a player's look.
    /// The Golfers were the proof: the fallback below matches "golfers" anywhere in
    /// a path, so Faces/Golfers/Golfer_Lady, Golfer_Ramirez, Golfer_Elite,
    /// Golfer_Whacker and Golfer_Gillman all collapsed to "golfers" — a name that
    /// resolves to no face at all. Every golfer came back with a missing head, and
    /// the same trap applies to any team whose name appears in its own asset paths.
    /// </summary>
    internal static string ReverseSkinPath(string path, string slot, bool goalie = false)
    {
        string friendly = ReverseSkinPathRaw(path, slot);
        if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(friendly)) return friendly;
        // If it already IS the path, nothing to verify.
        if (string.Equals(friendly, path, StringComparison.OrdinalIgnoreCase)) return friendly;
        try
        {
            string back = goalie ? Plugin.ResolveGoalieSkin(friendly, slot) : Plugin.ResolveSkin(friendly, slot);
            if (string.Equals(back, path, StringComparison.OrdinalIgnoreCase)) return friendly;
            Plugin.Log.LogInfo($"[Materialize] '{friendly}' would not round-trip for {slot}"
                + $" ('{path}' -> '{back}') — writing the full path instead.");
        }
        catch { }
        return path;
    }

    private static string ReverseSkinPathRaw(string path, string slot)
    {
        if (string.IsNullOrEmpty(path)) return "";
        string lower = path.ToLower();
        if (lower.Contains("customization_colors") || lower.Contains("helmet_colors"))
            return slot == "helmet" ? "team colors" : "standard";
        if (lower.Contains("helmet_face")) return "cage";

        // Goalie masks — Helmet/Helmet_<Name> → friendly name matching GOALIE_HELMET_SKINS
        if (slot == "helmet" && lower.StartsWith("helmet/helmet_"))
        {
            string maskKey = lower.Substring("helmet/helmet_".Length);
            switch (maskKey)
            {
                case "canadians":   return "canadians";
                case "cheese":      return "cheese";
                case "cultists":    return "cultists";
                case "disco":       return "disco";
                case "figure_skaters": return "figure_skaters";
                case "golfers":     return "golfers";
                case "hockeyfc":    return "hockey_fc";
                case "knights":     return "knights";
                case "meatballs":   return "meatballs";
                case "mountaineers": return "mountaineers";
                case "princess":    return "princess";
                case "prisoners":   return "prisoners";
                case "referees":    return "referees";
                case "toronto":     return "toronto";
                case "tycoons":     return "tycoons";
            }
        }

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

            // Copy relics from runtime TeamData first
            NukeRelics(team);
            if (srcTeam.relics != null)
                for (int i = 0; i < srcTeam.relics.Count; i++)
                    if (srcTeam.relics[i] != null)
                        GiveRelic(team, srcTeam.relics[i].name);

            // Apply this team's own relics/abilities/goalie face from cfg.
            // (The import above copies the source team's runtime data, but the
            // source runtime may not have those applied yet — and more importantly,
            // cfg is where the user actually specified relics/abilities for THIS team.)
            if (cfg.Relics != null && cfg.Relics.Count > 0)
            {
                NukeRelics(team);
                foreach (var r in cfg.Relics)
                    if (!string.IsNullOrEmpty(r)) GiveRelic(team, r);
                Plugin.Log.LogInfo($"[Config] Import: applied {cfg.Relics.Count} relics from config");
            }

            // Player abilities + talents by position (0=LW 1=RW 2=C 3=LD 4=RD)
            PlayerConfig[] cfgSlots = { cfg.LW, cfg.RW, cfg.C, cfg.LD, cfg.RD };
            var dstFwds = team.forwards;
            if (dstFwds != null)
            {
                for (int i = 0; i < Math.Min(dstFwds.Count, cfgSlots.Length); i++)
                {
                    if (dstFwds[i] == null || cfgSlots[i] == null) continue;
                    if (!string.IsNullOrEmpty(cfgSlots[i].Ability))
                        SetPlayerAbility(dstFwds[i], cfgSlots[i].Ability);
                    if (cfgSlots[i].Talents != null && cfgSlots[i].Talents.Count > 0)
                    {
                        try { dstFwds[i].powerups = new Il2CppSystem.Collections.Generic.List<Rogue.Talent>(); } catch {}
                        foreach (var t2 in cfgSlots[i].Talents)
                            if (!string.IsNullOrEmpty(t2)) GiveTalentToPlayer(dstFwds[i], t2);
                    }
                }
            }

            // Goalie face intentionally skipped — vanilla goalies use empty headSkin.
            // Applying a skater face path to headSkin renders the goalie headless.

            // Apply stat scale (shared with the manual path — see ApplyStatScale).
            ApplyStatScale(team, cfg);

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
            // Prefer a custom PNG in CustomLogos/ (logo packs). The campaign's
            // own team names collide with pack names (e.g. "Canucks (25-26)")
            // and those campaign teams carry blank/placeholder logos, so
            // resolving the team FIRST applied the wrong logo. PNG wins; only
            // borrow an in-game team's sprite+colors when no PNG matches.
            var customLogo = LoadCustomLogoSprite(cfg.LogoFrom);
            if (customLogo != null)
            {
                team.logo = customLogo;
                team.alternateBigLogo = customLogo.texture;
                try { team.hasBigLogo = true; } catch { }   // gate for rink/big-logo Texture surfaces
                PatchBossLaunchMatch.ApplyCustomLogoSkinToSkaters(team);   // jerseys use the custom-logo Spine slot
                Plugin.Log.LogInfo($"[Config] Applied CUSTOM logo '{cfg.LogoFrom}' (tex={(customLogo.texture != null ? customLogo.texture.width + "x" + customLogo.texture.height : "null")})");
            }
            else
            {
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
        //
        // NOTE: dropping unconfigured forwards from team.forwards to allow 1-4 man
        // campaign teams was tried (2026-07-31) and REVERTED — it did not work in
        // game. The removal itself was clean, so if this is attempted again the
        // problem lies downstream (line/on-ice construction or the match's expected
        // skater count), not in this loop. Blank slots therefore still keep the base
        // team's player.
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
                        var allFwds = EnsureForwardCache();
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
                // Apply team-level equipment color defaults (pass team so jersey
                // scheme is synced). Opponents are the VISITOR side → wear AWAY
                // colors so the two teams don't both render their home jersey.
                ApplyTeamEquipmentColors(fwds[i], cfg, team, useAway: true);
                // Apply per-player color overrides (highest priority, overrides team
                // defaults). Opponent = visitor → away kit, so away overrides win.
                PatchBossLaunchMatch.ApplyPlayerColorOverrides(fwds[i], pc, useAway: true);
            }
        }

        // Bug fix: base teams such as Hockey FC / Tycoons ship with MORE than 5
        // forwards (a bench / second line). The per-position apply above only
        // covers the 5 starters, so forwards[5..] keep the BASE team's identity
        // (name, look, talents) and leak through as e.g. "Hockey FC bench / 2nd
        // line". Repaint every extra forward as a copy of the matching configured
        // starter so the whole roster reads as THIS team. Each keeps its own id,
        // so any team.lines[] references stay valid. (isSoccerPlayer on every
        // forward was already cleared above.)
        if (fwds != null && fwds.Count > 5)
        {
            for (int i = 5; i < fwds.Count; i++)
            {
                if (fwds[i] == null) continue;
                var src = fwds[i % 5];
                if (src == null) continue;
                CopyPlayerData(src, fwds[i]);
                try { fwds[i].lastName = (src.lastName ?? "") + " II"; } catch {}
                Plugin.Log.LogInfo($"[Config] Neutralized extra forward[{i}] -> copy of starter '{src.firstName} {src.lastName}'");
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
            ApplyGoalieConfig(team.goalie, cfg.Goalie, useAway: true);
            // Opponent = visitor → away kit (see forward call above).
            ApplyTeamEquipmentColorsToGoalie(team.goalie, team, cfg, useAway: true);
        }

        // Apply stat scale. This used to exist ONLY in the import branch above, so
        // "Stat Scale = 1.5" silently did nothing on every hand-configured campaign
        // team — the common case. Applied last, after every per-player stat has been
        // written, so it scales the final values rather than being overwritten by
        // them.
        ApplyStatScale(team, cfg);

        // Apply relics
        foreach (var r in cfg.Relics)
            GiveRelic(team, r);

        Plugin.Log.LogInfo($"[Config] Applied manual team '{team.teamName}'");
    }

    /// <summary>Multiply every skater and goalie stat on the team by cfg.StatScale.
    /// Shared by the import and manual paths so they can't drift apart again.</summary>
    internal static void ApplyStatScale(TeamData team, TeamConfig cfg)
    {
        if (team == null || cfg == null) return;
        if (cfg.StatScale == 1.0f) return;
        try
        {
            float s = cfg.StatScale;
            var sf = team.forwards;
            if (sf != null)
                for (int i = 0; i < sf.Count; i++)
                {
                    if (sf[i] == null) continue;
                    sf[i].speed = (int)(sf[i].speed * s);
                    sf[i].shotPower = (int)(sf[i].shotPower * s);
                    sf[i].shotAccuracy = (int)(sf[i].shotAccuracy * s);
                    sf[i].checking = (int)(sf[i].checking * s);
                }
            var gg = team.goalie;
            if (gg != null)
            {
                gg.skill = (int)(gg.skill * s);
                gg.catchingSkill = (int)(gg.catchingSkill * s);
                gg.gloveSkill = (int)(gg.gloveSkill * s);
                gg.blockerSkill = (int)(gg.blockerSkill * s);
                gg.fiveHoleSkill = (int)(gg.fiveHoleSkill * s);
                gg.standingSpeed = (int)(gg.standingSpeed * s);
                gg.butterflySpeed = (int)(gg.butterflySpeed * s);
                gg.controlSkill = (int)(gg.controlSkill * s);
                gg.recoverySkill = (int)(gg.recoverySkill * s);
                gg.pokecheckSkill = (int)(gg.pokecheckSkill * s);
                gg.depth = (int)(gg.depth * s);
            }
            Plugin.Log.LogInfo($"[Config] '{team.teamName}': stats scaled by {s}x");
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[Config] ApplyStatScale: {ex.Message}"); }
    }

    internal static void SetSchemeColor(ColorScheme scheme, int[] primary, int[] secondary, int[] tertiary)
    {
        if (scheme == null) return;
        if (primary != null) scheme.primaryColor = new Color(primary[0]/255f, primary[1]/255f, primary[2]/255f);
        if (secondary != null) scheme.secondaryColor = new Color(secondary[0]/255f, secondary[1]/255f, secondary[2]/255f);
        if (tertiary != null) scheme.tertiaryColor = new Color(tertiary[0]/255f, tertiary[1]/255f, tertiary[2]/255f);
    }

    internal static void ApplyPlayerColorOverrides(ForwardData f, PlayerConfig pc, bool useAway = false)
    {
        if (f == null || pc == null) return;

        // Kit-aware pick: when the player's team wears its AWAY jersey the away
        // override wins, falling back to the home override so away-only or
        // home-only configs both behave sensibly.
        int[] Pick(int[] home, int[] away) => useAway ? (away ?? home) : home;

        int[] jerseyP = Pick(pc.JerseyColor, pc.JerseyColorAway);
        int[] jerseyS = Pick(pc.JerseySecondaryColor, pc.JerseySecondaryColorAway);
        int[] jerseyA = Pick(pc.JerseyAccentColor, pc.JerseyAccentColorAway);
        int[] glovesP = Pick(pc.GlovesColor, pc.GlovesColorAway);
        int[] glovesS = Pick(pc.GlovesSecondaryColor, pc.GlovesSecondaryColorAway);
        int[] glovesT = Pick(pc.GlovesTertiaryColor, pc.GlovesTertiaryColorAway);
        int[] helmetP = Pick(pc.HelmetColor, pc.HelmetColorAway);
        int[] helmetS = Pick(pc.HelmetSecondaryColor, pc.HelmetSecondaryColorAway);
        int[] helmetT = Pick(pc.HelmetTertiaryColor, pc.HelmetTertiaryColorAway);
        int[] pantsP  = Pick(pc.PantsColor, pc.PantsColorAway);
        int[] pantsS  = Pick(pc.PantsSecondaryColor, pc.PantsSecondaryColorAway);
        int[] pantsT  = Pick(pc.PantsTertiaryColor, pc.PantsTertiaryColorAway);
        int[] skates  = Pick(pc.SkatesColor, pc.SkatesColorAway);
        int[] blade   = Pick(pc.BladeColor, pc.BladeColorAway);
        int[] laces   = Pick(pc.LacesColor, pc.LacesColorAway);
        int[] bicep   = Pick(pc.BicepColor, pc.BicepColorAway);
        int[] number  = Pick(pc.NumberColor, pc.NumberColorAway);
        int[] numberS = Pick(pc.NumberSecondaryColor, pc.NumberSecondaryColorAway);
        int[] socksP  = Pick(pc.SocksColor, pc.SocksColorAway);
        int[] socksS  = Pick(pc.SocksSecondaryColor, pc.SocksSecondaryColorAway);
        int[] socksT  = Pick(pc.SocksTertiaryColor, pc.SocksTertiaryColorAway);

        bool hasAnyColor = jerseyP != null || jerseyS != null || jerseyA != null ||
            glovesP != null || glovesS != null || glovesT != null ||
            helmetP != null || helmetS != null || helmetT != null ||
            pantsP != null || pantsS != null || pantsT != null ||
            skates != null || blade != null || laces != null ||
            bicep != null || socksP != null || socksS != null || socksT != null;
        if (!hasAnyColor && number == null && numberS == null) return;

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
                    SetSchemeColor(f.colorSchemes.jerseyScheme, jerseyP, jerseyS, jerseyA);
                    SetSchemeColor(f.colorSchemes.glovesScheme, glovesP, glovesS, glovesT);
                    SetSchemeColor(f.colorSchemes.helmetScheme, helmetP, helmetS, helmetT);
                    SetSchemeColor(f.colorSchemes.pantsScheme, pantsP, pantsS, pantsT);
                    SetSchemeColor(f.colorSchemes.skatesScheme, skates, blade, laces);
                    SetSchemeColor(f.colorSchemes.socksScheme, socksP, socksS, socksT);
                    SetSchemeColor(f.colorSchemes.numberScheme, number, numberS, null);
                    if (bicep != null)
                        f.colorSchemes.jerseyScheme.secondaryColor = new Color(bicep[0]/255f, bicep[1]/255f, bicep[2]/255f);
                    Plugin.Log.LogInfo($"[Color] Applied per-player overrides to {f.firstName} {f.lastName}{(useAway ? " (away kit)" : "")}");
                }
            }

            if (number != null)
                f.numberColorOverride = new Color(number[0]/255f, number[1]/255f, number[2]/255f);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[Color] Player color override error: {ex.Message}");
        }
    }

    internal static void ApplyGoalieColorOverrides(GoaltenderData g, PlayerConfig pc, bool useAway = false)
    {
        if (g == null || pc == null) return;

        // Same kit-aware pick as the skater path: away override wins on the
        // away kit, falling back to the home override.
        int[] Pick(int[] home, int[] away) => useAway ? (away ?? home) : home;

        int[] jerseyP = Pick(pc.JerseyColor, pc.JerseyColorAway);
        int[] jerseyS = Pick(pc.JerseySecondaryColor, pc.JerseySecondaryColorAway);
        int[] jerseyA = Pick(pc.JerseyAccentColor, pc.JerseyAccentColorAway);
        int[] glovesP = Pick(pc.GlovesColor, pc.GlovesColorAway);
        int[] glovesS = Pick(pc.GlovesSecondaryColor, pc.GlovesSecondaryColorAway);
        int[] glovesT = Pick(pc.GlovesTertiaryColor, pc.GlovesTertiaryColorAway);
        int[] helmetP = Pick(pc.HelmetColor, pc.HelmetColorAway);
        int[] helmetS = Pick(pc.HelmetSecondaryColor, pc.HelmetSecondaryColorAway);
        int[] helmetT = Pick(pc.HelmetTertiaryColor, pc.HelmetTertiaryColorAway);
        int[] pantsP  = Pick(pc.PantsColor, pc.PantsColorAway);
        int[] pantsS  = Pick(pc.PantsSecondaryColor, pc.PantsSecondaryColorAway);
        int[] pantsT  = Pick(pc.PantsTertiaryColor, pc.PantsTertiaryColorAway);
        int[] skates  = Pick(pc.SkatesColor, pc.SkatesColorAway);
        int[] blade   = Pick(pc.BladeColor, pc.BladeColorAway);
        int[] laces   = Pick(pc.LacesColor, pc.LacesColorAway);
        int[] number  = Pick(pc.NumberColor, pc.NumberColorAway);
        int[] numberS = Pick(pc.NumberSecondaryColor, pc.NumberSecondaryColorAway);
        int[] socksP  = Pick(pc.SocksColor, pc.SocksColorAway);
        int[] socksS  = Pick(pc.SocksSecondaryColor, pc.SocksSecondaryColorAway);
        int[] socksT  = Pick(pc.SocksTertiaryColor, pc.SocksTertiaryColorAway);

        bool hasAny = jerseyP != null || jerseyS != null || jerseyA != null ||
            glovesP != null || glovesS != null || glovesT != null ||
            helmetP != null || helmetS != null || helmetT != null ||
            pantsP != null || pantsS != null || pantsT != null ||
            skates != null || blade != null || laces != null ||
            socksP != null || socksS != null || socksT != null ||
            number != null || numberS != null;
        if (!hasAny) return;

        try
        {
            // GoaltenderData has a colorSchemes field like ForwardData
            if (g.colorSchemes != null)
            {
                SetSchemeColor(g.colorSchemes.jerseyScheme, jerseyP, jerseyS, jerseyA);
                SetSchemeColor(g.colorSchemes.glovesScheme, glovesP, glovesS, glovesT);
                SetSchemeColor(g.colorSchemes.helmetScheme, helmetP, helmetS, helmetT);
                SetSchemeColor(g.colorSchemes.pantsScheme, pantsP, pantsS, pantsT);
                SetSchemeColor(g.colorSchemes.skatesScheme, skates, blade, laces);
                SetSchemeColor(g.colorSchemes.socksScheme, socksP, socksS, socksT);
                SetSchemeColor(g.colorSchemes.numberScheme, number, numberS, null);
                Plugin.Log.LogInfo($"[Color] Applied goalie color overrides to '{g.firstName} {g.lastName}'{(useAway ? " (away kit)" : "")}");
            }

        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[Color] Goalie color override error: {ex.Message}");
        }
    }

    internal static void ApplyTeamEquipmentColors(ForwardData f, TeamConfig cfg, TeamData team = null, bool useAway = false)
    {
        if (f == null || cfg == null) return;
        try
        {
            if (f.colorSchemes == null) return;

            // Sync the jersey scheme from the team's fully-resolved HOME or AWAY
            // colors (away for the visitor/opponent side) so Body/Customization
            // renders in the right kit instead of black AND the two teams don't
            // both wear their home jersey. Falls back to home if away is unset so
            // we never render black. Must run before per-player Jersey Color
            // overrides (ApplyPlayerColorOverrides), which stomp these if set.
            var srcColors = useAway ? team?.awayColors : team?.homeColors;
            if (srcColors?.jerseyScheme == null) srcColors = team?.homeColors;
            if (srcColors?.jerseyScheme != null)
            {
                var jc = srcColors.jerseyScheme;
                f.colorSchemes.jerseyScheme.primaryColor   = jc.primaryColor;
                f.colorSchemes.jerseyScheme.secondaryColor = jc.secondaryColor;
                f.colorSchemes.jerseyScheme.tertiaryColor  = jc.tertiaryColor;
            }

            // Equipment colors: prefer the AWAY variant when wearing the away
            // jersey, falling back (??) to the home value so unset away pieces
            // still match the kit instead of going blank.
            SetSchemeColor(f.colorSchemes.glovesScheme,
                useAway ? (cfg.TeamGlovesColorAway ?? cfg.TeamGlovesColor) : cfg.TeamGlovesColor,
                useAway ? (cfg.TeamGlovesSecondaryAway ?? cfg.TeamGlovesSecondary) : cfg.TeamGlovesSecondary,
                useAway ? (cfg.TeamGlovesTertiaryAway ?? cfg.TeamGlovesTertiary) : cfg.TeamGlovesTertiary);
            SetSchemeColor(f.colorSchemes.helmetScheme,
                useAway ? (cfg.TeamHelmetColorAway ?? cfg.TeamHelmetColor) : cfg.TeamHelmetColor,
                useAway ? (cfg.TeamHelmetSecondaryAway ?? cfg.TeamHelmetSecondary) : cfg.TeamHelmetSecondary,
                useAway ? (cfg.TeamHelmetTertiaryAway ?? cfg.TeamHelmetTertiary) : cfg.TeamHelmetTertiary);
            SetSchemeColor(f.colorSchemes.pantsScheme,
                useAway ? (cfg.TeamPantsColorAway ?? cfg.TeamPantsColor) : cfg.TeamPantsColor,
                useAway ? (cfg.TeamPantsSecondaryAway ?? cfg.TeamPantsSecondary) : cfg.TeamPantsSecondary,
                useAway ? (cfg.TeamPantsTertiaryAway ?? cfg.TeamPantsTertiary) : cfg.TeamPantsTertiary);
            SetSchemeColor(f.colorSchemes.skatesScheme,
                useAway ? (cfg.TeamSkatesColorAway ?? cfg.TeamSkatesColor) : cfg.TeamSkatesColor,
                useAway ? (cfg.TeamBladeColorAway ?? cfg.TeamBladeColor) : cfg.TeamBladeColor,
                useAway ? (cfg.TeamLacesColorAway ?? cfg.TeamLacesColor) : cfg.TeamLacesColor);
            SetSchemeColor(f.colorSchemes.socksScheme,
                useAway ? (cfg.TeamSocksColorAway ?? cfg.TeamSocksColor) : cfg.TeamSocksColor,
                useAway ? (cfg.TeamSocksSecondaryAway ?? cfg.TeamSocksSecondary) : cfg.TeamSocksSecondary,
                useAway ? (cfg.TeamSocksTertiaryAway ?? cfg.TeamSocksTertiary) : cfg.TeamSocksTertiary);
            SetSchemeColor(f.colorSchemes.numberScheme,
                useAway ? (cfg.TeamNumberColorAway ?? cfg.TeamNumberColor) : cfg.TeamNumberColor,
                useAway ? (cfg.TeamNumberSecondaryAway ?? cfg.TeamNumberSecondary) : cfg.TeamNumberSecondary, null);

            int[] bicep = useAway ? (cfg.TeamBicepColorAway ?? cfg.TeamBicepColor) : cfg.TeamBicepColor;
            if (bicep != null)
                f.colorSchemes.jerseyScheme.secondaryColor = new Color(bicep[0]/255f, bicep[1]/255f, bicep[2]/255f);
            int[] stick = useAway ? (cfg.TeamStickColorAway ?? cfg.TeamStickColor) : cfg.TeamStickColor;
            if (stick != null)
                f.colorSchemes.stickScheme.primaryColor = new Color(stick[0]/255f, stick[1]/255f, stick[2]/255f);
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[Config] Team equipment color error: {ex.Message}"); }
    }

    /// <summary>
    /// Give the goalie the SAME color schemes as the team's forwards so the
    /// customizable mask (Helmet_Customization_colors) renders in team colors.
    /// Painting individual fields on g.colorSchemes.helmetScheme didn't stick —
    /// sharing the reference does.
    /// </summary>
    internal static void ApplyTeamEquipmentColorsToGoalie(GoaltenderData g, TeamData team, TeamConfig cfg, bool useAway = false)
    {
        if (g == null || team == null) return;
        try
        {
            // Link the goalie to the team's HOME or AWAY color set (away for the
            // visitor/opponent), falling back to home so the mask is never black.
            var linked = useAway ? team.awayColors : team.homeColors;
            if (linked == null) linked = team.homeColors;
            if (linked != null)
            {
                g.colorSchemes = linked;
                Plugin.Log.LogInfo($"[Config] Goalie '{g.firstName} {g.lastName}' colorSchemes linked to team {(useAway ? "awayColors" : "homeColors")}");
            }
            // Then paint per-field cfg overrides on top. Each piece prefers the
            // away value (when wearing away) then the home value, so the mask has
            // a sensible color even when only some fields are set.
            if (g.colorSchemes != null && cfg != null)
            {
                int[] helmetPrimary   = useAway ? (cfg.TeamHelmetColorAway     ?? cfg.TeamHelmetColor     ?? cfg.AwayPrimary   ?? cfg.JerseyPrimary)   : (cfg.TeamHelmetColor     ?? cfg.JerseyPrimary);
                int[] helmetSecondary = useAway ? (cfg.TeamHelmetSecondaryAway ?? cfg.TeamHelmetSecondary ?? cfg.AwaySecondary ?? cfg.JerseySecondary) : (cfg.TeamHelmetSecondary ?? cfg.JerseySecondary);
                int[] helmetTertiary  = useAway ? (cfg.TeamHelmetTertiaryAway  ?? cfg.TeamHelmetTertiary  ?? cfg.AwayAccent    ?? cfg.JerseyAccent)    : (cfg.TeamHelmetTertiary  ?? cfg.JerseyAccent);
                SetSchemeColor(g.colorSchemes.helmetScheme, helmetPrimary, helmetSecondary, helmetTertiary);
                SetSchemeColor(g.colorSchemes.glovesScheme,
                    useAway ? (cfg.TeamGlovesColorAway ?? cfg.TeamGlovesColor) : cfg.TeamGlovesColor,
                    useAway ? (cfg.TeamGlovesSecondaryAway ?? cfg.TeamGlovesSecondary) : cfg.TeamGlovesSecondary,
                    useAway ? (cfg.TeamGlovesTertiaryAway ?? cfg.TeamGlovesTertiary) : cfg.TeamGlovesTertiary);
                SetSchemeColor(g.colorSchemes.pantsScheme,
                    useAway ? (cfg.TeamPantsColorAway ?? cfg.TeamPantsColor) : cfg.TeamPantsColor,
                    useAway ? (cfg.TeamPantsSecondaryAway ?? cfg.TeamPantsSecondary) : cfg.TeamPantsSecondary,
                    useAway ? (cfg.TeamPantsTertiaryAway ?? cfg.TeamPantsTertiary) : cfg.TeamPantsTertiary);
                SetSchemeColor(g.colorSchemes.skatesScheme,
                    useAway ? (cfg.TeamSkatesColorAway ?? cfg.TeamSkatesColor) : cfg.TeamSkatesColor,
                    useAway ? (cfg.TeamBladeColorAway ?? cfg.TeamBladeColor) : cfg.TeamBladeColor,
                    useAway ? (cfg.TeamLacesColorAway ?? cfg.TeamLacesColor) : cfg.TeamLacesColor);
                SetSchemeColor(g.colorSchemes.socksScheme,
                    useAway ? (cfg.TeamSocksColorAway ?? cfg.TeamSocksColor) : cfg.TeamSocksColor,
                    useAway ? (cfg.TeamSocksSecondaryAway ?? cfg.TeamSocksSecondary) : cfg.TeamSocksSecondary,
                    useAway ? (cfg.TeamSocksTertiaryAway ?? cfg.TeamSocksTertiary) : cfg.TeamSocksTertiary);
                SetSchemeColor(g.colorSchemes.numberScheme,
                    useAway ? (cfg.TeamNumberColorAway ?? cfg.TeamNumberColor) : cfg.TeamNumberColor,
                    useAway ? (cfg.TeamNumberSecondaryAway ?? cfg.TeamNumberSecondary) : cfg.TeamNumberSecondary, null);
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

    internal static void ApplyGoalieConfig(GoaltenderData g, PlayerConfig pc, bool useAway = false)
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
            // Always copy skin pools from any working NPC goalie first so the
            // Spine pool is populated before we set our desired helmet path.
            try
            {
                var allG = UnityEngine.Resources.FindObjectsOfTypeAll<GoaltenderData>();
                GoaltenderData anyDonor = null;
                if (allG != null)
                    foreach (var d in allG)
                        if (d != null && d != g && !string.IsNullOrEmpty(d.helmetSkin)) { anyDonor = d; break; }
                if (anyDonor != null) CopyGoalieSkinPoolsFrom(g, anyDonor);
            }
            catch { }

            if (!string.IsNullOrEmpty(pc.GoalieHelmetSkin))
                try
                {
                    var rh = Plugin.ResolveGoalieSkin(pc.GoalieHelmetSkin, "helmet");
                    if (!string.IsNullOrEmpty(rh))
                    {
                        g.helmetSkin = rh;
                        EnsureGoalieSkinInPool(g, "_helmetSkins", rh);
                        var donor = FindGoalieWithHelmet(rh);
                        if (donor != null && donor != g) CopyGoalieSkinPoolsFrom(g, donor);
                    }
                }
                catch (Exception ex) { Plugin.Log.LogWarning($"[Config] Goalie helmet apply: {ex.Message}"); }
            if (!string.IsNullOrEmpty(pc.GoalieLogoSkin)) try { g.logoSkin = pc.GoalieLogoSkin; EnsureGoalieSkinInPool(g, "_logoSkins", pc.GoalieLogoSkin); } catch {}

            // Goalies render with a mask placed over the bare goalie face.
            // If helmetSkin is empty OR doesn't start with the goalie helmet
            // prefix (e.g. a stale dump wrote "Helmet_Canadians" before the
            // reverse-map fix), fall back to the team-tinted default.
            try
            {
                if (string.IsNullOrEmpty(g.helmetSkin) ||
                    !g.helmetSkin.StartsWith("Helmet/", StringComparison.OrdinalIgnoreCase))
                {
                    g.helmetSkin = "Helmet/Helmet_Customization_colors";
                    EnsureGoalieSkinInPool(g, "_helmetSkins", g.helmetSkin);
                }
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
            ApplyGoalieColorOverrides(g, pc, useAway);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[Config] Error applying goalie '{pc.Name}': {ex.Message}");
        }
    }

    internal static void CopyPlayerData(ForwardData src, ForwardData dst)
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

    // Resources.FindObjectsOfTypeAll<ForwardData>() walks every loaded object and
    // is not cheap. It used to run at most a handful of times per match; since
    // opponents are configured for the WHOLE map at once (PatchMapOpponents) it
    // can now run once per imported player per node in a single frame, so cache
    // the sweep. A miss re-resolves and retries once, the same way FindTalent
    // recovers from a repo that was grabbed before the game filled it.
    internal static ForwardData[] AllForwardCache;

    internal static ForwardData[] EnsureForwardCache()
    {
        if (AllForwardCache == null || AllForwardCache.Length == 0)
            AllForwardCache = UnityEngine.Resources.FindObjectsOfTypeAll<ForwardData>();
        return AllForwardCache;
    }

    private static ForwardData FindPlayerByName(string name)
    {
        var found = FindPlayerByNameOnce(name, EnsureForwardCache());
        if (found != null) return found;
        // Miss — the cache may predate this player being loaded. Rescan once.
        AllForwardCache = null;
        return FindPlayerByNameOnce(name, EnsureForwardCache());
    }

    private static ForwardData FindPlayerByNameOnce(string name, ForwardData[] allForwards)
    {
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

// ============================================================
// Drop the vanilla team mascot from campaign match nodes.
//
// The node's 'NodeGraphic' skeleton has exactly ONE skin ("default") — confirmed
// at runtime — so the mascot is not skin-driven. It comes from the ANIMATION:
// the skeleton ships one animation per vanilla stadium (Stadium_Lettuce,
// Stadium_Meatballs, Stadium_Cheese, Stadium_Princess, Stadium_Ref, ...), each
// switching on that team's slots. Greasy Lettuce's animation turns on
// 'Stadium_Lettuce_Flag' — the leaf on the pole that was still showing next to
// our logo.
//
// Both SetElite and SetBoss take the animation name as an ARGUMENT, so the clean
// fix is to hand them a neutral one instead of fighting the attachment timeline
// frame by frame. 'Outside_Rink1' is a real animation on the same skeleton with
// no team mascot attached, so every campaign node reads as a plain rink wearing
// the custom team's logo.
//
// Only campaign nodes are touched: Default mode and unconfigured runs keep the
// vanilla stadiums.
// ============================================================
// ============================================================
// Late node probe. The map-generation probe ran before the node had finished
// building itself — every Spine slot read attachment='none', and anything created
// after map generation (very possibly a SEPARATE skeleton for the team mascot)
// simply wasn't there to be found.
//
// MapObject.RefreshNodeStates runs once the map is live and again on navigation,
// so by then the node is whole. Dumps every renderer AND every skeleton under the
// node, with the animation each is playing, once per session.
// ============================================================
public static class PatchLateNodeProbe
{
    private static bool _done;

    public static void Postfix(STS.Map.MapObject __instance)
    {
        try
        {
            if (__instance == null || Plugin.IsDefaultMode) return;
            var nodes = __instance.MapNodes;
            if (nodes == null) return;

            // Point each node's own 'rewardIcon_image' at the campaign logo. Done
            // here as well as at map generation because the node can still be
            // half-built then — which is what made the first probe report every
            // Spine slot as empty. Cheap and idempotent.
            for (int i = 0; i < nodes.Count; i++)
            {
                var mn = nodes[i];
                if (mn == null) continue;
                STS.Map.MatchMapNode m = null;
                try { m = mn.TryCast<STS.Map.MatchMapNode>(); } catch { }
                if (m != null) PatchMapOpponents.EnsureNodeArt(m);
            }

            if (_done) return;
            for (int i = 0; i < nodes.Count; i++)
            {
                var mn = nodes[i];
                if (mn == null) continue;
                STS.Map.MatchMapNode match = null;
                try { match = mn.TryCast<STS.Map.MatchMapNode>(); } catch { }
                if (match == null) continue;

                var go = mn.gameObject;
                if (go == null) continue;
                var root = go.transform;
                _done = true;

                string opp = "?";
                try { opp = match.opponent != null ? match.opponent.teamName : "null"; } catch { }
                Plugin.Log.LogInfo($"[LateArt] === '{go.name}' opponent='{opp}' (fully built) ===");

                // EVERY renderer, not just SpriteRenderers — the mascot may well be
                // a mesh drawn by a second Spine skeleton.
                try
                {
                    var rends = go.GetComponentsInChildren<Renderer>(true);
                    if (rends != null)
                        foreach (var r in rends)
                        {
                            if (r == null) continue;
                            string extra = "";
                            try
                            {
                                var sr = r.TryCast<SpriteRenderer>();
                                if (sr != null) extra = $" sprite='{(sr.sprite != null ? sr.sprite.name : "null")}'";
                            }
                            catch { }
                            string type = "?";
                            try { type = r.GetIl2CppType().Name; } catch { }
                            Plugin.Log.LogInfo($"[LateArt]   {type} '{PathOfT(r.transform, root)}'"
                                + $" enabled={r.enabled} order={r.sortingOrder}{extra}");
                        }
                }
                catch (Exception ex) { Plugin.Log.LogWarning($"[LateArt]   renderer sweep: {ex.Message}"); }

                // Every skeleton under the node, and what it's playing. A second
                // skeleton here is the most likely home of the mascot.
                try
                {
                    var skels = go.GetComponentsInChildren<Spine.Unity.SkeletonAnimation>(true);
                    Plugin.Log.LogInfo($"[LateArt]   skeletons found: {(skels != null ? skels.Length : 0)}");
                    if (skels != null)
                        foreach (var sk in skels)
                        {
                            if (sk == null) continue;
                            string anim = "?", skinName = "?", dataName = "?";
                            try
                            {
                                // No GetCurrent in this interop build — read track 0
                                // straight off the exposed Tracks list.
                                var st = sk.AnimationState;
                                var tracks = st != null ? st.Tracks : null;
                                var items = tracks != null ? tracks.Items : null;
                                var cur = items != null && tracks.Count > 0 && items.Length > 0 ? items[0] : null;
                                anim = cur != null && cur.Animation != null ? cur.Animation.Name : "none";
                            }
                            catch (Exception ex) { anim = "unreadable:" + ex.GetType().Name; }
                            try
                            {
                                var sd = sk.Skeleton;
                                if (sd != null)
                                {
                                    skinName = sd.Skin != null ? sd.Skin.Name : "null";
                                    dataName = sd.Data != null ? sd.Data.Name : "null";
                                }
                            }
                            catch { }
                            Plugin.Log.LogInfo($"[LateArt]   Skeleton '{PathOfT(sk.transform, root)}'"
                                + $" data='{dataName}' animation='{anim}' skin='{skinName}'");

                            // Slots actually showing something, now that the node is built.
                            try
                            {
                                var skeleton = sk.Skeleton;
                                var data = skeleton != null ? skeleton.Data : null;
                                var slots = data != null ? data.Slots : null;
                                var items = slots != null ? slots.Items : null;
                                int shown = 0;
                                if (items != null)
                                    for (int s = 0; s < slots.Count && s < items.Length; s++)
                                    {
                                        string slotName = null;
                                        try { var sdta = items[s]; if (sdta != null) slotName = sdta.Name; } catch { }
                                        if (string.IsNullOrEmpty(slotName)) continue;
                                        try
                                        {
                                            var live = skeleton.FindSlot(slotName);
                                            var a = live != null && live.Pose != null ? live.Pose.Attachment : null;
                                            if (a != null)
                                            {
                                                Plugin.Log.LogInfo($"[LateArt]     VISIBLE slot '{slotName}' attachment='{a.Name}'");
                                                shown++;
                                            }
                                        }
                                        catch { }
                                    }
                                Plugin.Log.LogInfo($"[LateArt]     ({shown} visible slot(s))");
                            }
                            catch (Exception ex) { Plugin.Log.LogWarning($"[LateArt]     slot sweep: {ex.Message}"); }
                        }
                }
                catch (Exception ex) { Plugin.Log.LogWarning($"[LateArt]   skeleton sweep: {ex.Message}"); }

                break;   // one node is plenty
            }
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[LateArt] probe: {ex.Message}"); }
    }

    private static string PathOfT(Transform t, Transform root)
    {
        var parts = new List<string>();
        int guard = 0;
        while (t != null && t != root && guard++ < 12) { parts.Insert(0, t.name); t = t.parent; }
        return parts.Count > 0 ? string.Join("/", parts.ToArray()) : ".";
    }
}

// ============================================================
// Give the mascot skeleton back for the post-match explosion.
//
// We switch MatchMapNode.explosionSkeleton's renderer off to hide the vanilla team
// mascot (see PatchMapOpponents.HideMascotSkeleton), but that same skeleton draws
// the explosion played once the team is beaten. Re-enable it here so the explosion
// still shows; by then the match is over and the mascot no longer matters.
// ============================================================
public static class PatchPlayExplosionAnim
{
    public static void Prefix(STS.Map.MatchMapNode __instance)
    {
        try
        {
            if (__instance == null) return;
            var skel = __instance.explosionSkeleton;
            var go = skel != null ? skel.gameObject : null;
            var rend = go != null ? go.GetComponent<Renderer>() : null;
            if (rend == null || rend.enabled) return;
            rend.enabled = true;
            Plugin.Log.LogInfo("[NodeArt] re-enabled the explosion skeleton for the post-match animation.");
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[NodeArt] PlayExplosionAnim: {ex.Message}"); }
    }
}

public static class PatchNodeStadiumAnimation
{
    // Any animation on the node skeleton works here. The full list, read off a
    // runtime probe, is: Campfire, Campfire_Act2, Challenge_Act2, Challenge_Act3,
    // Chopper, Explosion, Explosion_Act1, Explosion_Act2, Outside_Rink1, Poisson,
    // Rogue_Blademaster, Rogue_Chaos, Rogue_Coach, Rogue_Fan, Rogue_GM,
    // Rogue_Infirmary, Rogue_Training, Ship_Flags, Smity, Smity_Act2,
    // Stadium_Cheese, Stadium_Cultists, Stadium_Disco, Stadium_Golfers,
    // Stadium_Knights, Stadium_Lettuce, Stadium_Meatballs, Stadium_Meatballs_Alt,
    // Stadium_Mountaineers, Stadium_Princess, Stadium_Ref.
    internal const string NeutralNodeAnimation = "Outside_Rink1";

    // DISABLED. Swapping the stadium animation did NOT remove the mascot — it only
    // made every campaign node share one rink, losing the variety of the vanilla
    // stadiums for no gain. So the mascot is not driven by the stadium animation
    // after all; it is set up somewhere we haven't looked yet (see the late probe
    // in PatchLateNodeProbe — the early probe ran before the node finished
    // building, which is why every slot read 'none').
    //
    // Kept, switched off, because the animation-name inventory in the comment above
    // is hard-won and this is where it belongs if a neutral node look is ever
    // wanted deliberately.
    private const bool ReplaceStadiumAnimation = false;

    private static bool ShouldReplace()
    {
        return ReplaceStadiumAnimation && !Plugin.IsDefaultMode && Plugin.ConfigTeams.Count > 0;
    }

    public static void ElitePrefix(ref string eliteSkin)
    {
        if (!ShouldReplace()) return;
        Plugin.Log.LogInfo($"[NodeArt] SetElite: stadium animation '{eliteSkin}' -> '{NeutralNodeAnimation}' (drops the vanilla mascot)");
        eliteSkin = NeutralNodeAnimation;
    }

    public static void BossPrefix(ref string bossSkin)
    {
        if (!ShouldReplace()) return;
        Plugin.Log.LogInfo($"[NodeArt] SetBoss: stadium animation '{bossSkin}' -> '{NeutralNodeAnimation}' (drops the vanilla mascot)");
        bossSkin = NeutralNodeAnimation;
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
                // See PatchBossLaunchMatch.Prefix — normally already done at map
                // generation; this is the correcting fallback.
                if (CampaignOpponents.Ensure(opponent, Plugin.GamesPlayed, "EliteMapNode.LaunchMatch"))
                    Plugin.Log.LogInfo($"[Remix] Elite '{opponent.teamName}' configured at launch (game {Plugin.GamesPlayed + 1})");
            }
        }
        catch (Exception ex) { Plugin.Log.LogError($"[Remix] EliteLaunch: {ex}"); }
    }

}


// ============================================================
// Log all relics, abilities, talents from repositories
// ============================================================
// Team.Initialize(TeamData) was removed in the post-May-2026 game update.
// AutoDumpNameLists is now triggered from PatchSetCurrentAct via _pendingAutoDump.
//
// DumpSkinFields writes _game_skins.txt: for every skin field, the complete set of
// values the GAME itself uses, harvested from every loaded ForwardData and
// GoaltenderData. The Creator's dropdowns are generated from this rather than from
// hand-maintained lists, so a value can never be offered that the game doesn't have
// — and, more importantly, no real value is missing.
//
// This is the fix for the Golfers: hand-written friendly names could not express
// five distinct golfer faces, so they all collapsed to one name that resolved to
// nothing. Real values, dumped from the game, have no such ambiguity.
// ============================================================
// DEFAULT MODE data dump. The only patch that runs when the mod is switched off
// via active.txt — read-only, writes the game's team/player/logo/skin lists and
// the team library so the Creator has something to work from. Changes no game
// state, so "default" still plays as pure vanilla.
// ============================================================
public static class PatchDefaultModeDump
{
    public static void Postfix()
    {
        // No run-once flag of our own: AutoDumpNameLists gives up early when
        // TeamData isn't loaded yet, so this hook has to keep trying on each
        // refresh until it takes. Its own guards make repeat calls cheap.
        if (LogRepositories.GuiListsDumped) return;
        try
        {
            LogRepositories.AutoDumpNameLists();
            if (LogRepositories.GuiListsDumped)
                Plugin.Log.LogInfo("[Dump] DEFAULT MODE: game data + library written.");
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[Dump] DEFAULT MODE dump failed: {ex.Message}"); }
    }
}

public static class LogRepositories
{
    /// <summary>Collect every distinct value the game uses for each skin field and
    /// write them to _game_skins.txt, grouped by field. _gen_game_data.py turns this
    /// into the Creator's dropdown lists.</summary>
    internal static void DumpSkinFields(string root, Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppArrayBase<ForwardData> skaters,
                                        Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppArrayBase<GoaltenderData> goalies)
    {
        try
        {
            // Field name -> distinct values. Ordered so the file reads sensibly.
            var fields = new List<string>
            {
                "Face", "Body", "Body Away", "Bicep", "Bicep Away", "Gloves", "Gloves Away",
                "Pants", "Pants Away", "Skates", "Skates Away", "Stick", "Helmet", "Helmet Away",
                "Glasses", "Number", "Logo",
                "Goalie Helmet", "Goalie Skin", "Goalie Skin Away", "Goalie Glove", "Goalie Glove Away",
                "Goalie Blocker", "Goalie Blocker Away", "Goalie Pads", "Goalie Pads Away",
                "Goalie Stick", "Goalie Stick Away"
            };
            var values = new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in fields) values[f] = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

            void Add(string field, string val)
            {
                if (string.IsNullOrWhiteSpace(val)) return;
                if (values.TryGetValue(field, out var set)) set.Add(val.Trim());
            }

            if (skaters != null)
                foreach (var p in skaters)
                {
                    if (p == null) continue;
                    try
                    {
                        Add("Face", p.headSkin);
                        Add("Body", p.bodySkin);       Add("Body Away", p.bodyAwaySkin);
                        Add("Bicep", p.bicepSkin);     Add("Bicep Away", p.bicepAwaySkin);
                        Add("Gloves", p.gloveSkin);    Add("Gloves Away", p.gloveAwaySkin);
                        Add("Pants", p.pantsSkin);     Add("Pants Away", p.pantsAwaySkin);
                        Add("Skates", p.skateSkin);    Add("Skates Away", p.skateAwaySkin);
                        Add("Stick", p.stickSkin);
                        Add("Helmet", p.helmetSkin);   Add("Helmet Away", p.helmetAwaySkin);
                        Add("Glasses", p.glassesSkin);
                        Add("Number", p.numberSkin);
                        Add("Logo", p.logoSkin);
                    }
                    catch { }
                }

            if (goalies != null)
                foreach (var g in goalies)
                {
                    if (g == null) continue;
                    try
                    {
                        Add("Goalie Helmet", g.helmetSkin);
                        Add("Goalie Skin", g.skin);           Add("Goalie Skin Away", g.awaySkin);
                        Add("Goalie Glove", g.gloveSkin);     Add("Goalie Glove Away", g.awayGloveSkin);
                        Add("Goalie Blocker", g.blockerSkin); Add("Goalie Blocker Away", g.awayBlockerSkin);
                        Add("Goalie Pads", g.padsSkin);       Add("Goalie Pads Away", g.awayPadsSkin);
                        Add("Goalie Stick", g.stickSkin);     Add("Goalie Stick Away", g.awayStickSkin);
                    }
                    catch { }
                }

            var sb = new StringBuilder();
            sb.AppendLine("# Every value the GAME uses for each skin field, dumped from its own data.");
            sb.AppendLine("# Generated automatically — do not hand-edit; it is overwritten on launch.");
            sb.AppendLine("# Consumed by _gen_game_data.py to build the Creator's dropdowns.");
            int total = 0;
            foreach (var f in fields)
            {
                var set = values[f];
                sb.AppendLine();
                sb.AppendLine($"[{f}] ({set.Count})");
                foreach (var v in set) { sb.AppendLine(v); total++; }
            }
            File.WriteAllText(Path.Combine(root, "_game_skins.txt"), sb.ToString());
            Plugin.Log.LogInfo($"[Dump] Skin fields: {total} distinct value(s) across {fields.Count} field(s) -> _game_skins.txt");
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[Dump] Skin field dump failed: {ex.Message}"); }
    }

    // Called directly by PatchSetCurrentAct — not a Harmony patch.
    public static void OnTeamInitialized(Team __instance, TeamData teamData)
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

    // Helper: extract short name from a skin path (last path segment, no extension)
    private static string SkinShortName(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;
        int slash = path.LastIndexOf('/');
        return slash >= 0 ? path.Substring(slash + 1) : path;
    }

    // Helper: pull private Il2Cpp List<string> field via reflection into a HashSet
    private static void AddReflectedList(object obj, string fieldName, HashSet<string> target)
    {
        try
        {
            var field = obj.GetType().GetField(fieldName,
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field == null) return;
            var list = field.GetValue(obj) as Il2CppSystem.Collections.Generic.List<string>;
            if (list != null)
                for (int i = 0; i < list.Count; i++)
                    if (!string.IsNullOrEmpty(list[i])) target.Add(list[i]);
        } catch {}
    }

    // Write a sorted skin section. Faces are grouped by their folder prefix.
    // Other skins show:  shortName    fullPath
    private static void WriteSkinSection(StringBuilder sb, string header, HashSet<string> skins, bool groupByFolder = false)
    {
        var sorted = new List<string>(skins);
        sorted.Sort(StringComparer.OrdinalIgnoreCase);

        sb.AppendLine($"--- {header} ({sorted.Count}) ---");
        if (sorted.Count == 0) { sb.AppendLine("  (none)"); sb.AppendLine(); return; }

        if (groupByFolder)
        {
            // Group by the folder segment before the final name
            var groups = new SortedDictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in sorted)
            {
                int last = s.LastIndexOf('/');
                string folder = last > 0 ? s.Substring(0, last) : "(root)";
                // Strip leading "Faces/" from folder label for readability
                string label = folder.StartsWith("Faces/", StringComparison.OrdinalIgnoreCase)
                    ? folder.Substring(6) : folder;
                if (!groups.ContainsKey(label)) groups[label] = new List<string>();
                groups[label].Add(s);
            }
            foreach (var kvp in groups)
            {
                sb.AppendLine($"  [{kvp.Key}]");
                foreach (var s in kvp.Value)
                    sb.AppendLine($"    {SkinShortName(s),-32}  {s}");
            }
        }
        else
        {
            foreach (var s in sorted)
                sb.AppendLine($"  {SkinShortName(s),-32}  {s}");
        }
        sb.AppendLine();
    }

    // Known goalie talent keys — used to route talents to the correct file.
    private static readonly HashSet<string> s_goalieTalentKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Always Catch Pucks", "Cracked Mask", "Crease Clearer", "Goalie Dance",
        "Goalie Enrage First30Sec", "Goalie Enrage Last30Sec", "Goalie Enraged On Breakaway",
        "Goalie Enraged On Goal", "Goalie Enraged On Shot", "Goalie Fart", "Goalie Headshot",
        "Goalie Pass Proepl", "Goalie Speed Talent", "Goalie Throw Stick", "Mega Rebound"
    };

    private static void DumpPlayerLooks()
    {
        // ── Folder structure ──────────────────────────────────────────────
        string root         = Path.Combine(BepInEx.Paths.PluginPath, "T2T_Dumps");
        string dirPlayers   = Path.Combine(root, "players");
        string dirTeams     = Path.Combine(root, "teams");
        string dirSkSkins   = Path.Combine(root, "skins_skater");
        string dirGkSkins   = Path.Combine(root, "skins_goalie");
        string dirLogos     = Path.Combine(root, "logos");
        foreach (var d in new[]{ root, dirPlayers, dirTeams, dirSkSkins, dirGkSkins, dirLogos })
            Directory.CreateDirectory(d);

        // ── Gather PlayableTeams ──────────────────────────────────────────
        var ptArr = UnityEngine.Resources.FindObjectsOfTypeAll<PlayableTeams>();
        var pt    = ptArr != null && ptArr.Length > 0 ? ptArr[0] : null;

        var leagueTeams   = new List<TeamData>();
        var campaignTeams = new List<TeamData>();

        if (pt != null)
        {
            if (pt.leagueTeams != null)
                for (int i = 0; i < pt.leagueTeams.Count; i++)
                    if (pt.leagueTeams[i] != null) leagueTeams.Add(pt.leagueTeams[i]);

            var seenCamp = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (pt.campaignTeams != null)
                for (int i = 0; i < pt.campaignTeams.Count; i++)
                {
                    var t = pt.campaignTeams[i];
                    if (t != null && seenCamp.Add(t.teamName)) campaignTeams.Add(t);
                }
            if (pt.campaignExclusiveTeams != null)
                for (int i = 0; i < pt.campaignExclusiveTeams.Count; i++)
                {
                    var t = pt.campaignExclusiveTeams[i];
                    if (t != null && seenCamp.Add(t.teamName)) campaignTeams.Add(t);
                }
        }

        var seenAll      = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var allBaseTeams = new List<TeamData>();
        foreach (var t in leagueTeams)   if (seenAll.Add(t.teamName)) allBaseTeams.Add(t);
        foreach (var t in campaignTeams) if (seenAll.Add(t.teamName)) allBaseTeams.Add(t);

        // ── Collect players from base-game teams ──────────────────────────
        var skaters  = new List<(ForwardData f, string team)>();
        var goalies  = new List<(GoaltenderData g, string team)>();
        var ownedPtr = new HashSet<System.IntPtr>();

        foreach (var team in allBaseTeams)
        {
            if (team.forwards != null)
                for (int i = 0; i < team.forwards.Count; i++)
                {
                    var f = team.forwards[i];
                    if (f != null && ownedPtr.Add(f.Pointer)) skaters.Add((f, team.teamName));
                }
            if (team.goalie != null && ownedPtr.Add(team.goalie.Pointer))
                goalies.Add((team.goalie, team.teamName));
        }

        // Also mark players on ALL teams in memory (including user-created) as owned
        // so they are excluded from FREE_AGENTS.txt.
        var allTeamsMem = UnityEngine.Resources.FindObjectsOfTypeAll<TeamData>();
        if (allTeamsMem != null)
        {
            foreach (var td in allTeamsMem)
            {
                if (td == null) continue;
                if (td.forwards != null)
                    for (int i = 0; i < td.forwards.Count; i++)
                    { var f2 = td.forwards[i]; if (f2 != null) ownedPtr.Add(f2.Pointer); }
                if (td.goalie != null) ownedPtr.Add(td.goalie.Pointer);
            }
        }

        // ── Collect skins ONLY from base-game players ─────────────────────
        var hd  = new HashSet<string>(); // faces
        var bd  = new HashSet<string>(); // body
        var hm  = new HashSet<string>(); // helmet
        var st  = new HashSet<string>(); // stick
        var gl  = new HashSet<string>(); // glasses
        var bc  = new HashSet<string>(); // bicep
        var gv  = new HashSet<string>(); // gloves
        var pt2 = new HashSet<string>(); // pants
        var sk  = new HashSet<string>(); // skates
        var nm  = new HashSet<string>(); // number
        var lg  = new HashSet<string>(); // logo

        foreach (var (f, _) in skaters)
        {
            if (!string.IsNullOrEmpty(f.headSkin))      hd.Add(f.headSkin);
            if (!string.IsNullOrEmpty(f.bodySkin))      bd.Add(f.bodySkin);
            if (!string.IsNullOrEmpty(f.bodyAwaySkin))  bd.Add(f.bodyAwaySkin);
            if (!string.IsNullOrEmpty(f.helmetSkin))    hm.Add(f.helmetSkin);
            if (!string.IsNullOrEmpty(f.helmetAwaySkin))hm.Add(f.helmetAwaySkin);
            if (!string.IsNullOrEmpty(f.stickSkin))     st.Add(f.stickSkin);
            if (!string.IsNullOrEmpty(f.glassesSkin))   gl.Add(f.glassesSkin);
            if (!string.IsNullOrEmpty(f.numberSkin))    nm.Add(f.numberSkin);
            if (!string.IsNullOrEmpty(f.logoSkin))      lg.Add(f.logoSkin);
            try { if (!string.IsNullOrEmpty(f.bicepSkin))    bc.Add(f.bicepSkin); }    catch {}
            try { if (!string.IsNullOrEmpty(f.bicepAwaySkin))bc.Add(f.bicepAwaySkin); } catch {}
            try { if (!string.IsNullOrEmpty(f.gloveSkin))    gv.Add(f.gloveSkin); }    catch {}
            try { if (!string.IsNullOrEmpty(f.gloveAwaySkin))gv.Add(f.gloveAwaySkin); } catch {}
            try { if (!string.IsNullOrEmpty(f.pantsSkin))    pt2.Add(f.pantsSkin); }   catch {}
            try { if (!string.IsNullOrEmpty(f.pantsAwaySkin))pt2.Add(f.pantsAwaySkin);} catch {}
            try { if (!string.IsNullOrEmpty(f.skateSkin))    sk.Add(f.skateSkin); }    catch {}
            try { if (!string.IsNullOrEmpty(f.skateAwaySkin))sk.Add(f.skateAwaySkin); } catch {}
            AddReflectedList(f, "_headSkins",   hd);
            AddReflectedList(f, "_bodySkins",   bd);
            AddReflectedList(f, "_helmetSkins", hm);
            AddReflectedList(f, "_stickSkins",  st);
        }

        var ghd = new HashSet<string>(); // goalie face
        var gbd = new HashSet<string>(); // goalie body
        var ghm = new HashSet<string>(); // goalie helmet
        var ggv = new HashSet<string>(); // goalie gloves
        var gbl = new HashSet<string>(); // goalie blocker
        var gpd = new HashSet<string>(); // goalie pads
        var gst = new HashSet<string>(); // goalie stick

        foreach (var (g, _) in goalies)
        {
            if (!string.IsNullOrEmpty(g.headSkin))         ghd.Add(g.headSkin);
            try { if (!string.IsNullOrEmpty(g.skin))            gbd.Add(g.skin); }            catch {}
            try { if (!string.IsNullOrEmpty(g.awaySkin))        gbd.Add(g.awaySkin); }        catch {}
            try { if (!string.IsNullOrEmpty(g.helmetSkin))      ghm.Add(g.helmetSkin); }      catch {}
            try { if (!string.IsNullOrEmpty(g.gloveSkin))       ggv.Add(g.gloveSkin); }       catch {}
            try { if (!string.IsNullOrEmpty(g.awayGloveSkin))   ggv.Add(g.awayGloveSkin); }   catch {}
            try { if (!string.IsNullOrEmpty(g.blockerSkin))     gbl.Add(g.blockerSkin); }     catch {}
            try { if (!string.IsNullOrEmpty(g.awayBlockerSkin)) gbl.Add(g.awayBlockerSkin); } catch {}
            try { if (!string.IsNullOrEmpty(g.padsSkin))        gpd.Add(g.padsSkin); }        catch {}
            try { if (!string.IsNullOrEmpty(g.awayPadsSkin))    gpd.Add(g.awayPadsSkin); }    catch {}
            try { if (!string.IsNullOrEmpty(g.stickSkin))       gst.Add(g.stickSkin); }       catch {}
            try { if (!string.IsNullOrEmpty(g.awayStickSkin))   gst.Add(g.awayStickSkin); }   catch {}
            AddReflectedList(g, "_headSkins",        ghd);
            AddReflectedList(g, "_bodySkins",        gbd);
            AddReflectedList(g, "_helmetSkins",      ghm);
            AddReflectedList(g, "_gloveSkins",       ggv);
            AddReflectedList(g, "_awayGloveSkins",   ggv);
            AddReflectedList(g, "_blockerSkins",     gbl);
            AddReflectedList(g, "_awayBlockerSkins", gbl);
            AddReflectedList(g, "_padsSkins",        gpd);
            AddReflectedList(g, "_awayPadsSkins",    gpd);
            AddReflectedList(g, "_stickSkins",       gst);
            AddReflectedList(g, "_awayStickSkins",   gst);
        }

        // ── Helper: write one skin file ───────────────────────────────────
        void WriteSkinFile(string dir, string filename, string header, HashSet<string> skins, bool byFolder = false)
        {
            try
            {
                if (skins.Count == 0) return;
                var sb2 = new StringBuilder();
                sb2.AppendLine($"=== {header} ({skins.Count}) ===");
                sb2.AppendLine($"Generated: {DateTime.Now}");
                sb2.AppendLine("SHORT NAME (left col) goes in config files. Full asset path on the right.");
                sb2.AppendLine();
                WriteSkinSection(sb2, header, skins, byFolder);
                File.WriteAllText(Path.Combine(dir, filename), sb2.ToString());
                Plugin.Log.LogInfo($"[Dump] {filename} ({skins.Count})");
            }
            catch (Exception ex) { Plugin.Log.LogError($"[Dump] {filename}: {ex.Message}"); }
        }

        // ── skins_skater/ ─────────────────────────────────────────────────
        WriteSkinFile(dirSkSkins, "FACE.txt",    "SKATER FACES",   hd,  byFolder: true);
        WriteSkinFile(dirSkSkins, "BODY.txt",    "SKATER BODY",    bd);
        WriteSkinFile(dirSkSkins, "HELMET.txt",  "SKATER HELMET",  hm);
        WriteSkinFile(dirSkSkins, "STICK.txt",   "SKATER STICK",   st);
        WriteSkinFile(dirSkSkins, "SKATES.txt",  "SKATER SKATES",  sk);
        WriteSkinFile(dirSkSkins, "PANTS.txt",   "SKATER PANTS",   pt2);
        WriteSkinFile(dirSkSkins, "GLOVES.txt",  "SKATER GLOVES",  gv);
        WriteSkinFile(dirSkSkins, "BICEP.txt",   "SKATER BICEP",   bc);
        WriteSkinFile(dirSkSkins, "GLASSES.txt", "SKATER GLASSES", gl);
        WriteSkinFile(dirSkSkins, "NUMBER.txt",  "SKATER NUMBER",  nm);
        WriteSkinFile(dirSkSkins, "LOGO.txt",    "SKATER LOGO",    lg);

        // ── skins_goalie/ ─────────────────────────────────────────────────
        WriteSkinFile(dirGkSkins, "FACE.txt",    "GOALIE FACE",    ghd, byFolder: true);
        WriteSkinFile(dirGkSkins, "BODY.txt",    "GOALIE BODY",    gbd);
        WriteSkinFile(dirGkSkins, "HELMET.txt",  "GOALIE HELMET",  ghm);
        WriteSkinFile(dirGkSkins, "GLOVES.txt",  "GOALIE GLOVES",  ggv);
        WriteSkinFile(dirGkSkins, "BLOCKER.txt", "GOALIE BLOCKER", gbl);
        WriteSkinFile(dirGkSkins, "PADS.txt",    "GOALIE PADS",    gpd);
        WriteSkinFile(dirGkSkins, "STICK.txt",   "GOALIE STICK",   gst);

        // ── players/SKATERS.txt ───────────────────────────────────────────
        try
        {
            var sb2 = new StringBuilder();
            sb2.AppendLine($"=== SKATERS ({skaters.Count}) ===");
            sb2.AppendLine($"Generated: {DateTime.Now}");
            sb2.AppendLine("All forwards from base-game teams (no user-created players).");
            sb2.AppendLine();
            foreach (var (f, tn) in skaters)
            {
                sb2.AppendLine($"--- {f.firstName} {f.lastName} [{tn}] ---");
                sb2.AppendLine($"  speed: {f.speed}  shotPower: {f.shotPower}  shotAccuracy: {f.shotAccuracy}  checking: {f.checking}");
                sb2.AppendLine($"  size: {f.skaterSize}  sizeOffset: {f.sizeOffsetPercentage}  isLefty: {f.isLefty}  number: {f.number}");
                sb2.AppendLine($"  headSkin: \"{f.headSkin ?? ""}\"");
                sb2.AppendLine($"  bodySkin: \"{f.bodySkin ?? ""}\"  bodyAwaySkin: \"{f.bodyAwaySkin ?? ""}\"");
                sb2.AppendLine($"  helmetSkin: \"{f.helmetSkin ?? ""}\"  helmetAwaySkin: \"{f.helmetAwaySkin ?? ""}\"");
                sb2.AppendLine($"  stickSkin: \"{f.stickSkin ?? ""}\"");
                sb2.AppendLine($"  glassesSkin: \"{f.glassesSkin ?? ""}\"  numberSkin: \"{f.numberSkin ?? ""}\"  logoSkin: \"{f.logoSkin ?? ""}\"");
                try { sb2.AppendLine($"  bicepSkin: \"{f.bicepSkin ?? ""}\"  gloveSkin: \"{f.gloveSkin ?? ""}\"  pantsSkin: \"{f.pantsSkin ?? ""}\"  skateSkin: \"{f.skateSkin ?? ""}\""); } catch {}
                if (f.ability != null) sb2.AppendLine($"  ability: \"{f.ability.name}\"");
                if (f.powerups != null && f.powerups.Count > 0)
                {
                    sb2.Append("  talents: [");
                    for (int j = 0; j < f.powerups.Count; j++)
                    { if (j > 0) sb2.Append(", "); sb2.Append($"\"{f.powerups[j]?.name ?? "null"}\""); }
                    sb2.AppendLine("]");
                }
                sb2.AppendLine();
            }
            File.WriteAllText(Path.Combine(dirPlayers, "SKATERS.txt"), sb2.ToString());
            Plugin.Log.LogInfo($"[Dump] players/SKATERS.txt ({skaters.Count})");
        }
        catch (Exception ex) { Plugin.Log.LogError($"[Dump] SKATERS.txt: {ex}"); }

        // ── players/GOALIES.txt ───────────────────────────────────────────
        try
        {
            var sb2 = new StringBuilder();
            sb2.AppendLine($"=== GOALIES ({goalies.Count}) ===");
            sb2.AppendLine($"Generated: {DateTime.Now}");
            sb2.AppendLine("All goalies from base-game teams.");
            sb2.AppendLine();
            foreach (var (g, tn) in goalies)
            {
                sb2.AppendLine($"--- {g.firstName} {g.lastName} [{tn}] ---");
                sb2.AppendLine($"  skill: {g.skill}  catching: {g.catchingSkill}  glove: {g.gloveSkill}  blocker: {g.blockerSkill}");
                sb2.AppendLine($"  fiveHole: {g.fiveHoleSkill}  standSpd: {g.standingSpeed}  buttSpd: {g.butterflySpeed}");
                sb2.AppendLine($"  control: {g.controlSkill}  recovery: {g.recoverySkill}  passPower: {g.passPower}");
                sb2.AppendLine($"  shotPower: {g.shotPower}  pokecheck: {g.pokecheckSkill}  depth: {g.depth}  passRead: {g.passReadSkill}");
                sb2.AppendLine($"  headSkin: \"{g.headSkin ?? ""}\"");
                try { sb2.AppendLine($"  skin: \"{g.skin ?? ""}\"  awaySkin: \"{g.awaySkin ?? ""}\""); } catch {}
                try { sb2.AppendLine($"  helmetSkin: \"{g.helmetSkin ?? ""}\""); } catch {}
                try { sb2.AppendLine($"  gloveSkin: \"{g.gloveSkin ?? ""}\"  awayGloveSkin: \"{g.awayGloveSkin ?? ""}\""); } catch {}
                try { sb2.AppendLine($"  blockerSkin: \"{g.blockerSkin ?? ""}\"  awayBlockerSkin: \"{g.awayBlockerSkin ?? ""}\""); } catch {}
                try { sb2.AppendLine($"  padsSkin: \"{g.padsSkin ?? ""}\"  awayPadsSkin: \"{g.awayPadsSkin ?? ""}\""); } catch {}
                try { sb2.AppendLine($"  stickSkin: \"{g.stickSkin ?? ""}\"  awayStickSkin: \"{g.awayStickSkin ?? ""}\""); } catch {}
                try { sb2.AppendLine($"  logoSkin: \"{g.logoSkin ?? ""}\""); } catch {}
                if (g.powerups != null && g.powerups.Count > 0)
                {
                    sb2.Append("  talents: [");
                    for (int j = 0; j < g.powerups.Count; j++)
                    { if (j > 0) sb2.Append(", "); sb2.Append($"\"{g.powerups[j]?.name ?? "null"}\""); }
                    sb2.AppendLine("]");
                }
                sb2.AppendLine();
            }
            File.WriteAllText(Path.Combine(dirPlayers, "GOALIES.txt"), sb2.ToString());
            Plugin.Log.LogInfo($"[Dump] players/GOALIES.txt ({goalies.Count})");
        }
        catch (Exception ex) { Plugin.Log.LogError($"[Dump] GOALIES.txt: {ex}"); }

        // ── players/FREE_AGENTS.txt ───────────────────────────────────────
        // Players in memory not on any base-game team roster = draft pool / GM node candidates.
        try
        {
            var sb2 = new StringBuilder();
            sb2.AppendLine("=== FREE AGENTS / DRAFT POOL ===");
            sb2.AppendLine($"Generated: {DateTime.Now}");
            sb2.AppendLine("Forwards in memory not on any base-game team. These appear in the GM node.");
            sb2.AppendLine();
            var allMem = UnityEngine.Resources.FindObjectsOfTypeAll<ForwardData>();
            int freeCount = 0;
            if (allMem != null)
            {
                foreach (var f in allMem)
                {
                    if (f == null || string.IsNullOrEmpty(f.firstName)) continue;
                    if (ownedPtr.Contains(f.Pointer)) continue;
                    freeCount++;
                    sb2.AppendLine($"{f.firstName} {f.lastName}");
                    sb2.AppendLine($"  speed: {f.speed}  shotPower: {f.shotPower}  shotAccuracy: {f.shotAccuracy}  checking: {f.checking}");
                    sb2.AppendLine($"  headSkin: \"{f.headSkin ?? ""}\"  bodySkin: \"{f.bodySkin ?? ""}\"");
                    if (f.ability != null) sb2.AppendLine($"  ability: \"{f.ability.name}\"");
                    sb2.AppendLine();
                }
            }
            File.WriteAllText(Path.Combine(dirPlayers, "FREE_AGENTS.txt"), sb2.ToString());
            Plugin.Log.LogInfo($"[Dump] players/FREE_AGENTS.txt ({freeCount})");
        }
        catch (Exception ex) { Plugin.Log.LogError($"[Dump] FREE_AGENTS.txt: {ex}"); }

        // ── Helper: write a teams file ────────────────────────────────────
        void WriteTeamsFile(string filename, string header, List<TeamData> teams)
        {
            try
            {
                var sb2 = new StringBuilder();
                sb2.AppendLine($"=== {header} ({teams.Count} teams) ===");
                sb2.AppendLine($"Generated: {DateTime.Now}");
                sb2.AppendLine();
                foreach (var team in teams)
                {
                    if (team == null) continue;
                    int fwdCount = team.forwards?.Count ?? 0;
                    sb2.AppendLine($"===== {team.teamName} ({fwdCount} forwards) =====");
                    if (team.forwards != null)
                        for (int i = 0; i < team.forwards.Count; i++)
                        {
                            var f = team.forwards[i];
                            if (f == null) continue;
                            sb2.AppendLine($"  [{i+1}] {f.firstName} {f.lastName}  spd={f.speed} pwr={f.shotPower} acc={f.shotAccuracy} chk={f.checking}");
                            sb2.AppendLine($"       head=\"{f.headSkin ?? ""}\"  body=\"{f.bodySkin ?? ""}\"");
                        }
                    if (team.goalie != null)
                    {
                        var g = team.goalie;
                        sb2.AppendLine($"  [G] {g.firstName} {g.lastName}  skill={g.skill} catch={g.catchingSkill} glove={g.gloveSkill} blocker={g.blockerSkill}");
                        sb2.AppendLine($"       head=\"{g.headSkin ?? ""}\"");
                    }
                    sb2.AppendLine();
                }
                File.WriteAllText(Path.Combine(dirTeams, filename), sb2.ToString());
                Plugin.Log.LogInfo($"[Dump] teams/{filename} ({teams.Count})");
            }
            catch (Exception ex) { Plugin.Log.LogError($"[Dump] {filename}: {ex}"); }
        }

        // ── teams/ ────────────────────────────────────────────────────────
        WriteTeamsFile("ALL_TEAMS.txt",      "ALL BASE GAME TEAMS",  allBaseTeams);
        WriteTeamsFile("PLAYABLE_TEAMS.txt", "PLAYABLE LEAGUE TEAMS",leagueTeams);
        WriteTeamsFile("CAMPAIGN_TEAMS.txt", "CAMPAIGN TEAMS",       campaignTeams);

        // ── logos/LOGOS.txt ───────────────────────────────────────────────
        try
        {
            var sb2 = new StringBuilder();
            sb2.AppendLine("=== TEAM LOGOS ===");
            sb2.AppendLine($"Generated: {DateTime.Now}");
            sb2.AppendLine("PNG files are in: AppData/LocalLow/Excellent Rectangle/Tape to Tape/CustomLogos/");
            sb2.AppendLine("Use the team name (without .png) as the logo value in config files.");
            sb2.AppendLine();
            sb2.AppendLine("--- LEAGUE TEAMS ---");
            foreach (var t in leagueTeams)   sb2.AppendLine($"  {t.teamName}");
            sb2.AppendLine();
            sb2.AppendLine("--- CAMPAIGN TEAMS ---");
            foreach (var t in campaignTeams) sb2.AppendLine($"  {t.teamName}");
            File.WriteAllText(Path.Combine(dirLogos, "LOGOS.txt"), sb2.ToString());
            Plugin.Log.LogInfo($"[Dump] logos/LOGOS.txt ({allBaseTeams.Count})");
        }
        catch (Exception ex) { Plugin.Log.LogError($"[Dump] LOGOS.txt: {ex}"); }
    }

    private static bool _guiListsDumped = false;

    /// <summary>Write _game_maps.txt: the real layer/node layout of every map the
    /// game ships, so the Creator can offer a per-NODE team editor instead of the
    /// current "teams play in filename order" model.
    ///
    /// Everything the editor needs is already in the game's own data:
    ///   RunSquadScriptableObject.maps → MapConfig (one per act)
    ///     MapConfig.mapTemplates      → MapTemplateData (layout VARIANTS)
    ///       MapTemplateData.layers    → LayerData.layerIndex + nodes
    ///         NodeData: nodeIndex, type, gridPosition, outgoing (the branch graph),
    ///                   eliteGroupId, eliteTeamId ("Force Elite Team"), rewards,
    ///                   gmSelectionCount
    ///
    /// ALL templates are dumped, not just [0]. Whether the variants of one map share
    /// a layer/branch shape decides whether a node is a stable thing a user can
    /// assign a team to at all — if they differ, node identity is not stable across
    /// runs and the mod would have to pin one template. The SHAPE= line and the
    /// closing SUMMARY exist to answer exactly that question at a glance.
    ///
    /// Strictly read-only: it walks ScriptableObjects and writes a text file.</summary>
    internal static void DumpMapLayouts(string root)
    {
        try
        {
            var squads = UnityEngine.Resources.FindObjectsOfTypeAll<RunSquadScriptableObject>();
            if (squads == null || squads.Length == 0)
            {
                Plugin.Log.LogInfo("[Dump] Map layouts: no RunSquadScriptableObject loaded yet — skipping.");
                return;
            }

            var sb = new StringBuilder();
            // Plain ASCII throughout this file: it gets parsed by _gen_game_data.py
            // and the Creator, and an em-dash here only invites an encoding bug.
            sb.AppendLine("# _game_maps.txt - map layouts read from the game. AUTO-GENERATED, do not edit.");
            sb.AppendLine("# Regenerated every launch. Structure: SQUAD > MAP(act) > TEMPLATE > LAYER > NODE");
            sb.AppendLine("# NODE: n=<nodeIndex> type=<NodeType> pos=<x>,<y> out=[<nodeIndex>,..]");
            sb.AppendLine("#       group='<eliteGroupId>' team='<eliteTeamId>' gm=<n> talents=<n> relics=<n>");
            sb.AppendLine("# SHAPE=[nodes per layer] — templates of one map with equal SHAPE are");
            sb.AppendLine("# structurally interchangeable; see SUMMARY at the end.");
            sb.AppendLine();

            // squad -> act -> list of shape strings, for the summary at the end.
            var shapeIndex = new List<string>();
            int squadCount = 0, mapCount = 0, tmplCount = 0, nodeCount = 0;

            foreach (var sq in squads)
            {
                if (sq == null) continue;
                string sqName = null;
                try { sqName = sq.squadName; } catch { }
                if (string.IsNullOrEmpty(sqName)) sqName = "(unnamed)";

                Il2CppSystem.Collections.Generic.List<MapConfig> maps = null;
                try { maps = sq.maps; } catch { }
                if (maps == null || maps.Count == 0) continue;

                squadCount++;
                sb.AppendLine($"SQUAD '{sqName}' maps={maps.Count}");

                for (int mi = 0; mi < maps.Count; mi++)
                {
                    var cfg = maps[mi];
                    if (cfg == null) continue;
                    int act = 0;
                    try { act = cfg.act; } catch { }
                    Il2CppSystem.Collections.Generic.List<DAGG.Generation.Template.MapTemplateData> tmpls = null;
                    try { tmpls = cfg.mapTemplates; } catch { }
                    int tcount = tmpls != null ? tmpls.Count : 0;
                    mapCount++;
                    sb.AppendLine($"  MAP index={mi} act={act} templates={tcount}");
                    if (tmpls == null) continue;

                    var shapesForThisMap = new List<string>();

                    for (int ti = 0; ti < tcount; ti++)
                    {
                        var tmpl = tmpls[ti];
                        if (tmpl == null) continue;
                        Il2CppSystem.Collections.Generic.List<DAGG.Generation.Template.LayerData> layers = null;
                        try { layers = tmpl.layers; } catch { }
                        if (layers == null) continue;

                        // SHAPE is the per-layer node count — the cheapest signature
                        // that reveals whether two variants branch the same way.
                        var shape = new StringBuilder();
                        for (int li = 0; li < layers.Count; li++)
                        {
                            var lay = layers[li];
                            int n = 0;
                            try { n = lay != null && lay.nodes != null ? lay.nodes.Count : 0; } catch { }
                            shape.Append(shape.Length == 0 ? "" : ",").Append(n);
                        }
                        string shapeStr = "[" + shape.ToString() + "]";
                        shapesForThisMap.Add(shapeStr);
                        tmplCount++;

                        string tname = null;
                        try { tname = tmpl.name; } catch { }
                        sb.AppendLine($"    TEMPLATE {ti} '{tname}' layers={layers.Count} SHAPE={shapeStr}");

                        for (int li = 0; li < layers.Count; li++)
                        {
                            var lay = layers[li];
                            if (lay == null) continue;
                            int layerIndex = li;
                            try { layerIndex = lay.layerIndex; } catch { }
                            Il2CppSystem.Collections.Generic.List<DAGG.Generation.Template.NodeData> nodes = null;
                            try { nodes = lay.nodes; } catch { }
                            int ncount = nodes != null ? nodes.Count : 0;
                            sb.AppendLine($"      LAYER {layerIndex} nodes={ncount}");
                            if (nodes == null) continue;

                            for (int ni = 0; ni < ncount; ni++)
                            {
                                var nd = nodes[ni];
                                if (nd == null) continue;
                                nodeCount++;

                                int nodeIndex = ni;
                                try { nodeIndex = nd.nodeIndex; } catch { }

                                // DAGG.Core.NodeType — name it via the enum it really
                                // belongs to. STS.Map.NodeType has DIFFERENT values for
                                // the same names (GeneralManager 9 vs 8), so never
                                // resolve this through the other enum.
                                string typeName;
                                try { typeName = nd.type.ToString(); }
                                catch { try { typeName = ((int)nd.type).ToString(); } catch { typeName = "?"; } }

                                string pos = "";
                                try
                                {
                                    var gp = nd.gridPosition;   // Coordinate is a CLASS, not a boxed struct
                                    if (gp != null) pos = $"{gp.x:0.##},{gp.y:0.##}";
                                }
                                catch { }

                                var outs = new StringBuilder();
                                try
                                {
                                    var og = nd.outgoing;
                                    if (og != null)
                                        for (int oi = 0; oi < og.Count; oi++)
                                            outs.Append(outs.Length == 0 ? "" : ",").Append(og[oi]);
                                }
                                catch { }

                                string grp = "", team = "";
                                try { grp = nd.eliteGroupId ?? ""; } catch { }
                                try { team = nd.eliteTeamId ?? ""; } catch { }
                                int gm = 0, tal = 0, rel = 0;
                                try { gm = nd.gmSelectionCount; } catch { }
                                try { tal = nd.talentRewardCount; } catch { }
                                try { rel = nd.relicRewardCount; } catch { }

                                sb.AppendLine($"        NODE n={nodeIndex} type={typeName} pos={pos}"
                                    + $" out=[{outs}] group='{grp}' team='{team}'"
                                    + $" gm={gm} talents={tal} relics={rel}");
                            }
                        }
                    }

                    // Record whether this map's variants agree on structure.
                    if (shapesForThisMap.Count > 0)
                    {
                        bool identical = true;
                        for (int i = 1; i < shapesForThisMap.Count; i++)
                            if (shapesForThisMap[i] != shapesForThisMap[0]) { identical = false; break; }
                        shapeIndex.Add($"  '{sqName}' map {mi} (act {act}): {shapesForThisMap.Count} template(s) — "
                            + (identical ? $"shapes IDENTICAL {shapesForThisMap[0]}"
                                         : $"shapes DIFFER: {string.Join(" vs ", shapesForThisMap.ToArray())}"));
                    }
                }
                sb.AppendLine();
            }

            sb.AppendLine("SUMMARY - do a map's template variants share a branch shape?");
            sb.AppendLine("(If any line says DIFFER, a node is not a stable target across runs and the");
            sb.AppendLine(" mod must pin one template before per-node team assignment can be offered.)");
            foreach (var line in shapeIndex) sb.AppendLine(line);

            // NODE team='<guid>' is a TeamData.id — the game's own "Force Elite Team".
            // The Gauntlet templates use it to pin a specific opponent per node, so
            // these ids are the only way to tell which team a node ALREADY faces by
            // default. Without this table those GUIDs are unreadable.
            try
            {
                sb.AppendLine();
                sb.AppendLine("TEAM IDS - resolves NODE team='<guid>' above (TeamData.id -> teamName)");
                var seen = new HashSet<string>();
                var rows = new List<string>();
                var allTeams = UnityEngine.Resources.FindObjectsOfTypeAll<TeamData>();
                if (allTeams != null)
                    foreach (var t in allTeams)
                    {
                        if (t == null) continue;
                        string tid = null, tn = null;
                        try { tid = t.id; tn = t.teamName; } catch { }
                        if (string.IsNullOrEmpty(tid) || !seen.Add(tid)) continue;
                        rows.Add($"  {tid} = {tn}");
                    }
                rows.Sort(StringComparer.OrdinalIgnoreCase);
                foreach (var r in rows) sb.AppendLine(r);
                sb.AppendLine($"  ({rows.Count} team id(s))");
            }
            catch (Exception ex) { sb.AppendLine($"  (team id table failed: {ex.Message})"); }

            File.WriteAllText(Path.Combine(root, "_game_maps.txt"), sb.ToString());
            Plugin.Log.LogInfo($"[Dump] Map layouts: {squadCount} squad(s), {mapCount} map(s), "
                + $"{tmplCount} template(s), {nodeCount} node(s) -> _game_maps.txt");
        }
        catch (Exception ex) { Plugin.Log.LogError($"[Dump] DumpMapLayouts failed: {ex}"); }
    }

    /// <summary>True once the GUI lists + library have actually been written.
    /// AutoDumpNameLists gives up early if TeamData isn't loaded yet and expects to
    /// be called again, so callers that fire once per event need this to know
    /// whether to keep trying.</summary>
    internal static bool GuiListsDumped => _guiListsDumped;
    public static void AutoDumpNameLists()
    {
        if (_guiListsDumped) return;

        // Full T2T_Dumps — runs regardless of whether the mod content folder exists.
        // Run only once (SeparateFilesWritten guards re-entry).
        if (!Plugin.SeparateFilesWritten)
        {
            Plugin.SeparateFilesWritten = true;
            try { WriteSeparateFiles(); }
            catch (Exception ex) { Plugin.Log.LogError($"[Dump] WriteSeparateFiles failed: {ex}"); }
            try { DumpPlayerLooks(); }
            catch (Exception ex) { Plugin.Log.LogError($"[Dump] DumpPlayerLooks failed: {ex}"); }
        }

        string root = Plugin.ModContentRoot;
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return;

        var allTeams = UnityEngine.Resources.FindObjectsOfTypeAll<TeamData>();
        if (allTeams == null || allTeams.Length == 0)
        {
            Plugin.Log.LogInfo("[Dump] AutoDumpNameLists: TeamData not loaded yet — will retry on next trigger.");
            return;
        }

        // Mark done only AFTER we have team data so callers can retry if teams weren't ready.
        _guiListsDumped = true;

        // Map layer/node layouts, for the per-node team editor. Read-only.
        try { DumpMapLayouts(root); }
        catch (Exception ex) { Plugin.Log.LogWarning($"[Dump] Map layouts: {ex.Message}"); }

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
            var skaterNames = new HashSet<string>();
            var goalieNames = new HashSet<string>();
            if (allPlayers != null)
                foreach (var p in allPlayers)
                    if (p != null && !string.IsNullOrEmpty(p.firstName))
                        skaterNames.Add($"{p.firstName} {p.lastName}".Trim());
            if (allGoalies != null)
                foreach (var g in allGoalies)
                    if (g != null && !string.IsNullOrEmpty(g.firstName))
                        goalieNames.Add($"{g.firstName} {g.lastName}".Trim());
            var sortedS = new List<string>(skaterNames); sortedS.Sort();
            var sortedG = new List<string>(goalieNames); sortedG.Sort();
            // Backward-compat combined list + separate typed lists for the GUI dropdowns
            var allSorted = new List<string>(skaterNames); allSorted.AddRange(goalieNames); allSorted.Sort();
            File.WriteAllLines(Path.Combine(root, "_game_player_names.txt"), allSorted.ToArray());
            File.WriteAllLines(Path.Combine(root, "_game_skater_names.txt"), sortedS.ToArray());
            File.WriteAllLines(Path.Combine(root, "_game_goalie_names.txt"), sortedG.ToArray());
            Plugin.Log.LogInfo($"[Dump] Name lists: {teamNames.Count} teams, {sortedS.Count} skaters, {sortedG.Count} goalies");

            DumpSkinFields(root, allPlayers, allGoalies);
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[Dump] Name lists failed: {ex.Message}"); }

        // (skin-field dump lives in DumpSkinFields, called above)

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

        // Only exclude the user's personal in-game team (runtime-created, not a real game team)
        var excludeTeams = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "My Team" };

        // Detect custom (in-game-editor) teams by reading CustomTeam-*.json from the
        // game's save folder. This is more reliable than a hardcoded name whitelist
        // because base game teams change with every game update and the whitelist rots.
        // Anything NOT in this set is treated as a base game team.
        var customEditorTeamNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            string tdmPath = Path.Combine(UnityEngine.Application.persistentDataPath, "TeamDataModels");
            if (Directory.Exists(tdmPath))
            {
                foreach (var jsonFile in Directory.GetFiles(tdmPath, "CustomTeam-*.json"))
                {
                    string json = File.ReadAllText(jsonFile);
                    var m = System.Text.RegularExpressions.Regex.Match(
                        json, "\"TeamName\"\\s*:\\s*\"([^\"]+)\"");
                    if (m.Success)
                        customEditorTeamNames.Add(m.Groups[1].Value);
                }
                Plugin.Log.LogInfo($"[Dump] Detected {customEditorTeamNames.Count} custom (in-game editor) teams in TeamDataModels");
            }
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[Dump] Custom team detection: {ex.Message}"); }

        string customTeamsDir = Path.Combine(root, "library", "Custom Teams");
        string customPlayersDir = Path.Combine(root, "library", "Custom Players");

        // Create all four up front. These used to be created near the END of this
        // routine, AFTER the player files were written into them — which worked
        // only because the folders already existed from a previous run. On a fresh
        // or deleted library every player write failed with "Could not find a part
        // of the path" and the dump reported "0 players", while teams (which make
        // their own subfolder) came through fine. The later calls are idempotent.
        try
        {
            Directory.CreateDirectory(baseGameDir);
            Directory.CreateDirectory(basePlayersDir);
            Directory.CreateDirectory(customTeamsDir);
            Directory.CreateDirectory(customPlayersDir);
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[Dump] Could not create library folders: {ex.Message}"); }

        Plugin.Log.LogInfo($"[Dump] Dumping all game teams + players to library...");
        int teamCount = 0;
        foreach (var team in allTeams)
        {
            if (team == null || string.IsNullOrEmpty(team.teamName)) continue;
            if (excludeTeams.Contains(team.teamName.Trim())) continue;
            try
            {
                bool isCustomEditor = customEditorTeamNames.Contains(team.teamName.Trim());
                string targetTeamDir = isCustomEditor ? customTeamsDir : baseGameDir;
                DumpTeamToLibrary(targetTeamDir, team);
                teamCount++;
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"[Dump] Team '{team.teamName}' failed: {ex.Message}"); }
        }

        // Dump EVERY ForwardData and GoaltenderData in memory to Base Game Players /
        // Custom Players — regardless of whether they're on a team roster.
        // This is the only way to capture free agents, bench players, and any other
        // player objects that aren't reachable via team.forwards / team.goalie.
        try
        {
            var allFwds = UnityEngine.Resources.FindObjectsOfTypeAll<ForwardData>();
            var allGkps = UnityEngine.Resources.FindObjectsOfTypeAll<GoaltenderData>();
            int playerCount = 0;

            // Build two pointer sets:
            //   baseGamePlayerPtrs — players on ANY base game team (takes priority)
            //   customOnlyPtrs     — players exclusively on in-game editor teams
            // If the editor reuses a base-game ForwardData object, that player is
            // already in baseGamePlayerPtrs and stays in Base Game Players.
            var baseGamePlayerPtrs = new HashSet<System.IntPtr>();
            var customOnlyPtrs    = new HashSet<System.IntPtr>();
            foreach (var td in allTeams)
            {
                if (td == null) continue;
                bool isCustomEditor = customEditorTeamNames.Contains(td.teamName?.Trim() ?? "");
                if (td.forwards != null)
                    foreach (var f in td.forwards) { if (f != null) (isCustomEditor ? customOnlyPtrs : baseGamePlayerPtrs).Add(f.Pointer); }
                if (td.goalie != null)
                    (isCustomEditor ? customOnlyPtrs : baseGamePlayerPtrs).Add(td.goalie.Pointer);
            }
            // Remove from custom set any pointer that also appears on a base game team
            customOnlyPtrs.ExceptWith(baseGamePlayerPtrs);

            if (allFwds != null)
            {
                foreach (var f in allFwds)
                {
                    if (f == null) continue;
                    string pname = $"{f.firstName} {f.lastName}".Trim();
                    if (string.IsNullOrEmpty(pname)) continue;
                    string dir = customOnlyPtrs.Contains(f.Pointer) ? customPlayersDir : basePlayersDir;
                    try { DumpSkaterToFile(dir, "Center", f, flat: true); playerCount++; }
                    catch (Exception ex) { Plugin.Log.LogWarning($"[Dump] Player '{pname}': {ex.Message}"); }
                }
            }
            if (allGkps != null)
            {
                foreach (var g in allGkps)
                {
                    if (g == null) continue;
                    string pname = $"{g.firstName} {g.lastName}".Trim();
                    if (string.IsNullOrEmpty(pname)) continue;
                    string dir = customOnlyPtrs.Contains(g.Pointer) ? customPlayersDir : basePlayersDir;
                    try { DumpGoalieToFile(dir, g, flat: true); playerCount++; }
                    catch (Exception ex) { Plugin.Log.LogWarning($"[Dump] Goalie '{pname}': {ex.Message}"); }
                }
            }
            Plugin.Log.LogInfo($"[Dump] Dumped {playerCount} players to library (all in memory)");
        }
        catch (Exception ex) { Plugin.Log.LogError($"[Dump] Player dump failed: {ex}"); }

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
                // Never export the repository's fallback sprite. A team whose logo
                // never resolved still carries defaultLogo ('Teams_TapetoTape'),
                // and dumping that wrote the game's own house logo out under the
                // team's name — 34 identical copies, which then shadowed the real
                // artwork and looked like "custom logos don't work".
                if (IsFallbackLogo(t.logo))
                {
                    Plugin.Log.LogDebug($"[Dump] Logo '{t.teamName}' is the fallback sprite — not exported");
                    continue;
                }

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

    // The sprite TeamAssetsRepositoryScriptableObject.GetLogo() hands back for
    // ids it doesn't know. Its name is stable ('Teams_TapetoTape'), so match on
    // that rather than on pixels.
    internal static bool IsFallbackLogo(UnityEngine.Sprite s)
    {
        if (s == null) return false;
        try { return (s.name ?? "").IndexOf("TapetoTape", StringComparison.OrdinalIgnoreCase) >= 0; }
        catch { return false; }
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
        try { if (!string.IsNullOrEmpty(f.bicepAwaySkin)) sb.AppendLine($"Bicep Away              = {PatchBossLaunchMatch.ReverseSkinPath(f.bicepAwaySkin, "bicep")}"); } catch {}
        try { sb.AppendLine($"Gloves                  = {PatchBossLaunchMatch.ReverseSkinPath(f.gloveSkin, "gloves")}"); } catch {}
        try { if (!string.IsNullOrEmpty(f.gloveAwaySkin)) sb.AppendLine($"Gloves Away             = {PatchBossLaunchMatch.ReverseSkinPath(f.gloveAwaySkin, "gloves")}"); } catch {}
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
        try { sb.AppendLine($"Skill                   = {g.skill}"); } catch {}
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
        try { if (!string.IsNullOrEmpty(g.helmetSkin)) sb.AppendLine($"Helmet Skin             = {PatchBossLaunchMatch.ReverseSkinPath(g.helmetSkin, "helmet", goalie: true)}"); } catch {}
        try { if (!string.IsNullOrEmpty(g.skin)) sb.AppendLine($"Skin                    = {PatchBossLaunchMatch.ReverseSkinPath(g.skin, "body", goalie: true)}"); } catch {}
        try { if (!string.IsNullOrEmpty(g.awaySkin)) sb.AppendLine($"Skin Away               = {PatchBossLaunchMatch.ReverseSkinPath(g.awaySkin, "body", goalie: true)}"); } catch {}
        try { if (!string.IsNullOrEmpty(g.gloveSkin)) sb.AppendLine($"Glove Skin              = {PatchBossLaunchMatch.ReverseSkinPath(g.gloveSkin, "glove", goalie: true)}"); } catch {}
        try { if (!string.IsNullOrEmpty(g.awayGloveSkin)) sb.AppendLine($"Glove Away              = {PatchBossLaunchMatch.ReverseSkinPath(g.awayGloveSkin, "glove", goalie: true)}"); } catch {}
        try { if (!string.IsNullOrEmpty(g.blockerSkin)) sb.AppendLine($"Blocker Skin            = {PatchBossLaunchMatch.ReverseSkinPath(g.blockerSkin, "blocker", goalie: true)}"); } catch {}
        try { if (!string.IsNullOrEmpty(g.awayBlockerSkin)) sb.AppendLine($"Blocker Away            = {PatchBossLaunchMatch.ReverseSkinPath(g.awayBlockerSkin, "blocker", goalie: true)}"); } catch {}
        try { if (!string.IsNullOrEmpty(g.padsSkin)) sb.AppendLine($"Pads Skin               = {PatchBossLaunchMatch.ReverseSkinPath(g.padsSkin, "pads", goalie: true)}"); } catch {}
        try { if (!string.IsNullOrEmpty(g.awayPadsSkin)) sb.AppendLine($"Pads Away               = {PatchBossLaunchMatch.ReverseSkinPath(g.awayPadsSkin, "pads", goalie: true)}"); } catch {}
        try { if (!string.IsNullOrEmpty(g.stickSkin)) sb.AppendLine($"Stick Skin              = {PatchBossLaunchMatch.ReverseSkinPath(g.stickSkin, "stick", goalie: true)}"); } catch {}
        try { if (!string.IsNullOrEmpty(g.awayStickSkin)) sb.AppendLine($"Stick Away              = {PatchBossLaunchMatch.ReverseSkinPath(g.awayStickSkin, "stick", goalie: true)}"); } catch {}

        string gfname = $"Goalie - {safe}.txt";
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

    // Field names that are metadata, not gameplay values — skip when filling {0}/{1} placeholders.
    private static readonly HashSet<string> s_skipFieldNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "name", "description", "statBonusDescription", "id", "level", "relicName",
        "localizedRelicName", "m_InstanceID", "hideFlags", "icon", "sprite", "animatorController",
        "sfx", "audio", "prefab", "particles", "hasLevel2", "isBossRelic", "isCoachRelic"
    };

    // Substitute {0}, {1}, … in a description string using the object's non-zero numeric public fields.
    // Fields are visited in declaration order, matching how the game calls string.Format internally.
    private static string FillFromFields(string text, Il2CppSystem.Reflection.FieldInfo[] fields, Il2CppSystem.Object obj)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains("{")) return text;
        var vals = new List<string>();
        foreach (var f in fields)
        {
            try
            {
                if (s_skipFieldNames.Contains(f.Name)) continue;
                var v = f.GetValue(obj);
                if (v == null) continue;
                string vs = v.ToString() ?? "";
                if (vs == "0" || vs == "0.0" || vs == "" || vs == "null" || vs == "False" || vs == "True") continue;
                if (float.TryParse(vs, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out float fv) && fv != 0f)
                {
                    vals.Add(fv == MathF.Floor(fv) ? ((int)fv).ToString() : fv.ToString("0.##"));
                }
            }
            catch {}
        }
        if (vals.Count == 0) return text;
        for (int i = 0; i < vals.Count; i++)
            text = text.Replace("{" + i + "}", vals[i]);
        return text;
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
        string root      = Path.Combine(BepInEx.Paths.PluginPath, "T2T_Dumps");
        string dirRewards = Path.Combine(root, "rewards");
        string dirGui    = Path.Combine(root, "_gui_data"); // internal — read by Campaign Creator
        foreach (var d in new[]{ root, dirRewards, dirGui })
            Directory.CreateDirectory(d);

        // ── rewards/RELICS.txt ────────────────────────────────────────────
        try
        {
            var relicRepos = UnityEngine.Resources.FindObjectsOfTypeAll<RelicRepository>();
            var relicRepo  = relicRepos != null && relicRepos.Length > 0 ? relicRepos[0] : null;
            if (relicRepo != null)
            {
                var sb = new StringBuilder();
                sb.AppendLine("=== RELICS ===");
                sb.AppendLine($"Generated: {DateTime.Now}");
                sb.AppendLine("key = internal name for config.json | display name shown in-game");
                sb.AppendLine();

                // Build category hint map
                var catHint  = new Dictionary<string, string>();
                var catLists = new (Il2CppSystem.Collections.Generic.List<Rogue.Relic> list, string cat)[]
                {
                    (relicRepo.offensiveRelics, "Offensive"), (relicRepo.defensiveRelics, "Defensive"),
                    (relicRepo.utilityRelics,   "Utility"),   (relicRepo.speedRelics,     "Speed"),
                    (relicRepo.checkingRelics,  "Checking"),  (relicRepo.powerRelics,     "Power"),
                    (relicRepo.accuracyRelics,  "Accuracy"),  (relicRepo.chaosRelics,     "Chaos"),
                    (relicRepo.bossRelics,      "Boss"),      (relicRepo.goalieRelics,    "Goalie"),
                    (relicRepo.coachRelics,     "Coach"),     (relicRepo.injuryRelics,    "Injury"),
                    (relicRepo.timerRelics,     "Timer"),     (relicRepo.maxGoalRelics,   "Max Goal"),
                    (relicRepo.customizationRelics, "Customization"),
                };
                foreach (var (cl, cat) in catLists)
                {
                    if (cl == null) continue;
                    for (int i = 0; i < cl.Count; i++)
                    { var r = cl[i]; if (r != null && !catHint.ContainsKey(r.id ?? "")) catHint[r.id ?? ""] = cat; }
                }

                // Collect all relics, sort by category then display name
                var seen     = new HashSet<string>();
                var relicList = new List<(string id, string display, string cat, string desc)>();
                var allR     = UnityEngine.Resources.FindObjectsOfTypeAll<Rogue.Relic>();
                if (allR != null)
                {
                    foreach (var r in allR)
                    {
                        if (r == null || string.IsNullOrEmpty(r.id)) continue;
                        if (!seen.Add(r.id)) continue;
                        string display = ""; try { display = r.localizedRelicName ?? ""; } catch {}
                        if (string.IsNullOrEmpty(display)) display = r.relicName ?? r.id;
                        string cat2 = catHint.TryGetValue(r.id, out var ch) ? ch : "Misc";
                        string rawDesc = ""; try { rawDesc = r.description ?? ""; } catch {}
                        string desc = !string.IsNullOrEmpty(rawDesc) ? (ResolveI2(rawDesc) ?? "") : "";
                        if (string.IsNullOrEmpty(desc)) desc = rawDesc;
                        if (!string.IsNullOrEmpty(desc) && desc.Contains("{"))
                            try { var rf = r.GetIl2CppType().GetFields(Il2CppSystem.Reflection.BindingFlags.Public | Il2CppSystem.Reflection.BindingFlags.NonPublic | Il2CppSystem.Reflection.BindingFlags.Instance); if (rf != null) desc = FillFromFields(desc, rf, r); } catch {}
                        relicList.Add((r.id, display, cat2, desc));
                    }
                }
                relicList.Sort((a, b) => {
                    int c = string.Compare(a.cat, b.cat, StringComparison.OrdinalIgnoreCase);
                    return c != 0 ? c : string.Compare(a.display, b.display, StringComparison.OrdinalIgnoreCase);
                });
                string curCat = "";
                foreach (var (id, display, cat2, desc) in relicList)
                {
                    if (cat2 != curCat) { curCat = cat2; sb.AppendLine($"--- {cat2} ---"); }
                    sb.AppendLine($"  [{id}] {display}");
                    if (!string.IsNullOrEmpty(desc)) sb.AppendLine($"    {desc}");
                    sb.AppendLine();
                }
                File.WriteAllText(Path.Combine(dirRewards, "RELICS.txt"), sb.ToString());
                Plugin.Log.LogInfo($"[Dump] rewards/RELICS.txt ({relicList.Count})");

                // GUI-support file (used by Reward Pools editor)
                var sb2   = new StringBuilder();
                var poolIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                sb2.AppendLine("# id|display_name|category_hint|in_default_pool");
                if (relicRepo.usedInCampaignPoolRelics != null)
                    for (int i = 0; i < relicRepo.usedInCampaignPoolRelics.Count; i++)
                    { var r = relicRepo.usedInCampaignPoolRelics[i]; if (r != null && !string.IsNullOrEmpty(r.id)) poolIds.Add(r.id); }
                foreach (var (id, display, cat2, _) in relicList)
                    sb2.AppendLine($"{id}|{display}|{cat2}|{(poolIds.Contains(id) ? 1 : 0)}");
                File.WriteAllText(Path.Combine(dirGui, "_reward_relics.txt"), sb2.ToString());
            }
        }
        catch (Exception ex) { Plugin.Log.LogError($"[Dump] RELICS.txt: {ex.Message}"); }

        // ── rewards/ABILITIES_SKATER.txt + ABILITIES_GOALIE.txt ──────────
        try
        {
            var abilityRepos = UnityEngine.Resources.FindObjectsOfTypeAll<AbilityRepository>();
            var abilityRepo  = abilityRepos != null && abilityRepos.Length > 0 ? abilityRepos[0] : null;
            if (abilityRepo != null)
            {
                var sbSk = new StringBuilder();
                sbSk.AppendLine("=== SKATER ABILITIES ===");
                sbSk.AppendLine($"Generated: {DateTime.Now}");
                sbSk.AppendLine("key = config name | CD = cooldown seconds | Charges = max charges");
                sbSk.AppendLine();
                var sbGk = new StringBuilder();
                sbGk.AppendLine("=== GOALIE ABILITIES ===");
                sbGk.AppendLine($"Generated: {DateTime.Now}");
                sbGk.AppendLine("key = config name | CD = cooldown seconds | Charges = max charges");
                sbGk.AppendLine();

                var aList = abilityRepo.abilities;
                if (aList != null)
                {
                    for (int i = 0; i < aList.Count; i++)
                    {
                        var a = aList[i]; if (a == null) continue;
                        string dKey = ""; try { dKey = a.description ?? ""; } catch {}
                        string desc = ResolveI2(dKey);
                        if (!string.IsNullOrEmpty(desc) && desc.Contains("{"))
                            try { var af = a.GetIl2CppType().GetFields(Il2CppSystem.Reflection.BindingFlags.Public | Il2CppSystem.Reflection.BindingFlags.NonPublic | Il2CppSystem.Reflection.BindingFlags.Instance); if (af != null) desc = FillFromFields(desc, af, a); } catch {}
                        string nKey = !string.IsNullOrEmpty(dKey) && dKey.EndsWith("/description")
                            ? dKey.Replace("/description", "/name") : "";
                        string dName = ResolveI2(nKey);
                        if (string.IsNullOrEmpty(dName)) dName = a.name;
                        string tName = a.GetType().Name;
                        bool isGk    = tName.IndexOf("Goalie", StringComparison.OrdinalIgnoreCase) >= 0
                                    || tName.IndexOf("Goaltender", StringComparison.OrdinalIgnoreCase) >= 0;
                        var target   = isGk ? sbGk : sbSk;
                        target.AppendLine($"  [{a.name}] {dName} (Lv{a.level})  CD={a.baseCooldown}  Charges={a.maxCharges}");
                        if (!string.IsNullOrEmpty(desc)) target.AppendLine($"    {desc}");
                        target.AppendLine();
                    }
                }
                File.WriteAllText(Path.Combine(dirRewards, "ABILITIES_SKATER.txt"), sbSk.ToString());
                File.WriteAllText(Path.Combine(dirRewards, "ABILITIES_GOALIE.txt"), sbGk.ToString());
                Plugin.Log.LogInfo("[Dump] rewards/ABILITIES_SKATER.txt + ABILITIES_GOALIE.txt");

                // GUI-support: ability name → GUID
                var sbMap = new StringBuilder();
                sbMap.AppendLine("[abilities]");
                if (aList != null)
                    for (int i = 0; i < aList.Count; i++)
                    { var a = aList[i]; if (a != null && !string.IsNullOrEmpty(a.id) && !string.IsNullOrEmpty(a.name)) sbMap.AppendLine($"{a.name}|{a.id}"); }
                // Talent GUIDs appended to the same map file
                var talRepos3 = UnityEngine.Resources.FindObjectsOfTypeAll<TalentRepository>();
                var talRepo3  = talRepos3 != null && talRepos3.Length > 0 ? talRepos3[0] : null;
                if (talRepo3 != null)
                {
                    sbMap.AppendLine("[talents]");
                    var tList3 = talRepo3.talents;
                    if (tList3 != null)
                        for (int i = 0; i < tList3.Count; i++)
                        { var t = tList3[i]; if (t != null && !string.IsNullOrEmpty(t.id) && !string.IsNullOrEmpty(t.name)) sbMap.AppendLine($"{t.name}|{t.id}"); }
                }
                File.WriteAllText(Path.Combine(dirGui, "_export_id_map.txt"), sbMap.ToString());
                Plugin.Log.LogInfo("[Dump] _gui_data/_export_id_map.txt");
            }
        }
        catch (Exception ex) { Plugin.Log.LogError($"[Dump] ABILITIES: {ex.Message}"); }

        // ── rewards/TALENTS_SKATER.txt + TALENTS_GOALIE.txt ─────────────
        try
        {
            var talentRepos = UnityEngine.Resources.FindObjectsOfTypeAll<TalentRepository>();
            var talentRepo  = talentRepos != null && talentRepos.Length > 0 ? talentRepos[0] : null;
            if (talentRepo != null)
            {
                var sbSk = new StringBuilder();
                sbSk.AppendLine("=== SKATER TALENTS ===");
                sbSk.AppendLine($"Generated: {DateTime.Now}");
                sbSk.AppendLine();
                var sbGk = new StringBuilder();
                sbGk.AppendLine("=== GOALIE TALENTS ===");
                sbGk.AppendLine($"Generated: {DateTime.Now}");
                sbGk.AppendLine();

                var tList = talentRepo.talents;
                if (tList != null)
                {
                    for (int i = 0; i < tList.Count; i++)
                    {
                        var t = tList[i]; if (t == null) continue;
                        string dKey = ""; try { dKey = t.description ?? ""; } catch {}
                        string sKey = ""; try { sKey = t.statBonusDescription ?? ""; } catch {}
                        string desc = ResolveI2(dKey);
                        string stat = ResolveI2(sKey);
                        if ((!string.IsNullOrEmpty(desc) && desc.Contains("{")) || (!string.IsNullOrEmpty(stat) && stat.Contains("{")))
                            try { var tf = t.GetIl2CppType().GetFields(Il2CppSystem.Reflection.BindingFlags.Public | Il2CppSystem.Reflection.BindingFlags.NonPublic | Il2CppSystem.Reflection.BindingFlags.Instance); if (tf != null) { desc = FillFromFields(desc, tf, t); stat = FillFromFields(stat, tf, t); } } catch {}
                        string nKey = !string.IsNullOrEmpty(dKey) && dKey.EndsWith("/description")
                            ? dKey.Replace("/description", "/name") : "";
                        string dName = ResolveI2(nKey);
                        if (string.IsNullOrEmpty(dName)) dName = t.name;
                        bool isGk    = s_goalieTalentKeys.Contains(t.name);
                        var target   = isGk ? sbGk : sbSk;
                        target.AppendLine($"  [{t.name}] {dName} (Lv{t.level})");
                        if (!string.IsNullOrEmpty(desc)) target.AppendLine($"    {desc}");
                        if (!string.IsNullOrEmpty(stat)) target.AppendLine($"    Stats: {stat}");
                        target.AppendLine();
                    }
                }
                File.WriteAllText(Path.Combine(dirRewards, "TALENTS_SKATER.txt"), sbSk.ToString());
                File.WriteAllText(Path.Combine(dirRewards, "TALENTS_GOALIE.txt"), sbGk.ToString());
                Plugin.Log.LogInfo("[Dump] rewards/TALENTS_SKATER.txt + TALENTS_GOALIE.txt");

                // GUI-support: talent name → display + pool flag
                var sb2      = new StringBuilder();
                var poolNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                sb2.AppendLine("# id|display_name|in_default_pool");
                try
                {
                    var fld = talentRepo.GetIl2CppType().GetField("usedInCampaignPoolTalents",
                        Il2CppSystem.Reflection.BindingFlags.NonPublic | Il2CppSystem.Reflection.BindingFlags.Instance);
                    if (fld != null)
                    {
                        var v = fld.GetValue(talentRepo);
                        if (v != null)
                        {
                            var pl = v.Cast<Il2CppSystem.Collections.Generic.List<Rogue.Talent>>();
                            if (pl != null)
                                for (int i = 0; i < pl.Count; i++)
                                { var tt = pl[i]; if (tt != null && !string.IsNullOrEmpty(tt.name)) poolNames.Add(tt.name); }
                        }
                    }
                }
                catch {}
                var seenT = new HashSet<string>();
                if (tList != null)
                    for (int i = 0; i < tList.Count; i++)
                    {
                        var t = tList[i];
                        if (t == null || string.IsNullOrEmpty(t.name) || !seenT.Add(t.name)) continue;
                        string dKey2 = ""; try { dKey2 = t.description ?? ""; } catch {}
                        string nKey2 = !string.IsNullOrEmpty(dKey2) && dKey2.EndsWith("/description")
                            ? dKey2.Replace("/description", "/name") : "";
                        string disp2 = ResolveI2(nKey2);
                        if (string.IsNullOrEmpty(disp2)) disp2 = t.name;
                        sb2.AppendLine($"{t.name}|{disp2}|{(poolNames.Contains(t.name) ? 1 : 0)}");
                    }
                File.WriteAllText(Path.Combine(dirGui, "_reward_talents.txt"), sb2.ToString());
                Plugin.Log.LogInfo($"[Dump] _gui_data/_reward_talents.txt ({seenT.Count})");
            }
        }
        catch (Exception ex) { Plugin.Log.LogError($"[Dump] TALENTS: {ex.Message}"); }
    }
}

// ============================================================
// DEBUG: Boost player team
// ============================================================
// Team.Initialize(TeamData) removed in post-May-2026 game update — patch disabled.
// No [HarmonyPatch] attribute: PatchAll() skips this class entirely.
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
        string name = "";
        try { name = team.teamName?.Trim() ?? ""; } catch { return; }

        // DIAGNOSTIC: vanilla Basic baseline — capture the on-ice roster the game
        // assembled with ZERO mod involvement, to mirror it for custom squads.
        if (name.IndexOf("Basic", StringComparison.OrdinalIgnoreCase) >= 0)
            PatchChooseMetaUI.DumpTeamStructure(team, "VANILLA-Basic@MatchInit");

        if (Plugin.PlayerTeamConfigs == null || Plugin.PlayerTeamConfigs.Count == 0) return;
        bool isCustom = false;
        foreach (var key in Plugin.PlayerTeamConfigs.Keys)
        {
            if (PatchPlayerTeamInit.IsPresetKey(key)) continue;
            if (name.StartsWith(key, StringComparison.OrdinalIgnoreCase)) { isCustom = true; break; }
        }

        if (isCustom) PatchChooseMetaUI.DumpTeamStructure(team, "MatchInit-PRE(native)");

        // Apply player team config here since Team.Initialize(TeamData) was removed.
        PatchPlayerTeamInit.ApplyForTeamData(team);

        // EnsureLineupShowsRoster DISABLED for this diagnostic pass: it rewrote
        // lines[0] positionally from forwards[0..4], which clobbered the native
        // draft/superstar lineup. Dump what the native flow + ApplyForTeamData
        // leaves so we can see exactly what reaches the ice unaided.
        if (isCustom)
        {
            PatchChooseMetaUI.DumpTeamStructure(team, "MatchInit-POST(applied)");
            // PatchPlayerTeamInit.EnsureLineupShowsRoster(team, "MatchInit");  // disabled
        }
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

// AddForward / AddForwardToActiveLine / AddForwardToBench patches DISABLED.
// They previously rerouted drafted players into blank line slots (reshuffling).
// User wants the game's natural draft placement — no interception. The
// [HarmonyPatch] attributes are intentionally removed so PatchAll() skips
// these classes. The helper (DraftAddForwardHelper.MoveIntoBlank) is kept
// in source above in case a more targeted hook is needed later.
public static class PatchTeamDataAddForward_DISABLED { }
public static class PatchTeamDataAddForwardToActiveLine_DISABLED { }
public static class PatchTeamDataAddForwardToBench_DISABLED { }

// ── DIAGNOSTIC (log-only, NO mutation) ──────────────────────────────────────
// Trace every forward the game adds onto a tracked player team (Basic Squad or
// any custom squad) so we can see exactly when the picked superstar and drafted
// skaters land, and via which method. Single line per add — no structure dump
// here (to avoid flooding when the match-init line builder adds in bulk).
internal static class DraftTrace
{
    internal static bool IsTracked(TeamData team)
    {
        if (team == null) return false;
        string n = ""; try { n = team.teamName?.Trim() ?? ""; } catch { return false; }
        if (string.IsNullOrEmpty(n)) return false;
        if (n.IndexOf("Basic", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (Plugin.PlayerTeamConfigs != null)
            foreach (var k in Plugin.PlayerTeamConfigs.Keys)
            {
                if (PatchPlayerTeamInit.IsPresetKey(k)) continue;
                if (n.StartsWith(k, StringComparison.OrdinalIgnoreCase)) return true;
            }
        return false;
    }

    internal static void Log(TeamData team, ForwardData f, string via)
    {
        try
        {
            if (!IsTracked(team)) return;
            string fn = ""; try { fn = ((f?.firstName ?? "") + " " + (f?.lastName ?? "")).Trim(); } catch { }
            string id = ""; try { id = f?.id ?? ""; } catch { }
            string cat = ""; try { cat = f != null ? f.skaterCategory.ToString() : ""; } catch { }
            int cnt = -1; try { cnt = team.forwards?.Count ?? -1; } catch { }
            Plugin.Log.LogInfo($"[DraftTrace/{via}] team='{team.teamName}' +'{fn}' id={(id.Length > 8 ? id.Substring(0, 8) : id)} cat={cat} -> forwards.Count={cnt}");
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[DraftTrace] {ex.Message}"); }
    }
}

[HarmonyPatch(typeof(TeamData), "AddForward", new Type[] { typeof(ForwardData) })]
public static class PatchDraftTrace_AddForward
{
    [HarmonyPostfix] public static void Postfix(TeamData __instance, ForwardData __0) => DraftTrace.Log(__instance, __0, "AddForward");
}

[HarmonyPatch(typeof(TeamData), "AddForwardToBench", new Type[] { typeof(ForwardData) })]
public static class PatchDraftTrace_AddForwardToBench
{
    [HarmonyPostfix] public static void Postfix(TeamData __instance, ForwardData __0) => DraftTrace.Log(__instance, __0, "AddForwardToBench");
}

[HarmonyPatch(typeof(TeamData), "AddForwardToActiveLine", new Type[] { typeof(ForwardData) })]
public static class PatchDraftTrace_AddForwardToActiveLine
{
    [HarmonyPostfix] public static void Postfix(TeamData __instance, ForwardData __0) => DraftTrace.Log(__instance, __0, "AddForwardToActiveLine");
}

// ============================================================
// Player Team Editor — apply player_teams.txt to player teams
// ============================================================
// Team.Initialize(TeamData) removed in post-May-2026 game update.
// Postfix now called manually from PatchMatchInitForwards instead.
// No [HarmonyPatch] attribute: PatchAll() skips this class entirely.
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

    // Called manually from PatchMatchInitForwards (Team.Initialize no longer exists).
    public static void ApplyForTeamData(TeamData teamData)
    {
        Postfix(null, teamData);
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

        // FA sign-time: apply free_agents/ configs to newly-signed ForwardData.
        // Uses a separate dict (FreeAgentSignedConfigs) — DraftPoolConfigs and
        // existing bench players are never modified by this path.
        if (Plugin.FreeAgentSignedConfigs.Count > 0)
            ApplySignedFreeAgents();

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
        // Reconcile intentionally DISABLED at Team.Init — user explicitly
        // asked for no reshuffling on run start. Keep the chosen players where
        // the game placed them. (Was: ReconcileDraftedFAs(teamData, "Team.Init"))


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

        // PopulateCustomSquadBench DISABLED — it was injecting random base-game
        // benchwarmers/superstars instead of preserving the user's actual picks.
        // Per user: no reshuffling, no auto-fills, keep whatever the game's
        // squad-confirm flow produces. The method body remains below in case
        // we need to re-enable a more targeted version later.

        // Superstar inject at Team.Init REMOVED per user. The base game's
        // squad-confirm flow places the picked superstar wherever it places it —
        // we don't move it after the fact.
    }

    private static void PopulateCustomSquadBench(TeamData teamData)
    {
        var fwds = teamData.forwards;
        if (fwds == null) return;

        var byPtr = new HashSet<IntPtr>();
        var byName = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < fwds.Count; i++)
        {
            var f = fwds[i];
            if (f == null) continue;
            byPtr.Add(f.Pointer);
            byName.Add($"{f.firstName} {f.lastName}".Trim());
        }

        // Bench cap. TeamData.benchSize defaults to ~7 on vanilla squads; if
        // it's 0 (custom clone case) bump it to 7 so the game shows the seats.
        int benchCap = 7;
        try
        {
            if (teamData.benchSize > 0) benchCap = teamData.benchSize;
            else { try { teamData.benchSize = 7; } catch { } }
        }
        catch { }

        var allFwds = UnityEngine.Resources.FindObjectsOfTypeAll<Data.ForwardData>();
        if (allFwds == null || allFwds.Length == 0) return;

        int addedBenchwarmers = 0;
        int addedSuperstar = 0;

        // Pass 1: append benchwarmers (the user's renamed draft_pool players).
        foreach (var f in allFwds)
        {
            if (f == null) continue;
            if (f.skaterCategory != Data.SkaterCategory.Benchwarmer) continue;
            if (byPtr.Contains(f.Pointer)) continue;
            string nm = $"{f.firstName} {f.lastName}".Trim();
            if (string.IsNullOrEmpty(nm)) continue;
            if (!byName.Add(nm)) continue;
            if ((fwds.Count - 5) >= benchCap) break;
            try
            {
                fwds.Add(f);
                byPtr.Add(f.Pointer);
                addedBenchwarmers++;
                Plugin.Log.LogInfo($"[BenchPop] Added benchwarmer '{nm}' → fwds[{fwds.Count - 1}]");
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"[BenchPop] add '{nm}': {ex.Message}"); }
        }

        // Pass 2: ensure ONE Superstar is on the team (the player's pick lands
        // somewhere the squad-confirm flow doesn't carry into fwds). If any
        // superstar is already on the team, do nothing. Otherwise append the
        // first found superstar — it lands on the bench so Bench Bonus picks
        // it up; the player can swap to a line slot in Edit Lineup.
        bool hasSuperstar = false;
        for (int i = 0; i < fwds.Count; i++)
        {
            var f = fwds[i];
            if (f != null && f.skaterCategory == Data.SkaterCategory.Superstar) { hasSuperstar = true; break; }
        }
        if (!hasSuperstar)
        {
            foreach (var f in allFwds)
            {
                if (f == null) continue;
                if (f.skaterCategory != Data.SkaterCategory.Superstar) continue;
                if (byPtr.Contains(f.Pointer)) continue;
                string nm = $"{f.firstName} {f.lastName}".Trim();
                if (string.IsNullOrEmpty(nm)) continue;
                if (!byName.Add(nm)) continue;
                try
                {
                    fwds.Add(f);
                    addedSuperstar++;
                    Plugin.Log.LogInfo($"[BenchPop] Added superstar '{nm}' → fwds[{fwds.Count - 1}] (the squad-confirm flow dropped the pick)");
                    break;
                }
                catch (Exception ex) { Plugin.Log.LogWarning($"[BenchPop] add superstar '{nm}': {ex.Message}"); }
            }
        }

        Plugin.Log.LogInfo($"[BenchPop] Done: +{addedBenchwarmers} benchwarmer(s), +{addedSuperstar} superstar, fwds.Count now {fwds.Count}, benchSize={teamData.benchSize}");
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

    // Repoint lines[0] at the forwards actually sitting on the roster, so the
    // player's picked superstar + drafted skaters (which are present in
    // team.forwards by match start, but unreferenced because the cloned Basic
    // template left lines[0] pointing at dead ids) actually take the ice.
    //
    // NOT a reshuffle: every forward keeps its exact forwards-array slot. We
    // only (a) rewrite the five line id pointers to match forwards[0..4],
    // clearing any that resolve to nothing, and (b) move the captured superstar
    // to center by swapping the two id pointers (the forwards don't move). Any
    // genuinely empty line slot is left "" so the base game fills it from the
    // bench exactly as it does for vanilla squads.
    internal static void EnsureLineupShowsRoster(TeamData team, string source)
    {
        if (team == null) return;
        try
        {
            var fwds = team.forwards;
            var lns = team.lines;
            if (fwds == null || lns == null || lns.Count == 0 || lns[0] == null) return;
            var l0 = lns[0];

            bool IsReal(Data.ForwardData f)
            {
                if (f == null) return false;
                string fn = "", ln = "";
                try { fn = f.firstName ?? ""; ln = f.lastName ?? ""; } catch { }
                return !(string.IsNullOrWhiteSpace(fn) && string.IsNullOrWhiteSpace(ln));
            }

            // Full roster snapshot — so the log shows exactly what reached the
            // team by match start (superstar? drafted skaters? bench?).
            for (int i = 0; i < fwds.Count; i++)
            {
                var f = fwds[i];
                Plugin.Log.LogInfo($"[Lineup/{source}] fwds[{i}] = {(f == null ? "null" : (f.firstName + " " + f.lastName + " id=" + (f.id ?? "") + " cat=" + f.skaterCategory))}");
            }

            // Build the five line ids positionally from forwards[0..4].
            string[] ids = new string[5];
            for (int i = 0; i < 5; i++)
                ids[i] = (i < fwds.Count && IsReal(fwds[i])) ? (fwds[i].id ?? "") : "";

            // Move the captured superstar to center (slot 2) by swapping id
            // pointers. The forwards stay put; only the on-ice position label
            // changes. If center was empty, this just relocates the superstar
            // there and clears its old slot.
            var ss = PatchOldSquadMenuSuperstar.PickedSuperstar;
            if (ss != null && !string.IsNullOrEmpty(ss.id))
            {
                int k = -1;
                for (int i = 0; i < 5; i++)
                    if (ids[i] == ss.id) { k = i; break; }
                if (k >= 0 && k != 2)
                {
                    string tmp = ids[2];
                    ids[2] = ids[k];
                    ids[k] = tmp;
                    Plugin.Log.LogInfo($"[Lineup/{source}] Moved superstar '{ss.firstName} {ss.lastName}' to center (was line slot {k})");
                }
            }

            l0.leftWinger = ids[0];
            l0.rightWinger = ids[1];
            l0.center = ids[2];
            l0.leftDefensemen = ids[3];
            l0.rightDefensemen = ids[4];
            Plugin.Log.LogInfo($"[Lineup/{source}] lines[0] now: LW='{l0.leftWinger}' RW='{l0.rightWinger}' C='{l0.center}' LD='{l0.leftDefensemen}' RD='{l0.rightDefensemen}'");
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[Lineup/{source}] {ex.Message}"); }
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
        //
        // CRITICAL (v2.1.27 talent fix): wipe ONLY the forwards sitting in
        // user-CONFIGURED lineup slots (the ones whose talents we re-apply from
        // cfg right below). The picked superstar and drafted skaters occupy the
        // OTHER slots — their talents are applied elsewhere (the superstar at
        // GenerateSuperStarSkaters, drafts via ApplyDraftPool) and NOTHING
        // re-applies them here, so wiping every forward stripped the superstar's
        // talents at match start ("not all talents copied when picking
        // superstar"). Configured players always sit at their own slot index
        // (the clone seats them in Basic's real forward slot; the native draft
        // only fills the empty slots around them), so a per-slot wipe is exact.
        if (firstApply)
        {
            try
            {
                if (team.forwards != null)
                {
                    PlayerConfig[] slotCfgs = { cfg?.LW, cfg?.RW, cfg?.C, cfg?.LD, cfg?.RD };
                    for (int i = 0; i < team.forwards.Count; i++)
                    {
                        var fwd = team.forwards[i];
                        if (fwd == null) continue;
                        // Skip the superstar / drafted skaters — wipe only configured slots.
                        if (i >= slotCfgs.Length || !PatchChooseMetaUI.SlotIsConfigured(slotCfgs[i])) continue;
                        try { fwd.powerups = new Il2CppSystem.Collections.Generic.List<Rogue.Talent>(); } catch {}
                    }
                }
                if (team.goalie != null)
                    try { team.goalie.powerups = new Il2CppSystem.Collections.Generic.List<Rogue.Talent>(); } catch {}
                try { team.relics = new Il2CppSystem.Collections.Generic.List<Rogue.Relic>(); } catch {}
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"[PlayerTeam] New-run wipe: {ex.Message}"); }
        }

        // Logo: prefer a custom PNG in CustomLogos/ (logo packs). Campaign team
        // names collide with pack names (e.g. "Canucks (25-26)") and those
        // campaign teams carry blank logos, so resolving the team first applied
        // the wrong logo. PNG wins; borrow an in-game team's sprite only if no
        // PNG matches.
        if (!string.IsNullOrEmpty(cfg.LogoFrom))
        {
            var customLogo = PatchBossLaunchMatch.LoadCustomLogoSprite(cfg.LogoFrom);
            if (customLogo != null)
            {
                team.logo = customLogo;
                team.alternateBigLogo = customLogo.texture;
                try { team.hasBigLogo = true; } catch { }   // gate for rink/big-logo Texture surfaces
                PatchBossLaunchMatch.ApplyCustomLogoSkinToSkaters(team);   // jerseys use the custom-logo Spine slot
                Plugin.Log.LogInfo($"[PlayerTeam] Applied CUSTOM logo '{cfg.LogoFrom}' (tex={(customLogo.texture != null ? customLogo.texture.width + "x" + customLogo.texture.height : "null")})");
            }
            else
            {
                var logoTeam = PatchBossLaunchMatch.FindTeamByName(cfg.LogoFrom);
                if (logoTeam != null && logoTeam != team)
                {
                    team.logo = logoTeam.logo;
                    team.alternateBigLogo = logoTeam.alternateBigLogo;
                    if (logoTeam.nickname != null) team.nickname = logoTeam.nickname;
                    Plugin.Log.LogInfo($"[PlayerTeam] Applied logo from team '{cfg.LogoFrom}'");
                }
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
                PatchBossLaunchMatch.ApplyTeamEquipmentColors(fwds[i], cfg, team);
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
                // Stat Scale (custom squads): multiply the configured player's
                // freshly-set stats. ApplyPlayerConfig writes the config's
                // ABSOLUTE stat values every apply, so scaling here is
                // idempotent across re-applies (Spartan re-init etc.) — it never
                // compounds. Drafted/superstar skaters sit in the OTHER slots and
                // are skipped by the hasAny gate above, so they're left at their
                // own (pool/native) stats and never double-scaled.
                if (cfg.StatScale != 1.0f)
                {
                    fwds[i].speed = (int)(fwds[i].speed * cfg.StatScale);
                    fwds[i].shotPower = (int)(fwds[i].shotPower * cfg.StatScale);
                    fwds[i].shotAccuracy = (int)(fwds[i].shotAccuracy * cfg.StatScale);
                    fwds[i].checking = (int)(fwds[i].checking * cfg.StatScale);
                }
                Plugin.Log.LogInfo($"[PlayerTeam] Applied slot override: {pc.Name ?? pc.ImportPlayer}{(cfg.StatScale != 1.0f ? $" (stats x{cfg.StatScale})" : "")}");
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
            // Stat Scale (custom squads): scale the goalie's freshly-set stats.
            // ApplyGoalieConfig writes the config's absolute values, so this is
            // idempotent across re-applies just like the forwards above.
            if (cfg.StatScale != 1.0f)
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
                Plugin.Log.LogInfo($"[PlayerTeam] Goalie stats scaled x{cfg.StatScale}");
            }
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

        // Themed "Canadians" faces bake hair/beards into the head mesh, so
        // helmets clip or don't render. Force helmet off regardless of
        // uniform or per-player override settings.
        if (!string.IsNullOrEmpty(f.headSkin) &&
            f.headSkin.StartsWith("Faces/Canadians/", System.StringComparison.Ordinal))
        {
            Plugin.RegisterFaceAsHelmetless(f.headSkin);
            f.helmetSkin = "";
            f.helmetAwaySkin = "";
            NoHelmetForwards.Add(f.Pointer);
            Plugin.Log.LogInfo($"[NoHelmet] '{f.firstName} {f.lastName}' head '{f.headSkin}' auto-bared (themed Canadians face)");
            return;
        }

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
                Plugin.Log.LogInfo($"[PlayerTeam] Modifying draft player '{nameKey}' (name match)"
                    + (string.IsNullOrWhiteSpace(pc.Name) ? "" : $" → renaming to '{pc.Name}'"));
            ApplyConfigToForward(f, pc, applyName: true);
            applied++;
        }

        if (applied > 0)
            Plugin.Log.LogInfo($"[PlayerTeam] Bench players: {applied} instance(s) modified ({_loggedDraftNames.Count}/{Plugin.DraftPoolConfigs.Count} unique names)");
    }

    // Applies FreeAgentPoolList (player_teams/free_agents/) to the GM-node pool.
    // Phase 1 (here): truncate preGeneratedFreeAgents to N entries so the pick
    //   screen offers only N slots. Also snapshot each slot's templateFullName
    //   into FreeAgentSignedConfigs so ApplySignedFreeAgents can apply the right
    //   custom config when the player signs one.
    // Phase 2 (ApplySignedFreeAgents, called on every Team.Initialize): match
    //   newly-signed ForwardData by templateFullName and apply config. Completely
    //   separate from DraftPoolConfigs — existing bench players are not touched.
    internal static void ApplyFreeAgentPool()
    {
        if (Plugin.FreeAgentPoolList == null || Plugin.FreeAgentPoolList.Count == 0) return;

        var rawList = PatchPreGenerateFreeAgents.LastOutput;
        if (rawList == null || rawList.Count == 0)
        {
            Plugin.Log.LogInfo("[PlayerTeam] ApplyFreeAgentPool: preGeneratedFreeAgents not available yet");
            return;
        }

        int N = Plugin.FreeAgentPoolList.Count;
        int poolSize = rawList.Count;

        // FILL the entire pool: every slot becomes one of the editor's free
        // agents, cycling the list to cover all slots (user: "make it work no
        // matter what even if it means you have to have 28 in there"). No
        // truncation. Each slot's vanilla templateFullName is mapped to a cycled
        // custom config; ApplySignedFreeAgents (below + on every Team.Initialize)
        // customizes EVERY matching ForwardData — both the pick-screen previews
        // and the signed player — so the whole pool shows the editor's players.
        Plugin.FreeAgentSignedConfigs.Clear();
        int mapped = 0;
        for (int i = 0; i < poolSize; i++)
        {
            try
            {
                var entry = rawList[i];
                if (entry == null) continue;
                string tmpl = entry.templateFullName?.Trim() ?? "";
                if (string.IsNullOrEmpty(tmpl)) continue;
                var cfg = Plugin.FreeAgentPoolList[i % N];
                Plugin.FreeAgentSignedConfigs[tmpl.ToLowerInvariant()] = cfg;
                mapped++;
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"[PlayerTeam] FA slot {i} map: {ex.Message}"); }
        }
        Plugin.Log.LogInfo($"[PlayerTeam] Free agent pool: filled all {poolSize} slot(s) by cycling {N} editor FA(s) → {Plugin.FreeAgentSignedConfigs.Count} template(s) mapped");

        // Apply immediately so the pick-screen previews show custom players now,
        // not only after the next Team.Initialize fires.
        ApplySignedFreeAgents();
    }

    // Called on every Team.Initialize (alongside ApplyDraftPool).
    // Applies FreeAgentSignedConfigs to any newly-signed ForwardData whose
    // vanilla templateFullName appears in the dict. Bench player configs in
    // DraftPoolConfigs are untouched.
    internal static void ApplySignedFreeAgents()
    {
        if (Plugin.FreeAgentSignedConfigs.Count == 0) return;

        var allFwds = UnityEngine.Resources.FindObjectsOfTypeAll<ForwardData>();
        if (allFwds == null || allFwds.Length == 0) return;

        int applied = 0;
        foreach (var f in allFwds)
        {
            if (f == null) continue;
            if (Plugin.AppliedFreeAgentPtrs.Contains(f.Pointer)) continue;

            string fullName = $"{f.firstName} {f.lastName}".Trim().ToLowerInvariant();
            string assetName = (f.name ?? "").ToLowerInvariant();

            PlayerConfig pc = null;
            if (!Plugin.FreeAgentSignedConfigs.TryGetValue(fullName, out pc))
                Plugin.FreeAgentSignedConfigs.TryGetValue(assetName, out pc);
            if (pc == null) continue;

            Plugin.AppliedFreeAgentPtrs.Add(f.Pointer);
            Plugin.Log.LogInfo($"[PlayerTeam] Signed FA '{f.firstName} {f.lastName}' — applying custom config");
            ApplyConfigToForward(f, pc, applyName: true);
            applied++;
        }
        if (applied > 0)
            Plugin.Log.LogInfo($"[PlayerTeam] Signed free agents: {applied} customized");
    }

    // Look up the ForwardData instances that correspond to currently
    // pre-generated free agents. Reads CampaignState.preGeneratedFreeAgents
    // (List<PreGeneratedFreeAgentData>) and matches each entry's
    // templateFullName to a loaded ForwardData.
    internal static List<ForwardData> GetGeneratedFreeAgentForwards(ForwardData[] allFwds)
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

    internal static void ApplyConfigToForward(ForwardData f, PlayerConfig pc, bool applyName = false)
    {
        // NAME: applied only when applyName=true (bench rename + signed free
        // agents). The match key (draft_pool filename / FA templateFullName)
        // stays the vanilla name, so renaming the ForwardData's display name
        // does NOT break our name-based matching — every fresh vanilla instance
        // still matches the key and gets renamed on the next pass. Applied LAST
        // is unnecessary; AppliedDraftPtrs guards re-entry per instance.
            if (applyName && !string.IsNullOrWhiteSpace(pc.Name))
            {
                string nm = pc.Name.Trim();
                int sp = nm.IndexOf(' ');
                if (sp > 0) { f.firstName = nm.Substring(0, sp); f.lastName = nm.Substring(sp + 1).Trim(); }
                else { f.firstName = nm; f.lastName = ""; }
            }

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
    // Per-player AWAY color overrides (used when this player's team wears its
    // away jersey — the opponent/visitor side). Null = fall back to the
    // matching home override above, then to team colors.
    public int[] JerseyColorAway = null;
    public int[] JerseySecondaryColorAway = null;
    public int[] JerseyAccentColorAway = null;
    public int[] GlovesColorAway = null;
    public int[] GlovesSecondaryColorAway = null;
    public int[] GlovesTertiaryColorAway = null;
    public int[] HelmetColorAway = null;
    public int[] HelmetSecondaryColorAway = null;
    public int[] HelmetTertiaryColorAway = null;
    public int[] PantsColorAway = null;
    public int[] PantsSecondaryColorAway = null;
    public int[] PantsTertiaryColorAway = null;
    public int[] SkatesColorAway = null;
    public int[] BladeColorAway = null;
    public int[] LacesColorAway = null;
    public int[] BicepColorAway = null;
    public int[] NumberColorAway = null;
    public int[] NumberSecondaryColorAway = null;
    public int[] SocksColorAway = null;
    public int[] SocksSecondaryColorAway = null;
    public int[] SocksTertiaryColorAway = null;
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
    // Team-level AWAY equipment colors. Used when the team wears its away
    // jersey (the opponent/visitor side, and the player team on away nights).
    // Null = fall back to the matching home equipment color.
    public int[] TeamGlovesColorAway = null;
    public int[] TeamGlovesSecondaryAway = null;
    public int[] TeamGlovesTertiaryAway = null;
    public int[] TeamHelmetColorAway = null;
    public int[] TeamHelmetSecondaryAway = null;
    public int[] TeamHelmetTertiaryAway = null;
    public int[] TeamPantsColorAway = null;
    public int[] TeamPantsSecondaryAway = null;
    public int[] TeamPantsTertiaryAway = null;
    public int[] TeamSkatesColorAway = null;
    public int[] TeamBladeColorAway = null;
    public int[] TeamLacesColorAway = null;
    public int[] TeamBicepColorAway = null;
    public int[] TeamSocksColorAway = null;
    public int[] TeamSocksSecondaryAway = null;
    public int[] TeamSocksTertiaryAway = null;
    public int[] TeamStickColorAway = null;
    public int[] TeamNumberColorAway = null;
    public int[] TeamNumberSecondaryAway = null;
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
