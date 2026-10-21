# Four-feature candidate guide

This is a local review candidate for game build 26085704. Revision 23 remains the published release and does not include these additions. Candidate runtime and self-contained Studio are under build/FourFeatureCandidate. Independent integration and whole-branch review remain pending.

## Authoring and use

- Play Now export preserves two complete five-skater lines and one goalie, plus the authored stamina choice. In-game roster edits and player edits use a paired native/companion journal. Reload/restart recovery preserves both lines and shared player ownership; incomplete or stale records reject a save. Native fallback contains Line 1. Use explicit Save to commit. Modern No discards; legacy picker closure is not treated as a proven discard.
- Home and away jerseys have independent team logo choices. Blank inherits the team identity logo; none removes that kit's crest. Existing personal player artwork keeps precedence. Copy kit includes its logo, while Fill look preserves logos. Compatible and premade kits resolve independently. Missing/unbound artwork retains native behavior.
- Enable Goalie recruitment in campaign settings to author goalie GM nodes. Enable Reserve goalie slot separately for each playable squad when wanted. Both default off. Recruitment can replace Active with reserve disabled. Use campaign lineup controls between matches to swap the active/reserve full records; no Play Now reserve or in-match swap is added.
- Goalies can enable Custom contract duration; a new enabled duration defaults to four played matches. Signed duration/progress survives swaps and Continue. Only a proven playing home goalie on a completed owned attempt counts. A protected departure retains progress. Expiry promotes an eligible reserve or leaves the expired active awaiting replacement. See PLAYER-CONTRACTS.md for the precise behavior and recovery messages.

## Pending native and human acceptance

These checks are not performed by this integration:

1. On the exact subsequently authorized build, export two lines, use both sides and the same team on both sides, switch lines, check stamina/HUD, edit roster/player fields, explicitly save/cancel, restart and reimport. Confirm native float serialization matches the schema-aware comparison. A later Studio goalie export still intentionally writes empty headSkin; native saved head/reimport Face retention does not prove that later roundtrip.
2. Verify both kits on forward and goalie world/menu/bench/penalty/knockout paths, mixed premade kits, independent personal logos, explicit none and same-named artwork across campaigns. Actual raw detour trampoline/unwind and native async ownership remain unexecuted.
3. Verify fresh run, recruitment into either slot, repeated controller selection, swap/cancel, native graph/task/cache save success/failure, restart and Continue. Validate local commit versus native/platform failure messages on the exact payload.
4. Exercise normal match, shootout, rematch and overtime; cancel while arm save is pending, reopen/change destination or goalie, and recover an Armed attempt after process loss. Confirm bench-only reserve never debits, departure protection retains progress, unprotected expiry promotes correctly, and boss bonuses remain with the participant before promotion.
5. Verify actual Harmony ordering/injection and partial-install rollback. The source-linked coordinator test uses boundary doubles; no installed-success receipt exists. Unsupported direct or foreign launch paths remain unowned and cannot authorize a debit.

Legacy outer serialized close/discard wiring is still unproven. The native draft survives disable/re-enable and picker close, so no invented reset was added. Play Now's strong player/editor/raw-model registries remain process-lifetime caches: they preserve stale-revision evidence and avoid native pointer reuse being mistaken for ownership. No verified destruction/repository eviction callback justifies dropping them here; long-session memory retention remains a bounded concern for native validation.

## Evidence

Canonical artifacts/followup-four-features-20261020/final-integration contains the paired staged/verified baseline, compiled/source/manual/raw audits, native captures, focused test logs, source/payload manifests and FINAL-RECEIPT.json. It records hashes of the exact runtime and self-contained publish. Builds and managed tests establish diagnostics only; gameplay remains pending.
