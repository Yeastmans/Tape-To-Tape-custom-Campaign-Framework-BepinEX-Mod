# Beta 1.0 revision 26 validation

Revision 26 adds custom Coach nodes and squad map overrides for Tape to Tape public v1.0, Steam build **26150666**. Human gameplay acceptance for these additions is **pending**.

## Technical evidence

- The released product source matches the final reviewed custom-Coach and squad-map implementation.
- The implementation's 13 focused suites passed **2,258 checks**, with zero failures. Those results are reused because the tested product code is unchanged. Coverage includes editor saves, inheritance, reset/undo, recovery, campaign portability, strict Coach pools, retained reward definitions, goalie compatibility and native compatibility contracts.
- The release has a fresh runtime compilation, self-contained Windows Studio build and Inno Setup installer build. The runtime retains one existing unused-variable compiler warning.
- All 231 pinned dependency files, approved artwork inputs, six map seams, the compatibility baseline and runtime checksum are checked. The installer is extracted and both the Studio and game destinations are compared with the build outputs before publication.
- The published installer, update manifest and checksums are downloaded again and compared with the exact uploaded files. The release description records the source commit and build number.

These checks do not establish native gameplay, actual installer migration, world-map rendering or human Save/Continue acceptance.

## Setup

Download the revision 26 Windows installer from its GitHub release. Save your edits and close Studio and the game before installing. Use a disposable campaign copy with at least three maps, two player squads A/B, ordinary Coaches and two custom Coaches. Keep valued active saves separate from this acceptance run.

## Custom Coaches

1. In the map editor, select a node and choose **Custom → Create new**. Give it a name, a transparent non-square PNG and a pool containing one ordinary relic. Save and use it. Expect a reusable library entry and the named placement. Empty names/pools and invalid images must be rejected without losing the previous valid settings.
2. Place **Custom → Load saved** on another node. Change only that placement's name, artwork and relic selection. The first placement and saved library entry must remain unchanged. Cancel, Undo, Redo, save/reopen and conversion back to an ordinary Coach must preserve the expected independent state.
3. Start the disposable campaign. Check the name and artwork on the world map, including mouse/controller selection and visited states. Coach encounter art/dialogue remain native. Every offered relic, including upgrades and rerolls, must belong to that node's exact selected pool. An ordinary Coach must retain ordinary behavior.
4. Arrive already owning every eligible relic in a one-relic pool. Expect an explanation, no unrelated replacement relic and a working leave action. Continue along the map afterward. Repeat with stat-only eligibility or speedrun rules if used by the campaign.
5. Save a run containing a custom Coach and a node-only custom relic. Change the authored node and relic mechanics in Studio, restart and Continue. The existing run must keep its applied map and retained reward definitions; a new run must use the newly saved authoring. Check a later act too.

## Squad maps

1. Open squad A → **Maps**, enable **Use own maps**, then **Edit team maps**. All three maps must say **Following campaign** and share the campaign count/order. Open each map and save without editing: none should become custom.
2. Edit only A's Map 2, including a distinct custom Coach, then save. Change all three campaign maps. A's Maps 1/3 must follow the changes; A's Map 2 must keep its own version. Squad B must still use campaign maps.
3. Undo the first edit to an inherited map: inheritance must return. Redo must make it custom again. Use **Reset this map to campaign**, undo/redo the reset and save. Subsequent campaign edits must reach the reset map.
4. Disable **Use own maps** and save. New stages/runs must use campaign maps while the saved override is retained. Re-enable it and confirm the override returns. Continue must preserve an already-generated map even if its authoring or toggle changed.
5. Remove campaign Map 1 while A has a Map 2 override. The former Map 2 and its override must move together to slot 1. Added maps must follow the campaign. Squad editors must not offer independent add/remove/reorder controls.
6. Remove an opponent referenced by A's and B's overrides. Repair A through **Edit team maps** while B remains invalid. A's repair must save independently. Repeat while A is disabled using **Edit saved team maps**; activation before repair must report the invalid assignment.
7. Keep A's draft open while another editor changes the campaign map structure. A's stale save must be rejected with its draft retained. With campaign player teams disabled, retained overrides must not block unrelated campaign settings; re-enabling must validate them again.
8. Export/import the whole campaign with a disabled override and node-only custom relic. Re-enable it in a fresh content folder: its maps, name/art and reward definitions must survive without the original library. A standalone team copy into another campaign must use the destination campaign's maps instead.

Record the release build number, campaign/squad, input method, action and observed result. Report unexpected rewards, blocked exits, lost edits, incorrect inheritance or changed Continue behavior. Human acceptance remains pending until those results are supplied.

The earlier [custom Coach guide](CUSTOM-COACH-VALIDATION.md) and [squad-map implementation handoff](superpowers/2026-10-23-squad-map-overrides-handoff.md) preserve detailed local-candidate history. Use this document and the published revision 26 installer for release acceptance.
