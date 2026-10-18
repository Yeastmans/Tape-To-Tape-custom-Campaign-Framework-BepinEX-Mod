using System.Linq;
using System;
using CampaignCore;
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

[BepInPlugin("com.mods.customcampaign", "Custom Campaign Framework", PluginVersion)]
public class Plugin : BasePlugin
{
    // Numeric loader version; public Studio release is Beta 1.0.
    internal const string PluginVersion = "1.0.0";
    internal const string ReleaseLabel = "Beta 1.0";
    internal static new ManualLogSource Log;

    internal static bool BossJustBeaten = false;
    internal static bool TeamsLogged = false;
    internal static bool ReposLogged = false;
    internal static bool SeparateFilesWritten = false;

    // ===== CONFIG =====
    // Windows: Campaign Studio and the game share the current user's AppData content.
    // Linux/Steam Deck: the game runs under Proton, where this process sees Win32NT,
    // but the shipped editor is the Python creator writing beside the plugin. Wine is
    // detected explicitly so both sides keep using plugins/Custom Campaigns Mod.
    internal static readonly bool UsesLegacyLayout =
        Environment.OSVersion.Platform != PlatformID.Win32NT || RunningUnderWine();
    internal static readonly string ModContentRoot = UsesLegacyLayout
        ? Path.Combine(BepInEx.Paths.PluginPath, "Custom Campaigns Mod")
        : CampaignCore.UserContentPaths.EnsureRoot();
    internal static string DumpRoot => UsesLegacyLayout
        ? Path.Combine(BepInEx.Paths.PluginPath, "T2T_Dumps") : Path.Combine(ModContentRoot, "T2T_Dumps");

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, ExactSpelling = true)]
    private static extern IntPtr GetModuleHandleW(string moduleName);
    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Ansi, ExactSpelling = true, BestFitMapping = false)]
    private static extern IntPtr GetProcAddress(IntPtr module, string procName);
    private static bool RunningUnderWine()
    {
        try
        {
            IntPtr ntdll = GetModuleHandleW("ntdll.dll");
            return ntdll != IntPtr.Zero && GetProcAddress(ntdll, "wine_get_version") != IntPtr.Zero;
        }
        catch { return false; }
    }
    private static readonly string CampaignsRoot = Path.Combine(ModContentRoot, "campaigns");
    private static readonly string DefaultsPath = Path.Combine(ModContentRoot, "defaults.txt");

    // Default fallback values (loaded from defaults.txt)
    internal static TeamConfig DefaultTeam = new TeamConfig();
    internal static PlayerConfig DefaultSkater = new PlayerConfig();
    internal static PlayerConfig DefaultGoalie = new PlayerConfig();

    internal static string ModFolder;
    private static string ConfigPath;
    private static string SavePath;
    private static string PlayerTeamsPath;
    internal static string ActiveCampaign = CampaignSelection.BaseGame;

    internal static bool IsDefaultMode = true; // campaign gameplay requires a valid selection

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
    internal static bool PlayerTeamsEnabled = true;
    internal static bool UsePlayerTeams = false; // toggle from campaign settings
    // Squad that custom squads are cloned from. Empty = Basic Squad, which is
    // what every campaign did before this setting existed.
    internal static string SquadTemplate = "";
    internal static bool _pendingAutoDump = false;
    internal static List<string> ConfigTeamDirs = new List<string>(); // parallel to ConfigTeams — folder path per team

    internal static void ResolveCampaignPaths()
    {
        Log.LogInfo($"[Config] Content folder: {ModContentRoot}{(UsesLegacyLayout ? " (legacy creator layout)" : "")}");
        ActiveCampaign = CampaignSelection.Resolve(ModContentRoot, out string warning);
        if (!string.IsNullOrEmpty(warning)) Log.LogWarning($"[Campaign] {warning}");
        if (ActiveCampaign == CampaignSelection.BaseGame)
        {
            UseBaseGame();
            return;
        }

        IsDefaultMode = false;
        ModFolder = Path.Combine(CampaignsRoot, ActiveCampaign);
        ConfigPath = Path.Combine(ModFolder, "campaign.txt");
        SavePath = Path.Combine(ModFolder, "save.txt");
        PlayerTeamsPath = Path.Combine(ModFolder, "player_teams.txt");
        Log.LogInfo($"[Campaign] Active campaign: '{ActiveCampaign}' ({ModFolder})");
    }

    private static void UseBaseGame()
    {
        ActiveCampaign = CampaignSelection.BaseGame;
        IsDefaultMode = true;
        ModFolder = Path.Combine(CampaignsRoot, "default");
        ConfigPath = "";
        SavePath = "";
        PlayerTeamsPath = "";
        ConfigTeams.Clear();
        ConfigTeamDirs.Clear();
        VanillaCampaignRuntime.Reset();
        Log.LogInfo("[Campaign] DEFAULT MODE — no custom campaign selected; playing base game");
    }

    internal static void LoadDefaults()
    {
        // defaults.txt belongs to the legacy creator layout. Campaign Studio shows
        // unset fields as unset and has no editor for this file, so honouring it
        // would make the game invent a logo and colours the editor never showed.
        if (!UsesLegacyLayout)
        {
            if (File.Exists(DefaultsPath))
                Log.LogInfo("[Config] Ignoring defaults.txt; Campaign Studio content uses its own defaults.");
            return;
        }
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
    internal static int[] ActSequence = new[] { 1, 2, 3 };
    internal static int TotalMaps => ActSequence.Length;
    internal static bool ReplaceChallenges = false;
    internal static List<int> ReplaceChallengesActs = null; // null = all acts, list = only these acts
    internal static HashSet<int> ReplaceChallengesMaps = null; // null = use acts logic; set = 0-indexed map positions
    internal static bool DumpData = true; // generates reference dump files
    internal static bool UseGauntletMap = false;
    // "Maps = <squad>" in campaign.txt — play every run on this base squad's
    // map layout (Basic, Defence, Speedy, Gauntlet, Solo, ...). Empty = each
    // squad keeps its own maps. "Gauntlet Map = yes" is the legacy special
    // case and behaves like Maps = Gauntlet (plus injecting all squads).
    internal static string MapSourceSquad = "";

    // "Offside = yes" in campaign.txt — enforce the offside rule in every match
    // of the run. OffsideMatch activates the native scene controller and supplies
    // temporary setting reads. A force source is unsuitable because native
    // force sources also require the Knockout penalty.
    //
    // Deliberately NOT GameplaySettings.Offside. That setter writes the value and
    // then calls GameplaySettings.SaveSettings() (confirmed by disassembling
    // set_Offside in the 2026-08-18 build), so using it would permanently flip the
    // player's own Advanced Settings choice for Play Now and every other campaign.
    internal static bool OffsideEnabled = false;
    // "Offside Penalty = Whistle | Lose Puck | Knockout". Empty means "leave the
    // player's own Advanced Settings choice alone" — that path touches nothing
    // persistent. Setting it DOES persist (same SaveSettings call), so
    // OffsideMatch overrides native reads only during a match; saved settings stay intact.
    internal static string OffsidePenaltyName = "";

    // Reward-pool filters (populated from campaigns/<name>/reward_pools.txt).
    // IDs held here are filtered OUT of the game's random-reward picks at
    // runtime via Harmony postfixes on RelicRepository / TalentRepository.
    internal static HashSet<string> ExcludedRewardRelicIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    internal static HashSet<string> ExcludedRewardAbilityIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
        PlayerTeamsEnabled = true;
        UsePlayerTeams = false;
        ActSequence = new[] { 1, 2, 3 };
        ReplaceChallenges = false;
        ReplaceChallengesActs = null;
        ReplaceChallengesMaps = null;
        ConfigTeams.Clear();
        ConfigTeamDirs.Clear();
        VanillaCampaignRuntime.Reset();
        ExcludedRewardRelicIds.Clear();
        ExcludedRewardTalentIds.Clear();
        ExcludedRewardAbilityIds.Clear();
        if (IsDefaultMode) return;
        try
        {
            // Multi-folder format only. Campaign settings live in campaign.txt,
            // teams live in the teams/ subfolder.
            UseGauntletMap = false; // reset before parsing — absence of key means off
            MapSourceSquad = "";    // reset — absence of key means each squad keeps its own maps
            if (File.Exists(ConfigPath))
            {
                // The final authored map may use any native act. Read the same
                // strict sequence as Studio before enabling any campaign data;
                // invalid/empty values must not silently invent a third map.
                ActSequence = CampaignActSequence.Read(new CampaignDocument(File.ReadAllText(ConfigPath)));
                Log.LogInfo($"[Config] Act Sequence: [{string.Join(", ", ActSequence)}] ({TotalMaps} maps)");
                ParseKvFile(ConfigPath, (key, val) =>
                {
                    if (key == "replace challenges")
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
                    else if (key == "use player teams" || key == "custom player teams") PlayerTeamsEnabled = UsePlayerTeams = val.ToLower() == "yes" || val.ToLower() == "true";
                    else if (key == "gauntlet map") UseGauntletMap = val.ToLower() == "yes" || val.ToLower() == "true";
                    // Map selector: play every run on the map layout of a chosen
                    // base squad (Basic / Defence / Speedy / Gauntlet / ...).
                    // Generalizes "Gauntlet Map = yes" to any squad's maps.
                    // Which squad the custom squads are CLONED from. Separate
                    // from "Maps", which only picks the map layout: this decides
                    // the starting roster shape, relics and unlock state your
                    // squad inherits.
                    else if (key == "squad template" || key == "template squad")
                    {
                        string tv = val.Trim();
                        string tvl = tv.ToLowerInvariant();
                        SquadTemplate = (tvl.Length == 0 || tvl == "default" || tvl == "(default)"
                                         || tvl == "basic" || tvl == "basic squad") ? "" : tv;
                    }
                    else if (key == "maps" || key == "map source" || key == "squad maps")
                    {
                        string mv = val.Trim();
                        string mvl = mv.ToLowerInvariant();
                        if (mvl.Length == 0 || mvl == "default" || mvl == "(default)" || mvl == "no" || mvl == "none"
                            || mvl.StartsWith("(each squad")) MapSourceSquad = "";
                        else MapSourceSquad = mv;
                    }
                    else if (key == "dump data") DumpData = val.ToLower() == "yes" || val.ToLower() == "true";
                    else if (key == "offside" || key == "offsides")
                        OffsideEnabled = val.ToLower() == "yes" || val.ToLower() == "true";
                    else if (key == "offside penalty" || key == "offsides penalty")
                    {
                        string pv = val.Trim();
                        string pvl = pv.ToLowerInvariant();
                        OffsidePenaltyName = (pvl.Length == 0 || pvl == "default" || pvl == "(default)"
                                              || pvl == "no" || pvl == "none") ? "" : pv;
                    }
                });
            }
            else
            {
                Log.LogWarning("[Config] Selected campaign.txt is missing — using base game.");
                UseBaseGame();
                return;
            }

            // Reward-pool filters — campaigns/<name>/reward_pools.txt.
            // Format: "Excluded Relics = id1, id2, id3" (comma-separated ids,
            // matching the ids in _reward_relics.txt). Same for talents.
            ExcludedRewardRelicIds.Clear();
            ExcludedRewardTalentIds.Clear();
            ExcludedRewardAbilityIds.Clear();
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
                        else if (key == "excluded talents" || key == "excluded talent" || key == "excluded goalie talents")
                        {
                            foreach (var p in val.Split(','))
                            {
                                var id = p.Trim();
                                if (id.Length > 0) ExcludedRewardTalentIds.Add(id);
                            }
                        }
                        else if (key == "excluded abilities" || key == "excluded goalie abilities")
                            foreach (string part in val.Split(','))
                                if (part.Trim().Length > 0) ExcludedRewardAbilityIds.Add(part.Trim());
                    });
                    Log.LogInfo($"[Config] reward_pools.txt loaded — {ExcludedRewardRelicIds.Count} relics excluded, {ExcludedRewardTalentIds.Count} talents excluded");
                }
            }
            catch (Exception ex) { Log.LogWarning($"[Config] reward_pools.txt: {ex.Message}"); }

            LoadCampaignFolders();
        }
        catch (Exception ex)
        {
            Log.LogError($"[Config] Failed to load config — using base game: {ex.Message}\n{ex.StackTrace}");
            UseBaseGame();
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
        team.Fields.Set(key, val);
        string colorKey = AppearanceFields.ColorKey(key);
        if (colorKey.Length > 0)
        {
            var rgb = ParseRandomColor(val);
            if (rgb != null) { val = string.Join(",", rgb); team.Colors.Set(colorKey, val); }
        }
        if (key == "team name") team.Name = val;
        else if (key == "city") team.City = val;
        else if (key == "logo from") team.LogoFrom = val;
        else if (key == "import team") team.ImportTeam = val;
        else if (key == "abbreviation") team.Abbreviation = val;
        else if (key == "description") team.Description = val;
        else if (key == "goal sound") team.GoalSound = val;
        else if (key == "goal sound data") team.GoalSoundData = val;
        else if (key == "goal sound volume") team.GoalSoundVolume = val;
        else if (key == "opponent preset") team.OpponentPreset = val;
        else if (key == "opponent effects") team.OpponentEffects = val;
        else if (key == "referee hands interaction") team.RefereeHandsInteraction = val;
        else if (key == "opponent two lines") team.OpponentTwoLines = val;
        else if (key == "opponent offense") team.OpponentOffense = val;
        else if (key == "opponent defense") team.OpponentDefense = val;
        else if (key == "match puck") team.MatchPuck = val;
        else if (key == "match surface") team.MatchSurface = val;
        else if (key == "golf fairway") team.GolfFairway = val;
        else if (key == "tycoons sponsors") team.TycoonsSponsors = val;
        else if (key == "canada curses") team.CanadaCurses = val;
        else if (key == "squad head") team.SquadHead = val;
        else if (key == "zamboni head") team.ZamboniHead = val;
        else if (key == "mascot") team.Mascot = val;
        else if (key == "squad template" || key == "template squad") team.SquadTemplate = val;
        else if (key.StartsWith("squad behavior ", StringComparison.OrdinalIgnoreCase)) team.SquadBehaviors[key] = val;
        else if (key == "starting relics mode") team.CustomStartingRelics = val.Equals("Custom", StringComparison.OrdinalIgnoreCase);
        else if (key == "starting bench positions") team.StartingBenchPositions = val;
        else if (key == "no effects bench") team.NoEffectsBench = val.Equals("yes", StringComparison.OrdinalIgnoreCase);
        else if (key == "no effects superstars") team.NoEffectsSuperstars = val.Equals("yes", StringComparison.OrdinalIgnoreCase);
        else if (key == "stat scale") team.StatScale = ParseRandomFloat(val);
        else if (key == "number style") team.Uniform.NumberFormat = FieldSchema.NumberStyleIndex(val);
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
        else if (key == "team talents" || key == "talents")
        {
            team.TeamTalents = CampaignCore.CustomRewardAssignments.TeamTalents(team.Fields).ToList();
        }
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
        p.Fields.Set(key, val);
        string colorKey = AppearanceFields.ColorKey(key);
        if (colorKey.Length > 0)
        {
            var rgb = ParseRandomColor(val);
            if (rgb != null) p.Colors.Set(colorKey, string.Join(",", rgb));
            return;
        }
        if (key == "name") p.Name = val;
        else if (key == "import player") p.ImportPlayer = val;
        else if (key == "number") p.Number = ParseRandomInt(val);
        else if (key == "number style") p.NumberFormat = FieldSchema.PlayerNumberStyleIndex(val);
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
            foreach (var t in val.Split(','))
            { string tr = t.Trim(); if (tr.Length > 0 && !p.Talents.Contains(tr)) p.Talents.Add(tr); }
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
        // Blank (the Studio's "Normal") means the game's own 1.0, as when the key is absent.
        else if (key == "size offset") p.SizeOffset = val.Trim().Length == 0 ? 1f : ParseRandomFloat(val);
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
        else if (key == "logo skin") p.LogoSkin = val;
        else if (key == "number skin") p.NumberSkin = val;
    }

    // Parses a file as key=value pairs, applying each pair via the provided callback
    private static void ParseKvFile(string path, Action<string, string> apply)
    {
        if (!File.Exists(path)) return;
        foreach (var raw in File.ReadAllLines(path))
        {
            if (CampaignCore.CampaignDocument.TryReadField(raw, out var key, out var val) && val.Length > 0)
                apply(key.ToLowerInvariant(), val);
        }
    }

    private static void LoadTeamFile(string path, TeamConfig team)
    {
        team.IdentityKey = Path.GetRelativePath(ModFolder, Path.GetDirectoryName(path));
        ParseKvFile(path, (k, v) => ApplyTeamField(team, k, v));
    }

    internal static void LoadPlayerFile(string path, PlayerConfig player)
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

        // Use the same saved-position precedence and legacy fallback as Studio.
        foreach (var file in Directory.GetFiles(playersDir, "*.txt", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var document = new CampaignCore.CampaignDocument(File.ReadAllText(file));
            var slot = SlotFor(CampaignCore.RosterPosition.Read(file, playersDir, document));
            if (slot != null)
            {
                LoadPlayerFile(file, slot);
            }
            else
                Log.LogWarning($"[Campaign] Could not determine position for player file: {Path.GetFileName(file)}");
        }
    }

    // Goalie styles the Studio keeps per team (Team look's goalie pickers).
    private static readonly string[] TeamGoalieGearKeys = { "skin", "skin away", "goalie pants skin", "goalie pants away", "helmet skin",
        "glove skin", "glove away", "blocker skin", "blocker away", "pads skin", "pads away", "stick skin", "stick away" };

    // A team-look goalie wears the team's goalie gear, the way team-look skaters wear
    // team.txt's styles. An own-look goalie (Override Team Settings = yes) keeps its file's gear.
    private static void LoadTeamGoalieGear(string teamDir, TeamConfig team)
    {
        var goalie = team.Goalie;
        string path = Path.Combine(teamDir, "goalie-equipment.cfg");
        if (goalie == null || AppearanceFields.OverridesTeam(goalie.Fields) || !File.Exists(path)
            || (string.IsNullOrEmpty(goalie.Name) && string.IsNullOrEmpty(goalie.ImportPlayer))) return;
        ParseKvFile(path, (k, v) =>
        {
            if (Array.IndexOf(TeamGoalieGearKeys, k) < 0) return;
            ApplyPlayerField(goalie, k, v);
            goalie.TeamGoalieGear = true;
        });
    }

    internal static void LoadCampaignFolders()
    {
        string teamsDir = Path.Combine(ModFolder, "teams");
        if (!Directory.Exists(teamsDir))
        {
            Log.LogInfo("[Campaign] No authored opponents — keeping base-game teams.");
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
            LoadTeamGoalieGear(teamDir, tc);
            if (!VanillaCampaignRuntime.AddOpponent(tc))
            {
                ConfigTeams.Add(tc);
                ConfigTeamDirs.Add(teamDir);
            }
            string name = !string.IsNullOrEmpty(tc.ImportTeam) ? $"IMPORT '{tc.ImportTeam}'" : $"'{tc.Name}'";
            Log.LogInfo($"  Loaded team: {Path.GetFileName(teamDir)} → {name}");
        }
        Log.LogInfo($"[Campaign] Multi-folder: {ConfigTeams.Count} teams loaded");

        LoadNodeAssignments();
        LoadNodeSoccer();
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

    // Nodes whose match is played with the soccer ball on the soccer field.
    // Same coordinate space as NodeAssignments, kept in its own file so the
    // assignments parser — which is load-bearing and works — is not touched.
    internal static HashSet<string> NodeSoccer =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    // Set by the launch patches, read by PatchPuckManager and SoccerMatch.
    internal static bool SoccerThisMatch = false;

    /// Read "Map 1 / Layer 2 / Node 0" into coordinates. Shared by both node
    /// files so they can never disagree about what a coordinate means.
    internal static bool TryReadNodeCoords(string lhs, out int mapPos, out int layer, out int node)
    {
        mapPos = -1; layer = -1; node = -1;
        foreach (var partRaw in (lhs ?? "").Split('/'))
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
        return mapPos >= 1 && layer >= 0 && node >= 0;
    }

    internal static void LoadNodeSoccer()
    {
        NodeSoccer.Clear();
        try
        {
            string path = Path.Combine(ModFolder, "node_soccer.txt");
            if (!File.Exists(path)) return;

            int parsed = 0;
            foreach (var raw in File.ReadAllLines(path))
            {
                string line = (raw ?? "").Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string value = line.Substring(eq + 1).Trim().ToLowerInvariant();
                // Only an explicit yes turns it on, so a "no" written by the
                // Creator reads as off rather than as a marked node.
                if (value != "yes" && value != "true" && value != "on" && value != "1") continue;
                if (!TryReadNodeCoords(line.Substring(0, eq), out int mapPos, out int layer, out int node))
                {
                    Log.LogWarning($"[Soccer] Could not read node coordinates from '{line.Substring(0, eq).Trim()}'");
                    continue;
                }
                NodeSoccer.Add(NodeKey(mapPos, layer, node));
                parsed++;
            }
            if (parsed > 0) Log.LogInfo($"[Soccer] node_soccer.txt: {parsed} node(s) set to soccer");
        }
        catch (Exception ex) { Log.LogWarning($"[Soccer] node_soccer.txt: {ex.Message}"); }
    }

    internal static void LoadNodeAssignments(bool includeLayouts = true)
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
                if (!TryReadNodeCoords(lhs, out int mapPos, out int layer, out int node))
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
        finally { if (includeLayouts) CustomMapLayout.LoadAssignments(); }
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
        var copied=VanillaCampaignRuntime.FindByKey(want);if(copied!=null)return copied;

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
        if (!PlayerTeamsEnabled)
        {
            PlayerTeamConfigs.Clear();
            DraftPoolConfigs.Clear();
            SuperstarPoolList.Clear();
        }
        if (loadDraft)
        {
            DraftPoolConfigs.Clear();
            SuperstarPoolList.Clear();
            FreeAgentPoolList.Clear();
            foreach (var folder in new[] { Path.Combine(ModFolder, "free_agents"), Path.Combine(ModFolder, "player_teams", "free_agents"), Path.Combine(ModFolder, "player_teams", "free agents") })
            {
                if (!Directory.Exists(folder)) continue;
                if(VanillaCopyData.PoolInherited(folder))continue;
                foreach (var file in (SelectedPlayerPool.Read(folder)??Directory.GetFiles(folder, "*.txt")).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    var config = new PlayerConfig(); LoadPlayerFile(file, config); FreeAgentPoolList.Add(config);
                }
            }
            Log.LogInfo($"[PlayerTeam] Loaded {FreeAgentPoolList.Count} free agents from campaign pool files");
            CustomFreeAgentTemplates.Invalidate();
        }
        string rootDir = Path.Combine(ModFolder, "player_teams");
        if (!Directory.Exists(rootDir)) return;
        Log.LogInfo($"[PlayerTeam] Loading from player_teams/ (teams={loadTeams}, draft={loadDraft})");

        foreach (var subDir in Directory.GetDirectories(rootDir))
        {
            string folderName = Path.GetFileName(subDir);
            string lower = folderName.ToLower();

            if (lower == "draft_pool" || lower == "draft pool")
            {
                if (!loadDraft || !PlayerTeamsEnabled) continue;
                if(VanillaCopyData.PoolInherited(subDir))continue;
                foreach (var file in CampaignCore.SelectedPlayerPool.Read(subDir, VanillaCopyData.IsPool(subDir)?int.MaxValue:9) ?? Directory.GetFiles(subDir, "*.txt"))
                {
                    string playerName = Path.GetFileNameWithoutExtension(file);
                    var pc = new PlayerConfig();
                    LoadPlayerFile(file, pc);
                    DraftPoolConfigs[playerName.ToLower()] = pc;
                    Log.LogInfo($"  Draft pool: '{playerName}' loaded");
                }
                continue;
            }

            if (lower == "free_agents" || lower == "free agents") continue; // Loaded once from both supported locations above.

            if (lower == "superstars" || lower == "superstar")
            {
                if (!loadDraft || !PlayerTeamsEnabled) continue;
                SuperstarPoolList.Clear();
                if(VanillaCopyData.PoolInherited(subDir))continue;
                var ssFiles = CampaignCore.SelectedPlayerPool.Read(subDir)
                    ?? Directory.GetFiles(subDir, "*.txt").OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
                foreach (var file in ssFiles)
                {
                    string playerName = Path.GetFileNameWithoutExtension(file);
                    var pc = new PlayerConfig();
                    LoadPlayerFile(file, pc);
                    // A missing choice inherits the selected team. An explicit
                    // custom choice remains available for this superstar.
                    if (pc.Fields.Get("Override Team Settings").Length == 0)
                        pc.Fields.Set("Override Team Settings", "no");
                    pc.Fields.Set("Export Version", "");
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

            // Explicit off disables all player teams; legacy campaigns without the key
            // retain additive squads without enabling preset overrides.

            if (!PlayerTeamsEnabled || (isPreset && !loadTeams)) continue;

            var tc = new TeamConfig();
            LoadTeamFile(Path.Combine(subDir, "team.txt"), tc);
            LoadPlayersFolder(Path.Combine(subDir, "players"), tc);
            LoadTeamGoalieGear(subDir, tc);
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
        FreeAgentPoolList.Clear();
        FreeAgentSignedConfigs.Clear();
        AppliedFreeAgentPtrs.Clear();
        if (IsDefaultMode) return;
        if (!UsePlayerTeams)
            Log.LogInfo("[PlayerTeam] Player teams disabled or legacy preset overrides not enabled");
        LoadPlayerTeamsFolders(loadTeams: UsePlayerTeams, loadDraft: true);
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
            GmSquadActive = SquadBehaviorRuntime.Enabled(squadId, "gm_map", string.Equals(squadId, GmSquadId, StringComparison.OrdinalIgnoreCase));
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
        if (IsDefaultMode) return;
        int next = GamesPlayed;
        int total = ConfigTeams.Count;
        Log.LogInfo($"[Campaign] Progress: {next} games played | {total} authored opponents | Map {ActsCompleted + 1}/{TotalMaps}");
        if (next >= 0 && next < total && ConfigTeams[next] != null)
        {
            var t = ConfigTeams[next];
            string name = t.IsImport ? $"IMPORT '{t.ImportTeam}'" : $"'{t.Name}'";
            Log.LogInfo($"[Campaign] Next up: Game {next + 1} — {name}");
        }
        else
        {
            Log.LogInfo($"[Campaign] Game {next + 1}: no authored opponent — keeping the base-game team.");
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

    internal static bool ProgressWritable = true;

    internal static void SaveProgress()
    {
        if (IsDefaultMode || !ProgressWritable || string.IsNullOrEmpty(SavePath)) return;
        try
        {
            new CampaignCore.CampaignProgress(ActsCompleted, GamesPlayed, MapStartGamesPlayed).Save(SavePath);
        }
        catch (Exception ex) { Log.LogError($"[Campaign] Could not save progress to '{SavePath}': {ex.Message}"); }
    }

    internal static void LoadProgress()
    {
        if (IsDefaultMode || string.IsNullOrEmpty(SavePath)) return;
        try
        {
            var progress = CampaignCore.CampaignProgress.Load(SavePath, out bool recovered);
            ActsCompleted = progress.ActsCompleted;
            GamesPlayed = progress.GamesPlayed;
            MapStartGamesPlayed = progress.MapStartGamesPlayed;
            ProgressWritable = true;
            if (recovered) Log.LogWarning("[Campaign] Damaged or missing progress save; recovered previous counters from save.txt.bak.");
            Log.LogInfo($"[Campaign] Loaded progress: ActsCompleted={ActsCompleted}, GamesPlayed={GamesPlayed}, MapStartGamesPlayed={MapStartGamesPlayed}");
            // Config edits and generated runtime files must never erase a saved run.
            // Explicit New Run and native run completion own progress resets.
        }
        catch (Exception ex)
        {
            ProgressWritable = false;
            Log.LogError($"[Campaign] Could not load progress from '{SavePath}': {ex.Message}. Existing files are preserved; restore a backup or start a New Run.");
        }
    }

    internal static void ResetProgressForNewRun()
    {
        ActsCompleted = 0;
        GamesPlayed = 0;
        MapStartGamesPlayed = 0;
        ProgressWritable = true;
    }

    // ===== MAP PROPERTIES (config-driven) =====
    internal static bool IsRemixed => !IsDefaultMode;

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

    // Is this the final map?
    internal static bool IsFinalMap => ActsCompleted >= ActSequence.Length - 1;

    public override void Load()
    {
        Log = base.Log;
        Log.LogInfo("=== Custom Campaign Framework v2.0 ===");
        ResolveCampaignPaths();
        LoadDefaults();
        LoadConfig();
        LoadPlayerTeams();
        LoadProgress();
        // Keep match-rule polling available independently of editor artwork.
        MatchRuleRunner.Install();
        // Also before it: a player exported to Play Now belongs to Play Now, not
        // to a campaign, so their uniform must not blink out when the active
        // campaign is "default".
        PlayNowOverrides.Install(new Harmony("com.mods.customcampaign.playnow"));
        LivePlayerPreview.Install();
        HeadAppearanceRuntime.Install(new Harmony("com.mods.customcampaign.headappearance"));
        AppearanceMaterialRuntime.Install(new Harmony("com.mods.customcampaign.appearancematerials"));
        AppearanceDecalRuntime.Install(new Harmony("com.mods.customcampaign.appearancedecals"));
        CustomHeadRuntime.Install(new Harmony("com.mods.customcampaign.customheads"));
        if (GamesPlayed > 0)
            LogNextGame();

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
            CustomRewardPlayNow.InstallForDefault();
            CustomRewardGoalRules.Install();
            CustomRewardCapabilities.Install();
            CustomRewardDataCapabilities.Install();
            CustomRewardTeamCapabilities.Install();
                CustomRewardActionControllers.Install();
            InitialMatchSurface.Install();
            Log.LogInfo("[Campaign] DEFAULT MODE active — campaign gameplay patches disabled; exported player content remains available.");
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
        CustomRewardGoalRules.Install();
        CustomRewardCapabilities.Install();
        CustomRewardDataCapabilities.Install();
            CustomRewardTeamCapabilities.Install();
                CustomRewardActionControllers.Install();
        OpponentEffectScope.Install(harmony);
        InitialMatchSurface.Install();

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

            // Refresh campaign logos once the native map node UI is ready.
            try
            {
                var refreshStates = AccessTools.Method(typeof(STS.Map.MapObject), "RefreshNodeStates");
                if (refreshStates != null)
                {
                    harmony.Patch(refreshStates,
                        postfix: new HarmonyMethod(typeof(PatchLateNodeArt), nameof(PatchLateNodeArt.Postfix)));
                    Log.LogInfo("Patched MapObject.RefreshNodeStates — campaign node-art refresh");
                }
                else Log.LogWarning("Could not find MapObject.RefreshNodeStates");
            }
            catch (Exception ex) { Log.LogError($"Failed node-art refresh patch: {ex}"); }

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

            // Studio's per-node stadium choice (see NodeStadiums).
            try
            {
                var setElite = AccessTools.Method(typeof(EliteMapNode), nameof(EliteMapNode.SetElite));
                if (setElite != null)
                    harmony.Patch(setElite, prefix: new HarmonyMethod(typeof(NodeStadiums), nameof(NodeStadiums.ElitePrefix)));
                var setBoss = AccessTools.Method(typeof(STS.Map.MapObject), "SetBoss",
                    new[] { typeof(Il2CppSystem.Collections.Generic.List<ActBoss>), typeof(BossMapNode) });
                if (setBoss != null)
                    harmony.Patch(setBoss, prefix: new HarmonyMethod(typeof(NodeStadiums), nameof(NodeStadiums.BossPrefix)),
                        finalizer: new HarmonyMethod(typeof(NodeStadiums), nameof(NodeStadiums.BossFinalizer)));
                Log.LogInfo($"Patched map node stadiums — elite {(setElite != null ? "ok" : "MISSING")}, boss {(setBoss != null ? "ok" : "MISSING")}");
            }
            catch (Exception ex) { Log.LogError($"Failed map node stadium patches: {ex}"); }
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
                    prefix: new HarmonyMethod(typeof(PatchInstantiateStartingTeam), nameof(PatchInstantiateStartingTeam.Prefix)), postfix: new HarmonyMethod(typeof(PatchInstantiateStartingTeam), nameof(PatchInstantiateStartingTeam.Postfix)), finalizer: new HarmonyMethod(typeof(PatchInstantiateStartingTeam), nameof(PatchInstantiateStartingTeam.Finalizer)));
                Log.LogInfo("Patched CampaignState.InstantiateStartingTeam — guard custom roster during run setup");
            }
            else
                Log.LogWarning("[InstStartTeam] CampaignState.InstantiateStartingTeam NOT FOUND — custom starting roster guard is unavailable");

            // NOTE: We do NOT patch TeamData.IsCreationLineUpPositionLocked.
            // That accessor is on an extremely hot native path (called for every
            // UI refresh / skater card); reading __instance.teamName inside the
            // postfix caused a fatal AccessViolationException on superstar pick.
            // Use the native creation locks; never hook this shared accessor.
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
        // CustomRewardPools now supplies complete allow-lists. Retired exclusion
        // filters restored unchecked rewards when a pool was too small.

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
            CustomSquadMenu.Install(harmony);
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

        // Apply the current match puck selected by opponent settings.
        try
        {
            var initPuck = AccessTools.Method(typeof(PuckManager), "InitializePuck");
            if (initPuck != null)
            {
                harmony.Patch(initPuck,
                    prefix: new HarmonyMethod(typeof(PatchPuckManager), nameof(PatchPuckManager.Prefix)),
                    finalizer: new HarmonyMethod(typeof(PatchPuckManager), nameof(PatchPuckManager.Finalizer)));
                Log.LogInfo("Patched PuckManager.InitializePuck — applies editor Match Puck settings");
            }
        }
        catch (Exception ex) { Log.LogError($"Failed PuckManager patch: {ex}"); }

        Log.LogInfo("Patch registration finished (see warnings/errors above for failures).");

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
// Increment GamesPlayed on match win (any match type)
// ============================================================
[HarmonyPatch(typeof(MatchMapNode), nameof(MatchMapNode.OnGameEnd))]
public static class PatchMatchGameEnd
{
    [HarmonyPrefix]
    public static void Prefix(MatchMapNode __instance, bool isWinning)
    {
        // Before the isWinning gate: a lost match ends the match just the same,
        // and the offside force source must not survive it.
        OffsideMatch.End();

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
        // BossMapNode OVERRIDES OnGameEnd, so the MatchMapNode patch that also
        // calls this does not necessarily run for a boss. Win or lose, the
        // offside force source has to come off at the end of the match.
        OffsideMatch.End();

        if (isWinning)
        {
            Plugin.ActsCompleted++;
            // Final boss — let the real victory screen show, don't redirect
            if (CustomMatchCompletion.Active ? CustomMatchCompletion.FinalBoss : Plugin.ActsCompleted >= Plugin.TotalMaps)
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
// Soccer nodes — the ball and the field, per match
// ============================================================
//
// The ball is handled in PatchPuckManager.cs. InitialMatchSurface supplies the
// field choice to the native loader before it starts its first surface load.
public static class SoccerMatch
{
    /// <summary>Called when a map node launches. Decides the match, not the campaign.</summary>
    internal static void BeginMatch(bool soccer, string why)
    {
        Plugin.SoccerThisMatch = soccer;
        if (soccer) Plugin.Log.LogInfo($"[Soccer] {why}: this match uses the soccer ball and field");
    }

    /// <summary>The node's coordinates, or false when it is not a marked node.</summary>
    internal static bool IsSoccerNode(STS.Map.MatchMapNode node)
    {
        if (node == null || Plugin.NodeSoccer.Count == 0) return false;
        try
        {
            if (node.Node == null) return false;
            string key = Plugin.NodeKey(Plugin.ActsCompleted + 1, node.Node.LayerIndex, node.Node.NodeIndex);
            return Plugin.NodeSoccer.Contains(key);
        }
        catch { return false; }
    }
}

// ============================================================
// Play Now custom players — the half of a look the save format drops
// ============================================================
//
// A Play Now custom skater is stored as Tape2Tape.Customization.ForwardDataModel
// and that class has fields for exactly six look values: headSkin, bodySkin,
// bodyAwaySkin, stickSkin, logoSkin and numberSkin. ForwardData — what actually
// gets rendered — has eleven more: helmetSkin, helmetAwaySkin, glassesSkin,
// bicepSkin, gloveSkin, pantsSkin, skateSkin and the away variant of each, plus
// colorSchemes. None of them survive a save, and unknown JSON keys are ignored
// on load, so no amount of writing from the Creator can carry them.
//
// So the Creator writes the editor's own player file next to the JSON, in
// play_now_overrides/<id>.txt, and this stamps the missing fields back on after
// the game has built the ForwardData from its model. Same key=value format and
// the SAME parser the campaign side uses, which is the point: a player exported
// to Play Now and the same player in a campaign now go through one set of
// mappings instead of two that could disagree.
//
// Only the fields the model CANNOT carry are applied. Name, stats, number,
// face, handedness, size, body, stick and logo all round-trip through the JSON
// correctly, and re-applying them here would fight the game's own editor if the
// player later tweaks them in-game.
public static class PlayNowOverrides
{
    private static Dictionary<string, PlayerConfig> _byId;
    private static string FolderPath => Path.Combine(Plugin.ModContentRoot, "play_now_overrides");

    /// <summary>Read every sidecar once. Cheap enough to redo on demand: these
    /// are a few hundred bytes each and only exist for exported players.</summary>
    internal static void Reload()
    {
        var map = new Dictionary<string, PlayerConfig>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (Directory.Exists(FolderPath))
                foreach (var file in Directory.GetFiles(FolderPath, "*.txt"))
                {
                    string id = Path.GetFileNameWithoutExtension(file);
                    if (string.IsNullOrEmpty(id)) continue;
                    var pc = new PlayerConfig();
                    try { Plugin.LoadPlayerFile(file, pc); }
                    catch (Exception ex) { Plugin.Log.LogWarning($"[PlayNow] '{Path.GetFileName(file)}': {ex.Message}"); continue; }
                    map[id] = pc;
                }
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[PlayNow] Could not read {FolderPath}: {ex.Message}"); }
        _byId = map;
        if (map.Count > 0)
            Plugin.Log.LogInfo($"[PlayNow] {map.Count} custom-player override file(s) loaded");
    }

    private static PlayerConfig Find(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (_byId == null) Reload();
        return _byId != null && _byId.TryGetValue(id, out var pc) ? pc : null;
    }

    /// <summary>Postfix on ForwardDataAdapter.ConvertToForwardData — runs once per
    /// custom skater as the game loads it, with the model's id still in hand.</summary>
    public static void ForwardPostfix(Tape2Tape.Customization.ForwardDataModel model, ForwardData __result)
    {
        if (__result == null || model == null) return;
        try
        {
            var pc = Find(model.id);
            if (pc == null) return;

            // The same helper the campaign side uses for an appearance-only slot.
            // Body and stick are in it too and will simply re-apply what the JSON
            // already set — harmless, and it keeps one list of look fields rather
            // than two that can drift apart.
            //
            // Home kit: a Play Now custom player has no away side of their own,
            // the team they are dropped into supplies that.
            __result.colorSchemes = TeamColorsData.CopyFrom(__result.colorSchemes ?? TeamColorsData.Default);
            PatchBossLaunchMatch.ApplyPlayerLookOnly(__result, pc, useAway: false);
            PlayNowTranslation.Player(__result, model.id);

            Plugin.Log.LogInfo($"[PlayNow] Uniform overrides applied to '{__result.firstName} {__result.lastName}' ({model.id})");
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[PlayNow] Forward '{model.id}': {ex.Message}"); }
    }

    /// <summary>Postfix on GoalieDataAdapter.ConvertToGoaltenderData. GoalieDataModel
    /// already carries every goalie skin slot, so only the colours are missing.</summary>
    public static void GoaliePostfix(Tape2Tape.Customization.GoalieDataModel model, GoaltenderData __result)
    {
        if (__result == null || model == null) return;
        try
        {
            var pc = Find(model.id);
            if (pc == null) return;
            __result.colorSchemes = TeamColorsData.CopyFrom(__result.colorSchemes ?? TeamColorsData.Default);
            PatchBossLaunchMatch.ApplyGoalieColorOverrides(__result, pc);
            PlayNowTranslation.Player(__result, model.id);
            Plugin.Log.LogInfo($"[PlayNow] Colour overrides applied to goalie '{__result.firstName} {__result.lastName}' ({model.id})");
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[PlayNow] Goalie '{model.id}': {ex.Message}"); }
    }

    /// <summary>Registered before the DEFAULT MODE return in Plugin.Load: an
    /// exported Play Now player is not part of any campaign, so their look must
    /// not depend on which campaign happens to be active.</summary>
    internal static void Install(Harmony h)
    {
        Reload();
        PlayNowTranslation.Install(h);
        try
        {
            var conv = AccessTools.Method(typeof(Tape2Tape.Customization.ForwardDataAdapter), "ConvertToForwardData");
            if (conv != null)
            {
                h.Patch(conv, postfix: new HarmonyMethod(typeof(PlayNowOverrides), nameof(ForwardPostfix)));
                Plugin.Log.LogInfo("[PlayNow] Patched ForwardDataAdapter.ConvertToForwardData");
            }
            else Plugin.Log.LogWarning("[PlayNow] ForwardDataAdapter.ConvertToForwardData not found — Play Now uniform overrides are off");
        }
        catch (Exception ex) { Plugin.Log.LogError($"[PlayNow] Forward hook: {ex}"); }
        try
        {
            var convG = AccessTools.Method(typeof(Tape2Tape.Customization.GoalieDataAdapter), "ConvertToGoaltenderData");
            if (convG != null)
            {
                h.Patch(convG, postfix: new HarmonyMethod(typeof(PlayNowOverrides), nameof(GoaliePostfix)));
                Plugin.Log.LogInfo("[PlayNow] Patched GoalieDataAdapter.ConvertToGoaltenderData");
            }
            else Plugin.Log.LogWarning("[PlayNow] GoalieDataAdapter.ConvertToGoaltenderData not found — Play Now goalie colours are off");
        }
        catch (Exception ex) { Plugin.Log.LogError($"[PlayNow] Goalie hook: {ex}"); }
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
    // The Choose Your Squad tile head is RunSquadScriptableObject.m_SmallTeamIcon
    // — a plain UI Sprite drawn by MetaTeamItem.m_KeyPlayerHead, NOT a Spine skin.
    // That means a runtime-created Sprite renders fine here (unlike jersey logos),
    // so a PNG is a valid source. Accepts either the name of a sprite the game
    // already has loaded, or any PNG in CustomLogos/. Returns null when neither
    // matches, so the caller can keep the template's icon.
    internal static UnityEngine.Sprite ResolveTileIcon(string value, string squadKey)
    {
        // Explicit folder choices must not collide with a face or native sprite name.
        if ((value ?? "").StartsWith("CustomLogos/", StringComparison.OrdinalIgnoreCase))
            return PatchBossLaunchMatch.LoadCustomLogoSprite(Path.GetFileNameWithoutExtension(value));
        string leaf = (value ?? "").Trim();
        int slash = leaf.LastIndexOf('/');
        if (slash >= 0) leaf = leaf.Substring(slash + 1);
        if (leaf.Length == 0) return null;

        // 1) Keep the full skin ID. The native portrait catalog maps that ID to
        //    a UI sprite; a skin's leaf is generally not its atlas-region name.
        var face = FaceToSprite((value ?? "").Trim());
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

    // The native customization catalog owns face-to-portrait associations.
    // Keep successful sprites only: the first squad clone may precede UI assets
    // loading, and an early miss must not permanently poison later attempts.
    private static readonly Dictionary<string, UnityEngine.Sprite> _faceIconCache =
        new Dictionary<string, UnityEngine.Sprite>(StringComparer.OrdinalIgnoreCase);

    private static UnityEngine.Sprite PortraitToSprite(string head, out string where)
    {
        where = "";
        var catalogs = UnityEngine.Resources.FindObjectsOfTypeAll<Tape2Tape.Customization.UI.SkaterUIHeadData>();
        // This is a resource-backed native catalog, not a scene-only component.
        // A typed resource load also works during the early title-screen clone.
        if (catalogs == null || catalogs.Length == 0)
            catalogs = UnityEngine.Resources.LoadAll<Tape2Tape.Customization.UI.SkaterUIHeadData>("");
        if (catalogs == null) return null;

        string requested = HeadAppearance.Base(head);
        bool qualified = requested.IndexOf('/') >= 0;
        string matched = null;
        Tape2Tape.Customization.UI.SkaterUIHeadData owner = null;
        foreach (var catalog in catalogs)
        {
            if (catalog == null) continue;
            var choices = new Il2CppSystem.Collections.Generic.List<string>();
            catalog.GetAllHeads(choices, false);
            foreach (string candidate in choices)
            {
                if (string.IsNullOrEmpty(candidate)) continue;
                bool same = candidate.Equals(requested, StringComparison.OrdinalIgnoreCase);
                if (!same && !qualified)
                    same = candidate.Substring(candidate.LastIndexOf('/') + 1).Equals(requested, StringComparison.OrdinalIgnoreCase);
                if (!same) continue;
                // Preserve legacy short names only when their catalog identity
                // is unambiguous. Never let GetHead return its unrelated default.
                if (matched != null && !matched.Equals(candidate, StringComparison.OrdinalIgnoreCase)) return null;
                matched = candidate; owner = catalog;
            }
        }
        if (owner == null) return null;
        var sprite = owner.GetHead(matched);
        if (sprite != null) where = $"native portrait '{matched}' from '{owner.name}'";
        return sprite;
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

    internal static UnityEngine.Sprite FaceToSprite(string face)
    {
        if (string.IsNullOrWhiteSpace(face)) return null;
        face = face.Trim();
        if (_faceIconCache.TryGetValue(face, out var cached) && cached != null) return cached;

        UnityEngine.Sprite made = null;
        try
        {
            // Both calls use native reference/string APIs. Never walk boxed
            // Spine SkinEntry structs: the former reflection path caused an
            // uncatchable 0xC0000005 access violation during squad cloning.
            string where = "";
            try { made = PortraitToSprite(face, out where); }
            catch (Exception ex) { Plugin.Log.LogWarning($"[CustomSquad] Portrait '{face}': {ex.Message}"); }
            if (made == null)
            {
                string leaf = face.Substring(face.LastIndexOf('/') + 1);
                made = RegionToSprite(leaf, out where); // retain raw-region compatibility
            }
            if (made != null)
                Plugin.Log.LogInfo($"[CustomSquad] Face '{face}' resolved from {where}");
            else
                Plugin.Log.LogInfo($"[CustomSquad] Face '{face}' has no available portrait or matching atlas region");
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[CustomSquad] FaceToSprite '{face}': {ex.Message}"); }

        if (made != null) _faceIconCache[face] = made;
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
            Plugin.LoadPlayerTeamsFolders(loadTeams: Plugin.UsePlayerTeams, loadDraft: true);
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

            var cs = __instance.m_CampaignState;
            if (cs == null) { Plugin.Log.LogWarning("[CustomSquad] CampaignState not found"); return; }

            CustomMapLayout.CaptureSources();
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
                            if ((sq.id ?? "").StartsWith("Custom_")
                                && Plugin.PlayerTeamConfigs.TryGetValue(sq.id.Substring(7), out var ownConfig)
                                && !string.IsNullOrWhiteSpace(ownConfig.SquadTemplate)) continue;
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
    // Build the custom squad tabs after the native menu has created its entries.
public static void PostfixMenu(Tape2Tape.Hockey.UI.ChooseMetaMenu __instance)
    {
        try { CustomSquadMenu.Build(__instance); }
        catch (Exception ex) { Plugin.Log.LogError($"[CustomSquadMenu] Could not build menu: {ex}"); }
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

        string legacyTemplate;
        try
        {
            var campaignFile = Path.Combine(Plugin.ModFolder, "campaign.txt");
            var campaign = new CampaignCore.CampaignDocument(File.Exists(campaignFile) ? File.ReadAllText(campaignFile) : "");
            legacyTemplate = campaign.Get("Squad Template", "Basic Squad");
        }
        catch (Exception ex) { Plugin.Log.LogError("[CustomSquad] " + ex.Message); return; }

        for (int i = 0; i < customKeys.Count; i++)
        {
            string key = customKeys[i];
            var cfg = Plugin.PlayerTeamConfigs[key];
            string wanted = string.IsNullOrWhiteSpace(cfg.SquadTemplate) ? legacyTemplate : cfg.SquadTemplate;
            cfg.SquadTemplate = wanted;
            RunSquadScriptableObject template = null;
            for (int t = 0; t < squads.Count; t++)
            {
                var candidate = squads[t];
                if (candidate == null || (candidate.id ?? "").StartsWith("Custom_")) continue;
                if (CampaignCore.SquadTemplatePolicy.Identity(candidate.id) == CampaignCore.SquadTemplatePolicy.RuntimeIdentity(wanted)
                    || CampaignCore.SquadTemplatePolicy.Identity(candidate.squadName) == CampaignCore.SquadTemplatePolicy.RuntimeIdentity(wanted)) { template = candidate; break; }
            }
            if (template == null) { Plugin.Log.LogError($"[CustomSquad] {key}: template '{wanted}' is unavailable"); continue; }
            bool freeBuild = CampaignCore.SquadTemplatePolicy.Identity(wanted) == "noeffects";
            if (freeBuild && !SquadStartingScreens.Enabled(cfg, "superstar_screen") &&
                !(SlotIsConfigured(cfg.LW) && SlotIsConfigured(cfg.RW) && SlotIsConfigured(cfg.C)
                  && SlotIsConfigured(cfg.LD) && SlotIsConfigured(cfg.RD)))
            {
                Plugin.Log.LogWarning($"[CustomSquad] No Effects team '{key}' needs five authored skaters when starting superstars are off");
                continue;
            }
            // Each team's preset supplies its own assets and behavior defaults.
            var teamTemplate = template;
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
                if (sq != null && (sq.id == customId || sq.name == customUnityName))
                {
                    if (CampaignSquadSetup.Matches(sq, teamTemplate))
                    {
                        CampaignSquadSetup.ApplyBench(sq);
                        alreadyPresent = true;
                    }
                    else squads.RemoveAt(j); // Rebuild new-run choices after a campaign-wide template change.
                    break;
                }
            }
            if (alreadyPresent)
            {
                EnsureSquadUnlocked(profile, customId);
                continue;
            }

            try
            {
                SquadBehaviorRuntime.Validate(cfg);
                var clone = UnityEngine.Object.Instantiate(teamTemplate);
                bool keepNativeSquad=VanillaCampaignRuntime.InheritedSquad(cfg);
                if(!keepNativeSquad)SquadPresetData.Apply(clone, cfg);
                int requiredBench = SquadBehaviors.Enabled(cfg.SquadTemplate, "trio_draft", k => cfg.SquadBehaviors.TryGetValue(k, out var v) ? v : "") ? 3
                    : SquadBehaviors.Enabled(cfg.SquadTemplate, "gauntlet_draft", k => cfg.SquadBehaviors.TryGetValue(k, out var v) ? v : "") ? 2 : 0;
                if(!keepNativeSquad)clone.m_MaxBenchwarmersOnBench = Math.Max(clone.m_MaxBenchwarmersOnBench, requiredBench);
                if (freeBuild && !SquadStartingScreens.Enabled(cfg, "bench_screen")) clone.m_MaxBenchwarmersOnBench = 0;
                else if (!keepNativeSquad && SquadStartingScreens.Enabled(cfg, "bench_screen") && clone.m_MaxBenchwarmersOnBench == 0) clone.m_MaxBenchwarmersOnBench = 3;
                clone.name = customUnityName;
                try { clone.squadName = displayName; } catch {}
                try { clone.id = customId; } catch {}
                Plugin.Log.LogInfo($"[CustomSquad] clone.id after set = '{clone.id}' (wanted '{customId}'), clone.name='{clone.name}', template.id='{template.id}'");
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

                var origTeam = teamTemplate.startingTeam;
                if (origTeam != null)
                {
                    var teamClone = UnityEngine.Object.Instantiate(origTeam);
                    if (freeBuild && !SquadStartingScreens.Enabled(cfg, "bench_screen")) teamClone.benchSize = 0;
                    else if (!keepNativeSquad && SquadStartingScreens.Enabled(cfg, "bench_screen") && teamClone.benchSize == 0) teamClone.benchSize = 3;
                    if (freeBuild) teamClone.relics = new Il2CppSystem.Collections.Generic.List<Rogue.Relic>();
                    teamClone.teamName = keepNativeSquad ? origTeam.teamName : key + " " + displayName;

                    Plugin.Log.LogInfo($"[CustomSquad] Cloned team '{origTeam.teamName}' -> '{teamClone.teamName}' fwds={teamClone.forwards?.Count ?? -1} goalie={(teamClone.goalie != null ? teamClone.goalie.firstName : "null")}");

                    // Preserve roster locks from the selected template (for example Trio).

                    // Deep-clone every ForwardData in the roster up front.
                    // Instantiate on TeamData only shallow-clones reference
                    // fields, so the cloned list still points at Basic's
                    // actual player SOs. ApplyPlayerTeamConfig would then
                    // mutate Basic directly (name/stats/talents on Basic's
                    // real LW, RW, etc.) — which corrupts the Basic tile and
                    // eventually wipes all squad rendering once the UI tries
                    // to reuse the stale SOs. Pre-cloning isolates our edits.
                    try { if(keepNativeSquad)VanillaCampaignRuntime.CloneNativePlayers(teamClone);else DeepCloneForwards(teamClone); }
                    catch (Exception cEx) { Plugin.Log.LogWarning($"[CustomSquad] DeepCloneForwards: {cEx.Message}"); }
                    if(!keepNativeSquad) {CampaignSquadSetup.OrderAuthoredLines(teamClone);SquadBehaviorRoster.Prepare(teamClone, cfg);}

                    // For any user-configured slot whose template forward is
                    // null (e.g. Basic Squad ships with LW/RW/C empty), fill
                    // the slot with a fresh clone of the first non-null
                    // forward in the list — gives ApplyPlayerTeamConfig a
                    // real ForwardData to overwrite with the user's player.
                    try { if(!keepNativeSquad)FillNullConfiguredSlots(teamClone, cfg); }
                    catch (Exception fEx) { Plugin.Log.LogWarning($"[CustomSquad] FillNullConfiguredSlots: {fEx.Message}"); }

                    // Apply the user's team config NOW so the menu preview
                    // shows the custom players (names, stats, skins) — not
                    // the Basic squad's roster we cloned from. firstApply
                    // wipes stale talents/relics from the cloned objects.
                    try { PatchPlayerTeamInit.ApplyPlayerTeamConfig(teamClone, cfg, null, firstApply: true); }
                    catch (Exception apEx) { Plugin.Log.LogError($"[CustomSquad] Skipping '{key}': its authored roster could not be applied: {apEx.Message}"); continue; }
                    if(!keepNativeSquad) {CustomStartingRoster.RemoveTemplateStarters(teamClone, cfg);SquadBehaviorRoster.UpdateLines(teamClone, cfg);SquadBehaviorRoster.BenchChosenStarters(teamClone, cfg);}

                    // The template supplies behavior, never unselected starters.
                    bool starterAtAngus = false;
                    if (!keepNativeSquad) starterAtAngus = BindAuthoredLead(teamClone, clone);
                    if(!keepNativeSquad)CustomStartingRoster.LockAuthoredSkaters(teamClone, cfg);

                    clone.startingTeam = teamClone;
                    if(keepNativeSquad && teamTemplate.angusPlayer!=null)
                        foreach(var nativePlayer in teamClone.forwards)
                            if(nativePlayer!=null && nativePlayer.id==teamTemplate.angusPlayer.id){clone.angusPlayer=nativePlayer;break;}

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
                        // The lead keeps its authored position. Use that slot's
                        // head for the tile without moving the player's roster entry.
                        var positions = new[] { Tape2Tape.Customization.UI.ESkaterPosition.LW, Tape2Tape.Customization.UI.ESkaterPosition.RW,
                            Tape2Tape.Customization.UI.ESkaterPosition.C, Tape2Tape.Customization.UI.ESkaterPosition.LD, Tape2Tape.Customization.UI.ESkaterPosition.RD };
                        if (starterAtAngus && clone.angusPlayer != null)
                            for (int p = 0; p < Math.Min(5, teamClone.forwards.Count); p++)
                                if (teamClone.forwards[p]?.Pointer == clone.angusPlayer.Pointer) { picked = positions[p]; break; }
                        if (!picked.HasValue)
                            foreach (int p in new[] { 2, 0, 1, 3, 4 })
                                if (p < teamClone.forwards.Count && teamClone.forwards[p] != null) { picked = positions[p]; break; }
                        if (!picked.HasValue && teamClone.goalie != null) picked = Tape2Tape.Customization.UI.ESkaterPosition.Goalie;
                        if (picked.HasValue && !keepNativeSquad)
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
                                    if (string.IsNullOrWhiteSpace(cfg.ZamboniHead)) clone.m_ZamboniSkaterHead = icon;
                                }
                            }
                            catch (Exception spEx) { Plugin.Log.LogWarning($"[CustomSquad] Tile icon: {spEx.Message}"); }
                        }
                        if (!string.IsNullOrWhiteSpace(cfg?.ZamboniHead)
                            && !cfg.ZamboniHead.Equals("none", StringComparison.OrdinalIgnoreCase))
                        {
                            try
                            {
                                var icon = ResolveTileIcon(cfg.ZamboniHead, key);
                                if (icon != null) clone.m_ZamboniSkaterHead = icon;
                            }
                            catch (Exception spEx) { Plugin.Log.LogWarning($"[CustomSquad] Zamboni head: {spEx.Message}"); }
                        }
                        if (!string.IsNullOrWhiteSpace(cfg?.Mascot))
                        {
                            var source = UnityEngine.Resources.FindObjectsOfTypeAll<RunSquadScriptableObject>()
                                .FirstOrDefault(s => s != null && !s.id.StartsWith("Custom_")
                                    && s.id.Equals(cfg.Mascot, StringComparison.OrdinalIgnoreCase));
                            if (source?.m_Plushie != null) clone.m_Plushie = source.m_Plushie;
                            else Plugin.Log.LogWarning($"[CustomSquad] Mascot '{cfg.Mascot}' unavailable; keeping template mascot.");
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
                if(!VanillaCampaignRuntime.PreserveStartingRelics(cfg))
                {
                    try
                    {
                        var relicsToApply = new List<string>();
                        if (!freeBuild && !cfg.CustomStartingRelics && teamTemplate.m_RelicsData != null)
                            foreach (var rule in teamTemplate.m_RelicsData)
                                if (rule.Relic != null && !(cfg.NoBenchBonus && rule.Relic.TryCast<Rogue.BenchBonus>() != null)
                                    && !relicsToApply.Contains(rule.Relic.name)) relicsToApply.Add(rule.Relic.name);
                        if (!freeBuild && !cfg.CustomStartingRelics && teamTemplate.startingTeam?.relics != null)
                            foreach (var relic in teamTemplate.startingTeam.relics)
                                if (relic != null && !(cfg.NoBenchBonus && relic.TryCast<Rogue.BenchBonus>() != null) && !relicsToApply.Contains(relic.name)) relicsToApply.Add(relic.name);
                        bool optOut = freeBuild || cfg.CustomStartingRelics || (cfg != null && cfg.NoBenchBonus);
                        bool templateBenchBonus = false;
                        if (teamTemplate.startingTeam?.relics != null)
                            foreach (var relic in teamTemplate.startingTeam.relics)
                                if (relic != null && relic.name.Equals("Bench Bonus", StringComparison.OrdinalIgnoreCase)) templateBenchBonus = true;
                        bool hasBenchBonus = false;
                        if (cfg?.Relics != null)
                            for (int r = 0; r < cfg.Relics.Count; r++)
                            {
                                var rn = cfg.Relics[r] ?? "";
                                if (rn.Trim().ToLowerInvariant() == "bench bonus") hasBenchBonus = true;
                                if (!relicsToApply.Contains(rn)) relicsToApply.Add(rn);
                            }
                        if (templateBenchBonus && !optOut && !hasBenchBonus && !relicsToApply.Contains("Bench Bonus")) relicsToApply.Insert(0, "Bench Bonus");

                        if (relicsToApply.Count > 0)
                        {
                            var arr = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<State.SquadRelicData>(relicsToApply.Count);
                            int writeIdx = 0;
                            for (int r = 0; r < relicsToApply.Count; r++)
                            {
                                // Same level parsing as GiveRelic — a squad's
                                // starting "Steel Mitts LVL2" is a level-2 asset
                                // and is invisible to a level-1 lookup.
                                int rLevel = PatchBossLaunchMatch.LevelFromRelicName(relicsToApply[r], out string rName);
                                var relic = PatchBossLaunchMatch.FindRelic(rName, rLevel);
                                if (relic == null)
                                {
                                    Plugin.Log.LogWarning($"[CustomSquad] Relic '{relicsToApply[r]}' (level {rLevel}) not found for '{key}'");
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
                        else if (freeBuild || cfg.CustomStartingRelics)
                            clone.m_RelicsData = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<State.SquadRelicData>(0);
                        // Grant through the native squad-starting-relic path once.
                        // The live match refresh must never rebuild this list.
                        clone.startingTeam.relics = new Il2CppSystem.Collections.Generic.List<Rogue.Relic>();
                    }
                    catch (Exception rEx) { Plugin.Log.LogWarning($"[CustomSquad] Relic assign failed for '{key}': {rEx.Message}"); }
                }

                // Gauntlet map: if "Gauntlet Map = yes" is set on the campaign,
                // copy the Gauntlet squad's maps list onto this custom clone so
                // picking the custom squad actually loads the gauntlet map. The
                // game keys map selection off each squad's own .maps list — our
                // clone of Basic otherwise inherits Basic's standard maps.
                string cloneMapSource = string.IsNullOrWhiteSpace(cfg.SquadTemplate) ? EffectiveMapSource() : "";
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
                CampaignSquadSetup.ApplyBench(clone);
                CampaignSquadSetup.Remember(clone, teamTemplate);
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

    // Authored names/imports and legacy player overrides identify selected slots.
    // An untouched default config does not authorize retaining a template player.
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

    // Point native lead-player mechanics at an authored active starter, without
    // moving it, inventing a placeholder, or retaining a template donor.
    internal static bool BindAuthoredLead(TeamData team, RunSquadScriptableObject clone)
    {
        clone.angusPlayer = null;
        if (team?.forwards == null) return false;
        foreach (int index in new[] { 2, 0, 1, 3, 4 })
            if (index < team.forwards.Count && team.forwards[index] != null)
            { clone.angusPlayer = team.forwards[index]; return true; }
        return false;
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
        // Clone and remap every line along with the players. Keeping the donor's
        // line IDs after assigning new player IDs leaves saved lineups dangling.
        team.lines = team.CloneLines();
        var remap = new Dictionary<string, string>();
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
                    if (!string.IsNullOrEmpty(f.id)) remap[f.id] = c.id;
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
        if (team.lines != null)
            foreach (var line in team.lines)
            {
                if (line == null) continue;
                string Map(string value) => value != null && remap.TryGetValue(value, out var id) ? id : value;
                line.leftWinger = Map(line.leftWinger);
                line.rightWinger = Map(line.rightWinger);
                line.center = Map(line.center);
                line.leftDefensemen = Map(line.leftDefensemen);
                line.rightDefensemen = Map(line.rightDefensemen);
            }
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

        PlayerConfig[] slotCfgs = { cfg.LW, cfg.RW, cfg.C, cfg.LD, cfg.RD, cfg.L2_LW, cfg.L2_RW, cfg.L2_C, cfg.L2_LD, cfg.L2_RD };
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

}

// Custom squad labels use the current native localization entry points.
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

    // Fires immediately after Refresh — Toggle may not be wired yet.
    public static void Postfix(Tape2Tape.Hockey.UI.MetaTeamItem __instance, RunSquadScriptableObject __0)
    {
        try
        {
            if (__0 == null || __instance == null) return;
            string id = __0.id;
            string uname = __0.name;
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
            // Campaign free agents (CustomFreeAgentTemplates) carry their own ids.
            if (!__result && !string.IsNullOrEmpty(id) && id.StartsWith(CustomFreeAgentTemplates.IdPrefix, StringComparison.Ordinal)) { __result = true; return; }
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
            // This hook is TitleScreen.NewRun only; Continue never resets counters.
            Plugin.ResetProgressForNewRun();
            CustomMapLayout.ResetForNewRun();
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
            if (injected) return; // only once per game launch
            if (__instance == null) return;
            if (Plugin.PlayerTeamConfigs == null || Plugin.PlayerTeamConfigs.Count == 0) return;
            PatchChooseMetaUI.InjectCustomSquads(__instance);
            injected = true;
            Plugin.Log.LogInfo("[Campaign] Custom squads registered via LoadRunData prefix — Continue Run should work");
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[Campaign] LoadRunData prefix: {ex.Message}"); }
    }


}

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
        if (CustomMapLayout.Active) return;
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
// Second line of defense for the free-agent node cap during node creation.
// ============================================================
public static class PatchCreateMapNodeFACap
{
    public static void Prefix(STS.Map.Node node)
    {
        if (CustomMapLayout.Active) return;
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
        if (CustomMapLayout.Active) return;
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
// Apply only authored configs and remember successful applications so a
// re-entered map does not reconfigure every node on each InitializeMap.
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
        VanillaCampaignRuntime.ForgetInstances();
        CustomTeamRules.ClearDescriptions();
        OpponentMatch.Clear();
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

            if(VanillaCampaignRuntime.IsCopiedOpponent(cfg)&&VanillaCampaignRuntime.TeamInherited(cfg))OpponentMatch.Apply(team,cfg);
            else PatchBossLaunchMatch.ApplyTeamFromConfig(team, cfg);
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

    /// <summary>The campaign config this TeamData was configured from (node
    /// assignment or game index), or null for a native team.</summary>
    internal static TeamConfig ConfigFor(TeamData team)
    {
        if (!TryGetId(team, out int id)) return null;
        if (_assigned.TryGetValue(id, out string nodeKey))
            return Plugin.NodeAssignments.TryGetValue(nodeKey, out string teamKey) ? Plugin.FindConfigTeamByKey(teamKey) : null;
        if(VanillaCampaignRuntime.ConfigFor(team) is {} native)return native;
        return _configured.TryGetValue(id, out int game) && game >= 0 && game < Plugin.ConfigTeams.Count
            ? Plugin.ConfigTeams[game] : null;
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

            if(VanillaCampaignRuntime.ConfigFor(team)!=null)return false;

            if (!PatchBossLaunchMatch.RemixTeamForGame(team, gameIndex))
                return false;
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
    // Build 25595829: SetElite/SetBoss now Clear() that UI (icon object deactivated),
    // so a sprite written into the Image stays invisible. NodeLogo.Show drives the
    // UI's own SetRewardIconRaw instead.
    internal static bool ReplaceRewardIcon(STS.Map.MatchMapNode node, TeamData team)
    {
        try { return NodeLogo.Show(node, team); }
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
            PatchMapNodeTooltip.Postfix(node);

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

    // NOTE (2026-08-01): the launch-deferral machinery that used to live here
    // existed ONLY for `Import Team = PLAYER` (mirror match), which has been
    // removed — see the removal note on ApplyTeamFromConfig. Every remaining
    // config resolves fine at map generation, so nothing needs deferring and the
    // map pass applies teams in one go.

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
                VanillaCampaignRuntime.Apply(match);
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
            int applied = 0, shared = 0, alreadyDone = 0;
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
                                else
                                {
                                    // Each authored branch owns its opponent. Native
                                    // nodes can share a repository TeamData asset.
                                    // Reuse the assigned clone on the second map hook.
                                    if (CampaignOpponents.AssignedKey(opp) != nkey)
                                    {
                                        var isolated = VanillaCampaignRuntime.IsCopiedOpponent(acfg)
                                            ? VanillaCampaignRuntime.CloneAssigned(acfg) : UnityEngine.Object.Instantiate(opp);
                                        if(!VanillaCampaignRuntime.IsCopiedOpponent(acfg))PatchChooseMetaUI.DeepCloneForwards(isolated);
                                        node.opponent = isolated;
                                        opp = isolated;
                                    }
                                    if (CampaignOpponents.EnsureAssigned(opp, acfg, nkey, $"map gen {nkey}"))
                                    {
                                        applied++;
                                        summary.Append($" L{layer}.N{nodeIdx}=>'{opp.teamName}'");
                                    }
                                    else alreadyDone++;
                                    ApplyNodeLogo(node, opp);
                                    continue;   // assigned nodes never fall through
                                }
                            }
                        }
                    }

                    if(VanillaCampaignRuntime.ConfigFor(opp)!=null)continue;
                    if (gameIndex >= 0 && gameIndex < Plugin.ConfigTeams.Count)
                        opp = CustomTeamRules.OwnOpponent(node);
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

                    // Check before calling so a second pass over the same map (we
                    // hook both SetOpponents and InitializeMap) stays quiet.
                    if (CampaignOpponents.AppliedGameIndex(opp) == gameIndex) { alreadyDone++; ApplyNodeLogo(node, opp); continue; }

                    if (CampaignOpponents.Ensure(opp, gameIndex, $"map gen (layer {layer})")) applied++;
                    summary.Append($" L{layer}=>G{gameIndex + 1}('{opp.teamName}')");

                    // opp.logo is populated by the apply above, so this covers both
                    // custom PNGs and logos borrowed from another team.
                    ApplyNodeLogo(node, opp);
                }
            }

            // A pass that changed nothing is the normal second hook firing; only
            // the first pass over a map should report applications.
            if (applied > 0 || shared > 0)
            {
                Plugin.Log.LogInfo($"[MapTeams] Map configured from game {baseGames + 1} ({why}): {layers.Count} match layer(s),"
                    + $" {applied} team(s) applied, {alreadyDone} already current, {shared} shared-TeamData conflict(s),"
                    + $" {challengesSkipped} challenge node(s) left vanilla.");
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
            if (FreeAgentTalentWeightRuntime.InRecruitmentScope) return;
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
                    var t = PatchBossLaunchMatch.FindTalent(id);
                    if (t == null) continue;
                    bool present = false;
                    for (int j = 0; j < excludedTalents.Count; j++)
                        if (excludedTalents[j] != null && excludedTalents[j].id == t.id) { present = true; break; }
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
            if (FreeAgentTalentWeightRuntime.InRecruitmentScope) return;
            if (__result == null || __result.Count == 0) return;
            if (Plugin.ExcludedRewardTalentIds.Count == 0) return;
            int before = __result.Count;
            for (int i = __result.Count - 1; i >= 0 && __result.Count > 1; i--)
            {
                var t = __result[i];
                if (t == null) continue;
                if ((Plugin.ExcludedRewardTalentIds.Contains(t.name ?? "") || Plugin.ExcludedRewardTalentIds.Contains(t.id ?? "")))
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
            if (FreeAgentTalentWeightRuntime.InRecruitmentScope) return;
            if (__result == null) return;
            if (Plugin.ExcludedRewardTalentIds.Count == 0) return;
            if ((Plugin.ExcludedRewardTalentIds.Contains(__result.name ?? "") || Plugin.ExcludedRewardTalentIds.Contains(__result.id ?? "")))
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
    private static RunSquadScriptableObject SelectedSquad(Tape2Tape.Hockey.UI.OldSquadMenu menu)
    {
        // Prefer the menu's own campaign state; global resources can include
        // stale states left loaded during a screen transition.
        try
        {
            var selected = menu?.m_CampaignState?.GetCurrentSquad();
            if (selected != null) return selected;
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[Superstar] Menu squad lookup: {ex.Message}"); }
        var states = UnityEngine.Resources.FindObjectsOfTypeAll<State.CampaignState>();
        foreach (var state in states)
        {
            try
            {
                var squad = state?.GetCurrentSquad();
                if (squad != null) return squad;
            }
            catch { }
        }
        return null;
    }

    private static TeamConfig SelectedTeamConfig(RunSquadScriptableObject squad)
    {
        if (squad == null) return null;
        string id = squad.id ?? "";
        if (id.StartsWith("Custom_", StringComparison.OrdinalIgnoreCase)
            && Plugin.PlayerTeamConfigs.TryGetValue(id.Substring(7), out var custom)) return custom;
        string name = squad.startingTeam?.teamName ?? "";
        foreach (var pair in Plugin.PlayerTeamConfigs)
            if (id.Equals(pair.Key, StringComparison.OrdinalIgnoreCase)
                || name.StartsWith(pair.Key, StringComparison.OrdinalIgnoreCase)) return pair.Value;
        return null;
    }

    public static void Postfix(Tape2Tape.Hockey.UI.OldSquadMenu __instance, Il2CppSystem.Collections.Generic.List<Data.ForwardData> __result)
    {
        try
        {
            if (!Plugin.PlayerTeamsEnabled) return;
            if (__result == null || __result.Count == 0)
            {
                Plugin.Log.LogInfo("[Superstar] GenerateSuperStarSkaters returned no templates — nothing to replace");
                return;
            }
            var squad = SelectedSquad(__instance);
            var team = squad?.startingTeam;
            var teamConfig = SelectedTeamConfig(squad);
            if(VanillaCampaignRuntime.InheritedSquad(teamConfig) && SquadPoolRuntime.Inherited(teamConfig, "superstars"))return;
            var pool = SquadPoolRuntime.Superstars(teamConfig);
            int N = pool.Count;
            if (N == 0 && SquadStartingScreens.EmptyPool("superstars", teamConfig)) { __result.Clear(); return; }
            if (N == 0 && team == null && teamConfig == null) return;
            Plugin.Log.LogInfo($"[Superstar] Using selected player team's look: '{team?.teamName ?? squad?.squadName ?? "unknown"}'");
            // Sample the full authored pool; fixed i % N made every candidate
            // after the native screen's slot count impossible to choose.
            var candidates = Enumerable.Range(0, N).ToArray();
            for (int k = N - 1; k > 0; k--)
            {
                int pick = UnityEngine.Random.Range(0, k + 1);
                int swap = candidates[k]; candidates[k] = candidates[pick]; candidates[pick] = swap;
            }
            for (int i = 0; i < __result.Count; i++)
            {
                var template = __result[i];
                if (template == null) continue;
                PlayerConfig cfg = N>0 ? pool[candidates[i % N]] : null;
                var clone = VanillaCampaignRuntime.CloneCandidate(cfg) ?? UnityEngine.Object.Instantiate(template);
                if (teamConfig != null)
                {
                    PatchPlayerTeamInit.ApplyUniformToForward(clone, teamConfig.Uniform);
                    PatchBossLaunchMatch.ApplyTeamEquipmentColors(clone, teamConfig, team);
                }
                else if (team?.homeColors != null)
                    clone.colorSchemes = CampaignAppearanceRuntime.Unset(); // team kit, either side
                if (N > 0)
                {
                    if(cfg.Fields.Get(VanillaCopyData.PlayerId).Length>0)CampaignSquadSetup.ApplyFreshPlayerConfig(clone,cfg);
                    else PatchPlayerTeamInit.ApplyConfigToForward(clone, cfg, applyName: true);
                }
                __result[i] = clone;
                if (cfg != null)
                {
                    Plugin.Log.LogInfo($"[Superstar] Slot {i} → '{cfg.Name}' (cycled {i % N + 1}/{N})");
                }
            }
        }
        catch (Exception ex) { __result?.Clear(); Plugin.Log.LogError($"[Superstar] Could not load selected choices; native replacements suppressed: {ex.Message}"); }
    }
}

// ============================================================
// Capture which superstar the player clicked. Stored so the run-start
// hook can append it to the team's fwds (the game otherwise drops the
// pick for Custom_* squads).
// ============================================================
public static class PatchOldSquadMenuSuperstar
{


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

            var PickedSuperstar = ssList[idx];
            Plugin.Log.LogInfo($"[Superstar] Captured pick idx={idx} → '{PickedSuperstar?.firstName} {PickedSuperstar?.lastName}'");
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[Superstar] OnClickSuperStarPostfix: {ex.Message}"); }
    }
}

// ============================================================
// Protect authored slots only during native starting-team construction.
// ============================================================
public static class PatchInstantiateStartingTeam
{
    public static void Prefix(RunSquadScriptableObject __0, out TeamConfig __state) => __state = CustomStartingRoster.Begin(__0);
    public static void Finalizer(TeamConfig __state) => CustomStartingRoster.End(__state);
    public static void Postfix(Data.TeamData __result, RunSquadScriptableObject __0)
    {
        try
        {
            if (__result == null) return;
            string squadId = "";
            try { squadId = __0?.id ?? ""; } catch { }
            bool isCustom = squadId.StartsWith("Custom_", StringComparison.Ordinal);

            if (!isCustom) return;

            Plugin.Log.LogInfo($"[InstStartTeam] postfix: team='{__result.teamName}' squadId='{squadId}' fwds.Count={__result.forwards?.Count ?? -1}");
            if (Plugin.PlayerTeamConfigs.TryGetValue(squadId.Substring(7), out var config) && !VanillaCampaignRuntime.InheritedSquad(config))
            {
                CustomStartingRoster.RemoveUnselectedConstructedPlayers(__result, __0);
                CampaignAppearanceRuntime.RestoreAuthoredEquipment(__result, config);
            }
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

            // Use the generated interop accessor; private inherited fields are
            // not found by GetField on an EliteMapNode's runtime type.
            var tmp = __instance.m_TooltipDesc;
            if (tmp == null) return;
            var cur = tmp.text ?? "";
            string suffix = $"\nvs {teamName}";
            string description = CustomTeamRules.Description(opp);
            if (!string.IsNullOrWhiteSpace(description)) suffix += "\n" + description;
            if (CustomTeamRules.IsCustom(opp)) tmp.text = suffix.TrimStart();
            else if (!cur.Contains(suffix.Trim())) tmp.text = cur + suffix;
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
            MatchPeriods.Preview(__instance);
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
            if (opp != null && CampaignOpponents.Ensure(opp, next, "MatchPreviewMenu.ShowMenu"))
                Plugin.Log.LogInfo($"[Preview] Corrected previewed opponent to game {next + 1} — map generation had it wrong");

            // 2. Repaint the widgets ShowMenu already filled. Prefer the node's
            //    own team so the text can never disagree with what will be played;
            //    fall back to the config when the node isn't readable.
            string shownName = opp?.teamName;
            string logoFrom = null;
            if (opp == null && next < Plugin.ConfigTeams.Count)
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
            // "RANDOM" is a directive, not a team name — never show it.
            if (shownName.Trim().Equals("RANDOM", StringComparison.OrdinalIgnoreCase)) return;

            if (__instance.teamName != null)
                __instance.teamName.text = shownName;

            var sprite = opp != null ? opp.logo : !string.IsNullOrEmpty(logoFrom)
                ? PatchBossLaunchMatch.LoadCustomLogoSprite(logoFrom) : null;
            if (sprite != null)
            {
                if (__instance.logo != null) { __instance.logo.sprite = sprite; __instance.logo.overrideSprite = sprite; }
                if (__instance.logoDropShadow != null) { __instance.logoDropShadow.sprite = sprite; __instance.logoDropShadow.overrideSprite = sprite; }
            }
            if (CustomTeamRules.IsCustom(opp))
            {
                string description = CustomTeamRules.Description(opp);
                if (__instance.m_DescriptionText != null) __instance.m_DescriptionText.text = description;
                if (__instance.m_DescriptionContainer != null) __instance.m_DescriptionContainer.SetActive(!string.IsNullOrWhiteSpace(description));
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
        // Belt and braces alongside the per-match End(): a run that ends without
        // a clean match end must still put the player's penalty setting back.
        OffsideMatch.End();
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
            int sceneCount = SceneManager.sceneCountInBuildSettings;

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

    [HarmonyPrefix]
    public static void Prefix(ref int act)
    {
        CustomMapLayout.ClearApplied();

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
// Apply authored boss/elite teams
// ============================================================
[HarmonyPatch(typeof(BossMapNode), nameof(BossMapNode.LaunchMatch))]
public static class PatchBossLaunchMatch
{
    [HarmonyPrefix]
    public static void Prefix(BossMapNode __instance)
    {
        try
        {
            SoccerMatch.BeginMatch(SoccerMatch.IsSoccerNode(__instance), "Boss node");
            OffsideMatch.BeginMatch("Boss node");

            var opponent = __instance.opponent;
            if (opponent == null) return;

            if (Plugin.IsRemixed)
            {
                if (!Plugin.IsDefaultMode && Plugin.GamesPlayed < Plugin.ConfigTeams.Count)
                    opponent = CustomTeamRules.OwnOpponent(__instance);
                // Normally a no-op: PatchMapOpponents already configured this node
                // at map generation. It still matters when the map-gen pass
                // couldn't run (loaded map, node regenerated by a chaos effect) or
                // when the node's predicted game index drifted from the real one —
                // GamesPlayed is the authority for the match that is about to start.
                if (CampaignOpponents.Ensure(opponent, Plugin.GamesPlayed, "BossMapNode.LaunchMatch"))
                    Plugin.Log.LogInfo($"[Remix] Boss '{opponent.teamName}' configured at launch (game {Plugin.GamesPlayed + 1})");
            }
            CustomMatchCompletion.Begin(__instance, true);
            OpponentMatch.Begin(__instance);
        }
        catch (Exception ex) { Plugin.Log.LogError($"[Remix] BossLaunch: {ex}"); }
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

    // Every Talent asset loaded in the process, not just the ones the repo
    // lists. See step 4 of FindTalentOnce for why this exists.
    internal static Rogue.Talent[] AllTalentCache;

    internal static Rogue.Talent FindTalent(string name)
    {
        var custom = CustomRewardRuntime.FindTalent(name, false);
        if (custom != null) return custom;
        var found = FindTalentOnce(name);
        if (found != null) return found;
        // Miss: the cached repo may have been grabbed before the game filled
        // it (or belong to an unloaded scene). Re-resolve and retry once.
        CachedTalentRepo = null;
        AllTalentCache = null;
        EnsureRepos();
        return FindTalentOnce(name);
    }

    private static Rogue.Talent FindTalentOnce(string name)
    {
        if (Guid.TryParse(name, out _))
            return UnityEngine.Resources.FindObjectsOfTypeAll<Rogue.Talent>()
                .FirstOrDefault(t => t != null && string.Equals(t.id, name, StringComparison.OrdinalIgnoreCase) && !t.name.EndsWith("(Clone)"));
        if (CachedTalentRepo?.talents == null) EnsureRepos();
        // Check aliases first
        if (TalentAliases.TryGetValue(name, out string aliased))
            name = aliased;

        if (CachedTalentRepo?.talents != null)
        {
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

        }

        // 4) Last resort: EVERY Talent asset in memory, not just the repo's
        //    curated `talents` list. That list is the pool the game draws
        //    random rewards from, so a perfectly real talent can be absent
        //    from it and still be grantable — Linesman (the offside talent) is
        //    exactly that case, and the removed Level-1 "xRay" was another.
        //    FindRelic below has always searched all assets this way; talents
        //    now match it, so "Talent 'X' not found" means the asset genuinely
        //    isn't loaded rather than merely un-pooled.
        if (AllTalentCache == null)
            try { AllTalentCache = UnityEngine.Resources.FindObjectsOfTypeAll<Rogue.Talent>(); } catch {}
        if (AllTalentCache != null)
        {
            // Exact asset name first — that also skips "<name>(Clone)" copies,
            // which are per-skater instances, not the shared asset.
            foreach (var t in AllTalentCache)
                if (t != null && t.name == name)
                    return LogAssetFallback(t, name);
            foreach (var t in AllTalentCache)
                if (t != null && t.name != null &&
                    t.name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return LogAssetFallback(t, name);
            // Clone-stripped: only clones of this talent are loaded.
            foreach (var t in AllTalentCache)
            {
                if (t?.name == null) continue;
                string bare = t.name.EndsWith("(Clone)", StringComparison.Ordinal)
                    ? t.name.Substring(0, t.name.Length - 7).TrimEnd() : t.name;
                if (bare.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return LogAssetFallback(t, name);
            }
            // Localized display name, same order as the repo pass above.
            foreach (var t in AllTalentCache)
            {
                if (t == null) continue;
                try
                {
                    string key = t.powerupName;
                    if (string.IsNullOrEmpty(key)) continue;
                    string display = LocalizationManager.GetTranslation(key, true, 0, true, false, null, null, true);
                    if (!string.IsNullOrEmpty(display) &&
                        display.Equals(name, StringComparison.OrdinalIgnoreCase))
                        return LogAssetFallback(t, name);
                }
                catch {}
            }
        }

        return null;
    }

    /// <summary>Say so when a talent came from the all-assets sweep rather than
    /// the repo — it means the talent is real but outside the reward pool, which
    /// is worth knowing when a campaign's talents behave unexpectedly.</summary>
    private static Rogue.Talent LogAssetFallback(Rogue.Talent t, string asked)
    {
        try { Plugin.Log.LogInfo($"[Remix] Talent '{asked}' resolved as asset '{t.name}' (not in the repo's pool list)"); } catch {}
        return t;
    }

    /// <summary>Work out which level of a relic a config line is asking for.
    ///
    /// FindRelic filters on level, so getting this wrong means the relic silently
    /// never applies. Two spellings reach us:
    ///
    ///   "Steel Mitts:2"    — the explicit syntax, name and level separated.
    ///   "Steel Mitts LVL2" — what the Creator's picker actually writes, because
    ///                        the game's own DISPLAY NAME for the upgraded relic
    ///                        ends in LVL2 and the picker lists relics by display
    ///                        name. Read as level 1 (the old behaviour), every
    ///                        upgraded relic in every campaign was dropped.
    ///
    /// The LVL2 suffix is kept on the name — it is part of the display name the
    /// lookup matches against, not decoration.</summary>
    internal static int LevelFromRelicName(string raw, out string name)
    {
        name = raw ?? "";
        int level = 1;
        int colon = name.IndexOf(':');
        if (colon >= 0)
        {
            string tail = name.Substring(colon + 1).Trim();
            name = name.Substring(0, colon).Trim();
            if (!int.TryParse(tail, out level) || level < 1 || level > 2) level = 1;
            return level;
        }
        string t = name.Trim();
        if (t.EndsWith("+", StringComparison.Ordinal) || t.EndsWith("LVL2", StringComparison.OrdinalIgnoreCase)
            || t.EndsWith("(Level 2)", StringComparison.OrdinalIgnoreCase)
            || t.EndsWith("Lv2", StringComparison.OrdinalIgnoreCase)
            || t.EndsWith("Level 2", StringComparison.OrdinalIgnoreCase))
            level = 2;
        name = t;
        return level;
    }

    internal static Rogue.Relic[] AllRelicCache;
    internal static Rogue.Relic FindRelic(string nameContains, int level = 1)
    {
        var custom = CustomRewardRuntime.FindRelic(nameContains, false);
        if (custom != null) return custom;
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

        // An exact ID already identifies its level; never force upgraded IDs to level one.
        if (Guid.TryParse(nameContains, out _))
            return AllRelicCache.FirstOrDefault(r => r != null && string.Equals(r.id, nameContains, StringComparison.OrdinalIgnoreCase) && !r.name.EndsWith("(Clone)"));

        // Some starting relic assets share an ID (Bum/standard Bench Bonus).
        // Exact asset tokens from Studio must win over shared localized names.
        var exactAsset = AllRelicCache.FirstOrDefault(r => r != null && r.level == level && string.Equals(r.name, nameContains, StringComparison.OrdinalIgnoreCase));
        if (exactAsset != null) return exactAsset;

        string BaseName(string value) => Regex.Replace(value ?? "", @"\s*(?:LVL2|\(Level 2\)|Lv2|Level 2|\+)\s*$", "", RegexOptions.IgnoreCase).Trim();
        foreach (var relic in AllRelicCache)
        {
            if (relic == null || relic.level != level) continue;
            try
            {
                if (string.Equals(relic.localizedRelicName, nameContains, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(BaseName(relic.localizedRelicName), BaseName(nameContains), StringComparison.OrdinalIgnoreCase)) return relic;
            }
            catch { }
        }

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


    internal static void GiveRelic(TeamData team, string relicName)
    {
        string requested = relicName;
        int level = LevelFromRelicName(relicName, out relicName);
        var relic = FindRelic(relicName, level);
        if (relic == null) { Plugin.Log.LogWarning($"[Remix] Relic '{relicName}' level={level} not found"); return; }
        if (team.relics == null)
            team.relics = new Il2CppSystem.Collections.Generic.List<Rogue.Relic>();
        team.relics.Add(relic);
        Plugin.Log.LogInfo($"[Remix] Gave relic '{relicName}'");
    }

    internal static void SetJerseyColors(TeamColorsData colors, Color primary, Color secondary, Color tertiary)
    {
        if (colors == null) return;
        colors.jerseyScheme.primaryColor = primary;
        colors.jerseyScheme.secondaryColor = secondary;
        colors.jerseyScheme.tertiaryColor = tertiary;
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
    internal static bool RemixTeamForGame(TeamData team, int gameNum)
    {
        // An absent opponent is a native match, never an implicit example team.
        if (team == null || Plugin.IsDefaultMode || gameNum < 0 ||
            gameNum >= Plugin.ConfigTeams.Count || Plugin.ConfigTeams[gameNum] == null)
            return false;

        try
        {
            EnsureRepos();
            ResetClearedPlayers();
            Plugin.Log.LogInfo($"[Campaign] Applying authored opponent for game #{gameNum + 1} (base team '{team.teamName}')");
            ApplyTeamFromConfig(team, Plugin.ConfigTeams[gameNum]);
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[Config] Failed to apply team {gameNum + 1}: {ex.Message}. No substitute team will be applied.");
            return false;
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
        string selectedFile = CampaignCore.LogoFiles.Resolve(Plugin.ModContentRoot,
            Path.Combine(UnityEngine.Application.persistentDataPath, "CustomLogos"), key);
        var native = selectedFile == null && !Path.IsPathRooted(key) ? GetNativeLogo(key) : null;
        if (native != null) { _customLogoCache[key] = native; return native; }

        UnityEngine.Sprite sprite = null;
        try
        {
            string dir = Path.Combine(UnityEngine.Application.persistentDataPath, "CustomLogos");
            string fileName = key.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? key : key + ".png";
            string path = selectedFile ?? Path.Combine(dir, fileName);
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

        if (sprite != null) _customLogoCache[key] = sprite;
        return sprite;
    }

    // Team identity stays on TeamData; only compatible player jerseys receive it.
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
                    if (f == null || AppearanceDecalRuntime.ClearIncompatibleLogo(f)) continue;
                    bool lefty = false; try { lefty = f.isLefty; } catch { }
                    try { f.logoSkin = lefty ? "Team_Logo/Custom_L" : "Team_Logo/Custom_R"; n++; } catch { }
                }
            if (team.goalie != null && !AppearanceDecalRuntime.ClearIncompatibleLogo(team.goalie))
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


    // ===== ABILITY HELPERS =====

    internal static Rogue.Ability FindAbility(string name)
    {
        var all = UnityEngine.Resources.FindObjectsOfTypeAll<Rogue.Ability>();
        var exact = all.FirstOrDefault(a => a != null && !a.name.EndsWith("(Clone)") &&
            (string.Equals(a.id, name, StringComparison.OrdinalIgnoreCase) || string.Equals(a.name, name, StringComparison.OrdinalIgnoreCase)));
        if (exact != null) return exact;
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

    internal static void SetPlayerAbility(ForwardData f, string abilityName)
    {
        if (f == null) return;
        // Explicitly empty authored loadouts must clear a cloned native ability.
        // "none" is an editor value, not an entry in the ability repository.
        if (string.Equals(abilityName?.Trim(), "none", StringComparison.OrdinalIgnoreCase))
        {
            f.ability = null;
            return;
        }
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
        var talent = FindTalent(talentName);
        if (talent == null) { Plugin.Log.LogWarning($"[Remix] Goalie talent '{talentName}' not found; loadout retained"); return; }
        if (talent != null && CustomRewardRuntime.Definitions.TryGetValue(talent.id, out var custom))
            CampaignCore.CustomRewardAssignments.ValidateField(custom, "Talents", true);
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
        g.powerups.Add(talent);
        try { g.ResetTalentCache(); } catch {}
        Plugin.Log.LogInfo($"  [Remix] Goalie {g.firstName} {g.lastName} got '{talentName}'");
    }

    internal static void GiveTalentToPlayer(ForwardData f, string talentName)
    {
        if (f == null) return;
        var talent = FindTalent(talentName);
        if (talent == null) { Plugin.Log.LogWarning($"[Remix] Talent '{talentName}' not found for {f.firstName}; loadout retained"); return; }
        if (talent != null && CustomRewardRuntime.Definitions.TryGetValue(talent.id, out var custom))
            CampaignCore.CustomRewardAssignments.ValidateField(custom, "Talents", false);
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

        Plugin.Log.LogInfo($"[Materialize] Team '{team.teamName}' written to disk ({teamDir})");
    }

    // ── REMOVED 2026-08-01: `Import Team = PLAYER` (mirror match) ──────────────
    // It cloned "the player's team" by scanning Resources.FindObjectsOfTypeAll
    // <TeamSelection> and taking a homeTeam. That is not a reliable source: at the
    // pre-match preview TeamSelection still holds STALE menu values, so the mirror
    // cloned the wrong team entirely. Observed on 2026-08-01 with a custom squad —
    // the player was 'Wacky Squad' but TeamSelection[0] read
    // home='Greasy Lettuce' visitor='Calaveras', and 'Greasy Lettuce' (merely what
    // that squad imports FROM) got cloned. Preferring a fully-populated
    // TeamSelection made it worse, because the stale entry is the complete-looking
    // one.
    //
    // The whole launch-deferral mechanism existed only to serve this, and went with
    // it. If it is ever revived, do NOT go through TeamSelection: take the player's
    // team from the match-init path, which genuinely knows it (see the
    // [DUMP/MatchInit-PRE(native)] hook, whose `team` argument is the real squad),
    // and apply the mirror there rather than at map generation or preview.
    internal static void ApplyTeamFromConfig(TeamData team, TeamConfig cfg)
    {
        OpponentRosterInheritance.PreserveNativeHelmetDefaults(team, cfg);
        CustomTeamRules.Prepare(team, cfg);
        ApplyTeamContents(team, cfg);
        OpponentLineBehavior.Apply(team, cfg);
        CustomTeamTalentAssignments.Apply(team, cfg);
        OpponentMatch.Apply(team, cfg);
        CustomTeamRules.ExportLogo(team, cfg);
        team.ResetRelicCache();
    }

    private static void ApplyTeamContents(TeamData team, TeamConfig cfg)
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

        // Legacy authored defaults may supply colors; otherwise retain the native look.
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
        }
        if (cfg.JerseyPrimary != null || cfg.JerseySecondary != null || cfg.JerseyAccent != null)
        {
            var p = cfg.JerseyPrimary != null ? new Color(cfg.JerseyPrimary[0]/255f, cfg.JerseyPrimary[1]/255f, cfg.JerseyPrimary[2]/255f) : team.primaryColorPlayer;
            var s = cfg.JerseySecondary != null ? new Color(cfg.JerseySecondary[0]/255f, cfg.JerseySecondary[1]/255f, cfg.JerseySecondary[2]/255f) : team.secondaryColorPlayer;
            var a = cfg.JerseyAccent != null ? new Color(cfg.JerseyAccent[0]/255f, cfg.JerseyAccent[1]/255f, cfg.JerseyAccent[2]/255f) : Color.white;
            SetJerseyColors(team.homeColors, p, s, a);
            team.primaryColorPlayer = p;
            team.secondaryColorPlayer = s;
        }

        // Away colors override
        if (cfg.AwayPrimary != null || cfg.AwaySecondary != null || cfg.AwayAccent != null)
        {
            var ap = cfg.AwayPrimary != null ? new Color(cfg.AwayPrimary[0]/255f, cfg.AwayPrimary[1]/255f, cfg.AwayPrimary[2]/255f) : Color.white;
            var as2 = cfg.AwaySecondary != null ? new Color(cfg.AwaySecondary[0]/255f, cfg.AwaySecondary[1]/255f, cfg.AwaySecondary[2]/255f) : team.primaryColorPlayer;
            var aa = cfg.AwayAccent != null ? new Color(cfg.AwayAccent[0]/255f, cfg.AwayAccent[1]/255f, cfg.AwayAccent[2]/255f) : Color.white;
            SetJerseyColors(team.awayColors, ap, as2, aa);
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
        if (CampaignCore.SquadTemplatePolicy.Identity(cfg.SquadTemplate) == "noeffects" && !SquadStartingScreens.Enabled(cfg, "bench_screen")) team.benchSize = 0;
        else if (cfg.BenchSize >= 0) team.benchSize = cfg.BenchSize;
        if (!string.IsNullOrEmpty(cfg.BenchHead)) try { team.vanillaBenchPlayerHead = cfg.BenchHead; } catch {}

        // Nuke existing talents/abilities/relics
        var fwds = team.forwards;
        if (fwds != null)
        {
            for (int i = 0; i < fwds.Count; i++)
            {
                if (!OpponentRosterInheritance.ApplyAuthoredPlayer(cfg, i)) continue;
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
                if (!OpponentRosterInheritance.ApplyAuthoredPlayer(cfg, i)) continue;
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
        // Native-copy reserves are intentional parts of that opponent's roster.
        // Explicit Line 2 slots are applied by OpponentLineBehavior afterward.
        if (OpponentRosterInheritance.NeutralizeExtraPlayers(cfg) && fwds != null && fwds.Count > 5)
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
            team.goalie.numberFormat = (NumberFormat)(cfg.Goalie.NumberFormat >= 0 ? cfg.Goalie.NumberFormat : cfg.Uniform.NumberFormat);
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
                gg.passPower = (int)(gg.passPower * s);
                gg.shotPower = (int)(gg.shotPower * s);
                gg.pokecheckSkill = (int)(gg.pokecheckSkill * s);
                gg.depth = (int)(gg.depth * s);
            }
            Plugin.Log.LogInfo($"[Config] '{team.teamName}': stats scaled by {s}x");
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[Config] ApplyStatScale: {ex.Message}"); }
    }

    internal static void ApplyPlayerColorOverrides(ForwardData f, PlayerConfig pc, bool useAway = false)
        => CampaignAppearanceRuntime.Player(f, pc, useAway);

    internal static void ApplyGoalieColorOverrides(GoaltenderData g, PlayerConfig pc, bool useAway = false)
        => CampaignAppearanceRuntime.Player(g, pc, useAway);

    internal static void ApplyTeamEquipmentColors(ForwardData f, TeamConfig cfg, TeamData team = null, bool useAway = false)
        => CampaignAppearanceRuntime.Team(f, cfg, team, useAway);

    internal static void ApplyTeamEquipmentColorsToGoalie(GoaltenderData g, TeamData team, TeamConfig cfg, bool useAway = false)
        => CampaignAppearanceRuntime.Team(g, cfg, team, useAway);

    internal static bool HasAppearanceOverride(PlayerConfig pc)
    {
        if (pc != null && pc.NumberFormat >= 0) return true;
        if (pc != null && HasSizeOffset(pc)) return true;
        if (pc == null || !AppearanceFields.OverridesTeam(pc.Fields)) return false;
        if (pc.LogoSkin != null || pc.NumberSkin != null || pc.Fields.Get("Logo From").Length > 0) return true;
        if (!string.IsNullOrEmpty(pc.Glasses)) return true;
        if (pc.StickOverride != null || pc.HelmetOverride != null || pc.HelmetAwayOverride != null
            || pc.BodyOverride != null || pc.BodyAwayOverride != null
            || pc.BicepOverride != null || pc.BicepAwayOverride != null
            || pc.GlovesOverride != null || pc.GlovesAwayOverride != null
            || pc.PantsOverride != null || pc.PantsAwayOverride != null
            || pc.SkatesOverride != null || pc.SkatesAwayOverride != null) return true;
        if (pc.SizeOffset > 0) return true;
        return HasColorOverride(pc);
    }

    /// <summary>True when any per-player colour is set, home or away.</summary>
    internal static bool HasColorOverride(PlayerConfig pc)
        => pc != null && AppearanceFields.OverridesTeam(pc.Fields) && pc.Colors.Fields.Count > 0;

    /// <summary>Apply ONLY the per-player look: equipment skins, glasses and
    /// colours. Name, stats, face, number, talents and ability are left exactly
    /// as they are.
    ///
    /// Two callers want precisely this and nothing more: a custom-squad slot
    /// configured for looks alone (the occupant is a draft pick whose stats must
    /// survive), and a Play Now custom player (whose identity and stats already
    /// round-trip through the game's own save file).</summary>
    internal static string DefaultStick => !string.IsNullOrEmpty(Plugin.DefaultTeam?.Uniform?.Stick) ? Plugin.DefaultTeam.Uniform.Stick : AppearanceContract.DefaultSkin("skater", "Stick");
    /// <summary>Size Offset multiplies the body scale for the player's Size
    /// (ForwardData.GetBodySize, unclamped), so values past the base game's
    /// 0.9-1.1 grow or shrink the skater in match.</summary>
    internal static bool HasSizeOffset(PlayerConfig pc) => pc.Fields.Get("Size Offset").Length > 0 && pc.SizeOffset > 0;
    internal static void ApplyPlayerLookOnly(ForwardData f, PlayerConfig pc, bool useAway = false)
    {
        if (f == null || pc == null) return;
        if (pc.NumberFormat >= 0) f.numberFormat = (NumberFormat)pc.NumberFormat;
        // Base game: the stick style is the player's own (player customizer), never the team's.
        f.stickSkin = pc.StickOverride ?? DefaultStick;
        // The player's body, not team equipment: applies whatever the Team equipment toggle says.
        if (HasSizeOffset(pc)) f.sizeOffsetPercentage = pc.SizeOffset;
        if (!AppearanceFields.OverridesTeam(pc.Fields))
        {
            PatchPlayerTeamInit.HandleNoHelmetSentinel(f);
            ApplyPlayerColorOverrides(f, pc, useAway);
            return;
        }
        if (pc.LogoSkin != null) f.logoSkin = pc.LogoSkin;
        if (pc.NumberSkin != null) f.numberSkin = pc.NumberSkin;
        if (!string.IsNullOrEmpty(pc.Glasses)) f.glassesSkin = Plugin.ResolveSkin(pc.Glasses, "glasses");
        if (pc.StickOverride != null)       f.stickSkin      = pc.StickOverride;
        if (pc.HelmetOverride != null)      f.helmetSkin     = pc.HelmetOverride;
        if (pc.HelmetAwayOverride != null) f.helmetAwaySkin = pc.HelmetAwayOverride;
        if (pc.BodyOverride != null)        f.bodySkin       = pc.BodyOverride;
        if (pc.BodyAwayOverride != null) f.bodyAwaySkin = pc.BodyAwayOverride;
        if (pc.BicepOverride != null)       f.bicepSkin      = pc.BicepOverride;
        if (pc.BicepAwayOverride != null) f.bicepAwaySkin = pc.BicepAwayOverride;
        if (pc.GlovesOverride != null)      f.gloveSkin      = pc.GlovesOverride;
        if (pc.GlovesAwayOverride != null) f.gloveAwaySkin = pc.GlovesAwayOverride;
        if (pc.PantsOverride != null)       f.pantsSkin      = pc.PantsOverride;
        if (pc.PantsAwayOverride != null) f.pantsAwaySkin = pc.PantsAwayOverride;
        if (pc.SkatesOverride != null)      f.skateSkin      = pc.SkatesOverride;
        if (pc.SkatesAwayOverride != null) f.skateAwaySkin = pc.SkatesAwayOverride;
        // "Helmet = none" is two things, not one — see HandleNoHelmetSentinel.
        PatchPlayerTeamInit.HandleNoHelmetSentinel(f);
        ApplyPlayerColorOverrides(f, pc, useAway);
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
                    var allFaces = HeadAppearance.NativeChoices;
                    resolvedFace = allFaces[Plugin.ConfigRng.Next(allFaces.Length)];
                    Plugin.Log.LogInfo($"  [Random] {f.firstName} got random face: {resolvedFace}");
                }
                f.headSkin = resolvedFace;
            }
            f.isLefty = pc.Lefty;
            f.isBlack = pc.Black;

            // Glasses
            if (AppearanceFields.OverridesTeam(pc.Fields) && !string.IsNullOrEmpty(pc.Glasses)) f.glassesSkin = Plugin.ResolveSkin(pc.Glasses, "glasses");

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
            // Stick style is the player's (base game); the team uniform has none.

            // Shared look translation also serves benches, recruits and Play Now.
            ApplyPlayerLookOnly(f, pc);
            f.logoSkin = AppearanceFields.OverridesTeam(pc.Fields) && pc.LogoSkin != null
                ? pc.LogoSkin : (pc.Lefty ? "Team_Logo/Custom_L" : "Team_Logo/Custom_R");
            f.number = pc.Number >= 0 && pc.Number < 100 ? pc.Number : 88;
            f.numberSkin = pc.NumberSkin ?? $"Numbers/Number_{f.number}{(pc.Lefty ? "LH" : "")}";
            f.numberFormat = (NumberFormat)(pc.NumberFormat >= 0 ? pc.NumberFormat : uniform.NumberFormat);
            PatchPlayerTeamInit.HandleNoHelmetSentinel(f);

            // Fallback defaults from defaults.txt — ensure no invisible/broken assets
            var ds = Plugin.DefaultSkater;
            var du = Plugin.DefaultTeam.Uniform;
            if (string.IsNullOrEmpty(f.headSkin))
                f.headSkin = !string.IsNullOrEmpty(ds.Face) ? Plugin.ResolveSkin(ds.Face) : "Faces/Toronto/Mathieu";
            if (string.IsNullOrEmpty(f.bodySkin))
                f.bodySkin = !string.IsNullOrEmpty(du.Body) ? du.Body : "Body/Customization/Customization_colors";
            if (string.IsNullOrEmpty(f.bodyAwaySkin))
                f.bodyAwaySkin = !string.IsNullOrEmpty(du.BodyAway) ? du.BodyAway : "Body/Customization/Customization_colors";
            if (string.IsNullOrEmpty(f.bicepSkin))
                f.bicepSkin = !string.IsNullOrEmpty(du.Bicep) ? du.Bicep : "Body_Bicep/Customization/Customization_colors";
            if (string.IsNullOrEmpty(f.gloveSkin))
                f.gloveSkin = !string.IsNullOrEmpty(du.Gloves) ? du.Gloves : "Body_Gloves/Customization/Customization_colors";
            if (string.IsNullOrEmpty(f.pantsSkin))
                f.pantsSkin = !string.IsNullOrEmpty(du.Pants) ? du.Pants : "Body_Pants/Customization/Customization_colors";
            if (string.IsNullOrEmpty(f.skateSkin))
                f.skateSkin = !string.IsNullOrEmpty(du.Skates) ? du.Skates : "Body_Skates/Customization/Customization_colors";
            if (string.IsNullOrEmpty(f.bicepAwaySkin)) f.bicepAwaySkin = "Body_Bicep/Customization/Customization_colors";
            if (string.IsNullOrEmpty(f.gloveAwaySkin)) f.gloveAwaySkin = "Body_Gloves/Customization/Customization_colors";
            if (string.IsNullOrEmpty(f.pantsAwaySkin)) f.pantsAwaySkin = "Body_Pants/Customization/Customization_colors";
            if (string.IsNullOrEmpty(f.skateAwaySkin)) f.skateAwaySkin = "Body_Skates/Customization/Customization_colors";
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
                    f.helmetAwaySkin = !string.IsNullOrEmpty(du.HelmetAway) ? du.HelmetAway : "Faces/Custom/Helmet_Colors";
            }
            if (string.IsNullOrEmpty(f.stickSkin))
                f.stickSkin = !string.IsNullOrEmpty(du.Stick) ? du.Stick : "Sticks/Black";
            if (string.IsNullOrEmpty(f.numberSkin))
            {
                int defNum = ds.Number > 0 && ds.Number < 100 ? ds.Number : 88;
                f.numberSkin = $"Numbers/Number_{defNum}";
            }
            if (!AppearanceDecalRuntime.ClearIncompatibleLogo(f) && string.IsNullOrEmpty(f.logoSkin)) f.logoSkin = "Team_Logo/Custom_R";
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
                    if (SquadPoolRuntime.IsFreeAgent(pc))
                    {
                        // Imported recruitment templates can still contain legacy
                        // random fields. Every random recruit grant must respect
                        // the independent exact-level pool; fixed talents above
                        // and ordinary roster randomization keep their own rules.
                        EnsureRepos();
                        foreach (string talentId in FreeAgentTalentWeightRuntime.SelectTemplateTalents(CachedTalentRepo, pool, f, pc.RandomTalentCount))
                            GiveTalentToPlayer(f, talentId);
                    }
                    else
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
            }

            // Ability AFTER talents (so it doesn't get wiped)
            if (!string.IsNullOrEmpty(pc.Ability))
                SetPlayerAbility(f, pc.Ability);
            // A default face or a later equipment effect can arrive after the
            // initial uniform pass. Keep unsupported heads helmetless here too.
            PatchPlayerTeamInit.HandleNoHelmetSentinel(f);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[Config] Error applying player '{pc.Name}': {ex.Message}");
        }
    }

    internal static void ApplyGoalieConfig(GoaltenderData g, PlayerConfig pc, bool useAway = false, bool appearanceOnly = false)
    {
        if (g == null || pc == null) return;
        if (pc.NumberFormat >= 0) g.numberFormat = (NumberFormat)pc.NumberFormat;

        try
        {
            // Name
            if (!string.IsNullOrEmpty(pc.Name))
            {
                var parts = pc.Name.Split(' ', 2);
                g.firstName = parts[0];
                g.lastName = parts.Length > 1 ? parts[1] : "";
            }

            // Live campaign refreshes must retain training and earned rewards.
            if (!appearanceOnly)
            {
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
            }

            // Native SkinHelper resolves these names directly in the goalie
            // skeleton. No donor or private randomization pool is involved.
            // Own-look goalies use their file's gear; team-look goalies the team's
            // goalie-equipment.cfg (merged into pc by LoadTeamGoalieGear).
            bool ownLook = AppearanceFields.OverridesTeam(pc.Fields);
            if (ownLook || pc.TeamGoalieGear)
            {
                if (pc.GoalieSkin != null) g.skin = Plugin.ResolveGoalieSkin(pc.GoalieSkin, "body");
                if (pc.GoalieSkinAway != null || pc.GoalieSkin != null) g.awaySkin = Plugin.ResolveGoalieSkin(string.IsNullOrEmpty(pc.GoalieSkinAway) ? "standard" : pc.GoalieSkinAway, "body");
                if (pc.GoalieGloveSkin != null) g.gloveSkin = Plugin.ResolveGoalieSkin(pc.GoalieGloveSkin, "glove");
                if (pc.GoalieGloveAway != null || pc.GoalieGloveSkin != null) g.awayGloveSkin = Plugin.ResolveGoalieSkin(string.IsNullOrEmpty(pc.GoalieGloveAway) ? "standard" : pc.GoalieGloveAway, "glove");
                if (pc.GoalieBlockerSkin != null) g.blockerSkin = Plugin.ResolveGoalieSkin(pc.GoalieBlockerSkin, "blocker");
                if (pc.GoalieBlockerAway != null || pc.GoalieBlockerSkin != null) g.awayBlockerSkin = Plugin.ResolveGoalieSkin(string.IsNullOrEmpty(pc.GoalieBlockerAway) ? "standard" : pc.GoalieBlockerAway, "blocker");
                if (pc.GoaliePadsSkin != null) g.padsSkin = Plugin.ResolveGoalieSkin(pc.GoaliePadsSkin, "pads");
                if (pc.GoaliePadsAway != null || pc.GoaliePadsSkin != null) g.awayPadsSkin = Plugin.ResolveGoalieSkin(string.IsNullOrEmpty(pc.GoaliePadsAway) ? "standard" : pc.GoaliePadsAway, "pads");
                if (pc.GoalieStickSkin != null) g.stickSkin = Plugin.ResolveGoalieSkin(pc.GoalieStickSkin, "stick");
                if (pc.GoalieStickAway != null || pc.GoalieStickSkin != null) g.awayStickSkin = Plugin.ResolveGoalieSkin(string.IsNullOrEmpty(pc.GoalieStickAway) ? "standard" : pc.GoalieStickAway, "stick");
                if (pc.GoalieHelmetSkin != null) g.helmetSkin = Plugin.ResolveGoalieSkin(pc.GoalieHelmetSkin, "helmet");
            }
            if (ownLook && pc.LogoSkin != null) g.logoSkin = pc.LogoSkin;
            // The donor may have a premade jersey (e.g. The Officials) and no
            // logo slot. Resolve again after applying the authored goalie kit.
            if (!AppearanceDecalRuntime.ClearIncompatibleLogo(g) && string.IsNullOrEmpty(g.logoSkin)) g.logoSkin = "Team_Logo/Custom_R";
            if (pc.Fields.Get("Number").Length > 0) g.number = pc.Number;

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
            if (!appearanceOnly && pc.Talents != null)
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
        dst.number = src.number;
        dst.numberSkin = src.numberSkin; dst.logoSkin = src.logoSkin;
        dst.numberFormat = src.numberFormat;
        CampaignAppearanceRuntime.Copy(src, dst);
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
        dst.number = src.number;
        dst.numberFormat = src.numberFormat;
        CampaignAppearanceRuntime.Copy(src, dst);
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

    internal static ForwardData FindPlayerByName(string name)
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
// Refresh the campaign logo after the native map UI creates its renderers.
// ============================================================
public static class PatchLateNodeArt
{

    public static void Postfix(STS.Map.MapObject __instance)
    {
        try
        {
            if (__instance == null || Plugin.IsDefaultMode) return;
            var nodes = __instance.MapNodes;
            if (nodes == null) return;
            // Refresh logos after the native node UI has finished building.
            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                if (node == null) continue;
                STS.Map.MatchMapNode match = null;
                try { match = node.TryCast<STS.Map.MatchMapNode>(); } catch { }
                if (match != null) PatchMapOpponents.EnsureNodeArt(match);
            }
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[NodeArt] Could not refresh map logos: {ex.Message}"); }
    }}

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

[HarmonyPatch(typeof(EliteMapNode), nameof(EliteMapNode.LaunchMatch))]
public static class PatchEliteLaunchMatch
{
    [HarmonyPrefix]
    public static void Prefix(EliteMapNode __instance)
    {
        try
        {
            SoccerMatch.BeginMatch(SoccerMatch.IsSoccerNode(__instance), "Elite node");
            OffsideMatch.BeginMatch("Elite node");

            var opponent = __instance.opponent;
            if (opponent == null) return;

            if (Plugin.IsRemixed)
            {
                // See PatchBossLaunchMatch.Prefix — normally already done at map
                // generation; this is the correcting fallback.
                if (!Plugin.IsDefaultMode && Plugin.GamesPlayed < Plugin.ConfigTeams.Count)
                    opponent = CustomTeamRules.OwnOpponent(__instance);
                if (CampaignOpponents.Ensure(opponent, Plugin.GamesPlayed, "EliteMapNode.LaunchMatch"))
                    Plugin.Log.LogInfo($"[Remix] Elite '{opponent.teamName}' configured at launch (game {Plugin.GamesPlayed + 1})");
            }
            CustomMatchCompletion.Begin(__instance, false);
            OpponentMatch.Begin(__instance);
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
    /// <summary>Dumps describe the shipped game only. FindObjectsOfTypeAll also returns
    /// objects made at runtime: the game's own custom teams and players (loaded from its
    /// save folder, Studio's Play Now exports included) and every clone this mod builds
    /// from campaign files, which carry campaign names and looks. Unity gives objects
    /// created at runtime negative instance ids; assets loaded from the game files have
    /// positive ones.</summary>
    internal static bool IsShipped(UnityEngine.Object o)
    {
        if (o == null) return false;
        try
        {
            if (o.GetInstanceID() < 0) return false;
            string name = o.name ?? "";
            if (name.EndsWith("(Clone)", StringComparison.Ordinal) || name.StartsWith("StudioBench_", StringComparison.Ordinal)) return false;
            var team = o.TryCast<TeamData>();
            if (team != null) return !CampaignCore.CustomTeamIdentity.IsCustom(team.id);
            var player = o.TryCast<SkaterData>();
            return player == null || !player.isCustomSkater;
        }
        catch { return false; }
    }

    internal static List<T> Shipped<T>(IEnumerable<T> loaded) where T : UnityEngine.Object
    {
        var kept = new List<T>(); int total = 0;
        if (loaded != null)
            foreach (var item in loaded)
            {
                if (item == null) continue;
                total++;
                if (IsShipped(item)) kept.Add(item);
            }
        Plugin.Log.LogInfo($"[Dump] {typeof(T).Name}: {kept.Count} shipped of {total} loaded (runtime and custom objects skipped)");
        return kept;
    }

    /// <summary>Collect every distinct value the game uses for each skin field and
    /// write them to _game_skins.txt, grouped by field. _gen_game_data.py turns this
    /// into the Creator's dropdown lists.</summary>
    internal static void DumpSkinFields(string root, IEnumerable<ForwardData> skaters, IEnumerable<GoaltenderData> goalies)
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


    // Helper: extract short name from a skin path (last path segment, no extension)
    private static string SkinShortName(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;
        int slash = path.LastIndexOf('/');
        return slash >= 0 ? path.Substring(slash + 1) : path;
    }

    // Generated interop exposes native cached lists as properties, not managed
    // fields. Typed access keeps these names checked when game interop changes.
    private static void AddSkinOptions(Il2CppSystem.Collections.Generic.List<string> list, HashSet<string> target)
    {
        try
        {
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
        string root         = Plugin.DumpRoot;
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
                    if (IsShipped(pt.leagueTeams[i])) leagueTeams.Add(pt.leagueTeams[i]);

            var seenCamp = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (pt.campaignTeams != null)
                for (int i = 0; i < pt.campaignTeams.Count; i++)
                {
                    var t = pt.campaignTeams[i];
                    if (IsShipped(t) && seenCamp.Add(t.teamName)) campaignTeams.Add(t);
                }
            if (pt.campaignExclusiveTeams != null)
                for (int i = 0; i < pt.campaignExclusiveTeams.Count; i++)
                {
                    var t = pt.campaignExclusiveTeams[i];
                    if (IsShipped(t) && seenCamp.Add(t.teamName)) campaignTeams.Add(t);
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
            AddSkinOptions(f._headSkins, hd);
            AddSkinOptions(f._bodySkins, bd);
            AddSkinOptions(f._helmetSkins, hm);
            AddSkinOptions(f._stickSkins, st);
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
            AddSkinOptions(g._helmetSkins, ghm);
            AddSkinOptions(g._gloveSkins, ggv);
            AddSkinOptions(g._blockerSkins, gbl);
            AddSkinOptions(g._padSkins, gpd);
            AddSkinOptions(g._stickSkins, gst);
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
                    if (!IsShipped(f) || string.IsNullOrEmpty(f.firstName)) continue;
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
            sb.AppendLine("#       weights=[<NodeType>:<weight>,..] is the node's native pool, or its map's fallback pool.");
            sb.AppendLine("# SHAPE=[nodes per layer] — templates of one map with equal SHAPE are");
            sb.AppendLine("# structurally interchangeable; see SUMMARY at the end.");
            sb.AppendLine();

            // squad -> act -> list of shape strings, for the summary at the end.
            var shapeIndex = new List<string>();
            int squadCount = 0, mapCount = 0, tmplCount = 0, nodeCount = 0;

            foreach (var sq in squads)
            {
                if (!IsShipped(sq)) continue;
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

                                string pool = "";
                                if (typeName == "NULL")
                                {
                                    try
                                    {
                                        var weights = new Dictionary<string, float>();
                                        if (nd.nodeTypeWeights != null && nd.nodeTypeWeights.Count > 0)
                                        {
                                            foreach (var weight in nd.nodeTypeWeights)
                                                if (weight != null) weights[weight.type.ToString()] = weight.weight;
                                        }
                                        else if (cfg.nodeTypeConfig?.nodeTypeChances != null)
                                        {
                                            foreach (var weight in cfg.nodeTypeConfig.nodeTypeChances)
                                                weights[weight.Key.ToString()] = weight.Value;
                                        }
                                        pool = " weights=[" + string.Join(",", weights.OrderBy(w => w.Key, StringComparer.Ordinal)
                                            .Select(w => w.Key + ":" + w.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture))) + "]";
                                    }
                                    catch (Exception ex) { Plugin.Log.LogWarning($"[Dump] Random pool unavailable for {sqName}/{mi}/{ti}/{layerIndex}/{nodeIndex}: {ex.Message}"); }
                                }
                                sb.AppendLine($"        NODE n={nodeIndex} type={typeName} pos={pos}"
                                    + $" out=[{outs}] group='{grp}' team='{team}'"
                                    + $" gm={gm} talents={tal} relics={rel}" + pool);
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
                        if (!IsShipped(t)) continue;
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

        var loadedTeams = UnityEngine.Resources.FindObjectsOfTypeAll<TeamData>();
        if (loadedTeams == null || loadedTeams.Length == 0)
        {
            Plugin.Log.LogInfo("[Dump] AutoDumpNameLists: TeamData not loaded yet — will retry on next trigger.");
            return;
        }

        // Mark done only AFTER we have team data so callers can retry if teams weren't ready.
        _guiListsDumped = true;
        var allTeams = Shipped(loadedTeams);
        var allPlayers = Shipped(UnityEngine.Resources.FindObjectsOfTypeAll<ForwardData>());
        var allGoalies = Shipped(UnityEngine.Resources.FindObjectsOfTypeAll<GoaltenderData>());
        if (allTeams.Count == 0 || allPlayers.Count == 0)
        {
            // Never replace good lists with empty ones if shipped objects can't be told apart.
            Plugin.Log.LogError("[Dump] No shipped teams or players recognised; name, skin and logo lists left unchanged.");
            try { DumpMapLayouts(root); } catch (Exception ex) { Plugin.Log.LogWarning($"[Dump] Map layouts: {ex.Message}"); }
            try { GameCatalogBridge.WriteSquads(root); } catch (Exception ex) { Plugin.Log.LogWarning($"[GameCatalog] Squad catalog: {ex.Message}"); }
            return;
        }

        // Map layer/node layouts, for the per-node team editor. Read-only.
        try { DumpMapLayouts(root); }
        catch (Exception ex) { Plugin.Log.LogWarning($"[Dump] Map layouts: {ex.Message}"); }
        try { GameCatalogBridge.WriteSquads(root); }
        catch (Exception ex) { Plugin.Log.LogWarning($"[GameCatalog] Squad catalog: {ex.Message}"); }

        // 1. Name lists (for GUI dropdowns)
        try
        {
            var teamNames = new List<string>();
            foreach (var t in allTeams)
                if (t != null && !string.IsNullOrEmpty(t.teamName))
                    teamNames.Add(t.teamName);
            teamNames.Sort();
            File.WriteAllLines(Path.Combine(root, "_game_team_names.txt"), teamNames.ToArray());

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
    }

    private static bool _logosDumped = false;
    private static void DumpTeamLogosToCustomLogos(IEnumerable<TeamData> allTeams, string modRoot)
    {
        if (_logosDumped) return;
        _logosDumped = true;
        try
        {
            string logoDir = Path.Combine(modRoot, "_team_logos");
            Directory.CreateDirectory(logoDir);

            int wrote = 0, skipped = 0, failed = 0;
            var logoNames = new List<string>();
            foreach (var t in allTeams)
            {
                if (t == null || string.IsNullOrEmpty(t.teamName)) continue;
                if (t.logo == null || CampaignCore.CustomTeamIdentity.IsCustom(t.id) || (t.logo.name ?? "").StartsWith("custom_", StringComparison.OrdinalIgnoreCase)) continue;
                // Never export the repository's fallback sprite. A team whose logo
                // never resolved still carries defaultLogo ('Teams_TapetoTape'),
                // and dumping that wrote the game's own house logo out under the
                // team's name — 34 identical copies, which then shadowed the real
                // artwork and looked like "custom logos don't work".
                if (IsFallbackLogo(t.logo))
                {
                    continue;
                }

                string safe = t.teamName;
                foreach (char c in Path.GetInvalidFileNameChars()) safe = safe.Replace(c, '_');
                logoNames.Add(t.teamName);

                string outPath = CampaignCore.LogoFiles.NativeFile(modRoot, t.teamName);

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

    internal static byte[] SpriteToPng(UnityEngine.Sprite sprite)
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

            // Some startup/headless graphics contexts return uninitialized memory
            // without throwing. Never replace a good cached crest with that PNG.
            var pixels = readable.GetPixels32();
            bool varied = false;
            if (pixels.Length > 0)
                for (int i = 1; i < pixels.Length; i++)
                    if (pixels[i].r != pixels[0].r || pixels[i].g != pixels[0].g ||
                        pixels[i].b != pixels[0].b || pixels[i].a != pixels[0].a) { varied = true; break; }
            if (!varied) throw new InvalidDataException("Logo readback returned a blank/constant image; keeping existing artwork.");

            return UnityEngine.ImageConversion.EncodeToPNG(readable);
        }
        finally
        {
            UnityEngine.RenderTexture.active = prevActive;
            if (rt != null) UnityEngine.RenderTexture.ReleaseTemporary(rt);
            if (readable != null) UnityEngine.Object.Destroy(readable);
        }
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

    // Use the same virtual formatters as the native UI. Reflection field order is
    // not a format-argument contract (and can expose IL2CPP object addresses).
    private static string RewardText(Func<string> read)
    {
        try { return StripKeywordTags(read() ?? "").Replace("\r", "").Replace("\n", "\n    "); }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[Dump] Native reward text unavailable: {ex.Message}");
            return "[In-game text unavailable]";
        }
    }


    private static void WriteSeparateFiles()
    {
        Plugin.Log.LogInfo("[Dump] WriteSeparateFiles starting...");
        string root      = Plugin.DumpRoot;
        string dirRewards = Path.Combine(root, "rewards");
        string dirGui    = Path.Combine(root, "_gui_data"); // internal — read by Campaign Creator
        foreach (var d in new[]{ root, dirRewards, dirGui })
            Directory.CreateDirectory(d);

        try { RewardInventory.Write(dirGui); }
        catch (Exception ex) { Plugin.Log.LogError($"[RewardInventory] {ex}"); }

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
                        display = RewardText(() => r.localizedRelicName);
                        string desc = RewardText(() => r.GetDescription(false, true, "1FFF75"));
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
                        string desc = RewardText(() => a.GetDescription(false, true, "1FFF75"));
                        string dName = RewardText(() => a.localizedPowerupName);
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
                foreach (var a in UnityEngine.Resources.FindObjectsOfTypeAll<Rogue.Ability>())
                    if (a != null && !string.IsNullOrEmpty(a.id) && !string.IsNullOrEmpty(a.name) && !a.name.EndsWith("(Clone)")) sbMap.AppendLine($"{a.name}|{a.id}");
                // Talent GUIDs appended to the same map file
                var allExportTalents = UnityEngine.Resources.FindObjectsOfTypeAll<Rogue.Talent>();
                if (allExportTalents != null)
                {
                    sbMap.AppendLine("[talents]");
                    foreach (var t in allExportTalents)
                        if (t != null && !string.IsNullOrEmpty(t.id) && !string.IsNullOrEmpty(t.name) && !t.name.EndsWith("(Clone)"))
                            sbMap.AppendLine($"{t.name}|{t.id}");
                }
                // Relic GUIDs, so the Creator can fill CustomTeam.relics — the
                // Play Now team format stores relics by id and the Creator had no
                // way to resolve one, so it wrote an empty list every time and
                // exported teams arrived with no starting relics at all.
                //
                // Keyed by DISPLAY NAME, because that is what the Creator's relic
                // picker lists and what campaign team.txt files contain. The
                // level-2 display name already ends in "LVL2", so upgraded relics
                // key distinctly without any extra encoding.
                try
                {
                    var allRelics = UnityEngine.Resources.FindObjectsOfTypeAll<Rogue.Relic>();
                    if (allRelics != null && allRelics.Length > 0)
                    {
                        sbMap.AppendLine("[relics]");
                        var seenRelic = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var r in allRelics)
                        {
                            if (r == null || string.IsNullOrEmpty(r.id)) continue;
                            string display = RewardText(() => r.localizedRelicName);
                            if (string.IsNullOrEmpty(display) || !seenRelic.Add(display)) continue;
                            sbMap.AppendLine($"{display}|{r.id}");
                        }
                    }
                }
                catch (Exception rex) { Plugin.Log.LogWarning($"[Dump] relic id map: {rex.Message}"); }
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
                        string desc = RewardText(() => t.GetDescription(false, true, "1FFF75"));
                        string stat = ""; // Included by the native formatter above.
                        string dName = RewardText(() => t.localizedPowerupName);
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
                        string disp2 = RewardText(() => t.localizedPowerupName);
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
// Apply authored appearance before the native match builds skaters.
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
        if (Plugin.IsDefaultMode || GameManager.GameType != GameType.Rogue) return;
        if (team == null) return;
        // Native starting-team construction/save rehydration can restore the
        // donor's black skate preset after authoring. Normalize only the style;
        // keep the saved roster, earned progress and personal palettes intact.
        if (CustomTeamRules.IsCustom(team) && team.forwards != null)
            foreach (var forward in team.forwards)
                if (forward != null)
                    forward.skateSkin = forward.skateAwaySkin = AppearanceContract.StandardSkates;
        // Apply player team config here since Team.Initialize(TeamData) was removed.
        PatchPlayerTeamInit.ApplyForTeamData(team);
    }
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
        // A Play Now export can have the same name as its source squad. Only
        // campaign matches may resolve that name back to campaign authoring.
        if (Plugin.IsDefaultMode || GameManager.GameType != GameType.Rogue) return;
        // Studio squads are fully authored before the selection menu. Match
        // initialization and save/resume must not reapply their starting data.
        if (CustomTeamRules.IsCustom(teamData)) return;

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

        // Legacy named presets use appearance-only refreshes. Modern custom
        // IDs returned above; no live path reapplies starting gameplay values.
        bool firstApply = !AppliedTeamPtrs.Contains(teamData.Pointer);
        if (firstApply) AppliedTeamPtrs.Add(teamData.Pointer);

        var cfg = Plugin.PlayerTeamConfigs[matchedKey];
        Plugin.Log.LogInfo($"[PlayerTeam] Applying config for '{matchedKey}' to team '{name}' (firstApply={firstApply})");

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
        ApplyPlayerTeamConfig(teamData, cfg, __instance, firstApply, liveRun: true);

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

    internal static void ApplyPlayerTeamConfig(TeamData team, TeamConfig cfg, Team instance, bool firstApply, bool liveRun = false)
    {
        if(VanillaCampaignRuntime.InheritedSquad(cfg))return;
        CustomTeamRules.Prepare(team, cfg);
        var fwds = team.forwards;
        PlayerConfig[] slotCfgs = { cfg.LW, cfg.RW, cfg.C, cfg.LD, cfg.RD,
            cfg.L2_LW, cfg.L2_RW, cfg.L2_C, cfg.L2_LD, cfg.L2_RD };
        var rosterNames = new string[fwds?.Count ?? 0];
        for (int i = 0; i < rosterNames.Length; i++)
        {
            var forward = fwds[i];
            if (forward != null) rosterNames[i] = ((forward.firstName ?? "") + " " + (forward.lastName ?? "")).Trim();
        }
        var authoredNames = new string[slotCfgs.Length];
        for (int i = 0; i < slotCfgs.Length; i++) authoredNames[i] = slotCfgs[i]?.Name;
        var targets = AuthoredRosterMatcher.Resolve(authoredNames, rosterNames, liveRun);
        // Only the detached new-run authoring clone may replace starting data.
        // Even the first live match can follow Coach, training or recruitment.
        // Its relics, talents, stats and abilities are already run progress.
        //
        // Wipe only authored players that we can identify. A chosen superstar
        // or draft pick can occupy an authored position in the live list.
        if (firstApply && !liveRun)
        {
            try
            {
                if (fwds != null)
                {
                    for (int slot = 0; slot < targets.Length; slot++)
                    {
                        int index = targets[slot];
                        if (index < 0 || index >= fwds.Count) continue;
                        var fwd = fwds[index];
                        if (fwd == null) continue;
                        if (!PatchChooseMetaUI.SlotIsConfigured(slotCfgs[slot])) continue;
                        try { fwd.powerups = new Il2CppSystem.Collections.Generic.List<Rogue.Talent>(); } catch {}
                    }
                }
                if (team.goalie != null && !string.IsNullOrEmpty(cfg.Goalie?.Name))
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
            PatchBossLaunchMatch.SetJerseyColors(team.homeColors, p, s, a);
            team.primaryColorPlayer = p;
            team.secondaryColorPlayer = s;
        }

        // Away colors
        if (cfg.AwayPrimary != null || cfg.AwaySecondary != null || cfg.AwayAccent != null)
        {
            var ap = cfg.AwayPrimary != null ? new Color(cfg.AwayPrimary[0]/255f, cfg.AwayPrimary[1]/255f, cfg.AwayPrimary[2]/255f) : Color.white;
            var as2 = cfg.AwaySecondary != null ? new Color(cfg.AwaySecondary[0]/255f, cfg.AwaySecondary[1]/255f, cfg.AwaySecondary[2]/255f) : team.primaryColorPlayer;
            var aa = cfg.AwayAccent != null ? new Color(cfg.AwayAccent[0]/255f, cfg.AwayAccent[1]/255f, cfg.AwayAccent[2]/255f) : Color.white;
            PatchBossLaunchMatch.SetJerseyColors(team.awayColors, ap, as2, aa);
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
        if (!liveRun)
        {
            if (CampaignCore.SquadTemplatePolicy.Identity(cfg.SquadTemplate) == "noeffects" && !SquadStartingScreens.Enabled(cfg, "bench_screen")) team.benchSize = 0;
            else if (cfg.BenchSize >= 0) team.benchSize = cfg.BenchSize;
        }
        if (!string.IsNullOrEmpty(cfg.BenchHead)) try { team.vanillaBenchPlayerHead = cfg.BenchHead; } catch {}

        // Apply uniform skins and equipment colors to existing forwards
        if (fwds != null)
        {
            for (int i = 0; i < fwds.Count; i++)
            {
                if (fwds[i] == null) continue;
                ApplyUniformToForward(fwds[i], cfg.Uniform);
                PatchBossLaunchMatch.ApplyTeamEquipmentColors(fwds[i], cfg, team);
            }

            // The clone uses position order. The live roster may contain chosen
            // skaters between authored players, so match by unique identity.
            for (int slot = 0; slot < slotCfgs.Length; slot++)
            {
                var pc = slotCfgs[slot];
                int index = targets[slot];
                if (pc == null || index < 0 || index >= fwds.Count || fwds[index] == null) continue;
                var forward = fwds[index];
                if (liveRun)
                {
                    PatchBossLaunchMatch.ApplyPlayerLookOnly(forward, pc);
                    continue;
                }
                // Skip slots with no real data — LoadPlayersFolder leaves
                // untouched slots at their default empty PlayerConfig.
                bool hasAny = !string.IsNullOrEmpty(pc.Name) || !string.IsNullOrEmpty(pc.ImportPlayer)
                               || !string.IsNullOrEmpty(pc.Face)
                               || pc.Speed != 50 || pc.ShotPower != 50
                               || pc.Accuracy != 50 || pc.Checking != 50
                               || (pc.Talents != null && pc.Talents.Count > 0)
                               || !string.IsNullOrEmpty(pc.Ability);
                if (!hasAny)
                {
                    // A slot configured PURELY for looks — a helmet, a pair of
                    // gloves, a jersey colour and nothing else — matches none of
                    // the tests above and used to be skipped outright, which is
                    // why uniform overrides on a custom squad appeared to do
                    // nothing. Apply just the look.
                    //
                    // NOT the full ApplyPlayerConfig: that writes the config's
                    // absolute stats, and an appearance-only file leaves those at
                    // 50, which would flatten whichever draft pick or superstar
                    // the game seated here.
                    if (PatchBossLaunchMatch.HasAppearanceOverride(pc))
                    {
                        try
                        {
                            PatchBossLaunchMatch.ApplyPlayerLookOnly(forward, pc);
                            Plugin.Log.LogInfo($"[PlayerTeam] slot {slot}: appearance-only override applied to "
                                + $"'{forward.firstName} {forward.lastName}' (stats and talents left alone)");
                        }
                        catch (Exception ex) { Plugin.Log.LogWarning($"[PlayerTeam] slot {slot} look apply error: {ex.Message}"); }
                    }
                    continue;
                }
                try { PatchBossLaunchMatch.ApplyPlayerConfig(forward, pc, cfg.Uniform); }
                catch (Exception ex) { fwds[index] = null; Plugin.Log.LogError($"[PlayerTeam] slot {slot} left empty because its selected player could not be applied: {ex.Message}"); continue; }
                // Stat Scale (custom squads): multiply the configured player's
                // freshly-set stats. ApplyPlayerConfig writes the config's
                // ABSOLUTE stat values every apply, so scaling here is
                // idempotent across re-applies (Spartan re-init etc.). Chosen
                // skaters have no authored match and keep their own stats.
                if (cfg.StatScale != 1.0f)
                {
                    forward.speed = (int)(forward.speed * cfg.StatScale);
                    forward.shotPower = (int)(forward.shotPower * cfg.StatScale);
                    forward.shotAccuracy = (int)(forward.shotAccuracy * cfg.StatScale);
                    forward.checking = (int)(forward.checking * cfg.StatScale);
                }
                Plugin.Log.LogInfo($"[PlayerTeam] Applied slot override: {pc.Name ?? pc.ImportPlayer}{(cfg.StatScale != 1.0f ? $" (stats x{cfg.StatScale})" : "")}");
            }
        }

        // Apply goalie config
        if (team.goalie != null && cfg.Goalie != null && !string.IsNullOrEmpty(cfg.Goalie.Name))
        {
            if (!liveRun && !string.IsNullOrEmpty(cfg.Goalie.ImportPlayer))
            {
                var srcGoalie = PatchBossLaunchMatch.FindGoalieByName(cfg.Goalie.ImportPlayer);
                if (srcGoalie != null)
                {
                    PatchBossLaunchMatch.CopyGoalieData(srcGoalie, team.goalie);
                    Plugin.Log.LogInfo($"[PlayerTeam] Imported goalie '{srcGoalie.firstName} {srcGoalie.lastName}'");
                }
            }
            PatchBossLaunchMatch.ApplyGoalieConfig(team.goalie, cfg.Goalie, appearanceOnly: liveRun);
            team.goalie.numberFormat = (NumberFormat)(cfg.Goalie.NumberFormat >= 0 ? cfg.Goalie.NumberFormat : cfg.Uniform.NumberFormat);
            // Stat Scale (custom squads): scale the goalie's freshly-set stats.
            // ApplyGoalieConfig writes the config's absolute values, so this is
            // idempotent across re-applies just like the forwards above.
            if (!liveRun && cfg.StatScale != 1.0f)
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
                gg.passPower = (int)(gg.passPower * cfg.StatScale);
                gg.shotPower = (int)(gg.shotPower * cfg.StatScale);
                gg.pokecheckSkill = (int)(gg.pokecheckSkill * cfg.StatScale);
                gg.depth = (int)(gg.depth * cfg.StatScale);
                Plugin.Log.LogInfo($"[PlayerTeam] Goalie stats scaled x{cfg.StatScale}");
            }
            PatchBossLaunchMatch.ApplyTeamEquipmentColorsToGoalie(team.goalie, team, cfg);
        }

        // Starting talents are player-owned, even when authored as a team default.
        // Never regrant them into Continue or overwrite earned live-run loadouts.
        if (!liveRun) CustomTeamTalentAssignments.Apply(team, cfg);

        // Apply relics (additive — adds starting relics). Only on firstApply:
        // subsequent re-applies during the same run (e.g. Spartan match init)
        // must not add duplicate copies of every starting relic.
        if (firstApply && !liveRun && cfg.Relics.Count > 0)
        {
            foreach (var r in cfg.Relics)
                PatchBossLaunchMatch.GiveRelic(team, r);
            Plugin.Log.LogInfo($"[PlayerTeam] Added {cfg.Relics.Count} starting relics");
        }

        Plugin.Log.LogInfo($"[PlayerTeam] Applied team config for '{team.teamName}'");
    }

    internal static void ApplyUniformToForward(ForwardData f, UniformConfig uniform)
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
            // Native saved skaters omit numberFormat. This appearance-only helper
            // also runs after Continue; retain the saved number and restore its style.
            if (uniform.NumberFormat >= 0 && uniform.NumberFormat < FieldSchema.NumberStyles.Length)
                f.numberFormat = (NumberFormat)uniform.NumberFormat;
            // Stick style is the player's (base game); the team uniform has none.
            HandleNoHelmetSentinel(f);
        }
        catch (Exception ex) { Plugin.Log.LogError($"[PlayerTeam] Uniform apply error: {ex.Message}"); }
    }

    // Forwards explicitly flagged as no-helmet via `Helmet = none` in config.
    // Checked by the fallback default-fill code so the default helmet skin
    // doesn't sneak back in for these players.
    internal static readonly HashSet<IntPtr> NoHelmetForwards = new HashSet<IntPtr>();

    // Keep the per-kit sentinel until rendering. Registering the face globally
    // would also remove helmets from other players who share that face.
    internal static void HandleNoHelmetSentinel(ForwardData f)
    {
        if (f == null) return;
        HeadAppearanceRuntime.Configure(f);
        // Base game: no skate style in either customizer. Colors come from the team kit or the
        // player's own skate colors (colorSchemes.skatesScheme).
        f.skateSkin = f.skateAwaySkin = AppearanceContract.StandardSkates;
        if (f.helmetSkin == "__NO_HELMET__" && f.helmetAwaySkin == "__NO_HELMET__")
            NoHelmetForwards.Add(f.Pointer);
        else NoHelmetForwards.Remove(f.Pointer);
    }

    // Names we've already announced in the log this session. ApplyDraftPool
    // runs on every Team.Initialize (many times per scene); this keeps the
    // log to one line per unique player rather than one per instance.
    private static readonly HashSet<string> _loggedDraftNames = new(StringComparer.OrdinalIgnoreCase);

    internal static void ApplyDraftPool()
    {
        if (SquadPoolRuntime.Own(SquadPoolRuntime.Current())) return;
        // The new selected pool creates isolated players on each custom squad.
        // Legacy name-based mutation would alter shared base players as well.
        if (File.Exists(Path.Combine(Plugin.ModFolder, "player_teams", "draft_pool", CampaignCore.SelectedPlayerPool.Manifest))) return;
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
        // Per-squad templates are installed directly; never use the shared legacy mapper.
        if (SquadPoolRuntime.Own(SquadPoolRuntime.Current())) return;
        if (Plugin.FreeAgentPoolList == null || Plugin.FreeAgentPoolList.Count == 0) return;
        // The template swap already makes every free agent an authored one; name mapping would re-dress them.
        if (CustomFreeAgentTemplates.Active) return;

        var rawList = PatchPreGenerateFreeAgents.LastOutput;
        if (rawList == null || rawList.Count == 0)
        {
            Plugin.Log.LogInfo("[PlayerTeam] ApplyFreeAgentPool: preGeneratedFreeAgents not available yet");
            return;
        }

        var candidates = CampaignCore.RewardPoolSelection.Shuffle(Plugin.FreeAgentPoolList, max => UnityEngine.Random.Range(0, max));
        int N = candidates.Count;
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
                var cfg = candidates[i % N];
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
        if (CustomFreeAgentTemplates.Active) return;
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
            if (pc.Fields.Get("Speed").Length > 0 || pc.Speed != 50) f.speed = pc.Speed;
            if (pc.Fields.Get("Shot Power").Length > 0 || pc.ShotPower != 50) f.shotPower = pc.ShotPower;
            if (pc.Fields.Get("Accuracy").Length > 0 || pc.Accuracy != 50) f.shotAccuracy = pc.Accuracy;
            if (pc.Fields.Get("Checking").Length > 0 || pc.Checking != 50) f.checking = pc.Checking;

            // Apply size
            if (pc.Fields.Get("Size").Length > 0 || (!string.IsNullOrEmpty(pc.Size) && pc.Size != "Medium"))
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
            if (pc.Fields.Get("Left Handed").Length > 0 || pc.Lefty) f.isLefty = pc.Lefty;
            if (pc.Fields.Get("Skin Color").Length > 0 || pc.Black) f.isBlack = pc.Black;

            // Apply number
            if (pc.Fields.Get("Number").Length > 0 || pc.Number != 88) f.number = pc.Number;

            // Apply talents
            if (pc.Talents.Count > 0)
            {
                f.powerups = new Il2CppSystem.Collections.Generic.List<Rogue.Talent>();
                foreach (var talentName in pc.Talents)
                    PatchBossLaunchMatch.GiveTalentToPlayer(f, talentName);
            }

            // The first authored talent clears the donor ability. Restore the
            // authored ability afterward, as ApplyPlayerConfig does for starters.
            if (!string.IsNullOrEmpty(pc.Ability)) PatchBossLaunchMatch.SetPlayerAbility(f, pc.Ability);

            PatchBossLaunchMatch.ApplyPlayerLookOnly(f, pc);
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
    // Team default; an explicit PlayerConfig.NumberFormat takes precedence.
    public int NumberFormat = 0;
}

public class PlayerConfig
{
    public readonly CampaignDocument Fields = new("");
    public readonly CampaignDocument Colors = new("");
    public string LogoSkin = null, NumberSkin = null;
    public string Name = "";
    public string ImportPlayer = "";
    public int Number = 88;
    // -1 follows the team's style; explicit native imports may differ per player.
    public int NumberFormat = -1;
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
    public bool TeamGoalieGear = false; // goalie skins above came from the team's goalie-equipment.cfg
}

public class TeamConfig
{
    public readonly CampaignDocument Fields = new("");
    public readonly CampaignDocument Colors = new("");
    public string GoalSound = "", GoalSoundData = "", GoalSoundVolume = "100";
    public string OpponentPreset = "";
    public string OpponentEffects = "";
    public string RefereeHandsInteraction = "";
    public string OpponentTwoLines = "";
    public string OpponentOffense = "";
    public string OpponentDefense = "";
    public string MatchPuck = "";
    public string MatchSurface = "";
    public string GolfFairway = "";
    public string TycoonsSponsors = "";
    public string CanadaCurses = "";
    internal string IdentityKey = "";
    public string SquadTemplate = "";
    public Dictionary<string, string> SquadBehaviors = new(StringComparer.OrdinalIgnoreCase);
    public string StartingBenchPositions = "";
    public string Name = "";
    public string City = "";
    public string Abbreviation = "";
    public string Description = "";
    public string SquadHead = "";     // face name for the custom-squad tile icon
    public string ZamboniHead = "";   // blank preserves the legacy SquadHead fallback
    public string Mascot = "";        // base-game squad ID supplying its plushie sprite
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
    public bool NoEffectsBench = false;
    public bool CustomStartingRelics = false;
    public bool NoEffectsSuperstars = false;
    public int TeamRandomTalents = 0;
    public List<string> TeamTalents = new List<string>();
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


