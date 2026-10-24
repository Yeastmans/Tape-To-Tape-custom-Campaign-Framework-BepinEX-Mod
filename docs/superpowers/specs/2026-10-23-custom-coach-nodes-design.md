# Custom Coach nodes

Date: 2026-10-23
Status: Approved for implementation by the user on 2026-10-23. Independent
builder check found nothing important left to clarify for planning.

## Intent

Let campaign creators make a Coach that has its own identity and only grants
relics from a list chosen for that Coach. A creator should be able to reuse the
same design across maps without repeating its setup.

## Confirmed requirements

- Add **Custom** to the node-selection interface.
- Choosing Custom offers **Create new** and **Load saved**.
- Custom nodes have a custom name, artwork, and their own reward pool.
- The first version supports **Coach only**.
- Custom artwork appears **on the world map only** for this first version.
- Saved custom nodes form a reusable library. The selected approach places an
  independent copy into a map rather than linking existing maps to future library edits.

## Approaches

1. Customize every placed node directly. This is simpler but repeats the name,
   image, and reward selection on each map.
2. Save reusable node definitions and place independent copies. This supports
   reuse while keeping previously authored maps stable. The user selected this
   approach and specified the Custom / Create new / Load saved entry point.

Use the existing native Coach behavior for the encounter. Custom is an editor
choice, not a new native node enum. The underlying graph node stays Coach.

## Proposed authoring flow

These details are proposed for approval along with the confirmed requirements.

1. In the map's node picker, select **Custom**. The same option is available when
   changing a placed non-boss node's type.
2. **Create new** opens a Custom Coach editor with a name, image preview/import,
   and searchable relic checklist. The behavior is visibly Coach; other behavior
   types are not offered in this version.
3. **Save and use** validates the draft, saves a reusable library entry, and places
   an independent copy at the selected map position. It uses the existing map
   draft/save workflow, including undo and redo.
4. **Load saved** lists saved Custom Coaches by name with image previews. Choosing
   one places a copy. An empty library offers Create new.
5. Reopening a placed Custom Coach allows editing that placement and **Save as
   new custom node** for reuse. Editing a placement never overwrites its original
   library entry or other placements. Library overwrite/delete management is not
   needed for this first version.
6. Canceling a creation, selection, or edit leaves the map unchanged. Explicit
   Save and use creates the library entry even if the creator subsequently
   discards the enclosing map draft; this is explained beside the save action.

The name is required, trimmed, single-line, and limited to 80 characters. Saved
entries have stable IDs; names are labels, not filenames or identities.

The image is optional. No imported image uses the existing Coach world-map art.
Import accepts a fully decoded PNG, including transparency, with the existing
project limits of 4 MiB and 2048 pixels per side. Preserve aspect ratio and fit
the image into a consistent Coach-sized map footprint. No image generation or
custom animation is part of this feature.

## Relic-pool contract

- The checklist uses the existing relic catalog and compatible authored custom
  relics. Item identity is the existing stable reward ID. Native special relics
  retain the catalog's existing visibility controls; incompatible custom kinds
  and definitions marked as needing redesign are unavailable.
- Each Custom Coach must select at least one valid relic. A deliberately small
  pool of one or two relics is allowed.
- The node's selected list replaces the campaign-wide relic pool **for that
  node's Coach offers only**. It is not an intersection with the campaign pool.
  Relics selected only here are not added to unrelated Coaches or match rewards.
- Every offered relic must be selected in the node's list, including upgrade
  levels. Do not substitute an unchecked upgrade or fill remaining slots with
  unrelated relics. Preserve the native ownership, eligibility, and reward
  application rules within the selected list.
- Retain normal Coach choice/pick counts, rerolls, and applicable bonus-pick
  effects. Offer up to three distinct eligible choices; fewer available choices
  produce fewer cards. Rerolls and subsequent picks stay inside this node's pool.
- If no selected relic is eligible, show a clear no-eligible-relics message and
  allow the player to leave the node. Never introduce an unrelated reward to
  keep the screen populated.
- Ordinary, stat-only, and speedrun Coach selection paths must respect the same
  allow-list. Native mode eligibility still applies, so a stat-only visit can
  have no eligible relics if the author selected no stat relics.
- The editor displays selection counts and explains small/exhaustible pools.
  Reject an empty selection, malformed data, and unavailable selected IDs on
  new saves. Preserve unavailable selections when opening old/imported data so
  the creator can replace them; never silently erase or broaden them.

## Presentation and gameplay

Display the custom name and image in the map editor and world-map node/tooltip.
The node retains Coach traversal, input, visit state, route connections, and
reward application. This version retains the existing Coach encounter screen,
including its artwork, dialogue, and title. Its reward choices use the node pool.

Custom artwork replaces the visible Coach map illustration. It must not replace
the native node controller or break selection highlighting, visited appearance,
camera framing, click targets, or gamepad navigation. Fit the imported image to
the existing presentation bounds; do not mutate shared Coach art or blueprints.

## Persistence and portability

- Store reusable definitions under the existing content root's library.
- Each placed node carries a complete versioned snapshot: definition identity,
  Coach behavior, name, selected relic IDs, image bytes, and any referenced custom
  relic definitions required for independent reuse. Sharing must not require the
  author's original image path or library.
- Extend map serialization with optional metadata keyed by the stable node ID;
  retain compatibility with existing 13-field node records and older maps.
- New maps use the current draft snapshot. Continuing a run resolves custom
  nodes from the existing verified applied-layout receipt, including its image
  and pool, rather than from subsequently edited authoring files.
- Library changes and deletion of the original image cannot alter a placed node
  or an existing run. Save/reopen, undo/redo, campaign copy, export/import, and
  campaign ZIP sharing preserve the snapshot and referenced rewards.
- Custom relic dependencies keep their normal mechanics and stable identities.
  Detect conflicting definitions explicitly at authoring/import boundaries;
  never silently overwrite an unrelated campaign/library reward with the same ID.
- Converting a custom node to a normal node removes its active custom metadata
  within the undoable map edit. Ordinary nodes without metadata retain existing
  behavior.

## Implementation constraints and evidence

Current source has the necessary separation points:

- `CampaignCore/MapLayout.cs`: stable node IDs and optional metadata alongside
  compatible native node records.
- `CampaignCreator/MapEditorView.cs`: add/change node choices, map drafts,
  serialization-based undo/redo, and per-node editing.
- `CampaignCreator/MapNodeArtwork.cs` and `MapGamePreview.cs`: editor map images.
- `CampaignCreator/CustomRewardCatalog.cs`, `RewardCatalog.cs`, and
  `CampaignSharing.cs`: reward identities, snapshots, and portable campaigns.
- `CustomCampaignFramework/CustomMapLayout.cs`: exact applied-layout restoration.
- `CustomCampaignFramework/CustomRewardPools.cs`: explicit allow-list selection
  and dedicated Coach stat/starter paths.
- The current 26150666 native dump includes `CoachMapNode.LaunchEvent`,
  `CoachBlueprint.showEventPanel`, `CoachEvent.Initialize/NextChoices`, and both
  modern and legacy event menu surfaces. The correct initialization/selection
  boundary must be verified before installing a hook; method presence alone does
  not prove call order or that a native caller reaches the managed hook.

Bind each override to the exact authored node and active Coach event. Cover
initialization, rerolls, additional selections, closing, and resume without
leaking pool state to another event. Use a scoped event identity rather than a
global campaign pool replacement. Known custom-node errors must not silently
turn into vanilla rewards. Keep shared native assets unchanged or restore any
temporary mutation reliably.

Follow the project's native compatibility checks for every new hook. Source
builds and managed checks are diagnostics; they do not establish rendered or
native gameplay success. Preserve unrelated source work and existing user content.
Implementation creates a local candidate; installation and publication are
separate from this design's scope.

## Validation and acceptance

Technical checks should cover compatible serialization, independent snapshots,
exact reward allow-lists, depleted pools, custom-relic dependencies, and the
actual runtime call boundaries. Build Studio and runtime against the current
reviewed native inputs and verify applicable compatibility/package checks.

Human validation must exercise:

1. Custom / Create new; author two differently named Coaches with different PNGs
   and disjoint pools. Save, undo/redo, close/reopen, and load a saved Coach on
   another map. Expect independent, preserved definitions.
2. In a fresh custom run, visit both Coaches. Verify their world-map names and
   art, normal mouse/gamepad navigation, and that each offer, reroll, and bonus
   pick stays inside the correct pool. Verify an ordinary Coach and match reward
   elsewhere retain their existing campaign behavior.
3. Exercise one-relic and depleted pools. Expect fewer choices or a clear leave
   action, with no unrelated rewards and no blocked progression.
4. Save/Continue and share/import a campaign with a selected custom relic and
   image, without the original library/image file. Expect the saved node and its
   pool to survive. Changing authoring/library data must not rewrite a running
   map's applied snapshot.
5. Inspect long names, transparent/non-square images, selected/visited map states,
   map zoom/framing, and reopening a normal Coach after a custom one.

Record observed human results separately from builds/checks and leave gameplay
acceptance pending until reported.

## Decisions left to the builder

Choose internal filenames, versioned snapshot encoding, caching/resource
lifetimes, exact UI layout, and tested native patch points within these contracts.
Use the existing C#/Avalonia Studio and Unity IL2CPP mod architecture. Do not add
another encounter type, event-screen reskin, arbitrary scripting, library sync,
or a public release without a further scope decision.
