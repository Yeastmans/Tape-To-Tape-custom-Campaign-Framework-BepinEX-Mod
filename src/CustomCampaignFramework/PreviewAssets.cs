using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using BepInEx.Logging;
using Data;
using Il2CppInterop.Runtime.Injection;
using Spine.Unity;
using Tape2Tape.Customization.UI;
using UnityEngine;
using UnityEngine.Rendering;

namespace EndlessMode;

/// <summary>
/// Small persistent main-thread driver. PreviewAssets performs only a bounded
/// amount of work per Update, so title-screen loading is never blocked.
/// </summary>
public sealed class PreviewAssetsRunner : MonoBehaviour
{
    public PreviewAssetsRunner(IntPtr pointer) : base(pointer) { }

    private void Update()
    {
        PreviewAssets.Tick();
        // The only DontDestroyOnLoad per-frame hook the mod has. The soccer
        // field switch needs one because IceSetup does not exist until the match
        // scene has loaded, and switching from inside its own Initialize fights
        // the setup already in flight. It costs one bool test per frame when no
        // soccer node is being played.
        SoccerMatch.Tick();
        // Same reason as the soccer field: OffsideController only exists once
        // the match scene is loaded, so the rule is armed by polling for it.
        OffsideMatch.Tick();
    }
}

/// <summary>
/// Exports real game art to <c>_preview_assets/</c> so the Campaign Creator can
/// show what a face / piece of equipment actually looks like.
///
/// Two sources, deliberately different:
///   * Faces come from <see cref="SkaterUIHeadData"/>, which hands out real Unity
///     Sprites — a direct, cheap, always-available export.
///   * Equipment has no sprite anywhere; the only thing that knows what a skin
///     looks like is the Spine renderer. Those go through a render queue that
///     drives <see cref="SkaterPreviewInUI"/> and captures its mesh.
///
/// Never walk Spine's <c>Skin.Attachments</c> through IL2CPP reflection to shortcut
/// this — boxed SkinEntry structs hard-crash the process (0xC0000005, session 12).
/// </summary>
internal static class PreviewAssets
{
    // 5: every face exported, helmet suppression attempted (it did not work).
    // 6: helmeted faces dropped entirely. Removed — it left 25 faces of 132.
    // 7: ALL faces exported, including the "_Helmet" twins v5 collapsed away,
    //    each measured and grouped, and mirrored by name into faces/.
    // 8: faces render with NO helmet — the exporter now does both halves of
    //    Plugin.HandleNoHelmetSentinel (register the head as helmetless AND
    //    blank helmetSkin), which is what makes `Helmet = none` work in game.
    // 9: the shared camera frame unions its measurements instead of keeping the
    //    first centre, so heads are no longer cut off at the top edge.
    // 10: an empty layer capture is retried once before being cached as empty,
    //     so an async refresh landing in the next job's settle window cannot
    //     freeze a piece as "has no geometry" (the goalie's default helmet).
    internal const string ExporterVersion = "10";
    private const int PreviewSize = 320;

    // RefreshSkin_Internal is an async UniTask, so the skin is NOT applied by the
    // time RefreshSkater returns. These are frame budgets, escalated on retry.
    private static readonly int[] SettleFrames = { 12, 30, 60 };
    private const int MaxAttempts = 3;

    // A capture with almost nothing in it means the draw silently failed. An
    // earlier exporter wrote 161 byte-identical blank PNGs and reported success,
    // so an empty result is now a hard failure rather than a cached lie.
    private const int MinOpaquePixels = 64;

    // Values the Creator writes as UI placeholders. They are not real skins and
    // reach ForwardData only because the mod applied a half-configured player.
    private static readonly string[] SentinelValues =
    {
        "(use team default)", "(none)", "(default)"
    };

    private sealed class ManifestEntry
    {
        internal string Kind;
        internal string Role;
        internal string Field;
        internal string Value;
        internal string RelativePath;

        internal string Key => MakeKey(Role, Field, Value);
    }

    private sealed class RenderJob
    {
        internal string Kind;
        internal string Role;
        internal string Field;
        internal string Value;
        internal string RelativePath;
        internal SkaterData Player;
        internal TeamData Team;
        internal bool IsHome;
        internal bool IsExact;
        /// <summary>For a derived piece (Socks, Number) the real editor field
        /// whose art contains it, and therefore the one to vary on the donor.
        /// Null when the job's own Field is the real one.</summary>
        internal string SourceField;
        internal string RequestId;
        internal int Attempts;
        /// <summary>null = the normal flat preview; otherwise the colour channel
        /// ("primary"/"secondary"/"tertiary") or "base" for the un-tinted residue.</summary>
        internal string LayerChannel;
        internal readonly List<UnityEngine.Object> OwnedObjects = new();
    }

    private sealed class ExactRequest
    {
        internal string Id;
        internal bool IsGoalie;
        internal string FocusField;
        internal readonly List<(string key, string value)> Player = new();
        internal readonly List<(string key, string value)> Team = new();
    }

    private sealed class Capture
    {
        internal byte[] Png;
        internal ulong Signature;
        internal int OpaquePixels;
        internal Vector3 FrameCenter;
        internal float FrameHalf;
        /// <summary>Kept so a layer capture can be split into masks without
        /// decoding the PNG again. Summarize already reads them.</summary>
        internal Color32[] Pixels;
    }

    private readonly struct DrawItem
    {
        internal readonly Mesh Mesh;
        internal readonly int SubMesh;
        internal readonly Material Material;
        internal readonly Texture Texture;
        internal readonly Matrix4x4 Matrix;

        internal DrawItem(Mesh mesh, int subMesh, Material material, Texture texture, Matrix4x4 matrix)
        {
            Mesh = mesh; SubMesh = subMesh; Material = material; Texture = texture; Matrix = matrix;
        }
    }

    private static bool _installed;
    private static GameObject _runnerObject;
    private static string _root;
    private static string _manifestPath;
    private static string _statusPath;
    private static string _requestPath;
    private static string _responsePath;
    private static int _mainTexId;
    private static readonly Dictionary<string, ManifestEntry> Entries =
        new(StringComparer.Ordinal);
    private static readonly Queue<RenderJob> EquipmentQueue = new();
    private static readonly HashSet<string> QueuedKeys = new(StringComparer.Ordinal);

    private static bool _headsComplete;
    private static bool _inventoryBuilt;
    private static bool _cacheWasStale;
    // Separate from _cacheWasStale on purpose: that one is cleared by the first
    // WriteManifest, which can happen before the face dump runs.
    private static bool _purgeFacesPending;
    private static float _nextWorkTime;
    private static float _nextHeadScan;
    private static float _nextInventoryScan;
    private static float _nextRequestScan;
    private static float _nextStatusWrite;
    private static float _nextRendererWarning;
    private static float _nextRenderAttempt;
    private static float _nextStageAttempt;
    private static int _headsAvailable;
    private static int _headsWritten;
    private static int _headsCached;
    private static int _equipmentDiscovered;
    private static int _equipmentFailed;
    private static bool _readyLogged;

    private static RenderJob _activeJob;
    private static SkaterPreviewInUI _activePreview;
    private static bool _activeIsStage;
    private static int _framesRemaining;
    private static SkaterData _restorePlayer;
    private static TeamData _restoreTeam;
    private static string _restoreField;
    private static string _restoreFieldValue;
    private static SkaterData _restoreFieldOwner;
    private static ExactRequest _pendingExact;
    private static string _lastCompletedRequestId;
    private static string _exactAttemptId;
    private static int _exactAttempts;
    private static ulong _lastAcceptedSignature;
    private static int _identicalRun;
    private static bool _identicalWarned;
    private static readonly HashSet<ulong> AcceptedSignatures = new();
    private static readonly HashSet<string> SlotsDumped = new(StringComparer.Ordinal);
    private static bool _materialsDumped;

    /// <summary>
    /// One-off investigation into layered/recolourable previews. Left in place
    /// (behind this flag) because it is the only way to read Spine slot names and
    /// live shader properties — neither can be obtained offline. Turn it on to
    /// write `_preview_slots_*.txt` and `_preview_materials.txt`.
    /// </summary>
    private static readonly bool DumpSlotAndMaterialInventory = false;

    // Capture state
    private static readonly Dictionary<int, Material> SanitizedMaterials = new();
    private static Material _fallbackMaterial;
    private static bool _fallbackLogged;
    private static bool _useReplacementShader;
    private static int _consecutiveEmpty;
    private static bool _renderDisabled;
    private const int GiveUpAfterConsecutiveEmpty = 12;
    private static bool _diagStageLogged;
    private static bool _diagLiveLogged;
    private static ManualLogSource _silentLog;

    // Private off-screen renderer: lets the queue drain at the title screen and
    // keeps the export from hijacking the customization menu the player is using.
    private static GameObject _stageRoot;
    private static SkaterPreviewInUI _stagePreview;
    private static bool _stageBroken;
    private static bool _stageLogged;

    // Donor clones. Equipment is previewed by varying ONE field on a cloned
    // skater, never on a live ScriptableObject the game is still using.
    private static TeamData _donorTeam;
    private static ForwardData _donorForward;
    private static GoaltenderData _donorGoalie;

    internal static void Install()
    {
        if (_installed) return;
        _installed = true;
        try
        {
            _root = Path.Combine(Plugin.ModContentRoot, "_preview_assets");
            _manifestPath = Path.Combine(_root, "manifest.tsv");
            _statusPath = Path.Combine(_root, "status.tsv");
            _requestPath = Path.Combine(_root, "request.tsv");
            _responsePath = Path.Combine(_root, "current", "response.tsv");

            Directory.CreateDirectory(_root);
            Directory.CreateDirectory(Path.Combine(_root, "heads"));
            Directory.CreateDirectory(Path.Combine(_root, "equipment"));
            Directory.CreateDirectory(Path.Combine(_root, "players"));
            Directory.CreateDirectory(Path.Combine(_root, "layers"));
            Directory.CreateDirectory(Path.Combine(_root, "current"));
            LoadManifest();
            LoadFaceGroups();

            _mainTexId = Shader.PropertyToID("_MainTex");
            ClassInjector.RegisterTypeInIl2Cpp<PreviewAssetsRunner>();
            _runnerObject = new GameObject("CustomCampaignPreviewAssets");
            UnityEngine.Object.DontDestroyOnLoad(_runnerObject);
            _runnerObject.hideFlags = HideFlags.HideAndDontSave;
            _runnerObject.AddComponent<PreviewAssetsRunner>();
            WriteStatus(_cacheWasStale ? "building" : "waiting_data");
            Plugin.Log.LogInfo($"[PreviewAssets] Exporter v{ExporterVersion} installed at '{_root}'");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[PreviewAssets] Could not install preview runner: {ex}");
        }
    }

    internal static void Tick()
    {
        if (!_installed || string.IsNullOrEmpty(_root)) return;

        // An active render gets one inexpensive frame countdown per Update.
        if (_activeJob != null)
        {
            if (--_framesRemaining <= 0) FinishRender();
            TouchStatus("building");
            return;
        }

        float now = Time.unscaledTime;
        if (now < _nextWorkTime) return;
        _nextWorkTime = now + 0.05f;

        if (now >= _nextRequestScan)
        {
            _nextRequestScan = now + 0.20f;
            ReadNewestExactRequest();
        }

        // Head sprites load a few seconds after the title screen, and a second
        // SkaterUIHeadData can show up later, so keep rescanning — but slowly.
        if (!_headsComplete && now >= _nextHeadScan)
        {
            _nextHeadScan = now + 5f;
            TryExportHeads();
        }

        if (!_renderDisabled && LogRepositories.GuiListsDumped &&
            (!_inventoryBuilt || now >= _nextInventoryScan))
        {
            _nextInventoryScan = now + 15f;
            BuildEquipmentInventory();
            _inventoryBuilt = true;
        }

        // While no renderer exists there is nothing to do but wait, and both the
        // stage search and the live search cost a Resources scan — do not spin.
        if (!_renderDisabled && now >= _nextRenderAttempt)
        {
            RenderJob next = null;
            if (_pendingExact != null)
            {
                string exactId = _pendingExact.Id;
                next = PrepareExactJob(_pendingExact);
                // Always drop it from pending. A failed request that stays pending
                // is re-prepared every tick, and preparing one runs the whole team
                // config apply — that is how a single request ran 174 times.
                _pendingExact = null;
                if (next == null) NoteExactFailure(exactId);
            }
            if (next == null && EquipmentQueue.Count > 0)
                next = EquipmentQueue.Dequeue();

            if (next != null)
            {
                if (BeginRender(next, out bool waitingForRenderer)) return;
                if (waitingForRenderer) _nextRenderAttempt = now + 1f;
                if (!next.IsExact)
                    Requeue(next, waitingForRenderer);
                else
                {
                    CleanupOwned(next);
                    if (!waitingForRenderer) NoteExactFailure(next.RequestId);
                }
            }
        }

        string state;
        if (_renderDisabled) state = "ready"; // faces still work; stop saying "building"
        else if (!LogRepositories.GuiListsDumped) state = "waiting_data";
        else if (EquipmentQueue.Count > 0 || _pendingExact != null) state = "waiting_renderer";
        else state = "ready";
        TouchStatus(state);

        if (state == "ready" && !_readyLogged)
        {
            _readyLogged = true;
            string failed = _equipmentFailed > 0 ? $", {_equipmentFailed} could not be rendered" : "";
            string distinct = AcceptedSignatures.Count > 0
                ? $", {AcceptedSignatures.Count} distinct images rendered this launch" : "";
            Plugin.Log.LogInfo($"[PreviewAssets] Ready: {Entries.Count} cached values{distinct}{failed}");

            // Layers are the offline-recolouring half. Report them separately —
            // "recolourable" is the number that says the masks are real.
            int layerTotal = _layerMaskedPieces + _layerFlatPieces + _layerEmptyPieces;
            if (layerTotal > 0)
            {
                string verdict = "";
                if (_layerMaskedPieces == 0)
                    verdict = " — NO piece produced a mask, so offline recolouring will NOT work";
                else if (_layerEmptyPieces > layerTotal / 2)
                    verdict = " — most pieces came out empty, so isolation is hiding too much";
                Plugin.Log.LogInfo(
                    $"[PreviewAssets] Layers: {layerTotal} pieces isolated, {_layerMaskedPieces} recolourable, " +
                    $"{_layerFlatPieces} with no colour keys, {_layerEmptyPieces} empty{verdict}");
            }

            if (FaceHelmetPixels.Count > 0)
            {
                if (_facesFileDirty) { _facesFileDirty = false; WriteFacesFile(); }
                // Counted from the measurements, not from a running total: the
                // running total only sees this launch's renders, and a full cache
                // renders nothing at all.
                int helmeted = 0;
                foreach (var measured in FaceHelmetPixels)
                    if (measured.Value > MaxFaceKeyPixels) helmeted++;
                Plugin.Log.LogInfo(
                    $"[PreviewAssets] Faces: {FaceHelmetPixels.Count} measured of {SpineFaces.Count} — " +
                    $"{FaceHelmetPixels.Count - helmeted} helmetless, {helmeted} wearing a helmet. " +
                    "Both groups are exported into one folder, '_preview_assets/faces'; " +
                    "which is which is recorded in '_game_faces.txt' and drives the " +
                    "Creator's dropdown order.");
            }
        }
    }

    /// <summary>
    /// Stop retrying an exact request that will not render. Without this the
    /// Creator's request file is picked up again every 200 ms forever, and each
    /// pickup re-runs the full team config apply — 174 repeats in one session.
    /// </summary>
    private static void NoteExactFailure(string requestId)
    {
        if (string.IsNullOrEmpty(requestId)) return;
        if (requestId == _exactAttemptId) _exactAttempts++;
        else { _exactAttemptId = requestId; _exactAttempts = 1; }
        if (_exactAttempts < MaxAttempts) return;
        // Mark it completed so ReadNewestExactRequest stops offering it. The
        // Creator simply keeps showing its cached preview, which is correct.
        _lastCompletedRequestId = requestId;
        Plugin.Log.LogWarning(
            $"[PreviewAssets] Giving up on the exact preview request after {_exactAttempts} attempts");
    }

    /// <summary>Put a failed job back, or give up on it after MaxAttempts.</summary>
    private static void Requeue(RenderJob job, bool waitingForRenderer)
    {
        // Waiting for a renderer is not the job's fault — it must not burn a try.
        if (waitingForRenderer || ++job.Attempts < MaxAttempts)
        {
            EquipmentQueue.Enqueue(job);
            return;
        }
        QueuedKeys.Remove(MakeKey(job.Role, job.Field, job.Value));
        _equipmentFailed++;
        Plugin.Log.LogWarning(
            $"[PreviewAssets] Giving up on field='{job.Field}' value='{job.Value}' after {job.Attempts} attempts");
    }

    // ==========================================================
    //  Faces — direct sprite export, no renderer needed
    // ==========================================================

    private static void TryExportHeads()
    {
        try
        {
            var sources = Resources.FindObjectsOfTypeAll<SkaterUIHeadData>();
            if (sources == null || sources.Length == 0) return;

            // GetAllHeads reads a STATIC dictionary, so every loaded instance
            // returns the same list. Count each head once or the totals lie.
            var seen = new HashSet<string>(StringComparer.Ordinal);
            int written = 0, cached = 0, unresolved = 0;
            foreach (var source in sources)
            {
                if (source == null) continue;
                foreach (bool goalie in new[] { false, true })
                {
                    var names = new Il2CppSystem.Collections.Generic.List<string>();
                    try { source.GetAllHeads(names, goalie); }
                    catch { continue; }
                    for (int i = 0; i < names.Count; i++)
                    {
                        string value = names[i];
                        if (string.IsNullOrWhiteSpace(value)) continue;
                        string role = goalie ? "goalie" : "skater";
                        string field = goalie ? "Goalie Face" : "Face";
                        string key = MakeKey(role, field, value);
                        if (!seen.Add(key)) continue;
                        if (HasUsableEntry(key)) { cached++; continue; }

                        Sprite sprite = null;
                        try { sprite = goalie ? source.GetGoalieHead(value) : source.GetHead(value); }
                        catch { }
                        if (sprite == null) { unresolved++; continue; }
                        try
                        {
                            byte[] png = LogRepositories.SpriteToPng(sprite);
                            if (png == null || png.Length == 0) { unresolved++; continue; }
                            string relative = AssetRelativePath("head", role, field, value);
                            AtomicWriteBytes(FullPath(relative), png);
                            AddEntry("head", role, field, value, relative);
                            written++;
                        }
                        catch (Exception ex)
                        {
                            unresolved++;
                            Plugin.Log.LogDebug($"[PreviewAssets] Head '{value}' failed: {ex.Message}");
                        }
                    }
                }
            }

            int available = seen.Count;
            if (available == 0) return;
            _headsAvailable = available;
            _headsWritten += written;
            _headsCached = cached;
            if (written > 0) WriteManifest();

            // Only log when the picture changed; this runs on a repeating scan.
            if (written > 0 || !_headsComplete)
                Plugin.Log.LogInfo(
                    $"[PreviewAssets] Heads: {available} available, {written} written, {cached} cached" +
                    (unresolved > 0 ? $", {unresolved} with no sprite" : ""));

            // Everything that can resolve has resolved — stop rescanning.
            if (written == 0 && cached + unresolved == available) _headsComplete = true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[PreviewAssets] Head export failed: {ex.Message}");
        }
    }

    // ==========================================================
    //  Equipment — one canonical render per distinct field value
    // ==========================================================

    private static bool IsSentinel(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return true;
        foreach (string sentinel in SentinelValues)
            if (string.Equals(value, sentinel, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static readonly string[] SkaterFields =
    {
        "Body", "Body Away", "Bicep", "Bicep Away", "Gloves", "Gloves Away",
        "Pants", "Pants Away", "Skates", "Skates Away", "Stick",
        "Helmet", "Helmet Away", "Glasses", "Face"
    };

    private static readonly string[] GoalieFields =
    {
        "Helmet Skin", "Skin", "Skin Away", "Glove Skin", "Glove Away",
        "Blocker Skin", "Blocker Away", "Pads Skin", "Pads Away",
        "Stick Skin", "Stick Away", "Logo Skin", "Face"
    };

    /// <summary>Away fields must be rendered on the away kit to be visible.</summary>
    private static bool IsHomeField(string field)
    {
        return field.IndexOf("Away", StringComparison.OrdinalIgnoreCase) < 0;
    }

    private static string ReadSkaterField(ForwardData p, string field)
    {
        switch (field)
        {
            case "Body": return p.bodySkin;
            case "Body Away": return p.bodyAwaySkin;
            case "Bicep": return p.bicepSkin;
            case "Bicep Away": return p.bicepAwaySkin;
            case "Gloves": return p.gloveSkin;
            case "Gloves Away": return p.gloveAwaySkin;
            case "Pants": return p.pantsSkin;
            case "Pants Away": return p.pantsAwaySkin;
            case "Skates": return p.skateSkin;
            case "Skates Away": return p.skateAwaySkin;
            case "Stick": return p.stickSkin;
            case "Helmet": return p.helmetSkin;
            case "Helmet Away": return p.helmetAwaySkin;
            case "Glasses": return p.glassesSkin;
            // The face is a head SKIN, not a field named after it. Without this
            // the Face jobs wrote nothing and every face rendered as the donor's.
            case "Face": return p.headSkin;
            default: return null;
        }
    }

    private static void WriteSkaterField(ForwardData p, string field, string value)
    {
        switch (field)
        {
            case "Body": p.bodySkin = value; break;
            case "Body Away": p.bodyAwaySkin = value; break;
            case "Bicep": p.bicepSkin = value; break;
            case "Bicep Away": p.bicepAwaySkin = value; break;
            case "Gloves": p.gloveSkin = value; break;
            case "Gloves Away": p.gloveAwaySkin = value; break;
            case "Pants": p.pantsSkin = value; break;
            case "Pants Away": p.pantsAwaySkin = value; break;
            case "Skates": p.skateSkin = value; break;
            case "Skates Away": p.skateAwaySkin = value; break;
            case "Stick": p.stickSkin = value; break;
            case "Helmet": p.helmetSkin = value; break;
            case "Helmet Away": p.helmetAwaySkin = value; break;
            case "Glasses": p.glassesSkin = value; break;
            case "Face": p.headSkin = value; break;
        }
    }

    private static string ReadGoalieField(GoaltenderData g, string field)
    {
        switch (field)
        {
            case "Helmet Skin": return g.helmetSkin;
            case "Skin": return g.skin;
            case "Skin Away": return g.awaySkin;
            case "Glove Skin": return g.gloveSkin;
            case "Glove Away": return g.awayGloveSkin;
            case "Blocker Skin": return g.blockerSkin;
            case "Blocker Away": return g.awayBlockerSkin;
            case "Pads Skin": return g.padsSkin;
            case "Pads Away": return g.awayPadsSkin;
            case "Stick Skin": return g.stickSkin;
            case "Stick Away": return g.awayStickSkin;
            case "Logo Skin": return g.logoSkin;
            case "Face": return g.headSkin;
            default: return null;
        }
    }

    private static void WriteGoalieField(GoaltenderData g, string field, string value)
    {
        switch (field)
        {
            case "Helmet Skin": g.helmetSkin = value; break;
            case "Skin": g.skin = value; break;
            case "Skin Away": g.awaySkin = value; break;
            case "Glove Skin": g.gloveSkin = value; break;
            case "Glove Away": g.awayGloveSkin = value; break;
            case "Blocker Skin": g.blockerSkin = value; break;
            case "Blocker Away": g.awayBlockerSkin = value; break;
            case "Pads Skin": g.padsSkin = value; break;
            case "Pads Away": g.awayPadsSkin = value; break;
            case "Stick Skin": g.stickSkin = value; break;
            case "Stick Away": g.awayStickSkin = value; break;
            case "Logo Skin": g.logoSkin = value; break;
            case "Face": g.headSkin = value; break;
        }
    }

    private static string ReadField(SkaterData player, string role, string field)
    {
        try
        {
            if (role == "goalie") return ReadGoalieField(player.TryCast<GoaltenderData>(), field);
            return ReadSkaterField(player.TryCast<ForwardData>(), field);
        }
        catch { return null; }
    }

    private static void WriteField(SkaterData player, string role, string field, string value)
    {
        try
        {
            if (role == "goalie") WriteGoalieField(player.TryCast<GoaltenderData>(), field, value);
            else WriteSkaterField(player.TryCast<ForwardData>(), field, value);
        }
        catch { }
    }

    /// <summary>
    /// Clone one forward and one goalie once, and vary fields on the clones.
    /// Mutating a live ForwardData would leak a half-applied skin into the game
    /// if the process ever exits mid-render.
    /// </summary>
    private static bool EnsureDonors()
    {
        if (_donorTeam != null && _donorForward != null) return true;
        try
        {
            var teams = Resources.FindObjectsOfTypeAll<TeamData>();
            if (teams == null || teams.Length == 0) return false;
            TeamData source = null;
            foreach (var team in teams)
            {
                if (team == null) continue;
                try
                {
                    if (team.forwards == null || team.forwards.Count == 0) continue;
                    if (team.forwards[0] == null) continue;
                    source = team;
                    if (team.goalie != null) break; // prefer a team that has both
                }
                catch { }
            }
            if (source == null) return false;

            var clone = UnityEngine.Object.Instantiate(source);
            PatchChooseMetaUI.DeepCloneForwards(clone);
            DetachColors(clone);
            _donorTeam = clone;
            _donorForward = clone.forwards != null && clone.forwards.Count > 0 ? clone.forwards[0] : null;
            _donorGoalie = clone.goalie;
            if (_donorForward == null) { _donorTeam = null; return false; }
            Plugin.Log.LogInfo(
                $"[PreviewAssets] Donor skater cloned from '{source.teamName}'" +
                (_donorGoalie == null ? " (no goalie — goalie previews unavailable)" : ""));
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[PreviewAssets] Could not clone a donor skater: {ex.Message}");
            return false;
        }
    }

    private static bool _facesDumped;
    private static readonly List<string> SpineFaces = new();

    /// <summary>
    /// Every face the skeleton can wear, read straight from its Spine skin list
    /// rather than from whichever players happen to be loaded.
    ///
    /// SAFETY: this reads skin NAMES only. `SkeletonData.Skins` is an
    /// ExposedList of `Skin`, which is a CLASS, and `Skin.Name` is a string —
    /// both are reference types and safe to walk. `Skin.Attachments` is NOT: it
    /// is an ICollection of non-blittable `SkinEntry` STRUCTS, and reading the
    /// boxed entries through IL2CPP reflection hard-crashes the process
    /// (0xC0000005, session 12 §3) without throwing, so try/catch cannot save
    /// you. Nothing here touches it, and nothing added here ever should.
    /// </summary>
    private static void DumpSpineFaces()
    {
        if (_facesDumped) return;
        try
        {
            var graphics = Resources.FindObjectsOfTypeAll<SkeletonGraphic>();
            if (graphics == null) return;

            var found = new HashSet<string>(StringComparer.Ordinal);
            foreach (var graphic in graphics)
            {
                if (graphic == null) continue;
                Spine.ExposedList<Spine.Skin> skins;
                try { skins = graphic.skeleton?.Data?.Skins; } catch { continue; }
                if (skins == null) continue;
                for (int i = 0; i < skins.Count; i++)
                {
                    string name;
                    try { name = skins.Items[i]?.Name; } catch { continue; }
                    if (!string.IsNullOrEmpty(name) &&
                        name.StartsWith("Faces/", StringComparison.OrdinalIgnoreCase))
                        found.Add(name);
                }
            }
            if (found.Count == 0) return;

            _facesDumped = true;

            // A version bump only invalidates the manifest; layer and equipment
            // files are keyed by File.Exists, so without this a stale helmeted
            // face PNG survives every change to how faces are rendered.
            if (_purgeFacesPending) { _purgeFacesPending = false; PurgeRenderedArt(); }

            // ALL of them. An earlier version collapsed `Angus_Bald_Helmet` onto
            // `Angus_Bald` as a duplicate — that was wrong, and it is the reason
            // this took so long. The skins ship in PAIRS: one drawn wearing a
            // helmet and one bare. Collapsing them threw away the bare half
            // before it was ever rendered, so every surviving face came out with
            // a helmet on it and there was nothing left to compare against.
            // Both halves are exported and each is MEASURED, so the list can be
            // split into two groups (see WriteFacesFile).
            var sorted = new List<string>(found);
            sorted.Sort(StringComparer.OrdinalIgnoreCase);
            SpineFaces.Clear();
            SpineFaces.AddRange(sorted);
            WriteFacesFile();
            Plugin.Log.LogInfo(
                $"[PreviewAssets] Dumped {sorted.Count} face skins from Spine to '_game_faces.txt'");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[PreviewAssets] Face dump failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Write the face list the Creator's dropdown reads, in TWO GROUPS.
    ///
    /// Every face is listed. The skins ship in pairs — one drawn wearing a
    /// helmet and one bare — and both are useful, so the file says which is
    /// which rather than hiding either. A face is helmeted when its isolated
    /// render still contains pure colour-key pixels: the capture draws with a
    /// replacement UI/Default material that does not run the palette remap, so
    /// a helmet lands as raw key colour (a bright red one) while real head art
    /// does not. Measured on a full export: bare heads came in at 0-59 key
    /// pixels and helmeted ones started at 346.
    ///
    /// Rewritten as the measurements come in, so the grouping tracks the render.
    /// </summary>
    private static void WriteFacesFile()
    {
        var helmeted = new List<KeyValuePair<string, int>>();
        foreach (var measured in FaceHelmetPixels)
            if (measured.Value > MaxFaceKeyPixels) helmeted.Add(measured);
        helmeted.Sort((a, b) => b.Value.CompareTo(a.Value));

        var sb = new StringBuilder();
        sb.AppendLine("# Every face skin the skeletons carry, read from Spine itself.");
        sb.AppendLine("# Generated automatically — do not hand-edit; overwritten on launch.");
        sb.Append("# ").Append(SpineFaces.Count).Append(" faces; ")
          .Append(helmeted.Count).Append(" of them render wearing a helmet (listed below as")
          .AppendLine(" #helmeted, most helmet first).");
        foreach (string face in SpineFaces) sb.AppendLine(face);
        foreach (var measured in helmeted)
            sb.Append("#helmeted\t").Append(measured.Key).Append('\t')
              .Append(measured.Value).AppendLine();
        AtomicWriteText(Path.Combine(Plugin.ModContentRoot, "_game_faces.txt"), sb.ToString());
    }

    /// <summary>
    /// Read back the per-face helmet measurements a previous launch made.
    ///
    /// The grouping hangs off the render path, and with a full cache nothing
    /// renders — so without this a second launch would forget which faces wear a
    /// helmet and write the file back as one undifferentiated list.
    ///
    /// Skipped when the manifest is stale: a new exporter version measures
    /// everything again rather than inheriting the last one's verdicts.
    /// </summary>
    private static void LoadFaceGroups()
    {
        if (_purgeFacesPending) return;
        try
        {
            string path = Path.Combine(Plugin.ModContentRoot, "_game_faces.txt");
            if (!File.Exists(path)) return;
            foreach (string line in File.ReadAllLines(path))
            {
                if (!line.StartsWith("#helmeted\t", StringComparison.Ordinal)) continue;
                string[] parts = line.Split('\t');
                if (parts.Length < 3 || string.IsNullOrEmpty(parts[1])) continue;
                if (int.TryParse(parts[2], out int pixels)) FaceHelmetPixels[parts[1]] = pixels;
            }
            if (FaceHelmetPixels.Count > 0)
                Plugin.Log.LogInfo(
                    $"[PreviewAssets] {FaceHelmetPixels.Count} faces were measured as helmeted by an " +
                    "earlier launch.");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[PreviewAssets] Could not read face groups: {ex.Message}");
        }
    }

    // ----------------------------------------------------------
    //  Render a face with no helmet on it
    // ----------------------------------------------------------
    //
    // This mirrors Plugin.HandleNoHelmetSentinel, which is the code that makes
    // `Helmet = none` work on a real player in a real match. Copy it, do not
    // reinvent it: an earlier attempt here registered the head in
    // HeadsWithoutHelmets and stopped there, and every face still came out
    // wearing the donor's helmet. Blanking helmetSkin is the half that removes
    // it. Both are needed — the array stops the game re-deriving a helmet from
    // the head, the blank field stops the one already assigned from drawing.
    //
    // Scoped rather than permanent. RegisterFaceAsHelmetless appends for the
    // rest of the session, which is right for one configured player and would
    // leave every skater bare-headed if it ran across 232 faces, so the original
    // array is put back in FinishRender's finally.
    private static Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStringArray _restoreHeads;
    private static SkaterData _helmetOwner;
    private static string _restoreHelmet;
    private static string _restoreHelmetAway;
    private static bool _helmetlessActive;
    private static bool _helmetlessWarned;

    private static System.Reflection.PropertyInfo HeadsWithoutHelmetsProperty()
    {
        // Reflection, matching Plugin.RegisterFaceAsHelmetless: Il2CppInterop
        // exposes the static readonly field as a property.
        return typeof(ForwardDataExtensions).GetProperty(
            "HeadsWithoutHelmets",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
    }

    private static void BeginHelmetlessFace(RenderJob job)
    {
        if (_helmetlessActive || job?.Player == null || string.IsNullOrEmpty(job.Value)) return;
        _helmetlessActive = true;

        // 1. The head joins HeadsWithoutHelmets.
        try
        {
            var property = HeadsWithoutHelmetsProperty();
            var current = property?.GetValue(null)
                as Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStringArray;
            // Never install a replacement with nothing real to restore.
            if (current != null)
            {
                bool already = false;
                for (int i = 0; i < current.Length; i++)
                    if (string.Equals(current[i], job.Value, StringComparison.OrdinalIgnoreCase))
                    { already = true; break; }
                if (!already)
                {
                    var replacement = new Il2CppInterop.Runtime.InteropTypes.Arrays
                        .Il2CppStringArray(current.Length + 1);
                    for (int i = 0; i < current.Length; i++) replacement[i] = current[i];
                    replacement[current.Length] = job.Value;
                    property.SetValue(null, replacement);
                    _restoreHeads = current;
                }
            }
            else if (!_helmetlessWarned)
            {
                _helmetlessWarned = true;
                Plugin.Log.LogWarning("[PreviewAssets] HeadsWithoutHelmets is unreadable.");
            }
        }
        catch (Exception ex)
        {
            if (!_helmetlessWarned)
            {
                _helmetlessWarned = true;
                Plugin.Log.LogWarning($"[PreviewAssets] HeadsWithoutHelmets: {ex.Message}");
            }
        }

        // 2. The helmet skin is blanked. This is the part that was missing.
        try
        {
            if (job.Role == "goalie")
            {
                var goalie = job.Player.TryCast<GoaltenderData>();
                if (goalie != null)
                {
                    _helmetOwner = job.Player;
                    _restoreHelmet = goalie.helmetSkin;
                    goalie.helmetSkin = "";
                }
            }
            else
            {
                var forward = job.Player.TryCast<ForwardData>();
                if (forward != null)
                {
                    _helmetOwner = job.Player;
                    _restoreHelmet = forward.helmetSkin;
                    _restoreHelmetAway = forward.helmetAwaySkin;
                    forward.helmetSkin = "";
                    forward.helmetAwaySkin = "";
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogDebug($"[PreviewAssets] Could not blank the helmet skin: {ex.Message}");
        }
    }

    private static void EndHelmetlessFace()
    {
        if (!_helmetlessActive) return;
        _helmetlessActive = false;
        try
        {
            var property = HeadsWithoutHelmetsProperty();
            if (property != null && _restoreHeads != null) property.SetValue(null, _restoreHeads);
        }
        catch { }
        _restoreHeads = null;

        try
        {
            if (_helmetOwner != null)
            {
                var goalie = _helmetOwner.TryCast<GoaltenderData>();
                if (goalie != null) goalie.helmetSkin = _restoreHelmet;
                else
                {
                    var forward = _helmetOwner.TryCast<ForwardData>();
                    if (forward != null)
                    {
                        forward.helmetSkin = _restoreHelmet;
                        forward.helmetAwaySkin = _restoreHelmetAway;
                    }
                }
            }
        }
        catch { }
        _helmetOwner = null;
        _restoreHelmet = null;
        _restoreHelmetAway = null;
    }

    /// <summary>
    /// Write a browsable copy of a head, under its own name, into ONE folder.
    ///
    /// The export is keyed by a SHA-256 prefix, which is right for the cache and
    /// useless for looking at. Every head is mirrored to
    /// <c>_preview_assets/faces/&lt;Name&gt;.png</c> — one flat folder, no
    /// subfolders and no grouping, so the whole set can be opened at once. Which
    /// heads wear a helmet is recorded in <c>_game_faces.txt</c> instead.
    /// </summary>
    private static void WriteReadableFace(string face, byte[] png)
    {
        try
        {
            string leaf = face;
            int slash = leaf.LastIndexOf('/');
            if (slash >= 0) leaf = leaf.Substring(slash + 1);

            var clean = new StringBuilder();
            foreach (char c in leaf)
                clean.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);
            string name = clean.ToString();

            // Two faces share a leaf name (Anyteam/Heavy_Helmet and
            // Helmets/Heavy_Helmet). Without this the second silently overwrites
            // the first and the folder is quietly one head short.
            if (NamedFaceFiles.TryGetValue(name, out string owner) &&
                !string.Equals(owner, face, StringComparison.Ordinal))
            {
                string group = slash >= 0 ? face.Substring(0, slash) : "";
                int parent = group.LastIndexOf('/');
                if (parent >= 0) group = group.Substring(parent + 1);
                name = group + "_" + name;
            }
            NamedFaceFiles[name] = face;

            AtomicWriteBytes(Path.Combine(_root, "faces", name + ".png"), png);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogDebug($"[PreviewAssets] Could not write a named copy of '{face}': {ex.Message}");
        }
    }

    private static readonly Dictionary<string, string> NamedFaceFiles =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Throw away every rendered image when the exporter version has moved on.
    ///
    /// This has to be a WHOLESALE wipe, not a targeted one. Layer and equipment
    /// files are keyed by <c>File.Exists</c> rather than by the manifest, so a
    /// version bump on its own leaves every one of them in place — and the whole
    /// point of a bump is that the old renders may be wrong. An earlier version
    /// purged only the faces and that is exactly how a blank goalie helmet layer
    /// (cached from a transient empty capture) survived two version bumps.
    ///
    /// Heads are left alone: they come from real Unity Sprites rather than the
    /// renderer, so no change to how the skater is drawn can invalidate them.
    /// </summary>
    private static void PurgeRenderedArt()
    {
        int removed = 0;
        foreach (string folder in new[] { "layers", "equipment", "faces" })
        {
            try
            {
                string path = Path.Combine(_root, folder);
                if (!Directory.Exists(path)) continue;
                removed += Directory.GetFiles(path, "*", SearchOption.AllDirectories).Length;
                Directory.Delete(path, true);
            }
            catch { }
        }
        Entries.Clear();
        FaceHelmetPixels.Clear();
        NamedFaceFiles.Clear();
        if (removed > 0)
            Plugin.Log.LogInfo(
                $"[PreviewAssets] Exporter version changed; cleared {removed} cached images so " +
                "everything re-renders.");
    }

    private static bool DeleteIfPresent(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;
            File.Delete(path);
            return true;
        }
        catch { return false; }
    }

    private static void BuildEquipmentInventory()
    {
        try
        {
            if (!EnsureDonors()) return;
            DumpSpineFaces();

            var allForwards = Resources.FindObjectsOfTypeAll<ForwardData>();
            var allGoalies = Resources.FindObjectsOfTypeAll<GoaltenderData>();

            // Distinct values per field, gathered from every loaded player.
            var values = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            void Collect(string role, string field, string value)
            {
                if (IsSentinel(value)) return;
                string bucket = role + "\0" + field;
                if (!values.TryGetValue(bucket, out var set))
                    values[bucket] = set = new HashSet<string>(StringComparer.Ordinal);
                set.Add(value);
            }

            if (allForwards != null)
                foreach (var p in allForwards)
                {
                    if (p == null) continue;
                    foreach (string field in SkaterFields)
                    {
                        try { Collect("skater", field, ReadSkaterField(p, field)); }
                        catch { }
                    }
                }

            if (allGoalies != null && _donorGoalie != null)
                foreach (var g in allGoalies)
                {
                    if (g == null) continue;
                    foreach (string field in GoalieFields)
                    {
                        try { Collect("goalie", field, ReadGoalieField(g, field)); }
                        catch { }
                    }
                }

            // Every face the skeleton can wear, not only the ones some loaded
            // player happens to use — the Creator's dropdown offers all of them,
            // so all of them need a layer.
            foreach (string face in SpineFaces) Collect("skater", "Face", face);

            int discovered = 0, missing = 0, layers = 0;
            foreach (var pair in values)
            {
                string[] parts = pair.Key.Split('\0');
                string role = parts[0], field = parts[1];
                var donor = role == "goalie" ? _donorGoalie : (SkaterData)_donorForward;
                foreach (string value in pair.Value)
                {
                    discovered++;
                    // Layers are independent of the flat preview and must be
                    // queued even when the flat one is already cached.
                    layers += QueueLayerPasses(role, field, value, donor, _donorTeam, IsHomeField(field));
                    // Socks and the number have their own colour scheme but no
                    // field of their own, so they are queued off the field that
                    // draws them and isolated to their own slots.
                    if (DerivedLayerFields.TryGetValue(field, out string derived))
                        layers += QueueLayerPasses(role, derived, value, donor, _donorTeam,
                                                   IsHomeField(field), field);
                    string key = MakeKey(role, field, value);
                    if (HasUsableEntry(key) || QueuedKeys.Contains(key)) continue;
                    EquipmentQueue.Enqueue(new RenderJob
                    {
                        Kind = "equipment",
                        Role = role,
                        Field = field,
                        Value = value,
                        RelativePath = AssetRelativePath("equipment", role, field, value),
                        Player = role == "goalie" ? _donorGoalie : (SkaterData)_donorForward,
                        Team = _donorTeam,
                        IsHome = IsHomeField(field)
                    });
                    QueuedKeys.Add(key);
                    missing++;
                }
            }

            _equipmentDiscovered = discovered;
            if (missing > 0 || layers > 0) _readyLogged = false;
            Plugin.Log.LogInfo(
                $"[PreviewAssets] Equipment queue: {missing} missing ({discovered} distinct values)" +
                (layers > 0 ? $", {layers} colour-layer passes" : ""));
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[PreviewAssets] Equipment inventory failed: {ex.Message}");
        }
    }

    // ==========================================================
    //  Render queue
    // ==========================================================

    private static bool BeginRender(RenderJob job, out bool waitingForRenderer)
    {
        waitingForRenderer = false;
        bool goalie = job.Role == "goalie";

        // Re-resolve the donor at render time. A queued job can outlive the clone
        // it was built against, and a destroyed reference is not the job's fault.
        if (!job.IsExact)
        {
            if (!EnsureDonors()) { waitingForRenderer = true; return false; }
            job.Player = goalie ? _donorGoalie : (SkaterData)_donorForward;
            job.Team = _donorTeam;
            if (job.Player == null) { waitingForRenderer = true; return false; }
        }

        bool isStage;
        var preview = AcquirePreview(goalie, out isStage);
        if (preview == null)
        {
            waitingForRenderer = true;
            if (Time.unscaledTime >= _nextRendererWarning)
            {
                _nextRendererWarning = Time.unscaledTime + 15f;
                Plugin.Log.LogWarning(
                    "[PreviewAssets] No usable SkaterPreviewInUI is loaded yet; render queue is waiting " +
                    "(it will drain once a skater preview exists — the customization menu always has one).");
            }
            TouchStatus("waiting_renderer");
            return false;
        }

        try
        {
            _activeJob = job;
            _activePreview = preview;
            _activeIsStage = isStage;

            // Only a shared live preview needs restoring; the private stage is ours.
            _restorePlayer = isStage ? null : preview.CurrentSkater;
            _restoreTeam = _restorePlayer != null ? FindTeamForPlayer(_restorePlayer) : null;

            // Vary exactly one field on the donor clone, and remember the old value.
            _restoreField = null;
            _restoreFieldOwner = null;
            _restoreFieldValue = null;
            if (!job.IsExact && job.Player != null)
            {
                // A derived piece (Socks, Number) has no field of its own, so
                // the value is written to the field that actually draws it.
                string varied = job.SourceField ?? job.Field;
                _restoreFieldOwner = job.Player;
                _restoreField = varied;
                _restoreFieldValue = ReadField(job.Player, job.Role, varied);
                WriteField(job.Player, job.Role, varied, job.Value);

                // A face preview is the face, not the face under the donor's
                // helmet. Do EXACTLY what the in-game path does — see
                // Plugin.HandleNoHelmetSentinel, which is what makes
                // `Helmet = none` work on a real player. It is TWO things, and
                // an earlier attempt here did only the first and got nowhere:
                //   1. register the head in HeadsWithoutHelmets, and
                //   2. blank helmetSkin / helmetAwaySkin.
                if (IsFaceField(varied)) BeginHelmetlessFace(job);
            }

            preview.SetVisible(true);
            // RefreshSkater already carries isHome; RefreshSkinAndColors is what
            // re-reads the (just mutated) skin fields off the donor.
            preview.RefreshSkater(job.Player, job.Team, job.IsHome, null);
            preview.RefreshSkinAndColors();
            // Slot isolation is deliberately NOT applied here. Both of these
            // rebuild the skeleton asynchronously and would wipe it; it happens
            // in FinishRender instead, once the settle budget has elapsed.
            _framesRemaining = SettleFrames[Math.Min(job.Attempts, SettleFrames.Length - 1)];
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[PreviewAssets] Could not begin render field='{job.Field}' value='{job.Value}': {ex.Message}");
            EndHelmetlessFace();
            RestoreVariedField();
            _activeJob = null;
            _activePreview = null;
            CleanupOwned(job);
            return false;
        }
    }

    private static void FinishRender()
    {
        var job = _activeJob;
        bool retry = false;
        try
        {
            var skeleton = job.Role == "goalie" ? _activePreview?.GoalieSkeleton : _activePreview?.ForwardSkeleton;
            if (skeleton == null) throw new InvalidOperationException("preview skeleton is null");
            RebuildMesh(skeleton);

            // Slot/material reconnaissance is DONE and switched off — it answered
            // its question and there is no reason to keep writing those files.
            //
            // What it found: colours are applied by `Spine/T2T SkeletonGraphic`
            // through a 16x4 `_ColorScheme` palette texture, and every material
            // reports `_Customizable = 0`, so the art we export already has its
            // colours baked in. There is no per-region mask in the exported PNGs,
            // which means a piece cannot be recoloured offline — only the live
            // render can show a player's own colours on the model.
            if (DumpSlotAndMaterialInventory)
                DumpSlotInventory(skeleton, job.IsExact ? job.Role + "_player" : job.Role);

            // Measure the shared frame while every slot is still attached, and
            // before any isolation hides part of the body.
            if (!job.IsExact) EnsureRoleFrame(job.Role, skeleton);

            // The isolation proof needs the whole skater to compare against, so
            // the first layer job — and only the first — captures it twice.
            int wholePixels = 0;
            if (job.LayerChannel != null && !_layerProofDone)
            {
                var whole = CaptureSkeleton(skeleton);
                if (whole != null) wholePixels = whole.OpaquePixels;
            }

            // Hide the other slots HERE: after the async skin refresh has had its
            // settle budget, and immediately before the capture, so nothing gets
            // the chance to rebuild the skeleton and undo it.
            if (job.LayerChannel != null)
            {
                ApplyLayerIsolation(job, skeleton);
                RebuildMesh(skeleton);
            }

            // Every preview of a role shares one frame — an isolated glove lands
            // where the glove actually is, and no capture can zoom itself in.
            Capture capture;
            if (!job.IsExact && TryRoleFrame(job.Role, out var frameCenter, out float frameHalf))
                capture = CaptureSkeleton(skeleton, frameCenter, frameHalf);
            else
                capture = CaptureSkeleton(skeleton);
            bool nothingToDraw = capture == null || capture.Png == null || capture.Png.Length == 0;

            // A piece with no geometry of its own is a fact about the art, not a
            // render failure: the number is on the BACK of the jersey and never
            // appears in a front-facing pose, and the biceps are drawn by the
            // Body slot rather than separately. Measured — Number, Number Away,
            // Bicep and Bicep Away accounted for 156 of 160 retried failures.
            // Cache them as empty so they are not attempted three times each,
            // every launch. A systemic isolation break still surfaces, via the
            // "most pieces came out empty" warning on the Layers line.
            if (job.LayerChannel != null && nothingToDraw)
            {
                // Prove it is the ART before caching that verdict forever. An
                // empty capture can also be TRANSIENT: the skin refresh is an
                // async UniTask, so a job that blanked a field (a face render
                // blanks helmetSkin) can have its rebuild land inside the NEXT
                // job's settle window, and that job then captures a skeleton
                // with the piece still detached. Measured: the first goalie
                // 'Helmet Skin' after the goalie face job cached 0 px while the
                // other 15 rendered normally. One retry, on a longer settle
                // budget, separates "no geometry in this pose" from "not
                // attached yet" — Number and Bicep simply fail twice.
                if (job.Attempts == 0)
                {
                    job.Attempts++;
                    retry = true;
                    EquipmentQueue.Enqueue(job);
                    Plugin.Log.LogDebug(
                        $"[PreviewAssets] field='{job.Field}' value='{job.Value}' drew nothing; " +
                        "retrying on a longer settle before caching it as empty");
                    return;
                }

                var blank = new Color32[PreviewSize * PreviewSize];
                bool wrote = true;
                foreach (string channel in LayerChannels)
                    wrote &= WriteLayerPng(job, channel, blank);
                if (wrote)
                {
                    _layerEmptyPieces++;
                    QueuedKeys.Remove("layer\0" + MakeKey(job.Role, job.Field, job.Value));
                    Plugin.Log.LogDebug(
                        $"[PreviewAssets] field='{job.Field}' value='{job.Value}' draws nothing of its own; cached empty");
                }
                return;
            }

            // An isolated piece is allowed to be small, or even empty when this
            // pose has no attachment for it. Only a total absence of geometry is
            // a failure for a layer job — holding it to MinOpaquePixels would
            // retry legitimate small pieces and trip the circuit breaker.
            if (nothingToDraw || (job.LayerChannel == null && capture.OpaquePixels < MinOpaquePixels))
            {
                // Every way this fails is handled together on purpose. Treating
                // "no geometry collected" differently from "geometry drew nothing"
                // meant the stage was never retired and 437 jobs in a row used a
                // renderer that could not produce anything.
                LogCaptureDiagnostics(skeleton, nothingToDraw);
                NoteEmptyCapture();
                RetireStage();
                throw new InvalidOperationException(nothingToDraw
                    ? "renderer had no geometry to draw"
                    : "captured frame was empty");
            }

            // The skin swap is async. An image identical to the previous one means
            // it had not landed yet, so spend another (longer) attempt on it.
            // Layer passes are deliberately near-identical to each other (one
            // channel differs), so the "skin has not landed yet" retry must not
            // apply to them.
            if (!job.IsExact && job.LayerChannel == null &&
                capture.Signature == _lastAcceptedSignature && job.Attempts + 1 < MaxAttempts)
            {
                job.Attempts++;
                retry = true;
                EquipmentQueue.Enqueue(job);
                Plugin.Log.LogDebug(
                    $"[PreviewAssets] field='{job.Field}' value='{job.Value}' matched the previous frame; waiting longer");
                return;
            }

            // A layer job writes its own four files from the pixels, not this one.
            if (job.LayerChannel == null) AtomicWriteBytes(FullPath(job.RelativePath), capture.Png);

            if (job.IsExact)
            {
                string currentRelative = "current/preview.png";
                AtomicWriteBytes(FullPath(currentRelative), capture.Png);
                AtomicWriteText(_responsePath,
                    $"version\t{ExporterVersion}\nrequest_id\t{job.RequestId}\nfile\t{currentRelative}\n");
                _lastCompletedRequestId = job.RequestId;
                Plugin.Log.LogInfo("[PreviewAssets] Exact preview request completed");
            }
            else if (job.LayerChannel != null)
            {
                // Prove isolation reached the frame before writing anything. If
                // it did not, the files would be four copies of a whole skater.
                if (wholePixels > 0) CheckLayerProof(job, wholePixels, capture.OpaquePixels);
                if (_layersDisabled) return;

                // EVERY head is exported, helmet or not, and each one is measured
                // so the list can be split into two groups. The skins ship in
                // pairs — one drawn wearing a helmet, one bare — so the answer is
                // to offer both and say which is which, not to hide either.
                // A filter that dropped the helmeted ones was built and removed:
                // it took 132 faces down to 25.
                if (IsFaceField(job.Field))
                {
                    int keyPixels = CountKeyPixels(capture.Pixels);
                    bool helmeted = keyPixels > MaxFaceKeyPixels;
                    FaceHelmetPixels[job.Value] = keyPixels;
                    _facesFileDirty = true;
                    if (helmeted) _facesWithHelmet++;
                    WriteReadableFace(job.Value, capture.Png);
                }

                if (SplitLayers(capture.Pixels, job, out int masked))
                {
                    // An empty capture is legal for a piece this pose has no
                    // attachment for, but a run made almost entirely of them
                    // means isolation is hiding the piece as well as everything
                    // else — the one way this can fail that still satisfies the
                    // proof gate, since 0 pixels passes "less than the skater".
                    if (capture.OpaquePixels == 0) _layerEmptyPieces++;
                    else if (masked > 0) _layerMaskedPieces++;
                    else _layerFlatPieces++;
                    QueuedKeys.Remove("layer\0" + MakeKey(job.Role, job.Field, job.Value));
                    Plugin.Log.LogDebug(
                        $"[PreviewAssets] Layers for field='{job.Field}' value='{job.Value}' " +
                        $"({capture.OpaquePixels} px, {masked} recolourable)");
                }
            }
            else
            {
                NoteAcceptedImage(capture.Signature);
                AddEntry(job.Kind, job.Role, job.Field, job.Value, job.RelativePath);
                QueuedKeys.Remove(MakeKey(job.Role, job.Field, job.Value));
                // NOT written back here. Overwriting the frame with whatever this
                // capture happened to measure is what let a half-applied skin
                // re-zoom the camera for every later preview; EnsureRoleFrame
                // owns it and only ever widens it.
                WriteManifest();
                Plugin.Log.LogInfo($"[PreviewAssets] Rendered field='{job.Field}' value='{job.Value}'");
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[PreviewAssets] Render failed field='{job?.Field}' value='{job?.Value}': {ex.Message}");
            if (job != null && !job.IsExact) { Requeue(job, false); retry = true; }
            else if (job != null) NoteExactFailure(job.RequestId);
        }
        finally
        {
            // Slot alphas and the borrowed colour scheme must go back even when
            // the pass threw, or every later render inherits a half-hidden skater.
            if (job != null && job.LayerChannel != null)
                EndLayerPass(job.Role == "goalie"
                    ? _activePreview?.GoalieSkeleton : _activePreview?.ForwardSkeleton);
            EndHelmetlessFace();
            RestoreVariedField();
            RestorePreview();
            if (!retry) CleanupOwned(job);
            _activeJob = null;
            _activePreview = null;
            _activeIsStage = false;
            _restorePlayer = null;
            _restoreTeam = null;
        }
    }

    /// <summary>
    /// Track how varied the exported images actually are. If every render comes
    /// back the same the export is worthless, and that is precisely the failure
    /// the previous exporter hid — so say so in the log rather than cache it
    /// quietly. The images are still written: identical art is legal, a silent
    /// wall of identical art is not.
    /// </summary>
    /// <summary>
    /// Circuit breaker. If nothing has ever rendered and captures keep coming back
    /// empty, the renderer is not going to start working this session — retrying
    /// 165 jobs three times each just fills the log with 495 warnings and burns
    /// GPU time. Stop, once, loudly. Cached faces are unaffected.
    /// </summary>
    /// <summary>
    /// Retire the private stage the first time it fails to produce anything, and
    /// go back to the live menu preview, which is known to carry real geometry.
    /// </summary>
    private static void RetireStage()
    {
        if (!_activeIsStage || _stageBroken) return;
        _stageBroken = true;
        Plugin.Log.LogWarning(
            "[PreviewAssets] Private preview stage produced nothing; falling back to the live menu preview.");
        try { if (_stageRoot != null) UnityEngine.Object.Destroy(_stageRoot); } catch { }
        _stageRoot = null;
        _stagePreview = null;
        // The stage failing says nothing about the live preview, so let the queue
        // start over with a clean slate rather than counting its attempts against it.
        _consecutiveEmpty = 0;
    }

    private static void NoteEmptyCapture()
    {
        _consecutiveEmpty++;
        if (_renderDisabled || AcceptedSignatures.Count > 0) return;
        if (_consecutiveEmpty < GiveUpAfterConsecutiveEmpty) return;

        _renderDisabled = true;
        int abandoned = EquipmentQueue.Count;
        EquipmentQueue.Clear();
        QueuedKeys.Clear();
        _readyLogged = false;
        Plugin.Log.LogWarning(
            $"[PreviewAssets] {_consecutiveEmpty} captures in a row came back empty and none ever " +
            $"succeeded — equipment rendering is disabled for this session ({abandoned} queued " +
            "previews dropped). Face previews are unaffected. See the DIAG lines above.");
    }

    private static void NoteAcceptedImage(ulong signature)
    {
        _consecutiveEmpty = 0;
        _identicalRun = signature == _lastAcceptedSignature ? _identicalRun + 1 : 0;
        _lastAcceptedSignature = signature;
        AcceptedSignatures.Add(signature);
        if (_identicalRun >= 8 && !_identicalWarned)
        {
            _identicalWarned = true;
            Plugin.Log.LogWarning(
                $"[PreviewAssets] {_identicalRun + 1} consecutive renders were pixel-identical — the skin swap is " +
                "probably not reaching the preview. Equipment previews will all look the same.");
        }
    }

    private static void RestoreVariedField()
    {
        if (_restoreFieldOwner == null || _restoreField == null) return;
        try
        {
            string role = _restoreFieldOwner.TryCast<GoaltenderData>() != null ? "goalie" : "skater";
            WriteField(_restoreFieldOwner, role, _restoreField, _restoreFieldValue);
        }
        catch { }
        _restoreFieldOwner = null;
        _restoreField = null;
        _restoreFieldValue = null;
    }

    // ==========================================================
    //  Preview acquisition — private stage first, live menu second
    // ==========================================================

    /// <summary>
    /// The private off-screen stage is OFF because it was measured not to work.
    /// An instantiated SkaterPreviewInUI never builds its canvas renderers outside
    /// the menu that owns it — `canvasRenderers=0 multiMeshes=0 currentMeshVerts=4`
    /// — so it collects no geometry no matter where it is parked or what
    /// UpdateMode it is forced into. The live menu preview drains the whole queue
    /// in one visit anyway (165 values, 0 left over). Kept behind this flag rather
    /// than deleted so the next person does not re-derive it.
    /// </summary>
    // static readonly, not const: a const folds away and the compiler then flags
    // the disabled branch as unreachable code.
    private static readonly bool UseOffScreenStage = false;

    private static SkaterPreviewInUI AcquirePreview(bool goalie, out bool isStage)
    {
        isStage = false;
        if (UseOffScreenStage && !_stageBroken)
        {
            var stage = EnsureStage();
            if (stage != null)
            {
                bool hasSkeleton = false;
                try { hasSkeleton = goalie ? stage.GoalieSkeleton != null : stage.ForwardSkeleton != null; }
                catch { }
                if (hasSkeleton) { isStage = true; return stage; }
            }
        }
        return FindLivePreview(goalie);
    }

    /// <summary>
    /// Build a private, off-screen copy of the game's skater preview. It renders
    /// through the game's own uGUI/Spine path but is parked far outside the
    /// viewport, so the export never disturbs what the player is looking at and
    /// can run before the customization menu is ever opened.
    /// </summary>
    private static SkaterPreviewInUI EnsureStage()
    {
        if (_stagePreview != null && _stageRoot != null) return _stagePreview;
        if (_stageBroken) return null;
        // No template loaded yet is the normal early-boot state; retry slowly.
        if (Time.unscaledTime < _nextStageAttempt) return null;
        _nextStageAttempt = Time.unscaledTime + 2f;
        try
        {
            SkaterPreviewInUI template = null;
            var previews = Resources.FindObjectsOfTypeAll<SkaterPreviewInUI>();
            if (previews != null)
                foreach (var candidate in previews)
                {
                    if (candidate == null || candidate.gameObject == null) continue;
                    if (candidate == _stagePreview) continue;
                    bool inScene = false;
                    try { inScene = candidate.gameObject.scene.IsValid(); } catch { }
                    // A prefab asset is the better template: it carries no live
                    // state and exists before the menu is opened.
                    if (template == null || !inScene) template = candidate;
                    if (!inScene) break;
                }
            if (template == null) return null;

            if (_stageRoot == null)
            {
                _stageRoot = new GameObject("CustomCampaignPreviewStage");
                UnityEngine.Object.DontDestroyOnLoad(_stageRoot);
                _stageRoot.hideFlags = HideFlags.HideAndDontSave;
                var canvas = _stageRoot.AddComponent<Canvas>();
                // World space parked far from the play area, NOT an overlay canvas
                // pushed off-screen: uGUI culls off-screen graphics and spine then
                // stops generating their meshes, which left the collector with
                // nothing to draw for 437 jobs straight.
                canvas.renderMode = RenderMode.WorldSpace;
                var canvasRect = _stageRoot.GetComponent<RectTransform>();
                if (canvasRect != null) canvasRect.sizeDelta = new Vector2(1920f, 1080f);
                _stageRoot.transform.position = new Vector3(0f, 100000f, 0f);
            }

            var clone = UnityEngine.Object.Instantiate(template.gameObject, _stageRoot.transform);
            clone.name = "PreviewStageSkater";
            var rect = clone.GetComponent<RectTransform>();
            if (rect != null)
            {
                // Centred inside the stage canvas so nothing clips or culls it.
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = Vector2.zero;
            }
            clone.SetActive(true);

            var preview = clone.GetComponent<SkaterPreviewInUI>();
            KeepSkeletonsUpdating(preview);
            if (preview == null || preview.ForwardSkeleton == null)
            {
                UnityEngine.Object.Destroy(clone);
                _stageBroken = true;
                Plugin.Log.LogWarning(
                    "[PreviewAssets] Off-screen preview stage has no skeleton; using the live menu preview instead.");
                return null;
            }
            _stagePreview = preview;
            if (!_stageLogged)
            {
                _stageLogged = true;
                Plugin.Log.LogInfo("[PreviewAssets] Rendering into a private off-screen preview stage.");
            }
            return _stagePreview;
        }
        catch (Exception ex)
        {
            _stageBroken = true;
            Plugin.Log.LogWarning(
                $"[PreviewAssets] Could not build an off-screen preview stage ({ex.Message}); using the live menu preview.");
            return null;
        }
    }

    /// <summary>
    /// Force the stage's skeletons to keep building meshes even when uGUI decides
    /// they are invisible. Spine skips mesh generation for invisible graphics, and
    /// a preview that is deliberately parked out of sight is always invisible.
    /// </summary>
    private static void KeepSkeletonsUpdating(SkaterPreviewInUI preview)
    {
        if (preview == null) return;
        foreach (var skeleton in new[] { SafeSkeleton(preview, false), SafeSkeleton(preview, true) })
        {
            if (skeleton == null) continue;
            try
            {
                skeleton.UpdateWhenInvisible = UpdateMode.FullUpdate;
                skeleton.UpdateMode = UpdateMode.FullUpdate;
            }
            catch { }
            try { if (skeleton.canvasRenderer != null) skeleton.canvasRenderer.cull = false; } catch { }
        }
    }

    private static SkeletonGraphic SafeSkeleton(SkaterPreviewInUI preview, bool goalie)
    {
        try { return goalie ? preview.GoalieSkeleton : preview.ForwardSkeleton; }
        catch { return null; }
    }

    private static SkaterPreviewInUI FindLivePreview(bool goalie)
    {
        try
        {
            var previews = Resources.FindObjectsOfTypeAll<SkaterPreviewInUI>();
            if (previews == null) return null;
            SkaterPreviewInUI sceneFallback = null;
            foreach (var preview in previews)
            {
                if (preview == null || preview.gameObject == null) continue;
                if (preview == _stagePreview) continue;
                bool hasSkeleton = false;
                try { hasSkeleton = goalie ? preview.GoalieSkeleton != null : preview.ForwardSkeleton != null; }
                catch { }
                if (!hasSkeleton) continue;
                bool sceneObject = false;
                try { sceneObject = preview.gameObject.scene.IsValid(); } catch { }
                if (!sceneObject) continue; // never drive a prefab asset directly
                if (preview.gameObject.activeInHierarchy && preview.enabled) return preview;
                sceneFallback ??= preview;
            }
            // A hidden scene preview can still be refreshed directly; the mesh is
            // explicitly rebuilt before capture.
            return sceneFallback;
        }
        catch { return null; }
    }

    private static void RestorePreview()
    {
        if (_activePreview == null || _activeIsStage) return;
        try
        {
            if (_restorePlayer != null)
            {
                var team = _restoreTeam ?? FindTeamForPlayer(_restorePlayer);
                if (team != null) _activePreview.RefreshSkater(_restorePlayer, team, true, null);
            }
            else
            {
                _activePreview.SetVisible(false);
            }
        }
        catch { }
    }

    private static TeamData FindTeamForPlayer(SkaterData player)
    {
        if (player == null) return null;
        try
        {
            var teams = Resources.FindObjectsOfTypeAll<TeamData>();
            if (teams == null) return null;
            foreach (var team in teams)
            {
                if (team == null) continue;
                try
                {
                    if (team.goalie != null && team.goalie.Pointer == player.Pointer) return team;
                    if (team.forwards != null)
                        for (int i = 0; i < team.forwards.Count; i++)
                            if (team.forwards[i] != null && team.forwards[i].Pointer == player.Pointer) return team;
                }
                catch { }
            }
        }
        catch { }
        return null;
    }

    // ==========================================================
    //  Exact live preview requested by the Creator
    // ==========================================================

    private static void ReadNewestExactRequest()
    {
        var request = ReadExactRequest();
        if (request == null || request.Id == _lastCompletedRequestId) return;

        // The Creator derives the id from the configuration alone, so an id we
        // have already rendered is the same player. Answer from disk instead of
        // rebuilding a team clone and burning a render slot.
        string relative = "players/" + request.Id + ".png";
        if (File.Exists(FullPath(relative)))
        {
            try
            {
                AtomicWriteText(_responsePath,
                    $"version\t{ExporterVersion}\nrequest_id\t{request.Id}\nfile\t{relative}\n");
                _lastCompletedRequestId = request.Id;
                _pendingExact = null;
                return;
            }
            catch { }
        }

        if (_pendingExact == null || _pendingExact.Id != request.Id)
            _pendingExact = request;
    }

    private static ExactRequest ReadExactRequest()
    {
        if (string.IsNullOrEmpty(_requestPath) || !File.Exists(_requestPath)) return null;
        try
        {
            var request = new ExactRequest();
            string version = null;
            foreach (string raw in File.ReadAllLines(_requestPath))
            {
                string line = raw.TrimEnd('\r', '\n');
                if (string.IsNullOrEmpty(line)) continue;
                string[] parts = line.Split('\t');
                if (parts.Length < 2) continue;
                if (parts[0] == "version") version = parts[1];
                else if (parts[0] == "request_id") request.Id = parts[1];
                else if (parts[0] == "is_goalie") request.IsGoalie = parts[1] == "1";
                else if (parts[0] == "focus_field") request.FocusField = parts[1];
                else if (parts.Length >= 3 && parts[0] == "player") request.Player.Add((parts[1], parts[2]));
                else if (parts.Length >= 3 && parts[0] == "team") request.Team.Add((parts[1], parts[2]));
            }
            if (version != ExporterVersion || string.IsNullOrEmpty(request.Id)) return null;
            return request;
        }
        catch { return null; }
    }

    private static RenderJob PrepareExactJob(ExactRequest request)
    {
        try
        {
            var teams = Resources.FindObjectsOfTypeAll<TeamData>();
            if (teams == null || teams.Length == 0) return null;
            // Deterministic pick, not "first one Resources happened to return".
            // The request id is derived from the configuration, so the same id
            // must always produce the same image — including across launches,
            // where enumeration order is not stable.
            TeamData sourceTeam = null;
            string bestName = null;
            foreach (var team in teams)
            {
                if (team == null) continue;
                bool usable = request.IsGoalie
                    ? team.goalie != null
                    : team.forwards != null && team.forwards.Count > 0 && team.forwards[0] != null;
                if (!usable) continue;
                string name;
                try { name = team.teamName ?? ""; } catch { continue; }
                if (bestName == null || string.CompareOrdinal(name, bestName) < 0)
                {
                    bestName = name;
                    sourceTeam = team;
                }
            }
            if (sourceTeam == null) return null;

            var teamClone = UnityEngine.Object.Instantiate(sourceTeam);
            bool home = string.IsNullOrEmpty(request.FocusField) || IsHomeField(request.FocusField);
            SkaterData target;

            // The config apply below is the game's real one and logs heavily — a
            // whole team's relics, talents, logos and goalie debug per keystroke.
            // Preview work must not narrate itself into the player's log.
            var previousLog = BeginQuiet();
            try
            {
                PatchChooseMetaUI.DeepCloneForwards(teamClone);
                DetachColors(teamClone);

                var cfg = new TeamConfig();
                foreach (var pair in request.Team)
                    Plugin.ApplyTeamField(cfg, pair.key.Trim().ToLowerInvariant(), pair.value);
                var pc = new PlayerConfig();
                foreach (var pair in request.Player)
                    Plugin.ApplyPlayerField(pc, pair.key.Trim().ToLowerInvariant(), pair.value);

                // Resolve imported-team uniforms/colors and the current unsaved team
                // editor values on the detached clone only.
                PatchBossLaunchMatch.ApplyTeamFromConfig(teamClone, cfg);
                if (request.IsGoalie)
                {
                    target = teamClone.goalie;
                    PatchBossLaunchMatch.ApplyGoalieConfig(teamClone.goalie, pc, useAway: !home);
                    PatchBossLaunchMatch.ApplyTeamEquipmentColorsToGoalie(teamClone.goalie, teamClone, cfg, useAway: !home);
                }
                else
                {
                    if (teamClone.forwards == null || teamClone.forwards.Count == 0) target = null;
                    else
                    {
                        var forward = teamClone.forwards[0];
                        PatchBossLaunchMatch.ApplyPlayerConfig(forward, pc, cfg.Uniform);
                        PatchBossLaunchMatch.ApplyTeamEquipmentColors(forward, cfg, teamClone, useAway: !home);
                        PatchBossLaunchMatch.ApplyPlayerColorOverrides(forward, pc, useAway: !home);
                        target = forward;
                    }
                }
            }
            finally { EndQuiet(previousLog); }

            if (target == null)
            {
                try { UnityEngine.Object.Destroy(teamClone); } catch { }
                return null;
            }

            var job = new RenderJob
            {
                Kind = "player",
                Role = request.IsGoalie ? "goalie" : "skater",
                Field = request.FocusField ?? "Exact",
                Value = request.Id,
                RelativePath = $"players/{request.Id}.png",
                Player = target,
                Team = teamClone,
                IsHome = home,
                IsExact = true,
                RequestId = request.Id
            };
            job.OwnedObjects.Add(teamClone);
            if (teamClone.forwards != null)
                for (int i = 0; i < teamClone.forwards.Count; i++)
                    if (teamClone.forwards[i] != null) job.OwnedObjects.Add(teamClone.forwards[i]);
            if (teamClone.goalie != null) job.OwnedObjects.Add(teamClone.goalie);
            return job;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[PreviewAssets] Exact request preparation failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Redirect the mod's log to an unregistered source. A ManualLogSource that
    /// was never added to BepInEx's Logger.Sources has nowhere to write, so this
    /// silences the game-config code we call without touching any of it.
    /// </summary>
    private static ManualLogSource BeginQuiet()
    {
        var previous = Plugin.Log;
        try
        {
            _silentLog ??= new ManualLogSource("CustomCampaignPreview");
            Plugin.Log = _silentLog;
        }
        catch { return previous; }
        return previous;
    }

    private static void EndQuiet(ManualLogSource previous)
    {
        if (previous != null) Plugin.Log = previous;
    }

    private static void DetachColors(TeamData team)
    {
        if (team == null) return;
        try { if (team.homeColors != null) team.homeColors = TeamColorsData.CopyFrom(team.homeColors); } catch { }
        try { if (team.awayColors != null) team.awayColors = TeamColorsData.CopyFrom(team.awayColors); } catch { }
        try { if (team.homeGoalieColors != null) team.homeGoalieColors = TeamColorsData.CopyFrom(team.homeGoalieColors); } catch { }
        try { if (team.awayGoalieColors != null) team.awayGoalieColors = TeamColorsData.CopyFrom(team.awayGoalieColors); } catch { }
        try
        {
            if (team.forwards != null)
                for (int i = 0; i < team.forwards.Count; i++)
                    if (team.forwards[i]?.colorSchemes != null)
                        team.forwards[i].colorSchemes = TeamColorsData.CopyFrom(team.forwards[i].colorSchemes);
            if (team.goalie?.colorSchemes != null)
                team.goalie.colorSchemes = TeamColorsData.CopyFrom(team.goalie.colorSchemes);
        }
        catch { }
    }

    private static void CleanupOwned(RenderJob job)
    {
        if (job == null) return;
        foreach (var obj in job.OwnedObjects)
        {
            try { if (obj != null) UnityEngine.Object.Destroy(obj); } catch { }
        }
        job.OwnedObjects.Clear();
    }

    // ==========================================================
    //  Capture
    // ==========================================================

    /// <summary>
    /// Collect what the Spine graphic would actually draw. spine-unity splits a
    /// skeleton over several CanvasRenderers when it needs more than one material,
    /// and in that mode <c>GetCurrentMesh()</c> only knows about the first one.
    /// </summary>
    private static List<DrawItem> CollectDrawItems(SkeletonGraphic graphic)
    {
        var items = new List<DrawItem>();
        try
        {
            var meshes = graphic.MeshesMultipleCanvasRenderers;
            if (meshes != null && meshes.Count > 0)
            {
                var materials = graphic.MaterialsMultipleCanvasRenderers;
                var textures = graphic.TexturesMultipleCanvasRenderers;
                var renderers = graphic.canvasRenderers;
                for (int i = 0; i < meshes.Count; i++)
                {
                    Mesh mesh = meshes.Items[i];
                    if (mesh == null || mesh.vertexCount == 0) continue;

                    CanvasRenderer renderer = null;
                    if (renderers != null && i < renderers.Count) renderer = renderers[i];
                    // Do NOT skip a culled renderer. `cull` is uGUI's own
                    // on-screen visibility flag; we draw the mesh ourselves and an
                    // off-screen preview is exactly the case we care about.

                    Material material = null;
                    if (materials != null && i < materials.Length) material = materials[i];
                    if (material == null && renderer != null && renderer.materialCount > 0)
                        material = renderer.GetMaterial(0);
                    if (material == null) continue;

                    Texture texture = null;
                    if (textures != null && i < textures.Count) texture = textures.Items[i];

                    Matrix4x4 matrix = renderer != null && renderer.transform != null
                        ? renderer.transform.localToWorldMatrix
                        : graphic.transform.localToWorldMatrix;
                    items.Add(new DrawItem(mesh, 0, material, texture, matrix));
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogDebug($"[PreviewAssets] Multi-renderer collection failed: {ex.Message}");
        }

        if (items.Count > 0) return items;

        try
        {
            Mesh mesh = graphic.GetCurrentMesh();
            var canvasRenderer = graphic.canvasRenderer;
            if (mesh != null && mesh.vertexCount > 0 && canvasRenderer != null)
            {
                Matrix4x4 matrix = graphic.transform.localToWorldMatrix;
                Texture texture = null;
                try { texture = graphic.mainTexture; } catch { }
                int subMeshes = Math.Max(1, mesh.subMeshCount);
                int count = Math.Min(subMeshes, Math.Max(1, canvasRenderer.materialCount));
                for (int i = 0; i < count; i++)
                {
                    Material material = canvasRenderer.GetMaterial(i);
                    if (material == null) continue;
                    items.Add(new DrawItem(mesh, i, material, texture, matrix));
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogDebug($"[PreviewAssets] Single-renderer collection failed: {ex.Message}");
        }
        return items;
    }

    private static Bounds WorldBounds(List<DrawItem> items)
    {
        bool any = false;
        Bounds result = new Bounds(Vector3.zero, Vector3.zero);
        foreach (var item in items)
        {
            Bounds local;
            try
            {
                item.Mesh.RecalculateBounds();
                local = item.Mesh.bounds;
            }
            catch { continue; }
            Vector3 c = local.center, e = local.extents;
            for (int corner = 0; corner < 8; corner++)
            {
                var point = new Vector3(
                    c.x + ((corner & 1) == 0 ? -e.x : e.x),
                    c.y + ((corner & 2) == 0 ? -e.y : e.y),
                    c.z + ((corner & 4) == 0 ? -e.z : e.z));
                Vector3 world = item.Matrix.MultiplyPoint3x4(point);
                if (!any) { result = new Bounds(world, Vector3.zero); any = true; }
                else result.Encapsulate(world);
            }
        }
        return result;
    }

    /// <summary>
    /// Draw the skeleton's meshes into an off-screen texture.
    ///
    /// This goes through a CommandBuffer on purpose. The immediate-mode
    /// GL/Graphics.DrawMeshNow route this replaced silently drew NOTHING under
    /// this game's URP pipeline — every equipment PNG came out blank and was
    /// cached as if it had worked. Graphics.ExecuteCommandBuffer is executed
    /// straight away and is render-pipeline agnostic.
    /// </summary>
    private static Capture CaptureSkeleton(SkeletonGraphic graphic)
    {
        return CaptureSkeleton(graphic, null, 0f);
    }

    /// <summary>
    /// Push pose changes through to the mesh the capture will read. Slot colours
    /// only become vertex colours here, so isolation is not visible until this
    /// has run.
    /// </summary>
    private static void RebuildMesh(SkeletonGraphic skeleton)
    {
        try
        {
            skeleton.LateUpdate();
            skeleton.UpdateMesh(true);
            skeleton.UpdateMaterials();
            Canvas.ForceUpdateCanvases();
        }
        catch { }
    }

    private static Capture CaptureSkeleton(SkeletonGraphic graphic, Vector3? forcedCenter, float forcedHalf)
    {
        if (graphic == null) return null;
        var items = CollectDrawItems(graphic);
        if (items.Count == 0) return null;
        if (DumpSlotAndMaterialInventory) DumpMaterialInventory(items);

        Bounds bounds = WorldBounds(items);
        float extent = Math.Max(Math.Max(bounds.size.x, bounds.size.y), 1e-4f);
        float half = extent * 0.58f; // a little air around the skater
        if (forcedCenter.HasValue && forcedHalf > 0f)
        {
            // An isolated piece has tiny bounds of its own; framing it on those
            // would zoom in and destroy the alignment layers depend on.
            bounds = new Bounds(forcedCenter.Value, Vector3.zero);
            half = forcedHalf;
        }
        // A preview whose transform is scaled to zero collapses every vertex onto
        // one point. That was observed live (boundsSize=(0,0,0) with 4 items) and
        // would otherwise cache a frame zoomed infinitely into nothing.
        else if (bounds.size.x < 1e-3f && bounds.size.y < 1e-3f) return null;

        Capture capture = null;
        if (!_useReplacementShader)
        {
            capture = Render(items, bounds, half, useReplacementShader: false, clearOpaque: false);
            if (capture != null && capture.OpaquePixels >= MinOpaquePixels) return capture;
        }

        // The game's own material can refuse to draw into a foreign target — a
        // stencil ref left over from a uGUI Mask, a _ColorMask of 0, a clip rect
        // that does not contain us. Redraw the same geometry with a plain
        // transparent shader before declaring the frame empty.
        var replacement = Render(items, bounds, half, useReplacementShader: true, clearOpaque: false);
        if (replacement != null && replacement.OpaquePixels >= MinOpaquePixels)
        {
            if (!_fallbackLogged)
            {
                _fallbackLogged = true;
                Plugin.Log.LogInfo(
                    "[PreviewAssets] Game materials drew nothing; using a replacement shader for previews.");
            }
            // Stop paying for the doomed first pass on every remaining job.
            _useReplacementShader = true;
            return replacement;
        }

        return capture;
    }

    /// <summary>
    /// Draw the collected items into a temporary render texture and read it back.
    /// </summary>
    private static Capture Render(List<DrawItem> items, Bounds bounds, float half,
                                  bool useReplacementShader, bool clearOpaque)
    {
        Vector3 center = bounds.center;
        RenderTexture rt = null;
        Texture2D readable = null;
        CommandBuffer buffer = null;
        var previousActive = RenderTexture.active;
        try
        {
            rt = RenderTexture.GetTemporary(PreviewSize, PreviewSize, 24,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);

            // Straight-on orthographic view centred on the skater. Unity's view
            // matrix negates Z, hence the scale term.
            Matrix4x4 cameraToWorld = Matrix4x4.TRS(
                new Vector3(center.x, center.y, center.z - 500f), Quaternion.identity, Vector3.one);
            Matrix4x4 view = Matrix4x4.Scale(new Vector3(1f, 1f, -1f)) * cameraToWorld.inverse;
            // renderIntoTexture MUST be false. With true, D3D flips Y for the
            // texture origin, ReadPixels/EncodeToPNG then flip again, and every
            // preview comes out upside down (measured). false also keeps the
            // result identical on GL/Vulkan, where that flag is a no-op — the
            // capture is symmetric, so no platform correction belongs here.
            Matrix4x4 projection = GL.GetGPUProjectionMatrix(
                Matrix4x4.Ortho(-half, half, -half, half, 0.03f, 5000f), false);

            buffer = new CommandBuffer();
            buffer.name = "CustomCampaign preview capture";
            buffer.SetRenderTarget(new RenderTargetIdentifier(rt));
            buffer.ClearRenderTarget(true, true,
                clearOpaque ? new Color(1f, 0f, 0f, 1f) : new Color(0f, 0f, 0f, 0f));
            buffer.SetViewProjectionMatrices(view, projection);

            if (!clearOpaque)
            {
                var block = new MaterialPropertyBlock();
                foreach (var item in items)
                {
                    Material material = useReplacementShader
                        ? ReplacementMaterial()
                        : Sanitize(item.Material);
                    if (material == null) continue;
                    // uGUI binds the atlas page through CanvasRenderer, not through
                    // the material, so _MainTex can be empty on what we were given.
                    block.Clear();
                    if (item.Texture != null) block.SetTexture(_mainTexId, item.Texture);
                    buffer.DrawMesh(item.Mesh, item.Matrix, material, item.SubMesh, 0, block);
                }
            }
            Graphics.ExecuteCommandBuffer(buffer);

            RenderTexture.active = rt;
            readable = new Texture2D(PreviewSize, PreviewSize, TextureFormat.ARGB32, false);
            readable.ReadPixels(new Rect(0, 0, PreviewSize, PreviewSize), 0, 0);
            readable.Apply();

            var result = new Capture { FrameCenter = center, FrameHalf = half };
            Summarize(readable, result);
            if (!clearOpaque) result.Png = ImageConversion.EncodeToPNG(readable);
            return result;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[PreviewAssets] Capture threw: {ex.Message}");
            return null;
        }
        finally
        {
            RenderTexture.active = previousActive;
            if (buffer != null) { try { buffer.Release(); } catch { } }
            if (rt != null) RenderTexture.ReleaseTemporary(rt);
            if (readable != null) UnityEngine.Object.Destroy(readable);
        }
    }

    /// <summary>
    /// Copy a uGUI material with every reason it might refuse to draw removed.
    /// Stencil/clip/colour-mask state comes from MATERIAL properties, which a
    /// MaterialPropertyBlock cannot override — a masked preview would fail the
    /// stencil test against our freshly cleared buffer and draw nothing.
    /// </summary>
    private static Material Sanitize(Material source)
    {
        if (source == null) return null;
        int id;
        try { id = source.GetInstanceID(); } catch { return source; }
        if (SanitizedMaterials.TryGetValue(id, out var cached) && cached != null) return cached;
        try
        {
            var copy = new Material(source) { hideFlags = HideFlags.HideAndDontSave };
            SetIfPresent(copy, "_Stencil", 0f);
            SetIfPresent(copy, "_StencilComp", 8f);      // Always
            SetIfPresent(copy, "_StencilOp", 0f);        // Keep
            SetIfPresent(copy, "_StencilReadMask", 255f);
            SetIfPresent(copy, "_StencilWriteMask", 0f);
            SetIfPresent(copy, "_ColorMask", 15f);       // RGBA
            SetIfPresent(copy, "_UseUIAlphaClip", 0f);
            try { copy.DisableKeyword("UNITY_UI_CLIP_RECT"); } catch { }
            try { copy.DisableKeyword("UNITY_UI_ALPHACLIP"); } catch { }
            try
            {
                if (copy.HasProperty("_ClipRect"))
                    copy.SetVector("_ClipRect", new Vector4(-1e9f, -1e9f, 1e9f, 1e9f));
            }
            catch { }
            SanitizedMaterials[id] = copy;
            return copy;
        }
        catch { return source; }
    }

    private static void SetIfPresent(Material material, string property, float value)
    {
        try { if (material.HasProperty(property)) material.SetFloat(property, value); }
        catch { }
    }

    private static Material ReplacementMaterial()
    {
        if (_fallbackMaterial != null) return _fallbackMaterial;
        try
        {
            // UI/Default first: it ships with uGUI so it is always present, it
            // multiplies _MainTex by the vertex colour exactly like the canvas
            // path, and its stencil/clip defaults draw unconditionally.
            Shader shader = Shader.Find("UI/Default");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            if (shader == null) shader = Shader.Find("Unlit/Transparent");
            if (shader == null) return null;
            _fallbackMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }
        catch { return null; }
        return _fallbackMaterial;
    }

    /// <summary>
    /// Dump everything needed to tell apart the ways this can fail, once per
    /// session. The control render is the important part: it clears to opaque red
    /// and draws nothing, so if it comes back empty the command buffer itself is
    /// not running and no amount of material fiddling will help.
    /// </summary>
    private static void LogCaptureDiagnostics(SkeletonGraphic graphic, bool nothingToDraw)
    {
        // Once per renderer source. The stage failing first must not swallow the
        // diagnostic for the live preview, which is the more interesting case.
        if (_activeIsStage) { if (_diagStageLogged) return; _diagStageLogged = true; }
        else { if (_diagLiveLogged) return; _diagLiveLogged = true; }
        try
        {
            var items = CollectDrawItems(graphic);
            Bounds bounds = WorldBounds(items);
            float half = Math.Max(Math.Max(bounds.size.x, bounds.size.y), 1e-4f) * 0.58f;

            Plugin.Log.LogWarning(
                $"[PreviewAssets] DIAG source={(_activeIsStage ? "private stage" : "live menu preview")} " +
                $"nothingToDraw={nothingToDraw} items={items.Count} " +
                $"boundsCenter={bounds.center} boundsSize={bounds.size} half={half}");

            // Two controls, in increasing order of what they rule out.
            // 1. Blit: proves render texture + ReadPixels work at all (they do for
            //    head sprites, so a failure here would be a surprise).
            int blitPixels = -1;
            try
            {
                var rt = RenderTexture.GetTemporary(PreviewSize, PreviewSize, 0,
                    RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
                var previous = RenderTexture.active;
                Graphics.Blit(Texture2D.whiteTexture, rt);
                RenderTexture.active = rt;
                var probe = new Texture2D(PreviewSize, PreviewSize, TextureFormat.ARGB32, false);
                probe.ReadPixels(new Rect(0, 0, PreviewSize, PreviewSize), 0, 0);
                probe.Apply();
                var summary = new Capture();
                Summarize(probe, summary);
                blitPixels = summary.OpaquePixels;
                RenderTexture.active = previous;
                UnityEngine.Object.Destroy(probe);
                RenderTexture.ReleaseTemporary(rt);
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"[PreviewAssets] DIAG blit control threw: {ex.Message}"); }

            // 2. CommandBuffer clear to opaque red, drawing nothing. If THIS is 0
            //    the command buffer is not executing and no material work can help.
            var control = Render(items, bounds, half, useReplacementShader: false, clearOpaque: true);
            Plugin.Log.LogWarning(
                $"[PreviewAssets] DIAG blit-control={blitPixels} commandbuffer-clear-control={control?.OpaquePixels ?? -1} " +
                $"(both should be {PreviewSize * PreviewSize})");

            try
            {
                var canvas = graphic.canvas;
                var renderers = graphic.canvasRenderers;
                var meshes = graphic.MeshesMultipleCanvasRenderers;
                Mesh current = null;
                try { current = graphic.GetCurrentMesh(); } catch { }
                Plugin.Log.LogWarning(
                    $"[PreviewAssets] DIAG canvas='{(canvas == null ? "null" : canvas.name)}' " +
                    $"mode={(canvas == null ? "n/a" : canvas.renderMode.ToString())} " +
                    $"multiRenderers={graphic.allowMultipleCanvasRenderers} " +
                    $"canvasRenderers={(renderers == null ? -1 : renderers.Count)} " +
                    $"multiMeshes={(meshes == null ? -1 : meshes.Count)} " +
                    $"currentMeshVerts={(current == null ? -1 : current.vertexCount)} " +
                    $"materialCount={graphic.canvasRenderer?.materialCount} " +
                    $"cull={graphic.canvasRenderer?.cull} crAlpha={graphic.canvasRenderer?.GetAlpha()} " +
                    $"updateMode={graphic.UpdateMode} whenInvisible={graphic.UpdateWhenInvisible} " +
                    $"active={graphic.gameObject.activeInHierarchy} color={graphic.color}");
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"[PreviewAssets] DIAG graphic state threw: {ex.Message}"); }

            for (int i = 0; i < items.Count && i < 4; i++)
            {
                var item = items[i];
                string shader = "?", materialName = "?", texture = "none";
                try { materialName = item.Material == null ? "null" : item.Material.name; } catch { }
                try { shader = item.Material?.shader == null ? "null" : item.Material.shader.name; } catch { }
                try { texture = item.Texture == null ? "none" : $"{item.Texture.name} {item.Texture.width}x{item.Texture.height}"; } catch { }
                int minAlpha = -1, maxAlpha = -1;
                try
                {
                    var colors = item.Mesh.colors32;
                    if (colors != null && colors.Length > 0)
                    {
                        minAlpha = 255; maxAlpha = 0;
                        for (int c = 0; c < colors.Length; c++)
                        {
                            int a = colors[c].a;
                            if (a < minAlpha) minAlpha = a;
                            if (a > maxAlpha) maxAlpha = a;
                        }
                    }
                }
                catch { }
                Plugin.Log.LogWarning(
                    $"[PreviewAssets] DIAG item{i} verts={item.Mesh.vertexCount} sub={item.SubMesh}/{item.Mesh.subMeshCount} " +
                    $"localBounds={item.Mesh.bounds} mat='{materialName}' shader='{shader}' tex={texture} " +
                    $"vertexAlpha={minAlpha}..{maxAlpha}");
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[PreviewAssets] DIAG failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Write the skeleton's slot list to `_preview_slots.txt`, once per role.
    ///
    /// This is reconnaissance for layered previews: to render one piece in
    /// isolation we have to hide every slot that does not belong to it, and the
    /// slot names are the only way to know which those are. They cannot be read
    /// offline — the dump is signatures-only and `_game_skins.txt` lists skin
    /// paths, not slots.
    ///
    /// Safe by construction: `Skeleton.Slots` is an `ExposedList&lt;Slot&gt;` and
    /// `Slot` is a CLASS, so this walks a reference array — the same pattern
    /// already used for the mesh list. It is NOT `Skin.Attachments`, which is a
    /// collection of non-blittable `SkinEntry` STRUCTS and hard-crashes the
    /// process when read through IL2CPP reflection (session 12 §3).
    /// </summary>
    private static void DumpSlotInventory(SkeletonGraphic graphic, string role)
    {
        if (graphic == null || SlotsDumped.Contains(role)) return;
        SlotsDumped.Add(role);
        try
        {
            var skeleton = graphic.skeleton;
            if (skeleton == null) return;
            var slots = skeleton.Slots;
            if (slots == null || slots.Count == 0) return;

            var sb = new StringBuilder();
            sb.AppendLine($"# Spine slot inventory for '{role}' — written by the preview exporter.");
            sb.AppendLine("# Used to work out which slots belong to which equipment field.");
            try { sb.AppendLine($"# skin={skeleton.Skin?.Name ?? "(none)"}"); } catch { }
            // The slot colour is the deciding fact for offline recolouring. Spine
            // bakes it into the mesh vertex colours, so if the team's colours live
            // HERE they already survive into our captures and a layer rendered
            // with the slot forced white can simply be multiplied by the user's
            // colour in the Creator. If instead every slot is plain white, the
            // colouring happens in the shader and needs a different approach.
            sb.AppendLine("index\tslot\tattachment\tcolor_rgba\tdark_color");
            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots.Items[i];
                if (slot == null) continue;
                string name = "?", attachment = "", color = "", dark = "";
                try { name = slot.Data?.Name ?? "?"; } catch { }
                try { attachment = slot.Pose?.Attachment?.Name ?? ""; } catch { }
                try
                {
                    var c = slot.Pose.GetColor();
                    color = $"{c.r:F3},{c.g:F3},{c.b:F3},{c.a:F3}";
                }
                catch { }
                try
                {
                    var d = slot.Pose.GetDarkColor();
                    if (d.HasValue) dark = $"{d.Value.r:F3},{d.Value.g:F3},{d.Value.b:F3}";
                }
                catch { }
                sb.Append(i).Append('\t').Append(name).Append('\t').Append(attachment)
                  .Append('\t').Append(color).Append('\t').Append(dark).AppendLine();
            }
            string path = Path.Combine(Plugin.ModContentRoot, $"_preview_slots_{role}.txt");
            AtomicWriteText(path, sb.ToString());
            Plugin.Log.LogInfo($"[PreviewAssets] Wrote {slots.Count} {role} slot names to '_preview_slots_{role}.txt'");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[PreviewAssets] Slot inventory for '{role}' failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Dump the real Spine materials and every shader property they expose, once.
    ///
    /// The other half of the recolouring question. `Spine/T2T SkeletonGraphic` is
    /// a custom shader, and customization systems usually colour either through
    /// Spine slot colours (which land in vertex colours) or through shader
    /// parameters plus a mask texture — the `Customization_colors` attachment
    /// names hint at the latter. Those two need completely different offline
    /// approaches, so this reads the property list rather than guessing.
    ///
    /// Note this dumps the GAME's material, not our sanitized copy.
    /// </summary>
    private static void DumpMaterialInventory(List<DrawItem> items)
    {
        if (_materialsDumped || items == null || items.Count == 0) return;
        _materialsDumped = true;
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Spine material + shader properties, written by the preview exporter.");
            sb.AppendLine("# Decides how custom colours are applied, and so how they can be");
            sb.AppendLine("# reproduced offline in the Creator.");
            var seen = new HashSet<int>();
            foreach (var item in items)
            {
                var material = item.Material;
                if (material == null) continue;
                int id;
                try { id = material.GetInstanceID(); } catch { continue; }
                if (!seen.Add(id)) continue;

                var shader = material.shader;
                sb.AppendLine();
                sb.AppendLine($"material\t{material.name}");
                sb.AppendLine($"shader\t{(shader == null ? "(null)" : shader.name)}");
                try
                {
                    var keywords = material.shaderKeywords;
                    if (keywords != null && keywords.Length > 0)
                        sb.AppendLine($"keywords\t{string.Join(" ", keywords)}");
                }
                catch { }
                if (shader == null) continue;

                int count = 0;
                try { count = shader.GetPropertyCount(); } catch { }
                for (int i = 0; i < count; i++)
                {
                    string name = null, value = "";
                    try { name = shader.GetPropertyName(i); } catch { continue; }
                    if (string.IsNullOrEmpty(name)) continue;
                    try
                    {
                        var type = shader.GetPropertyType(i);
                        switch (type)
                        {
                            case ShaderPropertyType.Color:
                            {
                                var c = material.GetColor(name);
                                value = $"color {c.r:F3},{c.g:F3},{c.b:F3},{c.a:F3}";
                                break;
                            }
                            case ShaderPropertyType.Vector:
                                value = $"vector {material.GetVector(name)}";
                                break;
                            case ShaderPropertyType.Float:
                            case ShaderPropertyType.Range:
                                value = $"float {material.GetFloat(name):F3}";
                                break;
                            case ShaderPropertyType.Texture:
                            {
                                var tex = material.GetTexture(name);
                                value = tex == null ? "texture (none)"
                                    : $"texture {tex.name} {tex.width}x{tex.height}";
                                break;
                            }
                            default:
                                value = type.ToString();
                                break;
                        }
                    }
                    catch { value = "(unreadable)"; }
                    sb.AppendLine($"prop\t{name}\t{value}");
                }
            }
            AtomicWriteText(Path.Combine(Plugin.ModContentRoot, "_preview_materials.txt"), sb.ToString());
            Plugin.Log.LogInfo($"[PreviewAssets] Wrote {seen.Count} material(s) to '_preview_materials.txt'");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[PreviewAssets] Material inventory failed: {ex.Message}");
        }
    }

    // ==========================================================
    //  Layered, recolourable previews
    // ==========================================================
    //
    // The game bakes its colour scheme into the atlas, so an exported PNG has its
    // colours fixed and cannot be retinted. Rather than reverse-engineer that
    // bake, this drives the game's OWN colour inputs and captures the result:
    // for each piece, render it isolated once per colour channel with that
    // channel white and the others black. Each pass is a mask. The Creator then
    // reconstructs any colours with
    //
    //     final = residue + Σ (mask_channel × chosen_colour)
    //
    // which is exact for a linear per-channel colouring, and needs no knowledge
    // of how the game actually applies it.

    /// <summary>Which Spine slots belong to each editor field.</summary>
    private static readonly Dictionary<string, string[]> SkaterSlotGroups = new(StringComparer.Ordinal)
    {
        // Each group is coloured by its OWN scheme in game, so anything with a
        // separate scheme must be a separate group. The number is not part of
        // the jersey and the socks (the "Leg" slots) are not part of the pants —
        // conflating them meant one 3-colour decomposition had to serve two
        // schemes, and the second one could never be applied.
        ["Body"] = new[] { "Body" },
        ["Number"] = new[] { "Number", "Name" },
        ["Bicep"] = new[] { "Bicep left", "Bicep right", "Forearm left", "Forearm right" },
        ["Gloves"] = new[] { "Glove left", "Glove right" },
        ["Pants"] = new[] { "Pants left", "Pants right" },
        ["Socks"] = new[] { "Leg left", "Leg right" },
        ["Skates"] = new[] { "Skate left", "Skate right" },
        ["Stick"] = new[] { "Stick" },
        ["Helmet"] = new[] { "Helmet", "Helmet_Top", "Helmet_Custom" },
        ["Glasses"] = new[] { "Glasses" },
        // The face is drawn by the HELMET slots. Measured 2026-08-02: isolating
        // each piece and reading its bounds put Helmet at the top of the frame
        // (y 23-90) with 2664 pixels, while Body sat at y 66-182 — so the head
        // region belongs to Helmet, and a face is a head skin painted onto it.
        //
        // An earlier version isolated the face by EXCLUSION (hide everything
        // that is known gear, keep the rest) on the assumption the head had no
        // nameable slot. That produced 166 byte-identical BLANK layers, because
        // hiding the gear hides the head with it. Do not go back to that.
        ["Face"] = new[] { "Helmet", "Helmet_Top", "Helmet_Custom" },
    };

    private static readonly Dictionary<string, string[]> GoalieSlotGroups = new(StringComparer.Ordinal)
    {
        ["Skin"] = new[] { "Body", "Arm_left", "Arm_right", "Forearm_left" },
        ["Pads Skin"] = new[] { "Pad_left", "Pad_right", "Pants_right", "Pants_left" },
        ["Glove Skin"] = new[] { "Glove" },
        ["Blocker Skin"] = new[] { "Blocker" },
        ["Stick Skin"] = new[] { "Stick" },
        ["Helmet Skin"] = new[] { "Helmet", "Helmet_Top", "Helmet_Custom" },
        ["Logo Skin"] = new[] { "Team_logo" }, // prefix match
        // As with the skater, the head is drawn by the helmet slots.
        ["Face"] = new[] { "Helmet", "Helmet_Top", "Helmet_Custom" },
    };

    // Driving TeamColorsData to build masks is a DEAD END — do not rebuild it.
    //
    // The obvious design is to set a ColorScheme's primary/secondary/tertiary to
    // white/black/black, render, and keep the frame as the primary mask. It was
    // built, and it cannot work here: the capture draws with a replacement
    // `UI/Default` material (the game's `Spine/T2T SkeletonGraphic` will not draw
    // into a foreign render target, session 17 §3d), and `UI/Default` does not
    // run the palette remap that turns a ColorScheme into pixels. The scheme is
    // therefore invisible to the capture, which is why all four passes came out
    // identical whatever was written to it.
    //
    // For the record, the mapping was: jersey ← Body/Bicep/Glasses/Skin/Logo
    // Skin, gloves ← Gloves/Glove Skin/Blocker Skin, pants ← Pants/Pads Skin,
    // skates ← Skates, stick ← Stick/Stick Skin, helmet ← Helmet/Helmet Skin;
    // TeamColorsData exposes those plus number and socks. The Creator holds the
    // equivalent table in `GameAssetPreview._PIECE_COLORS`, which is where the
    // colours are now applied.

    /// <summary>Slots for a field, resolved against the role's group table.</summary>
    private static string[] SlotGroup(string role, string field)
    {
        string bare = field.Replace(" Away", "").Trim();
        var table = role == "goalie" ? GoalieSlotGroups : SkaterSlotGroups;
        if (table.TryGetValue(bare, out var slots)) return slots;
        // The goalie's away fields drop the "Skin" its home fields carry —
        // "Glove Away" is the same piece as "Glove Skin". Without this those
        // four pieces silently get no layers at all.
        return table.TryGetValue(bare + " Skin", out slots) ? slots : null;
    }

    /// <summary>
    /// The channel each key colour in the atlas art stands for, and the unit
    /// vector it normalises to. See <see cref="SplitLayers"/>.
    /// </summary>
    private static readonly (string channel, Vector3 key)[] KeyColors =
    {
        ("primary",   new Vector3(1f, 0f, 0f)), // #ff0000
        ("secondary", new Vector3(1f, 1f, 0f)), // #ffff00
        ("tertiary",  new Vector3(1f, 0f, 1f)), // #ff00ff
    };

    // Below this saturation a pixel is real art (skin, stick wood, skate black,
    // the grey blade) and is never treated as a colour key.
    private const float KeySaturation = 0.35f;
    // How far a normalised pixel may sit from a key and still count as it.
    private const float KeyTolerance = 0.40f;
    // A pixel this bright with red dominant can only be a blend of keys — the
    // art in this atlas never reaches it. This is what lets edge pixels between
    // two key regions be unmixed instead of rounded to the nearer key.
    private const byte FullBrightness = 250;

    /// <summary>
    /// Fields the game does not team-colour. Their art is real (skin, hair) and
    /// happens to contain saturated reds, so key extraction must not run on it —
    /// a red-haired face would otherwise be recoloured with the jersey.
    /// </summary>
    private static readonly HashSet<string> NonColourableFields =
        new(StringComparer.Ordinal) { "Face" };

    // Layers must share one camera frame or they will not stack. The frame comes
    // from the FULL skeleton on the flat pass, before any slot is hidden.
    // The UNION of every bounds measured for a role, in world space. Both the
    // centre and the extent are derived from it, so the frame always contains
    // everything it has ever seen. An earlier version kept the FIRST centre and
    // only grew the half-extent — which meant a centre measured on a partly
    // attached skeleton was never corrected, and the head, which sits at the top
    // of the body, fell off the top edge of every capture.
    private static readonly Dictionary<string, Bounds> RoleBounds =
        new(StringComparer.Ordinal);

    /// <summary>The camera frame for a role: centred on, and containing, its union bounds.</summary>
    private static bool TryRoleFrame(string role, out Vector3 center, out float half)
    {
        center = default; half = 0f;
        if (!RoleBounds.TryGetValue(role, out Bounds union)) return false;
        center = union.center;
        // 1.16x the largest dimension: the same framing as before, with the
        // margin now applied around a centre that cannot drift out of date.
        half = Math.Max(union.size.x, union.size.y) * 0.58f;
        return half > 1e-4f;
    }
    private static List<(Spine.Slot, Color)> _isolated;
    private static bool _layersDisabled;
    private static bool _layerProofDone;
    private static int _layerMaskedPieces;
    private static int _layerFlatPieces;
    private static int _layerEmptyPieces;
    // How many key-coloured (i.e. helmet) pixels each face measured. This is
    // what splits the list into the helmeted and helmetless groups.
    private static readonly Dictionary<string, int> FaceHelmetPixels =
        new(StringComparer.Ordinal);
    private static bool _facesFileDirty;
    private static int _facesWithHelmet;

    /// <summary>
    /// ON. One isolated render per piece; the four masks are split out of that
    /// single capture rather than rendered separately.
    ///
    /// The previous design rendered each piece four times, driving the game's
    /// ColorScheme to white/black per pass. That could never have worked, for a
    /// reason that only became clear once the captures were measured against the
    /// request that produced them (2026-08-02):
    ///
    ///   * The capture draws with a replacement `UI/Default` material, because
    ///     `Spine/T2T SkeletonGraphic` refuses to draw into a foreign render
    ///     target (session 17 §3d). `UI/Default` does not run the game's palette
    ///     remap, so the ColorScheme cannot affect the captured image at all —
    ///     all four passes were identical because the input was irrelevant.
    ///   * What the capture DOES contain is the raw atlas art, which is already
    ///     key-coloured: pure red = primary, pure yellow = secondary, pure
    ///     magenta = tertiary, with shading carried as luminance. Measured on a
    ///     player render whose config asked for a green jersey and got red.
    ///
    /// So the masks are extracted from the art, not driven into it. That is one
    /// render per piece instead of four, and it removes the dependency on a
    /// colour path the exporter cannot reach.
    /// </summary>
    private static readonly bool GenerateColorLayers = true;

    internal static string LayerRelativePath(string role, string field, string value, string channel)
    {
        string identity = (role ?? "") + "\n" + (field ?? "") + "\n" + (value ?? "");
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        string hex = Convert.ToHexString(hash).ToLowerInvariant().Substring(0, 24);
        return "layers/" + hex + "_" + channel + ".png";
    }

    /// <summary>
    /// Queue the ONE isolated pass that makes a piece recolourable offline.
    /// Returns how many were queued (0 or 1).
    ///
    /// Driven from the inventory scan, NOT only from a completed flat render:
    /// once the flat cache is full nothing re-renders, so hanging layer work off
    /// the render path would mean layers never got built at all.
    ///
    /// Every piece with a slot group gets one, colourable or not — a piece the
    /// colour scheme never touches still needs its isolated art so the Creator
    /// can stack it. Such a piece simply yields empty masks.
    /// </summary>
    private static int QueueLayerPasses(string role, string field, string value,
                                        SkaterData player, TeamData team, bool isHome,
                                        string sourceField = null)
    {
        if (!GenerateColorLayers || _layersDisabled) return 0;
        if (SlotGroup(role, field) == null) return 0;

        // All four outputs come from one capture, so the job is only worth
        // running when at least one of them is missing.
        bool complete = true;
        foreach (string channel in LayerChannels)
        {
            if (!File.Exists(FullPath(LayerRelativePath(role, field, value, channel))))
            {
                complete = false;
                break;
            }
        }
        if (complete) return 0;

        string key = "layer\0" + MakeKey(role, field, value);
        if (!QueuedKeys.Add(key)) return 0;
        EquipmentQueue.Enqueue(new RenderJob
        {
            Kind = "layer", Role = role, Field = field, Value = value,
            RelativePath = LayerRelativePath(role, field, value, "base"),
            Player = player, Team = team, IsHome = isHome, LayerChannel = "*",
            SourceField = sourceField
        });
        return 1;
    }

    /// <summary>
    /// Pieces the game colours with their own scheme but which have no field of
    /// their own in the editor. They ride along on the field whose art contains
    /// them, isolated to their own slots.
    /// </summary>
    private static readonly Dictionary<string, string> DerivedLayerFields =
        new(StringComparer.Ordinal)
    {
        ["Body"] = "Number",
        ["Body Away"] = "Number Away",
        ["Pants"] = "Socks",
        ["Pants Away"] = "Socks Away",
    };

    private static readonly string[] LayerChannels = { "base", "primary", "secondary", "tertiary" };

    /// <summary>
    /// Hide every slot outside this piece's group.
    ///
    /// Called from <see cref="FinishRender"/>, NOT from <see cref="BeginRender"/>.
    /// `RefreshSkinAndColors` is an async UniTask that rebuilds the skeleton and
    /// resets slot colours, so isolation applied before it is silently undone —
    /// that is why every previous layer pass came out as a whole skater. By the
    /// time FinishRender runs the settle budget has elapsed, and nothing else
    /// touches the skeleton between here and the capture.
    /// </summary>
    /// <summary>
    /// Establish ONE camera frame per role and reuse it for every capture.
    ///
    /// Framing each capture on its own bounds produced previews at wildly
    /// different zooms — a partially-applied skin has only some slots attached,
    /// so its bounds are tiny and the camera zooms into a corner of the body
    /// (seen on the Hockey FC goalie). A fixed frame also makes the pieces
    /// stack, which the layers depend on.
    ///
    /// Every measurement is UNIONED, and the centre is re-derived from the
    /// union each time. The previous version kept the first centre it ever saw
    /// and only grew the half-extent, so a centre measured on a half-attached
    /// skeleton stayed wrong forever: the frame grew around the wrong point and
    /// the head — which sits at the top of the body — was cut off at the top
    /// edge of 225 of 226 face exports. Growing an extent cannot fix a bad
    /// centre; only re-deriving it can.
    /// </summary>
    private static void EnsureRoleFrame(string role, SkeletonGraphic skeleton)
    {
        if (skeleton == null) return;
        try
        {
            var whole = CollectDrawItems(skeleton);
            if (whole.Count == 0) return;
            Bounds b = WorldBounds(whole);
            // A preview transform can be scaled to zero for a frame, which
            // collapses every bound onto a point; unioning that would drag the
            // centre to the origin and shrink everything into a corner.
            if (b.size.x < 1e-3f && b.size.y < 1e-3f) return;
            if (RoleBounds.TryGetValue(role, out Bounds union))
            {
                union.Encapsulate(b);
                RoleBounds[role] = union;
            }
            else RoleBounds[role] = b;
        }
        catch { }
    }

    private static void ApplyLayerIsolation(RenderJob job, SkeletonGraphic skeleton)
    {
        if (skeleton == null) return;
        string[] keep = SlotGroup(job.Role, job.Field);
        if (keep != null) _isolated = IsolateSlots(skeleton, keep);
    }

    private static void EndLayerPass(SkeletonGraphic skeleton)
    {
        if (_isolated == null) return;
        RestoreSlots(_isolated);
        _isolated = null;
        // Put the restored colours back into the mesh in the same frame. The
        // live menu preview is a skater the player is looking at, and leaving
        // the isolated mesh up until the game's next LateUpdate shows as a
        // one-frame flicker of pieces vanishing.
        if (skeleton != null) RebuildMesh(skeleton);
    }

    /// <summary>
    /// Prove isolation actually reached the render, by CONTENT, before writing
    /// hundreds of files.
    ///
    /// Two false passes in this project came from comparing hashes: 161
    /// byte-identical blank PNGs counted as successes, and four visually
    /// identical images that anti-aliasing made differ by a few pixels counted
    /// as proof. So this compares what is actually in the frame — one piece must
    /// cover materially less of it than the whole skater does. A glove that
    /// still fills the skater's silhouette was not isolated.
    /// </summary>
    private static void CheckLayerProof(RenderJob job, int wholePixels, int isolatedPixels)
    {
        if (_layerProofDone) return;
        _layerProofDone = true;

        if (wholePixels > 0 && isolatedPixels < wholePixels * 0.9f)
        {
            Plugin.Log.LogInfo(
                $"[PreviewAssets] Isolation verified by content: '{job.Field}' covers {isolatedPixels} " +
                $"of the skater's {wholePixels} pixels. Building recolourable layers.");
            return;
        }

        _layersDisabled = true;
        DropQueuedLayerJobs();
        Plugin.Log.LogWarning(
            $"[PreviewAssets] Slot isolation is not reaching the render — an isolated '{job.Field}' still " +
            $"covers {isolatedPixels} of {wholePixels} pixels. Layers would just be whole skaters, so " +
            "layered previews are disabled for this session. Flat previews are unaffected.");
    }

    private static void DropQueuedLayerJobs()
    {
        var remaining = new Queue<RenderJob>();
        while (EquipmentQueue.Count > 0)
        {
            var pending = EquipmentQueue.Dequeue();
            if (pending.LayerChannel == null) remaining.Enqueue(pending);
            else QueuedKeys.Remove("layer\0" + MakeKey(pending.Role, pending.Field, pending.Value));
        }
        while (remaining.Count > 0) EquipmentQueue.Enqueue(remaining.Dequeue());
    }

    /// <summary>
    /// Split one isolated capture into the base plate and three colour masks.
    ///
    /// The atlas art is key-coloured — pure red/yellow/magenta stand for the
    /// primary/secondary/tertiary channels, and shading rides along as
    /// luminance. So a pixel is classified by the direction of its RGB vector
    /// and its brightness becomes the mask value, which makes
    /// <c>base + Σ(mask × colour)</c> reproduce the shading for free.
    ///
    /// Alpha is assigned to exactly one output per pixel. If every layer carried
    /// the source alpha, the Creator's additive composite would stack four
    /// alphas on each anti-aliased edge and harden it.
    /// </summary>
    private static bool IsFaceField(string field)
    {
        return string.Equals(field, "Face", StringComparison.Ordinal);
    }

    // ----------------------------------------------------------
    //  Why helmeted faces are dropped rather than rendered bare
    // ----------------------------------------------------------
    //
    // The obvious fix is to make the game draw the head bare while the face is
    // captured. The game does decide helmet visibility from the HEAD skin rather
    // than the helmet field — `ForwardDataExtensions.HeadsWithoutHelmets` — and
    // appending the face being rendered to that array for the duration of the job
    // was built and MEASURED. It does not work, and this is written down so it is
    // not rebuilt: the array is writable (the log shows Plugin's own
    // RegisterFaceAsHelmetless growing it 43 -> 46) but the preview render is
    // unchanged, so SkaterPreviewInUI does not consult it on this path. The
    // export came back with 109 of 132 faces still helmeted.
    //
    // If this is picked up again, the untried route is
    // `Skaters.IEquipmentOwner.IgnoreHelmetSkin` / `NoHelmetEquipmentOwner`
    // (dumped by inspect_helmet4.ps1 in the project root), which is the game's
    // other way to a bare head. Until then a helmeted face is dropped: exporting
    // one means a bright red helmet, because the capture draws with a
    // replacement UI/Default material that does not run the palette remap.

    // A few stray key-coloured pixels can come from anti-aliasing; a helmet is
    // hundreds. Above this a face is carrying one.
    private const int MaxFaceKeyPixels = 60;

    /// <summary>How many pixels are pure colour-key art (i.e. recolourable).</summary>
    private static int CountKeyPixels(Color32[] pixels)
    {
        if (pixels == null) return 0;
        int count = 0;
        foreach (Color32 pixel in pixels)
        {
            if (pixel.a == 0) continue;
            byte mx = (byte)Math.Max(pixel.r, Math.Max(pixel.g, pixel.b));
            byte mn = (byte)Math.Min(pixel.r, Math.Min(pixel.g, pixel.b));
            if (pixel.r != mx || mx == 0) continue;
            if ((mx - mn) / (float)mx < KeySaturation) continue;
            var unit = new Vector3(pixel.r / (float)mx, pixel.g / (float)mx, pixel.b / (float)mx);
            for (int k = 0; k < KeyColors.Length; k++)
                if ((unit - KeyColors[k].key).magnitude <= KeyTolerance) { count++; break; }
        }
        return count;
    }

    private static bool SplitLayers(Color32[] pixels, RenderJob job, out int maskedPixels)
    {
        maskedPixels = 0;
        if (pixels == null || pixels.Length == 0) return false;

        int count = pixels.Length;
        var basePlate = new Color32[count];
        var masks = new Color32[KeyColors.Length][];
        for (int m = 0; m < masks.Length; m++) masks[m] = new Color32[count];
        bool colourable = !NonColourableFields.Contains(job.Field.Replace(" Away", "").Trim());

        for (int i = 0; i < count; i++)
        {
            Color32 pixel = pixels[i];
            byte a = pixel.a;
            if (a == 0) continue;

            // Math.Max has no byte overload, so these promote to int.
            byte mx = (byte)Math.Max(pixel.r, Math.Max(pixel.g, pixel.b));
            byte mn = (byte)Math.Min(pixel.r, Math.Min(pixel.g, pixel.b));

            // Decompose into an achromatic floor plus one weight per key. Every
            // key is red-dominant, so the green above the floor can only have
            // come from yellow and the blue only from magenta; what is left of
            // the red is pure red. This is EXACT for solid keys, for shaded
            // keys, and — the part that matters — for anti-aliased blends
            // BETWEEN two keys, which a nearest-key vote had to round to one
            // side. Those blends measured 0% interior across 58 of 60 files:
            // they are edge pixels, and rounding them was the visible fringing.
            int wy = pixel.g - mn;
            int wm = pixel.b - mn;
            int wr = (pixel.r - mn) - wy - wm;

            bool isKey = false;
            if (colourable && pixel.r == mx && wr >= 0 && mx > 0 &&
                (mx - mn) / (float)mx >= KeySaturation)
            {
                var unit = new Vector3(pixel.r / (float)mx, pixel.g / (float)mx, pixel.b / (float)mx);
                float best = float.MaxValue;
                for (int k = 0; k < KeyColors.Length; k++)
                    best = Math.Min(best, (unit - KeyColors[k].key).magnitude);

                // Near a key at any brightness, OR a full-brightness pixel that
                // has to be a blend of keys. Real art fails both: it sits too
                // far from every key AND is darker than a key ever is. Measured:
                // #de660d (leather) is 0.46 away at brightness 222, and stays
                // art; #ff9500 (a red/yellow edge) is full brightness and gets
                // unmixed into the two keys it actually came from.
                isKey = best <= KeyTolerance || mx >= FullBrightness;
            }

            if (!isKey)
            {
                // Real art the colour scheme never touches — skin, stick wood,
                // the grey blade. It carries the pixel and its alpha unchanged.
                basePlate[i] = pixel;
            }
            else
            {
                // The achromatic part stays on the base and only the coloured
                // part is tinted, so feeding the key colours back in reproduces
                // the source pixel for pixel.
                basePlate[i] = new Color32(mn, mn, mn, a);
                if (wr > 0) masks[0][i] = new Color32((byte)wr, (byte)wr, (byte)wr, a);
                if (wy > 0) masks[1][i] = new Color32((byte)wy, (byte)wy, (byte)wy, a);
                if (wm > 0) masks[2][i] = new Color32((byte)wm, (byte)wm, (byte)wm, a);
                maskedPixels++;
            }
        }

        bool ok = WriteLayerPng(job, "base", basePlate);
        for (int m = 0; m < masks.Length; m++)
            ok &= WriteLayerPng(job, KeyColors[m].channel, masks[m]);
        return ok;
    }

    private static bool WriteLayerPng(RenderJob job, string channel, Color32[] pixels)
    {
        Texture2D texture = null;
        try
        {
            texture = new Texture2D(PreviewSize, PreviewSize, TextureFormat.ARGB32, false);
            texture.SetPixels32(pixels);
            texture.Apply();
            byte[] png = ImageConversion.EncodeToPNG(texture);
            if (png == null || png.Length == 0) return false;
            AtomicWriteBytes(FullPath(LayerRelativePath(job.Role, job.Field, job.Value, channel)), png);
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[PreviewAssets] Layer '{channel}' write failed: {ex.Message}");
            return false;
        }
        finally
        {
            if (texture != null) UnityEngine.Object.Destroy(texture);
        }
    }

    /// <summary>
    /// Hide every slot outside <paramref name="keep"/> by zeroing its alpha, and
    /// return the original colours so they can be put back. Every slot in this
    /// skeleton sits at opaque white, so this is fully reversible.
    /// </summary>
    private static List<(Spine.Slot slot, Color color)> IsolateSlots(SkeletonGraphic graphic, string[] group)
    {
        var saved = new List<(Spine.Slot, Color)>();
        // A leading "!" inverts the list: hide these and keep everything else.
        // The face needs it — a face is a Spine skin, not a slot, so the head
        // can only be named by what it is NOT.
        bool hideMode = group.Length > 0 && group[0] == "!";
        int first = hideMode ? 1 : 0;
        try
        {
            var slots = graphic.skeleton?.Slots;
            if (slots == null) return saved;
            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots.Items[i];
                if (slot == null) continue;
                string name;
                try { name = slot.Data?.Name ?? ""; } catch { continue; }

                bool matches = false;
                for (int g = first; g < group.Length; g++)
                {
                    // Team_logo slots carry the team name, so match on prefix.
                    string want = group[g];
                    if (name.Equals(want, StringComparison.Ordinal) ||
                        name.StartsWith(want, StringComparison.Ordinal)) { matches = true; break; }
                }
                // Keep mode keeps what matches; hide mode keeps what does not.
                if (matches != hideMode) continue;

                try
                {
                    var original = slot.Pose.GetColor();
                    saved.Add((slot, original));
                    slot.Pose.SetColor(new Color(original.r, original.g, original.b, 0f));
                }
                catch { }
            }
        }
        catch { }
        return saved;
    }

    private static void RestoreSlots(List<(Spine.Slot slot, Color color)> saved)
    {
        if (saved == null) return;
        foreach (var (slot, color) in saved)
        {
            try { slot.Pose.SetColor(color); } catch { }
        }
        saved.Clear();
    }

    /// <summary>Count visible pixels and hash the image, in one pass.</summary>
    private static void Summarize(Texture2D texture, Capture capture)
    {
        capture.OpaquePixels = 0;
        capture.Signature = 1469598103934665603UL; // FNV-1a offset basis
        try
        {
            var pixels = texture.GetPixels32();
            capture.Pixels = pixels;
            for (int i = 0; i < pixels.Length; i++)
            {
                var pixel = pixels[i];
                if (pixel.a > 8) capture.OpaquePixels++;
                unchecked
                {
                    capture.Signature = (capture.Signature ^ pixel.r) * 1099511628211UL;
                    capture.Signature = (capture.Signature ^ pixel.g) * 1099511628211UL;
                    capture.Signature = (capture.Signature ^ pixel.b) * 1099511628211UL;
                    capture.Signature = (capture.Signature ^ pixel.a) * 1099511628211UL;
                }
            }
        }
        catch
        {
            // Without pixel data we cannot prove the frame has content, so treat
            // it as empty rather than risk caching another blank.
            capture.OpaquePixels = 0;
        }
    }

    // ==========================================================
    //  Manifest + atomic IO
    // ==========================================================

    private static void LoadManifest()
    {
        Entries.Clear();
        if (!File.Exists(_manifestPath))
        {
            // No manifest is as stale as a wrong one. Without this the face
            // measurements from a previous exporter version were read back and
            // reused — v8 inherited v7's "145 helmeted" verdicts, which were
            // taken before the helmet fix and were simply wrong.
            _cacheWasStale = true;
            _purgeFacesPending = true;
            return;
        }
        try
        {
            string version = null;
            var loaded = new List<ManifestEntry>();
            foreach (string raw in File.ReadAllLines(_manifestPath))
            {
                if (string.IsNullOrWhiteSpace(raw) || raw.StartsWith("#")) continue;
                string[] parts = raw.Split('\t');
                if (parts[0] == "version" && parts.Length >= 2) { version = parts[1]; continue; }
                if (parts[0] == "kind" || parts.Length < 5) continue;
                loaded.Add(new ManifestEntry
                {
                    Kind = parts[0], Role = parts[1], Field = parts[2],
                    Value = parts[3], RelativePath = parts[4]
                });
            }
            if (version != ExporterVersion)
            {
                _cacheWasStale = true;
                _purgeFacesPending = true;
                Plugin.Log.LogWarning($"[PreviewAssets] Cache exporter version '{version ?? "missing"}' is stale; rebuilding as v{ExporterVersion}.");
                return;
            }
            foreach (var entry in loaded) Entries[entry.Key] = entry;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[PreviewAssets] Manifest read failed: {ex.Message}");
        }
    }

    private static void WriteManifest()
    {
        var rows = new List<ManifestEntry>(Entries.Values);
        rows.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
        var sb = new StringBuilder();
        sb.AppendLine("# Tape to Tape game asset previews. Generated automatically.");
        sb.AppendLine($"version\t{ExporterVersion}");
        sb.AppendLine("kind\trole\tfield\tvalue\tfile");
        foreach (var entry in rows)
            sb.Append(entry.Kind).Append('\t').Append(entry.Role).Append('\t')
              .Append(entry.Field).Append('\t').Append(entry.Value).Append('\t')
              .Append(entry.RelativePath.Replace('\\', '/')).AppendLine();
        AtomicWriteText(_manifestPath, sb.ToString());
        _cacheWasStale = false;
    }

    private static void AddEntry(string kind, string role, string field, string value, string relative)
    {
        var entry = new ManifestEntry
        {
            Kind = kind, Role = role, Field = field, Value = value,
            RelativePath = relative.Replace('\\', '/')
        };
        Entries[entry.Key] = entry;
    }

    private static bool HasUsableEntry(string key)
    {
        return Entries.TryGetValue(key, out var entry) && File.Exists(FullPath(entry.RelativePath));
    }

    private static string MakeKey(string role, string field, string value)
    {
        return (role ?? "") + "\0" + (field ?? "") + "\0" + (value ?? "");
    }

    /// <summary>
    /// Hash the whole identity into the filename. Leaf names are NOT unique —
    /// Faces/TeamA/Same and Faces/TeamB/Same must not fight over one PNG — and
    /// the value can contain characters no filesystem accepts.
    /// </summary>
    private static string AssetRelativePath(string kind, string role, string field, string value)
    {
        string identity = (role ?? "") + "\n" + (field ?? "") + "\n" + (value ?? "");
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        string hex = Convert.ToHexString(hash).ToLowerInvariant().Substring(0, 24);
        return (kind == "head" ? "heads/" : "equipment/") + hex + ".png";
    }

    private static string FullPath(string relative)
    {
        return Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
    }

    private static void AtomicWriteBytes(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, path, true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
        }
    }

    private static void AtomicWriteText(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, text, new UTF8Encoding(false));
            File.Move(temporary, path, true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
        }
    }

    private static void TouchStatus(string state)
    {
        if (Time.unscaledTime < _nextStatusWrite) return;
        _nextStatusWrite = Time.unscaledTime + 1f;
        WriteStatus(state);
    }

    private static void WriteStatus(string state)
    {
        try
        {
            AtomicWriteText(_statusPath,
                $"version\t{ExporterVersion}\nstate\t{state}\n" +
                $"heads_total\t{_headsAvailable}\nheads_written\t{_headsWritten}\nheads_cached\t{_headsCached}\n" +
                $"equipment_total\t{_equipmentDiscovered}\nequipment_queue\t{EquipmentQueue.Count}\n" +
                $"equipment_failed\t{_equipmentFailed}\n" +
                $"cached\t{Entries.Count}\nupdated_utc\t{DateTime.UtcNow:O}\n");
        }
        catch { }
    }
}
