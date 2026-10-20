# Home/Away jersey-logo implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give each team independent home/away jersey artwork that survives preview, kit copy, sharing, export and gameplay.

**Architecture:** Keep the existing identity logo and add two optional jersey selections. A shared resolver distinguishes inherit, image and no-logo; adapters apply the selected kit without clearing the other kit or personal overrides.

**Tech Stack:** CampaignCore netstandard2.1, Avalonia Studio, Unity/Spine IL2CPP renderer and Harmony.

**Spec:** [Approved design](../../FOUR-FEATURE-DESIGN-2026-10-20.md), Jersey artwork section.

## Global constraints

The [umbrella plan](2026-10-20-four-features.md) constraints apply. Team identity/map/menu logos remain unchanged. Team selectors apply to skater and goalie jerseys where compatible. Existing independent personal shared logos retain precedence; do not add personal home/away pickers. Missing per-kit fields inherit identity independently. Kit copy includes the choice and image; Fill look preserves logos. Preserve version-3 export ownership semantics.

## Review focus

- An incompatible away or goalie jersey must not erase another compatible kit's logo.
- Explicit no-logo must not expose an atlas texture because the renderer skipped a null argument.
- Two same-named campaign PNGs must never share an incorrect cached texture.
- Copying an inherited selection must remove stale destination embedded metadata.
- A native flags-only async world renderer may bypass the existing player-aware logo hook.

## Task 1: Per-kit contract and export payloads

**Files:** create `src/CampaignCore/JerseyLogoOptions.cs`; modify `AppearanceContract.cs`, `AppearanceFields.cs`, `src/CampaignCreator/LogoExport.cs`, `ExportSelections.cs`, `PlayerExport.cs`, `ExportTranslation.cs`; create `tests/JerseyLogoChecks/{JerseyLogoChecks.csproj,Program.cs}` referencing Core/Creator.

**Interfaces:** `enum JerseyLogoMode { Inherit, Image, None }`; `JerseyLogoChoice` exposes `Mode`, `Source` and `MetadataPrefix`. `JerseyLogoOptions.Read(CampaignDocument document, bool away)` reads authored choice; `Resolve(CampaignDocument player, CampaignDocument team, bool away)` returns effective choice with existing personal precedence. `Copy(CampaignDocument source, bool sourceAway, CampaignDocument target, bool targetAway)` replaces only one kit selection and its payload. `SupportsJerseyLogo(string role, string skin)` is a new single-kit overload in `AppearanceContract`; retain existing overloads for old callers while migrating affected paths.

Fields: home `Jersey Logo From`, away `Jersey Logo Away From`; absent/blank means inherit, `none` means no crest. Payload prefixes: `Export Jersey Logo` and `Export Jersey Logo Away`, each followed by `Mode`, `ID`, `SHA256`, `PNG`. Retain legacy `Export Logo` identity fields. Extend `LogoExport.Snapshot`/`Copy` with slot-specific overloads; existing calls retain identity behavior. Reuse the existing PNG limits: 16 MiB, dimensions 1..8192, hash/ID verification.

- [ ] Add failing checks `MissingKitsInheritIdentityIndependently`, `PersonalLogoWins`, `CompatibleHomeSurvivesPremadeAway`, `SkaterCompatibilityDoesNotClearGoalieChoice`, `CopyInheritedClearsTargetPayload`, `CorruptAwayDoesNotMasqueradeAsHome`. Assert identity document fields unchanged after every kit operation.
- [ ] Run `dotnet run --project tests/JerseyLogoChecks -c Release`; confirm expected missing-contract failures.
- [ ] Implement the shared contract, per-kit compatibility and slot-specific export helpers. Preserve explicit no-logo versus unavailable external art. Resolve and embed both kits for team-dependent exported players without changing `Export Own Equipment`. A missing external image reports the affected slot; corrupt embedded data remains an error before writes.
- [ ] Run new checks, `tests/ExportTeamAppearanceChecks`, and existing campaign-logo resolution checks. Require all assertions passing; compare legacy no-new-field exports.
- [ ] Review/commit this shared contract and export unit.

## Task 2: Studio controls, copy/default look and portable sharing

**Files:** modify `src/CampaignCreator/TeamEditorView.cs`, `TeamEditorLogos.cs`, `TeamLookCopy.cs`, `DefaultPlayerLook.cs`, `CampaignSharing.cs`, `UpgradeCleanup.cs`; inspect and verify the existing consumers in `TeamEditorFillLook.cs` and `GameCustomContent.cs`, changing them only if the required behavior is missing. Add `TeamEditorJerseyLogos.cs` for slot-specific UI if needed to keep the existing partial focused. Extend `tests/TeamLookFillChecks/Program.cs`, `tests/CampaignStudioChecks/LogoPickerChecks.cs`, `PlayerLogoChecks.cs`, `ImportLibraryChecks.cs`; extend the Task 1 fixture for portable sharing.

**Interfaces:** consume Task 1 resolver/copy helpers. Add `TeamEditorView.JerseyLogoPicker(bool away)` returning a control bound to the existing draft readers/register/change callbacks, not direct file writes. `TeamLookCopy.Kit` retains its signature and carries the source kit's selection/payload; existing Fill look key enumeration explicitly excludes all logo slots. Sharing's reference discovery returns both new fields along with legacy identity.

Verify that `TeamEditorFillLook.FillLook` iterates the updated `TeamLookCopy.Keys` and that `GameCustomContent` merges current Play Now values into authored snapshots while retaining both kit fields and independent personal equipment. These consumer files need no edit when the focused checks confirm that behavior.

- [ ] Add failing UI checks for Home/Away picker changes, unsaved copy in both directions, Save/reopen, default-look save/new-team, disabled incompatible preview, and Fill look retaining all three logos. Add portable campaign export/import with identically named PNGs from different folders and independent personal equipment.
- [ ] Run `dotnet run --project tests/CampaignStudioChecks -c Release -- --logo-picker artifacts/followup-four-features-20261020/logo-picker`, then the corresponding `--player-logo` and `--import-library` commands with distinct output folders, plus `dotnet run --project tests/TeamLookFillChecks -c Release`. Confirm failures are new behavior; do not invoke the full Studio suite.
- [ ] Implement the selected-kit picker with `Use team logo`, `No jersey logo`, and the existing image selection flow. Preview both role diagrams using resolved kit/role compatibility. Dispose replaced bitmaps, invalidate only affected previews, and keep identity changes updating inherited kits. Copy inheritance rather than baking its current image.
- [ ] Extend reference discovery, portable payload staging and import/cleanup allowlists. Package/rewrite only staged copies, never source documents; preserve both kits in library/export snapshots. Use content-derived names to avoid collision. Newly created players must not receive team-default personal-logo overrides.
- [ ] Run the focused UI/export/import checks plus `tests/CampaignLogoResolutionChecks`. Require expected preview selection and exact saved fields; no tests may mutate authored user folders. Review/commit this complete Studio/portability unit.

## Task 3: Forward and goalie world rendering

**Files:** create `src/CustomCampaignFramework/JerseyLogoRuntime.cs`; modify `CampaignAppearanceRuntime.cs`, `ExportAppearanceRuntime.cs`, `AppearanceDecalRuntime.cs`, `HeadAppearanceRuntime.cs` only at verified binding points; extend `tests/JerseyLogoRuntimeChecks/{JerseyLogoRuntimeChecks.csproj,Program.cs,NativeDoubles.cs}` and existing logo-resolution tests. New hooks enter final compatibility review.

**Interfaces:** `JerseyLogoRuntime.Resolve(SkaterData player, bool away)` returns `JerseyLogoRenderChoice` with `bool Override` and `Texture Texture`. `Override=false` means leave the native input alone. Explicit no-logo supplies a valid transparent texture to the correct logo attachment rather than null. `CampaignAppearanceRuntime.ForKit` remains the source of kit selection; bindings retain Team/Player/Own documents and carry both payloads through snapshots.

- [ ] Add failing source-linked tests `ForwardHomeAwayTexturesDiffer`, `GoalieHomeAwayTexturesDiffer`, `ExplicitNoneUsesBlankTexture`, `CampaignSameNameIsolation`, `SnapshotRestoresBothKits`, `OwnPlayerUnaffectedByTeamChange`, `UnboundVanillaPlayerPassesThrough`.
- [ ] Run `dotnet run --project tests/JerseyLogoRuntimeChecks -c Release`; verify expected failures.
- [ ] Trace native forward/goalie skin composition, player-aware ApplySkin overloads and flags-based async ApplySkin calls against the pinned build. Save the exact signatures, kit argument direction and player binding lifetime in `artifacts/followup-four-features-20261020/jersey-native-review.md`. Do not implement a global mutable current-team logo or rely on synchronous state spanning an await.
- [ ] Implement kit-specific texture application at every verified preview/world path. Use binding-scoped data or explicit arguments across asynchronous operations; cache by resolved campaign path/content identity. Preserve native identity sprites and explicit skin selections. Track new resources for disposal and clear stale bindings on context changes.
- [ ] Run runtime/source checks, existing logo and independent-equipment regression checks; compile the runtime candidate. Independently review world-call coverage and no-logo semantics, then commit. Pass hook contracts to integration. Actual world images remain a human gameplay check until observed for both roles and kits.
