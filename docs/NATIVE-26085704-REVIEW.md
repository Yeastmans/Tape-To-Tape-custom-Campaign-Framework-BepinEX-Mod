# Tape to Tape build 26085704 compatibility review

Reviewed 20 October 2026 against preserved Windows x64 game inputs. This is a
source/runtime compatibility checkpoint. It does not establish in-game behavior
or human acceptance, and does not install or publish the mod.

- GameAssembly SHA-256: `92E73E6D0BE549DD8C695302B7AAE6A77CBBF4DE27F27D26ED01F4B3DABC5B8D`.
- Metadata SHA-256: `1009F63178E260153CBBF224338BDC9124F9468D0F462F7A58327F638188914A`.
- Runtime contract: `t2t-native-v2-20261020`.
- Baseline: `A78702D600299C60AD35A74D95CC740C6AC560529C3088ABEA508EDDCCB65B4C`.
- Protected native methods: 293; squad sites: 137 (109 direct queries and 28 inline comparisons).
- Ice surface entry: `4175110`; starting-screen flow: `4404340`. Both displaced byte contracts are unchanged.

## Native routing repair

The update preserves the inspected metadata signatures but changes compiler output
broadly. The existing 589-identity comparison reports 583 changed normalized bodies;
it is not an address-only update. Observed compiler changes include expanded runtime
initialization barriers, inline GC write barriers and inlined string comparisons.
The saved body exports and difference classification are review evidence, not a
claim that every instruction in all 583 bodies was manually proven equivalent.

All 28 remaining literal string squad comparisons now use an inline equality
sequence. The verifier accepts only the reviewed `cmp rcx,rdx` (26 sites) or
`cmp rbx,rdx` (2 sites), pointer equality, null and string-length checks, argument
preparation and exact `System.SpanHelpers.SequenceEqual(ref byte, ref byte, UInt64)`
overload. The displaced comparison and equality jump occupy 5 or 9 bytes. The
full comparison block is pinned and checked before runtime patching. The verifier
also checks each owner's aligned fixed stack frame and terminating epilogues.

The callback reports native fallback, custom unequal or custom equal. Fallback
replays the original comparison and equality branch. A custom result resumes after
the native equality call, so the original caller keeps its inversion, branch and
side effects. The stub preserves volatile integer registers and XMM0 through XMM5;
only AL changes for a custom result. Default mode and unknown IDs retain native
execution. The full binary hash, baseline identity and bytes remain mandatory.

The reviewed direct query inventory removes `GoalLineTrigger.VerifyGoalScored`,
`NurseProcedureRoster.CreateForCurrentRun` and
`NormalMatchGameModeController.TryRefereeBreakawayPenaltyShot` local queries.
Their native helper paths remain: `IsGameEndingGoal`, `GetRequiredForwardId` and
`get_CanAwardRefereeBreakawayPenaltyShot`. The new referee coroutine eliminator
query is mapped to the eliminator policy. The new
`BlademasterNodeReward.GenerateReward` tchel query is mapped to `no_ability_offers`:
its true branch skips ability selection and clears the generated reward.
`GolferOutroDirector.Stage` now uses `GetMainAngusId` instead of a local allangus
literal comparison. Removed local sites are not patched. All four former site
owners remain protected native dependencies.

The new baseline additionally protects the exact SpanHelpers overload,
`SkinHelper.OverrideSkinsWithCustomTeamUniform`, the generic `Rogue.Talent.Get`
body used by inlined stat-lock consumers, and all 13 explicit live reward query
scope owners. Ten of those scope owners were new to this baseline; three were
already protected. Pinned reward detours and their MethodInfo allowlists have a
separate review under `reward-review` in the evidence folder.

The native compiler also folds `Relic.get_localizedDescription` with
`get_BaseLocalizedDescription` at `3D5A4D0`. The display adapter selects only
the public `get_localized*` getter once. Its custom-only description prefix is
valid for both aliases, while native reward IDs continue to the original body.
The address audit accepts only this exact two-name alias set, alongside the
previously reviewed upgraded/stat-bonus getter aliases.

## Verification and evidence

Evidence root: `artifacts/game-update-26085704-20261020`.

- `reviewed/reviewed-sites.json` records each mapped site and its native context;
  `reviewed/reviewed-mapping.json` records the exact added/removed query inventory.
- `staged-baseline-final.log` records baseline creation and verification against
  the preserved native input bytes before source promotion.
- `staged-compatibility-checks.log`: 114 checks passed, including every inline
  block, current protected dependencies, profile tampering and altered native
  instruction rejection. The old baseline rejects the real changed game.
- `inline-stub-execution.log`: 375 assertions across 24 Windows x64 execution
  scenarios, including a callback deliberately clobbering volatile registers and
  vectors and a real managed reverse-P/Invoke callback.
- `source-compatibility-promotion.json` records the staged/source SHA-256 equality;
  `source-compatibility-before` preserves the previous three compatibility assets.
- The original `coverage-review/FINAL-RECEIPT.json`, now preserved at
  `artifacts/goalie-recruitment-20261020/compatibility-before-299/compiled-audit/FINAL-RECEIPT.json`, identifies compiled runtime SHA-256
  `D86CEDA306BEA8E564E4AE6ED4D80F1745DFE4321EF089F81A284A3D507A0D10`,
  preserved under `runtime-checkpoint`. Its embedded profile exactly equals the
  staged profile, source profile and baseline profile. The audit passes 330
  managed target references and 534 callback bindings across 234 attributed
  classes, including all 13 live reward scopes. The source audit resolves 522
  references to 460 distinct native targets with no unresolved targets or
  unexpected aliases. All 223 fresh interop output hashes match their manifest;
  the native value-type ABI inventory verifier passes. Any later runtime change
  requires a new compiled audit receipt.

## Goalie recruitment extension

The subsequent single-active-goalie recruitment feature adds six reviewed native
dependencies, bringing the baseline to 299 methods. All four new Harmony target
overloads are protected: `CampaignState.SetCurrentNode(MapNode)` and
`DeserializerV2.DeserializeFromModel(RunData, RunDataModelV2)` were already present;
`GeneralManagerEvent.Initialize()` and `NextChoices(int)` are newly protected.
Native GM initialization resets its choices and calls the virtual choice method;
the latter returns a bool and enforces the offer limit before native skater
selection. The goalie adapter supplies its own bounded choices at that boundary.

The other four additions protect the exact team/goalie serializer overloads and
team/goalie deserializer methods. Native team serialization reads the single
`TeamData.goalie` reference at offset `0x138`; deserialization reconstructs and
assigns that same active slot. Supplemental fields absent from that native model
are restored by the mod after native run deserialization.

`artifacts/goalie-recruitment-20261020/compatibility-staged` contains the six native
body listings, staged baseline/profile, promotion receipt, 122 passing
compatibility checks, and current-installed-game hash comparison. The reviewed
site manifest remains unchanged at 137 sites. The previous 293-method assets and
compiled audit are preserved separately under `compatibility-before-299`.

The final goalie runtime audit is preserved at
`artifacts/goalie-recruitment-20261020/compatibility-audit/FINAL-RECEIPT.json`.
It identifies runtime SHA-256
`7A1785994015F15B0F7C90ECB637A8551F9138367A8253DB4FF8A8E1910E824C`
and baseline ID `6285FD523E1268A704A2C267336617580C89210AC0BFE5339934B1C553AF3EFC`.
All 334 managed target references and 540 callback bindings across 238 patch
classes pass, including the four goalie hooks and 13 live reward scopes. The
source audit resolves 526 references to 460 distinct native targets with no
unresolved target or unexpected alias. The native ABI verifier passes, all 223
fresh interop output hashes match, and the compiled profile exactly equals the
staged/source/baseline profile. Installed game and metadata hashes were rechecked
after this audit and still match the reviewed inputs.

No game launch, install, publication, save edit or authored-content edit is part of
this compatibility work. Native/custom gameplay and visual acceptance remain pending.


## Four-feature integration candidate (not Revision 23)

The isolated candidate under build/FourFeatureCandidate now uses baseline ID
0D16BE041548C631DD4B32BC2C9339604D30BBBED2B89225FDE596323C70F060.
Its 1,448 protected methods retain the earlier protected contracts and add the actual manual hooks, native save/serializer APIs and called semantic dependencies. The comparison manifest remains 137 sites and 109 native queries. The paired profile carries 46 named contracts (jersey 11, checkpoint 8, menu 7, launch 6, completion 14) and 21 exact jersey raw callbacks, including the unnamed two-pointer failure helper at 0x9D38F0.

The final runtime SHA256 is
3E480DC35C81FF4E68640EEEEFBCD0E4D3FAC5DA812FEF4B34924D8A3DB63E77.
Canonical artifacts/followup-four-features-20261020/final-integration holds the exact source/compiled/manual/raw audits, dependency and payload hashes, and FINAL-RECEIPT.json. The 80 manual targets include 34 Play Now registrations beyond the 46 named contracts. Fresh compiled wrappers resolve 2,062 interop/provider callable references with zero issues, including typed Handcuffs, ES3/type APIs, generic promise factories and UniTask.Status. These are structural/static checks; native execution is not claimed.

The goalie startup transaction resolves and checks the complete 35-target lifecycle before mutation, verifies the existing actual custom-retention observer, subscribes both observations, installs 14 completion finalizers with Priority.First, and publishes capability flags only after the installed plan audit succeeds. Rollback disables before unsubscribe/unpatch and includes a partially failing patch call. The Play Now Team.Initialize hook now explicitly selects (TeamData,bool,bool); the fresh compiled audit discovered its previous ambiguous overload selection.

New native captures close the OnDestroy body/cleanup and overtime wrapper/state-machine linkage gaps. JsonUtility's exact injected icall and native Single schema are recorded, but emitted Unity float spelling remains unexecuted. Direct caller evidence does not prove every interface/virtual initialization chronology or nesting. Play Now legacy outer close/discard wiring and retained strong-registry lifetime remain bounded native acceptance concerns; no invented discard or timeout cleanup was introduced. See native-review.md and docs/FOUR-FEATURE-CANDIDATE.md for precise limits. Later goalie Studio re-export still clears headSkin by existing convention.

The exact self-contained Studio publish passes GameArtPackageChecks using its actual publish and runtime seam paths. No install, game, real save, push or release occurred. Independent integration and whole-branch review, followed by human/native acceptance, remain pending. Earlier receipts above retain their historical payload identities and are not reused as proof for this candidate.
