# Two-line Play Now, jersey logos, and campaign goalies

2026-10-20. Revised design approved by the user after confirming campaign-level
recruitment and per-playable-squad reserve settings, both off by default.
Implementation is integrated in the local four-feature candidate; independent integration/whole-branch review and human/native validation remain. Revision 23 is already published independently;
none of these additions is included in it.

## Confirmed requirements

- Export two complete lines to Play Now, allow native line switching, carry the
  team's stamina setting, and persist in-game lineup edits across restarts.
- Allow different home and away jersey logos. Copy to Home/Away includes the full
  selected kit, including skater and goalie settings; existing Save keeps it.
- A campaign Free Agent node defaults to Skaters and can select Goalies. Studio
  has a goalie pool section. These recruitment controls shipped in revision 23.
- Add a reserve goalie and between-match swaps for campaign runs only.
- Goalie recruitment is a campaign-level toggle, so the shared map has one
  consistent recruitment policy for every playable squad. The reserve goalie
  slot has a separate per-playable-squad toggle. Recruitment can remain enabled with the
  reserve disabled; in that combination recruiting replaces the active goalie.
- Both toggles default off and must be explicitly enabled. Show and allow
  the node's Goalies option only when the required recruitment feature is on;
  provide a reminder tooltip explaining that prerequisite.
- Add optional per-player goalie contracts, default four played matches, with
  existing departure-prevention effects respected. Bench matches do not count.
- On active-goalie expiry, promote the bench goalie. If the bench is empty, keep
  the expired goalie until a replacement is available.
- Preserve independent player appearance, campaign-specific opponent identity,
  authored content, existing saves and unrelated unfinished source work.

## Approved behavior

1. One active goalie and, when the playable squad's reserve toggle is enabled, one reserve
   slot. A new campaign starts with its existing authored starter and no reserve
   occupant. When recruitment is enabled, it offers the active slot and also the
   reserve slot if that separate toggle is enabled, clearly naming any goalie
   being replaced. Canceling
   leaves both slots and the recruitment choice untouched. Replacing a filled
   slot removes that slot's previous occupant; it does not silently reshuffle
   another slot. Swapping is available between matches, never during a match.
2. Home/Away logo selectors belong to the team Look tab and apply to the selected
   kit's skater and goalie jerseys. Each defaults to Use team logo, can select an
   image, or select No jersey logo. The existing Team logo remains the identity
   used for map/menu icons. Existing independent personal logo overrides retain
   their behavior; this does not add separate personal home/away pickers.
3. Copy to Home/Away copies the jersey-logo choice along with the kit. An inherited
   choice remains inherited. Fill look retains its existing promise to preserve
   logos. Saving a default team look includes the two kit-logo choices.
4. Saving lineup edits in game updates the exported team. Explicitly updating that
   same export from Studio replaces it with Studio's authored lineup; the update
   confirmation must state this when an in-game lineup differs. In-game edits do
   not silently rewrite the original Studio campaign or library source.

## Architecture and alternatives

Use additive mod-owned records around the game's existing roster and rendering
flows. The alternative of placing a goalie in the native skater bench is invalid:
the bench is a list of ForwardData. Replacing native save formats globally would
broaden compatibility risk and affect unrelated content. The selected approach
keeps valid native first-line and active-goalie records while adding narrowly
scoped supplemental data.

Implement in three independently verifiable increments: Play Now roster
persistence, jersey-logo selection/rendering, then campaign reserve/contract
lifecycle. Goalie contracts depend on safe reserve persistence; jersey logos do
not depend on either roster subsystem. Integrate and audit the final runtime only
after each increment passes its focused checks.

## Play Now roster

- Extend the existing exported-team sidecar with an explicit roster version and
  ordered identities for two five-skater lines plus one active goalie. Native
  team JSON retains its supported six starting-position fields as a fallback.
  Export normal native player records and existing appearance/reward sidecars for
  all eleven players. A team with two-line behavior disabled stays single-line.
- Validate every slot, player role, unique identity and referenced file before
  writing or applying the extension. Reject incomplete enabled second lines.
  An unreadable or unknown extension leaves the valid native first line intact
  and produces an actionable diagnostic; it must not partially mutate a team.
- Hydrate only eligible exported teams after native repository loading completes.
  Preserve native clone-generated match identities and remap both lines through
  the native clone flow. Never write persisted IDs over match-instance IDs.
- Scope multi-line roster editing and stamina policy to the actual exported team
  being edited or initialized. Both sides of Play Now must work independently.
  Do not enable campaign-only drafting rules or change unrelated vanilla teams.
- Reuse native line switching, faceoff application and stamina presentation.
  Stamina follows the exported team's authored setting; disabled stamina still
  permits manual switching. In-game Save writes both native starting positions
  and the full supplemental lineup as one recoverable operation.
- Export updates account for references from both lines of every affected team.
  Preserve shared player records and standalone exports. Detect intervening
  native edits before reuse/update; roll back an incomplete write. Reimport reads
  the current saved lineup rather than blindly restoring a stale Studio snapshot.

## Jersey artwork

- Keep Logo From and its embedded image as team identity and legacy fallback.
  Add optional home and away jersey selections with independently named embedded
  payload metadata. Missing fields preserve existing campaigns unchanged.
- Resolve inherit, explicit image and explicit no-logo distinctly. An omitted
  away choice inherits identity, not the home override. Existing own-player
  shared logo overrides take precedence; players using team appearance receive
  the appropriate kit selection.
- Resolve compatibility per kit and per role. A premade away jersey must not
  clear the home choice, and an incompatible goalie jersey must not clear a
  compatible skater's artwork. No-logo must render a genuinely blank crest.
- Preview the effective selection. Copy selection and embedded bytes together;
  changing one slot clears only that slot's stale metadata. Keep campaign-first
  artwork resolution and content-addressed exported image caching.
- Share/import/export both images portably, without rewriting authored sources
  or relying on another machine's absolute paths. Check unavailable external
  files independently; corrupt embedded data remains a validation error.
- Apply the selected texture on both forward and goalie world-render paths as
  well as previews. Inspect the flags-based async renderer before selecting a
  hook; existing player-aware hooks alone are not evidence of match coverage.

## Reserve goalie and contracts

- Store Goalie recruitment at campaign level and Reserve goalie slot separately
  on each playable squad. The existing node Skaters/Goalies selector remains distinct
  from these feature settings. A disabled reserve is not an available recruitment
  destination and does not expose a swap action. Enabling a reserve slot does not
  independently enable goalie recruitment.
- Both toggles default off. The node inspector must not offer or permit selecting
  Goalies while the prerequisite is disabled. Keep a reminder tooltip available
  on the recruitment control even when the Goalies option is hidden, explaining
  that Goalie recruitment must be enabled in campaign settings.
- The campaign-level recruitment toggle supersedes the proposed requirement for
  every playable squad to enable recruitment separately. Do not silently convert
  existing authored Goalies nodes when changing the campaign setting. Validation
  must flag a Goalies node whose campaign prerequisite is disabled and identify
  the node and required setting rather than silently selecting replacement offers.
- Keep the native TeamData.goalie as active. Store the reserve separately, scoped
  to the exact campaign and run. Preserve native goalie fields, earned stats,
  ability/talents, identity, appearance bindings and supplemental fields. Do not
  reconstruct a used reserve from its original authored template.
- When the reserve setting is enabled, show active/reserve identity and a swap
  action in the between-match roster flow, with mouse and controller navigation.
  Recruitment uses the enabled slots and prevents duplicate ownership of a
  candidate. A stale/double click or
  canceled selection cannot consume the offer or overwrite a later roster.
- Persist the pair and contract counters with native save checkpoints. Keep a
  recoverable previous record so interruption between native and companion writes
  cannot duplicate, lose, or replace the wrong goalie on Continue. Missing or
  corrupt supplemental data retains the valid native active goalie and reports
  the reserve problem instead of guessing from another run.
- The optional contract controls also work for authored starting goalies in new
  runs. A disabled custom contract preserves the existing goalie behavior of
  staying until replaced. Signed duration/progress remains fixed across Continue
  and later edits to the authored template; invalid durations never mean zero.
- Snapshot actual goalie participation and debit once per completed match.
  Reloading or duplicate completion callbacks cannot debit twice. Bench time
  does not debit. Respect the native departure-prevention decision at the same
  campaign boundary, without inserting goalies into forward-only contract lists.
- When departure is allowed, an expired active goalie leaves and a valid reserve
  becomes active. If no replacement exists, retain the goalie with an expired,
  awaiting-replacement status and do not reset its duration. Signing a replacement
  allows the pending departure; show that result before confirming the signing.
  An expired reserve can leave without disturbing the active goalie. A swap must
  not renew or reset either player's contract.
- Existing revision-23 runs without reserve/contract data retain their active
  goalie and begin with an empty reserve. No retrospective contract is inferred
  merely because an authored template changed.

## Compatibility and verification

Before using a new native entry point, inspect its build-26085704 body, signature,
caller ordering and required fields; add it to the protected compatibility audit.
Do not bypass the compatibility gate or infer behavior from a successful build.

Focused automated checks must cover:

- Two-line export/update/import, stable and shared IDs, complete role validation,
  save/reload, native clone remapping, independent home/opponent stamina, and
  rollback after injected write failures.
- Home/away previews and copy/save, old documents, own-player precedence, mixed
  compatible jerseys, no-logo rendering input, portable images, and independent
  missing/corrupt image handling.
- Goalie slot selection/cancel/swap, full-state save/Continue, campaign/run
  isolation, interrupted writes, old saves, duplicate callbacks, exact contract
  participation, departure prevention, expiry promotion and empty-bench deferral.
- All four combinations of recruitment/reserve settings, campaign recruitment
  isolation, reserve-slot isolation between playable squads, hidden/unselectable
  Goalies options when disabled, reminder tooltips, and consistency of existing
  nodes with campaign recruitment settings.
- Existing single-line exports, skater recruitment, native squads and map logos
  remain covered by directly affected regression checks.

Build the Studio/runtime, verify the packaged runtime/profile identity, and review
the final native hook audit. Actual match switching/stamina, world jersey artwork,
controller roster interactions and native Save/Continue require gameplay evidence;
label these pending until observed. Do not install or modify real user saves as
part of preparing the implementation.

## Evidence and release boundary

Native/source investigations:

- artifacts/followup-four-features-20261020/two-line-design.md
- artifacts/followup-four-features-20261020/split-logo-design.md
- docs/GOALIE-BENCH-FEASIBILITY-2026-10-20.md
- docs/GOALIE-CONTRACT-FEASIBILITY-2026-10-20.md

Published revision 23 receipt:
artifacts/release-r23-26085704-20261020/public-receipt.json.
Commit 29434df1220ed998de372e90c5464574f5f93023. This design has not changed
the published runtime or the six unfinished files in the original Git mirror.
