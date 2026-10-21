using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CampaignCore;
using Data;
using HarmonyLib;
using State;
using STS.Map;
using Tape2Tape;
using Tape2Tape.Customization.UI;

namespace EndlessMode;

// Manual hooks only. Final integration must activate the complete signing,
// launch, provenance and completion set together; PatchAll cannot enable this.
internal static class GoalieMatchLaunchGate
{
    internal const string Feature = "goalie-match-launch";
    internal static readonly NamedManagedHookContract[] Required = {
        Hook("start", "StartMatch", Array.Empty<string>(), "StartPrefix"),
        Hook("input", "OnStartInput", new[] { "Tape2Tape.Customization.UI.UIInputShortcutBase" }, "InputPrefix"),
        Hook("show", "ShowMenu", new[] { "STS.Map.MapNode" }, "ShowPrefix", "ShowPostfix"),
        Hook("back", "BackToMap", Array.Empty<string>(), "BackPrefix"),
        Hook("cancel", "OnCancelInput", new[] { "Tape2Tape.Customization.UI.UIInputShortcutBase" }, "CancelPrefix"),
        Hook("destroy", "OnDestroy", Array.Empty<string>(), "DestroyPrefix")
    };
    private static NamedManagedHookContract Hook(string id, string method, string[] parameters, string prefix, string postfix = "")
        => new() { Feature = Feature, Id = "goalie-launch-" + id, Assembly = "MainScriptsAssembly", DeclaringType = "MatchPreviewMenu",
            Method = method, ReturnType = "System.Void", Parameters = parameters, Prefix = prefix, Postfix = postfix };
    internal static (MethodInfo Native, MethodInfo Prefix, MethodInfo Postfix)[] Resolve()
        => Required.Select(c => (JerseyLogoManagedTargets.Resolve(c, AppDomain.CurrentDomain.GetAssemblies()),
            AccessTools.Method(typeof(GoalieMatchLaunchGate), c.Prefix) ?? throw new MissingMethodException(c.Prefix),
            c.Postfix.Length == 0 ? null : AccessTools.Method(typeof(GoalieMatchLaunchGate), c.Postfix) ?? throw new MissingMethodException(c.Postfix))).ToArray();

    internal enum Entry { StartMatch, OnStartInput }
    private sealed class Preview
    {
        internal MatchPreviewMenu Menu;
        internal long Generation;
        internal bool Open;
        internal Pending Pending;
    }
    private sealed class Pending
    {
        internal Preview Preview;
        internal long Generation;
        internal Entry Entry;
        internal UIInputShortcutBase Input;
        internal CampaignState Campaign;
        internal RunData Run;
        internal TeamData Team;
        internal GoaltenderData Goalie;
        internal MapNode Node;
        internal string Folder;
        internal GoalieMatchAttempt Attempt;
        internal GoaliePersistenceBridge.SaveReceipt Receipt;
        internal bool Used;
    }
    private static readonly Dictionary<IntPtr, Preview> Previews = new();
    private static long generation;
    [ThreadStatic] private static Pending permit;

    // Task4c captures this exact origin, then pairs it with the expected native
    // node and home initialization. No API exposes a mutable "current attempt".
    internal sealed class LaunchOrigin
    {
        internal readonly GoalieMatchAttempt attempt;
        internal GoalieMatchAttempt Attempt => attempt.Clone();
        internal CampaignState Campaign { get; }
        internal RunData Run { get; }
        internal TeamData HomeTeam { get; }
        internal GoaltenderData SourceGoalie { get; }
        internal MatchPreviewMenu Menu { get; }
        internal UIInputShortcutBase Input { get; }
        internal MapNode Destination { get; }
        internal long PreviewGeneration { get; }
        internal Entry OriginEntry { get; }
        internal string CheckpointId { get; }
        internal string OperationId { get; }
        internal string Folder { get; }
        internal LaunchOrigin(GoalieMatchAttempt attempt, CampaignState campaign, RunData run, TeamData team,
            MapNode destination, GoaltenderData sourceGoalie, MatchPreviewMenu menu, UIInputShortcutBase input,
            long previewGeneration, Entry entry, string checkpointId, string operationId, string folder)
        {
            this.attempt = attempt.Clone(); SourceGoalie = sourceGoalie; Menu = menu; Input = input; Campaign = campaign; Run = run; HomeTeam = team; Destination = destination;
            PreviewGeneration = previewGeneration; OriginEntry = entry; CheckpointId = checkpointId; OperationId = operationId; Folder = folder;
        }
    }
    internal static event Action<LaunchOrigin> LaunchAuthorized;
    internal static bool TryReuseOrigin(LaunchOrigin origin, CampaignState campaign, TeamData homeTeam)
    {
        try {
            var roster = GoaliePersistenceBridge.Roster;
            var saved = roster.Selection;
            return origin != null && !Plugin.IsDefaultMode && GameManager.GameType == ScriptableStates.GameType.Rogue
                && campaign?.Pointer == origin.Campaign.Pointer && campaign.Pointer == CampaignUtil.CampaignState?.Pointer
                && campaign.runData?.Pointer == origin.Run.Pointer && campaign.runData.teamData?.Pointer == origin.HomeTeam.Pointer
                && homeTeam?.Pointer == origin.HomeTeam.Pointer && homeTeam.id == origin.attempt.HomeTeamId
                && homeTeam.goalie?.id == origin.attempt.GoalieId && origin.Run.runID == origin.attempt.RunId
                && origin.Run.squadId == origin.attempt.SquadId && origin.Run.CurrentAct == origin.attempt.Act
                && origin.Run.currentNodePoint.x == origin.attempt.NodeIndex && origin.Run.currentNodePoint.y == origin.attempt.LayerIndex
                && roster.UnavailableReason == null
                && string.Equals(origin.Folder, Plugin.ModFolder, StringComparison.OrdinalIgnoreCase)
                && roster.Owns(campaign.runData, Plugin.ModFolder) && origin.attempt.Same(saved?.Attempt)
                && saved.ArmedAttemptToken == origin.attempt.Token && !saved.ProcessedAttemptTokens.Contains(origin.attempt.Token);
        } catch { return false; }
    }
    internal static bool MatchesDestination(LaunchOrigin origin, CampaignState campaign, MapNode destination, TeamData homeTeam)
        => TryReuseOrigin(origin, campaign, homeTeam) && destination?.Pointer == origin.Destination.Pointer
            && DestinationMatches(origin.attempt, campaign, destination);
    private static bool DestinationMatches(GoalieMatchAttempt attempt, CampaignState campaign, MapNode node)
        => node?.Node != null && campaign?.runData?.CurrentAct == attempt.Act
            && node.Node.NodeIndex == attempt.NodeIndex && node.Node.LayerIndex == attempt.LayerIndex
            && node.GetIl2CppType().FullName == attempt.NodeType;
    private static bool NeedsGate(GoalieRosterState saved) => saved != null
        && (saved.Active?.Contract != null || saved.Reserve?.Contract != null || saved.Attempt != null || saved.ArmedAttemptToken.Length > 0);
    private static void ShowPrefix(MatchPreviewMenu __instance) => Invalidate(__instance, false);
    private static void ShowPostfix(MatchPreviewMenu __instance)
    {
        if (__instance == null) return;
        Previews[__instance.Pointer] = new Preview { Menu = __instance, Generation = checked(++generation), Open = true };
    }
    private static void BackPrefix(MatchPreviewMenu __instance) => Invalidate(__instance, false);
    private static void CancelPrefix(MatchPreviewMenu __instance, UIInputShortcutBase __0)
    { if (__instance != null && !__instance.m_IsExiting) Invalidate(__instance, false); }
    private static void DestroyPrefix(MatchPreviewMenu __instance) => Invalidate(__instance, true);
    private static void Invalidate(MatchPreviewMenu menu, bool remove)
    {
        if (menu == null || !Previews.TryGetValue(menu.Pointer, out var preview)) return;
        preview.Open = false;
        if (remove) Previews.Remove(menu.Pointer);
    }
    private static bool StartPrefix(MatchPreviewMenu __instance) => Start(__instance, Entry.StartMatch, null);
    private static bool InputPrefix(MatchPreviewMenu __instance, UIInputShortcutBase __0) => Start(__instance, Entry.OnStartInput, __0);
    private static bool Start(MatchPreviewMenu menu, Entry entry, UIInputShortcutBase input)
    {
        var roster = GoaliePersistenceBridge.Roster;
        if (Plugin.IsDefaultMode || GameManager.GameType != ScriptableStates.GameType.Rogue || !NeedsGate(roster.Selection)) return true;
        try {
            if (permit != null) {
                var allowed = permit;
                // A wrong nested call cannot consume another origin's permit.
                if (allowed.Preview.Menu.Pointer != menu?.Pointer || allowed.Entry != entry || allowed.Input?.Pointer != input?.Pointer) return false;
                permit = null;
                if (allowed.Used || !Valid(allowed) || !Saved(allowed)) return false;
                allowed.Used = true;
                LaunchAuthorized?.Invoke(new LaunchOrigin(allowed.Attempt, allowed.Campaign, allowed.Run, allowed.Team,
                    allowed.Node, allowed.Goalie, allowed.Preview.Menu, allowed.Input, allowed.Generation, allowed.Entry, allowed.Receipt.Checkpoint.CheckpointId,
                    allowed.Receipt.OperationId, allowed.Folder));
                return true;
            }
            if (menu == null || menu.m_IsExiting || !menu.IsShown || !menu.gameObject.activeInHierarchy) return false;
            if (menu.m_TestZoomOnly) return true; // native zoom test is not a match
            if (!Previews.TryGetValue(menu.Pointer, out var preview) || !preview.Open) return false;
            if (preview.Pending != null) return false;
            var campaign = menu._campaignState;
            if (!roster.CanEdit(campaign) || roster.Busy) { Status(preview, roster.UnavailableReason ?? "Goalie save is in progress. Wait or go back."); return false; }
            var run = campaign.runData;
            var node = menu.currentMapNode;
            if (node?.Node == null) { Status(preview, "Match destination unavailable. Go back and reopen this preview."); return false; }
            var state = roster.Capture(run, Plugin.ModFolder);
            string route = GoalieRosterRuntime.RecruitmentWitness(campaign);
            var attempt = state.Attempt;
            if (attempt != null && !attempt.Matches(state, run.teamData.id, route, run.CurrentAct, node.Node.NodeIndex, node.Node.LayerIndex, node.GetIl2CppType().FullName)) {
                // Pending explicit user ruling: cancel/unlaunched supersession policy.
                // Do not retire a token or choose product behavior here yet.
                Status(preview, "Match context changed. Launch unavailable."); return false;
            }
            if (attempt == null && state.ArmedAttemptToken.Length != 0) {
                Status(preview, "Saved match context is unavailable. Return to the main menu and Continue before starting."); return false;
            }
            attempt ??= new GoalieMatchAttempt(state, run.teamData.id, route, run.CurrentAct, node.Node.NodeIndex, node.Node.LayerIndex, node.GetIl2CppType().FullName);
            var pending = new Pending { Preview = preview, Generation = preview.Generation, Entry = entry, Input = input,
                Campaign = campaign, Run = run, Team = run.teamData, Goalie = run.teamData.goalie, Node = node,
                Folder = Plugin.ModFolder, Attempt = attempt.Clone() };
            preview.Pending = pending; Status(preview, "Saving match attempt… You can still cancel.");
            if (!roster.TryArmMatch(campaign, attempt, result => Complete(pending, result))) {
                preview.Pending = null; Status(preview, "Match attempt could not be saved. Try again or go back.");
            }
            return false;
        } catch (Exception ex) {
            Plugin.Log.LogError("[GoalieLaunch] Launch withheld: " + ex);
            if (menu != null && Previews.TryGetValue(menu.Pointer, out var preview)) Status(preview, "Match launch unavailable. Go back or reload through Continue.");
            return false;
        }
    }
    private static bool Valid(Pending pending)
    {
        var p = pending.Preview; var menu = p.Menu;
        return p.Open && Previews.TryGetValue(menu.Pointer, out var selected) && ReferenceEquals(p, selected)
            && ReferenceEquals(p.Pending, pending) && p.Generation == pending.Generation
            && menu.IsShown && menu.gameObject.activeInHierarchy && !menu.m_IsExiting && !menu.m_TestZoomOnly
            && menu._campaignState?.Pointer == pending.Campaign.Pointer && menu.currentMapNode?.Pointer == pending.Node.Pointer
            && pending.Campaign.Pointer == CampaignUtil.CampaignState?.Pointer && pending.Campaign.runData?.Pointer == pending.Run.Pointer
            && pending.Run.runID == pending.Attempt.RunId && pending.Run.squadId == pending.Attempt.SquadId
            && pending.Run.teamData?.Pointer == pending.Team.Pointer && pending.Team.goalie?.Pointer == pending.Goalie.Pointer
            && string.Equals(pending.Folder, Plugin.ModFolder, StringComparison.OrdinalIgnoreCase)
            && GoaliePersistenceBridge.Roster.CanEdit(pending.Campaign)
            && pending.Attempt.Matches(GoaliePersistenceBridge.Roster.Selection, pending.Team.id,
                GoalieRosterRuntime.RecruitmentWitness(pending.Campaign), pending.Run.CurrentAct,
                pending.Node.Node.NodeIndex, pending.Node.Node.LayerIndex, pending.Node.GetIl2CppType().FullName);
    }
    private static bool Saved(Pending pending)
    {
        var receipt = pending.Receipt; var saved = GoaliePersistenceBridge.Roster.Selection;
        return receipt?.Durable == true && receipt.CallerToken != null && !string.IsNullOrEmpty(receipt.OperationId)
            && receipt.RunPointer == pending.Run.Pointer && receipt.RepositoryPointer == pending.Campaign._runDataRepository?.Pointer
            && receipt.AttemptToken == pending.Attempt.Token && pending.Attempt.Same(receipt.Admission?.Attempt)
            && pending.Attempt.Same(receipt.Checkpoint?.Attempt) && pending.Attempt.Same(saved?.Attempt)
            && receipt.Admission.ArmedAttemptToken == pending.Attempt.Token && receipt.Checkpoint.ArmedAttemptToken == pending.Attempt.Token
            && saved.ArmedAttemptToken == pending.Attempt.Token && saved.CheckpointId == receipt.Checkpoint.CheckpointId
            && Guid.TryParseExact(saved.CheckpointId, "N", out _) && saved.Revision > 0
            && saved.Revision == receipt.Checkpoint.Revision && !saved.ProcessedAttemptTokens.Contains(pending.Attempt.Token);
    }
    private static void Complete(Pending pending, GoalieRosterRuntime.ChangeResult result)
    {
        try {
            pending.Receipt = result.Receipt;
            if (!Valid(pending)) {
                // Unresolved persistence intentionally makes CanEdit false. It
                // must still explain recovery on the same surviving preview.
                if (result.Outcome == GoalieRosterRuntime.ChangeOutcome.Unresolved && pending.Preview.Open
                    && Previews.TryGetValue(pending.Preview.Menu.Pointer, out var current) && ReferenceEquals(current, pending.Preview)
                    && pending.Preview.Menu.currentMapNode?.Pointer == pending.Node.Pointer
                    && pending.Campaign.Pointer == CampaignUtil.CampaignState?.Pointer
                    && pending.Campaign.runData?.Pointer == pending.Run.Pointer)
                    Status(pending.Preview, result.Message);
                return;
            }
            if (result.Outcome != GoalieRosterRuntime.ChangeOutcome.Committed || !Saved(pending)) {
                Status(pending.Preview, result.Outcome == GoalieRosterRuntime.ChangeOutcome.Committed
                    ? result.Receipt?.Durable == true
                        ? "Saved match proof does not match this attempt. Return to the main menu and Continue before starting."
                        : "Match attempt saved locally, but platform save failed. Start again to retry, or cancel."
                    : result.Message); return;
            }
            permit = pending;
            if (pending.Entry == Entry.StartMatch) pending.Preview.Menu.StartMatch();
            else pending.Preview.Menu.OnStartInput(pending.Input);
        } catch (Exception ex) { Plugin.Log.LogError("[GoalieLaunch] Reentry withheld: " + ex); }
        finally {
            if (ReferenceEquals(permit, pending)) permit = null;
            if (ReferenceEquals(pending.Preview.Pending, pending)) pending.Preview.Pending = null;
        }
    }
    private static void Status(Preview preview, string message)
    {
        if (!preview.Open) return;
        Plugin.Log.LogWarning("[GoalieLaunch] " + message);
        if (preview.Menu.m_DescriptionText != null) preview.Menu.m_DescriptionText.text = message;
        if (preview.Menu.m_DescriptionContainer != null) preview.Menu.m_DescriptionContainer.SetActive(true);
    }
}
