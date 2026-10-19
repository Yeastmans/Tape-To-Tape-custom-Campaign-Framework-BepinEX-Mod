using System.Text.Json;
using CampaignCore;
using Iced.Intel;

namespace ModCompatibility;

public sealed class MethodContract
{
    public string Name { get; set; } = "";
    public string Signature { get; set; } = "";
    public string BodyHash { get; set; } = "";
}
public sealed class SiteContract
{
    public NativeComparisonSite Site { get; set; } = new();
    public int InstructionIndex { get; set; }
}
public sealed class CompatibilityBaseline
{
    public string Id { get; set; } = "";
    public NativeCompatibilityProfile Profile { get; set; } = new();
    public List<MethodContract> Methods { get; set; } = new();
    public List<SiteContract> Sites { get; set; } = new();
    public string FlowMethod { get; set; } = "";
    public int FlowIndex { get; set; }
    public string[] SquadCalls { get; set; } = [];
    public static readonly JsonSerializerOptions Json = new() { WriteIndented=true };
    public static CompatibilityBaseline Read(string file)
    {
        var baseline=JsonSerializer.Deserialize<CompatibilityBaseline>(File.ReadAllText(file)) ?? throw new InvalidDataException("Missing compatibility baseline.");
        string id=baseline.Id;baseline.Id="";baseline.Profile.BaselineId="";
        string actual=NativeImage.Digest(JsonSerializer.Serialize(baseline,Json));
        baseline.Id=id;baseline.Profile.BaselineId=id;
        if(actual!=id) throw new InvalidDataException("Studio's compatibility baseline was modified. Reinstall the suite.");
        return baseline;
    }

    // Release-time operation only. Studio never trusts a new baseline automatically.
    public static CompatibilityBaseline Create(NativeImage image,string manifest,string build,int flow,string flowMethod)
    {
        var baseline=new CompatibilityBaseline(); var p=baseline.Profile;
        p.GameHash=image.GameHash;p.MetadataHash=image.MetadataHash;p.Build=build;p.FlowRva=flow;
        p.SurfaceRva=checked((int)image.Resolve("Arena.IceSetup$$SwitchIceType").Rva);
        var protectedMethods=new HashSet<string>(StringComparer.Ordinal);
        foreach(var line in File.ReadLines(manifest).Where(l=>l.Length>0 && l[0]!='#'))
        {
            var cells=line.Split('\t');
            var site=new NativeComparisonSite { Feature=cells[0],Query=cells[1],Kind=cells[2],Rva=Convert.ToInt32(cells[3],16),Target=Convert.ToInt32(cells[4],16),Bytes=cells[5].ToUpperInvariant(),Method=NativeSymbolNames.Stable(cells[6]) };
            var method=image.Containing(site.Method,(ulong)site.Rva);site.Method=image.Identity(method); var body=image.Instructions(method.Rva);
            int pos=Array.FindIndex(body,i=>i.IP==(ulong)site.Rva);
            if(pos<0) throw new InvalidDataException("Patch is not on an instruction boundary: "+site.Method);
            p.Sites.Add(site);baseline.Sites.Add(new(){Site=site,InstructionIndex=pos}); protectedMethods.Add(site.Method);
        }
        baseline.FlowMethod=NativeSymbolNames.Stable(flowMethod);
        baseline.FlowIndex=Array.FindIndex(image.Instructions(image.Resolve(baseline.FlowMethod).Rva),i=>i.IP==(ulong)flow);
        if(baseline.FlowIndex<0) throw new InvalidDataException("Unknown starting-flow boundary.");
        protectedMethods.UnionWith(new[]{baseline.FlowMethod,"Arena.IceSetup$$SwitchIceType","Tape2Tape.CampaignUtil$$IsPlayingSquad","System.String$$op_Equality","System.String$$op_Inequality"});
        // The custom Check/Checked bridge observes the native match-wide hit
        // consumer instead of converting its non-blittable value-type event to
        // a managed delegate. Keep its ABI and balanced subscription lifecycle
        // under the same strict review gate as the other native integrations.
        foreach (string name in new[] { "GameStats$$OnHit::System.Void OnHit(Events.SkaterCheckEventArgs args)",
            "GameStats$$Awake::System.Void Awake()", "GameStats$$OnDestroy::System.Void OnDestroy()" })
        {
            var method = image.Resolve(name);
            if (image.SharedMethodCount(method.Rva) != 1) throw new InvalidDataException("Shared custom check-event target: " + name);
            protectedMethods.Add(image.Identity(method));
        }
        // The read-only custom two-line preview relies on the native roster's
        // inactive-line ordering and its presentation list. An address-only
        // game update may move these methods, but must not change that contract.
        foreach (string name in new[] { "Tape2Tape.Hockey.UI.LineupMenu$$LoadCurrentTeam", "Tape2Tape.Customization.UI.TeamRosterData$$.ctor", "Data.TeamData$$GetBench",
            "STS.Map.MatchMapNode$$AssignSubstitute", "STS.Map.MatchMapNode$$ReplaceForwardWithSubstitute", "Data.Lineup$$UpdateLineWithNewID" })
        {
            var method = image.Resolve(name);
            if (image.SharedMethodCount(method.Rva) != 1) throw new InvalidDataException("Shared lineup preview target: " + name);
            protectedMethods.Add(image.Identity(method));
        }
        // Pin native offer construction and its stat-adjustment contract. The
        // optional policy restores the completed offer's stats before display.
        foreach (string name in new[] { "Rogue.NPC.GeneralManagerNPC.GeneralManagerEvent$$CreatePlayerChoice", "Data.ForwardData$$AdjustPlayerStatsToMatchTeam" })
        {
            var method = image.Resolve(name);
            if (image.SharedMethodCount(method.Rva) != 1) throw new InvalidDataException("Shared recruitment stats target: " + name);
            protectedMethods.Add(image.Identity(method));
        }
        // Authored skater durations use the native played-match counter and
        // save format. Pin the fresh-run boundary, hire callback, expiry and
        // persistence consumers before accepting a future game update.
        foreach (string name in new[] { "State.CampaignState$$ResetRunData", "State.CampaignState$$ApplyNewRunStartEffects",
            "State.CampaignState$$InstantiateStartingTeam", "Rogue.NPC.GeneralManagerNPC.GeneralManagerEvent.<>c__DisplayClass#$$<CreatePlayerChoice>b__0",
            "Rogue.FreeAgents.FreeAgentManager$$OnCampaignMatchEnded", "Rogue.FreeAgents.FreeAgentManager$$HandleFreeAgentsLeaving",
            "Rogue.FreeAgents.FreeAgent$$AddMatch", "Rogue.FreeAgents.MaxGamesCondition$$Check",
            "State.SerializerV2$$SerializeToModel::State.RunDataModelV2 SerializeToModel(Data.RunData input)",
            "State.DeserializerV2$$DeserializeFromModel" })
        {
            var method = image.Resolve(name);
            if (image.SharedMethodCount(method.Rva) != 1) throw new InvalidDataException("Shared player-contract target: " + name);
            protectedMethods.Add(image.Identity(method));
        }
        // Custom ability pools may return fewer cards than the native fallback
        // requests. Its bounded adapter mirrors this event's card construction
        // and selection callback, so changed native logic requires review.
        foreach (string name in new[] { "Rogue.NPC.BlademasterNPC.BlademasterEvent$$OldBlademasterEvent",
            "Rogue.NPC.BlademasterNPC.BlademasterEvent.<>c__DisplayClass14_1$$<OldBlademasterEvent>b__1" })
        {
            var method = image.Resolve(NativeSymbolNames.Stable(name));
            if (image.SharedMethodCount(method.Rva) != 1) throw new InvalidDataException("Shared Blademaster target: " + name);
            protectedMethods.Add(image.Identity(method));
        }
        // The custom one-period timed HUD bypasses the native Rogue/Challenge
        // shortcut while retaining native localized formatting, marker cleanup
        // and real overtime transitions. Review those contracts on game updates.
        foreach (string name in new[] { "ScoreboardTimeRemaining$$Awake", "ScoreboardTimeRemaining$$GeneratePeriodMarkers",
            "ScoreboardTimeRemaining$$OnEndOfPeriod", "PeriodIndicatorUI$$GeneratePeriodMarkers",
            "PeriodIndicatorUI$$ClearPeriodMarkers", "PeriodIndicatorUI$$Start", "PeriodIndicatorUI$$OnEndOfPeriod",
            "PeriodIndicatorUI$$UpdatePeriodMarker", "PeriodIndicatorUI$$UpdateMercyCondition" })
        {
            var method = image.Resolve(name);
            if (image.SharedMethodCount(method.Rva) != 1) throw new InvalidDataException("Shared period HUD target: " + name);
            protectedMethods.Add(image.Identity(method));
        }
        // Match settings and authored reward counts are captured while the
        // selected map node still exists, before a match unloads that scene.
        // Review assignment order and null-node reset behavior on game updates.
        {
            const string name = "State.CampaignState$$SetCurrentNode";
            var method = image.Resolve(name);
            if (image.SharedMethodCount(method.Rva) != 1) throw new InvalidDataException("Shared current-node target: " + name);
            protectedMethods.Add(image.Identity(method));
        }
        // Authored map framing is established before native node art/links and
        // before the intro captures its end size. Native menu restoration must
        // retain that captured size; an update changing this order needs review.
        foreach (string name in new[] { "STS.Map.MapObject$$InitializeMap",
            "Tape2Tape.Hockey.UI.WorldMapActIntro$$Play",
            "Tape2Tape.Hockey.UI.WorldMapActIntro.<Play>d__30$$MoveNext",
            "Tape2Tape.Hockey.UI.WorldMapMenusHandler$$RefreshInitialCameraSettings",
            "Tape2Tape.Hockey.UI.WorldMapMenusHandler$$RestoreCamera",
            "Tape2Tape.Hockey.UI.WorldMapMenusHandler$$<RestoreCamera>g__Restore|69_0",
            "MatchPreviewMenu$$BackToMap",
            "DG.Tweening.ShortcutExtensions$$DOOrthoSize" })
        {
            var method = image.Resolve(NativeSymbolNames.Stable(name));
            if (image.SharedMethodCount(method.Rva) != 1) throw new InvalidDataException("Shared map camera target: " + name);
            protectedMethods.Add(image.Identity(method));
        }
        // Preserve authored goalie number text and each bound UI preview's
        // native equipment palette without changing native data or world colors.
        // Exact hook overloads, native format/material helpers and concrete UI
        // continuations require review if their behavior changes on an update.
        foreach (string name in new[] {
            "Skaters.T2TSkeletonExtensions$$ApplySkinAsync::Cysharp.Threading.Tasks.UniTask ApplySkinAsync(Spine.Unity.SkeletonGraphic skeletonGraphic, Spine.Skin skin, Data.SkaterData skaterData, Data.TeamColorsData teamColors = null, UnityEngine.Texture logo = null, System.Boolean matchTransform = False, System.Boolean skipAnimation = False, System.Boolean synchronous = False)",
            "Skaters.T2TSkeletonExtensions$$OverrideSlotWithCustomizationMaterial::System.Void OverrideSlotWithCustomizationMaterial(Spine.Unity.SkeletonGraphic skeletonGraphic, Spine.Skin skin, Data.TeamColorsData teamColors, System.String slotName, System.Collections.Generic.Dictionary`2<UnityEngine.Texture, UnityEngine.Material> materialCache)",
            "Skaters.T2TSkeletonExtensions$$ApplyColorSchemesToMaterial::System.Void ApplyColorSchemesToMaterial(Data.TeamColorsData teamColors, UnityEngine.Material material)",
            "Skaters.T2TSkeletonExtensions$$ApplySkinAsync::Cysharp.Threading.Tasks.UniTask ApplySkinAsync(Spine.Unity.SkeletonGraphic skeletonGraphic, Spine.Skin skin, Skaters.T2TSkeletonExtensions.CustomizationFlags flags, System.Boolean isGoalie, Data.TeamColorsData teamColors = null, UnityEngine.Texture logo = null, System.Boolean matchTransform = False, System.Boolean skipAnimation = False, System.Boolean synchronous = False)",
            "Skaters.T2TSkeletonExtensions$$RegisterMergedColors::Data.TeamColorsData RegisterMergedColors(UnityEngine.Object owner, Data.TeamColorsData colors)",
            "Data.TeamColorsData$$GetColorSchemeTexture::UnityEngine.Texture2D GetColorSchemeTexture()",
            "Data.TeamColorsData$$Merge::Data.TeamColorsData Merge(Data.TeamColorsData a, Data.TeamColorsData b)",
            "Data.SkaterData$$GetColors::Data.TeamColorsData GetColors()",
            "Tape2Tape.Customization.UI.TeamCustomizationPlayerPreview.<RefreshColors>d__#$$MoveNext::System.Void MoveNext()",
            "Tape2Tape.Hockey.UI.SkaterAndSkin.<ApplySkinAsync>d__#$$MoveNext::System.Void MoveNext()",
            "Tape2Tape.Customization.UI.SkaterPreviewInUI.<RefreshSkin_Internal>d__#$$MoveNext::System.Void MoveNext()",
            "Tape2Tape.Customization.CustomNumberApplier$$ApplyNumber::System.Void ApplyNumber(Data.SkaterData skaterData, Spine.Unity.SkeletonAnimation skeletonAnimation, System.Boolean isLefty, System.Boolean skipName, UnityEngine.Texture schemeTexture)",
            "Tape2Tape.Customization.CustomNumberController$$SetCustomNumberGoalie::System.Void SetCustomNumberGoalie(System.String backNumber, System.String playerName, Spine.Unity.SkeletonAnimation mySkeletonAnimation, Data.NumberFormat numberFormat, UnityEngine.Texture schemeTexture)",
            "Tape2Tape.Customization.CustomNumberApplier$$ToRomanNumeral::System.String ToRomanNumeral(System.Int32 number)",
            "Tape2Tape.Customization.CustomNumberController$$GenerateMaterial::UnityEngine.Material GenerateMaterial(System.String backNumber, System.String playerName, System.Boolean isLefty, System.Int32 numberTextureSize, Data.NumberFormat numberFormat, UnityEngine.Texture schemeTexture, UnityEngine.Shader shader)" })
        {
            var method = image.Resolve(name);
            if (image.SharedMethodCount(method.Rva) != 1) throw new InvalidDataException("Shared authored appearance target: " + name);
            protectedMethods.Add(image.Identity(method));
        }
        // Native async skin reuse is content-hash based; UI preloads may use
        // a new equivalent Skin object while the Skeleton keeps its old one.
        // Protect the canonical helper and the native decision using its result.
        foreach (string name in new[] {
            "Spine.Skin$$ComputeHash::System.Int32 ComputeHash(Spine.Skin s)",
            "Spine.Skin$$ComputeAttachmentHash::System.Int32 ComputeAttachmentHash(Spine.Attachment a)",
            "Spine.Skeleton.<SetSkinAsyncInternal>d__#$$MoveNext::System.Void MoveNext()" })
        {
            var method = image.Resolve(name);
            if (image.SharedMethodCount(method.Rva) != 1) throw new InvalidDataException("Shared preview skin-hash dependency: " + name);
            protectedMethods.Add(image.Identity(method));
        }
        // Authored fixed special nodes and explicit custom random outcomes are
        // preserved around the native training-map conversion. Both the order
        // of native generation and the existing replacement target need review.
        foreach (string name in new[] { "DAGG.Generation.TemplateToGraphConverter$$Convert",
            "DAGG.Generation.TemplateToGraphConverter$$ReplaceSpecialNodesWithTraining" })
        {
            var method = image.Resolve(name);
            if (image.SharedMethodCount(method.Rva) != 1) throw new InvalidDataException("Shared training-map target: " + name);
            protectedMethods.Add(image.Identity(method));
        }
        // Keep a fresh custom intro tied to its loaded graph rather than a
        // pre-loading map reference. Protect evaluation, its final camera write,
        // target synchronization and exact-owner cleanup on game updates.
        foreach (string name in new[] { "Tape2Tape.Hockey.UI.WorldMapActIntro$$Evaluate",
            "STS.Map.ScrollNonUI$$UpdatePosition", "STS.Map.ScrollNonUI$$SetTargetX",
            "STS.Map.MapObject$$OnDestroy" })
        {
            var method = image.Resolve(name);
            if (image.SharedMethodCount(method.Rva) != 1) throw new InvalidDataException("Shared fresh-map camera target: " + name);
            protectedMethods.Add(image.Identity(method));
        }
        foreach(var effect in OpponentEffects.All)
            foreach(var consumer in effect.Consumers)
            {
                int dot=consumer.LastIndexOf('.');
                string name=NativeSymbolNames.Stable(consumer[..dot]+"$$"+consumer[(dot+1)..]);
                var matches=image.Find(name).ToArray();
                if(matches.Length==0) throw new InvalidDataException("Missing opponent consumer: "+name);
                foreach(var match in matches)
                {
                    if(image.SharedMethodCount(match.Rva)!=1) throw new InvalidDataException("Shared opponent patch target: "+name);
                    protectedMethods.Add(image.Identity(match));
                }
            }
        // Recruitment-only talent multipliers preserve these native eligibility,
        // ownership, upgrade and weight contracts. One read-only category predicate
        // shares its body with two sibling filters; none is a new shared hook.
        foreach (string name in new[]
        {
            "Rogue.Powerups.Repository.TalentRepository$$GetRandomTalents::System.Collections.Generic.List`1<Rogue.Talent> GetRandomTalents(Rogue.Powerups.Repository.TalentCategory talentCategory = 0, System.Collections.Generic.List`1<Rogue.Talent> excludedTalents = null, System.Boolean isClone = True, System.Int32 numberOfTalents = 3, System.Boolean mustBeInPool = False, System.Boolean matchAnyCategory = True, Data.ForwardData forwardData = null)",
            "Rogue.Powerups.Repository.TalentRepository$$GetRandomTalent::Rogue.Talent GetRandomTalent(Rogue.Powerups.Repository.TalentCategory talentCategory = 0, System.Collections.Generic.List`1<Rogue.Talent> excludedTalents = null, System.Boolean isClone = True, System.Boolean mustBeInPool = False, System.Boolean matchAnyCategory = True, Data.ForwardData forwardData = null)",
            "Rogue.Powerups.Repository.TalentRepository$$GetTalents::System.Collections.Generic.IEnumerable`1<Rogue.Talent> GetTalents(Rogue.Powerups.Repository.TalentCategory category = 0)",
            "Rogue.Powerups.Repository.TalentRepository$$GetTalentsMatchingAnyCategory::System.Collections.Generic.IEnumerable`1<Rogue.Talent> GetTalentsMatchingAnyCategory(Rogue.Powerups.Repository.TalentCategory categories)",
            "Rogue.Powerups.Repository.TalentRepository.<>c__DisplayClass#$$<GetTalents>b__0::System.Boolean <GetTalents>b__0(Rogue.Talent talent)",
            "Rogue.Powerups.Repository.TalentRepository.<>c__DisplayClass#$$<GetTalentsMatchingAnyCategory>b__0::System.Boolean <GetTalentsMatchingAnyCategory>b__0(Rogue.Talent talent)",
            "Rogue.Powerups.Repository.TalentRepository$$AddExcludedIds::System.Void AddExcludedIds(System.Collections.Generic.List`1<Rogue.Talent> excludedTalents, System.Collections.Generic.HashSet`1<System.String> excludedIds)",
            "Rogue.Powerups.Repository.TalentRepository$$TryUpgradeTalent::Rogue.Talent TryUpgradeTalent(Rogue.Talent talent, System.Single upgradeChance, System.Boolean isClone)",
            "Rogue.Powerups.Repository.TalentRepository$$GetTalent::Rogue.Talent GetTalent(System.Type talentType, System.Int32 level = 1, System.Boolean isClone = True)",
            "Tape2Tape.CampaignUtil$$GetExcludedTalentRewards::System.Collections.Generic.List`1<Rogue.Talent> GetExcludedTalentRewards(System.Collections.Generic.IEnumerable`1<Rogue.Talent> talents)",
            "Rogue.PowerUp$$GetRewardWeight::System.Single GetRewardWeight(Data.RunData runData, Data.ForwardData forwardData = null)",
        })
        {
            var method = image.Resolve(name);
            int expectedShared = name.Contains("<GetTalents>b__0", StringComparison.Ordinal) ? 3 : 1;
            if (image.SharedMethodCount(method.Rva) != expectedShared) throw new InvalidDataException("Changed free-agent talent dependency sharing: " + name);
            protectedMethods.Add(image.Identity(method));
        }
        foreach(var name in protectedMethods.Order())
        {
            var method=image.Resolve(name);
            baseline.Methods.Add(new(){Name=name,Signature=image.Signature(method),BodyHash=image.BodyHash(method)});
        }
        baseline.SquadCalls=image.SquadCallInventory();
        baseline.Id=NativeImage.Digest(JsonSerializer.Serialize(baseline,Json));p.BaselineId=baseline.Id;
        baseline.Verify(image,build);
        return baseline;
    }
    public NativeCompatibilityProfile Verify(NativeImage image,string build,IProgress<string>? progress=null)
    {
        if(Profile.RuntimeContract!=NativeCompatibilityProfile.Contract || string.IsNullOrWhiteSpace(Id)) throw new InvalidDataException("Unsupported compatibility baseline.");
        if(!SquadCalls.SequenceEqual(image.SquadCallInventory())) throw new InvalidDataException("The game's squad behavior checks changed. A mod repair is required.");
        int done=0;
        foreach(var contract in Methods)
        {
            var method=image.Resolve(contract.Name);
            if(image.Signature(method)!=contract.Signature || image.BodyHash(method)!=contract.BodyHash)
                throw new InvalidDataException("The game changed "+contract.Name+" beyond an address update. A mod repair is required.");
            if(++done%20==0) progress?.Report($"Checking mod compatibility: {done}/{Methods.Count} native methods");
        }
        var result=new NativeCompatibilityProfile { BaselineId=Id,Build=build,GameHash=image.GameHash,MetadataHash=image.MetadataHash,
            SurfaceRva=checked((int)image.Resolve("Arena.IceSetup$$SwitchIceType").Rva),
            FlowRva=checked((int)image.Instructions(image.Resolve(FlowMethod).Rva)[FlowIndex].IP) };
        foreach(var c in Sites)
        {
            var method=image.Resolve(c.Site.Method); var body=image.Instructions(method.Rva); var ins=body[c.InstructionIndex];
            if(ins.Length!=5 || ins.FlowControl is not (FlowControl.Call or FlowControl.UnconditionalBranch)) throw new InvalidDataException("Native comparison ABI changed.");
            string targetName=c.Site.Kind switch {"query"=>"Tape2Tape.CampaignUtil$$IsPlayingSquad","eq"=>"System.String$$op_Equality","neq"=>"System.String$$op_Inequality",_=>throw new InvalidDataException("Unknown comparison kind.")};
            if(ins.NearBranchTarget!=image.Resolve(targetName).Rva) throw new InvalidDataException("Native comparison target changed.");
            string? literal=null;
            foreach(var prev in body.Skip(Math.Max(0,c.InstructionIndex-10)).Take(Math.Min(10,c.InstructionIndex)).Reverse())
                if(prev.Mnemonic==Mnemonic.Mov && prev.Op0Kind==OpKind.Register && prev.Op0Register==Register.RDX && prev.IsIPRelativeMemoryOperand)
                { literal=image.Literal(prev.IPRelativeMemoryAddress); if(literal!=null) break; }
            if(literal!=c.Site.Query) throw new InvalidDataException("Native squad ID argument changed.");
            result.Sites.Add(new(){Feature=c.Site.Feature,Query=c.Site.Query,Kind=c.Site.Kind,Method=c.Site.Method,Rva=checked((int)ins.IP),Target=checked((int)ins.NearBranchTarget),Bytes=Convert.ToHexString(image.Bytes(ins.IP,5))});
        }
        if(Convert.ToHexString(image.Bytes((ulong)result.SurfaceRva,18))!=result.SurfaceBytes || Convert.ToHexString(image.Bytes((ulong)result.FlowRva,7))!=result.FlowBytes)
            throw new InvalidDataException("Native hook entry instructions changed.");
        result.Validate(image.GameHash,Id);return result;
    }
}
