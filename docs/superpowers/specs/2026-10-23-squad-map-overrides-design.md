# Player squad map overrides

Date: 2026-10-23
Status: Approved for implementation by the user on 2026-10-23. Independent builder check: nothing important left.

## Intent

Let a campaign author customize maps for an individual playable squad without
duplicating the campaign or losing campaign updates on maps they have not edited.
This extends the existing campaign map editor and player-team editor.

## Confirmed requirements

- Add an own-maps toggle to player squads inside a campaign.
- When enabled, offer **Edit team maps**.
- Team maps initially match the campaign maps and follow campaign changes until
  edited for that squad.
- **Only the edited map becomes independent.** Other maps keep following the
  campaign. This was explicitly selected by the user.
- **Keep the campaign map count and order.** Squad editing customizes individual
  maps; squads cannot independently add, remove, or reorder campaign stages.

## Approaches

The conversation considered detaching the whole map set after the first edit
versus detaching each edited map. The user selected per-map independence, then
selected a shared campaign sequence rather than an independent squad sequence.

Implement that as inherited maps with optional full-map overrides. An untouched
slot resolves its current campaign map; an edited slot resolves its saved squad
copy. The editor presents a detached, editable copy in both cases. Storing only
overrides follows the existing per-map draft model and avoids rewriting copied
files whenever a shared campaign map changes.

A permanently copied map set would violate inheritance for untouched maps.
Automatically merging individual node changes into an edited map would go beyond
the selected whole-map independence. Neither is part of this version.

## Proposed user flow

These details complete the confirmed requirements and are proposed for approval.

1. In the campaign's player-team editor, a Maps area has **Use own maps**, off by
   default for existing and new squads. When off, show that the squad uses campaign
   maps. When on, expose **Edit team maps**.
2. Use the existing map editor with a clear squad name in its title. Show every
   campaign map, in campaign order. Label maps **Following campaign** or **Custom
   for this squad**. Keep campaign opponents, custom Coaches, artwork, node settings,
   map names, and other existing per-map editing features available.
3. Squad mode has no Add map, Remove map, reorder, or independent act-sequence
   controls. Per-map appearance, content, and generation choices remain editable;
   they do not change the shared campaign sequence.
4. Merely enabling the toggle, opening the editor, navigating tabs, or saving an
   unchanged view does not detach any map. Automatic preview normalization is not
   a user edit. The first actual edit creates a draft override for that map only.
5. Save team maps persists only changed overrides and explicit resets. It never
   rewrites shared campaign layouts, assignments, or campaign settings. Cancellation
   and the existing unsaved-change guard preserve the last saved state. Undoing all
   edits to a previously inherited map before save restores inheritance.
6. **Reset this map to campaign** restores the current campaign version and removes
   that map's override on save. This is undoable before saving. A previously saved
   custom map remains custom until reset, even if its contents happen to match the
   campaign again.
7. Turning **Use own maps** off returns the squad to campaign maps while retaining
   its saved overrides. Turning it on again restores those overrides. Match the
   existing squad pool toggle's preservation behavior. Save the toggle with the
   player team; opening map editing must not silently discard unsaved team edits.
   If a team save is needed before opening the map window, use the existing save
   action and proceed only when that save succeeds.

## Inheritance and campaign sequence changes

- A saved squad override is a complete snapshot of one map. Later campaign edits
  cannot overwrite its layout, names, art, assignments, or custom Coach snapshots.
- An inherited map always resolves the shared campaign map for its corresponding
  slot. Where the campaign has no authored layout, retain the existing native map
  generation/template behavior; turning the toggle on must not freeze a random
  native map into a fixed preview layout.
- Campaign map additions create inherited slots for all squads. Campaign removals
  and renumbering keep overrides attached to their corresponding surviving maps,
  using the same original-to-new stage mapping as campaign assignment saves. Keep
  recoverable backups of removed overrides. A squad editor never changes this
  mapping itself.
- Detect campaign map/sequence changes made while a squad map editor is open.
  Do not save stale inherited drafts or attach an override to a different map.
  Preserve the user's draft and explain that the campaign changed before allowing
  a stale save. The exact revision mechanism is a builder choice.
- Old campaigns without the toggle or overrides retain their existing behavior.

## Runtime behavior and Continue

- Resolve the selected player squad by its stable team identity, never its display
  name. For each newly generated stage, use its override only when the toggle is
  enabled and an override exists for that stage. Otherwise use the same campaign
  or native source that would have applied without this feature.
- An invalid existing override is an error, not permission to silently substitute
  a different authored map. Follow the project's existing safe native-generation
  failure handling and report the rejected source clearly.
- The campaign's act sequence, total stage count, completion logic, general rules,
  and shared content remain campaign-wide.
- Continue restores the actual applied map from the existing verified native-save
  receipt, including custom Coaches and their retained reward definitions. Changing
  a toggle or authoring file must not rewrite a map already in a saved run.
- New stages resolve current saved settings from disk, including the squad toggle;
  the already-generated current map is
  governed by its applied receipt. Include the selected squad/source identity in
  new receipt metadata for diagnosis and validation without invalidating older
  legitimate campaign-only receipts.
- Default/base-game mode and squads without enabled overrides retain existing
  behavior. No global map assets may be mutated for another squad.

## Saving, validation, and sharing

- Keep overrides in the owning campaign squad directory and the flag in its team
  document. Exact names and serialization details are left to the builder.
- Save only deliberately edited maps. Existing squad files and disabled overrides
  must survive unrelated team or campaign saves. Backups cover any overwrite or
  structural remapping.
- Use the same map validation rules as campaign maps, including source availability,
  node connections, opponents, goalie recruitment, artwork, custom Coach dependencies,
  and supported native settings. Validate against the owning campaign's content.
- Reward budgets and other map-dependent validation must use each squad's effective
  maps rather than treating one squad's overrides as the shared campaign graph.
  A campaign draft must overlay its shared changes with existing squad overrides
  correctly when checking all selectable squads.
- Campaign copying and campaign ZIP export/import preserve the toggle, active and
  disabled overrides, embedded custom Coach art/pools, and custom reward dependencies.
  Dependency collision checks must include squad override maps before library or
  campaign precedence can hide a conflicting identity.
- Standalone team/library export or copy into a different campaign must not silently
  transfer overrides tied to another campaign's slots. Treat overrides as campaign
  content: preserve them with a whole-campaign copy/share, but omit the own-maps flag
  and override files from a standalone team export/import. Existing unrelated team
  content remains portable.

## Existing code and likely touch points

- `CampaignCreator/TeamEditorView.cs` already hosts squad-specific settings and
  `SquadPoolsEditor`; that toggle preserves choices when disabled.
- `CampaignCreator/MapEditorView.cs` stores per-map drafts, undo stacks, inherited
  flags, original stage indices, and shared sequence editing. Its save currently
  writes every map and campaign.txt; squad mode needs a distinct save destination
  and an explicit record of genuine overrides.
- `CampaignCore/MapLayout.cs` preserves the full map and custom Coach snapshots.
- `CampaignCreator/CampaignRewardBudget.cs` and `OpponentAssignments` resolve maps
  for validation; the current budget cache groups by template and must distinguish
  squads with different effective graphs.
- `CustomCampaignFramework/SquadPoolRuntime.cs` demonstrates stable selected-squad
  resolution. `CustomMapLayout.cs` chooses and applies a campaign map; its receipt
  code and Coach reward hydration already restore saved snapshots before lookup.
- `CustomCoachCatalog.Saved`, `CustomRewardCatalog.PortableItems`, and campaign
  sharing must include saved squad maps, including disabled retained overrides.

The canonical workspace is not a Git repository. Preserve this approved design
with the feature's isolated implementation branch rather than touching the
unrelated publishing mirror. Guard source promotion against concurrent changes.

## Scope and constraints

This is an editor, persistence, and runtime feature in the current C#/Avalonia and
Unity IL2CPP project. Reuse existing map editing and native generation boundaries.
Any new native hook must satisfy the pinned compatibility process.

Do not add independent squad campaign lengths, independent campaign rules, automatic
node-level merges, cross-campaign override synchronization, or a public release.
Do not install into the live game or modify existing user campaigns or save files.
Preserve the custom Coach feature just completed and all unrelated work.

Implementation produces reviewed source changes and a local candidate package.
Human gameplay acceptance remains separate and pending until observed.

## Validation and acceptance

Technical checks should reproduce the previous lack of squad overrides, then cover
real editor save/load/undo paths, per-map inheritance, untouched-save behavior,
toggle persistence, resets, stage remapping, stale editors, custom Coach dependencies,
campaign copy/sharing, and the selected squad's runtime map source. Build against
the actual installed interop and verify the candidate's bundled runtime identity.

Human validation:

1. Make a disposable campaign with three visibly different maps and squads A/B.
   Enable A's own maps and open Edit team maps. Expect all three maps to follow the
   campaign. Open/save without editing; they must continue following.
2. Change only A's Map 2, save, close, and reopen. Change campaign Maps 1/2/3.
   Expect A's Maps 1/3 to update, A's Map 2 to retain its edit, and B to follow all
   three campaign maps.
3. Undo an unsaved first edit, reset a saved override, and turn the toggle off/on.
   Expect the specified inheritance and preservation behavior. Verify no shared
   campaign file changes while saving A's maps.
4. Add/remove a campaign map and check surviving overrides follow their original
   map after renumbering. An open stale editor must not overwrite or misapply them.
5. Start fresh runs with A and B and compare the actual maps, opponents, and custom
   Coaches. Save/Continue A, edit authoring/toggle settings, and verify the already
   applied map and saved relic definitions remain unchanged.
6. Share/import the whole campaign into a fresh content root. Expect identical
   squad inheritance, overrides, names/art, and custom Coach pools without the
   author's original library or image files.

Record observed behavior separately from source/build diagnostics. Leave gameplay
acceptance pending until the user reports results.
